using System.Text.Json;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// H70 (run 114), the board design pass: one judgement (<see cref="Board.Judge"/>) from owner patience and club strength to the
/// goal, the win target, the verdict and the confidence change. Beating the goal, or going up, is never "expected more".
/// </summary>
public class H70BoardTests
{
    private static readonly Dictionary<int, PlayerSeasonStats> None = new();
    private static League NewLeague()
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        return league;
    }

    /// <summary>A tier-2 style league: no NABL rules, a 62-game season, 16 playoff places.</summary>
    private static LeagueContext Tier2(League league) => new("NBA G League", league.Teams, 62, 16, false, null, -10, 2);

    [Fact]
    public void A_patient_owner_asks_a_step_less_than_an_impatient_one()
    {
        var league = NewLeague();
        var ctx = LeagueContext.Nabl(league);
        var team = league.Teams.OrderBy(t => Board.ProjectedRank(league, t)).First();
        team.Owner.Ambition = 10;
        team.Owner.Patience = 4;
        var impatient = Board.SetExpectations(ctx, team, 2026);
        team.Owner.Patience = 18;
        var patient = Board.SetExpectations(ctx, team, 2026);
        Assert.Equal(SeasonGoal.Title, impatient.Goal);
        Assert.Equal(SeasonGoal.Finals, patient.Goal);
        Assert.Equal(impatient.TargetWins - 3, patient.TargetWins);
        // A development goal can't step lower.
        var worst = league.Teams.OrderBy(t => Board.ProjectedRank(league, t)).Last();
        worst.Owner.Patience = 18; worst.Owner.Ambition = 10;
        Assert.Equal(SeasonGoal.Develop, Board.SetExpectations(ctx, worst, 2026).Goal);
    }

    [Fact]
    public void A_record_well_past_the_target_with_a_playoff_upset_is_never_expected_more()
    {
        // nba-real-strong (seed 31, main): "win the championship … reached the conference finals (69-13, target 60 wins) …
        // expected more. Board confidence: 79/100."
        var league = NewLeague();
        var team = league.Teams.First();
        var job = new JobState { Current = new Expectations { Season = 2026, Goal = SeasonGoal.Title, TargetWins = 60 }, Confidence = 70, HiredSeason = 2025 };
        var (_, body, sacked) = Board.SeasonReview(job, new Coach(), LeagueContext.Nabl(league), team, SeasonGoal.ConferenceFinals, 69, 13, None, new DateOnly(2027, 6, 1), 2026);
        Assert.False(sacked);
        Assert.DoesNotContain("expected more", body);
        Assert.True(body.Contains("is pleased with the regular season (9 wins past the target), but hoped to go further"), body);
        Assert.True(job.Confidence >= 72, $"confidence {job.Confidence}");
        Assert.Equal(2027, job.ProtectedSeason);

        // On target but out early: disappointed with the exit, not "expected more".
        var level = new JobState { Current = new Expectations { Season = 2026, Goal = SeasonGoal.Finals, TargetWins = 55 }, Confidence = 70, HiredSeason = 2025 };
        var (_, body2, _) = Board.SeasonReview(level, new Coach(), LeagueContext.Nabl(league), team, SeasonGoal.SecondRound, 56, 26, None, new DateOnly(2027, 6, 1), 2026);
        Assert.Contains("disappointed with the playoff exit; the regular season was on target", body2);

        // Short of both: still "expected more".
        var bad = new JobState { Current = new Expectations { Season = 2026, Goal = SeasonGoal.SecondRound, TargetWins = 55 }, Confidence = 80, HiredSeason = 2025 };
        var (_, body3, _) = Board.SeasonReview(bad, new Coach(), LeagueContext.Nabl(league), team, SeasonGoal.Playoffs, 48, 34, None, new DateOnly(2027, 6, 1), 2026);
        Assert.True(body3.Contains("expected more"), body3);
    }

    [Fact]
    public void Going_up_beats_any_goal_and_a_lost_promotion_playoff_says_so()
    {
        // The Kings (G League, Nov 18 hunt): "won your division and go up; won a playoff round (50-12, target 37 wins) … expected more".
        var league = NewLeague();
        var ctx = Tier2(league);
        var team = league.Teams.First();
        var job = new JobState { Current = new Expectations { Season = 2026, Goal = SeasonGoal.Title, TargetWins = 37 }, Confidence = 70, HiredSeason = 2025 };
        var (_, body, _) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.SecondRound, 50, 12, None, new DateOnly(2027, 6, 1), 2026,
            wonDivision: true, goingUp: true);
        Assert.Contains("won your division and go up", body);
        Assert.Contains("is delighted: the club goes up", body);
        Assert.DoesNotContain("expected more", body);
        Assert.True(job.Confidence > 70);

        // Won the division, lost the promotion playoff: the result says so, and the verdict follows the record.
        var lost = new JobState { Current = new Expectations { Season = 2026, Goal = SeasonGoal.Title, TargetWins = 37 }, Confidence = 70, HiredSeason = 2025 };
        var (_, body2, _) = Board.SeasonReview(lost, new Coach(), ctx, team, SeasonGoal.SecondRound, 50, 12, None, new DateOnly(2027, 6, 1), 2026,
            wonDivision: true, goingUp: false);
        Assert.Contains("won your division but lost the promotion playoff", body2);
        Assert.DoesNotContain("go up", body2);
        Assert.Contains("pleased with the regular season", body2);
    }

    [Fact]
    public void A_winning_relegation_names_the_rule()
    {
        // The Kings: "relegated … (35-27)" with "expected more" and nothing about the division rule.
        var league = NewLeague();
        var job = new JobState { Current = new Expectations { Season = 2026, Goal = SeasonGoal.Playoffs, TargetWins = 31 }, Confidence = 70, HiredSeason = 2025 };
        var (_, body, _) = Board.SeasonReview(job, new Coach(), Tier2(league), league.Teams.First(), SeasonGoal.Develop, 35, 27, None, new DateOnly(2027, 6, 1), 2026, relegated: true);
        Assert.Contains("a winning record wasn't enough to stay up, last in the division", body);
    }

    [Fact]
    public void The_verdict_never_contradicts_the_judgement()
    {
        // A sweep over goals, results and records: a goal met, a target clearly beaten, or going up never reads "expected more",
        // and "delighted" or "satisfied" never comes with a fall in confidence.
        var league = NewLeague();
        var team = league.Teams.First();
        var ctx = Tier2(league);
        int checkedCount = 0;
        foreach (var goal in Enum.GetValues<SeasonGoal>())
        foreach (var got in Enum.GetValues<SeasonGoal>())
        foreach (int wins in new[] { 20, 31, 37, 45, 55 })
        foreach (bool up in new[] { false, true })
        {
            var e = new Expectations { Season = 2026, Goal = goal, TargetWins = 37 };
            var job = new JobState { Current = e, Confidence = 60, HiredSeason = 2024 };
            var (_, body, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, got, wins, 62 - wins, None, new DateOnly(2027, 6, 1), 2026,
                wonDivision: up, goingUp: up);
            var j = Board.Judge(new Expectations { Goal = goal, TargetWins = 37 }, ctx, got, wins, 62 - wins, false, up);
            if (j.GoalMet || j.BeatTarget || up) Assert.DoesNotContain("expected more", body);
            if (body.Contains("delighted") || body.Contains("satisfied")) Assert.True(job.Confidence >= 60, $"{goal}/{got}/{wins}/{up}: {job.Confidence}");
            if (up) Assert.False(sacked);
            checkedCount++;
        }
        Assert.Equal(7 * 7 * 5 * 2, checkedCount);
    }

    [Fact]
    public void A_target_beaten_or_a_goal_met_does_not_bring_a_much_lower_target()
    {
        // nba-real-strong (seed 31, main, patient owner): targets 60 → 58 → 50 → 42 while the club won 69, 64 and 61.
        var league = NewLeague();
        var ctx = LeagueContext.Nabl(league);
        var team = league.Teams.OrderBy(t => Board.ProjectedRank(league, t)).ElementAt(14); // a mid-table squad: target about 42
        team.Owner.Patience = 10; team.Owner.Ambition = 10;
        var squad = Board.SetExpectations(ctx, team, 2027);
        Assert.True(squad.TargetWins < 50, $"squad target {squad.TargetWins}");
        var prev = new Expectations { Season = 2026, Goal = SeasonGoal.Finals, TargetWins = 58, TeamId = team.Id, League = "nabl", Achieved = SeasonGoal.ConferenceFinals, Wins = 64 };
        var next = Board.NextExpectations(ctx, team, 2027, prev, "nabl", 1, 1);
        Assert.Equal(55, next.TargetWins);
        // Missed both: the target may fall further (by up to 8).
        var missed = new Expectations { Season = 2026, Goal = SeasonGoal.Finals, TargetWins = 58, TeamId = team.Id, League = "nabl", Achieved = SeasonGoal.Playoffs, Wins = 44 };
        Assert.True(Board.NextExpectations(ctx, team, 2027, missed, "nabl", 1, 1).TargetWins <= 50);
        // An older save without the wins keeps the old rules.
        var old = new Expectations { Season = 2026, Goal = SeasonGoal.Finals, TargetWins = 58, TeamId = team.Id, League = "nabl", Achieved = SeasonGoal.ConferenceFinals };
        Assert.Equal(50, Board.NextExpectations(ctx, team, 2027, old, "nabl", 1, 1).TargetWins);
    }

    [Fact]
    public void College_goals_move_one_step_along_their_own_ladder()
    {
        // The Elite Eight (ConferenceFinals) wasn't on the old ladder, so a program set "reach the Elite Eight" could be asked for the
        // title next year.
        var league = NewLeague();
        var ctx = new LeagueContext("College basketball", league.Teams, 34, 68, false, null, -10, 0, true);
        var team = league.Teams.OrderBy(t => Board.ProjectedRank(ctx, t)).First();
        team.Owner.Patience = 10; team.Owner.Ambition = 10;
        Assert.Equal(SeasonGoal.Title, Board.SetExpectations(ctx, team, 2027).Goal);
        var prev = new Expectations { Season = 2026, Goal = SeasonGoal.ConferenceFinals, TargetWins = 25, TeamId = team.Id, League = "college", Achieved = SeasonGoal.ConferenceFinals, Wins = 27 };
        Assert.Equal(SeasonGoal.Finals, Board.NextExpectations(ctx, team, 2027, prev, "college", 0, 0).Goal);
        Assert.Equal(SeasonGoal.ConferenceFinals, Board.StepDown(SeasonGoal.Finals, ctx));
        Assert.Equal(SeasonGoal.SecondRound, Board.StepDown(SeasonGoal.Finals, Tier2(league)));
        Assert.Equal(SeasonGoal.Develop, Board.StepDown(SeasonGoal.Develop, ctx));
    }

    [Fact]
    public void Expectations_from_an_older_save_load_without_the_wins()
    {
        var e = JsonSerializer.Deserialize<Expectations>("{\"Season\":2026,\"Goal\":3,\"TargetWins\":45,\"Achieved\":3}")!;
        Assert.Null(e.Wins);
        Assert.Equal(SeasonGoal.SecondRound, e.Achieved);
        var round = JsonSerializer.Deserialize<Expectations>(JsonSerializer.Serialize(new Expectations { Wins = 51 }))!;
        Assert.Equal(51, round.Wins);
    }
}
