using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 176, a polish run (college):
/// - H182: a college coach isn't sacked before the end of his third season at a program, unless it collapses (under .300
///   two seasons running). Providence's coach was let go after 17-13 and 13-16 in the Big East.
/// - H119: a college career's draft board shows a graduate's current ability as a scouting range, like his potential.
/// - H185: three or four wins short of the target is "N wins short", not "well short".
/// </summary>
public class Run176PolishTests
{
    private readonly ITestOutputHelper _out;
    public Run176PolishTests(ITestOutputHelper o) => _out = o;
    private static readonly Dictionary<int, PlayerSeasonStats> None = new();

    private static (LeagueContext Ctx, Team Team) College(int patience = 7)
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        var ctx = new LeagueContext("College basketball", league.Teams, 29, 68, false, null, -10, 0, true);
        var team = league.Teams[5];
        team.Owner.Patience = patience; team.Owner.Ambition = 7;
        return (ctx, team);
    }

    private static JobState Job(Team team, int season, int hired, double confidence, double? lastPct = null) => new()
    {
        Confidence = confidence, HiredSeason = hired, LastSeasonPct = lastPct,
        Current = new Expectations { Season = season, Goal = SeasonGoal.Playoffs, TargetWins = 19, TeamId = team.Id, League = "college" },
    };

    [Fact]
    public void A_college_coach_keeps_the_job_through_his_second_season()
    {
        var (ctx, team) = College();
        // Providence, seed 77: 17-13 then 13-16, AD confidence low going into the second review.
        var job = Job(team, 2027, 2026, 22, lastPct: 17 / 30.0);
        var (title, body, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Develop, 13, 16, None, new DateOnly(2028, 4, 7), 2027);
        _out.WriteLine(body);
        Assert.False(sacked);
        Assert.Equal("Season review", title);
        Assert.Contains("third season", body);
        Assert.Equal(13 / 29.0, job.LastSeasonPct!.Value, 3);

        // Mid-season, too: no weekly sacking in the second season.
        var weekly = Job(team, 2027, 2026, 2, lastPct: 17 / 30.0);
        var (_, _, firedWeekly) = Board.Weekly(weekly, ctx, team, 4, 16, None, new DateOnly(2028, 2, 10), 2027);
        Assert.False(firedWeekly);
    }

    [Fact]
    public void A_collapse_two_seasons_running_can_still_cost_the_job()
    {
        var (ctx, team) = College();
        var job = Job(team, 2027, 2026, 10, lastPct: 7 / 29.0);
        var (_, body, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Develop, 6, 23, None, new DateOnly(2028, 4, 7), 2027);
        _out.WriteLine(body);
        Assert.True(sacked);
    }

    [Fact]
    public void The_third_season_is_judged_as_before()
    {
        var (ctx, team) = College();
        var job = Job(team, 2028, 2026, 10, lastPct: 13 / 29.0);
        var (_, _, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Develop, 11, 18, None, new DateOnly(2029, 4, 7), 2028);
        Assert.True(sacked);
        // The pros are unchanged: a second NBA season can end in the sack.
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        var pro = league.Teams[5]; pro.Owner.Patience = 7;
        var pj = new JobState { Confidence = 5, HiredSeason = 2025, Current = new Expectations { Season = 2026, Goal = SeasonGoal.Playoffs, TargetWins = 45 } };
        var (_, _, proSacked) = Board.SeasonReview(pj, new Coach(), league, pro, SeasonGoal.Develop, 25, 57, None, new DateOnly(2027, 4, 20));
        Assert.True(proSacked);
    }

    [Fact]
    public void A_college_graduates_ability_is_a_scouting_range()
    {
        var league = new WorldGenerator(new Rng(5)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        int checkedCount = 0;
        foreach (var p in league.Teams.SelectMany(t => t.Roster))
            foreach (double k in new[] { 0.3, 0.55, 0.85 })
            {
                var ca = Scouting.CollegeAbility(p, k);
                var pa = Scouting.CollegePotential(p, k);
                Assert.InRange(p.CurrentAbility, ca.Low, ca.High);
                Assert.True(ca.High <= Math.Max(pa.High, p.CurrentAbility), $"{p.Name}: CA {ca} PA {pa}");
                Assert.True(ca.High - ca.Low <= 14, $"{ca}");
                Assert.Equal(ca, Scouting.CollegeAbility(p, k)); // stable from screen to screen
                checkedCount++;
            }
        Assert.True(checkedCount > 100);
        var any = league.Teams[0].Roster[0];
        _out.WriteLine($"{any.Name}: CA {any.CurrentAbility} → {Scouting.CollegeAbility(any, 0.55)}, PA {Scouting.CollegePotential(any, 0.55)}");
        Assert.False(Scouting.CollegeAbility(any, 0.55).Exact);
    }

    [Theory]
    [InlineData(16, 14, 19, "16-14 is 3 wins short of the 19-win target")]
    [InlineData(15, 15, 19, "15-15 is 4 wins short of the 19-win target")]
    [InlineData(14, 16, 19, "14-16 is well short of the 19-win target")]
    public void Three_or_four_short_is_not_well_short(int wins, int losses, int target, string expected) =>
        Assert.Equal(expected, Board.TargetPhrase(wins, losses, target));
}
