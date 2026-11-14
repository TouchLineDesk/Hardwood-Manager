using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Every real-world save format still loads, keeps playing, and is backed up before it is upgraded.
/// Made-up (NABL) saves are refused since v0.74.0 (D61 B, #478).</summary>
public class SaveCompatibilityTests
{
    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "SaveFixtures"), "real-v*.hwsave").OrderBy(f => f).Select(f => new object[] { Path.GetFileName(f) });

    [Fact]
    public void There_is_a_real_sample_save_for_every_format_since_real_careers_began()
    {
        // Real-world careers started at format 12 (v0.51.0).
        var versions = Fixtures().Select(f => int.Parse(((string)f[0])["real-v".Length..^".hwsave".Length])).ToHashSet();
        for (int v = 12; v <= SaveFile.SchemaVersion; v++) Assert.Contains(v, versions);
    }

    [Fact]
    public void A_made_up_world_save_is_refused_with_a_clear_message_and_left_untouched()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-madeup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SaveFixtures", "madeup-v2.hwsave"), path);
        var original = File.ReadAllBytes(path);
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => GameSession.Load(path));
            Assert.Equal(Hardwood.Core.RealWorld.RealWorldData.MadeUpSaveMessage, ex.Message);
            Assert.Contains("v0.73.0", ex.Message);
            SqliteConnection.ClearAllPools();
            Assert.Equal(original, File.ReadAllBytes(path)); // not upgraded
            Assert.Empty(Directory.GetFiles(dir, "*backup*"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
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
        int version = int.Parse(fixture["real-v".Length..^".hwsave".Length]);
        try
        {
            var s = GameSession.Load(path);
            Assert.True(Hardwood.Core.RealWorld.RealWorldData.IsReal(s.League));
            Assert.Equal(32, s.League.Teams.Count);
            Assert.True(s.League.AllActivePlayers.Count() >= 32 * 13);
            Assert.True(s.World.Playing.Count() >= 5);
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
