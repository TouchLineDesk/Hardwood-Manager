using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 21 (#267-#269): every coach is scouted the same way (film or personnel, never the other bench's live settings),
/// the halftime read and the assistant's game plan for manual coaches, and no made-up player with a real NBA name.
/// </summary>
public class ScoutingTests
{
    private static League NewLeague(ulong seed = 3) => new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));

    private static OffensiveSystem NotFit(Team t)
    {
        var fit = CoachAi.BestSystem(CoachAi.Rotation(t, null));
        return Enum.GetValues<OffensiveSystem>().First(s => s != fit);
    }

    [Fact]
    public void A_manual_coach_is_scouted_by_personnel_until_he_is_on_film()
    {
        var lg = NewLeague();
        var us = lg.Teams[0];
        var rot = CoachAi.Rotation(us, null);
        us.Tactic = new Tactic { Auto = false, System = NotFit(us) };
        // Nobody sees his live setting: with no film, he's expected to run what his players suit.
        Assert.Equal(CoachAi.BestSystem(rot), CoachAi.Expected(us, null).system);
        // Three games of the same thing and it's on film.
        for (int i = 0; i < CoachAi.FilmReadGames; i++) CoachAi.Remember(us);
        Assert.Equal(us.Tactic.System, CoachAi.Expected(us, null).system);
        Assert.Equal(us.Tactic.Scheme, CoachAi.Expected(us, null).scheme);
        // A change breaks the read.
        var other = Enum.GetValues<OffensiveSystem>().First(s => s != us.Tactic.System && s != CoachAi.BestSystem(rot));
        us.Tactic.System = other; CoachAi.Remember(us);
        Assert.Equal(CoachAi.BestSystem(rot), CoachAi.Expected(us, null).system);
    }

    [Fact]
    public void Auto_and_manual_teams_are_scouted_alike()
    {
        var lg = NewLeague();
        var a = lg.Teams[0]; var b = lg.Teams[1];
        a.Tactic = new Tactic { Auto = true };
        var asAuto = CoachAi.Expected(a, null);
        a.Tactic = new Tactic { Auto = false, System = NotFit(a) };
        Assert.Equal(asAuto, CoachAi.Expected(a, null));
        // The scouting report and the AI's own read agree.
        Assert.Equal(CoachAi.Expected(b, null), CoachAi.TheirPlan(b, a, null));
    }

    [Fact]
    public void Games_put_both_teams_on_film_and_keep_five()
    {
        var lg = NewLeague();
        var a = lg.Teams[0]; var b = lg.Teams[1];
        var sim = new GameSimulator();
        for (int i = 0; i < 7; i++) sim.Simulate(a, b, lg.Rules, (ulong)(100 + i));
        Assert.Equal(5, a.Tactic.RecentSystems.Count);
        Assert.Equal(5, b.Tactic.RecentSchemes.Count);
    }

    [Fact]
    public void The_assistant_plans_each_game_but_leaves_roles_alone()
    {
        var lg = NewLeague();
        var a = lg.Teams[0]; var b = lg.Teams[1];
        var p = a.Roster[0];
        a.Tactic = new Tactic { Auto = false, AssistantPlans = true, System = NotFit(a) };
        a.Tactic.Roles[p.Id] = Role.SpotUpShooter;
        CoachAi.Prepare(a, b, null);
        Assert.Equal(CoachAi.PlanFor(a, b, null), (a.Tactic.System, a.Tactic.Scheme));
        Assert.Equal(Role.SpotUpShooter, a.Tactic.Roles[p.Id]);
        Assert.False(a.Tactic.Auto);
    }

    [Fact]
    public void The_halftime_read_counters_what_they_really_run_without_touching_saved_tactics()
    {
        var lg = NewLeague();
        var a = lg.Teams[0]; var b = lg.Teams[1];
        var sim = new GameSimulator();
        int adjusted = 0;
        for (int i = 0; i < 12; i++)
        {
            // Start each game in Balanced / man-to-man, which the counter never picks (both carry a penalty in BestSystem / BestScheme),
            // so the read always has something to do (run 48: post / drop became the counter after the retune).
            a.Tactic = new Tactic { Auto = false, HalftimeAdjust = true, System = OffensiveSystem.Balanced, Scheme = DefensiveScheme.Man };
            var r = sim.Simulate(a, b, lg.Rules, (ulong)(900 + i), keepLog: true);
            if (r.PlayLog.Any(l => l.Contains("Halftime adjustment: " + a.Abbreviation))) adjusted++;
            Assert.Equal(OffensiveSystem.Balanced, a.Tactic.System);
            Assert.Equal(DefensiveScheme.Man, a.Tactic.Scheme);
            Assert.DoesNotContain(r.PlayLog, l => l.Contains("Halftime adjustment: " + b.Abbreviation)); // auto doesn't adjust
        }
        Assert.True(adjusted > 0);
        // Switched off: no adjustment.
        a.Tactic = new Tactic { Auto = false, HalftimeAdjust = false, System = OffensiveSystem.Balanced, Scheme = DefensiveScheme.Man };
        Assert.DoesNotContain(sim.Simulate(a, b, lg.Rules, 77, keepLog: true).PlayLog, l => l.Contains("Halftime adjustment"));
    }

    [Fact]
    public void An_older_saved_tactic_loads_with_the_new_options_defaulted()
    {
        var t = System.Text.Json.JsonSerializer.Deserialize<Tactic>("{\"Auto\":false,\"System\":2,\"Scheme\":1}")!;
        Assert.True(t.HalftimeAdjust);
        Assert.False(t.AssistantPlans);
        Assert.Empty(t.RecentSystems);
        Assert.Empty(t.RecentSchemes);
    }

    [Fact]
    public void Real_names_are_known_accent_blind()
    {
        Assert.True(RealNames.Count > 500);
        Assert.True(RealNames.IsReal("Rudy", "Gobert"));
        Assert.True(RealNames.IsReal("Nikola", "Jokic"));
        Assert.True(RealNames.IsReal("Shai", "Gilgeous-Alexander"));
        Assert.True(RealNames.IsReal("Wilt Chamberlain"));
        Assert.False(RealNames.IsReal("Marcus", "Girard"));
    }

    [Fact]
    public void No_made_up_player_or_coach_carries_a_real_nba_name()
    {
        var gen = new PlayerGenerator(new Rng(2051));
        var today = new DateOnly(2026, 10, 1);
        foreach (var h in Heritages.All)
            for (int i = 0; i < 1500; i++)
            {
                var p = gen.Generate((Position)(i % 5), 60 + i % 100, 19 + i % 12, today, h, null);
                Assert.False(RealNames.IsReal(p.FirstName, p.LastName), $"{p.FirstName} {p.LastName} ({h.Key})");
            }
        var lg = NewLeague(2051);
        var staff = new StaffGenerator(new Rng(9));
        for (int i = 0; i < 3000; i++)
        {
            var s = staff.Generate(lg, StaffRole.AssistantCoach, today, 12);
            Assert.False(RealNames.IsReal(s.FirstName, s.LastName), $"{s.FirstName} {s.LastName}");
        }
    }
}
