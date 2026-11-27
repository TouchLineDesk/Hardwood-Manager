using System.IO.Compression;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 219 (K5 slice 3, Danny's `danny-world-leagues-nov26`): Poland, the Latvian-Estonian league, Czechia, the BNXT League,
/// Argentina, Brazil, Puerto Rico, Mexico and the BAL have their real clubs, cities and arenas; each slot keeps its country; older
/// saves are renamed. The BAL keeps two made-up clubs (Bamako, Kinshasa), whose countries have no recent BAL club.
/// </summary>
public class Run219RealClubsTests
{
    private static readonly string[] Eight =
    {
        WorldLeagues.Poland, WorldLeagues.Baltic, WorldLeagues.Czechia, WorldLeagues.Benelux, WorldLeagues.Argentina,
        WorldLeagues.Brazil, WorldLeagues.PuertoRico, WorldLeagues.Mexico, WorldLeagues.Africa,
    };

    [Fact]
    public void A_new_world_has_the_real_clubs_of_the_nine_leagues()
    {
        var (_, world) = RealWorldData.NewWorld(31, players: false);
        foreach (var key in Eight)
        {
            var comp = world.Competitions.First(c => c.Key == key);
            Assert.All(comp.Clubs, t => Assert.True(RealClubs.Of(t) != null || MadeUp(t), $"{t.FullName} is not real"));
            Assert.All(comp.Clubs, t => Assert.True(t.Roster.Count >= 12, $"{t.FullName} has {t.Roster.Count}"));
            // Each real club took one made-up club's place in its own league, and the slots keep their countries.
            var p = Competitions.Get(key);
            var real = p.Clubs.Where(c => c.Was != null).ToArray();
            Assert.Equal(real.Length, real.Select(c => c.Was).Distinct().Count());
            Assert.Equal(key == WorldLeagues.Africa ? 12 : p.Clubs.Length, real.Length);
        }
        Club(world, WorldLeagues.Poland, "Trefl Sopot", 11409, "Ergo Arena");
        Club(world, WorldLeagues.Baltic, "Kalev/Cramo", 7700, "Tondiraba Sports Center");
        Club(world, WorldLeagues.Benelux, "Filou Oostende", 5000, "Coretec Dôme");
        Club(world, WorldLeagues.Argentina, "Boca Juniors", 2000, "Luis Conde");
        Club(world, WorldLeagues.Brazil, "Flamengo", 11800, "Maracanãzinho");
        Club(world, WorldLeagues.PuertoRico, "Vaqueros de Bayamón", 12000, "Ruben Rodriguez Coliseum");
        Club(world, WorldLeagues.Mexico, "Fuerza Regia de Monterrey", 5000, "Arena Mobil");
        Assert.Contains(world.Competitions.First(c => c.Key == WorldLeagues.Czechia).Clubs, t => t.FullName == "Basketball Nymburk");
        // H133: Jelgava's 14,321-seat hall is gone; Ogre plays in a 1,700-seat arena.
        Assert.Equal(1700, world.Competitions.First(c => c.Key == WorldLeagues.Baltic).Clubs.Single(t => t.Name == "Ogre").ArenaCapacity);
        // The slots keep their countries, so the BAL's Egyptian and Tunisian pairs, and the Baltic's six and six, are still there.
        var bal = world.Competitions.First(c => c.Key == WorldLeagues.Africa);
        Assert.Contains(bal.Clubs.Single(t => t.Name == "Al Ahly").Roster, p => p.Nationality == "Egypt");
        Assert.Contains(bal.Clubs.Single(t => t.Name == "US Monastir").Roster, p => p.Nationality == "Tunisia");
        var baltic = world.Competitions.First(c => c.Key == WorldLeagues.Baltic);
        Assert.Contains(baltic.Clubs.Single(t => t.Name == "TalTech").Roster, p => p.Nationality == "Estonia");
        Assert.DoesNotContain(world.Clubs, t => t.Name is "Silesians" or "Zemgale" or "Vltava" or "Seagulls" or "Porteños" or "Tricolor" or "Cowboys" or "Zonkeys" or "Pharaohs");
    }

    private static bool MadeUp(Hardwood.Core.Model.Team t) => t.FullName is "Bamako Stade" or "Kinshasa Leopards";

    private static void Club(Hardwood.Core.World.GameWorld world, string key, string full, int seats, string arena)
    {
        var t = world.Competitions.First(c => c.Key == key).Clubs.Single(c => c.FullName == full);
        Assert.Equal(seats, t.ArenaCapacity);
        Assert.Equal(arena, RealClubs.ArenaOf(t));
    }

    [Fact]
    public void The_giants_are_stronger_than_the_small_clubs()
    {
        double giants = 0, small = 0; int g = 0, n = 0;
        foreach (ulong seed in new ulong[] { 31, 2026, 77 })
        {
            var (_, world) = RealWorldData.NewWorld(seed, players: false);
            foreach (var key in Eight)
            foreach (var t in world.Competitions.First(c => c.Key == key).Clubs)
            {
                double top = t.Roster.OrderByDescending(p => p.CurrentAbility).Take(8).Average(p => p.CurrentAbility);
                var real = RealClubs.Of(t);
                if (real == null) continue;
                int standing = real.Standing;
                if (standing >= 3) { giants += top; g++; }
                else if (standing <= -2) { small += top; n++; }
            }
        }
        Assert.True(giants / g > small / n + 4, $"giants {giants / g:0.0}, small clubs {small / n:0.0}");
    }

    [Fact]
    public void An_older_save_gets_the_nine_leagues_real_clubs_on_load()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hw-run219-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            using (var src = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SaveFixtures", CollegeSaveCompatibilityTests.LaunchFixtureName)), CompressionMode.Decompress))
            using (var dst = File.Create(path)) src.CopyTo(dst);
            var s = GameSession.Load(path);
            foreach (var key in Eight)
            {
                var comp = s.World.Competitions.First(c => c.Key == key);
                Assert.All(comp.Clubs, t => Assert.True(RealClubs.Of(t) != null || MadeUp(t), $"{key}: {t.FullName} not renamed"));
            }
            Assert.Equal(11409, s.World.Competitions.First(c => c.Key == WorldLeagues.Poland).Clubs.Single(t => t.Name == "Trefl").ArenaCapacity);
            Assert.Equal(1700, s.World.Competitions.First(c => c.Key == WorldLeagues.Baltic).Clubs.Single(t => t.Name == "Ogre").ArenaCapacity);
            Assert.Contains(s.World.Competitions.First(c => c.Key == WorldLeagues.Argentina).Clubs, t => t.FullName == "Argentino de Junín");
            Assert.Equal(0, RealClubs.Rename(s.World, s.League.Teams));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
