using Avalonia.Controls;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Bug hunt H28 (High; run 75): a renewal panel left open into free agency, then Make offer, left the player in
/// the free-agent pool and on your roster, and every later save failed ("UNIQUE constraint failed: player.id").
/// Now: the panel reopens on him as a free agent when the summer moves on; a player joins the pool once however often
/// he is released; a signing takes every pool entry; and a save first heals any player in the pool and on a roster.</summary>
[Collection(SharedUiState.Name)]
public class Run75OfferTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_stale_renewal_panel_then_Make_offer_keeps_one_player_in_one_place_and_saves()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run75-offer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(dir, "settings.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var probe = GameSession.NewGame(31, 1, null);
            int id = probe.League.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).First().Id;
            var s = GameSession.NewGame(31, id, path);
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            Assert.Equal(OffseasonPhase.ReSigning, s.Offseason!.Phase);
            Assert.NotEmpty(s.MyExpiring);
            var p = s.MyExpiring[0];
            // The summer's own list names him twice (however that happens, a double release once put him in the pool twice).
            s.Offseason.Expiring[s.HumanTeam.Id].Add(p);

            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { for (int i = 0; i < 4; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            vm.Navigate("Offseason"); Settle();
            var off = (OffseasonViewModel)vm.CurrentPage!;
            var row = off.Expiring.First(r => r.Player == p);
            off.PickExpiring(row); Settle();
            Assert.Equal($"New deal for {p.Name}", off.Talk.Title);
            Assert.True(off.Talk.CanRelease);

            // Continue past the re-signing window without deciding: he goes to the pool, once.
            s.AdvanceOffseason();
            vm.AfterTick(); off.Refresh(); Settle();
            Assert.NotEqual(OffseasonPhase.ReSigning, s.Offseason!.Phase);
            Assert.Single(s.League.FreeAgents, x => x.Id == p.Id);
            Assert.DoesNotContain(p, s.HumanTeam.Roster);

            // The panel follows: he is a free agent now, so "Sign …" and no Release.
            Assert.Equal(p, off.Talk.Player);
            Assert.Equal($"Sign {p.Name}", off.Talk.Title);
            Assert.False(off.Talk.IsRenewal);
            Assert.False(off.Talk.CanRelease);

            // Make offer (well over his ask, with the budget to pay it): he signs, in one place only, and the save works.
            s.HumanTeam.Money.WageBudget += 200_000_000;
            if (s.HumanTeam.Roster.Count >= s.League.Finance.RosterMax) s.ReleaseExpiring(s.HumanTeam.Roster[^1]);
            var ask = s.PeekDemand(p);
            off.Talk.Wage = Math.Round((decimal)(ask.Wage * 1.5 / 1e6), 2);
            off.Talk.Years = ask.Years;
            off.Talk.OfferCommand.Execute(null); Settle();
            if (off.Talk.Signed)
            {
                Assert.Single(s.HumanTeam.Roster, x => x.Id == p.Id);
                Assert.DoesNotContain(s.League.FreeAgents, x => x.Id == p.Id);
            }
            Assert.DoesNotContain(s.League.FreeAgents, x => s.HumanTeam.Roster.Any(r => r.Id == x.Id));
            s.SaveNow();
            var back = GameSession.Load(path);
            Assert.Equal(back.League.FreeAgents.Count, back.League.FreeAgents.Select(x => x.Id).Distinct().Count());
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void A_save_heals_a_player_in_the_pool_and_on_a_roster()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run75-heal-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var probe = GameSession.NewGame(2026, 1, null);
            var s = GameSession.NewGame(2026, probe.League.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var onRoster = s.HumanTeam.Roster[3];
            // A pool player (a new career's pool can be empty: release one from another club).
            var other = s.League.Teams.First(t => t != s.HumanTeam);
            var pooled = other.Roster[^1];
            other.Roster.Remove(pooled); pooled.TeamId = null; pooled.Contract = null;
            s.League.FreeAgents.Add(pooled);
            int pool = s.League.FreeAgents.Count;
            s.League.FreeAgents.Add(onRoster);   // the H28 state
            s.League.FreeAgents.Add(pooled);     // and a pool entry twice
            s.SaveNow();                          // used to throw: UNIQUE constraint failed: player.id
            Assert.Equal(pool, s.League.FreeAgents.Count);
            Assert.Contains(onRoster, s.HumanTeam.Roster);
            Assert.DoesNotContain(onRoster, s.League.FreeAgents);
            var back = GameSession.Load(path);
            Assert.Equal(pool, back.League.FreeAgents.Count);
            Assert.Contains(back.HumanTeam.Roster, x => x.Id == onRoster.Id);
            Assert.Equal(0, s.HealDuplicatePlayers());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
