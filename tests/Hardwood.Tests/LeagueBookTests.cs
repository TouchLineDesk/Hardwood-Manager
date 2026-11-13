using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Every league seen one way (Danny, Nov 10): tables, games, stats, rules and champions for the NABL, the world and college.</summary>
public class LeagueBookTests
{
    [Fact]
    public void Every_league_has_a_table_games_stats_and_rules()
    {
        var s = GameSession.NewGame(2026, 5, null);
        while (s.MyRecord.Wins + s.MyRecord.Losses < 10) s.AdvanceDay();
        var books = LeagueBook.All(s).ToList();
        Assert.Equal(1 + s.World.Playing.Count() + 1, books.Count); // NABL + world + college
        foreach (var b in books)
        {
            Assert.NotEmpty(b.Teams);
            Assert.NotEmpty(b.Rules);
            Assert.Contains(b.Region, Competitions.Regions.Append("USA"));
            var table = b.Table();
            Assert.Equal(b.Teams.Count, table.Count);
            Assert.NotEmpty(b.Games());
        }
        var nabl = books[0];
        Assert.Equal(LeagueBook.NablKey, nabl.Key);
        Assert.Contains(nabl.Games(), g => g.Played && g.Box != null);
        Assert.NotEmpty(nabl.Stats());
        // By conference and division (Danny, #318, #344): eight groups of four.
        Assert.All(nabl.Table(), r => Assert.Matches("^(East|West) · .+", r.Group));
        Assert.Equal(8, nabl.Table().Select(r => r.Group).Distinct().Count());
        Assert.All(nabl.Table().GroupBy(r => r.Group), g => Assert.Equal(4, g.Count()));
        // The team page finds each team's own league and cups.
        var club = s.World[Competitions.Elite].Season.Participants.Select(id => s.World.TeamById(id)!).First();
        Assert.Equal(2, LeagueBook.Of(s, club).Count());
        Assert.Equal(LeagueBook.CollegeKey, LeagueBook.HomeOf(s, s.World.College!.Programs[0])!.Key);
        // The pyramid shows promotion and relegation places.
        var t2 = LeagueBook.Get(s, UsaPyramid.Tier2)!.Table();
        Assert.Equal("Promotion", t2[0].Zone);
        Assert.Equal("Relegation", t2[^1].Zone);
        // Eight regional divisions of four, four a conference: one promotion and one relegation place in each (#319, #344).
        Assert.Equal(8, t2.Select(r => r.Group).Distinct().Count());
        Assert.All(t2, r => Assert.Matches("^(East|West) · .+", r.Group));
        Assert.All(t2.GroupBy(r => r.Group), g => { Assert.Equal(4, g.Count()); Assert.Equal(1, g.Count(r => r.Zone == "Promotion")); Assert.Equal(1, g.Count(r => r.Zone == "Relegation")); });
    }
}
