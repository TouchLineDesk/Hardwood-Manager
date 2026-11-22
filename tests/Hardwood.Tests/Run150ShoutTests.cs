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

/// <summary>Run 150, M3 of the 2D match engine (claude/match-engine-2d-design.md C): the ten shouts move the existing
/// levers by their stated amounts and back; the shape on the floor shows them; the commentary names every change; a sub
/// waits with a chip for the next dead ball; and shouts are for one game only.</summary>
[Collection(SharedUiState.Name)]
public class Run150ShoutTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _out;
    public Run150ShoutTests(Xunit.Abstractions.ITestOutputHelper output) => _out = output;

    private static (Team home, Team away) Teams()
    {
        var l = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var h = l.Teams[0]; var a = l.Teams[1];
        h.Tactic = new Tactic { Auto = false }; a.Tactic = new Tactic { Auto = false };
        return (h, a);
    }

    private static List<Player> Five(Team t) => t.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();
    private static readonly Spot Rim = new(PossessionChoreographer.RimRight, 25);

    /// <summary>A home half-court possession: the SF shoots a corner three (made, assisted; or missed and rebounded by
    /// the away centre).</summary>
    private static PossessionInput Input(bool made = true, int threes = 50, int glass = 50, int awayAggression = 50, bool awayDouble = false,
        IReadOnlyList<string>? notes = null)
    {
        var (h, a) = Teams();
        var hf = Five(h); var af = Five(a);
        var ev = new List<PlayEvent> { new(1, 500, PlayKind.Shot, true, hf[2], made ? hf[0] : null, ShotZone.CornerThree, made, made ? 3 : 0, 0) };
        if (!made) ev.Add(new(1, 499, PlayKind.DefensiveRebound, false, af[4], null, ShotZone.Rim, false, 0, 0));
        var start = hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7));
        return new PossessionInput(7, 2026, ev, 516, made ? 500 : 499, hf, af, start, 0, 0, true, "Home", "Away", DefensiveScheme.Man, DefensiveScheme.Man, PossessionStart.DeadBall, 4, false)
        {
            HomeSystem = OffensiveSystem.Motion, HomeThrees = threes, HomeGlass = glass, AwayAggression = awayAggression, AwayDouble = awayDouble,
            Notes = notes ?? Array.Empty<string>(),
        };
    }

    public static IEnumerable<object[]> AllShouts => Enum.GetValues<Shout>().Select(s => new object[] { s });

    /// <summary>Each shout moves its lever by the amount its tooltip states, and cancelling puts it back exactly.</summary>
    [Theory, MemberData(nameof(AllShouts))]
    public void Each_shout_moves_its_lever_and_back(Shout s)
    {
        var t = new Tactic();
        var before = TacticSnapshot.Of(t);
        var active = new List<ActiveShout>();
        Assert.True(Shouts.Toggle(t, active, s));
        var on = TacticSnapshot.Of(t);
        var expected = s switch
        {
            Shout.PushPace => before with { Pace = before.Pace + Shouts.PaceStep },
            Shout.SlowDown => before with { Pace = before.Pace - Shouts.PaceStep },
            Shout.HuntThrees => before with { Threes = before.Threes + Shouts.ThreeStep },
            Shout.AttackPaint => before with { Threes = before.Threes - Shouts.ThreeStep },
            Shout.CrashGlass => before with { Glass = before.Glass + Shouts.GlassStep },
            Shout.GetBack => before with { Glass = before.Glass - Shouts.GlassStep },
            Shout.Pressure => before with { Aggression = before.Aggression + Shouts.AggressionStep },
            Shout.PackPaint => before with { Aggression = before.Aggression - Shouts.AggressionStep },
            Shout.FeedStar => before with { Star = before.Star + Shouts.StarStep },
            _ => before with { Double = true },
        };
        Assert.Equal(expected, on);
        // The tooltip names the amount.
        var info = Shouts.Info(s);
        if (s != Shout.DoubleStar) Assert.Matches(@"\d", info.Tip);
        Assert.False(Shouts.Toggle(t, active, s));
        Assert.Equal(before, TacticSnapshot.Of(t));
        Assert.Empty(active);
    }

    [Fact]
    public void Opposite_shouts_cancel_each_other_and_limits_are_respected()
    {
        var t = new Tactic { Pace = 107, ThreePointEmphasis = 90 };
        var active = new List<ActiveShout>();
        Shouts.Toggle(t, active, Shout.PushPace);
        Assert.Equal(110, t.Pace);   // only 3 left on the slider
        Shouts.Toggle(t, active, Shout.SlowDown);   // turns Push the pace off first
        Assert.Equal(107 - Shouts.PaceStep, t.Pace);
        Assert.Equal(new[] { Shout.SlowDown }, active.Select(a => a.Shout));
        Shouts.Toggle(t, active, Shout.HuntThrees);
        Assert.Equal(100, t.ThreePointEmphasis);
        // A slider moved by hand in between: cancelling takes off only what the shout added.
        t.ThreePointEmphasis = 95;
        Shouts.ClearAll(t, active);
        Assert.Equal(107, t.Pace);
        Assert.Equal(85, t.ThreePointEmphasis);
        Assert.Empty(active);
        // Doubling that was already on stays on when the shout is cancelled.
        var d = new Tactic { DoubleStar = true };
        Shouts.Toggle(d, active, Shout.DoubleStar); Shouts.Toggle(d, active, Shout.DoubleStar);
        Assert.True(d.DoubleStar);
    }

    [Fact]
    public void Hunt_threes_spreads_four_out_and_attack_the_paint_pulls_them_in()
    {
        double Spacing(Choreography c)
        {
            double tag = c.Actions.First(x => x.Tag is "spread" or "attack-paint").T;
            return Enumerable.Range(1, 3).Average(k => c.PositionOf(k, tag).DistanceTo(Rim));
        }
        var normal = PossessionChoreographer.Build(Input());
        Assert.DoesNotContain(normal.Actions, x => x.Tag is "spread" or "attack-paint" or "pressure" or "pack-paint" or "double" or "crash" or "retreat" or "change");
        var spread = PossessionChoreographer.Build(Input(threes: 70));
        var paint = PossessionChoreographer.Build(Input(threes: 30));
        double s = Spacing(spread), p = Spacing(paint);
        _out.WriteLine($"wings and four from the rim: spread {s:0.0} ft, attack {p:0.0} ft");
        Assert.True(s >= 23.4, $"spread {s:0.0}");
        Assert.True(p <= 15.1, $"attack {p:0.0}");
        // The shooter still meets the engine's corner three.
        foreach (var c in new[] { spread, paint })
        {
            var shot = c.Ball.First(b => b.Mode == BallMode.Shot);
            Assert.True(c.PositionOf(shot.Holder, shot.T0).DistanceTo(Rim) > 20.5);
        }
    }

    [Fact]
    public void Crash_the_glass_sends_bodies_in_and_get_back_sends_them_back()
    {
        double Offence(Choreography c)
        {
            var reb = c.Actions.FirstOrDefault(x => x.Tag is "crash" or "retreat");
            double at = (reb?.T ?? c.Length - 1) + PossessionChoreographer.ReboundTime;
            return Enumerable.Range(1, 4).Average(k => c.PositionOf(k, at).DistanceTo(Rim));
        }
        var crash = PossessionChoreographer.Build(Input(made: false, glass: 75));
        var back = PossessionChoreographer.Build(Input(made: false, glass: 25));
        var plain = PossessionChoreographer.Build(Input(made: false));
        Assert.Contains(crash.Actions, x => x.Tag == "crash");
        Assert.Contains(back.Actions, x => x.Tag == "retreat");
        double c = Offence(crash), b = Offence(back), n = Offence(plain);
        _out.WriteLine($"offence from the rim after the miss: crash {c:0.0}, normal {n:0.0}, get back {b:0.0}");
        Assert.True(c < n - 2 && b > n + 8, $"crash {c:0.0} normal {n:0.0} back {b:0.0}");
    }

    [Fact]
    public void Pressure_and_the_double_change_the_defence()
    {
        var press = PossessionChoreographer.Build(Input(awayAggression: 70));
        Assert.Contains(press.Actions, x => x.Tag == "pressure" && x.Slot >= 5);
        Assert.Contains(PossessionChoreographer.Build(Input(awayAggression: 30)).Actions, x => x.Tag == "pack-paint");
        var dbl = PossessionChoreographer.Build(Input(awayDouble: true));
        var tag = Assert.Single(dbl.Actions, x => x.Tag == "double");
        int star = Enumerable.Range(0, 5).OrderByDescending(k => dbl.Players[k].Overall).ThenBy(k => k).First();
        // The helper leaves his man for their best scorer as the walk-up ends.
        double t = tag.T + 0.31;
        var plain = PossessionChoreographer.Build(Input());
        double near = dbl.PositionOf(tag.Slot, t).DistanceTo(dbl.PositionOf(star, t));
        double nearPlain = plain.PositionOf(tag.Slot, t).DistanceTo(plain.PositionOf(star, t));
        _out.WriteLine($"helper {tag.Slot} from the star: doubled {near:0.0} ft, plain {nearPlain:0.0} ft");
        Assert.True(near < 4 && nearPlain > near + 4, $"doubled {near:0.0} plain {nearPlain:0.0}");
    }

    [Fact]
    public void The_commentary_names_each_change_first()
    {
        var before = TacticSnapshot.Of(new Tactic());
        var now = TacticSnapshot.Of(new Tactic { Scheme = DefensiveScheme.Zone23, Pace = 105, DoubleStar = true });
        var said = now.ChangesFrom(before, new[] { Shout.PushPace });
        Assert.Equal("Duke switch to a 2-3 zone, push the pace and send a double at the other team's best scorer", TacticSnapshot.Line("Duke", said));
        // A hand-moved slider of a few points isn't called out; a big one is.
        Assert.Empty(TacticSnapshot.Of(new Tactic { CrashGlass = 55 }).ChangesFrom(before, Array.Empty<Shout>()));
        Assert.Equal(new[] { "send more to the offensive glass" }, TacticSnapshot.Of(new Tactic { CrashGlass = 80 }).ChangesFrom(before, Array.Empty<Shout>()));
        Assert.Equal(new[] { "switch to pace and space" }, TacticSnapshot.Of(new Tactic { System = OffensiveSystem.PaceAndSpace }).ChangesFrom(before, Array.Empty<Shout>()));
        var c = PossessionChoreographer.Build(Input(notes: new[] { "Away switch to a 2-3 zone" }));
        Assert.Equal("Away switch to a 2-3 zone", c.Lines[0].Text);
        Assert.Contains(c.Actions, x => x.Tag == "change");
        Assert.True(c.Lines[1].T >= c.Lines[0].T + 1.9, "the bring-up line waits for the change line");
        // Review (run 150): both benches' changes are read, one after the other.
        var two = PossessionChoreographer.Build(Input(notes: new[] { "Away switch to a 2-3 zone", "Home push the pace" }));
        Assert.Equal("Away switch to a 2-3 zone", two.Sample(0.5).Line);
        Assert.Equal("Home push the pace", two.Sample(PossessionChoreographer.NoteTime + 0.5).Line);
    }

    /// <summary>A whole game with shouts on both benches: the picture never changes a result (the box score is the plain
    /// simulation's with the same levers).</summary>
    [Fact]
    public void A_game_with_shouts_on_is_still_the_engines_game()
    {
        void Shout(Team h, Team a)
        {
            var ah = new List<ActiveShout>(); var aa = new List<ActiveShout>();
            Shouts.Toggle(h.Tactic, ah, Hardwood.Core.Presentation.Shout.HuntThrees); Shouts.Toggle(h.Tactic, ah, Hardwood.Core.Presentation.Shout.CrashGlass);
            Shouts.Toggle(a.Tactic, aa, Hardwood.Core.Presentation.Shout.PackPaint); Shouts.Toggle(a.Tactic, aa, Hardwood.Core.Presentation.Shout.DoubleStar);
            Shouts.Toggle(a.Tactic, aa, Hardwood.Core.Presentation.Shout.GetBack); Shouts.Toggle(h.Tactic, ah, Hardwood.Core.Presentation.Shout.Pressure);
        }
        var (h, a) = Teams(); Shout(h, a);
        var plain = new GameSimulator().StartLive(h, a, Rules.College, 91); plain.Finish();
        (h, a) = Teams(); Shout(h, a);
        var live = new GameSimulator().StartLive(h, a, Rules.College, 91);
        Dictionary<Player, Spot>? spots = null; var next = PossessionStart.PeriodStart; int index = 0;
        var tags = new HashSet<string>();
        while (!live.Complete)
        {
            int before = live.Result.Plays.Count; double elapsed = live.Result.Elapsed;
            live.Step();
            var ev = live.Result.Plays.Skip(before).ToList();
            double end = ev.Count > 0 ? ev[^1].Clock : live.Clock;
            var c = PossessionChoreographer.Build(new PossessionInput(index++, 91, ev, end + live.Result.Elapsed - elapsed, end, live.HomeOnCourt.ToList(), live.AwayOnCourt.ToList(),
                spots, 0, 0, true, h.Name, a.Name, h.Tactic.Scheme, a.Tactic.Scheme, next, live.Result.Periods, false)
            {
                HomeThrees = h.Tactic.ThreePointEmphasis, AwayThrees = a.Tactic.ThreePointEmphasis, HomeGlass = h.Tactic.CrashGlass, AwayGlass = a.Tactic.CrashGlass,
                HomeAggression = h.Tactic.DefensiveAggression, AwayAggression = a.Tactic.DefensiveAggression, HomeDouble = h.Tactic.DoubleStar, AwayDouble = a.Tactic.DoubleStar,
            });
            foreach (var x in c.Actions) tags.Add(x.Tag);
            for (double t = 0; t < c.Length; t += 0.25) Assert.True(c.ClockAt(t + 0.25) <= c.ClockAt(t) + 1e-6);
            spots = c.FinalSpots(); next = c.Next;
        }
        Assert.Equal(plain.Result.Home.Points, live.Result.Home.Points);
        Assert.Equal(plain.Result.Away.Points, live.Result.Away.Points);
        foreach (var tag in new[] { "spread", "crash", "retreat", "pressure", "pack-paint", "double" }) Assert.Contains(tag, tags);
    }

    /// <summary>The live page: every shout is clicked in a live game, the bar shows the active chips, the commentary
    /// names the changes, a sub waits with a chip, the layout stays clean, and Continue puts the levers back.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_live_page_takes_every_shout_and_names_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run150-{Guid.NewGuid():N}.hwsave");
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
            void Shot(string name) { if (dir == null) return; Directory.CreateDirectory(dir); using var f = w.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, $"run150-{name}.png")); }
            var home = s.HumanTeam; var away = s.League.Teams.First(t => t != home);
            home.Tactic = new Tactic { Auto = false };
            away.Tactic = new Tactic { Auto = false };
            var plan = TacticSnapshot.Of(home.Tactic);
            var game = new GameSimulator().StartLive(home, away, s.League.Rules, 271, s.Today);
            var page = new LiveGameViewModel(vm, game, new());
            vm.InLiveGame = true;
            vm.CurrentPage = page; Settle();
            Assert.Equal(10, page.ShoutRows.Count);
            // Click every shout's button on the page (the opposites take each other off), then put on a few to keep.
            var buttons = w.GetVisualDescendants().OfType<Button>().Where(b => b.FindAncestorOfType<ItemsControl>()?.Name == "ShoutButtons").ToList();
            Assert.Equal(10, buttons.Count);
            foreach (var b in buttons) { b.Command!.Execute(b.CommandParameter); Settle(); }
            Assert.Contains(page.ShoutRows, r => r.IsOn);
            foreach (var r in page.ShoutRows.Where(r => r.IsOn).ToList()) page.ToggleShout(r.Key);
            Assert.Equal(plan, TacticSnapshot.Of(home.Tactic));
            page.ToggleShout(((int)Shout.HuntThrees).ToString());
            Assert.Contains("three-point emphasis 50 → 70", page.Status);
            page.ToggleShout(((int)Shout.Pressure).ToString());
            page.ToggleShout(((int)Shout.DoubleStar).ToString());
            Assert.Equal("Hunt threes · Pressure the ball · Double their star", page.ActiveShoutsLine);
            // A sub waiting for the dead ball.
            var bench = page.Lineup[0].Options.First(p => !game.HomeOnCourt.Contains(p) && !game.AwayOnCourt.Contains(p));
            page.Lineup[4].Selected = bench;
            page.ApplyLineup();
            Settle();
            Assert.Contains(bench.LastName, page.SubChip);
            Assert.Contains("next dead ball", page.SubChip);
            Shot("shouts-paused");
            Assert.Empty(LayoutCheck.Scan(w).Select(x => x.ToString()));
            page.PlayPause();
            page.SpeedIndex = 1;
            bool named = false, spread = false, doubled = false, shotSpread = false;
            string team = s.InCollege ? home.City : home.Name;
            for (int i = 0; i < 200_000 && !page.Complete && !(named && spread && doubled && shotSpread); i++)
            {
                page.Play(1.0 / 30);
                var c = page.Script;
                if (c == null) continue;
                if (page.Commentary.StartsWith(team + " ") && page.Commentary.Contains("hunt threes")) named = true;
                if (c.HomeOffense && c.Actions.FirstOrDefault(x => x.Tag == "spread") is { } sp)
                {
                    spread = true;
                    if (!shotSpread && page.ScriptTime >= sp.T + 1.5 && page.ScriptTime < sp.T + 2.2) { Settle(); Shot("hunt-threes-four-out"); Assert.Empty(LayoutCheck.Scan(w).Select(x => x.ToString())); shotSpread = true; }
                }
                if (!c.HomeOffense && c.Actions.Any(x => x.Tag == "double")) doubled = true;
            }
            Assert.True(named, "the commentary bar named the shouts");
            Assert.True(spread && doubled, $"spread {spread}, double {doubled}");
            Assert.Contains(page.Log, l => l.Contains("Hunt threes"));
            Assert.Equal("", page.SubChip);   // the sub went in at a dead ball
            page.SimToEnd();
            Assert.True(home.Tactic.DoubleStar);   // still on through the final whistle
            Assert.True(vm.SaveOnLeave());   // review (run 150): leaving mid-game saves without the shouts (Continue clears them too)
            Assert.Equal(plan, TacticSnapshot.Of(home.Tactic));   // shouts are for one game
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
