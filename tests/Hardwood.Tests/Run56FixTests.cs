using Hardwood.Core.Engine;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 56 (Phase 0's bug finder over three seasons): in a bottom-tier career's third season, a played game
/// had a score and no box score. Two causes: once 200 box scores were kept, removing <c>Keys.First()</c> removed the
/// game just added (a dictionary reuses the freed slot and lists it first); and a game below the NBA was keyed by its
/// fixture number alone, which starts again every season.</summary>
public class Run56FixTests
{
    private static GameResult Box(Hardwood.Core.Model.Team h, Hardwood.Core.Model.Team a) =>
        new() { Home = new TeamBox { Team = h }, Away = new TeamBox { Team = a } };

    [Fact]
    public void Every_new_box_score_is_kept_once_the_store_is_full()
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var (h, a) = (league.Teams[0], league.Teams[1]);
        var boxes = new Dictionary<string, GameResult>();
        var day = new DateOnly(2026, 10, 22);
        for (int i = 0; i < 260; i++)
        {
            var f = new Fixture { Index = i, Date = day.AddDays(i) };
            string key = GameSession.AbroadKey("usa5", f);
            boxes[key] = Box(h, a);
            GameSession.KeepNewest(boxes);
            Assert.True(boxes.ContainsKey(key), $"the game just played (#{i}) keeps its box score");
            Assert.True(boxes.Count <= 200);
        }
        // The 200 newest are the ones kept.
        Assert.Equal(200, boxes.Count);
        Assert.True(boxes.ContainsKey(GameSession.AbroadKey("usa5", new Fixture { Index = 60, Date = day.AddDays(60) })));
        Assert.False(boxes.ContainsKey(GameSession.AbroadKey("usa5", new Fixture { Index = 59, Date = day.AddDays(59) })));
    }

    [Fact]
    public void A_fixture_number_used_again_next_season_is_a_different_game()
    {
        var first = new Fixture { Index = 12, Date = new DateOnly(2026, 11, 3) };
        var later = new Fixture { Index = 12, Date = new DateOnly(2028, 11, 1) };
        Assert.NotEqual(GameSession.AbroadKey("usa5", first), GameSession.AbroadKey("usa5", later));
    }

    [Fact]
    public void A_save_from_v0_79_3_keeps_this_seasons_box_scores()
    {
        var (probe, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        int id = world[UsaPyramid.Tier5].Clubs.OrderBy(t => QuickSim.Strength(t)).ElementAt(10).Id;
        var path = Path.Combine(Path.GetTempPath(), $"hw-run56-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = GameSession.NewGame(2026, id, path);
            int guard = 0;
            while (s.MyGames().Count(g => g.Played) < 3 && guard++ < 200) s.AdvanceDay();
            var played = s.MyGames().Where(g => g.Played && g.Result is { } r && r.Home.Lines.Count > 0).ToList();
            Assert.NotEmpty(played);
            s.SaveNow();

            // Write the keys as v0.79.3 did ("usa5:12"), and add one from an "earlier season" that matches no game.
            using (var conn = new Hardwood.Core.Persistence.SaveFile(path).OpenConnection())
            {
                using var read = conn.CreateCommand();
                read.CommandText = "SELECT value FROM career_meta WHERE key = 'side_boxes'";
                var json = (string)read.ExecuteScalar()!;
                json = System.Text.RegularExpressions.Regex.Replace(json, "\"Key\":\"([^\":]+):\\d{8}:(\\d+)\"", "\"Key\":\"$1:$2\"");
                Assert.DoesNotContain(":2026", json.Split("\"Key\":\"")[1].Split('"')[0]);
                var stale = System.Text.RegularExpressions.Regex.Match(json, "\\{\"Kind\":\"abroad\".*?\\]\\]\\}").Value;
                Assert.NotEmpty(stale);
                var wrong = System.Text.RegularExpressions.Regex.Replace(stale, "\"Key\":\"([^\":]+):(\\d+)\"", "\"Key\":\"$1:99999\"");
                json = json.TrimEnd(']') + "," + wrong + "]";
                using var write = conn.CreateCommand();
                write.CommandText = "UPDATE career_meta SET value = $v WHERE key = 'side_boxes'";
                write.Parameters.AddWithValue("$v", json);
                write.ExecuteNonQuery();
            }

            var back = GameSession.Load(path);
            var again = back.MyGames().Where(g => g.Played && g.Result is { } r && r.Home.Lines.Count > 0).ToList();
            Assert.Equal(played.Count, again.Count);
            Assert.Equal(played.Select(g => g.Result!.Home.Points), again.Select(g => g.Result!.Home.Points));
            Assert.All(back.AbroadResults.Keys, k => Assert.Equal(3, k.Split(':').Length));
            Assert.Equal(played.Count, back.AbroadResults.Count); // the unmatched old box is dropped
            // No unplayed game shows a box score.
            Assert.DoesNotContain(back.MyGames(), g => !g.Played && g.Result != null);
        }
        finally { foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f); }
    }
}
