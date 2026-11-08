using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

public class LiveGameTests
{
    [Fact]
    public void Stepping_to_the_end_matches_an_instant_sim_with_the_same_seed()
    {
        var league = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        var instant = sim.Simulate(league.Teams[0], league.Teams[1], Rules.Nba, 4242, keepLog: true, date: new DateOnly(2026, 11, 5));
        var live = sim.StartLive(league.Teams[0], league.Teams[1], Rules.Nba, 4242, new DateOnly(2026, 11, 5));
        int steps = 0;
        while (live.Step()) steps++;
        Assert.True(steps > 150);
        Assert.True(live.Complete);
        Assert.Equal(instant.Home.Points, live.HomeScore);
        Assert.Equal(instant.Away.Points, live.AwayScore);
        Assert.Equal(instant.Home.FGA, live.Result.Home.FGA);
        Assert.Equal(instant.PlayLog.Count, live.Log.Count);
        Assert.Equal(instant.Injuries.Count, live.Result.Injuries.Count);
    }

    [Fact]
    public void Requested_lineup_takes_the_floor_at_the_next_dead_ball()
    {
        var league = new WorldGenerator(new Rng(18)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var home = league.Teams[2];
        var sim = new GameSimulator();
        var live = sim.StartLive(home, league.Teams[3], Rules.Nba, 7, new DateOnly(2026, 11, 5));
        for (int i = 0; i < 20; i++) live.Step();
        var bench = home.Roster.Where(p => !live.HomeOnCourt.Contains(p)).OrderByDescending(p => p.Overall).Take(5).ToList();
        live.RequestLineup(home, bench);
        live.Step();
        Assert.Equal(5, live.HomeOnCourt.Intersect(bench).Count());
        Assert.Contains(live.Log, l => l.Contains("Lineup change"));
        live.Finish();
        Assert.True(live.Complete);
    }

    [Fact]
    public void Live_day_in_a_session_records_the_result_like_any_other()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-live-{Guid.NewGuid():N}.save");
        try
        {
            var s = GameSession.NewGame(55, humanTeamId: 9, path);
            while (!s.HumanPlaysToday) s.AdvanceDay();
            int before = s.Results.Count;
            var (live, played) = s.BeginLiveDay();
            Assert.NotNull(live);
            Assert.Equal(before + played.Count, s.Results.Count); // the human game is not recorded yet
            for (int i = 0; i < 40; i++) live!.Step();
            s.HumanTeam.Tactic.ThreePointEmphasis = 90;
            live!.Finish();
            int others = played.Count;
            s.FinishLiveDay(live, played);
            Assert.Equal(before + others + 1, s.Results.Count);
            Assert.Equal(1, s.Standings[s.HumanTeam].Games);
            Assert.Contains(s.Inbox, m => m.Category == "Game");
            Assert.True(s.Stats.Values.Any(x => x.TeamId == 9 && x.Games == 1));

            // A reload sees the same game count and date.
            var loaded = GameSession.Load(path);
            Assert.Equal(s.Results.Count, loaded.Results.Count);
            Assert.Equal(s.Today, loaded.Today);

            // Playoff live games too: run to the postseason and play our series live if we are in it.
            int guard = 0;
            while (loaded.Phase == SessionPhase.RegularSeason && guard++ < 300) loaded.AdvanceDay();
            Assert.Equal(SessionPhase.Postseason, loaded.Phase);
            guard = 0;
            while (loaded.Phase == SessionPhase.Postseason && guard++ < 120)
            {
                if (loaded.HumanPlaysToday)
                {
                    var (l2, p2) = loaded.BeginLiveDay();
                    Assert.NotNull(l2);
                    l2!.Finish();
                    loaded.FinishLiveDay(l2, p2);
                }
                else loaded.AdvanceDay();
            }
            Assert.Equal(SessionPhase.Offseason, loaded.Phase);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
