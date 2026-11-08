using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

public class EngineTests
{
    private static League NewLeague(ulong seed = 7) =>
        new WorldGenerator(new Rng(seed)).Generate("Test League", 2026, new DateOnly(2026, 10, 1));

    [Fact]
    public void World_has_30_teams_with_15_players_each()
    {
        var league = NewLeague();
        Assert.Equal(30, league.Teams.Count);
        Assert.All(league.Teams, t => Assert.Equal(15, t.Roster.Count));
        Assert.Equal(15, league.Teams.Count(t => t.Conference == Conference.East));
    }

    [Fact]
    public void Attributes_stay_in_range_and_track_ability()
    {
        var league = NewLeague();
        var players = league.Teams.SelectMany(t => t.Roster).ToList();
        foreach (var p in players)
        foreach (var a in AttrInfo.All)
            Assert.InRange(p.Attributes[a], 1, 20);

        var best = players.OrderByDescending(p => p.CurrentAbility).First();
        var worst = players.OrderBy(p => p.CurrentAbility).First();
        Assert.True(best.Overall > worst.Overall);
        Assert.All(players, p => Assert.True(p.PotentialAbility >= p.CurrentAbility));
    }

    [Fact]
    public void Same_seed_gives_identical_game()
    {
        var league = NewLeague();
        var sim = new GameSimulator();
        var a = sim.Simulate(league.Teams[0], league.Teams[1], Rules.Nba, 12345);
        var b = sim.Simulate(league.Teams[0], league.Teams[1], Rules.Nba, 12345);
        Assert.Equal(a.Home.Points, b.Home.Points);
        Assert.Equal(a.Away.Points, b.Away.Points);
        Assert.Equal(a.Home.FGA, b.Home.FGA);
    }

    [Fact]
    public void Box_score_is_internally_consistent()
    {
        var league = NewLeague();
        var r = new GameSimulator().Simulate(league.Teams[2], league.Teams[17], Rules.Nba, 99);
        foreach (var box in new[] { r.Home, r.Away })
        {
            Assert.Equal(box.Points, box.Lines.Sum(l => (l.FGM - l.ThreePM) * 2 + l.ThreePM * 3 + l.FTM));
            Assert.True(box.FGM <= box.FGA);
            Assert.True(box.ThreePM <= box.ThreePA);
            Assert.True(box.Assists <= box.FGM);
            // Five players on the floor for the whole game: 5 × 48 minutes (plus overtime).
            double expected = 5 * (48 + 5 * r.Overtimes);
            Assert.InRange(box.Lines.Sum(l => l.Minutes), expected - 0.5, expected + 0.5);
            Assert.All(box.Lines, l => Assert.True(l.Fouls <= Rules.Nba.FoulsToFoulOut));
        }
        Assert.NotEqual(r.Home.Points, r.Away.Points);
    }

    [Fact]
    public void Schedule_is_82_games_per_team()
    {
        var league = NewLeague();
        var schedule = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), new Rng(1));
        Assert.Equal(30 * 82 / 2, schedule.Count);
        foreach (var t in league.Teams)
        {
            Assert.Equal(82, schedule.Count(g => g.Home == t || g.Away == t));
            Assert.Equal(41, schedule.Count(g => g.Home == t));
        }
        // No team plays twice in one day.
        foreach (var day in schedule.GroupBy(g => g.Date))
        {
            var ids = day.SelectMany(g => new[] { g.Home.Id, g.Away.Id }).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
    }

    [Fact]
    public void Better_team_wins_most_of_the_time()
    {
        var league = NewLeague();
        var strong = league.Teams.OrderByDescending(t => t.Roster.Take(8).Average(p => p.CurrentAbility)).First();
        var weak = league.Teams.OrderBy(t => t.Roster.Take(8).Average(p => p.CurrentAbility)).First();
        var sim = new GameSimulator();
        int wins = 0;
        for (int i = 0; i < 100; i++)
            if (sim.Simulate(strong, weak, Rules.Nba, (ulong)i).Winner.Team == strong) wins++;
        Assert.InRange(wins, 70, 100);
    }

    [Fact]
    public void League_averages_land_near_targets()
    {
        var league = NewLeague(42);
        var sim = new GameSimulator();
        var rng = new Rng(5);
        var games = new List<GameResult>();
        for (int i = 0; i < 300; i++)
        {
            var h = league.Teams[rng.Next(30)];
            Team a; do { a = league.Teams[rng.Next(30)]; } while (a == h);
            games.Add(sim.Simulate(h, a, Rules.Nba, rng.NextULong()));
        }
        var avg = LeagueAverages.From(games);
        Assert.InRange(avg.Points, 104, 124);
        Assert.InRange(avg.FGM / avg.FGA, 0.43, 0.52);
        Assert.InRange(avg.ThreePM / avg.ThreePA, 0.32, 0.41);
        Assert.InRange(avg.FTM / avg.FTA, 0.72, 0.84);
        Assert.InRange(avg.Turnovers, 10, 17);
    }
}
