using System.IO.Compression;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 217 (K5, Danny's `danny-world-leagues-nov26`): the EuroLeague's seven strongest domestic leagues have their real clubs,
/// cities, arenas and capacities; every league abroad has its real name in a real-world career; the rosters stay generated;
/// an older save's made-up clubs are renamed when it loads.
/// </summary>
public class Run217RealClubsTests
{
    private static readonly string[] Seven = { Competitions.Spain, Competitions.Turkey, WorldLeagues.Greece, WorldLeagues.Italy, WorldLeagues.France, WorldLeagues.Germany, WorldLeagues.Lithuania };

    [Fact]
    public void A_new_world_has_the_real_clubs_names_and_arenas()
    {
        var (league, world) = RealWorldData.NewWorld(31, players: false);
        var spain = world.Competitions.First(c => c.Key == Competitions.Spain);
        Assert.Equal("Liga ACB", spain.Profile.Name);
        Assert.Equal("EuroLeague", world.Competitions.First(c => c.Key == Competitions.Elite).Profile.Name);
        Assert.Equal("EuroCup", world.Competitions.First(c => c.Key == WorldLeagues.EuroCup).Profile.Name);
        Assert.Equal("Basketball Bundesliga", world.Competitions.First(c => c.Key == WorldLeagues.Germany).Profile.Name);

        var barca = spain.Clubs.Single(t => t.Name == "Barça");
        Assert.Equal("FC Barcelona", barca.FullName);
        Assert.Equal("FC Barcelona", barca.TheFullName); // no "the" for a club's own name
        Assert.Equal(7786, barca.ArenaCapacity);
        Assert.Equal("Palau Blaugrana", RealClubs.ArenaOf(barca));
        Assert.Equal("Real Madrid", spain.Clubs.Single(t => t.Abbreviation == "RMA").FullName);
        var hamburg = world.Competitions.First(c => c.Key == WorldLeagues.Germany).Clubs.Single(t => t.City == "Hamburg");
        Assert.Equal("the Hamburg Towers", hamburg.TheFullName);
        Assert.Equal("Zalgiris Kaunas", world.Competitions.First(c => c.Key == WorldLeagues.Lithuania).Clubs.Single(t => t.Abbreviation == "ZAL").FullName);

        // Every club of the seven is a real one, and the rosters are still full and generated.
        foreach (var key in Seven)
        {
            var comp = world.Competitions.First(c => c.Key == key);
            Assert.All(comp.Clubs, t => Assert.NotNull(RealClubs.Of(t)));
            Assert.All(comp.Clubs, t => Assert.True(t.Roster.Count >= 12, $"{t.FullName} has {t.Roster.Count}"));
        }
        // No made-up name survives in them.
        Assert.DoesNotContain(world.Clubs, t => t.Name is "Capitales" or "Kartallar" or "Shamrocks" or "Green Knights" or "Olimpians" or "Albatrosses" or "Seine");
    }

    [Fact]
    public void Abbreviations_and_names_stay_unique_across_the_world()
    {
        var (league, world) = RealWorldData.NewWorld(2026, players: false);
        // The clubs abroad (outside the USA pyramid) and the NBA: the studio finds an NBA arena by abbreviation, so Baskonia isn't BKN.
        var all = world.Competitions.Where(c => !UsaPyramid.IsPyramid(c.Key) && c.Profile.Kind == CompetitionKind.Domestic).SelectMany(c => c.Clubs).Concat(league.Teams).ToList();
        var dupAbbr = all.GroupBy(t => t.Abbreviation).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupAbbr.Count == 0, "duplicate abbreviations: " + string.Join(", ", dupAbbr));
        var dupName = all.GroupBy(t => t.FullName).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupName.Count == 0, "duplicate names: " + string.Join(", ", dupName));
        // Each real club replaced one made-up club in its own league.
        foreach (var key in Seven)
        {
            var p = Competitions.Get(key);
            Assert.All(p.Clubs, c => Assert.NotNull(c.Was));
            Assert.Equal(p.Clubs.Length, p.Clubs.Select(c => c.Was).Distinct().Count());
        }
    }

    [Fact]
    public void The_giants_are_stronger_than_the_newcomers()
    {
        double giants = 0, small = 0; int g = 0, n = 0;
        foreach (ulong seed in new ulong[] { 31, 2026, 77 })
        {
            var (_, world) = RealWorldData.NewWorld(seed, players: false);
            foreach (var key in Seven)
            foreach (var t in world.Competitions.First(c => c.Key == key).Clubs)
            {
                double top = t.Roster.OrderByDescending(p => p.CurrentAbility).Take(8).Average(p => p.CurrentAbility);
                int standing = RealClubs.Of(t)!.Standing;
                if (standing >= 5) { giants += top; g++; }
                else if (standing <= -2) { small += top; n++; }
            }
        }
        Assert.True(giants / g > small / n + 5, $"giants {giants / g:0.0}, small clubs {small / n:0.0}");
    }

    [Fact]
    public void An_older_save_gets_the_real_clubs_on_load()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hw-run217-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            using (var src = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SaveFixtures", CollegeSaveCompatibilityTests.LaunchFixtureName)), CompressionMode.Decompress))
            using (var dst = File.Create(path)) src.CopyTo(dst);
            var s = GameSession.Load(path);
            var spain = s.World.Competitions.First(c => c.Key == Competitions.Spain);
            Assert.Contains(spain.Clubs, t => t.FullName == "Real Madrid");
            Assert.Contains(spain.Clubs, t => t.FullName == "Força Lleida"); // took Sevilla's place
            Assert.DoesNotContain(s.World.Clubs, t => t.Name is "Capitales" or "Kartallar" or "Green Knights");
            Assert.All(spain.Clubs, t => Assert.True(t.Roster.Count >= 8, $"{t.FullName} {t.Roster.Count}"));
            Assert.Equal(15415, s.World.Competitions.First(c => c.Key == WorldLeagues.Lithuania).Clubs.Single(t => t.Name == "Zalgiris").ArenaCapacity);
            Assert.Equal("BAS", spain.Clubs.Single(t => t.Name == "Baskonia").Abbreviation);
            // A second load changes nothing more.
            Assert.Equal(0, RealClubs.Rename(s.World, s.League.Teams));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
