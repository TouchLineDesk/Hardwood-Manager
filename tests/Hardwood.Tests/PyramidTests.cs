using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>The USA pyramid (decisions #78-79, #91-94): structure, levels, promotion and relegation, goals without playoffs.</summary>
public class PyramidTests
{
    private static (League nabl, GameWorld world) NewWorld(ulong seed = 3)
    {
        var nabl = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        return (nabl, GameWorld.Generate(nabl, seed, Array.Empty<string>(), new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void The_pyramid_has_its_levels_sizes_and_a_step_down_in_ability()
    {
        var (nabl, world) = NewWorld();
        var pyramid = world.Competitions.Where(c => UsaPyramid.IsPyramid(c.Key)).ToList();
        Assert.Equal(2 + 6 + 16, pyramid.Count);
        Assert.Equal(24, world[UsaPyramid.Tier2].Clubs.Count);
        Assert.Equal(24, world[UsaPyramid.Tier3].Clubs.Count);
        Assert.All(pyramid.Where(c => UsaPyramid.Level(c.Key) == 4), c => Assert.Equal(UsaPyramid.RegionalSize, c.Clubs.Count));
        int total = pyramid.Sum(c => c.Clubs.Count);
        Assert.InRange(total, 360, 400); // about 400 with the NABL (decision #78)

        // No abbreviation is shared with the NABL or another club in the pyramid.
        var abbrs = nabl.Teams.Concat(pyramid.SelectMany(c => c.Clubs)).Select(t => t.Abbreviation).ToList();
        Assert.Equal(abbrs.Count, abbrs.Distinct().Count());

        // Every US pro is at least 23 (decision #75); each level is weaker than the one above.
        var on = new DateOnly(2026, 10, 1);
        Assert.All(pyramid.SelectMany(c => c.Clubs).SelectMany(t => t.Roster), p => Assert.True(p.Age(on) >= 23, $"{p.Name} is {p.Age(on)}"));
        double Level(int l) => pyramid.Where(c => UsaPyramid.Level(c.Key) == l).SelectMany(c => c.Clubs).SelectMany(t => t.Roster).Average(p => p.CurrentAbility);
        double nablCa = nabl.Teams.SelectMany(t => t.Roster).Average(p => p.CurrentAbility);
        Assert.True(nablCa > Level(2) && Level(2) > Level(3) && Level(3) > Level(4) && Level(4) > Level(5),
            $"{nablCa:F0} {Level(2):F0} {Level(3):F0} {Level(4):F0} {Level(5):F0}");
        // Every club has a home area in its region.
        Assert.All(pyramid.Where(c => UsaPyramid.Level(c.Key) == 5).SelectMany(c => c.Clubs.Select(t => (c, t))),
            x => Assert.Equal(x.c.Key, UsaPyramid.LocalKey(UsaPyramid.AreaOf(x.t))));
    }

    [Fact]
    public void Every_pyramid_league_has_regionally_close_divisions()
    {
        var (_, world) = NewWorld();
        foreach (var c in world.Competitions.Where(c => UsaPyramid.IsPyramid(c.Key)))
        {
            var divs = c.Clubs.GroupBy(t => t.Division).ToList();
            Assert.Equal(Math.Min(UsaPyramid.DivisionCount(c.Key, c.Clubs.Count), c.Clubs.Count), divs.Count);
            Assert.True(divs.Max(g => g.Count()) - divs.Min(g => g.Count()) <= 1, $"{c.Key}: {string.Join(", ", divs.Select(g => $"{g.Key} {g.Count()}"))}");
            Assert.DoesNotContain(divs, g => g.Key.Length == 0);
        }
        // The national tiers: East and West, three divisions of four each, named after their clubs' home areas.
        var t2 = world[UsaPyramid.Tier2];
        Assert.Equal(6, t2.Clubs.Select(t => t.Division).Distinct().Count());
        Assert.Equal(12, t2.Clubs.Count(t => t.Conference == Conference.East));
        Assert.All(t2.Clubs.GroupBy(t => t.Division), g => Assert.Single(g.Select(t => t.Conference).Distinct()));
        // A regional league's two divisions are each made of neighbouring areas: no area is split across both unless it must be.
        var ne = world[UsaPyramid.RegionalKey("ne")];
        Assert.Equal(2, ne.Clubs.Select(t => t.Division).Distinct().Count());
        Assert.True(ne.Clubs.GroupBy(UsaPyramid.AreaOf).Count(g => g.Select(t => t.Division).Distinct().Count() > 1) <= 1);
        // Divisions are drawn from the clubs alone, so drawing them again changes nothing (they aren't saved).
        var before = world.Competitions.SelectMany(c => c.Clubs).ToDictionary(t => t.Id, t => (t.Division, t.Conference));
        UsaPyramid.AssignDivisions(world);
        Assert.All(world.Competitions.SelectMany(c => c.Clubs), t => Assert.Equal(before[t.Id], (t.Division, t.Conference)));
    }

    [Fact]
    public void Swaps_match_the_counts_with_runners_up_and_second_to_last()
    {
        Team T(int id) => new() { Id = id, City = "C" + id, Name = "N", Abbreviation = "A" + id, Division = "" };
        List<UsaPyramid.Standing> Div(int first, params int[] wins) => wins.Select((w, i) => new UsaPyramid.Standing(T(first + i), w, 30 - w)).ToList();
        var upper = new List<List<UsaPyramid.Standing>> { Div(0, 25, 20, 10, 5), Div(10, 22, 18, 12, 8) };
        var lower = new List<List<UsaPyramid.Standing>> { Div(20, 24, 20, 3), Div(30, 23, 21, 2), Div(40, 25, 15, 4) };
        var (down, up) = UsaPyramid.Swap(upper, lower);
        // Three division winners below, two divisions above: both lasts go down, plus the worse second-to-last (10 wins).
        Assert.Equal(new[] { 3, 13, 2 }, down.Select(t => t.Id));
        Assert.Equal(new[] { 20, 30, 40 }, up.Select(t => t.Id));
        // The other way round: two winners below, three lasts above → the best runner-up (20 wins) comes up too.
        var (down2, up2) = UsaPyramid.Swap(lower, upper);
        Assert.Equal(new[] { 22, 32, 42 }, down2.Select(t => t.Id));
        Assert.Equal(new[] { 0, 10, 1 }, up2.Select(t => t.Id));
    }

    [Fact]
    public void Promotion_and_relegation_move_clubs_through_every_level()
    {
        var (nabl, world) = NewWorld(5);
        world.FinishSeasons(new GameSimulator());
        // The top league's season: the last team in each of the six divisions goes down (Danny, #319).
        var order = nabl.Teams.OrderBy(t => t.Id).ToList();
        var summary = new SeasonSummary { Season = 2026, Records = order.Select((t, i) => (t.Id, 20 + i, 62 - i)).ToList() };
        nabl.History.Add(summary);
        var down = nabl.Teams.GroupBy(t => t.Division).Select(g => g.OrderBy(t => t.Id).First()).ToList();
        Assert.Equal(6, down.Count);
        var slots = down.Select(t => (t.Conference, t.Division)).OrderBy(x => x).ToList();
        var wagesBefore = down.SelectMany(t => t.Roster).Where(p => p.Contract != null).ToDictionary(p => p.Id, p => p.Contract!.Salary);
        var t2 = world[UsaPyramid.Tier2];
        var t3 = world[UsaPyramid.Tier3];
        List<Team> Winners(WorldCompetition c) => UsaPyramid.Divisions(world, c).Select(d => d[0].Team).ToList();
        List<Team> Lasts(WorldCompetition c) => UsaPyramid.Divisions(world, c).Select(d => d[^1].Team).ToList();
        var up = Winners(t2);
        var t2Down = Lasts(t2);
        var t3Up = Winners(t3);
        var regionalWinners = world.Competitions.Where(c => UsaPyramid.Level(c.Key) == 4).SelectMany(Winners).ToList();
        Assert.Equal(12, regionalWinners.Count);
        var localChamp = world[UsaPyramid.LocalKey("nen")].Season.Table()[0].TeamId;
        var localWinners = Winners(world[UsaPyramid.LocalKey("nen")]);
        int total = world.Competitions.Where(c => UsaPyramid.IsPyramid(c.Key)).Sum(c => c.Clubs.Count);
        var sizes = world.Competitions.Where(c => UsaPyramid.Level(c.Key) is 2 or 3 or 4).ToDictionary(c => c.Key, c => c.Clubs.Count);

        var news = UsaPyramid.Promote(world, nabl, new StaffGenerator(new Rng(1)), new DateOnly(2027, 8, 15));

        Assert.Equal(30, nabl.Teams.Count);
        Assert.All(down, t => Assert.Contains(t, t2.Clubs));
        Assert.All(up, t => Assert.Contains(t, nabl.Teams));
        Assert.All(up, t => Assert.NotEmpty(t.Staff));
        // The promoted clubs fill the vacated places: still five in each of the six divisions.
        Assert.Equal(slots, up.Select(t => (t.Conference, t.Division)).OrderBy(x => x).ToList());
        Assert.All(nabl.Teams.GroupBy(t => t.Division), g => Assert.Equal(5, g.Count()));
        Assert.All(up, t => Assert.True(t.Money.WageBudget > 100_000_000, $"{t.FullName}: {t.Money.WageBudget:N0}")); // NABL money now
        Assert.All(down, t => Assert.True(t.Money.Revenue < 50_000_000)); // tier-2 money
        // Relegation clauses halve the wages; a parachute (half the lost revenue, for two seasons) covers the rest.
        Assert.All(down.SelectMany(t => t.Roster).Where(p => p.Contract != null && wagesBefore.ContainsKey(p.Id)),
            p => Assert.InRange(p.Contract!.Salary, wagesBefore[p.Id] * 0.49, wagesBefore[p.Id] * 0.51));
        Assert.All(down, t => { Assert.True(t.Money.Parachute > 50_000_000); Assert.Equal(2, t.Money.ParachuteYears); });
        Assert.All(down, t => Assert.True(t.Money.Income > ContractMarket.Payroll(t), $"{t.FullName}: income {t.Money.Income:N0} vs payroll {ContractMarket.Payroll(t):N0}"));
        Assert.All(up, t => Assert.Equal(0, t.Money.Parachute));
        Assert.All(t3Up, t => Assert.Contains(t, t2.Clubs));
        Assert.All(t2Down, t => Assert.Contains(t, t3.Clubs));
        // Every regional division winner goes up; tier 3 sends as many down.
        Assert.All(regionalWinners, t => Assert.Contains(t, t3.Clubs));
        Assert.All(localWinners, t => Assert.Contains(t, world[UsaPyramid.RegionalKey("ne")].Clubs));
        Assert.Contains(world.TeamById(localChamp)!, world[UsaPyramid.RegionalKey("ne")].Clubs);
        // The pyramid's sizes stay fixed.
        Assert.All(sizes, kv => Assert.Equal(kv.Value, world[kv.Key].Clubs.Count));
        Assert.Equal(total, world.Competitions.Where(c => UsaPyramid.IsPyramid(c.Key)).Sum(c => c.Clubs.Count)); // nobody lost
        // New divisions for the new season: the relegated clubs have pyramid divisions with their neighbours.
        Assert.All(down, t => Assert.Equal(6, t2.Clubs.Select(x => x.Division).Distinct().Count()));
        Assert.Contains(news, n => n.Title.StartsWith("Promoted to the NABL") || n.Title.StartsWith("Promoted to the NBA")); // the top league is the NBA while a real-world UI test runs alongside (Brand is static)
        // The next season's fixtures include the new arrivals.
        world.StartSeason(2027);
        Assert.All(down, t => Assert.Contains(t.Id, t2.Season.Participants));
    }

    [Fact]
    public void Without_playoffs_the_board_judges_the_final_table()
    {
        var (nabl, world) = NewWorld();
        var comp = world[UsaPyramid.RegionalKey("mw")];
        var ctx = LeagueContext.Of(comp);
        Assert.Equal(34, ctx.SeasonGames);
        var best = comp.Clubs.OrderBy(t => Board.ProjectedRank(ctx, t)).First();
        var mid = comp.Clubs.OrderBy(t => Board.ProjectedRank(ctx, t)).ElementAt(6);
        // Regional and local owners are modest (critic review #4): the favourite aims for the top three, not the title.
        Assert.True(ctx.Grassroots);
        Assert.Equal(SeasonGoal.SecondRound, Board.SetExpectations(ctx, best, 2026).Goal);
        Assert.Equal(SeasonGoal.Playoffs, Board.SetExpectations(ctx, mid, 2026).Goal);
        Assert.Equal("Finish in the top half", Board.Describe(SeasonGoal.Playoffs, ctx));

        world.FinishSeasons(new GameSimulator());
        var table = comp.Season.Table();
        Assert.Equal(SeasonGoal.Title, Board.Achieved(table[0].TeamId, comp.Season, comp.Profile));
        Assert.Equal(SeasonGoal.Playoffs, Board.Achieved(table[5].TeamId, comp.Season, comp.Profile));
        Assert.Equal(SeasonGoal.Develop, Board.Achieved(table[^1].TeamId, comp.Season, comp.Profile));
    }

    [Fact]
    public void You_can_start_in_the_lower_tiers()
    {
        var (_, world) = NewWorld(2026);
        var club = world[UsaPyramid.LocalKey("cal")].Clubs[0];
        var s = GameSession.NewGame(2026, club.Id, null);
        Assert.True(s.Abroad);
        Assert.Equal(5, s.MyLevel);
        Assert.Equal("Golden Coast League", s.HumanLeagueName);
        Assert.InRange(s.HumanTeam.Money.WageBudget, 10_000, 3_000_000); // near-amateur wages
        while (!s.HumanPlaysToday) s.AdvanceDay();
        s.AdvanceDay();
        Assert.Equal(1, s.MyGames().Count(g => g.Played));
    }
}
