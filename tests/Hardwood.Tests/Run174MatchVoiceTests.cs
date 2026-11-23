using Avalonia.Headless;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 174, K25 (critic review #22): the play-by-play calls the game. Pass chains fold after the first pass, the
/// period's last shot is always resolved on the bar, the final horn gets a result call with what the game meant, a
/// shared surname gets an initial, late live-ball subs aren't read over the play, the push after a stop starts with the
/// man who has the ball, and the broadcast says "Miami", not "Miami (FL)". Presentation only: no engine or RNG change.</summary>
[Collection(SharedUiState.Name)]
public class Run174MatchVoiceTests
{
    private readonly ITestOutputHelper _out;
    public Run174MatchVoiceTests(ITestOutputHelper output) => _out = output;

    private static (Team home, Team away) Teams()
    {
        var l = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var h = l.Teams[0]; var a = l.Teams[1];
        h.Tactic = new Tactic(); a.Tactic = new Tactic();
        return (h, a);
    }

    private static List<Player> Five(Team t) => t.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();

    private static PossessionInput Input(List<PlayEvent> ev, List<Player> hf, List<Player> af, double start, double end,
        PossessionStart how = PossessionStart.DeadBall, Dictionary<Player, Spot>? spots = null, int periods = 2) =>
        new(9, 2026, ev, start, end, hf, af, spots ?? hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7)),
            0, 0, true, "Home", "Away", DefensiveScheme.Man, DefensiveScheme.Man, how, periods, false)
        { HomeSystem = OffensiveSystem.Motion };

    [Fact]
    public void Passes_fold_after_the_first_and_wording_never_draws_from_the_random_stream()
    {
        Assert.Equal("Ames swings it to Hart", PossessionChoreographer.PassCall(0, 0, "Ames", "Hart", "Duke"));
        Assert.Equal("Ames hits Hart", PossessionChoreographer.PassCall(0, 3, "Ames", "Hart", "Duke"));
        Assert.Null(PossessionChoreographer.PassCall(1, 0, "Ames", "Hart", "Duke"));
        // A long Motion possession: at most one pass line, whatever the chain.
        var (h, a) = Teams();
        var hf = Five(h); var af = Five(a);
        var ev = new List<PlayEvent> { new(1, 1170, PlayKind.Shot, true, hf[2], null, ShotZone.LongMid, false, 0, 0) };
        var c = PossessionChoreographer.Build(Input(ev, hf, af, 1200, 1170));
        Assert.True(c.Lines.Count(l => l.Text.Contains(" swings it to ") || l.Text.Contains(" kicks it to ") || l.Text.Contains(" moves it to ") || l.Text.Contains(" hits ")) <= 1);
        // Same input, same picture and same words.
        var c2 = PossessionChoreographer.Build(Input(ev, hf, af, 1200, 1170));
        Assert.Equal(c.Lines.Select(l => l.Text), c2.Lines.Select(l => l.Text));
        Assert.Equal(c.Tracks.Select(t => t.Count), c2.Tracks.Select(t => t.Count));
    }

    [Fact]
    public void The_last_shot_is_resolved_on_the_bar_before_the_horn()
    {
        var (h, a) = Teams();
        var hf = Five(h); var af = Five(a);
        var shooter = hf[1];
        foreach (bool made in new[] { true, false })
        {
            var ev = new List<PlayEvent>
            {
                new(2, 0.8, PlayKind.Shot, true, shooter, null, ShotZone.AboveBreakThree, made, made ? 70 : 67, 68),
                new(2, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, made ? 70 : 67, 68),
            };
            Assert.True(PossessionChoreographer.AtTheBuzzer(ev, 0));
            var c = PossessionChoreographer.Build(Input(ev, hf, af, 8, 0));
            var result = c.Lines.Single(l => l.Text.Contains("AT THE BUZZER") || l.Text.Contains("at the buzzer... no good"));
            var end = c.Lines.Single(l => l.Text.StartsWith("That's the end of"));
            // The result is on the bar for a beat before the horn's line replaces it.
            Assert.True(end.T - result.T >= PossessionChoreographer.ResultHold - 1e-9, $"{result.T} → {end.T}");
            Assert.Equal(result.Text, c.Sample(result.T + 0.5).Line);
        }
        // A shot with time left isn't "at the buzzer".
        var early = new List<PlayEvent> { new(2, 14, PlayKind.Shot, true, shooter, null, ShotZone.Rim, true, 2, 0) };
        Assert.False(PossessionChoreographer.AtTheBuzzer(early, 0));
    }

    [Fact]
    public void A_shared_surname_gets_an_initial_and_a_late_live_ball_sub_is_not_read_over_the_play()
    {
        var (h, a) = Teams();
        var hf = Five(h); var af = Five(a);
        var bench = h.Roster.Except(hf).First();
        var twin = new Player { Id = 999_001, FirstName = "Zed", LastName = bench.LastName, TeamId = h.Id };
        var spots = hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7));
        // The twin is on the floor; the bench man with the same surname comes in for the SG at a dead ball.
        var five = hf.ToList(); spots.Remove(five[3]); spots[twin] = new Spot(40, 30); five[3] = twin;
        var leaving = five[1];
        var on = five.ToList(); on[1] = bench;
        var ev = new List<PlayEvent> { new(2, 400, PlayKind.Shot, true, on[2], null, ShotZone.Rim, true, 2, 0) };
        var c = PossessionChoreographer.Build(Input(ev, on, af, 410, 400, PossessionStart.DeadBall, spots));
        var sub = c.Lines.Single(l => l.Text.StartsWith("Substitution"));
        Assert.Contains($"{bench.FirstName[0]}. {bench.LastName} in for {leaving.LastName}", sub.Text);
        // The same change after a stop with a minute left in the game: no sub line over the live play.
        var late = new List<PlayEvent> { new(2, 50, PlayKind.Shot, true, on[2], null, ShotZone.Rim, true, 2, 0) };
        var c2 = PossessionChoreographer.Build(Input(late, on, af, 58, 50, PossessionStart.AfterStop, spots));
        Assert.Contains(c2.Actions, x => x.Tag == "sub");
        Assert.DoesNotContain(c2.Lines, l => l.Text.StartsWith("Substitution"));
    }

    [Fact]
    public void The_push_after_a_stop_starts_with_the_man_who_has_the_ball()
    {
        var (h, a) = Teams();
        var hf = Five(h); var af = Five(a);
        var ev = new List<PlayEvent> { new(1, 1000, PlayKind.Shot, true, hf[3], null, ShotZone.Rim, true, 2, 0) };
        var c = PossessionChoreographer.Build(Input(ev, hf, af, 1010, 1000, PossessionStart.AfterStop) with { BallWith = hf[2] });
        Assert.Contains(c.Lines, l => l.Text.StartsWith($"{hf[2].LastName} pushes it up the floor"));
        // A defensive rebound and outlet: the possession ends with the outlet's target holding the ball.
        var reb = new List<PlayEvent>
        {
            new(1, 990, PlayKind.Shot, true, hf[1], null, ShotZone.LongMid, false, 0, 0),
            new(1, 989, PlayKind.DefensiveRebound, false, af[4], null, ShotZone.Rim, false, 0, 0),
        };
        var c2 = PossessionChoreographer.Build(Input(reb, hf, af, 1010, 989));
        // (Possession 9 says it the first way.)
        Assert.Contains(c2.Lines, l => l.Text == $"Outlet to {c2.EndHolder!.LastName}");
    }

    [Fact]
    public void The_final_horn_calls_the_result_and_what_it_meant()
    {
        Assert.Equal("Miami", GameCalls.BroadcastName("Miami (FL)", "Notre Dame"));
        Assert.Equal("Miami (FL)", GameCalls.BroadcastName("Miami (FL)", "Miami (OH)"));
        Assert.Equal("Notre Dame", GameCalls.BroadcastName("Notre Dame", "Miami (FL)"));

        PlayEvent P(int hs, int aw, int period = 2) => new(period, 10, PlayKind.Shot, true, null, null, ShotZone.Rim, true, hs, aw);
        // A close home win: "hold on".
        var close = GameCalls.Final(new[] { P(80, 78), P(87, 82) }, "Notre Dame", "Miami", 87, 82, 2);
        Assert.Equal("NOTRE DAME WIN IT! Final: Notre Dame 87, Miami 82", Assert.Single(close));
        Assert.StartsWith("NOTRE DAME HOLD ON!", GameCalls.Final(new[] { P(70, 68) }, "Notre Dame", "Miami", 70, 68, 2)[0]);
        // A comeback from 12 down, in overtime.
        var back = GameCalls.Final(new[] { P(30, 42), P(80, 80), P(90, 86, 3) }, "Notre Dame", "Miami", 90, 86, 2);
        Assert.Equal("NOTRE DAME COMPLETE THE COMEBACK! Final: Notre Dame 90, Miami 86 in overtime", back[0]);
        Assert.Equal("Notre Dame were down 12 and came all the way back", back[1]);
        // March: an upset that advances, and the title.
        var march = GameCalls.Final(new[] { P(60, 64) }, "Virginia", "Creighton", 60, 64, 2,
            new GameCalls.Occasion("Round of 64", "Round of 32", WinnerSeed: 12, LoserSeed: 5));
        Assert.Equal("Creighton advance to the Round of 32", march[1]);
        Assert.Equal("An upset: the 12-seed knocks out the 5-seed. Their season is over", march[2]);
        var title = GameCalls.Final(new[] { P(70, 61) }, "Duke", "Kansas", 70, 61, 2, new GameCalls.Occasion("Championship", null, 1, 2));
        Assert.Equal("DUKE ARE NATIONAL CHAMPIONS! Final: Duke 70, Kansas 61", Assert.Single(title));
        // The regular season: an unranked side over a ranked one, and a rivalry.
        var upset = GameCalls.Final(new[] { P(70, 61) }, "Virginia Tech", "Virginia", 70, 61, 2,
            new GameCalls.Occasion(LoserRank: 8, Rivalry: "Commonwealth Clash"));
        Assert.Equal("An upset: unranked Virginia Tech take down No. 8 Virginia", upset[1]);
        Assert.Equal("Commonwealth Clash: this one goes to Virginia Tech", upset[2]);
    }

    /// <summary>The critic's check (review #22): three whole college games through the live view, every line logged.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Three_college_games_call_the_game()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run174-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false, StudioShow = false });
        string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "31";
            start.PlayLeague = StartViewModel.CollegeChoice;
            start.SelectedTeam = start.Teams[1];
            start.NewGame();
            var s = vm.Session!;
            int allLines = 0, swings = 0, results = 0, unresolved = 0, lateSubs = 0, twinSubs = 0;
            var distinct = new HashSet<string>();
            double minDistinctPct = 100;
            for (int g = 0; g < 3; g++)
            {
                while (!s.HumanPlaysToday) s.AdvanceDay();
                var (game, played) = s.BeginLiveDay();
                var live = new LiveGameViewModel(vm, game!, played);
                live.PauseAtBreaks = false;
                var said = new List<CommentaryLine>();
                string home = live.HomeTeam.City, away = live.AwayTeam.City;
                while (!live.Complete)
                {
                    live.StepOne();
                }
                said.AddRange(live.SaidForTest);
                allLines += said.Count;
                swings += said.Count(l => l.Text.Contains(" swings it to "));
                foreach (var l in said) distinct.Add(l.Text);
                if (said.Any(l => l.Text.Contains("! Final: "))) results++;
                // The bar's view: a line replaced by one at the same instant is never seen. Every horn's line follows a
                // line that was seen for at least a beat.
                for (int i = 1; i < said.Count; i++)
                    if (said[i].Text.StartsWith("That's the end of") && said[i - 1].Text.EndsWith("...")) unresolved++;
                lateSubs += live.LateLiveSubLinesForTest;
                twinSubs += said.Count(l => System.Text.RegularExpressions.Regex.IsMatch(l.Text, @"(\b[A-Z][\w'-]+) in for \1\b"));
                foreach (var grp in said.GroupBy(l => l.Text).OrderByDescending(x => x.Count()).Take(12)) _out.WriteLine($"   {grp.Count()}× {grp.Key}");
                int gameDistinct = said.Select(l => l.Text).Distinct().Count();
                minDistinctPct = Math.Min(minDistinctPct, 100.0 * gameDistinct / said.Count);
                _out.WriteLine($"{away} at {home}: {said.Count} lines, {gameDistinct} distinct; last: {string.Join(" | ", said.TakeLast(4).Select(l => l.Text))}");
                if (g == 0)
                {
                    // The final call on the commentary bar at 1280 × 800: shown whole, nothing cut off.
                    vm.InLiveGame = true; vm.CurrentPage = live;
                    for (int i = 0; i < 5; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
                    Assert.Contains("! Final: ", live.Commentary);
                    Assert.Empty(LayoutCheck.Scan(window).Select(x => x.ToString()));
                    if (dir != null) { Directory.CreateDirectory(dir); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, "run174-final-call.png")); }
                    vm.InLiveGame = false;
                }
                s.FinishLiveDay(game!, played);
                s.AdvanceDay();
            }
            double swingPct = 100.0 * swings / allLines, distinctPct = 100.0 * distinct.Count / allLines;
            _out.WriteLine($"3 games: {allLines} lines, 'swings it to' {swings} ({swingPct:0.0}%), distinct {distinct.Count} ({distinctPct:0.0}% pooled, {minDistinctPct:0.0}% in the most repetitive game), result calls {results}, unresolved last shots {unresolved}, late live subs said {lateSubs}, twin subs {twinSubs}");
            Assert.True(swingPct < 8, $"swings {swingPct:0.0}%");
            Assert.True(minDistinctPct > 60, $"distinct {minDistinctPct:0.0}% in the most repetitive game");
            Assert.Equal(3, results);
            Assert.Equal(0, unresolved);
            Assert.Equal(0, lateSubs);
            Assert.Equal(0, twinSubs);
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
