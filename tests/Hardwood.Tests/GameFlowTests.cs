using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Roadmap 2, match day part 1: the score timeline, win probability, quarter splits and scoring runs.</summary>
public class GameFlowTests
{
    private static GameResult Played(ulong seed, bool log = true)
    {
        var league = new WorldGenerator(new Rng(21)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        return new GameSimulator().Simulate(league.Teams[0], league.Teams[1], Rules.Nba, seed, keepLog: log, date: new DateOnly(2026, 11, 5));
    }

    /// <summary>A hand-made game: each entry is (seconds played, home points, away points) after a scoring possession.</summary>
    private static GameResult Made(params (double t, int h, int a)[] flow)
    {
        var league = new WorldGenerator(new Rng(22)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var r = new GameResult { Home = new TeamBox { Team = league.Teams[0] }, Away = new TeamBox { Team = league.Teams[1] } };
        foreach (var (t, h, a) in flow) r.Flow.Add(new FlowPoint(t, (int)(t / 720) + 1, h, a));
        r.Elapsed = flow.Length == 0 ? 0 : flow[^1].t;
        r.CurrentPeriod = (int)(r.Elapsed / 720) + 1;
        return r;
    }

    [Fact]
    public void Timeline_ends_on_the_final_score_and_splits_match_the_box()
    {
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            var r = Played(seed);
            Assert.NotEmpty(r.Flow);
            Assert.Equal(r.Home.Points, r.Flow[^1].Home);
            Assert.Equal(r.Away.Points, r.Flow[^1].Away);
            // Never goes backwards.
            for (int i = 1; i < r.Flow.Count; i++)
            {
                Assert.True(r.Flow[i].Elapsed >= r.Flow[i - 1].Elapsed);
                Assert.True(r.Flow[i].Home >= r.Flow[i - 1].Home && r.Flow[i].Away >= r.Flow[i - 1].Away);
            }
            var (home, away) = GameFlow.Splits(r);
            Assert.Equal(r.Home.PointsByPeriod, home);
            Assert.Equal(r.Away.PointsByPeriod, away);
        }
    }

    [Fact]
    public void Games_without_a_log_keep_no_timeline()
    {
        var r = Played(9, log: false);
        Assert.Empty(r.Flow);
        Assert.True(r.Elapsed >= 2880); // the clock is still tracked
    }

    [Fact]
    public void Win_probability_starts_from_the_pregame_line_and_ends_with_the_winner()
    {
        var r = Played(11);
        var wp = GameFlow.WinProbability(r);
        double expectedStart = 100 * GameFlow.Phi(r.PregameEdge / r.MarginSpread);
        Assert.Equal(expectedStart, wp[0].Percent, 6);
        Assert.Equal(r.Home.Points > r.Away.Points ? 100 : 0, wp[^1].Percent, 6);
        Assert.All(wp, p => Assert.InRange(p.Percent, 0, 100));
    }

    [Fact]
    public void A_lead_is_worth_more_the_later_it_is()
    {
        var r = Made();
        r.PregameEdge = 0; r.MarginSpread = 13;
        double early = GameFlow.HomeWinProbability(r, 8, 2400), late = GameFlow.HomeWinProbability(r, 8, 120);
        Assert.True(late > early && early > 0.5);
        Assert.Equal(0.5, GameFlow.HomeWinProbability(r, 0, 1440), 6);
        Assert.Equal(1.0 - GameFlow.HomeWinProbability(r, 5, 600), GameFlow.HomeWinProbability(r, -5, 600), 6);
        Assert.Equal(0.5, GameFlow.HomeWinProbability(r, 0, 0), 6); // tied at the horn: overtime
    }

    [Fact]
    public void A_twelve_two_run_is_found_and_described()
    {
        // Away scores first, then home goes 2-3-2-3, away answers with 2, home adds 2: 12-2.
        var r = Made((20, 0, 2), (60, 2, 2), (90, 5, 2), (130, 7, 2), (170, 10, 2), (200, 10, 4), (240, 12, 4));
        var runs = GameFlow.Runs(r);
        var run = Assert.Single(runs);
        Assert.True(run.Home);
        Assert.Equal("12-2", run.Score);
        Assert.Equal(60, run.StartElapsed);
        Assert.Equal(240, run.EndElapsed);
        // Still going: home scored last.
        Assert.Equal(run, GameFlow.CurrentRun(r));
        Assert.Equal($"{r.Home.Team.Abbreviation} 12-2 run (Q1, 11:00 to 8:00)", GameFlow.Describe(r, run));
        // Once the other side answers, it's over.
        r.Flow.Add(new FlowPoint(270, 1, 12, 7));
        Assert.Null(GameFlow.CurrentRun(r));
    }

    [Fact]
    public void Trading_baskets_is_not_a_run()
    {
        var flow = new List<(double, int, int)>();
        int h = 0, a = 0;
        for (int i = 1; i <= 20; i++) { if (i % 2 == 0) h += 2; else a += 2; flow.Add((i * 30, h, a)); }
        Assert.Empty(GameFlow.Runs(Made(flow.ToArray())));
    }

    [Fact]
    public void Every_run_in_a_real_game_meets_the_rule()
    {
        int found = 0;
        foreach (ulong seed in Enumerable.Range(1, 12).Select(i => (ulong)i))
        {
            var r = Played(seed);
            foreach (var run in GameFlow.Runs(r))
            {
                found++;
                Assert.True(run.For >= GameFlow.RunMinPoints && run.Against <= GameFlow.RunMaxAgainst, run.Score);
                // Check it against the timeline: the score change over the run's span.
                var before = r.Flow.LastOrDefault(p => p.Elapsed < run.StartElapsed);
                var end = r.Flow.Last(p => p.Elapsed <= run.EndElapsed);
                int forPts = run.Home ? end.Home - before.Home : end.Away - before.Away;
                int against = run.Home ? end.Away - before.Away : end.Home - before.Home;
                Assert.Equal(run.For, forPts);
                Assert.Equal(run.Against, against);
            }
        }
        Assert.True(found > 0, "twelve games and not one 8-point run");
    }

    [Fact]
    public void Live_splits_include_the_period_being_played()
    {
        var league = new WorldGenerator(new Rng(23)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var live = new GameSimulator().StartLive(league.Teams[0], league.Teams[1], Rules.Nba, 5, new DateOnly(2026, 11, 5));
        for (int i = 0; i < 130; i++) live.Step();
        var r = live.Result;
        var (home, away) = GameFlow.Splits(r);
        Assert.Equal(live.HomeScore, home.Sum());
        Assert.Equal(live.AwayScore, away.Sum());
        Assert.Equal(live.Period, r.CurrentPeriod);
        Assert.True(home[r.CurrentPeriod - 1] + away[r.CurrentPeriod - 1] > 0 || live.Clock > 600);
    }

    [Fact]
    public void Momentum_adds_up_to_the_final_margin()
    {
        var r = Played(14);
        Assert.Equal(r.Home.Points - r.Away.Points, GameFlow.Momentum(r).Sum(), 6);
    }
}
