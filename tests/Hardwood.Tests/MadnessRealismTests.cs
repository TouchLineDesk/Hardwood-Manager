using Hardwood.Core;
using Hardwood.Core.Audit;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// C4 step 0, realism first (review #7, decisions #182 and #184-#188): the blue bloods keep their talent, prestige
/// doesn't pile up at 99, college games play on neutral courts at a college pace, and March Madness favourites win
/// about as often as real ones. The full numbers come from <c>hardwood madness</c> (300 tournaments: 1 v 16 95%).
/// </summary>
public class MadnessRealismTests
{
    [Fact]
    public void Computer_schools_only_sign_prospects_within_their_reach_until_spring()
    {
        var nabl = new WorldGenerator(new Rng(9)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(nabl, 9, Array.Empty<string>(), new DateOnly(2026, 10, 1));
        var c = world.College!;
        var ranks = c.PrestigeRanks();
        c.RecruitingThrough(new DateOnly(2027, 2, 28), new Rng(1));
        var autumn = c.ClassOf(2027).Select(p => c.Recruits[p.Id]).Where(r => r.CommittedTo != null && r.CommittedOn >= new DateOnly(2026, 9, 1)).ToList();
        Assert.True(autumn.Count > 50, $"{autumn.Count} commitments before March");
        Assert.All(autumn, r => Assert.True(r.Rank <= CollegeWorld.Reach(ranks[r.CommittedTo!.Value]),
            $"No. {r.Rank} committed to the No. {ranks[r.CommittedTo!.Value]} program (reach {CollegeWorld.Reach(ranks[r.CommittedTo!.Value])})"));
        // The best program waits for the best: nobody below the top 23 in the autumn, and twice that in spring.
        Assert.Equal(23, CollegeWorld.Reach(1));
        Assert.Equal(46, CollegeWorld.Reach(1, 2));
    }

    [Fact]
    public void The_top_programs_stay_stronger_than_the_bubble_and_prestige_does_not_pile_up()
    {
        var (t, _) = MadnessAudit.Run(31, 8);
        Assert.Equal(8, t.Tournaments);
        // Review #7: after year four the top programs (80-84) were barely above the 60th-68th (71).
        Assert.True(t.TopTenPrestigeStrength.Skip(4).Average() > t.BubbleStrength.Skip(4).Average() + 14,
            $"top ten {t.TopTenPrestigeStrength.Skip(4).Average():F1}, bubble {t.BubbleStrength.Skip(4).Average():F1}");
        Assert.True(t.OneSeedStrength.Average() > t.SixteenSeedStrength.Average() + 18,
            $"1-seeds {t.OneSeedStrength.Average():F1}, 16-seeds {t.SixteenSeedStrength.Average():F1}");
        // Review #7: eight programs at prestige 99 after eight seasons.
        Assert.True(t.ProgramsAt99[^1] <= 2, $"{t.ProgramsAt99[^1]} programs at 99");
        Assert.True(t.FiveStarsAtTopTwenty.Average() > 0.6, $"{t.FiveStarsAtTopTwenty.Average():P0} of five-stars at a top-20 program");
        // One scoring scale: quick results and the full engine both near the NCAA's ~72.
        Assert.InRange(t.QuickPoints.Average(), 68, 76);
        Assert.InRange(t.EnginePoints.Average(), 66, 76);
        Assert.Equal(0, t.TitlesBySeed[16]);
    }

    [Fact]
    public void College_events_are_on_neutral_courts_and_talent_gaps_count_for_more()
    {
        var cfg = new EngineConfig();
        var college = cfg.For(Rules.College);
        Assert.Equal(cfg.SkillBeta * Rules.College.TalentScale, college.SkillBeta, 6);
        Assert.Equal(cfg.ContestBeta * Rules.College.TalentScale, college.ContestBeta, 6);
        Assert.Equal(cfg.HomeLogit, college.HomeLogit);
        Assert.Equal(0, cfg.For(Rules.CollegeNeutral).HomeLogit);
        Assert.Same(cfg, cfg.For(Rules.Nba)); // the pros are untouched
        Assert.Same(cfg, cfg.For(Rules.Fiba));
        Assert.Equal(Rules.CollegeNeutral, CollegeWorld.RulesFor(new CollegeGame { Event = CollegeWorld.Madness }));
        Assert.Equal(Rules.College, CollegeWorld.RulesFor(new CollegeGame { Stage = "Conference" }));
    }

    [Fact]
    public void A_full_engine_college_game_scores_at_the_college_level()
    {
        var gen = new PlayerGenerator(new Rng(5), 2_000_000);
        var c = CollegeWorld.Generate(gen, new Rng(5), new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        var rng = new Rng(7);
        var teams = c.Programs.OrderBy(_ => rng.NextDouble()).Take(60).ToList();
        var pts = new List<double>();
        for (int i = 0; i < teams.Count; i += 2)
        {
            var r = sim.Simulate(teams[i], teams[i + 1], Rules.College, rng.NextULong());
            pts.Add((r.Home.Points + r.Away.Points) / 2.0);
        }
        // Review #7: 77-78 a side in March Madness and about 88 in your own games, against 72 in quick results.
        Assert.InRange(pts.Average(), 66, 76);
    }

    [Fact]
    public void Win_chance_never_says_certain_while_the_game_can_still_turn()
    {
        static TeamBox Box() => new() { Team = new Team { City = "A", Name = "B", Abbreviation = "AB", Division = "D" } };
        // Review #7: "100%" at 50-70 with 20 minutes left, and at 77-53 with 10 minutes left in a college game.
        var nabl = new GameResult { Home = Box(), Away = Box(), Periods = 4, PeriodSeconds = 720, MarginSpread = 13 };
        var college = new GameResult { Home = Box(), Away = Box(), Periods = 2, PeriodSeconds = 1200, MarginSpread = 11, PregameEdge = 6 };
        Assert.Equal(0.01, GameFlow.HomeWinProbability(nabl, -20, 20 * 60), 6);
        Assert.Equal(0.99, GameFlow.HomeWinProbability(college, 24, 10 * 60), 6);
        Assert.True(GameFlow.HomeWinProbability(college, 2, 10 * 60) is > 0.5 and < 0.99);
        // Settled: up 10 with 10 seconds left, or the final whistle.
        Assert.True(GameFlow.HomeWinProbability(college, 10, 10) > 0.999);
        Assert.Equal(1, GameFlow.HomeWinProbability(college, 1, 0));
    }
}
