using System.Text.RegularExpressions;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Critic review #8: the weekly results message was 58-70 of the 230-250 messages a career gets in three seasons.
/// Results now arrive every other Monday (or in a week that has a press conference or locker-room moment to carry),
/// and no game is lost or counted twice on the way.
/// </summary>
public class ResultsMessageTests
{
    [Fact]
    public void Results_arrive_every_other_week_and_every_regular_season_game_is_in_exactly_one_message()
    {
        var probe = new WorldGenerator(new Rng(92)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var id = probe.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(15).Id;
        var s = GameSession.NewGame(92, id, null);
        var start = s.Today;
        while (s.Phase != SessionPhase.Postseason && s.Phase != SessionPhase.Offseason) s.AdvanceDay();

        var results = s.Inbox.Where(m => m.Category == "Game" && GameSession.IsResultsTitle(m.Title)).OrderBy(m => m.Date).ToList();
        int games = 0;
        foreach (var m in results)
        {
            var wl = Regex.Match(m.Title, @": (\d+)-(\d+) \(");
            Assert.True(wl.Success, m.Title);
            int n = int.Parse(wl.Groups[1].Value) + int.Parse(wl.Groups[2].Value);
            Assert.Equal(n, m.Body.Split('\n').Count(l => l.Contains("  W ") || l.Contains("  L ")));
            games += n;
        }
        Assert.Equal(s.MyRecord.Wins + s.MyRecord.Losses, games);

        // About one message a fortnight (plus weeks carrying a press conference), not one a week.
        int weeks = (s.Today.DayNumber - start.DayNumber) / 7;
        Assert.InRange(results.Count, weeks / 3, weeks * 3 / 4);
        Assert.Contains(results, m => m.Title.StartsWith("Last two weeks"));

        // The open market has no trades and no draft (#80): the calendar news says so.
        Assert.True(s.League.Finance.OpenMarket);
        Assert.Contains(s.Inbox, m => m.Title == "The January window has shut");
        Assert.DoesNotContain(s.Inbox, m => m.Title == "Trade deadline has passed" || m.Body.Contains("lottery awaits"));
    }
}
