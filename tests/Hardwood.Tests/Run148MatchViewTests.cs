using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 148, M1 of the 2D match engine (claude/match-engine-2d-design.md): every live game plays out in real time
/// on a full-screen, top-down court. The choreography only draws what the engine decided: the final box score with the
/// viewer on equals the plain simulation's, the choreography's own draws are its own, and the picture agrees with the
/// plays (the shooter in his zone, the score changing when the ball drops, the clock running down to the play).</summary>
[Collection(SharedUiState.Name)]
public class Run148MatchViewTests
{
    private readonly ITestOutputHelper _out;
    public Run148MatchViewTests(ITestOutputHelper output) => _out = output;

    private static (Team home, Team away) Teams(int league)
    {
        var l = new WorldGenerator(new Rng((ulong)league)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var h = l.Teams[0]; var a = l.Teams[1];
        h.Tactic = new Tactic(); a.Tactic = new Tactic();
        return (h, a);
    }

    /// <summary>Plays a live game possession by possession, choreographing each one as the viewer does.</summary>
    private static (GameResult result, List<Choreography> scripts) Watch(Team home, Team away, Rules rules, ulong seed)
    {
        var live = new GameSimulator().StartLive(home, away, rules, seed);
        var scripts = new List<Choreography>();
        Dictionary<Player, Spot>? spots = null;
        var next = PossessionStart.PeriodStart;
        int index = 0, hs = 0, aws = 0;
        while (!live.Complete)
        {
            int before = live.Result.Plays.Count; double elapsed = live.Result.Elapsed;
            live.Step();
            var ev = live.Result.Plays.Skip(before).ToList();
            double dur = live.Result.Elapsed - elapsed;
            double end = ev.Count > 0 ? ev[^1].Clock : live.Clock;
            var c = PossessionChoreographer.Build(new PossessionInput(index++, seed, ev, end + dur, end, live.HomeOnCourt.ToList(), live.AwayOnCourt.ToList(),
                spots, hs, aws, true, home.Name, away.Name, home.Tactic.Scheme, away.Tactic.Scheme, next, live.Result.Periods, false));
            scripts.Add(c);
            spots = c.FinalSpots(); next = c.Next;
            var f = c.Sample(c.Length); hs = f.HomeScore; aws = f.AwayScore;
        }
        return (live.Result, scripts);
    }

    [Theory]
    [InlineData(17, 4242UL, "nba")]
    [InlineData(17, 77UL, "college")]
    [InlineData(31, 2026UL, "fiba")]
    public void Watching_changes_no_result_and_the_picture_agrees_with_the_plays(int league, ulong seed, string kind)
    {
        var rules = kind switch { "college" => Rules.College, "fiba" => Rules.Fiba, _ => Rules.Nba };
        var (h, a) = Teams(league);
        var plain = new GameSimulator().StartLive(h, a, rules, seed); plain.Finish();
        (h, a) = Teams(league);
        var (watched, scripts) = Watch(h, a, rules, seed);
        // The same game, to the player.
        Assert.Equal(plain.Result.Home.Points, watched.Home.Points);
        Assert.Equal(plain.Result.Away.Points, watched.Away.Points);
        for (int side = 0; side < 2; side++)
        {
            var p = side == 0 ? plain.Result.Home : plain.Result.Away; var w = side == 0 ? watched.Home : watched.Away;
            Assert.Equal(p.Lines.Select(l => (l.Player.Name, l.Points, l.Rebounds, l.Assists, l.FGA, l.Fouls)), w.Lines.Select(l => (l.Player.Name, l.Points, l.Rebounds, l.Assists, l.FGA, l.Fouls)));
        }

        double screen = 0, game = 0;
        int scored = 0;
        foreach (var c in scripts)
        {
            screen += c.Length;
            game += Math.Max(0, c.Input.StartClock - c.Input.EndClock);
            // The clock never runs up inside a possession, and ends where the engine left it.
            double last = double.MaxValue;
            for (double t = 0; t <= c.Length; t += 0.25) { double k = c.ClockAt(t); Assert.True(k <= last + 1e-6, $"clock ran up at {t}"); last = k; }
            Assert.InRange(c.ClockAt(c.Length), c.Input.EndClock - 0.01, c.Input.EndClock + 0.01);
            // The score at the end is the engine's.
            var end = c.Sample(c.Length);
            if (c.Input.Events.Count > 0) { Assert.Equal(c.Input.Events[^1].HomeScore, end.HomeScore); Assert.Equal(c.Input.Events[^1].AwayScore, end.AwayScore); }
            // Every field-goal attempt is drawn from its shooter, in order: he stands in his zone when the ball leaves his
            // hands, and a make changes the score as it drops.
            var fga = c.Input.Events.Where(e => e.Kind == PlayKind.Shot).ToList();
            var flights = c.Ball.Where(b => b.Mode == BallMode.Shot && !b.FreeThrow).ToList();
            Assert.Equal(fga.Count, flights.Count);
            for (int k = 0; k < fga.Count; k++)
            {
                var e = fga[k]; var shot = flights[k];
                int slot = Array.IndexOf(c.Players, e.Actor);
                Assert.True(slot >= 0, "the shooter is drawn");
                Assert.Equal(slot, shot.Holder);
                if (e.Blocked) continue;
                var at = c.PositionOf(slot, shot.T0);
                double rim = e.Home ? PossessionChoreographer.RimRight : PossessionChoreographer.RimLeft;
                double dist = Math.Sqrt((at.X - rim) * (at.X - rim) + (at.Y - 25) * (at.Y - 25));
                if (e.Zone.IsThree()) Assert.True(dist > 21, $"a three from {dist:0.0} ft");
                if (e.Zone == ShotZone.Rim) Assert.True(dist < 6, $"a rim shot from {dist:0.0} ft");
                if (!e.Made) continue;
                var before = c.Sample(shot.T0); var after = c.Sample(shot.T1 + 0.01);
                Assert.NotEqual((before.HomeScore, before.AwayScore), (after.HomeScore, after.AwayScore));
                scored++;
            }
            // Nobody leaves the floor's surround.
            for (double t = 0; t <= c.Length; t += 0.5)
                foreach (var p in c.Sample(t).Players) { Assert.InRange(p.X, 0, 94); Assert.InRange(p.Y, 0, 51); }
        }
        Assert.True(scored > 40);
        // Real time: the picture runs at least as long as the game clock, plus its dead balls, but not wildly more.
        _out.WriteLine($"{kind}: {scripts.Count} possessions, game clock {game / 60:0.0} min, on screen at 1x {screen / 60:0.0} min");
        Assert.True(screen >= game);
        Assert.True(screen < game * 1.5, $"{screen / 60:0.0} min on screen for {game / 60:0.0} of game clock");
    }

    [Fact]
    public void The_same_seed_draws_the_same_game()
    {
        var (h, a) = Teams(17);
        var (_, one) = Watch(h, a, Rules.Nba, 99);
        (h, a) = Teams(17);
        var (_, two) = Watch(h, a, Rules.Nba, 99);
        Assert.Equal(one.Count, two.Count);
        for (int i = 0; i < one.Count; i += 7)
            for (double t = 0; t < one[i].Length; t += 1.3)
                Assert.Equal(one[i].Sample(t).Players, two[i].Sample(t).Players);
    }

    [Fact]
    public void Shirt_numbers_are_unique_on_a_roster()
    {
        var (h, _) = Teams(17);
        var n = ShirtNumbers.For(h);
        Assert.Equal(h.Roster.Count, n.Values.Distinct().Count());
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_live_page_is_the_court_and_plays_a_whole_game_with_no_result_change()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run148-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false, StudioShow = false });
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
            for (int i = 0; i < 30; i++) s.AdvanceDay();
            vm.AfterTick();
            var w = window;
            void Settle() { for (int i = 0; i < 5; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }
            void Shot(string name) { if (dir == null) return; Directory.CreateDirectory(dir); using var f = w.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, $"run148-{name}.png")); }

            var home = s.HumanTeam; var away = s.League.Teams.First(t => t != home);
            home.Tactic = new Tactic(); away.Tactic = new Tactic();
            var plain = new GameSimulator().StartLive(home, away, s.League.Rules, 314, s.Today); plain.Finish();
            home.Tactic = new Tactic(); away.Tactic = new Tactic();
            var game = new GameSimulator().StartLive(home, away, s.League.Rules, 314, s.Today);
            var page = new LiveGameViewModel(vm, game, new());
            vm.InLiveGame = true;
            vm.CurrentPage = page; Settle();
            var view = w.GetVisualDescendants().OfType<LiveGameView>().First(v => v.IsEffectivelyVisible);
            _out.WriteLine($"views {w.GetVisualDescendants().OfType<LiveGameView>().Count()}, children {string.Join(",", view.GetVisualDescendants().Take(8).Select(x => x.GetType().Name))}");
            var court = view.GetVisualDescendants().OfType<MatchCourt>().Single();
            // The court fills the page: no sidebar, and the floor is most of the window's width.
            Assert.False(w.FindControl<Border>("Sidebar")!.IsVisible);
            var (scale, origin) = MatchCourt.Fit(court.Bounds.Size, court.Reserve);
            _out.WriteLine($"page {court.Bounds.Size}, {scale:0.0} px/ft, floor {94 * scale:0} px wide");
            Assert.True(94 * scale > court.Bounds.Width * 0.6);
            Assert.Empty(LayoutCheck.Scan(w));
            Shot("tip");

            // A few seconds at 1×, frame by frame: the clock runs in real time.
            page.PlayPause();
            double clock0 = game.MinutesPerPeriod * 60;
            for (int i = 0; i < 6 * 60; i++) page.Play(1.0 / 60);
            Settle();
            Assert.NotNull(page.Script);
            Assert.InRange(clock0 - page.MatchFrame!.Clock, 3, 7.5);
            Shot("1x-mid-possession");
            // Render frames for the frame-time log.
            court.FrameMs.Clear();
            for (int i = 0; i < 120; i++) { page.Play(1.0 / 60); court.InvalidateVisual(); using var f = w.CaptureRenderedFrame(); }
            var ms = court.FrameMs.OrderBy(x => x).ToList();
            _out.WriteLine($"court render p50 {ms[ms.Count / 2]:0.00} ms, p95 {ms[(int)(ms.Count * 0.95)]:0.00} ms over {ms.Count} frames");
            // The drawers open over the court.
            page.OpenDrawerCommand.Execute("0"); Settle();
            Assert.True(view.FindControl<Border>("Drawer")!.IsVisible);
            Assert.True(view.FindControl<Border>("LogPanel")!.IsEffectivelyVisible);
            Shot("drawer-box");
            Assert.Empty(LayoutCheck.Scan(w).Select(x => x.ToString()));
            page.OpenDrawerCommand.Execute("2"); Settle();
            Assert.True(view.FindControl<TabControl>("SideTabs")!.IsEffectivelyVisible);
            Shot("drawer-tactics");
            page.CloseDrawerCommand.Execute(null);

            // The rest of the game at 8×, frame by frame (breaks don't stop PlayThrough).
            page.SetSpeedCommand.Execute("8");
            Assert.Equal(8, page.Rate);
            int frames = page.PlayThrough();
            _out.WriteLine($"8x: {frames} frames = {frames / 60.0 / 60:0.0} min");
            Assert.True(page.Complete);
            Assert.Equal(plain.Result.Home.Points, game.Result.Home.Points);
            Assert.Equal(plain.Result.Away.Points, game.Result.Away.Points);
            Assert.Equal(plain.Result.Home.Lines.Select(l => (l.Points, l.Rebounds, l.Assists)), game.Result.Home.Lines.Select(l => (l.Points, l.Rebounds, l.Assists)));
            Assert.Equal(game.Result.Home.Points, page.BarHomeScore);
            Assert.Equal(game.Result.Away.Points, page.BarAwayScore);
            Settle();
            Shot("final");
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
