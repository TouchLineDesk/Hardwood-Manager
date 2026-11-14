using System.Text.RegularExpressions;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Review #12, Top fix 1 (run 42): the inbox under 220 messages per three seasons. In the regular season the world's
/// headlines and short knocks ride in the weekly results message instead of arriving as messages of their own.
/// </summary>
public class InboxQuietTests
{
    [Fact]
    public void The_regular_season_carries_headlines_and_knocks_in_the_weekly_results()
    {
        var s = GameSession.NewGame(31, 5, null, realWorld: true);
        int guard = 0;
        while (s.Phase != SessionPhase.Postseason && s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();

        var results = s.Inbox.Where(m => m.Category == "Game" && GameSession.IsResultsTitle(m.Title)).OrderBy(m => m.Date).ToList();
        Assert.NotEmpty(results);
        var first = results[0].Date;
        var last = results[^1].Date;
        // No separate world digest between the first and last results message: the headlines are inside them.
        Assert.DoesNotContain(s.Inbox, m => m.Title.StartsWith("Around the world") && m.Date > first && m.Date < last);
        Assert.Contains(results, m => m.Body.Contains("Around the world ("));
        // Every injury that still arrives on its own is a long one (over two weeks) or leaves a lasting loss.
        foreach (var m in s.Inbox.Where(m => m.Category == "Medical" && m.Title.Contains(" injured: ") && m.Date >= first && m.Date <= last))
        {
            var days = Regex.Match(m.Body, @"Out approximately (\d+) days");
            Assert.True(days.Success, m.Body);
            Assert.True(int.Parse(days.Groups[1].Value) > GameSession.KnockDays || m.Body.Contains("lasting loss"), m.Title + ": " + m.Body);
        }
        // The game lines still count the week's record exactly (the knocks are a section of their own).
        foreach (var m in results)
        {
            var wl = Regex.Match(m.Title, @": (\d+)-(\d+) \(");
            Assert.Equal(int.Parse(wl.Groups[1].Value) + int.Parse(wl.Groups[2].Value), m.Body.Split('\n').Count(l => l.Contains("  W ") || l.Contains("  L ")));
        }
    }
}
