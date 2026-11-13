using System.Text.RegularExpressions;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Critic review #8 moved the results to every other Monday; since v0.56.0 (Danny, #243) they arrive every Monday again,
/// because the inbox can be cleaned, and every other Monday is an inbox setting. Either way no game is lost or counted twice.
/// </summary>
public class ResultsMessageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Results_arrive_weekly_or_every_other_week_and_every_regular_season_game_is_in_exactly_one_message(bool everyOtherWeek)
    {
        var probe = new WorldGenerator(new Rng(92)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var id = probe.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(15).Id;
        var s = GameSession.NewGame(92, id, null);
        s.InboxSettings.ResultsEveryOtherWeek = everyOtherWeek;
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
        // Games since the last message wait for the next one (the regular season can end mid-week, #265).
        Assert.Equal(s.MyRecord.Wins + s.MyRecord.Losses, games + s.WeekResults.Count);

        int weeks = (s.Today.DayNumber - start.DayNumber) / 7;
        if (everyOtherWeek)
        {
            // About one message a fortnight (plus weeks carrying a press conference), not one a week.
            Assert.InRange(results.Count, weeks / 3, weeks * 3 / 4);
            Assert.Contains(results, m => m.Title.StartsWith("Last two weeks"));
        }
        else
        {
            Assert.InRange(results.Count, weeks * 3 / 4, weeks + 1);
            Assert.DoesNotContain(results, m => m.Title.StartsWith("Last two weeks"));
        }

        // The open market has no trades and no draft (#80): the calendar news says so.
        Assert.True(s.League.Finance.OpenMarket);
        Assert.Contains(s.Inbox, m => m.Title == "The January window has shut");
        Assert.DoesNotContain(s.Inbox, m => m.Title == "Trade deadline has passed" || m.Body.Contains("lottery awaits"));
    }
}
