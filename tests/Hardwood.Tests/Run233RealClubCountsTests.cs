using System.IO.Compression;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 233 (Danny's `danny-world-leagues-nov26`, the domestic real club counts): the leagues abroad that had fewer places than
/// the real league now have its real 2025-26 size, from the clubs slices 1-3 left out. They're added from their own stream, so
/// the slotted clubs and the college world draw as before; an older save keeps the clubs it has.
/// </summary>
public class Run233RealClubCountsTests
{
    public static readonly (string Key, int Size)[] Sizes =
    {
        (WorldLeagues.Greece, 13), (WorldLeagues.Israel, 14), (WorldLeagues.Poland, 16), (WorldLeagues.Baltic, 14), (WorldLeagues.Czechia, 13),
        (WorldLeagues.Benelux, 18), (WorldLeagues.Argentina, 19), (WorldLeagues.Brazil, 18), (WorldLeagues.Mexico, 14), (WorldLeagues.Japan, 26),
    };

    [Theory]
    [InlineData(31ul)]
    [InlineData(2026ul)]
    public void A_new_world_has_each_league_at_its_real_size(ulong seed)
    {
        var (league, world) = RealWorldData.NewWorld(seed, players: false);
        foreach (var (key, size) in Sizes)
        {
            var comp = world.Competitions.First(c => c.Key == key);
            Assert.Equal(size, comp.Clubs.Count);
            Assert.All(comp.Clubs, t => Assert.NotNull(RealClubs.Of(t)));
            Assert.All(comp.Clubs, t => Assert.True(t.Roster.Count >= 12, $"{t.FullName} has {t.Roster.Count}"));
            // Every club plays everyone home and away (a 13- or 19-club league has a bye each round).
            var games = comp.Season.Fixtures.SelectMany(f => new[] { f.HomeId, f.AwayId }).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
            Assert.All(comp.Clubs, t => Assert.Equal((size - 1) * comp.Profile.RoundRobins, games[t.Id]));
            // The regular season ends before the summer, leaving room for the playoffs.
            Assert.True(comp.Season.Fixtures.Max(f => f.Date) <= new DateOnly(league.Season + 1, 5, 31), $"{key} ends {comp.Season.Fixtures.Max(f => f.Date)}");
        }
        // Unique team and player ids, and the NBA's next id is past every one of them.
        var teams = world.Clubs.Concat(league.Teams).ToList();
        Assert.Equal(teams.Count, teams.Select(t => t.Id).Distinct().Count());
        var players = teams.SelectMany(t => t.Roster).Concat(world.College?.Programs.SelectMany(p => p.Roster) ?? Enumerable.Empty<Hardwood.Core.Model.Player>()).ToList();
        Assert.Equal(players.Count, players.Select(p => p.Id).Distinct().Count());
        Assert.True(league.NextPlayerId > players.Max(p => p.Id));
        // The added clubs bring their own country's players, and some of their arenas are real.
        var walbrzych = world.Competitions.First(c => c.Key == WorldLeagues.Poland).Clubs.Single(t => t.Name == "Górnik");
        Assert.Contains(walbrzych.Roster, p => p.Nationality == "Poland");
        Assert.Equal(2023, walbrzych.ArenaCapacity);
        var aalst = world.Competitions.First(c => c.Key == WorldLeagues.Benelux).Clubs.Single(t => t.Name == "Okapi");
        Assert.Equal("Okapi Forum", RealClubs.ArenaOf(aalst));
        Assert.Equal(10, world.Competitions.First(c => c.Key == WorldLeagues.Benelux).Clubs.Count(t => RealClubs.Of(t)!.Country == "Belgium"));
        Assert.Contains(world.Competitions.First(c => c.Key == WorldLeagues.Japan).Clubs, t => t.FullName == "Gunma Crane Thunders");
    }

    [Fact]
    public void The_slotted_clubs_draw_as_before()
    {
        // The added clubs come last in their league, with the next ids of its block, so the slotted clubs keep their ids and
        // order, and every league's first club matches the profile's first seed.
        var (_, world) = RealWorldData.NewWorld(31, players: false);
        foreach (var (key, size) in Sizes)
        {
            var comp = world.Competitions.First(c => c.Key == key);
            var p = comp.Profile;
            Assert.Equal(p.Clubs.Select(s => s.Name).Concat(p.More.Select(s => s.Name)), comp.Clubs.Select(t => t.Name));
            Assert.Equal(Enumerable.Range(comp.Clubs[0].Id, size), comp.Clubs.Select(t => t.Id));
            // Review: no league repeats a nickname or an abbreviation, the added clubs included (Run107's rule).
            var seeds = p.Clubs.Concat(p.More).ToList();
            Assert.Equal(seeds.Count, seeds.Select(c => c.Name).Distinct().Count());
            Assert.Equal(seeds.Count, seeds.Select(c => c.Abbr).Distinct().Count());
        }
    }

    [Fact]
    public void A_new_career_keeps_the_added_clubs_through_a_save_and_an_older_save_keeps_its_own()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"hw-run233-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var (probe, _) = RealWorldData.NewWorld(31, 2026);
            var path = Path.Combine(dir, "new.hwsave");
            var s = GameSession.NewGame(31, probe.Teams[0].Id, path);
            for (int i = 0; i < 10; i++) s.AdvanceDay();
            s.SaveNow();
            var reloaded = GameSession.Load(path);
            foreach (var (key, size) in Sizes)
                Assert.Equal(size, reloaded.World.Competitions.First(c => c.Key == key).Clubs.Count);
            Assert.Equal(0, RealClubs.Rename(reloaded.World, reloaded.League.Teams));

            var old = Path.Combine(dir, "old.hwsave");
            using (var src = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SaveFixtures", CollegeSaveCompatibilityTests.LaunchFixtureName)), CompressionMode.Decompress))
            using (var dst = File.Create(old)) src.CopyTo(dst);
            var launch = GameSession.Load(old);
            Assert.Equal(12, launch.World.Competitions.First(c => c.Key == WorldLeagues.Greece).Clubs.Count);
            Assert.Equal(16, launch.World.Competitions.First(c => c.Key == WorldLeagues.Japan).Clubs.Count);
            for (int i = 0; i < 3; i++) launch.AdvanceDay();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
