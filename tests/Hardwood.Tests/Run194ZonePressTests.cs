using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Core.RealWorld;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Slice 4 of the revised launch plan (run 194, #1170): college zone defences (the 2-3, the 1-3-1 and the match-up zone)
/// and the full-court press, as team tactics with engine effects. The pros keep their four schemes and never press.
/// </summary>
public class Run194ZonePressTests
{
    private readonly ITestOutputHelper _out;
    public Run194ZonePressTests(ITestOutputHelper output) => _out = output;

    private static CollegeWorld NewWorld(ulong seed)
    {
        var rng = new Rng(seed);
        var gen = new PlayerGenerator(new Rng(seed ^ 0x51), 2_000_000);
        return CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void The_pros_keep_their_four_schemes_and_never_press()
    {
        Assert.Equal(new[] { DefensiveScheme.Man, DefensiveScheme.SwitchEverything, DefensiveScheme.Zone23, DefensiveScheme.Drop }, Tactics.Schemes(false));
        Assert.Equal(Enum.GetValues<DefensiveScheme>(), Tactics.Schemes(true));
        var lg = RealWorldData.NewWorld(31).League;
        foreach (var t in lg.Teams)
        {
            var rot = CoachAi.Rotation(t, null);
            Assert.Contains(CoachAi.BestScheme(rot), Tactics.Schemes(false));
            Assert.False(CoachAi.Presses(rot, college: false));
        }
        var a = lg.Teams[0]; var b = lg.Teams[1];
        new GameSimulator().Simulate(a, b, Rules.Nba, 7);
        Assert.False(a.Tactic.FullCourtPress || b.Tactic.FullCourtPress);
    }

    [Fact]
    public void College_benches_mostly_play_man_some_zone_and_a_few_press()
    {
        var c = NewWorld(31);
        var picks = c.Programs.Select(t => CoachAi.BestScheme(CoachAi.Rotation(t, null), null, college: true)).ToList();
        double Share(DefensiveScheme d) => picks.Count(x => x == d) / (double)picks.Count;
        int presses = c.Programs.Count(t => CoachAi.Presses(CoachAi.Rotation(t, null), true));
        _out.WriteLine(string.Join(" · ", Enum.GetValues<DefensiveScheme>().Select(d => $"{d} {Share(d):P0}")) + $" · press {presses}/{c.Programs.Count}");
        Assert.InRange(Share(DefensiveScheme.Man), 0.5, 0.85);
        Assert.True(Share(DefensiveScheme.Zone23) >= 0.05, "the 2-3 is the common zone");
        Assert.True(picks.Any(x => x == DefensiveScheme.Zone131) && picks.Any(x => x == DefensiveScheme.MatchUpZone), "the specialist zones are played somewhere");
        Assert.InRange(presses / (double)c.Programs.Count, 0.04, 0.2);
        // Before run 194 every college rotation fit every scheme at 0 against the pros' centres.
        Assert.True(c.Programs.Average(t => Systems.Fit(DefensiveScheme.Zone23, CoachAi.Rotation(t, null), true)) is > 0.35 and < 0.65);
    }

    /// <summary>Two college programs play the same games with and without a press: the pressed side turns it over more,
    /// the game is quicker, and the pressers tire (they play fewer minutes per starter).</summary>
    [Fact]
    public void The_press_forces_turnovers_and_speeds_the_game_up()
    {
        var c = NewWorld(77);
        var ranked = c.Programs.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ToList();
        var press = ranked[20]; var opp = ranked[60];
        press.Tactic.Auto = false; opp.Tactic.Auto = false;
        press.Tactic.Scheme = DefensiveScheme.Man; opp.Tactic.Scheme = DefensiveScheme.Man;
        var sim = new GameSimulator();
        (double tov, double poss, double steals, double fouls) Run(bool pressing)
        {
            press.Tactic.FullCourtPress = pressing;
            double tov = 0, poss = 0, st = 0, pf = 0;
            for (int i = 0; i < 300; i++)
            {
                var r = sim.Simulate(press, opp, Rules.College, (ulong)(1000 + i));
                tov += r.Away.Turnovers; st += r.Home.Steals; pf += r.Home.Fouls;
                poss += r.Away.FGA - r.Away.OReb + r.Away.Turnovers + 0.44 * r.Away.FTA;
                if (pressing) Assert.True(r.PressedPossessions[1] > 0);
                else Assert.Equal(0, r.PressedPossessions[1]);
            }
            return (tov / 300, poss / 300, st / 300, pf / 300);
        }
        var off = Run(false); var on = Run(true);
        _out.WriteLine($"no press: TOV {off.tov:0.0}, poss {off.poss:0.0}, steals {off.steals:0.0}, fouls {off.fouls:0.0}; press: TOV {on.tov:0.0}, poss {on.poss:0.0}, steals {on.steals:0.0}, fouls {on.fouls:0.0}");
        Assert.InRange(on.tov - off.tov, 1.5, 6);
        Assert.True(on.poss > off.poss + 1, "a pressed game is quicker");
        Assert.True(on.steals > off.steals + 1);
        Assert.True(on.fouls > off.fouls);
    }

    [Fact]
    public void The_1_3_1_gives_up_corner_threes_and_offensive_rebounds()
    {
        var c = NewWorld(9);
        var ranked = c.Programs.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ToList();
        var def = ranked[40]; var off = ranked[45];
        def.Tactic.Auto = false; off.Tactic.Auto = false; off.Tactic.Scheme = DefensiveScheme.Man;
        var sim = new GameSimulator();
        (double corner, double orb) Run(DefensiveScheme d)
        {
            def.Tactic.Scheme = d;
            double corner = 0, fga = 0, orb = 0, misses = 0;
            for (int i = 0; i < 300; i++)
            {
                var r = sim.Simulate(def, off, Rules.College, (ulong)(5000 + i));
                corner += r.Away.Lines.Sum(l => l.ZoneAttempts[(int)ShotZone.CornerThree]); fga += r.Away.FGA;
                orb += r.Away.OReb; misses += r.Away.FGA - r.Away.FGM;
            }
            return (corner / fga, orb / misses);
        }
        var man = Run(DefensiveScheme.Man); var z = Run(DefensiveScheme.Zone131);
        _out.WriteLine($"man: corner share {man.corner:P1}, ORB {man.orb:P1}; 1-3-1: {z.corner:P1}, {z.orb:P1}");
        Assert.True(z.corner > man.corner * 1.1);
        Assert.True(z.orb > man.orb);
    }

    [Fact]
    public void The_press_is_kept_in_presets_and_named_on_the_commentary()
    {
        var t = new Tactic { FullCourtPress = true, Scheme = DefensiveScheme.Zone131 };
        var p = TacticPreset.From(t, "havoc");
        var u = new Tactic();
        p.ApplyTo(u);
        Assert.True(u.FullCourtPress);
        Assert.Equal(DefensiveScheme.Zone131, u.Scheme);
        var before = TacticSnapshot.Of(new Tactic());
        var changes = TacticSnapshot.Of(t).ChangesFrom(before, Array.Empty<Shout>());
        Assert.Contains("switch to a 1-3-1 zone", changes);
        Assert.Contains("go to the full-court press", changes);
        Assert.Equal("1-3-1 zone", Systems.Name(DefensiveScheme.Zone131));
        Assert.Equal("Match-up zone", Systems.Name(DefensiveScheme.MatchUpZone));
        Assert.Contains("1-3-1", PlayBook.DefenceNote(DefensiveScheme.Zone131));
    }

    [Fact]
    public void A_1_3_1_stands_one_two_three_one()
    {
        var b = PlayBook.For(OffensiveSystem.Motion)[0];
        var z = PlayBook.Defence(DefensiveScheme.Zone131, b.X, b.Y, 25, 30);
        Assert.Equal(5, z.Length);
        // One at the top, the deepest on the baseline, three across the middle.
        Assert.Equal(1, z.Count(d => d.y > 22));
        Assert.Equal(1, z.Count(d => d.y < 8));
        Assert.Equal(3, z.Count(d => d.y is >= 8 and <= 22));
    }

    [Fact]
    public void A_college_scouting_report_names_a_pressing_opponent()
    {
        var c = NewWorld(31);
        var presser = c.Programs.First(t => CoachAi.Presses(CoachAi.Rotation(t, null), true));
        var us = c.Programs.First(t => t != presser);
        var report = CoachAi.ScoutingReport(us, presser, null, college: true);
        Assert.Contains("press full court", report);
        Assert.DoesNotContain("press full court", CoachAi.ScoutingReport(us, presser, null));
    }
}
