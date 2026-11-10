using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Tactics (decisions #58-61): systems, schemes, roles, matchups, presets, the AI coach and saving.</summary>
public class TacticsTests
{
    private static League NewLeague(ulong seed = 3) => new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
    private static readonly DateOnly On = new(2026, 11, 1);

    private static Player With(Player template, params (Attr a, int v)[] set)
    {
        var p = new Player { FirstName = "T", LastName = "P", BirthDate = new DateOnly(2000, 1, 1), PrimaryPosition = template.PrimaryPosition };
        foreach (var a in AttrInfo.All) p.Attributes[a] = 8;
        foreach (var (a, v) in set) p.Attributes[a] = v;
        return p;
    }

    [Fact]
    public void Role_stars_follow_the_attributes_that_matter()
    {
        var any = NewLeague().Teams[0].Roster[0];
        var shooter = With(any, (Attr.ThreePoint, 18), (Attr.CatchAndShoot, 18), (Attr.OffBallMovement, 16), (Attr.FreeThrow, 17));
        Assert.True(Roles.Stars(shooter, Role.SpotUpShooter) >= 4);
        Assert.True(Roles.Stars(shooter, Role.RimProtector) <= 1.5);
        Assert.InRange(Roles.Stars(shooter, Role.FloorGeneral), 0, 5);
        // Every player gets a role natural to his position.
        foreach (var p in NewLeague().Teams.SelectMany(t => t.Roster))
            Assert.True(Roles.Natural(p.PrimaryPosition, Roles.Best(p)));
    }

    [Fact]
    public void Shooters_fit_pace_and_space_better_than_the_post()
    {
        var any = NewLeague().Teams[0].Roster[0];
        var shooters = Enumerable.Range(0, 9).Select(_ => With(any, (Attr.ThreePoint, 17), (Attr.CatchAndShoot, 17), (Attr.Speed, 15), (Attr.Stamina, 15))).ToList();
        Assert.True(Systems.Fit(OffensiveSystem.PaceAndSpace, shooters) > Systems.Fit(OffensiveSystem.PostCentric, shooters));
        Assert.Equal(0.5, Systems.Fit(OffensiveSystem.Balanced, shooters));
        // An average NABL rotation sits near the middle of the scale.
        var league = NewLeague();
        double mean = league.Teams.Average(t => Systems.Fit(OffensiveSystem.Motion, CoachAi.Rotation(t, null)));
        Assert.InRange(mean, 0.3, 0.7);
    }

    private static (double threeShare, double pace, double starShare) Play(Team a, Team b, int games, Rules rules, Player? star = null)
    {
        var sim = new GameSimulator();
        double threes = 0, fga = 0, poss = 0, starFga = 0;
        star ??= b.Roster.MaxBy(p => p.CurrentAbility)!;
        for (int i = 0; i < games; i++)
        {
            var r = sim.Simulate(a, b, rules, (ulong)(100 + i), date: On);
            threes += r.Home.ThreePA; fga += r.Home.FGA; poss += r.Home.Possessions;
            starFga += r.Away.LineFor(star).FGA / (double)Math.Max(1, r.Away.FGA);
        }
        return (threes / fga, poss / games, starFga / games);
    }

    [Fact]
    public void Systems_and_schemes_change_how_games_are_played()
    {
        var league = NewLeague();
        var (a, b) = (league.Teams[0], league.Teams[1]);
        a.Tactic.Auto = false; b.Tactic.Auto = false;

        a.Tactic.System = OffensiveSystem.PaceAndSpace;
        var space = Play(a, b, 40, league.Rules);
        a.Tactic.System = OffensiveSystem.PostCentric;
        var post = Play(a, b, 40, league.Rules);
        Assert.True(space.threeShare > post.threeShare + 0.05, $"{space.threeShare:F3} vs {post.threeShare:F3}");
        Assert.True(space.pace > post.pace + 1, $"{space.pace:F1} vs {post.pace:F1}");

        // Doubling their star cuts his share of their shots.
        a.Tactic.System = OffensiveSystem.Balanced;
        a.Tactic.DoubleStar = false;
        // Their star is whoever takes their shots (the engine's go-to man), not simply their highest-rated player.
        var sim = new GameSimulator();
        var first = sim.Simulate(a, b, league.Rules, 100UL, date: On);
        var star = b.Roster.MaxBy(p => first.Away.LineFor(p).FGA)!;
        var normal = Play(a, b, 40, league.Rules, star);
        a.Tactic.DoubleStar = true;
        var doubled = Play(a, b, 40, league.Rules, star);
        Assert.True(doubled.starShare < normal.starShare, $"{doubled.starShare:F3} vs {normal.starShare:F3}");
    }

    [Fact]
    public void Switching_hurts_pick_and_roll_and_the_zone_stops_the_post()
    {
        Assert.True(Tactics.Matchup(OffensiveSystem.PickAndRoll, DefensiveScheme.SwitchEverything) < 0);
        Assert.True(Tactics.Matchup(OffensiveSystem.PostCentric, DefensiveScheme.Zone23) < 0);
        Assert.True(Tactics.Matchup(OffensiveSystem.Motion, DefensiveScheme.Zone23) > 0);
        Assert.Equal(0, Tactics.Matchup(OffensiveSystem.Balanced, DefensiveScheme.Man));
    }

    [Fact]
    public void The_ai_coach_plays_to_its_roster_and_marks_their_star()
    {
        var league = NewLeague();
        var (a, b) = (league.Teams[4], league.Teams[5]);
        CoachAi.Prepare(a, b, On);
        var rot = CoachAi.Rotation(a, On);
        // The chosen system is at least as good a fit as Balanced (it never picks something its players can't run).
        Assert.True(a.Tactic.System == OffensiveSystem.Balanced || Systems.Fit(a.Tactic.System, rot) >= 0.5);
        Assert.NotNull(a.Tactic.StarStopperId);
        Assert.Contains(a.Roster, p => p.Id == a.Tactic.StarStopperId);
        // A human-run team is left alone.
        b.Tactic.Auto = false; b.Tactic.System = OffensiveSystem.Isolation;
        CoachAi.Prepare(b, a, On);
        Assert.Equal(OffensiveSystem.Isolation, b.Tactic.System);
        Assert.Contains("Assistant's suggestion", CoachAi.ScoutingReport(b, a, On));
        // Across the league, coaches don't all pick the same thing.
        foreach (var t in league.Teams) CoachAi.Prepare(t, league.Teams[(league.Teams.IndexOf(t) + 1) % 30], On);
        Assert.True(league.Teams.Select(t => t.Tactic.System).Distinct().Count() >= 3);
    }

    [Fact]
    public void Presets_round_trip_and_tactics_survive_save_and_load()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-tac-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var s = GameSession.NewGame(2026, 3, path);
            var t = s.HumanTeam.Tactic;
            Assert.False(t.Auto); // the assistant set it up; the human runs it from here
            t.System = OffensiveSystem.PickAndRoll; t.Scheme = DefensiveScheme.Drop; t.ThreePointEmphasis = 70;
            var p = s.HumanTeam.Roster[2];
            t.Roles[p.Id] = Role.GlueGuy;
            t.StarStopperId = s.HumanTeam.Roster[1].Id; t.DoubleStar = true;
            t.Presets.Add(TacticPreset.From(t, "Closing"));
            t.LateGamePreset = "Closing";
            t.System = OffensiveSystem.Motion;
            s.SaveNow();

            var loaded = GameSession.Load(path);
            var lt = loaded.HumanTeam.Tactic;
            Assert.False(lt.Auto);
            Assert.Equal(OffensiveSystem.Motion, lt.System);
            Assert.Equal(DefensiveScheme.Drop, lt.Scheme);
            Assert.Equal(70, lt.ThreePointEmphasis);
            Assert.Equal(Role.GlueGuy, lt.Roles[p.Id]);
            Assert.Equal(s.HumanTeam.Roster[1].Id, lt.StarStopperId);
            Assert.True(lt.DoubleStar);
            Assert.Equal("Closing", lt.LateGamePreset);
            var preset = Assert.Single(lt.Presets);
            Assert.Equal(OffensiveSystem.PickAndRoll, preset.System);
            preset.ApplyTo(lt);
            Assert.Equal(OffensiveSystem.PickAndRoll, lt.System);
            // AI teams stay computer-run after a load.
            Assert.All(loaded.League.Teams.Where(x => x != loaded.HumanTeam), x => Assert.True(x.Tactic.Auto));
            for (int i = 0; i < 12; i++) loaded.AdvanceDay(); // games run with presets and the late-game switch
        }
        finally { Directory.Delete(dir, true); }
    }
}
