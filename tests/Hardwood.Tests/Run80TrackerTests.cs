using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 80. The 2D tracker, first slice (D67 B): the engine records each play as data beside its commentary
/// (no random numbers, no second simulator, #595), and the live game's court draws ten players and the ball for the
/// latest play, with a Step control while paused.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run80TrackerTests
{
    [Fact]
    public void Recorded_plays_agree_with_the_box_score_and_change_no_result()
    {
        var league = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        foreach (ulong seed in new ulong[] { 4242, 77, 2026 })
        {
            var quick = sim.Simulate(league.Teams[0], league.Teams[1], Rules.Nba, seed, keepLog: false);
            var logged = sim.Simulate(league.Teams[0], league.Teams[1], Rules.Nba, seed, keepLog: true);
            Assert.Empty(quick.Plays);
            // Same game with or without the record.
            Assert.Equal(quick.Home.Points, logged.Home.Points);
            Assert.Equal(quick.Away.Points, logged.Away.Points);
            Assert.Equal(quick.Home.FGA, logged.Home.FGA);
            Assert.Equal(quick.Away.Turnovers, logged.Away.Turnovers);

            var plays = logged.Plays;
            foreach (var box in new[] { logged.Home, logged.Away })
            {
                bool home = box == logged.Home;
                var mine = plays.Where(p => p.Home == home).ToList();
                Assert.Equal(box.FGA, mine.Count(p => p.Kind == PlayKind.Shot));
                Assert.Equal(box.FGM, mine.Count(p => p.Kind == PlayKind.Shot && p.Made));
                Assert.Equal(box.FTA, mine.Count(p => p.Kind == PlayKind.FreeThrow));
                Assert.Equal(box.FTM, mine.Count(p => p.Kind == PlayKind.FreeThrow && p.Made));
                Assert.Equal(box.Steals, mine.Count(p => p.Kind == PlayKind.Steal));
                Assert.Equal(box.Blocks, plays.Count(p => p.Blocked && p.Home != home));
                Assert.Equal(box.OReb, mine.Count(p => p.Kind == PlayKind.OffensiveRebound));
                Assert.Equal(box.DReb, mine.Count(p => p.Kind == PlayKind.DefensiveRebound));
                Assert.Equal(box.Assists, mine.Count(p => p.Kind == PlayKind.Shot && p.Made && p.Other != null));
                Assert.Equal(box.Fouls, plays.Count(p => p.Kind == PlayKind.Foul && p.Home == home));
            }
            Assert.Equal(logged.Periods + logged.Overtimes, plays.Count(p => p.Kind == PlayKind.PeriodEnd));
            Assert.Equal(logged.Home.Points, plays[^1].HomeScore);
            Assert.Equal(logged.Away.Points, plays[^1].AwayScore);
            // The clock only runs down within a period.
            for (int i = 1; i < plays.Count; i++)
                if (plays[i].Period == plays[i - 1].Period) Assert.True(plays[i].Clock <= plays[i - 1].Clock + 1e-9);
        }
    }

    [Fact]
    public void The_tracker_sets_ten_players_in_the_half_with_the_ball()
    {
        var league = new WorldGenerator(new Rng(18)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        var live = sim.StartLive(league.Teams[2], league.Teams[3], Rules.Nba, 9, new DateOnly(2026, 11, 5));
        int checkedShots = 0, seen = 0;
        while (live.Step())
        {
            var plays = live.Result.Plays;
            for (int idx = seen; idx < plays.Count; idx++)
            {
                var play = plays[idx];
                var f = TrackerLayout.Build(play, idx, live.HomeOnCourt, live.AwayOnCourt, "x");
                Assert.Equal(10, f.Markers.Count);
                Assert.Equal(5, f.Markers.Count(m => m.Home));
                Assert.All(f.Markers, m => Assert.InRange(m.Spot.X, 0, 94));
                Assert.All(f.Markers, m => Assert.InRange(m.Spot.Y, 0, 50));
                Assert.All(f.Markers, m => Assert.InRange(m.Label.Length, 1, 3));
                if (play.Kind != PlayKind.Shot) continue;
                // Everyone is in the attacking half (home attacks right), and the shooter stands at the shot's spot.
                bool right = play.HomeOffense;
                Assert.All(f.Markers, m => Assert.True(right ? m.Spot.X >= 47 - 1e-9 : m.Spot.X <= 47 + 1e-9, $"{m.Label} at {m.Spot}"));
                Assert.Equal(new LiveShot(play.HomeOffense, play.Zone, play.Made, idx).Spot, f.From);
                // Even when he was subbed off at the dead ball that ended the possession, the shooter is drawn.
                Assert.Contains(f.Markers, m => m.Actor && m.Label == TrackerLayout.Label(play.Actor!));
                if (play.Made) Assert.Equal(TrackerLayout.Basket(play.HomeOffense), f.Ball);
                checkedShots++;
            }
            seen = plays.Count;
        }
        Assert.True(checkedShots > 100);
    }

    [Fact]
    public void The_headline_play_of_a_possession_is_its_shot()
    {
        var p = new Player { FirstName = "A", LastName = "Bee" };
        PlayEvent E(PlayKind k) => new(1, 600, k, true, p, null, ShotZone.Rim, false, 0, 0);
        var plays = new List<PlayEvent> { E(PlayKind.Shot), E(PlayKind.OffensiveRebound), E(PlayKind.Shot), E(PlayKind.DefensiveRebound) };
        Assert.Equal(2, LiveGameViewModel.HeadlinePlay(plays, 0));
        Assert.Equal(3, LiveGameViewModel.HeadlinePlay(plays, 3));
        var fts = new List<PlayEvent> { E(PlayKind.Foul), E(PlayKind.FreeThrow), E(PlayKind.FreeThrow) };
        Assert.Equal(2, LiveGameViewModel.HeadlinePlay(fts, 0));
        // A shot, then a trip to the line later in the same refresh: the latest trip is shown.
        var later = new List<PlayEvent> { E(PlayKind.Shot), E(PlayKind.DefensiveRebound), E(PlayKind.Foul), E(PlayKind.FreeThrow) };
        Assert.Equal(3, LiveGameViewModel.HeadlinePlay(later, 0));
        // An and-one: the basket, not its free throw.
        var andOne = new List<PlayEvent> { E(PlayKind.Shot) with { Made = true }, E(PlayKind.Foul), E(PlayKind.FreeThrow) };
        Assert.Equal(0, LiveGameViewModel.HeadlinePlay(andOne, 0));
        Assert.Equal("BEE", TrackerLayout.Label(p));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_live_game_draws_the_latest_play_and_steps_while_paused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run80-{Guid.NewGuid():N}.hwsave");
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
            start.SelectedTeam = start.Teams.First(t => t.Team.Abbreviation == "BOS");
            start.NewGame();
            var s = vm.Session!;
            int guard = 0;
            while (!s.HumanPlaysToday && guard++ < 60) s.AdvanceDay();
            Assert.True(s.HumanPlaysToday);
            vm.AfterTick();
            var w = window;
            void Settle() { for (int i = 0; i < 4; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

            var (live, others) = s.BeginLiveDay();
            var page = new LiveGameViewModel(vm, live!, others);
            vm.CurrentPage = page; Settle();
            Assert.NotNull(page.Frame);   // tip-off: the home side set up to attack
            Assert.Equal("Tip-off.", page.PlayCaption);
            Assert.True(page.CanStep);

            // Step: one possession, and the court shows what happened in it.
            page.StepOne(); Settle();
            Assert.False(page.Running);
            Assert.NotEqual("Tip-off.", page.PlayCaption);
            Assert.Equal(10, page.Frame!.Markers.Count);
            Assert.Contains("Stepped one possession", page.Status);

            // Mid-game, as the screenshot test leaves it.
            for (int i = 0; i < 120; i++) live!.Step();
            page.Refresh(); Settle();
            var last = live!.Result.Plays[^1];
            Assert.Contains(page.Caption(live.Result.Plays[LiveGameViewModel.HeadlinePlay(live.Result.Plays, 0)]), page.PlayCaption);
            Assert.True(last.Clock >= 0);
            var view = window.GetVisualDescendants().OfType<LiveGameView>().First(v => v.Bounds.Width > 0);
            var court = view.GetVisualDescendants().OfType<LiveCourt>().First();
            Assert.Same(page.Frame, court.Frame);
            Assert.True(court.Bounds.Height >= 200, $"court {court.Bounds}");   // the tracker is the live game's main view
            var step = view.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Step");
            Assert.True(step.IsEffectivelyEnabled);
            var problems = LayoutCheck.Scan(window);
            Assert.True(problems.Count == 0, "Live game:\n" + string.Join("\n", problems));
            if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, "run80-tracker.png")); }

            // Step on to a shot for a picture of one.
            for (int i = 0; i < 40 && page.Frame!.Kind != PlayKind.Shot && !page.Complete; i++) { page.StepOne(); Settle(); }
            if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, "run80-tracker-shot.png")); }
            Assert.True(LayoutCheck.Scan(window).Count == 0);

            page.PlayPause(); Settle();
            Assert.False(page.CanStep);
            page.SimToEnd(); Settle();
            Assert.False(page.CanStep);
            Assert.Contains("End of the period", page.Caption(live.Result.Plays.Last(p => p.Kind == PlayKind.PeriodEnd)));
            if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, "run80-tracker-final.png")); }
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
