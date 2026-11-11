using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Roadmap 4, the living history part 1: the records book, milestones and "on this day".</summary>
public class RecordBookTests
{
    private static League NewLeague(ulong seed = 61) => new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));

    private static List<GameResult> Games(League league, int n, ulong seed)
    {
        var sim = new GameSimulator();
        return Enumerable.Range(0, n).Select(i => sim.Simulate(league.Teams[i % 15], league.Teams[15 + i % 15], Rules.Nba, seed + (ulong)i)).ToList();
    }

    [Fact]
    public void Single_game_records_hold_the_best_lines_in_order()
    {
        var league = NewLeague();
        var book = new RecordBook();
        var games = Games(league, 120, 1000);
        var day = new DateOnly(2026, 11, 1);
        foreach (var g in games) Assert.Empty(book.Consider(g, day = day.AddDays(1), 2026)); // the first season makes no news
        foreach (var (key, _, _, of) in RecordBook.PlayerGame)
        {
            var list = book.Top(key);
            Assert.InRange(list.Count, 1, RecordBook.Keep);
            for (int i = 1; i < list.Count; i++) Assert.True(list[i - 1].Value >= list[i].Value);
            int best = games.SelectMany(g => g.Home.Lines.Concat(g.Away.Lines)).Max(of);
            Assert.Equal(best, list[0].Value);
            // The tenth best is really tenth: no line outside the book beats it.
            int beating = games.SelectMany(g => g.Home.Lines.Concat(g.Away.Lines)).Count(l => of(l) > list[^1].Value);
            Assert.True(beating < RecordBook.Keep);
        }
        Assert.Equal(games.Max(g => Math.Max(g.Home.Points, g.Away.Points)), book.Top("team-pts")[0].Value);
        Assert.Equal(games.Max(g => Math.Abs(g.Home.Points - g.Away.Points)), book.Top("margin")[0].Value);
    }

    [Fact]
    public void A_record_broken_in_a_later_season_makes_news()
    {
        var league = NewLeague();
        var book = new RecordBook();
        var g = Games(league, 1, 7)[0];
        book.Consider(g, new DateOnly(2026, 11, 2), 2026);
        var top = g.Home.Lines.Concat(g.Away.Lines).OrderByDescending(l => l.Points).First();
        var old = book.Top("pts")[0];
        // Next season someone scores two more.
        top.Points += 2;
        var news = book.Consider(g, new DateOnly(2027, 11, 2), 2027);
        Assert.Contains(news, n => n.StartsWith($"League record: {top.Player.Name}") && n.Contains($"{old.Value + 2} pts") && n.Contains("2026-27"));
        // Equalling a record isn't breaking it.
        Assert.DoesNotContain(book.Consider(g, new DateOnly(2027, 11, 3), 2027), n => n.Contains(" pts against"));
    }

    [Fact]
    public void Milestones_fire_once_when_a_round_number_is_passed()
    {
        var league = NewLeague();
        var p = league.Teams[0].Roster[0];
        var book = new RecordBook();
        var day = new DateOnly(2027, 2, 1);
        var found = book.CheckMilestones(p, league.Teams[0], day, 2026, new Dictionary<string, (int, int)> { ["points"] = (9_990, 10_012), ["games"] = (998, 999) });
        var m = Assert.Single(found);
        Assert.Equal(10_000, m.Threshold);
        Assert.Equal($"{p.Name} ({league.Teams[0].Abbreviation}) passed 10,000 career points", m.Text);
        Assert.Empty(book.CheckMilestones(p, league.Teams[0], day, 2026, new Dictionary<string, (int, int)> { ["points"] = (9_990, 10_012) }));
        Assert.Equal(2, book.CheckMilestones(p, league.Teams[0], day, 2026, new Dictionary<string, (int, int)> { ["rebounds"] = (2_990, 5_001) }).Count);
    }

    [Fact]
    public void On_this_day_looks_back_to_earlier_seasons_only()
    {
        var league = NewLeague();
        var book = new RecordBook();
        var g = Games(league, 1, 9)[0];
        book.Consider(g, new DateOnly(2026, 12, 25), 2026);
        Assert.Empty(book.OnThisDay(new DateOnly(2026, 12, 25))); // this season isn't history yet
        var moments = book.OnThisDay(new DateOnly(2028, 12, 24));
        Assert.NotEmpty(moments);
        Assert.All(moments, m => Assert.Equal(2, m.YearsAgo));
        Assert.Contains(moments, m => m.Text.Contains("the league record for points in a game"));
        Assert.Empty(book.OnThisDay(new DateOnly(2028, 12, 10))); // two weeks off
    }

    [Fact]
    public void The_book_is_saved_and_old_saves_start_empty()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-rb-{Guid.NewGuid():N}.save");
        try
        {
            var league = NewLeague();
            foreach (var g in Games(league, 20, 50)) league.Records.Consider(g, new DateOnly(2026, 11, 3), 2026);
            league.Records.CheckMilestones(league.Teams[0].Roster[0], league.Teams[0], new DateOnly(2026, 11, 3), 2026, new Dictionary<string, (int, int)> { ["games"] = (499, 500) });
            new SaveFile(path).Save(league);
            var loaded = new SaveFile(path).Load();
            Assert.Equal(league.Records.Top("pts").Select(e => (e.Value, e.PlayerName, e.Date)), loaded.Records.Top("pts").Select(e => (e.Value, e.PlayerName, e.Date)));
            Assert.Equal(2026, loaded.Records.FirstSeason);
            Assert.Single(loaded.Records.Milestones);

            // A save from before the records book: no meta row, an empty book.
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM meta WHERE key = 'record_book'";
                cmd.ExecuteNonQuery();
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            var old = new SaveFile(path).Load();
            Assert.Empty(old.Records.Games);
            Assert.Empty(old.Records.Milestones);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Career_leaders_include_the_season_being_played()
    {
        var careers = new Dictionary<int, CareerTotals> { [1] = new() { PlayerId = 1, Points = 9_000, Games = 600 }, [2] = new() { PlayerId = 2, Points = 9_500, Games = 640 } };
        var current = new Dictionary<int, PlayerSeasonStats> { [1] = new() { PlayerId = 1, Points = 800, Games = 30 } };
        var all = RecordBook.CareerWithCurrent(careers, current);
        Assert.Equal(new[] { 1, 2 }, RecordBook.CareerLeaders(all, c => c.Points).Select(c => c.PlayerId));
        Assert.Equal(9_000, careers[1].Points); // the stored totals aren't touched
    }
}
