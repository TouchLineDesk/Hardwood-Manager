using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Every save format the game has ever written still loads, keeps playing, and is backed up before it is upgraded.</summary>
public class SaveCompatibilityTests
{
    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "SaveFixtures"), "career-v*.hwsave").OrderBy(f => f).Select(f => new object[] { Path.GetFileName(f) });

    [Fact]
    public void There_is_a_sample_save_for_every_format_from_2_to_the_current_one()
    {
        var versions = Fixtures().Select(f => int.Parse(((string)f[0])["career-v".Length..^".hwsave".Length])).ToHashSet();
        for (int v = 2; v <= SaveFile.SchemaVersion; v++) Assert.Contains(v, versions);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Old_save_loads_upgrades_keeps_playing_and_is_backed_up(string fixture)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SaveFixtures", fixture), path);
        var original = File.ReadAllBytes(path);
        int version = int.Parse(fixture["career-v".Length..^".hwsave".Length]);
        try
        {
            var s = GameSession.Load(path);
            Assert.Equal(30, s.League.Teams.Count);
            Assert.True(s.League.AllActivePlayers.Count() >= 30 * 13);
            Assert.Equal(5, s.World.Playing.Count());
            Assert.True(s.World.Playing.Sum(c => c.Season.Fixtures.Count(f => f.Played)) > 0); // the world caught up
            Assert.Equal(new DateOnly(2026, 10, 26), s.Today);

            var backup = Path.Combine(dir, $"career.v{version}-backup.hwsave");
            if (version < SaveFile.SchemaVersion)
            {
                Assert.True(File.Exists(backup), "old save should be backed up before the upgrade");
                Assert.Equal(original, File.ReadAllBytes(backup));
                Assert.Contains(s.Inbox, m => m.Title == "Save upgraded");
            }
            else Assert.False(File.Exists(backup));

            var ids = s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.World.Players).Select(p => p.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());

            for (int i = 0; i < 3; i++) s.AdvanceDay();
            var again = GameSession.Load(path);
            Assert.Equal(s.Today, again.Today);
            Assert.Equal(s.Results.Count, again.Results.Count);
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={path};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
            Assert.Equal(SaveFile.SchemaVersion.ToString(), cmd.ExecuteScalar());
            // A second load of the now-current save makes no new backup.
            Assert.Equal(version < SaveFile.SchemaVersion ? 1 : 0, Directory.GetFiles(dir, "*backup*").Length);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }
}
