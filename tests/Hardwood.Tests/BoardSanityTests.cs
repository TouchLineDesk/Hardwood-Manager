using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>The board after the critic review (Nov 10): goals move gradually, promoted clubs aim to stay up, success isn't punished.</summary>
public class BoardSanityTests
{
    private static League NewLeague() => new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));

    [Fact]
    public void A_great_regular_season_with_a_playoff_upset_is_a_disappointment_not_a_crisis()
    {
        var league = NewLeague();
        Board.EnsureOwnersAndCoaches(league, 3);
        var team = league.Teams.OrderBy(t => Board.ProjectedRank(league, t)).First();
        team.Owner.Patience = 4;
        var job = new JobState { Current = Board.SetExpectations(league, team, 2026), Confidence = 75 };
        Assert.Equal(SeasonGoal.Title, job.Current.Goal);
        Board.SeasonReview(job, new Coach(), league, team, SeasonGoal.SecondRound, 70, 12, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2027, 6, 20));
        Assert.True(job.Confidence >= 60, $"confidence {job.Confidence}");
    }

    [Fact]
    public void Goals_climb_one_step_a_season_and_promoted_clubs_aim_to_stay_up()
    {
        var league = NewLeague();
        Board.EnsureOwnersAndCoaches(league, 3);
        var ctx = LeagueContext.Nabl(league);
        var best = league.Teams.OrderBy(t => Board.ProjectedRank(league, t)).First();
        var previous = new Expectations { TeamId = best.Id, League = "nabl", Goal = SeasonGoal.Playoffs, TargetWins = 44 };
        var next = Board.NextExpectations(ctx, best, 2027, previous, "nabl", 1, 1);
        Assert.Equal(SeasonGoal.SecondRound, next.Goal);          // the squad says Title; one step at a time
        Assert.True(next.TargetWins <= 52, $"target {next.TargetWins}");

        var promoted = Board.NextExpectations(ctx, best, 2027, new Expectations { TeamId = best.Id, League = "chp", Goal = SeasonGoal.Title }, "nabl", 1, 2);
        Assert.True(promoted.Survive);
        Assert.Equal(SeasonGoal.Develop, promoted.Goal);
        Assert.InRange(promoted.TargetWins, 30, 35);

        // A new club (or the first season) takes the squad's goal as it is.
        Assert.Equal(SeasonGoal.Title, Board.NextExpectations(ctx, best, 2027, null, "nabl", 1, 0).Goal);
    }
}
