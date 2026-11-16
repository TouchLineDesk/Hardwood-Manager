using Avalonia.Headless;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 69 (D75 A, #613): watching your game live, the assistant's halftime read is a one-click suggestion on the
/// halftime card. It is the same read Continue applies, it lasts for the second half only, and applying it every time
/// plays exactly the games Continue plays.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run69HalftimeReadTests
{
    private static int BenchClub(ulong seed)
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026);
        return probe.Teams.OrderBy(t => QuickSim.Strength(t)).ElementAt(14).Id;
    }

    private static bool AtHalftime(GameSimulator.LiveGame live) =>
        live.Log.Any(l => l.Contains($"End of period {live.Result.Periods / 2}:") || l.Contains($"End of period {live.Result.Periods / 2} "));

    [Fact]
    public void Applying_the_read_when_watching_plays_the_same_games_as_Continue()
    {
        // A manual coach with the halftime read on. Continue applies it inside the engine; the watched career applies the
        // suggestion at every halftime (the same read, from LiveGame.HalftimeRead) and puts its own plan back after.
        var id = BenchClub(2026);
        var quick = GameSession.NewGame(2026, id, savePath: null);
        var watched = GameSession.NewGame(2026, id, savePath: null);
        foreach (var s in new[] { quick, watched }) { s.HumanTeam.Tactic.Auto = false; s.HumanTeam.Tactic.HalftimeAdjust = true; }
        int games = 0, applied = 0;
        for (int d = 0; d < 30; d++)
        {
            bool mine = watched.HumanPlaysToday;
            quick.AdvanceDay();
            if (!mine) { watched.AdvanceDay(); continue; }
            var (live, played) = watched.BeginLiveDay();
            Assert.NotNull(live);
            var t = watched.HumanTeam.Tactic;
            var own = (t.System, t.Scheme);
            while (!AtHalftime(live!) && live!.Step()) { }
            if (live!.HalftimeRead(watched.HumanTeam) is var (sys, sch) && (sys != t.System || sch != t.Scheme))
            {
                t.System = sys; t.Scheme = sch; applied++;
            }
            while (live.Step()) { }
            (t.System, t.Scheme) = own;
            watched.FinishLiveDay(live, played);
            games++;
        }
        Assert.True(games >= 6, $"some games watched ({games})");
        Assert.True(applied >= 1, $"the read changed something at least once ({applied})");
        Assert.Equal(quick.Results.Count, watched.Results.Count);
        for (int i = 0; i < quick.Results.Count; i++)
        {
            var a = quick.Results[i]; var b = watched.Results[i];
            Assert.Equal((a.Home.Team.Id, a.Away.Team.Id), (b.Home.Team.Id, b.Away.Team.Id));
            Assert.True(a.Home.Points == b.Home.Points && a.Away.Points == b.Away.Points && a.Home.FGA == b.Home.FGA,
                $"game {i} ({a.Away.Team.Abbreviation} at {a.Home.Team.Abbreviation}, {a.Date}): Continue {a.Away.Points}-{a.Home.Points}, watched {b.Away.Points}-{b.Home.Points}");
        }
        Assert.Equal(quick.HumanTeam.Tactic.System, watched.HumanTeam.Tactic.System);
        Assert.Equal(quick.HumanTeam.Tactic.Scheme, watched.HumanTeam.Tactic.Scheme);
    }

    [Fact]
    public void The_read_is_a_pure_read()
    {
        // Asking for the read mid-game draws no random numbers: a game asked every possession ends as one never asked.
        var id = BenchClub(77);
        var a = GameSession.NewGame(77, id, savePath: null);
        var b = GameSession.NewGame(77, id, savePath: null);
        while (!a.HumanPlaysToday) { a.AdvanceDay(); b.AdvanceDay(); }
        var (la, pa) = a.BeginLiveDay(); var (lb, pb) = b.BeginLiveDay();
        while (la!.Step()) { la.HalftimeRead(a.HumanTeam); la.HalftimeRead(la.Result.Home.Team == a.HumanTeam ? la.Result.Away.Team : la.Result.Home.Team); }
        while (lb!.Step()) { }
        Assert.Equal((lb.HomeScore, lb.AwayScore, lb.Log.Count), (la.HomeScore, la.AwayScore, la.Log.Count));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_halftime_card_offers_the_read_with_one_click()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run69-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            start.SelectedTeam = start.Teams[3];
            start.NewGame();
            var s = vm.Session!;
            void Settle() { for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            void Snap(string name) { if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, name)); } }
            s.HumanTeam.Tactic.Auto = false;
            while (!s.HumanPlaysToday) s.AdvanceDay();
            vm.AfterTick();
            var (live, others) = s.BeginLiveDay();
            var page = new LiveGameViewModel(vm, live!, others);
            vm.CurrentPage = page; Settle();
            while (!AtHalftime(live!) && live!.Step()) { }
            page.Refresh();

            // Make sure the read has something to change: play anything but the counter.
            var (cs, cd) = live!.HalftimeRead(s.HumanTeam)!.Value;
            var t = s.HumanTeam.Tactic;
            t.System = Enum.GetValues<OffensiveSystem>().First(x => x != cs);
            t.Scheme = Enum.GetValues<DefensiveScheme>().First(x => x != cd);
            var own = (t.System, t.Scheme);
            page.ShowBreakCard(final: false);
            Assert.Equal("Halftime", page.BreakTitle);
            Assert.True(page.HasReadSuggestion);
            Assert.True(page.CanApplyRead);
            Assert.False(page.ReadApplied);
            Assert.Equal("Assistant's halftime read", page.ReadTitle);
            Assert.Contains(Systems.Name(page.Them.Tactic.System).ToLower(), page.ReadLine);
            Assert.Contains(Systems.Name(cs).ToLower(), page.ReadLine);
            Assert.Contains(Systems.Name(cd).ToLower(), page.ReadLine);
            Assert.Contains("Second half only", page.ReadNote);
            Assert.DoesNotContain(page.BreakChips, c => c.Value == "Switch?");   // the card replaces the chip at halftime
            Snap("run69-halftime-read.png");

            page.ApplyRead();
            Assert.True(page.ReadApplied);
            Assert.False(page.CanApplyRead);
            Assert.Equal((cs, cd), (t.System, t.Scheme));
            Assert.Equal(cs, page.OffenseChoice!.System);
            Assert.Equal(cd, page.DefenseChoice!.Scheme);
            Assert.Contains("applied", page.ReadTitle);
            Assert.Contains(Systems.Name(own.System).ToLower(), page.ReadNote);   // says what comes back after the game
            Snap("run69-halftime-read-applied.png");

            page.UndoRead();
            Assert.Equal(own, (t.System, t.Scheme));
            Assert.True(page.CanApplyRead);

            // Code review: applied, then Main menu or closing the window saves mid-game: your own plan is what's saved.
            page.ApplyRead();
            Assert.True(vm.SaveOnLeave());
            Assert.Equal(own, (t.System, t.Scheme));
            Assert.False(page.ReadApplied);
            Assert.True(page.CanApplyRead);
            Assert.Equal("Assistant's halftime read", page.ReadTitle);

            // Applied, then the game ends: your own plan comes back (second half only, as on Continue).
            page.ApplyRead();
            page.SimToEnd();
            Assert.True(page.Complete);
            Assert.Equal(own, (t.System, t.Scheme));
            Assert.False(page.HasReadSuggestion);
            page.Continue();
            Assert.Equal(own, (s.HumanTeam.Tactic.System, s.HumanTeam.Tactic.Scheme));

            // The next game: already in the counter at halftime, so no card, and the chip says so.
            while (!s.HumanPlaysToday) s.AdvanceDay();
            vm.AfterTick();
            var (live2, others2) = s.BeginLiveDay();
            var page2 = new LiveGameViewModel(vm, live2!, others2);
            vm.CurrentPage = page2; Settle();
            while (!AtHalftime(live2!) && live2!.Step()) { }
            var (cs2, cd2) = live2!.HalftimeRead(s.HumanTeam)!.Value;
            var own2 = (t.System, t.Scheme);
            t.System = cs2; t.Scheme = cd2;
            page2.ShowBreakCard(final: false);
            Assert.False(page2.HasReadSuggestion);
            Assert.Contains(page2.BreakChips, c => c.Value == "Countered");
            (t.System, t.Scheme) = own2;
            page2.SimToEnd();
            page2.Continue();
        }
        finally
        {
            window?.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
