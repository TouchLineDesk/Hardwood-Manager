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
        // Danny's D33 design (#344, #355): 32 clubs in each pro tier, the top league too (two strongest tier-2 clubs joined it).
        Assert.Equal(32, nabl.Teams.Count);
        Assert.Equal(32, world[UsaPyramid.Tier2].Clubs.Count);
        Assert.Equal(32, world[UsaPyramid.Tier3].Clubs.Count);
        Assert.All(pyramid.Where(c => UsaPyramid.Level(c.Key) == 4), c => Assert.Equal(UsaPyramid.RegionalSize, c.Clubs.Count));
        // Two conferences of four divisions of four in the top league.
        Assert.Equal(16, nabl.Teams.Count(t => t.Conference == Conference.East));
        Assert.All(nabl.Teams.GroupBy(t => (t.Conference, t.Division)), g => Assert.Equal(4, g.Count()));
        Assert.Equal(8, nabl.Teams.Select(t => t.Division).Distinct().Count());
        Assert.All(nabl.Teams.Skip(30), t => Assert.NotEmpty(t.Staff));
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
    public void Promotion_and_relegation_move_clubs_through_every_level()
    {
        var (nabl, world) = NewWorld(5);
        world.FinishSeasons(new GameSimulator());
        // The top league's season: the 4th-placed (last) team in each of the eight divisions goes down (Danny, #319, #344).
        var order = nabl.Teams.OrderBy(t => t.Id).ToList();
        var summary = new SeasonSummary { Season = 2026, Records = order.Select((t, i) => (t.Id, 20 + i, 62 - i)).ToList() };
        nabl.History.Add(summary);
        var down = nabl.Teams.GroupBy(t => t.Division).Select(g => g.OrderBy(t => t.Id).First()).ToList();
        Assert.Equal(8, down.Count);
        var wagesBefore = down.SelectMany(t => t.Roster).Where(p => p.Contract != null).ToDictionary(p => p.Id, p => p.Contract!.Salary);
        var t2 = world[UsaPyramid.Tier2];
        // Each tier-2 and tier-3 division winner goes up; the divisions are regional, eight of four.
        List<Team> Winners(WorldCompetition c)
        {
            var table = c.Season.Table().Select(r => world.TeamById(r.TeamId)!).ToList();
            var divs = UsaPyramid.Divisions(c.Key, c.Clubs);
            Assert.Equal(8, divs.Count);
            Assert.All(divs, d => Assert.Equal(4, d.Teams.Count));
            Assert.Equal(4, divs.Count(d => d.Name.StartsWith("East · ")));
            return divs.Select(d => table.First(d.Teams.Contains)).ToList();
        }
        var up = Winners(t2);
        var t3Up = Winners(world[UsaPyramid.Tier3]);
        var t2Last = UsaPyramid.Divisions(t2.Key, t2.Clubs).Select(d => t2.Season.Table().Select(r => world.TeamById(r.TeamId)!).Last(d.Teams.Contains)).ToList();
        var regionalChamp = world[UsaPyramid.RegionalKey("ne")].Season.Table()[0].TeamId;
        var localChamp = world[UsaPyramid.LocalKey("nen")].Season.Table()[0].TeamId;
        int total = world.Competitions.Where(c => UsaPyramid.IsPyramid(c.Key)).Sum(c => c.Clubs.Count);

        var news = UsaPyramid.Promote(world, nabl, new StaffGenerator(new Rng(1)), new DateOnly(2027, 8, 15));

        Assert.Equal(32, nabl.Teams.Count);
        Assert.All(down, t => Assert.Contains(t, t2.Clubs));
        Assert.All(up, t => Assert.Contains(t, nabl.Teams));
        Assert.All(up, t => Assert.NotEmpty(t.Staff));
        // Room to sign at the new level (#356): players well below a typical top-league 10th man go, keeping at least nine.
        Assert.All(up, t => Assert.InRange(t.Roster.Count, 9, 15));
        Assert.All(up, t => Assert.True(t.Money.WageBudget > 100_000_000, $"{t.FullName}: {t.Money.WageBudget:N0}")); // NABL money now
        Assert.All(down, t => Assert.True(t.Money.Revenue < 50_000_000)); // tier-2 money
        // Relegation clauses halve the wages; a parachute (half the lost revenue, for two seasons) covers the rest.
        Assert.All(down.SelectMany(t => t.Roster).Where(p => p.Contract != null && wagesBefore.ContainsKey(p.Id)),
            p => Assert.InRange(p.Contract!.Salary, wagesBefore[p.Id] * 0.49, wagesBefore[p.Id] * 0.51));
        Assert.All(down, t => { Assert.True(t.Money.Parachute > 50_000_000); Assert.Equal(2, t.Money.ParachuteYears); });
        Assert.All(down, t => Assert.True(t.Money.Income > ContractMarket.Payroll(t), $"{t.FullName}: income {t.Money.Income:N0} vs payroll {ContractMarket.Payroll(t):N0}"));
        Assert.All(up, t => Assert.Equal(0, t.Money.Parachute));
        Assert.All(t3Up, t => Assert.Contains(t, t2.Clubs));
        Assert.All(t2Last, t => Assert.Contains(t, world[UsaPyramid.Tier3].Clubs));
        // Every top-league division is still four strong, sixteen a conference.
        Assert.All(nabl.Teams.GroupBy(t => (t.Conference, t.Division)), g => Assert.Equal(4, g.Count()));
        Assert.Equal(8, nabl.Teams.Select(t => t.Division).Distinct().Count());
        Assert.Equal(32, t2.Clubs.Count);
        Assert.Equal(32, world[UsaPyramid.Tier3].Clubs.Count);
        Assert.Contains(world.TeamById(regionalChamp)!, world[UsaPyramid.Tier3].Clubs);
        Assert.Contains(world.TeamById(localChamp)!, world[UsaPyramid.RegionalKey("ne")].Clubs);
        Assert.All(world.Competitions.Where(c => UsaPyramid.Level(c.Key) == 4), c => Assert.Equal(UsaPyramid.RegionalSize, c.Clubs.Count));
        Assert.Equal(total, world.Competitions.Where(c => UsaPyramid.IsPyramid(c.Key)).Sum(c => c.Clubs.Count)); // nobody lost
        Assert.Contains(news, n => n.Title.StartsWith("Promoted to the NABL") || n.Title.StartsWith("Promoted to the NBA")); // the top league is the NBA while a real-world UI test runs alongside (Brand is static)
        // The next season's fixtures include the new arrivals.
        world.StartSeason(2027);
        Assert.All(down, t => Assert.Contains(t.Id, t2.Season.Participants));
    }

    [Fact]
    public void An_older_world_grows_to_thirty_two_in_each_pro_tier_at_its_next_summer()
    {
        // Rebuild the shape of a save from before Danny's D33 design (#344, #355): a top league of 30 in six divisions of five,
        // tiers 2 and 3 of 24.
        var (nabl, world) = NewWorld(7);
        var t2 = world[UsaPyramid.Tier2]; var t3 = world[UsaPyramid.Tier3];
        foreach (var t in nabl.Teams.Skip(30).ToList()) { nabl.Teams.Remove(t); t2.Clubs.Add(t); }
        string[] old = { "Atlantic", "Central", "Southeast", "Northwest", "Pacific", "Southwest" };
        for (int i = 0; i < 30; i++) { nabl.Teams[i].Conference = i < 15 ? Conference.East : Conference.West; nabl.Teams[i].Division = old[i / 5]; }
        foreach (var t in t2.Clubs.OrderBy(GameWorld.Strength).Take(10).ToList()) { t2.Clubs.Remove(t); t3.Clubs.Add(t); }
        foreach (var t in t3.Clubs.OrderBy(GameWorld.Strength).Take(18).ToList())
        { t3.Clubs.Remove(t); world[UsaPyramid.RegionalKey(UsaPyramid.RegionOf(t))].Clubs.Add(t); }
        Assert.Equal((30, 24, 24), (nabl.Teams.Count, t2.Clubs.Count, t3.Clubs.Count));
        world.Index();
        world.StartSeason(2026);
        world.FinishSeasons(new GameSimulator());
        nabl.History.Add(new SeasonSummary { Season = 2026, Records = nabl.Teams.Select((t, i) => (t.Id, 20 + i, 62 - i)).ToList() });
        int total = world.Competitions.Where(c => UsaPyramid.IsPyramid(c.Key)).Sum(c => c.Clubs.Count) + nabl.Teams.Count;

        var news = UsaPyramid.Promote(world, nabl, new StaffGenerator(new Rng(1)), new DateOnly(2027, 8, 15));

        Assert.Equal((32, 32, 32), (nabl.Teams.Count, t2.Clubs.Count, t3.Clubs.Count));
        Assert.All(world.Competitions.Where(c => UsaPyramid.Level(c.Key) == 4), c => Assert.Equal(UsaPyramid.RegionalSize, c.Clubs.Count));
        Assert.Equal(total, world.Competitions.Where(c => UsaPyramid.IsPyramid(c.Key)).Sum(c => c.Clubs.Count) + nabl.Teams.Count);
        Assert.All(nabl.Teams.GroupBy(t => (t.Conference, t.Division)), g => Assert.Equal(4, g.Count()));
        Assert.Equal(16, nabl.Teams.Count(t => t.Conference == Conference.East));
        Assert.All(nabl.Teams, t => Assert.NotEmpty(t.Staff));
        Assert.Contains(news, n => n.Body.Contains("grows to 32"));
        // The next season's schedule: 82 games each, 41 at home, 4 against each division rival.
        var schedule = ScheduleBuilder.Build(nabl, new DateOnly(2027, 10, 21), new Rng(4));
        Assert.All(nabl.Teams, t =>
        {
            var mine = schedule.Where(g => g.Home == t || g.Away == t).ToList();
            Assert.Equal(82, mine.Count);
            Assert.Equal(41, mine.Count(g => g.Home == t));
            foreach (var rival in nabl.Teams.Where(x => x != t && x.Division == t.Division))
                Assert.Equal(4, mine.Count(g => g.Home == rival || g.Away == rival));
            foreach (var other in nabl.Teams.Where(x => x.Conference != t.Conference))
                Assert.Equal(2, mine.Count(g => g.Home == other || g.Away == other));
        });
    }

    [Fact]
    public void The_real_top_league_keeps_its_conferences_in_eight_regional_divisions()
    {
        var (nabl, _) = NewWorld(11);
        var east = nabl.Teams.Where(t => t.Conference == Conference.East).Select(t => t.City).ToHashSet();
        // The pro divisions are regional: Boston and New York together; Los Angeles in the Pacific.
        var bos = nabl.Teams.First(t => t.City == "Boston"); var nyk = nabl.Teams.First(t => t.City == "New York");
        Assert.Equal(bos.Division, nyk.Division);
        Assert.Equal("Atlantic", bos.Division);
        Assert.Contains(nabl.Teams, t => t.City == "Los Angeles" && t.Division == "Pacific");
        Assert.All(nabl.Teams.Where(t => t.Conference == Conference.East), t => Assert.Contains(t.Division, UsaPyramid.EastDivisions));
        Assert.All(nabl.Teams.Where(t => t.Conference == Conference.West), t => Assert.Contains(t.Division, UsaPyramid.WestDivisions));
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
