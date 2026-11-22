using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 152, M5 of the 2D match engine (claude/match-engine-2d-design.md): the commentary's bigger calls (runs,
/// lead changes late, milestones, foul trouble), the clutch-plan chip (run 150's review), the "On the floor" drawer with
/// the jumbotron's numbers, keyboard shortcuts, and a whole game at 2× at 1920 × 1080 with a frame-time log. The old court
/// classes (LiveCourt, CourtTracker, TrackerMotion, the arena) are gone.</summary>
[Collection(SharedUiState.Name)]
public class Run152CommentaryTests
{
    private readonly ITestOutputHelper _out;
    public Run152CommentaryTests(ITestOutputHelper output) => _out = output;

    private static readonly Team HomeT = new() { Id = 1, City = "Home", Name = "Hawks", Abbreviation = "HOM", Division = "D" };
    private static readonly Team AwayT = new() { Id = 2, City = "Away", Name = "Owls", Abbreviation = "AWY", Division = "D" };

    private static (TeamBox home, TeamBox away, Player h, Player a) Boxes()
    {
        var h = new Player { FirstName = "Hal", LastName = "Hart" };
        var a = new Player { FirstName = "Al", LastName = "Ames" };
        var hb = new TeamBox { Team = HomeT }; hb.Lines.Add(new PlayerLine { Player = h });
        var ab = new TeamBox { Team = AwayT }; ab.Lines.Add(new PlayerLine { Player = a });
        return (hb, ab, h, a);
    }

    private static PlayEvent Make(Player p, bool home, int hs, int aw, int period = 1) =>
        new(period, 300, PlayKind.Shot, home, p, null, ShotZone.Rim, true, hs, aw);

    [Fact]
    public void Runs_are_called_at_eight_unanswered_and_every_four_after()
    {
        var calls = new GameCalls();
        var (hb, ab, h, a) = Boxes();
        int hs = 0, aw = 2;
        var said = new List<string>();
        calls.For(new[] { Make(a, false, 0, 2) }, hb, ab, 0, 0, "Home", "Away", 2, 5);
        for (int i = 0; i < 7; i++)
        {
            int before = hs; hs += 2;
            said.AddRange(calls.For(new[] { Make(h, true, hs, aw) }, hb, ab, before, aw, "Home", "Away", 2, 5));
        }
        // 14-0 by the home side: called at 8 and 12, never at 10 or 14.
        Assert.Equal(new[] { "That's an 8-0 run for Home", "That's a 12-0 run for Home" }, said.Where(s => s.Contains("run")));
        Assert.Equal(14, calls.Run);
        // The other side scores: the run ends without a call.
        Assert.DoesNotContain(calls.For(new[] { Make(a, false, hs, aw + 2) }, hb, ab, hs, aw, "Home", "Away", 2, 5), s => s.Contains("run"));
        Assert.Equal(-2, calls.Run);
    }

    [Fact]
    public void Lead_changes_are_counted_all_game_and_called_in_the_last_period()
    {
        var calls = new GameCalls();
        var (hb, ab, h, a) = Boxes();
        calls.For(new[] { Make(h, true, 2, 0, 1) }, hb, ab, 0, 0, "Home", "Away", 2, 5);
        Assert.Empty(calls.For(new[] { Make(a, false, 2, 3, 1) with { Zone = ShotZone.CornerThree } }, hb, ab, 2, 0, "Home", "Away", 2, 5));
        Assert.Equal(1, calls.LeadChanges);
        var late = calls.For(new[] { Make(h, true, 4, 3, 2) }, hb, ab, 2, 3, "Home", "Away", 2, 5);
        Assert.Equal("Lead change, the 2nd of the game: Home in front, 4-3", Assert.Single(late));
        // A tie isn't a change of lead; going back ahead from a tie by the same side isn't either.
        Assert.Empty(calls.For(new[] { Make(a, false, 4, 4, 2) with { Made = true } }, hb, ab, 4, 3, "Home", "Away", 2, 5));
        Assert.Empty(calls.For(new[] { Make(h, true, 6, 4, 2) }, hb, ab, 4, 4, "Home", "Away", 2, 5));
        Assert.Equal(2, calls.LeadChanges);
    }

