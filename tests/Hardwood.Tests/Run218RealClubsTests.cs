using System.IO.Compression;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 218 (K5 slice 2, Danny's `danny-world-leagues-nov26`): Australia, China, Japan, Korea, the Philippines, Israel, the ABA
/// League and the VTB United League have their real clubs, cities and arenas; each slot keeps its country; older saves are renamed.
/// </summary>
public class Run218RealClubsTests
{
    private static readonly string[] Eight =
    {
        Competitions.Australia, Competitions.China, WorldLeagues.Japan, WorldLeagues.Korea, WorldLeagues.Philippines,
        WorldLeagues.Israel, WorldLeagues.Aba, WorldLeagues.Vtb,
    };

    [Fact]
    public void A_new_world_has_the_real_clubs_of_the_eight_leagues()
    {
        var (_, world) = RealWorldData.NewWorld(31, players: false);
        foreach (var key in Eight)
        {
            var comp = world.Competitions.First(c => c.Key == key);
            Assert.All(comp.Clubs, t => Assert.NotNull(RealClubs.Of(t)));
            Assert.All(comp.Clubs, t => Assert.True(t.Roster.Count >= 12, $"{t.FullName} has {t.Roster.Count}"));
            // Each real club took one made-up club's place in its own league, and the slots keep their countries.
            var p = Competitions.Get(key);
            Assert.Equal(p.Clubs.Length, p.Clubs.Select(c => c.Was).Distinct().Count());
        }
        Club(world, Competitions.Australia, "Sydney Kings", 18200, "Sydney SuperDome");
        Club(world, Competitions.China, "Guangdong Southern Tigers", 16133, "Dongguan Basketball Center");
        Club(world, WorldLeagues.Japan, "Alvark Tokyo", 10000, "Toyota Arena Tokyo");
        Club(world, WorldLeagues.Korea, "Busan KCC Egis", 14099, "Busan Sajik Gymnasium");
        Club(world, WorldLeagues.Philippines, "Barangay Ginebra San Miguel", 14429, "Smart Araneta Coliseum");
        Club(world, WorldLeagues.Israel, "Maccabi Tel Aviv", 10383, "Menora Mivtachim Arena");
        Club(world, WorldLeagues.Aba, "Partizan", 18386, "Belgrade Arena");
        Club(world, WorldLeagues.Vtb, "CSKA Moscow", 13926, "Megasport Arena");
        // The slots keep their countries, so the ABA's Croatian and Macedonian players are still there.
        var aba = world.Competitions.First(c => c.Key == WorldLeagues.Aba);
        Assert.Contains(aba.Clubs.Single(t => t.Name == "MZT").Roster, p => p.Nationality == "North Macedonia");
        Assert.Contains(aba.Clubs.Single(t => t.Name == "Cibona").Roster, p => p.Nationality == "Croatia");
        Assert.DoesNotContain(world.Clubs, t => t.Name is "Harbourmen" or "Pearl River" or "Alvarks" or "Yellows" or "Partisans" or "Army" or "Tide");
    }

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
                int standing = RealClubs.Of(t)!.Standing;
                if (standing >= 5) { giants += top; g++; }
                else if (standing <= -2) { small += top; n++; }
            }
        }
        Assert.True(giants / g > small / n + 5, $"giants {giants / g:0.0}, small clubs {small / n:0.0}");
    }

    [Fact]
    public void An_older_save_gets_the_eight_leagues_real_clubs_on_load()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hw-run218-{Guid.NewGuid():N}");
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
                Assert.All(comp.Clubs, t => Assert.True(RealClubs.Of(t) != null, $"{key}: {t.FullName} not renamed"));
            }
            Assert.Equal(18200, s.World.Competitions.First(c => c.Key == Competitions.Australia).Clubs.Single(t => t.Name == "Kings").ArenaCapacity);
            Assert.Contains(s.World.Competitions.First(c => c.Key == WorldLeagues.Japan).Clubs, t => t.FullName == "SeaHorses Mikawa");
            Assert.Equal(0, RealClubs.Rename(s.World, s.League.Teams));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
