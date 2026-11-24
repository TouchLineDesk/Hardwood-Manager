using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 177 (K28, critic review #22): late-game fouling and late timeouts. College benches foul earlier and
/// further back; the pros keep the 40-second window.</summary>
public class Run177LateGameTests
{
    private static readonly DateOnly Day = new(2026, 11, 5);

    [Theory]
    // college, final period, clock, lead of the side with the ball, one-and-one next → expected
    [InlineData(true, true, 17, 7, false, LateGame.Foul.StopClock)]    // Miami, down 7 with 17 s left
    [InlineData(true, true, 20, 9, true, LateGame.Foul.StopClock)]     // Creighton, down 9 with 20 s left in March
    [InlineData(true, true, 55, 4, false, LateGame.Foul.StopClock)]
    [InlineData(true, true, 55, 10, false, LateGame.Foul.StopClock)]
    [InlineData(true, true, 55, 11, false, LateGame.Foul.None)]        // 11 with a minute left and no one-and-one: play it out
    [InlineData(true, true, 55, 11, true, LateGame.Foul.OneAndOne)]
    [InlineData(true, true, 100, 9, true, LateGame.Foul.OneAndOne)]    // 1:40, down 9, the one-and-one
    [InlineData(true, true, 100, 9, false, LateGame.Foul.None)]        // ... but not into the double bonus
    [InlineData(true, true, 100, 13, true, LateGame.Foul.None)]
    [InlineData(true, true, 130, 9, true, LateGame.Foul.None)]
    [InlineData(true, true, 50, 2, false, LateGame.Foul.None)]         // down 2 with 50 s: defend
    [InlineData(true, true, 35, 2, false, LateGame.Foul.StopClock)]    // the old window
    [InlineData(true, false, 20, 7, false, LateGame.Foul.None)]        // the first half
    [InlineData(true, true, 2, 7, false, LateGame.Foul.None)]          // too late to matter
    [InlineData(false, true, 17, 7, false, LateGame.Foul.None)]        // the NBA keeps its rules
    [InlineData(false, true, 35, 5, false, LateGame.Foul.StopClock)]
    [InlineData(false, true, 100, 9, false, LateGame.Foul.None)]
    public void The_trailing_side_fouls_inside_the_window(bool college, bool finalPeriod, double clock, int lead, bool oneAndOne, LateGame.Foul want)
        => Assert.Equal(want, LateGame.ShouldFoul(college, finalPeriod, clock, lead, oneAndOne));

    [Fact]
    public void A_college_side_down_four_to_ten_late_hurries_and_looks_for_three()
    {
        var (s, three) = LateGame.Hurry(true, true, 45, -7, 20);
        Assert.Equal(9, s, 3);
        Assert.True(three);
        (s, three) = LateGame.Hurry(true, true, 45, -5, 20);
        Assert.False(three);                                   // down 5 with 45 s: a quick two will do
        Assert.True(LateGame.Hurry(true, true, 25, -5, 20).NeedThree);
        Assert.Equal(3, LateGame.Hurry(true, true, 45, -5, 4).Seconds, 3);   // never under 3 s ...
        Assert.Equal(2, LateGame.Hurry(true, true, 45, -5, 2).Seconds, 3);   // ... and never longer than the trip was
        Assert.Equal((20.0, false), LateGame.Hurry(false, true, 45, -7, 20));   // the NBA: unchanged
        Assert.Equal((20.0, false), LateGame.Hurry(true, true, 45, -3, 20));
        Assert.Equal((20.0, false), LateGame.Hurry(true, true, 45, -11, 20));
        Assert.Equal((20.0, false), LateGame.Hurry(true, true, 70, -7, 20));
    }

    [Fact]
    public void College_games_foul_on_every_late_trip_and_the_log_says_why()
    {
        var league = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        int chances = 0, inWindow = 0, deep = 0, oneAndOne = 0;
        for (int g = 0; g < 160; g++)
        {
            var r = sim.Simulate(league.Teams[g % 10], league.Teams[10 + g % 10], Rules.College, (ulong)(1700 + g), keepLog: true, date: Day);
            chances += r.LateFoulChances;
            foreach (var f in r.LateFouls)
            {
                Assert.True(f.Period >= Rules.College.Periods);
                Assert.InRange(f.Margin, LateGame.ShortMin, LateGame.BonusMax);
                if (f.Clock <= LateGame.FoulWindowSeconds && f.Margin is >= LateGame.FoulMin and <= LateGame.FoulMax) inWindow++;
                if (f.Margin >= 7 && f.Margin <= 10) deep++;
                if (f.Kind == LateGame.Foul.OneAndOne) { oneAndOne++; Assert.True(f.Clock <= LateGame.BonusWindowSeconds); }
            }
            Assert.Equal(r.LateFouls.Count(f => f.Kind == LateGame.Foul.OneAndOne),
                r.PlayLog.Count(l => l.Contains("sending him to the one-and-one")));
        }
        Assert.True(chances >= 30, $"only {chances} late trips in 160 games");
        // Every trip the trailing side could foul on, it did (before K28, only the last 40 s and down one to six).
        Assert.Equal(chances, inWindow);
        Assert.True(deep > 0 && oneAndOne > 0, $"down 7-10: {deep}, one-and-one: {oneAndOne}");
    }

    [Fact]
    public void Nba_games_never_foul_outside_the_old_window()
    {
        var league = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        for (int g = 0; g < 80; g++)
        {
            var r = sim.Simulate(league.Teams[g % 10], league.Teams[10 + g % 10], Rules.Nba, (ulong)(2700 + g), date: Day);
            Assert.Equal(0, r.LateFoulChances);
            Assert.All(r.LateFouls, f =>
            {
                Assert.True(f.Clock <= LateGame.ShortWindowSeconds);
                Assert.InRange(f.Margin, LateGame.ShortMin, LateGame.ShortMax);
                Assert.Equal(LateGame.Foul.StopClock, f.Kind);
            });
        }
    }

    [Fact]
    public void Late_timeouts_are_about_the_clock_not_the_legs()
    {
        var league = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        foreach (var rules in new[] { Rules.College, Rules.Nba })
            for (int g = 0; g < 60; g++)
            {
                var r = sim.Simulate(league.Teams[g % 10], league.Teams[10 + g % 10], rules, (ulong)(3700 + g), date: Day);
                Assert.DoesNotContain(r.Timeouts, t => t.Reason == TimeoutReason.Breather && t.Period >= rules.Periods && t.Clock <= 120);
            }
        var call = new TimeoutCall(2, 60, true, TimeoutReason.StopClock, 0, 1, 60, 65);
        Assert.Equal("to stop the clock", call.Why);
        Assert.Equal("to set up a play", (call with { Reason = TimeoutReason.LatePlay }).Why);
        Assert.Equal("to set his defense", (call with { Reason = TimeoutReason.SetDefense }).Why);
    }
}
