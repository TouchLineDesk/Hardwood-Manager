using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Desktop.ViewModels;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 168 (#1064): timeouts. Who gets how many, when benches call them, the coach's button and the log.</summary>
public class Run168TimeoutTests
{
    private static readonly DateOnly Day = new(2026, 11, 5);

    [Fact]
    public void Each_rule_set_gets_its_real_allowance()
    {
        Assert.Same(TimeoutRules.Nba, TimeoutRules.For(Rules.Nba));
        Assert.Same(TimeoutRules.College, TimeoutRules.For(Rules.College));
        Assert.Same(TimeoutRules.College, TimeoutRules.For(Rules.CollegeMadness));
        Assert.Same(TimeoutRules.Fiba, TimeoutRules.For(Rules.Fiba));
        // A league restored from a save keeps only the basic columns: the shape still picks the right allowance.
        Assert.Same(TimeoutRules.Fiba, TimeoutRules.For(new Rules("FIBA", 4, 10, 24, 5, 5, 6.75)));

        var nba = TimeoutRules.Nba;
        Assert.Equal(7, nba.AtPeriodStart(1, 4, 0));
        Assert.Equal(6, nba.AtPeriodStart(3, 4, 6));
        Assert.Equal(4, nba.AtPeriodStart(4, 4, 6));     // at most 4 in the fourth
        Assert.Equal(2, nba.AtPeriodStart(4, 4, 2));
        Assert.Equal(2, nba.AtPeriodStart(5, 4, 1));     // 2 for each overtime, unused ones don't carry

        var col = TimeoutRules.College;
        Assert.Equal(4, col.AtPeriodStart(1, 2, 0));
        Assert.Equal(3, col.AtPeriodStart(2, 2, 4));     // at most 3 into the second half
        Assert.Equal(1, col.AtPeriodStart(2, 2, 1));
        Assert.Equal(2, col.AtPeriodStart(3, 2, 1));     // +1 in overtime, and unused ones carry

        var fiba = TimeoutRules.Fiba;
        Assert.Equal(2, fiba.AtPeriodStart(1, 4, 0));
        Assert.Equal(2, fiba.AtPeriodStart(2, 4, 2));
        Assert.Equal(3, fiba.AtPeriodStart(3, 4, 1));    // first-half ones are lost; 3 for the second half
        Assert.Equal(1, fiba.AtPeriodStart(5, 4, 2));
    }

    [Theory]
    [InlineData("nba")]
    [InlineData("fiba")]
    [InlineData("college")]
    public void Benches_call_them_within_the_rules_and_the_log_says_so(string kind)
    {
        var league = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var rules = kind == "nba" ? Rules.Nba : kind == "fiba" ? Rules.Fiba : Rules.College;
        var to = TimeoutRules.For(rules);
        var sim = new GameSimulator();
        int total = 0;
        for (int g = 0; g < 40; g++)
        {
            var r = sim.Simulate(league.Teams[g % 10], league.Teams[10 + g % 10], rules, (ulong)(900 + g), keepLog: true, date: Day);
            foreach (var home in new[] { true, false })
            {
                var calls = r.Timeouts.Where(t => t.Home == home).ToList();
                var box = home ? r.Home : r.Away;
                Assert.Equal(calls.Count, box.TimeoutsUsed);
                // What's left at the final is what the last period gave, less what was called in it.
                Assert.InRange(box.Timeouts, 0, to.AtTip + to.SecondHalfAdd + to.PerOvertime * r.Overtimes);
                // Never more than the allowance: each call leaves a count that isn't negative, and none comes at a period's tip.
                Assert.All(calls, c => Assert.True(c.Left >= 0));
                int full(int p) => p <= rules.Periods ? rules.MinutesPerPeriod * 60 : 300;
                Assert.All(calls, c => Assert.True(c.Clock < full(c.Period)));
                // Regulation: at most the allowance a game can give.
                int max = to.AtTip + to.SecondHalfAdd;
                Assert.True(calls.Count(c => c.Period <= rules.Periods) <= max, $"{kind}: {calls.Count} timeouts > {max}");
                // A stop-the-run timeout is called for a real run.
                Assert.All(calls.Where(c => c.Reason == TimeoutReason.StopRun), c => Assert.True(c.Run >= TimeoutRules.RunToStop));
            }
            Assert.Equal(r.Timeouts.Count, r.PlayLog.Count(l => l.Contains("Timeout, ")));
            total += r.Timeouts.Count;
        }
        Assert.True(total >= 40, $"{kind}: only {total} timeouts in 40 games");
    }

    [Fact]
    public void A_game_with_timeouts_is_still_the_same_game_for_the_same_seed()
    {
        var league = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        var a = sim.Simulate(league.Teams[0], league.Teams[1], Rules.Nba, 4242, keepLog: true, date: Day);
        var b = sim.Simulate(league.Teams[0], league.Teams[1], Rules.Nba, 4242, keepLog: false, date: Day);
        Assert.Equal(a.Summary(), b.Summary());
        Assert.Equal(a.Timeouts, b.Timeouts);
    }

    [Fact]
    public void The_coach_calls_one_at_the_next_dead_ball_until_none_are_left()
    {
        var league = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        var home = league.Teams[0];
        var live = sim.StartLive(home, league.Teams[1], Rules.Nba, 4242, Day);
        Assert.Equal(7, live.TimeoutsLeft(true));
        live.Step(); live.Step();   // the tip, then a possession
        int before = live.TimeoutsLeft(true);
        Assert.True(live.RequestTimeout(home));
        Assert.True(live.TimeoutPending(home));
        int count = live.Result.Timeouts.Count;
        // A live ball after a steal waits for the next dead ball.
        while (live.TimeoutPending(home)) live.Step();
        var call = live.Result.Timeouts.Skip(count).Single(t => t.Reason == TimeoutReason.Coach);
        Assert.True(call.Home);
        Assert.Equal(before - 1, call.Left);
        Assert.Contains(live.Result.PlayLog, l => l.Contains($"Timeout, {home.Abbreviation} (called by the coach; {call.LeftText})"));

        // Spend the rest: the button refuses once none are left.
        while (live.TimeoutsLeft(true) > 0 && !live.Complete)
        {
            live.RequestTimeout(home);
            live.Step();
        }
        if (!live.Complete && live.Period < 4)
        {
            Assert.Equal(0, live.TimeoutsLeft(true));
            Assert.False(live.RequestTimeout(home));
        }
        live.Finish();
        Assert.False(live.RequestTimeout(home));
    }

    [Fact]
    public void The_commentary_line_names_the_team_the_reason_and_whats_left()
    {
        var t = new TimeoutCall(2, 300, true, TimeoutReason.StopRun, 9, 3, 40, 49);
        Assert.Equal("Timeout, Duke: to stop a 9-0 run · 3 left", LiveGameViewModel.TimeoutLine("Duke", t));
        Assert.Equal("1 left", (t with { Left = 1 }).LeftText);
        Assert.Equal("none left", (t with { Left = 0 }).LeftText);
        Assert.Equal("to set up the last play", (t with { Reason = TimeoutReason.LastPlay }).Why);
    }
}
