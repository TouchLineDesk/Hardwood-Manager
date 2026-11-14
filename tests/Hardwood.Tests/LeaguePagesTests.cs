using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// The league pages (Danny, #463 and #470): every league's standings by division, and the playoffs laid out as a bracket
/// where each series sits beside the two that fed it.
/// </summary>
public class LeaguePagesTests
{
    private static void AssertFed(List<BracketColumn> cols)
    {
        for (int c = 1; c < cols.Count; c++)
        {
            Assert.Equal(cols[c - 1].Slots.Count, cols[c].Slots.Count * 2);
            for (int i = 0; i < cols[c].Slots.Count; i++)
            {
                if (cols[c].Slots[i] is not { } x) continue;
                var feeders = new[] { cols[c - 1].Slots[2 * i], cols[c - 1].Slots[2 * i + 1] };
                Assert.All(feeders, f => Assert.NotNull(f));
                Assert.Contains(x.High, feeders.Select(f => f!.Winner));
                Assert.Contains(x.Low, feeders.Select(f => f!.Winner));
            }
        }
        Assert.Single(cols[^1].Slots);
    }

    [Fact]
    public void The_playoffs_and_the_third_place_tournament_draw_as_brackets()
    {
        var s = GameSession.NewGame(4, 3, null);
        int guard = 0;
        // Into the conference semi-finals: the later rounds are still empty slots.
        while ((s.Postseason == null || s.Postseason.Series.All(x => !x.Round.EndsWith("Semifinals"))) && guard++ < 400) s.AdvanceDay();
        var book = LeagueBook.Get(s, LeagueBook.NablKey)!;
        var main = LeagueBook.Columns(book.Bracket().Where(x => !x.ThirdPlace));
        Assert.Equal(new[] { 8, 4, 2, 1 }, main.Select(c => c.Slots.Count));
        Assert.Equal("First Round", main[0].Round);
        Assert.Equal("Conference semifinals", main[1].Round);
        Assert.All(main[0].Slots, x => Assert.NotNull(x));
        Assert.All(main[2].Slots, x => Assert.Null(x));
        AssertFed(main);
        var third = LeagueBook.Columns(book.Bracket().Where(x => x.ThirdPlace));
        Assert.Equal(new[] { 4, 2, 1 }, third.Select(c => c.Slots.Count));
        AssertFed(third);

        // To the end: every slot filled, one champion.
        while (!s.Postseason!.Complete && guard++ < 800) s.AdvanceDay();
        book = LeagueBook.Get(s, LeagueBook.NablKey)!;
        main = LeagueBook.Columns(book.Bracket().Where(x => !x.ThirdPlace));
        Assert.All(main.SelectMany(c => c.Slots), x => Assert.True(x is { Complete: true }));
        AssertFed(main);
        Assert.Equal(s.Postseason.Champion, main[^1].Slots[0]!.Winner);
        AssertFed(LeagueBook.Columns(book.Bracket().Where(x => x.ThirdPlace)));

        // A lower tier's bracket too (the same format, its own series).
        var t2 = LeagueBook.Get(s, Hardwood.Core.World.UsaPyramid.Tier2)!;
        var t2cols = LeagueBook.Columns(t2.Bracket().Where(x => !x.ThirdPlace));
        Assert.NotEmpty(t2cols);
        AssertFed(t2cols);
    }

    [Fact]
    public void Every_league_has_its_standings_by_group_and_its_champions_as_data()
    {
        var s = GameSession.NewGame(4, 3, null);
        for (int i = 0; i < 20; i++) s.AdvanceDay();
        foreach (var b in LeagueBook.All(s))
        {
            var rows = b.Table();
            Assert.Equal(b.Teams.Count, rows.Count);
            // The NBA and every USA tier: four-team divisions, named "Conference · Division".
            if (b.Key == LeagueBook.NablKey || Hardwood.Core.World.UsaPyramid.IsNational(b.Key))
                Assert.All(rows.GroupBy(r => r.Group), g => { Assert.Contains(" · ", g.Key); Assert.Equal(4, g.Count()); });
        }
        Assert.Empty(LeagueBook.Get(s, LeagueBook.NablKey)!.ChampionRows());
    }
}
