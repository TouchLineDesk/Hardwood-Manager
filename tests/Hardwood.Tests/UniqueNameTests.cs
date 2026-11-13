using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>D18 (run 35, review #11 Top fix 3): no two living players share a full name.</summary>
public class UniqueNameTests
{
    private static readonly DateOnly On = new(2026, 10, 1);

    [Theory]
    [InlineData(2026UL, false)]
    [InlineData(77UL, true)]
    public void No_two_living_players_share_a_full_name_in_a_new_world(ulong seed, bool real)
    {
        var league = new WorldGenerator(new Rng(seed)).Generate(real ? RealWorldData.LeagueName : "NABL", 2026, On);
        var world = GameWorld.Generate(league, seed, Array.Empty<string>(), On);
        if (real) RealWorldData.Apply(league, world, seed);
        var everyone = league.AllActivePlayers.Concat(league.FreeAgents).Concat(world.Players).Concat(world.College!.Players).ToList();
        Assert.True(everyone.Count > 10_000, $"{everyone.Count} players");
        var (madeUp, sharing) = NameBook.Census(everyone);
        Assert.True(madeUp > 10_000);
        Assert.Equal(0, sharing);
    }

    [Fact]
    public void A_taken_name_is_rerolled_within_the_heritage_without_moving_ratings()
    {
        var us = Heritages.All.First(h => h.Key == "us");
        var plain = new PlayerGenerator(new Rng(31), 1);
        var unique = new PlayerGenerator(new Rng(31), 1) { Names = new NameBook() };
        var seen = new HashSet<string>();
        int plainRepeats = 0;
        for (int i = 0; i < 3000; i++)
        {
            var a = plain.Generate((Position)(i % 5), 60 + i % 100, 19 + i % 12, On, us, "USA");
            var b = unique.Generate((Position)(i % 5), 60 + i % 100, 19 + i % 12, On, us, "USA");
            if (!seen.Add(NameBook.Key(a.FirstName, a.LastName))) plainRepeats++;
            // Same player apart from the name: the re-roll has its own stream.
            Assert.Equal(a.CurrentAbility, b.CurrentAbility);
            Assert.Equal(a.PotentialAbility, b.PotentialAbility);
            Assert.Equal(a.BirthDate, b.BirthDate);
            Assert.Equal(a.HeightCm, b.HeightCm);
            Assert.Equal(a.Ethnicity, b.Ethnicity);
            Assert.Contains(b.FirstName, us.FirstNames);
            Assert.Contains(b.LastName, us.LastNames);
            Assert.False(RealNames.IsReal(b.FirstName, b.LastName));
        }
        Assert.Equal(3000, unique.Names!.Count);
        Assert.True(plainRepeats > 0, "without a book, 3,000 Americans repeat some names");
    }

    [Fact]
    public void Names_compare_like_real_names_accent_and_case_blind()
    {
        var book = new NameBook();
        Assert.True(book.Add("Luka", "Petrović"));
        Assert.True(book.Taken("luka", "Petrovic"));
        Assert.False(book.Add("LUKA", "Petrovic"));
    }
}
