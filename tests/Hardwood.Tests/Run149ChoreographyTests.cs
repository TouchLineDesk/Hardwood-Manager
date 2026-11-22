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

namespace Hardwood.Tests;

/// <summary>Run 149, M2 of the 2D match engine (claude/match-engine-2d-design.md): the choreography runs each offensive
/// system's set, shows each defensive scheme's shape, runs in transition after a stop, plays substitutions and period ends
/// as dead-ball scenes, and moves tired players slower. Presentation only: none of this can change a result.</summary>
[Collection(SharedUiState.Name)]
public class Run149ChoreographyTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public Run149ChoreographyTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    private static (Team home, Team away) Teams()
    {
        var l = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var h = l.Teams[0]; var a = l.Teams[1];
        h.Tactic = new Tactic(); a.Tactic = new Tactic();
        return (h, a);
    }

    private static List<Player> Five(Team t) => t.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();

    /// <summary>A home half-court possession of 16 seconds: the SF hits a corner three, assisted by the PG.</summary>
    private static PossessionInput Input(OffensiveSystem system, DefensiveScheme scheme, PossessionStart how = PossessionStart.DeadBall,
        Func<Player, double>? energy = null, Dictionary<Player, Spot>? start = null, Func<Player, Role>? roles = null, bool assisted = true,
        List<Player>? homeFive = null)
    {
        var (h, a) = Teams();
        var hf = homeFive ?? Five(h); var af = Five(a);
        var shooter = hf[2]; var passer = hf[0];
        var ev = new List<PlayEvent> { new(1, 500, PlayKind.Shot, true, shooter, assisted ? passer : null, ShotZone.CornerThree, true, 3, 0) };
        start ??= hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7));
        return new PossessionInput(7, 2026, ev, 516, 500, hf, af, start, 0, 0, true, "Home", "Away", DefensiveScheme.Man, scheme, how, 4, false)
        {
            HomeSystem = system, EnergyOf = energy, RoleOf = roles,
        };
    }

    private static double ToRim(Spot p) => p.DistanceTo(new Spot(PossessionChoreographer.RimRight, 25));

    [Fact]
    public void Pick_and_roll_sets_a_ball_screen_and_the_screener_rolls_or_pops()
    {
        var c = PossessionChoreographer.Build(Input(OffensiveSystem.PickAndRoll, DefensiveScheme.Man));
        Assert.Equal(OffensiveSystem.PickAndRoll, c.Set);
        var screen = Assert.Single(c.Actions, x => x.Tag == "screen");
        Assert.Contains(c.Actions, x => x.Tag is "roll" or "pop" && x.Slot == screen.Slot);
        // At the screen the screener stands next to the ball handler.
        var f = c.Sample(screen.T);
        Assert.True(f.Players[screen.Slot].DistanceTo(f.Players[0]) < 4.5, $"screener {f.Players[screen.Slot]} handler {f.Players[0]}");
        Assert.Contains(c.Lines, l => l.Text.Contains("ball screen"));
    }

    [Fact]
    public void Motion_runs_a_cut_and_a_down_screen()
    {
        var c = PossessionChoreographer.Build(Input(OffensiveSystem.Motion, DefensiveScheme.Man));
        var cut = Assert.Single(c.Actions, x => x.Tag == "cut");
        Assert.Contains(c.Actions, x => x.Tag == "screen");
        // The cutter goes through the lane.
        double closest = Enumerable.Range(0, 60).Select(i => cut.T + i * 0.1).Min(t => ToRim(c.PositionOf(cut.Slot, t)));
        Assert.True(closest < 8, $"the cutter came within {closest:0.0} ft of the rim");
    }

    [Fact]
    public void Post_centric_puts_a_big_on_the_block()
    {
        var c = PossessionChoreographer.Build(Input(OffensiveSystem.PostCentric, DefensiveScheme.Man));
        var post = Assert.Single(c.Actions, x => x.Tag == "post-up");
        var at = c.PositionOf(post.Slot, post.T);
        Assert.True(ToRim(at) < 8.5, $"the post-up was {ToRim(at):0.0} ft from the rim");
    }

    [Fact]
    public void Pace_and_space_plays_four_out()
    {
        var c = PossessionChoreographer.Build(Input(OffensiveSystem.PaceAndSpace, DefensiveScheme.Man));
        var four = Assert.Single(c.Actions, x => x.Tag == "four-out");
        var f = c.Sample(four.T);
        int outside = Enumerable.Range(0, 5).Count(i => ToRim(f.Players[i]) >= 20.5);
        Assert.True(outside >= 4, $"{outside} beyond the arc");
    }

    [Fact]
    public void Isolation_clears_the_floor_for_the_scorer()
    {
        var c = PossessionChoreographer.Build(Input(OffensiveSystem.Isolation, DefensiveScheme.Man, assisted: false));
        var clear = Assert.Single(c.Actions, x => x.Tag == "clear-out");
        var f = c.Sample(clear.T + 2.5);
        var iso = f.Players[clear.Slot];
        int weak = Enumerable.Range(0, 5).Where(i => i != clear.Slot && i != 0).Count(i => Math.Sign(f.Players[i].Y - 25) == -Math.Sign(iso.Y - 25));
        Assert.True(weak >= 3, $"{weak} cleared to the weak side");
    }

    [Fact]
    public void Balanced_mixes_the_sets()
    {
        var sets = Enumerable.Range(0, 40).Select(i => PossessionChoreographer.Build(Input(OffensiveSystem.Balanced, DefensiveScheme.Man) with { Index = i }).Set).Distinct().ToList();
        Assert.True(sets.Count >= 4, string.Join(", ", sets));
        Assert.DoesNotContain(OffensiveSystem.Balanced, sets);
    }

    [Fact]
    public void The_zone_holds_its_shape_and_man_follows_the_men()
    {
        var zone = PossessionChoreographer.Build(Input(OffensiveSystem.Motion, DefensiveScheme.Zone23));
        Assert.Contains(zone.Actions, x => x.Tag == "zone");
        // Just before the shot, the zone's back line is in the lane and the middle man near the rim.
        double shotT = zone.Ball.First(b => b.Mode == BallMode.Shot).T0;
        var f = zone.Sample(shotT - 0.1);
        // (The whole zone shifts towards the ball, here a corner three.)
        Assert.True(ToRim(f.Players[9]) < 9, $"the zone's middle is {ToRim(f.Players[9]):0.0} ft from the rim");
        // The back line's wing closes out on the corner shooter.
        Assert.True(Enumerable.Range(5, 5).Min(i => f.Players[i].DistanceTo(f.Players[2])) < 10, "the zone's wing steps out towards the corner");
        // Two up top, three along the back.
        double top = Math.Min(f.Players[5].X, f.Players[6].X) ;
        Assert.True(new[] { 7, 8, 9 }.All(i => f.Players[i].X > top + 4), "the back line is behind the top two");
    }

    [Fact]
    public void Drop_sags_the_big_and_switch_trades_men()
    {
        var drop = PossessionChoreographer.Build(Input(OffensiveSystem.PickAndRoll, DefensiveScheme.Drop));
        var d = Assert.Single(drop.Actions, x => x.Tag == "drop");
        Assert.True(ToRim(drop.PositionOf(d.Slot, d.T + 2)) < 13, "the dropping big is in the paint");
        var sw = PossessionChoreographer.Build(Input(OffensiveSystem.PickAndRoll, DefensiveScheme.SwitchEverything));
        Assert.Contains(sw.Actions, x => x.Tag == "switch");
        Assert.Contains(sw.Lines, l => l.Text.Contains("switch it"));
    }

    [Fact]
    public void After_a_stop_the_offence_runs_and_the_defence_gets_back()
    {
        var (_, _) = Teams();
        var input = Input(OffensiveSystem.PickAndRoll, DefensiveScheme.Man, PossessionStart.AfterStop);
        // Start from the other end, as after a defensive rebound.
        var start = input.Start!.ToDictionary(kv => kv.Key, kv => new Spot(12, kv.Value.Y));
        var c = PossessionChoreographer.Build(input with { Start = start });
        var lanes = Assert.Single(c.Actions, x => x.Tag == "lanes");
        Assert.Contains(c.Actions, x => x.Tag == "get-back");
        // The wings fill the wide lanes: one near each sideline as they come down.
        var mid = c.Actions.First(x => x.Tag == "lanes").T + 2.5;
        var f = c.Sample(mid);
        Assert.True(f.Players[1].Y < 12 || f.Players[2].Y < 12, "a wing in the low lane");
        Assert.True(f.Players[1].Y > 38 || f.Players[2].Y > 38, "a wing in the high lane");
        // The defence is back behind the ball (closer to the rim than the handler).
        Assert.True(Enumerable.Range(5, 5).Count(i => ToRim(f.Players[i]) < ToRim(f.Players[0])) >= 4);
        Assert.True(lanes.T >= 0);
    }

    [Fact]
    public void A_tired_player_moves_slower()
    {
        var fresh = PossessionChoreographer.Build(Input(OffensiveSystem.Motion, DefensiveScheme.Man));
        var input = Input(OffensiveSystem.Motion, DefensiveScheme.Man);
        var tiredOne = input.Home[3];
        var tired = PossessionChoreographer.Build(input with { EnergyOf = p => p == tiredOne ? 0.3 : 1.0 });
        int slot = Array.IndexOf(tired.Players, tiredOne);
        // The same walk-up: the tired one reaches his spot later.
        double Arrive(Choreography c) => c.Tracks[slot][1].T;
        Assert.True(Arrive(tired) > Arrive(fresh) + 0.3, $"tired {Arrive(tired):0.00}, fresh {Arrive(fresh):0.00}");
        // Nothing the engine decided moves: the shot leaves at the same moment from the same zone.
        Assert.Equal(fresh.Ball.First(b => b.Mode == BallMode.Shot).T0, tired.Ball.First(b => b.Mode == BallMode.Shot).T0, 6);
    }

    [Fact]
    public void A_substitution_is_a_dead_ball_scene()
    {
        var input = Input(OffensiveSystem.PickAndRoll, DefensiveScheme.Man);
        var (h, _) = Teams();
        var bench = h.Roster.Except(input.Home).First();
        var start = new Dictionary<Player, Spot>(input.Start!);
        // The SG came off for the bench player: he's in the start spots, the newcomer isn't.
        var five = input.Home.ToList();
        var leaving = five[1];
        five[1] = bench;
        var c = PossessionChoreographer.Build(input with { Home = five });
        Assert.Contains(c.Actions, x => x.Tag == "sub");
        Assert.Contains(c.Lines, l => l.Text.Contains("in for") && l.Text.Contains(leaving.LastName));
        var exit = Assert.Single(c.Leaving);
        Assert.Equal(leaving, exit.Who);
        Assert.Single(c.Sample(0.4).Exits);
        Assert.Empty(c.Sample(c.ExitTime + 0.1).Exits);
        // The clock is stopped while they change.
        Assert.Equal(516, c.ClockAt(c.ExitTime), 3);
    }

    [Fact]
    public void A_substitution_after_a_stop_is_not_a_dead_ball()
    {
        var input = Input(OffensiveSystem.PickAndRoll, DefensiveScheme.Man, PossessionStart.AfterStop);
        var (h, _) = Teams();
        var five = input.Home.ToList();
        five[1] = h.Roster.Except(input.Home).First();
        var c = PossessionChoreographer.Build(input with { Home = five });
        Assert.Contains(c.Actions, x => x.Tag == "sub");
        Assert.Contains(c.Actions, x => x.Tag == "lanes");
        // The clock runs from the start: no stopped-clock scene before a push.
        Assert.True(c.ClockAt(1.0) < 516);
        Assert.Contains(c.Lines, l => l.Text.StartsWith("Substitution") && l.T > 0);
    }

    [Fact]
    public void Same_input_draws_the_same_picture()
    {
        var a = PossessionChoreographer.Build(Input(OffensiveSystem.Balanced, DefensiveScheme.Zone23));
        var b = PossessionChoreographer.Build(Input(OffensiveSystem.Balanced, DefensiveScheme.Zone23));
        for (int i = 0; i < 10; i++) Assert.Equal(a.Tracks[i], b.Tracks[i]);
        Assert.Equal(a.Actions, b.Actions);
    }

    /// <summary>Whole games under every system and scheme: the picture still meets each engine play (the shooter in his
    /// zone at the release, the clock never running up), and the box score is the plain simulation's.</summary>
    [Theory]
    [InlineData(OffensiveSystem.PickAndRoll, DefensiveScheme.Drop)]
    [InlineData(OffensiveSystem.Motion, DefensiveScheme.Zone23)]
    [InlineData(OffensiveSystem.PostCentric, DefensiveScheme.SwitchEverything)]
    [InlineData(OffensiveSystem.PaceAndSpace, DefensiveScheme.Man)]
    [InlineData(OffensiveSystem.Isolation, DefensiveScheme.Zone23)]
    [InlineData(OffensiveSystem.Balanced, DefensiveScheme.Man)]
    public void Every_system_and_scheme_plays_a_whole_game_true_to_the_engine(OffensiveSystem system, DefensiveScheme scheme)
    {
        Team h, a;
        (h, a) = Teams(); h.Tactic.System = system; a.Tactic.System = system; h.Tactic.Scheme = scheme; a.Tactic.Scheme = scheme;
        var plain = new GameSimulator().StartLive(h, a, Rules.College, 77); plain.Finish();
        (h, a) = Teams(); h.Tactic.System = system; a.Tactic.System = system; h.Tactic.Scheme = scheme; a.Tactic.Scheme = scheme;
        var live = new GameSimulator().StartLive(h, a, Rules.College, 77);
        Dictionary<Player, Spot>? spots = null;
        var next = PossessionStart.PeriodStart;
        int index = 0, hs = 0, aws = 0, shots = 0, subs = 0;
        var sets = new HashSet<string>();
        while (!live.Complete)
        {
            int before = live.Result.Plays.Count; double elapsed = live.Result.Elapsed;
            live.Step();
            var ev = live.Result.Plays.Skip(before).ToList();
            double dur = live.Result.Elapsed - elapsed;
            double end = ev.Count > 0 ? ev[^1].Clock : live.Clock;
            var c = PossessionChoreographer.Build(new PossessionInput(index++, 77, ev, end + dur, end, live.HomeOnCourt.ToList(), live.AwayOnCourt.ToList(),
                spots, hs, aws, true, h.Name, a.Name, h.Tactic.Scheme, a.Tactic.Scheme, next, live.Result.Periods, false)
            {
                HomeSystem = h.Tactic.System, AwaySystem = a.Tactic.System, EnergyOf = live.EnergyOf,
                RoleOf = p => (h.Roster.Contains(p) ? h : a).Tactic.RoleOf(p),
            });
            foreach (var x in c.Actions) sets.Add(x.Tag);
            subs += c.Leaving.Count;
            // The clock never runs up.
            for (double t = 0; t < c.Length; t += 0.25) Assert.True(c.ClockAt(t + 0.25) <= c.ClockAt(t) + 1e-6);
            // Each field-goal attempt leaves from its shooter, in his zone.
            foreach (var seg in c.Ball.Where(b => b.Mode == BallMode.Shot && !b.FreeThrow))
            {
                var e = ev.FirstOrDefault(x => x.Kind == PlayKind.Shot);
                if (e == null || e.Blocked) continue;
                var at = c.PositionOf(seg.Holder, seg.T0);
                double rimX = c.HomeOffense ? PossessionChoreographer.RimRight : PossessionChoreographer.RimLeft;
                double dist = at.DistanceTo(new Spot(rimX, 25));
                if (e.Zone is ShotZone.CornerThree or ShotZone.AboveBreakThree) Assert.True(dist > 20.5, $"three from {dist:0.0} ft ({c.Set})");
                if (e.Zone == ShotZone.Rim) Assert.True(dist < 6.5, $"rim shot from {dist:0.0} ft ({c.Set})");
                shots++;
                break;
            }
            spots = c.FinalSpots(); next = c.Next;
            var f = c.Sample(c.Length); hs = f.HomeScore; aws = f.AwayScore;
        }
        Assert.Equal(plain.Result.Home.Points, live.Result.Home.Points);
        Assert.Equal(plain.Result.Away.Points, live.Result.Away.Points);
        Assert.Equal((hs, aws), (live.Result.Home.Points, live.Result.Away.Points));
        Assert.True(shots > 60);
        Assert.Contains("to-bench", sets);
        Assert.True(subs > 0, "some substitutions were drawn");
    }

    /// <summary>The live page mid-set: the home side runs pick-and-roll against a 2-3 zone, then the away side the same;
    /// the page shows the set's commentary, stays clean in the layout scan, and draws a substitution's walk-off.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_live_page_shows_the_set_mid_possession()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run149-{Guid.NewGuid():N}.hwsave");
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
            vm.AfterTick();
            var w = window;
            void Settle() { for (int i = 0; i < 5; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }
            void Shot(string name) { if (dir == null) return; Directory.CreateDirectory(dir); using var f = w.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, $"run149-{name}.png")); }
            var home = s.HumanTeam; var away = s.League.Teams.First(t => t != home);
            home.Tactic = new Tactic { Auto = false, System = OffensiveSystem.PickAndRoll, Scheme = DefensiveScheme.Zone23 };
            away.Tactic = new Tactic { Auto = false, System = OffensiveSystem.PaceAndSpace, Scheme = DefensiveScheme.Drop };
            var game = new GameSimulator().StartLive(home, away, s.League.Rules, 314, s.Today);
            var page = new LiveGameViewModel(vm, game, new());
            vm.InLiveGame = true;
            vm.CurrentPage = page; Settle();
            page.PlayPause();
            page.SpeedIndex = 1;
            var seen = new HashSet<string>();
            bool shotSet = false, shotZone = false, shotSub = false;
            for (int i = 0; i < 200_000 && !page.Complete && !(shotSet && shotZone && shotSub); i++)
            {
                page.Play(1.0 / 30);
                var c = page.Script;
                if (c == null) continue;
                foreach (var x in c.Actions) seen.Add(x.Tag);
                double t = page.ScriptTime;
                if (!shotSet && c.HomeOffense && c.Actions.FirstOrDefault(x => x.Tag is "roll" or "pop") is { } roll && t >= roll.T && t < roll.T + 0.6)
                { Settle(); Shot("home-pick-and-roll-v-zone"); Assert.Empty(LayoutCheck.Scan(w).Select(x => x.ToString())); shotSet = true; }
                if (!shotZone && !c.HomeOffense && c.Actions.FirstOrDefault(x => x.Tag == "four-out") is { } four && t >= four.T + 1 && t < four.T + 1.6)
                { Settle(); Shot("away-four-out"); shotZone = true; }
                if (!shotSub && page.MatchFrame?.Exits.Count > 0) { Settle(); Shot("substitution"); shotSub = true; }
            }
            _out.WriteLine($"seen: {string.Join(", ", seen)}; complete {page.Complete}");
            Assert.True(shotSet, "a home pick-and-roll was seen on the live page");
            Assert.True(shotZone, "an away four-out set was seen");
            Assert.Contains("drop", seen);
            Assert.True(shotSub, "a substitution's walk-off was drawn");
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