    [Fact]
    public void Milestones_and_foul_trouble_are_said_once_for_the_player_in_the_play()
    {
        var calls = new GameCalls();
        var (hb, ab, h, a) = Boxes();
        var hl = hb.Lines[0]; var al = ab.Lines[0];
        hl.Points = 21; hl.DReb = 9;
        al.Points = 25;   // not in the play: nothing about him
        var said = calls.For(new[] { Make(h, true, 40, 50) }, hb, ab, 38, 50, "Home", "Away", 2, 5);
        Assert.Contains("Hart has 21 points", said);
        Assert.DoesNotContain(said, s => s.Contains("Ames"));
        hl.Points = 23; hl.DReb = 10;
        said = calls.For(new[] { new PlayEvent(2, 200, PlayKind.DefensiveRebound, true, h, null, ShotZone.Rim, false, 40, 50) }, hb, ab, 40, 50, "Home", "Away", 2, 5);
        Assert.Equal("A double-double for Hart: 23 points, 10 rebounds", Assert.Single(said));
        // Foul trouble at one short of fouling out (the 4th of 5), then the foul-out first among the calls.
        al.Fouls = 4;
        said = calls.For(new[] { new PlayEvent(2, 150, PlayKind.Foul, false, a, h, ShotZone.Rim, false, 40, 50) }, hb, ab, 40, 50, "Home", "Away", 2, 5);
        Assert.Contains("Ames picks up his 4th foul: one more and he's done", said);
        Assert.Contains("Ames has 25 points", said);
        al.Fouls = 5; al.FouledOut = true;
        said = calls.For(new[] { new PlayEvent(2, 100, PlayKind.Foul, false, a, h, ShotZone.Rim, false, 40, 50) }, hb, ab, 40, 50, "Home", "Away", 2, 5);
        Assert.Equal("Ames has fouled out with 25 points", said[0]);
        // Nothing is said twice.
        Assert.Empty(calls.For(new[] { new PlayEvent(2, 90, PlayKind.Foul, false, a, h, ShotZone.Rim, false, 40, 50) }, hb, ab, 40, 50, "Home", "Away", 2, 5));
        Assert.Equal("an", GameCalls.Article(11)); Assert.Equal("a", GameCalls.Article(12)); Assert.Equal("an", GameCalls.Article(83));
        // The drawer and speed keys (code-behind, so a focused picker keeps its letters).
        Assert.Equal("drawer:7", LiveGameView.KeyAction(Avalonia.Input.Key.O));
        Assert.Equal("speed:8", LiveGameView.KeyAction(Avalonia.Input.Key.D4));
        Assert.Null(LiveGameView.KeyAction(Avalonia.Input.Key.Z));
        Assert.Equal("21st", GameCalls.Ordinal(21)); Assert.Equal("12th", GameCalls.Ordinal(12)); Assert.Equal("3rd", GameCalls.Ordinal(3));
    }

    [Fact]
    public void The_clutch_plan_is_on_as_the_engine_plays_it()
    {
        var t = new Team { Id = 3, City = "C", Name = "N", Abbreviation = "CCC", Division = "D", Tactic = new Tactic() };
        Assert.False(LiveGameViewModel.ClutchOn(t, 2, 2, 120, 60, 58));
        t.Tactic.Presets.Add(new TacticPreset { Name = "Closer" });
        t.Tactic.LateGamePreset = "Closer";
        Assert.True(LiveGameViewModel.ClutchOn(t, 2, 2, 300, 60, 54));
        Assert.True(LiveGameViewModel.ClutchOn(t, 3, 2, 200, 70, 70), "overtime too");
        Assert.False(LiveGameViewModel.ClutchOn(t, 2, 2, 301, 60, 58), "more than five minutes left");
        Assert.False(LiveGameViewModel.ClutchOn(t, 2, 2, 120, 60, 53), "a seven-point game");
        Assert.False(LiveGameViewModel.ClutchOn(t, 1, 2, 120, 30, 30), "the first half");
        Assert.True(LiveGameViewModel.ClutchOn(t, 4, 4, 120, 90, 89), "the pros' fourth quarter");
    }

