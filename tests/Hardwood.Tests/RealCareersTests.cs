using System.Text.Json;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Names;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// The real world, slice R2 part 2b (decisions #223-#227): the players who hadn't played yet this season, the real
/// players' careers carried in, and record-breaking news against the real NBA record book. Runs with the name-pack
/// tests because a real-world career changes the static top-league name.
/// </summary>
[Collection(NamePackCollection.Name)]
public class RealCareersTests
{
    [Fact]
    public void Players_not_yet_seen_this_season_are_rated_from_last_season_on_their_real_teams()
    {
        try
        {
            var s = GameSession.NewGame(2026, 9, null);
            Player On(string abbr, string name) => s.League.Teams.First(t => t.Abbreviation == abbr).Roster.Single(p => p.Name == name);
            var all = s.League.Teams.SelectMany(t => t.Roster).OrderByDescending(p => p.CurrentAbility).ToList();
            int median = all[all.Count / 2].CurrentAbility;
            // Last season's 20-point scorers rate as rotation players or better.
            foreach (var (abbr, name) in new[] { ("GSW", "Jimmy Butler"), ("LAC", "Brandon Ingram"), ("POR", "Shaedon Sharpe"), ("GSW", "Kristaps Porziņģis"), ("SAS", "Tobias Harris") })
                Assert.True(On(abbr, name).CurrentAbility > median, $"{name} {On(abbr, name).CurrentAbility} vs median {median}");
            // Real bios and contracts came with them (R2 part 2a's data).
            var butler = On("GSW", "Jimmy Butler");
            Assert.Equal(new DateOnly(1989, 9, 14), butler.BirthDate);
            Assert.Equal(56_830_000L, butler.Contract!.Salary);
            // Every team is still a full, real 15.
            var names = RealPlayers.Lines.Select(l => l.Name).ToHashSet();
            Assert.All(s.League.Teams.Take(30), t => Assert.Equal(15, t.Roster.Count(p => names.Contains(p.Name))));
            // A player with no NBA line at all (a 2026 rookie) is rated at bench level.
            var rookie = RealPlayers.Rate(RealPlayers.Lines.Single(l => l.Name == "Jayden Quaintance"));
            Assert.Equal(RealPlayers.PriorPer36, rookie.Per36, 3);
        }
        finally { Brand.Real = false; }
    }

    [Fact]
    public void Real_careers_start_the_records_book_and_the_real_record_book_is_seeded()
    {
        Assert.Equal("2026-11-12", RealCareers.Checked);
        Assert.Contains("Basketball-Reference", RealCareers.Source);
        Assert.Equal(43_440, RealCareers.Players["LeBron James"].Points); // the real record book says the same
        try
        {
            var s = GameSession.NewGame(2026, 9, null);
            Player Find(string name) => s.League.Teams.SelectMany(t => t.Roster).Single(p => p.Name == name);
            var lebron = s.League.Careers[Find("LeBron James").Id];
            Assert.Equal((43_440, 1_622, 4, 4), (lebron.Points, lebron.Games, lebron.Mvps, lebron.FinalsMvps));
            Assert.InRange(lebron.Seasons, 22, 24);
            var shai = s.League.Careers[Find("Shai Gilgeous-Alexander").Id];
            Assert.Equal(2, shai.Mvps);
            Assert.InRange(shai.Games, 380, 550); // estimated: he isn't in the top 100 for games
            Assert.InRange(shai.Points, 13_000, 14_000);
            Assert.InRange(s.League.Careers.Count, 120, 160);
            // The records book's career leaders start from the real past.
            var leaders = RecordBook.CareerLeaders(s.League.Careers, c => c.Points, 3);
            Assert.Equal(Find("LeBron James").Id, leaders[0].PlayerId);
            Assert.Equal(Find("Kevin Durant").Id, leaders[1].PlayerId);
            // The real records are in the book, with LeBron as the holder of career points.
            var marks = s.League.Records.RealMarks;
            Assert.Equal((100.0, "Wilt Chamberlain"), (marks["game-pts"].Value, marks["game-pts"].Holder));
            Assert.Equal(402, marks["season-3pm"].Value);
            Assert.Equal(Find("LeBron James").Id, marks["career-pts"].HolderId);
            Assert.Null(marks["career-ast"].HolderId); // Stockton isn't in the game
        }
        finally { Brand.Real = false; }
    }

