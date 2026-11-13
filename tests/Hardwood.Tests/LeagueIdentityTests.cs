using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Danny's league identity answers: D44's names (#411) and D37 C, the real teams' fixed divisions (#412).</summary>
public class LeagueIdentityTests
{
    private static readonly DateOnly On = new(2026, 10, 1);
    private static readonly string[] Real = { "Atlantic", "Central", "Southeast", "Northwest", "Pacific", "Southwest" };

    [Fact]
    public void The_tiers_below_the_NBA_have_Dannys_names_in_real_careers()
    {
        // Read the real-world names without switching the career's world (other tests may be running).
        Assert.Equal(("NBA G League", "G League"), (Competitions.Get(UsaPyramid.Tier2).RealName, Competitions.Get(UsaPyramid.Tier2).RealShort));
        Assert.Equal("American Basketball Association", Competitions.Get(UsaPyramid.Tier3).RealName);
        Assert.Equal("Premier Basketball League", Competitions.Get(UsaPyramid.Tier4).RealName);
        Assert.Equal("National Basketball Conference", Competitions.Get(UsaPyramid.Tier5).RealName);
        // The made-up world keeps its names.
        Assert.Equal("The Championship", Competitions.Get(UsaPyramid.Tier2).BaseName);
        Assert.Equal("National League Three", Competitions.Get(UsaPyramid.Tier5).BaseName);
    }

    private static void AssertRealLayout(League league)
    {
        Assert.Equal(32, league.Teams.Count);
        Assert.All(league.Teams.GroupBy(t => (t.Conference, t.Division)), g => Assert.Equal(4, g.Count()));
        Assert.Equal(8, league.Teams.Select(t => (t.Conference, t.Division)).Distinct().Count());
        foreach (var t in league.Teams)
            if (UsaPyramid.RealHome(t) is { } h)
            {
                Assert.Equal(h.Conf, t.Conference);
                Assert.True(t.Division == h.Division || t.Division == (h.Conf == Conference.East ? "South" : "Midwest"), $"{t.FullName} in {t.Division}");
            }
    }

    [Fact]
    public void A_new_real_career_keeps_the_real_divisions_and_nobody_moves_afterwards()
    {
        var league = new WorldGenerator(new Rng(2026)).Generate(RealWorldData.LeagueName, 2026, On);
        var world = GameWorld.Generate(league, 2026, Array.Empty<string>(), On);
        RealWorldData.Apply(league, world, 2026);
        AssertRealLayout(league);
        // Each real division keeps four of its five teams; the fifth (the one nearest) makes up the extra division.
        Assert.All(Real, d => Assert.Equal(4, league.Teams.Count(t => t.Division == d && UsaPyramid.RealHome(t) is { } h && h.Division == d)));
        Assert.Equal(new[] { "ATL", "IND", "PHI" }, league.Teams.Where(t => t.Division == "South" && UsaPyramid.RealHome(t) != null).Select(t => t.Abbreviation).OrderBy(x => x));
        Assert.Equal(new[] { "MEM", "OKC", "PHX" }, league.Teams.Where(t => t.Division == "Midwest" && UsaPyramid.RealHome(t) != null).Select(t => t.Abbreviation).OrderBy(x => x));

        Hardwood.Core.Jobs.Board.EnsureOwnersAndCoaches(league, 2026);
        Hardwood.Core.Jobs.Board.EnsureOwnersAndCoaches(world.Clubs, 2026);
        var before = league.Teams.ToDictionary(t => t, t => (t.Conference, t.Division));
        var sim = new GameSimulator();
        world.FinishSeasons(sim);
        new Career(new Rng(7)).PlaySeason(league, world: world, worldSim: sim);
        AssertRealLayout(league);
        // Clubs that stayed up kept their division; the eight that came up took the eight openings.
        foreach (var t in league.Teams.Where(before.ContainsKey)) Assert.Equal(before[t], (t.Conference, t.Division));
        Assert.Equal(8, league.Teams.Count(t => !before.ContainsKey(t)));
    }

    [Fact]
    public void An_older_real_career_laid_out_by_geography_gets_the_real_divisions_back()
    {
        var league = new WorldGenerator(new Rng(77)).Generate(RealWorldData.LeagueName, 2026, On);
        var world = GameWorld.Generate(league, 77, Array.Empty<string>(), On);
        RealWorldData.Apply(league, world, 77);
        // As the geographic layout (v0.61–v0.67) once drew it: Brooklyn in the Southeast, Miami in the Atlantic.
        var bkn = league.Teams.First(t => t.Abbreviation == "BKN");
        var mia = league.Teams.First(t => t.Abbreviation == "MIA");
        (bkn.Division, mia.Division) = ("Southeast", "Atlantic");
        UsaPyramid.RealignTop(league, Array.Empty<Team>());
        AssertRealLayout(league);
        Assert.Equal("Atlantic", bkn.Division);
        Assert.Equal("Southeast", mia.Division);
    }
}

/// <summary>Danny's paired divisions (#424): a club drops to, or rises from, the same-named division one tier away.</summary>
public class PairedDivisionTests
{
    private static readonly DateOnly On = new(2026, 10, 1);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Clubs_move_between_same_named_divisions_and_every_division_stays_at_four(bool real)
    {
        var league = new WorldGenerator(new Rng(31)).Generate(real ? RealWorldData.LeagueName : "NABL", 2026, On);
        var world = GameWorld.Generate(league, 31, Array.Empty<string>(), On);
        if (real) RealWorldData.Apply(league, world, 31);
        Hardwood.Core.Jobs.Board.EnsureOwnersAndCoaches(league, 31);
        Hardwood.Core.Jobs.Board.EnsureOwnersAndCoaches(world.Clubs, 31);
        var tiers = UsaPyramid.LowerTiers.Select(k => world.Competitions.First(c => c.Key == k)).ToList();
        Assert.True(UsaPyramid.Balanced(league.Teams));
        Assert.All(tiers, c => Assert.True(UsaPyramid.Balanced(c.Clubs), c.Key));
        string Level(Team t) => league.Teams.Contains(t) ? "top" : world.HomeOf(t).Key;
        var sim = new GameSimulator();
        var career = new Career(new Rng(9));
        for (int season = 0; season < 2; season++)
        {
            var before = league.Teams.Concat(tiers.SelectMany(c => c.Clubs)).ToDictionary(t => t, t => (Level(t), t.Conference, t.Division));
            world.FinishSeasons(sim);
            career.PlaySeason(league, world: world, worldSim: sim);
            Assert.True(UsaPyramid.Balanced(league.Teams));
            Assert.All(tiers, c => Assert.True(UsaPyramid.Balanced(c.Clubs), c.Key));
            int moved = 0;
            foreach (var (t, (lvl, conf, div)) in before)
            {
                // Up, down or staying: a club keeps its conference and division name.
                Assert.Equal((conf, div), (t.Conference, t.Division));
                if (Level(t) != lvl) moved++;
            }
            Assert.Equal(64, moved); // eight up and eight down at each of the four boundaries
        }
    }

    [Fact]
    public void The_lower_tiers_divisions_survive_a_save()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-pair-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var s = GameSession.NewGame(5, 1, path, realWorld: true);
            var t2 = s.World.Competitions.First(c => c.Key == UsaPyramid.Tier2);
            Assert.True(UsaPyramid.Balanced(t2.Clubs));
            // Shuffle two clubs' divisions so a geographic redraw on load would be caught.
            var a = t2.Clubs.First(t => t.Conference == Conference.East && t.Division == "Atlantic");
            var b = t2.Clubs.First(t => t.Conference == Conference.East && t.Division == "South");
            (a.Division, b.Division) = (b.Division, a.Division);
            var labels = t2.Clubs.ToDictionary(t => t.Id, t => (t.Conference, t.Division));
            s.SaveNow();
            var back = GameSession.Load(path);
            var t2b = back.World.Competitions.First(c => c.Key == UsaPyramid.Tier2);
            Assert.All(t2b.Clubs, t => Assert.Equal(labels[t.Id], (t.Conference, t.Division)));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