    [Fact]
    public void Calls_are_said_as_the_next_possession_starts_and_make_it_a_key_moment()
    {
        var p = new Player { FirstName = "A", LastName = "Bee" };
        var five = Enumerable.Range(0, 5).Select(i => new Player { FirstName = "H", LastName = $"H{i}" }).ToList();
        var other = Enumerable.Range(0, 5).Select(i => new Player { FirstName = "A", LastName = $"A{i}" }).ToList();
        var ev = new List<PlayEvent> { new(2, 100, PlayKind.Shot, true, five[0], null, ShotZone.LongMid, false, 50, 50), new(2, 99, PlayKind.DefensiveRebound, false, other[4], null, ShotZone.Rim, false, 50, 50) };
        PossessionInput In(IReadOnlyList<string> notes, IReadOnlyList<string> calls) => new(5, 7, ev, 114, 99, five, other, null, 50, 50, true, "Home", "Away",
            DefensiveScheme.Man, DefensiveScheme.Man, PossessionStart.AfterMake, 2, false) { Notes = notes, Calls = calls };
        var plain = PossessionChoreographer.Build(In(Array.Empty<string>(), Array.Empty<string>()));
        Assert.False(plain.Key);
        var c = PossessionChoreographer.Build(In(new[] { "Home switch to a 2-3 zone" }, new[] { "That's a 10-0 run for Home" }));
        Assert.True(c.Key);
        var zone = c.Lines.First(l => l.Text.StartsWith("Home switch"));
        var run = c.Lines.First(l => l.Text.StartsWith("That's a 10-0"));
        Assert.Equal(PossessionChoreographer.NoteTime, run.T - zone.T, 3);
        Assert.True(run.Big);
        Assert.Contains(c.Actions, a => a.Tag == "call");
        // The bring-up comes after both.
        Assert.True(c.Lines.First(l => l.Text.Contains("brings it up") || l.Text.Contains("pushes it")).T >= run.T + 1.9);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_whole_game_at_2x_at_1080p_says_the_big_calls_and_keeps_its_frame_time()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run152-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false, StudioShow = false });
        string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1920, Height = 1080 };
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
            void Shot(string name) { if (dir == null) return; Directory.CreateDirectory(dir); using var f = w.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, $"run152-{name}.png")); }

            var home = s.HumanTeam; var away = s.League.Teams.First(t => t != home);
            // A clutch plan for us (both runs carry it, so the plain game is the same game): the chip shows while it plays.
            Tactic Ours() { var t = new Tactic(); t.Presets.Add(new TacticPreset { Name = "Closer", System = OffensiveSystem.PickAndRoll, Scheme = t.Scheme, Pace = 92 }); t.LateGamePreset = "Closer"; return t; }
            home.Tactic = Ours(); away.Tactic = new Tactic();
            var plain = new GameSimulator().StartLive(home, away, s.League.Rules, 152, s.Today); plain.Finish();
            home.Tactic = Ours(); away.Tactic = new Tactic();
            var game = new GameSimulator().StartLive(home, away, s.League.Rules, 152, s.Today);
            var page = new LiveGameViewModel(vm, game, new());
            vm.InLiveGame = true;
            vm.CurrentPage = page; Settle();
            var view = w.GetVisualDescendants().OfType<LiveGameView>().First(v => v.IsEffectivelyVisible);
            var court = view.GetVisualDescendants().OfType<MatchCourt>().Single();

            // The frame-time log at 1920 × 1080: render 300 frames at 1×.
            page.PlayPause();
            for (int i = 0; i < 120; i++) page.Play(1.0 / 60);
            court.FrameMs.Clear();
            for (int i = 0; i < 300; i++) { page.Play(1.0 / 60); court.InvalidateVisual(); using var f = w.CaptureRenderedFrame(); }
            var ms = court.FrameMs.OrderBy(x => x).ToList();
            double p95 = ms[(int)(ms.Count * 0.95)];
            _out.WriteLine($"1080p court render p50 {ms[ms.Count / 2]:0.00} ms, p95 {p95:0.00} ms, max {ms[^1]:0.00} ms over {ms.Count} frames");
            Assert.True(ms.Count >= 250);

            // The On the floor drawer: the jumbotron's rows, momentum and both fives.
            page.OpenDrawerCommand.Execute("7"); Settle();
            Assert.True(view.FindControl<ScrollViewer>("FloorDrawer")!.IsEffectivelyVisible);
            Assert.Equal(5, page.HomeFloor.Count);
            Assert.Equal(5, page.AwayFloor.Count);
            Assert.NotEmpty(page.JumboRows);
            Assert.Equal("On the floor", page.DrawerTitle);
            Assert.Empty(LayoutCheck.Scan(w).Select(x => x.ToString()));
            Shot("drawer-on-the-floor");
            page.CloseDrawerCommand.Execute(null);

            // The rest of the game at 2×, frame by frame, gathering what the bar says.
            page.SetSpeedCommand.Execute("2");
            page.PauseAtBreaks = false;
            var said = new List<string>();
            Choreography? last = null;
            double longest = 0, real = 0;
            bool clutchSeen = false;
            int frames = 0;
            while (!page.Complete && frames < 2_000_000)
            {
                page.Play(1.0 / 60); frames++; real += 1.0 / 60;
                if (page.Script != last)
                {
                    if (last != null) longest = Math.Max(longest, real);
                    real = 0;
                    last = page.Script;
                    if (last != null) said.AddRange(last.Lines.Select(l => l.Text));
                }
                if (page.HasClutchChip && !clutchSeen) { clutchSeen = true; Settle(); Shot("clutch-chip"); }
            }
            _out.WriteLine($"2x: {frames} frames = {frames / 60.0 / 60:0.0} min; longest possession on screen {longest:0.0} s; {said.Count} lines");
            foreach (var l in said.Where(x => x.Contains(" run for ") || x.Contains("Lead change") || x.Contains("points") || x.Contains("foul:") || x.Contains("clutch")).Distinct().Take(14))
                _out.WriteLine("  " + l);
            Assert.True(page.Complete);
            Assert.True(clutchSeen, "the clutch plan's chip showed in a close fourth quarter");
            // No stall: no possession holds the screen for more than 40 real seconds at 2× (a period end with its changes is the longest).
            Assert.InRange(longest, 1, 40);
            // The vocabulary: passes or drives, blocks or steals, fouls, free throws and the calls.
            Assert.Contains(said, x => x.Contains("Whistle") || x.StartsWith("Foul on"));
            Assert.Contains(said, x => x.Contains("from the line") || x.Contains("knocks it down") || x.Contains("makes the first"));
            Assert.Contains(said, x => x.Contains("REJECTED") || x.StartsWith("Stolen"));
            Assert.Contains(said, x => x.Contains(" run for ") || x.Contains("Lead change") || x.Contains(" points"));
            // Nothing the viewer said changed the game.
            Assert.Equal(plain.Result.Home.Points, game.Result.Home.Points);
            Assert.Equal(plain.Result.Away.Points, game.Result.Away.Points);
            Assert.Equal(plain.Result.Home.Lines.Select(l => (l.Points, l.Rebounds, l.Assists)), game.Result.Home.Lines.Select(l => (l.Points, l.Rebounds, l.Assists)));
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