    private static (Player P, Team T) Someone(string name = "Test Scorer")
    {
        var league = new WorldGenerator(new Rng(5)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var t = league.Teams[0];
        int i = name.IndexOf(' ');
        return (new Player { Id = 90_000 + name.Length * 7 + name[0], FirstName = name[..i], LastName = name[(i + 1)..] }, t);
    }

    private static readonly Dictionary<string, int> Quiet = new() { ["pts"] = 20, ["reb"] = 5, ["ast"] = 5, ["stl"] = 1, ["blk"] = 1, ["3pm"] = 2 };
    private static Dictionary<string, (int, int)> Flat(params (string K, int B, int A)[] x) => x.ToDictionary(v => v.K, v => (v.B, v.A));

    [Fact]
    public void Breaking_a_real_record_makes_news_once_and_the_new_mark_is_the_one_to_beat()
    {
        var book = new RecordBook();
        var (p, t) = Someone();
        RealRecordChase.Seed(book, new Dictionary<string, int>());
        var day = new DateOnly(2027, 1, 10);
        // A quiet night is no news.
        Assert.Empty(RealRecordChase.Check(book, p, t, "BOS", day, 2026, Quiet, Flat(("pts", 500, 520), ("3pm", 50, 52)), Flat(("pts", 9_000, 9_020))));
        // 101 points: Wilt's record falls.
        var game = new Dictionary<string, int>(Quiet) { ["pts"] = 101 };
        var news = RealRecordChase.Check(book, p, t, "BOS", day, 2026, game, Flat(("pts", 500, 601)), Flat(("pts", 9_000, 9_101)));
        var broken = Assert.Single(news);
        Assert.True(broken.Broken);
        Assert.Contains("Wilt Chamberlain", broken.Body);
        Assert.Equal((101.0, p.Id), (book.RealMarks["game-pts"].Value, book.RealMarks["game-pts"].HolderId));
        // 101 again is now a tie, not a record.
        var tie = RealRecordChase.Check(book, Someone("Other Guy").P, t, "NYK", day.AddDays(2), 2026, game, Flat(("pts", 0, 101)), Flat(("pts", 0, 101)));
        Assert.Contains("ties an NBA record", Assert.Single(tie).Title);
        // Single season: passing 402 threes is a record; the holder's later games that season extend it without news.
        var threes = RealRecordChase.Check(book, p, t, "MIA", day.AddDays(40), 2026, Quiet, Flat(("3pm", 401, 404)), Flat());
        Assert.Contains("404 threes", Assert.Single(threes).Title);
        Assert.Empty(RealRecordChase.Check(book, p, t, "MIA", day.AddDays(42), 2026, Quiet, Flat(("3pm", 404, 409)), Flat()));
        Assert.Equal(409, book.RealMarks["season-3pm"].Value);
    }

    [Fact]
    public void Career_chases_close_in_pass_legends_and_respect_the_holders_own_games()
    {
        var book = new RecordBook();
        var (lebron, lal) = Someone("LeBron James");
        RealRecordChase.Seed(book, new Dictionary<string, int> { ["LeBron James"] = lebron.Id });
        var (kd, hou) = Someone("Kevin Durant");
        Assert.NotEqual(lebron.Id, kd.Id);
        var day = new DateOnly(2027, 2, 1);
        // The holder adds to his own record without news.
        Assert.Empty(RealRecordChase.Check(book, lebron, lal, "BOS", day, 2026, Quiet, Flat(), Flat(("pts", 43_440, 43_465))));
        Assert.Equal(43_465, book.RealMarks["career-pts"].Value);
        // Passing a legend on the scoring list (Kobe, 33,643).
        var kobe = RealRecordChase.Check(book, kd, hou, "DAL", day, 2026, Quiet, Flat(), Flat(("pts", 33_630, 33_655)));
        Assert.Contains("passes Kobe Bryant", Assert.Single(kobe).Title);
        // Closing in on the career record (within 1,000), once.
        var near = RealRecordChase.Check(book, kd, hou, "DAL", day, 2026, Quiet, Flat(), Flat(("pts", 42_460, 42_480)));
        Assert.Contains(near, n => n.Title.StartsWith("Record watch") && !n.Broken);
        Assert.Empty(RealRecordChase.Check(book, kd, hou, "DAL", day, 2026, Quiet, Flat(), Flat(("pts", 42_480, 42_500))));
        // Passing the holder's current total breaks it.
        var record = RealRecordChase.Check(book, kd, hou, "DAL", day, 2026, Quiet, Flat(), Flat(("pts", 43_460, 43_470)));
        Assert.Contains(record, n => n.Broken && n.Title.Contains("all-time leader in points"));
        Assert.Equal(kd.Id, book.RealMarks["career-pts"].HolderId);
    }

    [Fact]
    public void A_records_book_saved_before_part_2b_loads_with_no_real_marks()
    {
        var json = JsonSerializer.Serialize(new RecordBook { FirstSeason = 2026 });
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
        doc.Remove("RealMarks");
        var old = JsonSerializer.Deserialize<RecordBook>(JsonSerializer.Serialize(doc))!;
        Assert.Empty(old.RealMarks);
        // A real career seeds it on the next game.
        var league = new WorldGenerator(new Rng(5)).Generate(RealWorldData.LeagueName, 2026, new DateOnly(2026, 10, 1));
        league.Records = old;
        RealRecordChase.EnsureSeeded(league);
        Assert.Equal(55, league.Records.RealMarks["game-reb"].Value);
        // And it round-trips.
        var back = JsonSerializer.Deserialize<RecordBook>(JsonSerializer.Serialize(league.Records))!;
        Assert.Equal(league.Records.RealMarks.Count, back.RealMarks.Count);
    }
}
