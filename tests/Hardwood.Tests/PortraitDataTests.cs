using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Data the player-card portraits need: ethnicity (face folder), nationality, draft record.</summary>
public class PortraitDataTests
{
    private static League NewLeague(ulong seed = 11) =>
        new WorldGenerator(new Rng(seed)).Generate("Test League", 2026, new DateOnly(2026, 10, 1));

    [Fact]
    public void Generated_players_have_matching_heritage()
    {
        var league = NewLeague();
        var players = league.AllActivePlayers.ToList();
        foreach (var p in players)
        {
            var h = Heritages.All.Single(x => x.Nationalities.Contains(p.Nationality));
            Assert.Contains(p.FirstName, h.FirstNames);
            Assert.Contains(p.LastName, h.LastNames);
            Assert.Contains(h.Ethnicities, e => e.Ethnicity == p.Ethnicity);
        }
        // A North American league: mostly USA, but not only.
        double usa = players.Count(p => p.Nationality == "USA") / (double)players.Count;
        Assert.InRange(usa, 0.55, 0.85);
        Assert.True(players.Select(p => p.Ethnicity).Distinct().Count() >= 4);
    }

    [Fact]
    public void Every_ethnicity_maps_to_a_face_pack_folder()
    {
        string[] folders = { "African", "Asian", "Caucasian", "Central European", "EECA", "Italmed", "MENA", "MESA", "SAMed", "Scandinavian", "Seasian", "South American" };
        Assert.Equal(folders.OrderBy(x => x), Enum.GetValues<Ethnicity>().Select(EthnicityInfo.FolderName).OrderBy(x => x));
        foreach (var e in Enum.GetValues<Ethnicity>())
            Assert.Equal(e, EthnicityInfo.FromFolderName(EthnicityInfo.FolderName(e)));
    }

    [Fact]
    public void Draft_record_and_ethnicity_survive_save_and_load()
    {
        var league = NewLeague();
        var path = Path.Combine(Path.GetTempPath(), $"hw-portrait-{Guid.NewGuid():N}.save");
        try
        {
            var played = new Career(new Rng(3), new SaveFile(path)).PlaySeason(league);
            var first = played.Offseason.Draft.First();
            Assert.Equal(new DraftRecord(2027, 1, 1), first.Player.Draft);

            var loaded = new SaveFile(path).Load();
            var all = loaded.AllActivePlayers.Concat(loaded.FreeAgents).ToDictionary(p => p.Id);
            Assert.Equal(new DraftRecord(2027, 1, 1), all[first.Player.Id].Draft);
            foreach (var p in league.AllActivePlayers)
            {
                var q = all[p.Id];
                Assert.Equal(p.Ethnicity, q.Ethnicity);
                Assert.Equal(p.Nationality, q.Nationality);
                Assert.Equal(p.Draft, q.Draft);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Version_2_save_without_new_columns_still_loads()
    {
        var league = NewLeague();
        var path = Path.Combine(Path.GetTempPath(), $"hw-v2-{Guid.NewGuid():N}.save");
        try
        {
            new SaveFile(path).Save(league);
            // Rebuild the player table without the v3 columns, as an older build would have written it.
            using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE player_old AS SELECT id, first_name, last_name, birth_date, nationality, height_cm, wingspan_cm, weight_kg, primary_pos, secondary_pos,
                        ca, pa, team_id, status, attributes, professionalism, ambition, loyalty, temperament, pressure, adaptability, consistency, big_games, injury_proneness, ego,
                        t_three, t_drive, t_post, t_iso, t_pass_first, t_crash, t_gamble, rookie_season FROM player;
                    DROP TABLE player;
                    ALTER TABLE player_old RENAME TO player;
                    UPDATE meta SET value = '2' WHERE key = 'schema_version';
                    """;
                cmd.ExecuteNonQuery();
            }

            var loaded = new SaveFile(path).Load();
            var p = loaded.AllActivePlayers.First();
            Assert.Equal(Heritages.Backfill(p.Id, p.Nationality), p.Ethnicity); // stable guess
            Assert.Null(p.Draft);

            // Saving again writes the new columns.
            new SaveFile(path).Save(loaded);
            var again = new SaveFile(path).Load();
            Assert.Equal(p.Ethnicity, again.AllActivePlayers.First(x => x.Id == p.Id).Ethnicity);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
