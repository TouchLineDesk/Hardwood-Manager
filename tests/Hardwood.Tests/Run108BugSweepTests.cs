using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Names;
using Hardwood.Core.RealWorld;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 108, the third bug sweep (Danny, #883). H62: the generated pyramid carries no real or former NBA, ABA, WNBA or G League
/// nickname (nor the fictional top league's, nor a real club's name like "Charlotte FC"), and every club has a readable, unique
/// code ("SEH", never "SE2", "NON" or "LOS"). H87: no two clubs in the four tiers share a nickname. The names were swapped one
/// for one, so the random numbers, and so the tiers' cities and order, are what they were.
/// </summary>
[Collection(SharedUiState.Name)]   // a name pack sets the static names, app-wide state
public class Run108BugSweepTests
{
    private readonly ITestOutputHelper _out;
    public Run108BugSweepTests(ITestOutputHelper o) => _out = o;

    private static readonly DateOnly On = new(2026, 10, 1);

    private static IEnumerable<CompetitionProfile> Tiers =>
        UsaPyramid.Profiles.Where(p => UsaPyramid.IsNational(p.Key));

    // Real and former nicknames at the top of the game, and the fictional top league's (a sample the old list used).
    private static readonly string[] Taken =
    {
        "Thunder", "Hawks", "Hornets", "Grizzlies", "Raptors", "Mavericks", "Clippers", "Rockets", "Bobcats", "SuperSonics", "Aces",
        "Colonels", "Condors", "Pipers", "Squires", "Vipers", "Wolves", "Toros", "Monarchs", "Comets", "Sparks", "Lynx",
        "Rebels", "Ironmen", "Steamrollers", "Bombers", "Pioneers", "Huskies", "Rangers", "Mariners", "Marlins", "Patriots", "Falcons",
        "Kings", "Lakers", "Nets", "Bulls", "Heat", "Jazz", "Suns", "Spurs", "Celtics", "Knicks", "Pistons", "Pacers", "Bucks",
    };

    [Fact]
    public void The_pyramid_uses_no_taken_nickname_and_no_real_club_name()
    {
        var all = UsaPyramid.Profiles.SelectMany(p => p.Clubs).ToList();
        var fictional = NamePool.Franchises.Select(f => f.Name).ToHashSet();
        var real = RealWorldData.Teams.Concat(RealWorldData.Expansion).Select(t => t.Name).ToHashSet();
        Assert.DoesNotContain(all, c => Taken.Contains(c.Name) || fictional.Contains(c.Name) || real.Contains(c.Name));
        Assert.DoesNotContain(all, c => UsaPyramid.RealStyleNames.Contains($"{c.City} {c.Name}"));
        Assert.DoesNotContain(all, c => c.Name == "City" && c.City.EndsWith(" City"));
        // The hunts' examples.
        var names = all.Select(c => $"{c.City} {c.Name}").ToHashSet();
        foreach (var bad in new[] { "Charlotte FC", "Minneapolis Toros", "San Antonio Hawks", "Detroit Colonels", "Orlando Clippers", "Norman Clippers", "Austin FC" })
            Assert.DoesNotContain(bad, names);
        // Every nickname comes from the game's own lists.
        Assert.All(all.Where(c => !UsaPyramid.ClubStyles.Contains(c.Name)), c => Assert.Contains(c.Name, UsaPyramid.AllNicknames));
    }

    [Fact]
    public void No_two_clubs_in_the_four_tiers_share_a_nickname()
    {
        var clubs = Tiers.SelectMany(p => p.Clubs).ToList();
        Assert.Equal(130, clubs.Count);
        var dup = clubs.Where(c => !UsaPyramid.ClubStyles.Contains(c.Name)).GroupBy(c => c.Name).Where(g => g.Count() > 1)
            .Select(g => string.Join(", ", g.Select(c => $"{c.City} {c.Name}"))).ToList();
        Assert.Empty(dup);
        // Club-style names may repeat, as football's Uniteds do, but never twice in one city.
        Assert.All(clubs.GroupBy(c => c.City), g => Assert.Equal(g.Count(), g.Select(c => c.Name).Distinct().Count()));
    }

    [Fact]
    public void Every_club_has_a_readable_unique_code()
    {
        var clubs = Tiers.SelectMany(p => p.Clubs).ToList();
        var top = NamePool.Franchises.Select(f => f.Abbr).Concat(RealWorldData.Teams.Concat(RealWorldData.Expansion).Select(t => t.Abbr)).ToHashSet();
        Assert.Equal(clubs.Count, clubs.Select(c => c.Abbr).Distinct().Count());
        foreach (var c in clubs)
        {
            Assert.Matches("^[A-Z]{3}$", c.Abbr);
            Assert.Equal(char.ToUpperInvariant(c.City[0]), c.Abbr[0]);
            Assert.DoesNotContain(c.Abbr, top);
        }
        var sea = clubs.First(c => c.City == "Seattle" && c.Name == "Highlanders");
        Assert.Equal("SEH", sea.Abbr);
        _out.WriteLine(string.Join("; ", clubs.Take(32).Select(c => $"{c.Abbr} {c.City} {c.Name}")));
    }

    [Fact]
    public void The_tiers_draw_the_same_cities_in_the_same_order()
    {
        // Tier 2 by market before run 108: the names changed, the random numbers didn't.
        var before = new[]
        {
            "Los Angeles", "Atlanta", "Minneapolis", "New York", "Orlando", "Buffalo", "San Francisco", "Portland", "Dallas", "Boston",
            "Oklahoma City", "Seattle", "St. Louis", "San Antonio", "Pittsburgh", "Denver", "Detroit", "Houston", "Cleveland", "Baltimore",
            "Fort Worth", "Chicago", "Charlotte", "Indianapolis", "Miami", "Phoenix", "Norman", "Syracuse", "Spokane", "Tucson", "Norfolk", "Philadelphia",
        };
        Assert.Equal(before, UsaPyramid.Profiles.First(p => p.Key == UsaPyramid.Tier2).Clubs.Select(c => c.City));
    }

    [Fact]
    public void A_real_career_has_no_numbered_code_and_a_pack_finds_renamed_clubs()
    {
        var league = new WorldGenerator(new Rng(31)).Generate(RealWorldData.LeagueName, 2026, On);
        var world = GameWorld.Generate(league, 31, Array.Empty<string>(), On);
        RealWorldData.Apply(league, world, 31);
        var codes = league.Teams.Concat(world.Clubs.Where(c => world.Competitions.Any(x => UsaPyramid.IsNational(x.Key) && x.Clubs.Contains(c)))).ToList();
        Assert.DoesNotContain(codes, t => t.Abbreviation.Any(char.IsDigit));
        Assert.Equal(codes.Count, codes.Select(t => t.Abbreviation).Distinct().Count());
        Assert.Contains(league.Teams, t => t.Abbreviation == "SEA" && t.Name == "SuperSonics");

        // A pack written for the old name (Charlotte FC) and one for the new name both find the club.
        Assert.Equal("Charlotte FC", UsaPyramid.FormerNames["Charlotte United"]);
        var club = world.Clubs.First(c => c.FullName == "Charlotte United");
        try
        {
            var pack = new NamePack { Name = "t", Clubs = new(StringComparer.OrdinalIgnoreCase) { ["Charlotte FC"] = new PackClub { City = "Charlotte", Name = "Packed" } } };
            pack.ApplyToClubs(league, world);
            Assert.Equal("Packed", club.Name);
            // A career saved before the rename still has "Charlotte FC"; a pack written for the new name finds it.
            club.Name = "FC";
            var pack2 = new NamePack { Name = "t2", Clubs = new(StringComparer.OrdinalIgnoreCase) { ["Charlotte United"] = new PackClub { City = "Charlotte", Name = "Again" } } };
            pack2.ApplyToClubs(league, world);
            Assert.Equal("Again", club.Name);
        }
        finally { NamePack.ResetStatic(); }
    }
}
