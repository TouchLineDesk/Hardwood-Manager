using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 240, a polish run: H243 (a relegation at .500 read only "expected more"), H244 (a player entered the Hall
/// of Fame the summer he retired) and H240 (the board's pace line after the season).</summary>
public class Run240PolishTests
{
    [Fact]
    public void Every_relegation_verdict_says_the_club_is_going_down()
    {
        // The Aces at 31-31, last in their division: the generic fallback.
        var v = Board.TellsTheDrop("Franklin Calloway expected more. Board confidence: 38/100.");
        Assert.Equal("Franklin Calloway expected more. The club is going down. Board confidence: 38/100.", v);
        // Lines that already tell the drop are unchanged.
        foreach (var told in new[]
                 {
                     "X set one job, to stay up, and the club is going down. Board confidence: 20/100.",
                     "X gave you a season to rebuild after the last relegation, and the club has gone down again. Board confidence: 12/100.",
                     "X is disappointed: a winning record wasn't enough to stay up, last in the division. Board confidence: 40/100.",
                 })
            Assert.Equal(told, Board.TellsTheDrop(told));
        Assert.Equal("X has decided to make a change. The club is going down. You have been sacked.",
            Board.TellsTheDrop("X has decided to make a change. You have been sacked."));
        Assert.Equal("X is giving you another season, but expects much better. Consider this a final warning. The club is going down.",
            Board.TellsTheDrop("X is giving you another season, but expects much better. Consider this a final warning."));
    }

    [Fact]
    public void A_hall_of_famer_waits_four_full_seasons_after_retiring_and_the_wait_survives_a_save()
    {
        var league = Legacy.Rules(new WorldGenerator(new Rng(77)).Generate("L", 2026, new DateOnly(2026, 10, 1)));
        int start = league.Season;
        var before = league.HallOfFame.Select(h => h.PlayerId).ToHashSet();
        var career = new Career(new Rng(9));
        int firstPendingSummer = -1;
        for (int i = 0; i < 12; i++)
        {
            career.PlaySeason(league);
            if (firstPendingSummer < 0 && league.HallPending.Count > 0) firstPendingSummer = i;
            // Nobody waiting is already in, and nobody waits past his class.
            Assert.All(league.HallPending, kv => Assert.DoesNotContain(league.HallOfFame, h => h.PlayerId == kv.Key));
            Assert.All(league.HallPending, kv => Assert.True(kv.Value > league.Season, $"class {kv.Value} still pending in {league.Season}"));
        }
        Assert.True(firstPendingSummer >= 0, "no retiree ever reached the Hall's score");
        var inducted = league.HallOfFame.Where(h => !before.Contains(h.PlayerId)).ToList();
        Assert.NotEmpty(inducted);
        // The first summer's retirees join the class of start + 1 + 4 at the earliest.
        Assert.All(inducted, h => Assert.True(h.Year >= start + 1 + Offseason.HallWaitSeasons, $"class of {h.Year}"));
        Assert.All(inducted, h => Assert.Contains(league.Retired, p => p.Id == h.PlayerId));

        var path = Path.Combine(Path.GetTempPath(), $"hw-run240-{Guid.NewGuid():N}.hwsave");
        try
        {
            league.HallPending[999_999] = 2099;
            new SaveFile(path).Save(league);
            var back = new SaveFile(path).Load();
            Assert.Equal(league.HallPending.OrderBy(kv => kv.Key), back.HallPending.OrderBy(kv => kv.Key));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void The_boards_pace_line_is_a_result_once_the_season_is_played()
    {
        var league = Legacy.Rules(new WorldGenerator(new Rng(31)).Generate("L", 2026, new DateOnly(2026, 10, 1)));
        var team = league.Teams[0];
        var job = new JobState { Current = Board.SetExpectations(league, team, 2026), Confidence = 55, HiredOn = new DateOnly(2026, 1, 1) };
        var ctx = LeagueContext.Nabl(league);
        int target = job.Current!.TargetWins;
        int games = ctx.SeasonGames;
        int wins = Math.Min(games, target + 2);
        var o = Board.Outlook(job, ctx, team, wins, games - wins, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2027, 4, 20));
        Assert.Equal($"Finished with {wins} wins against a target of {target} (2 ahead)", o!.Drivers[0].Text);
        var mid = Board.Outlook(job, ctx, team, 10, 10, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2026, 12, 15));
        Assert.StartsWith("On pace for", mid!.Drivers[0].Text);
    }
}
