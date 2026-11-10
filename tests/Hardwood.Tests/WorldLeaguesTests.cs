using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>The wider world (decisions #111-114): twenty more top flights, two more club cups, every basketball country's players.</summary>
public class WorldLeaguesTests
{
    private static (League nabl, GameWorld world) NewWorld(ulong seed = 4)
    {
        var nabl = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        return (nabl, GameWorld.Generate(nabl, seed, Array.Empty<string>(), new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Twenty_new_leagues_bring_forty_countries_with_a_national_team_pool()
    {
        var (nabl, world) = NewWorld();
        Assert.DoesNotContain(world.Competitions, c => c.Key == Competitions.Guests); // replaced by real leagues
        var added = WorldLeagues.Profiles.Where(p => p.Kind == CompetitionKind.Domestic).ToList();
        Assert.Equal(20, added.Count);
        var on = new DateOnly(2026, 10, 1);
        foreach (var p in added)
        {
            var comp = world[p.Key];
            Assert.InRange(comp.Clubs.Count, 10, 18);
            Assert.All(comp.Clubs, t => Assert.Equal(GameWorld.RosterSize, t.Roster.Count));
            // Mostly home players, within the import limit.
            Assert.All(comp.Clubs, t => Assert.True(GameWorld.Imports(t, p) <= p.ForeignLimit, $"{t.FullName}: {GameWorld.Imports(t, p)} imports"));
            // Abbreviations are unique within a league (clubs are matched to their seeds by them).
            Assert.Equal(comp.Clubs.Count, comp.Clubs.Select(t => t.Abbreviation).Distinct().Count());
        }
        var everyone = nabl.Teams.SelectMany(t => t.Roster).Concat(world.Players).Concat(world.College!.Players).Where(p => p.Age(on) >= 18);
        int nations = everyone.GroupBy(p => p.Nationality).Count(g => g.Count() >= 12);
        Assert.True(nations >= 40, $"{nations} countries have 12 players");
        // Every nationality has a heritage, so names and portrait faces match.
        Assert.All(everyone.Select(p => p.Nationality).Distinct(), n => Assert.Contains(Heritages.All, h => h.Nationalities.Contains(n)));
        // Ids stay below college's.
        Assert.All(world.Clubs, t => Assert.True(t.Id < Hardwood.Core.College.CollegeWorld.FirstId));
    }

    [Fact]
    public void Three_club_cups_take_different_clubs_and_crown_champions()
    {
        var (_, world) = NewWorld();
        var elite = world[Competitions.Elite].Season.Participants;
        var cup = world[WorldLeagues.EuroCup].Season.Participants;
        var americas = world[WorldLeagues.Americas].Season.Participants;
        Assert.Equal(18, elite.Distinct().Count());
        Assert.Equal(16, cup.Distinct().Count());
        Assert.Equal(8, americas.Distinct().Count());
        Assert.Empty(elite.Intersect(cup));
        Assert.All(americas, id => Assert.Equal("Americas", Competitions.RegionOf(world.HomeOf(world.TeamById(id)!).Profile)));

        world.FinishSeasons(new GameSimulator());
        foreach (var key in new[] { Competitions.Elite, WorldLeagues.EuroCup, WorldLeagues.Americas, WorldLeagues.Lithuania, WorldLeagues.Africa })
            Assert.Single(world[key].History);
        // Each league's season (playoffs included) is over by the end of June.
        Assert.All(world.Playing, c => Assert.True(c.Season.Fixtures.Max(f => f.Date) <= new DateOnly(2027, 6, 30), $"{c.Profile.Name} ends {c.Season.Fixtures.Max(f => f.Date)}"));
    }

    [Fact]
    public void Every_competition_has_a_region()
    {
        Assert.All(Competitions.Current, p => Assert.Contains(Competitions.RegionOf(p), Competitions.Regions));
        Assert.Equal("Africa", Competitions.RegionOf(Competitions.Get(WorldLeagues.Africa)));
        Assert.Equal("Europe", Competitions.RegionOf(Competitions.Get(WorldLeagues.Aba)));
    }
}
