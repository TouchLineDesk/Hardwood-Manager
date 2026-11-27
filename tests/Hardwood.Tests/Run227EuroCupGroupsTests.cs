using Json = System.Text.Json.JsonSerializer;
using Hardwood.Core.Engine;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 227 (`danny-world-leagues-nov26`, the EuroCup's real format): 20 clubs in two groups of ten, snake-drawn by strength, home
/// and away inside the group (18 rounds); the top two in each group go straight to the quarter-finals, 3rd-6th play single-game
/// eighth-finals across the groups (A3 v B6, B3 v A6, A4 v B5, B4 v A5), then single-game quarter- and semi-finals and a best-of-three
/// final. A season saved before the groups keeps its one table and 16-club bracket.
/// </summary>
public class Run227EuroCupGroupsTests
{
    // The save's JSON settings (SaveFile.JsonOpts).
    private static readonly System.Text.Json.JsonSerializerOptions Opts = new() { IncludeFields = true };
    private static WorldCompetition Cup(GameWorld w) => w.Competitions.First(c => c.Key == WorldLeagues.EuroCup);

    [Theory]
    [InlineData(31UL)]
    [InlineData(2026UL)]
    public void The_EuroCup_is_two_groups_of_ten_home_and_away(ulong seed)
    {
        var (_, world) = RealWorldData.NewWorld(seed, players: false);
        var s = Cup(world).Season;
        Assert.True(s.Grouped);
        Assert.Equal(2, s.Groups!.Count);
        Assert.All(s.Groups, g => Assert.Equal(10, g.Count));
        Assert.Equal(20, s.Groups.SelectMany(g => g).Distinct().Count());
        Assert.Equal(s.Participants.OrderBy(x => x), s.Groups.SelectMany(g => g).OrderBy(x => x));

        // Every game is inside a group; every club plays its nine group rivals home and away.
        var groupOf = s.Groups.SelectMany((g, i) => g.Select(id => (id, i))).ToDictionary(x => x.id, x => x.i);
        Assert.All(s.Fixtures, f => Assert.Equal(groupOf[f.HomeId], groupOf[f.AwayId]));
        foreach (var id in s.Participants)
        {
            Assert.Equal(9, s.Fixtures.Count(f => f.HomeId == id));
            Assert.Equal(9, s.Fixtures.Count(f => f.AwayId == id));
        }
        // 18 round dates; nobody plays twice on one.
        Assert.Equal(18, s.Fixtures.Select(f => f.Date).Distinct().Count());
        foreach (var day in s.Fixtures.GroupBy(f => f.Date))
            Assert.Equal(day.Count() * 2, day.SelectMany(f => new[] { f.HomeId, f.AwayId }).Distinct().Count());

        // Snake draw: the two strongest clubs are in different groups.
        var strongest = s.Participants.OrderByDescending(id => GameWorld.Strength(world.TeamById(id)!)).ThenBy(id => id).Take(2).ToList();
        Assert.NotEqual(groupOf[strongest[0]], groupOf[strongest[1]]);
    }

    [Fact]
    public void The_knockouts_follow_the_group_places()
    {
        var (_, world) = RealWorldData.NewWorld(31, players: false);
        var sim = new GameSimulator();
        var cup = Cup(world);
        var day = new DateOnly(2026, 10, 1);
        while (cup.Season.Stage == CompetitionSeason.RegularSeason && day < new DateOnly(2027, 5, 1)) { world.PlayThrough(day, sim); day = day.AddDays(1); }
        var s = cup.Season;
        Assert.Equal("Eighth-finals", s.Stage);
        var g = s.GroupTables();
        int A(int place) => g[0][place - 1].TeamId;
        int B(int place) => g[1][place - 1].TeamId;

        var eighths = s.Series.Where(x => x.Round == "Eighth-finals").OrderBy(x => x.Id).ToList();
        Assert.Equal(new[] { (B(4), A(5)), (A(3), B(6)), (A(4), B(5)), (B(3), A(6)) }, eighths.Select(x => (x.HighId, x.LowId)));
        Assert.All(eighths, x => Assert.Equal(1, x.BestOf));
        Assert.Contains(world.News, n => n.Item1.Contains("the groups are done"));
        // The top two wait: nobody in 1st or 2nd plays an eighth-final; nobody 7th or lower does either.
        var inEighths = eighths.SelectMany(x => new[] { x.HighId, x.LowId }).ToHashSet();
        Assert.DoesNotContain(A(1), inEighths); Assert.DoesNotContain(B(2), inEighths); Assert.DoesNotContain(A(7), inEighths);

        world.FinishSeasons(sim);
        Assert.True(s.IsComplete);
        var qf = s.Series.Where(x => x.Round == "Quarter-finals").OrderBy(x => x.Id).ToList();
        Assert.Equal(new[] { A(1), B(2), B(1), A(2) }, qf.Select(x => x.HighId));
        Assert.Equal(eighths.Select(x => x.WinnerId!.Value), qf.Select(x => x.LowId));
        // Quarter-finals are home games for the waiting clubs.
        foreach (var x in qf) Assert.Equal(x.HighId, s.Fixtures.Single(f => f.SeriesId == x.Id).HomeId);
        // The group winners are in different halves.
        var semis = s.Series.Where(x => x.Round == "Semi-finals").OrderBy(x => x.Id).ToList();
        Assert.Equal(2, semis.Count);
        Assert.Equal(new[] { qf[0].WinnerId, qf[1].WinnerId }.OrderBy(x => x), new int?[] { semis[0].HighId, semis[0].LowId }.OrderBy(x => x));
        Assert.Equal(3, s.Series.Single(x => x.Round == "Final").BestOf);
        Assert.NotNull(s.ChampionId);
        // Playoff games stay off days either club plays at home in its league.
        Assert.All(s.Fixtures.Where(f => f.Stage != CompetitionSeason.RegularSeason), f => Assert.True(f.Date >= new DateOnly(2027, 3, 4)));
    }

    [Fact]
    public void A_season_saved_before_the_groups_keeps_its_one_table_and_sixteen_club_bracket()
    {
        var (_, world) = RealWorldData.NewWorld(31, players: false);
        var s = Cup(world).Season;
        s.Groups = null;   // as a v0.214 save loads: no groups
        world.FinishSeasons(new GameSimulator());
        Assert.True(s.IsComplete);
        Assert.Equal(8, s.Series.Count(x => x.Round == "Eighth-finals"));
        Assert.Equal(4, s.Series.Count(x => x.Round == "Quarter-finals"));
    }

    [Fact]
    public void The_groups_survive_a_save()
    {
        var (_, world) = RealWorldData.NewWorld(31, players: false);
        var s = Cup(world).Season;
        var back = Json.Deserialize<CompetitionSeason>(Json.Serialize(s, Opts), Opts)!;
        Assert.Equal(s.Groups!.Select(g => string.Join(",", g)), back.Groups!.Select(g => string.Join(",", g)));
        // And a season saved without the field loads with none.
        var old = Json.Deserialize<CompetitionSeason>("{\"Year\":2026,\"Participants\":[1,2]}", Opts)!;
        Assert.Null(old.Groups);
        Assert.False(old.Grouped);
    }

    [Fact]
    public void The_page_shows_a_card_per_group_and_the_bracket_keeps_the_quarter_final_slots()
    {
        var session = GameSession.NewGame(31, 5, null);
        session.World.FinishSeasons(new GameSimulator());
        var book = LeagueBook.Get(session, WorldLeagues.EuroCup)!;
        var rows = book.Table();
        Assert.Equal(new[] { "Group A", "Group B" }, rows.Select(r => r.Group).Distinct());
        foreach (var grp in rows.GroupBy(r => r.Group))
        {
            var z = grp.Select(r => r.Zone).ToList();
            Assert.Equal(new[] { LeagueBook.QuarterZone, LeagueBook.QuarterZone }, z.Take(2));
            Assert.All(z.Skip(2).Take(4), x => Assert.Equal(LeagueBook.EighthZone, x));
            Assert.All(z.Skip(6), x => Assert.Equal("", x));
        }
        Assert.Contains(book.Rules, r => r.Label == "Season" && r.Value.StartsWith("2 groups of 10") && r.Value.Contains("(18 games)"));
        Assert.Contains(book.Rules, r => r.Label == "Playoffs" && r.Value.Contains("straight to the quarter-finals"));

        var cols = LeagueBook.Columns(book.Bracket());
        Assert.Equal(new[] { "Eighth-finals", "Quarter-finals", "Semi-finals", "Final" }, cols.Select(c => c.Round));
        Assert.Equal(new[] { 4, 4, 2, 1 }, cols.Select(c => c.Slots.Count));
        // Each quarter-final sits beside the eighth-final that fed it.
        for (int i = 0; i < 4; i++)
        {
            var e = cols[0].Slots[i]!; var q = cols[1].Slots[i]!;
            Assert.True(q.Low == e.Winner, $"slot {i}");
        }
    }
}
