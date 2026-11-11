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
    public void Every_playoff_exit_is_judged_by_the_round_it_came_in()
    {
        // Review #7: the bracket names rounds "East First Round", "West Semifinals", "East Finals", and the board matched
        // only plain names, so a 64-18 second seed that went out in the playoffs was reviewed as "reach the play-in".
        var league = NewLeague();
        var post = new Postseason(league, new Standings(league));
        var sim = new Hardwood.Core.Engine.GameSimulator(); var rng = new Rng(7); var day = new DateOnly(2027, 4, 15);
        for (int i = 0; i < 200 && !post.Complete; i++, day = day.AddDays(1)) { post.PlayDay(sim, rng, day); post.AfterDay(); }
        Assert.True(post.Complete);
        var got = league.Teams.GroupBy(t => Board.Achieved(t, post)).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(1, got.GetValueOrDefault(SeasonGoal.Title));
        Assert.Equal(1, got.GetValueOrDefault(SeasonGoal.Finals));
        Assert.Equal(2, got.GetValueOrDefault(SeasonGoal.ConferenceFinals));
        Assert.Equal(4, got.GetValueOrDefault(SeasonGoal.SecondRound));
        Assert.Equal(8, got.GetValueOrDefault(SeasonGoal.Playoffs));
        Assert.Equal(4, got.GetValueOrDefault(SeasonGoal.PlayIn));
        Assert.Equal(league.Teams.Count - 20, got.GetValueOrDefault(SeasonGoal.Develop));
        // Each team's result matches the last series it lost.
        foreach (var s in post.Series.Where(s => s.Loser != null && !s.Round.Contains("Play-In")))
            if (post.Series.Last(x => x.Higher == s.Loser || x.Lower == s.Loser) == s)
                Assert.Equal(Board.RoundReached(s.Round), Board.Achieved(s.Loser!, post));
    }

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

        // A near miss on the win target and an early exit: a warning, not the sack.
        var near = new JobState { Current = Board.SetExpectations(league, team, 2026), Confidence = 57, HiredSeason = 2025 };
        near.Current.Goal = SeasonGoal.ConferenceFinals; near.Current.TargetWins = 56;
        var (_, _, sacked) = Board.SeasonReview(near, new Coach(), league, team, SeasonGoal.Playoffs, 54, 28, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2028, 6, 20));
        Assert.False(sacked);
        Assert.True(near.Confidence >= 40, $"confidence {near.Confidence}");

        // A clearly winning season is never a sacking, even from a low base (critic review #3: 51-31 a year after 72-10).
        var low = new JobState { Current = Board.SetExpectations(league, team, 2026), Confidence = 15, HiredSeason = 2025 };
        low.Current.Goal = SeasonGoal.ConferenceFinals; low.Current.TargetWins = 62;
        var (_, _, lowSacked) = Board.SeasonReview(low, new Coach(), league, team, SeasonGoal.Playoffs, 51, 31, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2028, 6, 20));
        Assert.False(lowSacked);
        Assert.True(low.Confidence >= 25, $"confidence {low.Confidence}");
    }

    [Fact]
    public void Beating_the_target_buys_a_protected_season_but_not_a_free_pass()
    {
        // Critic review #5: 59-23 (target ~49) then 41-41 was sacked on May 3.
        var league = NewLeague();
        Board.EnsureOwnersAndCoaches(league, 3);
        var team = league.Teams.OrderBy(t => Board.ProjectedRank(league, t)).First();
        team.Owner.Patience = 3;
        var none = new Dictionary<int, PlayerSeasonStats>();
        var job = new JobState { Current = Board.SetExpectations(league, team, 2026), Confidence = 60, HiredSeason = 2025 };
        job.Current.Goal = SeasonGoal.Playoffs; job.Current.TargetWins = 49;
        Board.SeasonReview(job, new Coach(), LeagueContext.Nabl(league), team, SeasonGoal.Playoffs, 59, 23, none, new DateOnly(2027, 6, 20), 2026);
        Assert.Equal(2027, job.ProtectedSeason);
        Assert.Equal("credit", job.ProtectedReason);

        // The next season goes badly: 41-41 against a Finals goal and a 57-win target, confidence on the floor.
        job.Current = Board.SetExpectations(league, team, 2027);
        job.Current.Goal = SeasonGoal.Finals; job.Current.TargetWins = 57; job.Confidence = 5;
        var (_, _, midSeason) = Board.Weekly(job, LeagueContext.Nabl(league), team, 20, 30, none, new DateOnly(2028, 2, 10), 2027);
        Assert.False(midSeason);
        var (_, body, sacked) = Board.SeasonReview(job, new Coach(), LeagueContext.Nabl(league), team, SeasonGoal.Develop, 41, 41, none, new DateOnly(2028, 5, 3), 2027);
        Assert.False(sacked);
        Assert.Contains("bought you time", body);
        Assert.NotEqual(2028, job.ProtectedSeason); // no credit carried forward from a bad year

        // The year after that there's no shield: the same season is a sacking.
        job.Current = Board.SetExpectations(league, team, 2028);
        job.Current.Goal = SeasonGoal.Finals; job.Current.TargetWins = 57; job.Confidence = 5;
        var (_, _, sackedNow) = Board.SeasonReview(job, new Coach(), LeagueContext.Nabl(league), team, SeasonGoal.Develop, 41, 41, none, new DateOnly(2029, 5, 3), 2028);
        Assert.True(sackedNow);
    }

    [Fact]
    public void Winning_the_title_buys_a_protected_season()
    {
        // The seed-31 career: title at 59-23 against a 60-win target, then 41-41 against a Finals goal.
        var league = NewLeague();
        Board.EnsureOwnersAndCoaches(league, 3);
        var team = league.Teams.OrderBy(t => Board.ProjectedRank(league, t)).First();
        var none = new Dictionary<int, PlayerSeasonStats>();
        var job = new JobState { Current = Board.SetExpectations(league, team, 2026), Confidence = 70, HiredSeason = 2025 };
        job.Current.Goal = SeasonGoal.Title; job.Current.TargetWins = 60;
        Board.SeasonReview(job, new Coach(), LeagueContext.Nabl(league), team, SeasonGoal.Title, 59, 23, none, new DateOnly(2027, 6, 20), 2026);
        Assert.Equal(2027, job.ProtectedSeason);
        // Missing the goal by a mile earns nothing.
        var miss = new JobState { Current = Board.SetExpectations(league, team, 2026), Confidence = 70, HiredSeason = 2025 };
        miss.Current.Goal = SeasonGoal.Playoffs; miss.Current.TargetWins = 50;
        Board.SeasonReview(miss, new Coach(), LeagueContext.Nabl(league), team, SeasonGoal.Develop, 30, 52, none, new DateOnly(2027, 6, 20), 2026);
        Assert.Equal(0, miss.ProtectedSeason);
    }

    [Fact]
    public void A_protected_season_still_ends_in_the_sack_below_the_floor()
    {
        var league = NewLeague();
        Board.EnsureOwnersAndCoaches(league, 3);
        var team = league.Teams.First();
        team.Owner.Patience = 3;
        var job = new JobState { Current = Board.SetExpectations(league, team, 2027), Confidence = 5, HiredSeason = 2025, ProtectedSeason = 2027, ProtectedReason = "rebuild" };
        job.Current.Goal = SeasonGoal.Playoffs; job.Current.TargetWins = 45;
        // Mid-season: protected, however bad.
        var (_, _, mid) = Board.Weekly(job, LeagueContext.Nabl(league), team, 10, 40, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2028, 2, 10), 2027);
        Assert.False(mid);
        // Season end below 40% wins: the board acts.
        var (_, _, sacked) = Board.SeasonReview(job, new Coach(), LeagueContext.Nabl(league), team, SeasonGoal.Develop, 29, 53, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2028, 5, 3), 2027);
        Assert.True(sacked);
    }

    [Fact]
    public void Older_saves_load_without_a_protected_season()
    {
        var job = SaveFile_FromJson("{\"Employed\":true,\"Confidence\":50,\"HiredSeason\":2026}");
        Assert.Equal(0, job.ProtectedSeason);
        Assert.Equal("", job.ProtectedReason);
    }

    // Saves store the job as plain JSON (CareerStore "job"); fields an older save lacks keep their defaults.
    private static JobState SaveFile_FromJson(string json) => System.Text.Json.JsonSerializer.Deserialize<JobState>(json)!;

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

        // Relegated (level 1 → 2): a rebuild year, at most the playoffs and half the games won.
        var relegated = Board.NextExpectations(ctx, best, 2027, new Expectations { TeamId = best.Id, League = "nabl", Goal = SeasonGoal.Playoffs }, "chp", 2, 1);
        Assert.True((int)relegated.Goal <= (int)SeasonGoal.Playoffs, $"goal {relegated.Goal}");
        Assert.True(relegated.TargetWins <= (int)Math.Round(ctx.SeasonGames * 0.5), $"target {relegated.TargetWins}");

        // A new club (or the first season) takes the squad's goal as it is.
        Assert.Equal(SeasonGoal.Title, Board.NextExpectations(ctx, best, 2027, null, "nabl", 1, 0).Goal);
    }

    [Fact]
    public void A_missed_goal_is_not_raised_the_next_season()
    {
        // Critic review #4: Japan's goal climbed to "reach the Finals" after a 5th-place finish.
        var league = NewLeague();
        Board.EnsureOwnersAndCoaches(league, 3);
        var ctx = LeagueContext.Nabl(league);
        var best = league.Teams.OrderBy(t => Board.ProjectedRank(league, t)).First();
        var missed = new Expectations { TeamId = best.Id, League = "nabl", Goal = SeasonGoal.SecondRound, TargetWins = 50, Achieved = SeasonGoal.Playoffs };
        var next = Board.NextExpectations(ctx, best, 2027, missed, "nabl", 1, 1);
        Assert.True(next.Goal <= SeasonGoal.SecondRound, $"{next.Goal}");
        Assert.True(next.TargetWins <= 50);
        // Met it: one step up is allowed.
        var met = new Expectations { TeamId = best.Id, League = "nabl", Goal = SeasonGoal.SecondRound, TargetWins = 50, Achieved = SeasonGoal.SecondRound };
        Assert.Equal(SeasonGoal.ConferenceFinals, Board.NextExpectations(ctx, best, 2027, met, "nabl", 1, 1).Goal);
    }

    [Fact]
    public void A_goal_falls_at_most_one_step_and_eight_wins_a_season()
    {
        // Critic review #6 (#152): Milwaukee went 48-34 against "make the playoffs, about 49 wins" and was then set
        // "develop, about 31 wins". Goals now fall the way they rise: one step, and the target at most 8 of 82.
        var league = NewLeague();
        Board.EnsureOwnersAndCoaches(league, 3);
        var ctx = LeagueContext.Nabl(league);
        var worst = league.Teams.OrderBy(t => Board.ProjectedRank(league, t)).Last();
        worst.Owner.Ambition = 10; // no owner demand lifting the goal on its own
        Assert.Equal(SeasonGoal.Develop, Board.SetExpectations(ctx, worst, 2027).Goal); // what the squad alone would say

        var missed = new Expectations { TeamId = worst.Id, League = "nabl", Goal = SeasonGoal.Playoffs, TargetWins = 49, Achieved = SeasonGoal.PlayIn };
        var next = Board.NextExpectations(ctx, worst, 2027, missed, "nabl", 1, 1);
        Assert.Equal(SeasonGoal.PlayIn, next.Goal);
        Assert.Equal(41, next.TargetWins);

        // From the title, a collapse in squad strength still only drops the bar one step.
        var champs = new Expectations { TeamId = worst.Id, League = "nabl", Goal = SeasonGoal.Title, TargetWins = 60, Achieved = SeasonGoal.Title };
        var after = Board.NextExpectations(ctx, worst, 2027, champs, "nabl", 1, 1);
        Assert.Equal(SeasonGoal.Finals, after.Goal);
        Assert.Equal(52, after.TargetWins);

        // Promotion and relegation still reset the bar (handled before the symmetry rule).
        var relegated = Board.NextExpectations(ctx, worst, 2027, new Expectations { TeamId = worst.Id, League = "nabl", Goal = SeasonGoal.Title, TargetWins = 60 }, "chp", 2, 1);
        Assert.True((int)relegated.Goal <= (int)SeasonGoal.Playoffs);

        // A goal that was met falls one step at most as well.
        var euro = Board.NextExpectations(ctx, worst, 2027, new Expectations { TeamId = worst.Id, League = "nabl", Goal = SeasonGoal.SecondRound, TargetWins = 50, Achieved = SeasonGoal.SecondRound }, "nabl", 1, 1);
        Assert.Equal(SeasonGoal.Playoffs, euro.Goal);
    }
}
