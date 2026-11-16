using Avalonia.Headless;
using Hardwood.Core.Guide;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 78. H29 (bug hunt): a club outside the NBA had three roster minimums (the sporting director filled to 10,
/// the minimum-deal rules used 11, Offseason said "needs 13-15"); now one number per league, shown everywhere. H30: Roster's
/// Expiring chip counted only Years 0, which in season never happens, so it said 0 all year; deals in their final year count,
/// and their Yrs reads "Final year".</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run78RosterMinimumTests
{
    private static (GameSession s, MainViewModel vm, MainWindow window, Action settle) Open(string path, Func<GameWorld, Hardwood.Core.Model.League, int> pick, ulong seed = 2026)
    {
        var (probe, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed);
        var s = GameSession.NewGame(seed, pick(world, probe), path);
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
        window.Show();
        vm.AttachSession(s);
        void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
        return (s, vm, window, Settle);
    }

    private static void Snap(MainWindow window, Action settle, string name)
    {
        if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is not { Length: > 0 } dir) return;
        settle();
        using var f = window.CaptureRenderedFrame();
        f?.Save(Path.Combine(dir, name));
    }

    private static T WithSettings<T>(Func<T> body)
    {
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        try { return body(); }
        finally { AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings); }
    }

    [Fact]
    public void Outside_the_NBA_there_is_one_roster_minimum()
    {
        Assert.Equal(GameWorld.RosterMinimum, GameWorld.ManagedMinimum);
        Assert.Equal(11, GameWorld.RosterMinimum);
        Assert.Equal(15, GameWorld.RosterMaximum);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_pyramid_club_sees_the_same_minimum_on_Roster_Offseason_and_from_the_assistant_and_opens_the_season_at_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run78a-{Guid.NewGuid():N}.hwsave");
        MainWindow? window = null;
        WithSettings(() =>
        {
            try
            {
                var (s, vm, w, settle) = Open(path, (world, _) => world[UsaPyramid.Tier5].Clubs.OrderBy(t => QuickSim.Strength(t)).First().Id, 9);
                window = w;
                Assert.False(s.League.Teams.Contains(s.HumanTeam));
                Assert.Equal(11, s.MyRosterMin);
                Assert.Equal(15, s.MyRosterMax);

                // Short of the minimum: Roster's Players chip says so with the league's numbers.
                var team = s.HumanTeam;
                var parked = team.Roster.OrderBy(p => p.CurrentAbility).Take(team.Roster.Count - 9).ToList();
                foreach (var p in parked) team.Roster.Remove(p);
                vm.Navigate("Roster"); settle();
                var roster = (RosterViewModel)vm.CurrentPage!;
                roster.Refresh(); settle();
                var players = roster.Chips.First(c => c.Label == "Players");
                Assert.Equal("bad", players.Tone);
                Assert.Contains("11-15", players.Sub);
                // The assistant names the same minimum.
                var shortTask = Assistant.Tasks(s, default, new GuideProgress()).First(t => t.Title.StartsWith("We're short"));
                Assert.Contains("at least 11", shortTask.Detail);
                Snap(window, settle, "run78-roster-short.png");
                foreach (var p in parked) team.Roster.Add(p);

                // The summer: Offseason's header gives the league's range, not the NBA's 13-15.
                int season = s.League.Season;
                int guard = 0;
                while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
                Assert.Equal(SessionPhase.Offseason, s.Phase);
                vm.Navigate("Offseason"); settle();
                var off = (OffseasonViewModel)vm.CurrentPage!;
                off.Refresh(); settle();
                Assert.Contains("needs 11-15", off.CapText);
                Assert.DoesNotContain("13-15", off.CapText);
                Snap(window, settle, "run78-offseason-pyramid.png");

                // Cut the squad to 8 and let the summer run: the sporting director fills to the league minimum (was 10).
                foreach (var p in s.HumanTeam.Roster.Where(p => !s.MyExpiring.Contains(p)).OrderBy(p => p.CurrentAbility).Skip(8).ToList())
                {
                    s.HumanTeam.Roster.Remove(p); p.TeamId = null; p.Contract = null;
                }
                guard = 0;
                while (s.Phase == SessionPhase.Offseason && guard++ < 20) s.AdvanceOffseason();
                Assert.NotEqual(SessionPhase.Offseason, s.Phase);
                Assert.Equal(season + 1, s.League.Season);
                Assert.True(s.HumanTeam.Roster.Count >= s.MyRosterMin, $"{s.HumanTeam.Roster.Count} players, minimum {s.MyRosterMin}");
                return 0;
            }
            finally { window?.Close(); if (File.Exists(path)) File.Delete(path); }
        });
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void In_season_the_Expiring_chip_counts_final_year_deals_and_they_read_Final_year()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run78b-{Guid.NewGuid():N}.hwsave");
        MainWindow? window = null;
        WithSettings(() =>
        {
            try
            {
                var (s, vm, w, settle) = Open(path, (_, lg) => lg.Teams.First(t => t.Abbreviation == "SAC").Id);
                window = w;
                Assert.Equal(13, s.MyRosterMin);
                vm.Navigate("Roster"); settle();
                var roster = (RosterViewModel)vm.CurrentPage!;
                roster.Refresh(); settle();
                int finalYear = s.HumanTeam.Roster.Count(p => p.Contract is { Years: 1 });
                Assert.True(finalYear > 0, "SAC should have a deal in its final year");
                var chip = roster.Chips.First(c => c.Label == "Expiring");
                Assert.Equal(finalYear.ToString(), chip.Value);
                Assert.Equal("warn", chip.Tone);
                var row = roster.Rows.First(r => r.Player.Contract is { Years: 1 });
                Assert.Equal("Final year", row.Years);
                Assert.Contains(roster.Rows, r => r.Player.Contract is { Years: >= 2 } c && r.Years == $"{c.Years} yr");
                Snap(window, settle, "run78-roster-expiring.png");

                // Once the summer counts contracts down, deals at 0 are the expiring ones and Years 1 reads "1 yr" (next season).
                int guard = 0;
                while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
                Assert.True(s.ContractsCounted(s.HumanTeam));
                roster.Refresh(); settle();
                int ending = s.HumanTeam.Roster.Count(p => p.Contract is { Years: <= 0 });
                Assert.Equal(ending.ToString(), roster.Chips.First(c => c.Label == "Expiring").Value);
                Assert.DoesNotContain(roster.Rows, r => r.Years == "Final year");
                return 0;
            }
            finally { window?.Close(); if (File.Exists(path)) File.Delete(path); }
        });
    }
}
