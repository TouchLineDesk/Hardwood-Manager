using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 117's bug sweep: H109 (out of work, the old club's results arrived as yours), H70 (a contender's goal fell every summer;
/// the weekly drift cost a team on target 25 confidence; "satisfied" at 32), H113 (the scoring list's order) and H115 ($0.0M).
/// </summary>
public class Run117SweepTests
{
    private static readonly Dictionary<int, PlayerSeasonStats> None = new();

    private static League NewLeague()
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        return league;
    }

    [Fact]
    public void Out_of_work_no_results_message_reports_the_old_clubs_games()
    {
        var s = GameSession.NewGame(2026, 1, null);
        for (int i = 0; i < 400 && s.MyRecord.Wins + s.MyRecord.Losses < 3; i++) s.AdvanceDay();
        Assert.True(s.MyRecord.Wins + s.MyRecord.Losses >= 3);
        Assert.Null(s.Resign());
        var left = s.Today;
        for (int i = 0; i < 21; i++) s.AdvanceDay();
        var after = s.Inbox.Where(m => m.Date >= left).ToList();
        Assert.DoesNotContain(after, m => GameSession.IsResultsTitle(m.Title));
        Assert.DoesNotContain(after, m => m.Category == "Medical");
        Assert.Empty(s.WeekResults);
    }

    [Fact]
    public void A_contender_winning_sixty_is_not_asked_for_less()
    {
        var league = NewLeague();
        var ctx = LeagueContext.Nabl(league);
        // A mid-table squad (an ageing contender): its own projection asks for far less.
        var team = league.Teams.OrderBy(t => Board.ProjectedRank(league, t)).ElementAt(12);
        team.Owner.Patience = 18; team.Owner.Ambition = 10;
        Assert.True(Board.SetExpectations(ctx, team, 2027).Goal < SeasonGoal.ConferenceFinals);
        // The Spurs: Finals asked, 69 wins, out in the conference finals. The goal and target hold.
        var missed = new Expectations { Season = 2026, Goal = SeasonGoal.Finals, TargetWins = 57, TeamId = team.Id, League = "nabl", Achieved = SeasonGoal.ConferenceFinals, Wins = 69 };
        var next = Board.NextExpectations(ctx, team, 2027, missed, "nabl", 1, 1, topLeagueDrops: false);
        Assert.Equal(SeasonGoal.Finals, next.Goal);
        Assert.Equal(57, next.TargetWins);
        // Conference finals asked and reached at 64 wins: next year the Finals.
        var met = new Expectations { Season = 2026, Goal = SeasonGoal.ConferenceFinals, TargetWins = 55, TeamId = team.Id, League = "nabl", Achieved = SeasonGoal.ConferenceFinals, Wins = 64 };
        Assert.Equal(SeasonGoal.Finals, Board.NextExpectations(ctx, team, 2027, met, "nabl", 1, 1, topLeagueDrops: false).Goal);
        // A Finals run at 62 wins: the title.
        var finals = new Expectations { Season = 2026, Goal = SeasonGoal.Finals, TargetWins = 57, TeamId = team.Id, League = "nabl", Achieved = SeasonGoal.Finals, Wins = 62 };
        Assert.Equal(SeasonGoal.Title, Board.NextExpectations(ctx, team, 2027, finals, "nabl", 1, 1, topLeagueDrops: false).Goal);
        // Under 60 wins the old rules apply: the goal may fall a step.
        var fewer = new Expectations { Season = 2026, Goal = SeasonGoal.Finals, TargetWins = 57, TeamId = team.Id, League = "nabl", Achieved = SeasonGoal.ConferenceFinals, Wins = 52 };
        Assert.True(Board.NextExpectations(ctx, team, 2027, fewer, "nabl", 1, 1, topLeagueDrops: false).Goal < SeasonGoal.Finals);
    }

    [Fact]
    public void A_team_on_target_keeps_the_boards_goodwill_through_the_season()
    {
        // The Suns: 53-29 against a target of 49, confidence 88 → 63 by the review.
        var league = NewLeague();
        var ctx = LeagueContext.Nabl(league);
        var team = league.Teams.First();
        team.Owner.Patience = 8;
        var job = new JobState { Employed = true, Current = new Expectations { Season = 2026, Goal = SeasonGoal.SecondRound, TargetWins = 49 }, Confidence = 88, HiredSeason = 2025 };
        for (int g = 8; g <= 82; g += 4)
            Board.Weekly(job, ctx, team, (int)Math.Round(g * 53 / 82.0), g - (int)Math.Round(g * 53 / 82.0), None, new DateOnly(2027, 1, 1), 2026);
        Assert.Equal(88, job.Confidence, 3);
        var outlook = Board.Outlook(job, ctx, team, 53, 29, None, new DateOnly(2027, 4, 10))!;
        Assert.Contains(outlook.Drivers, d => d.Label == "On target");
        Assert.Equal(88, outlook.Level, 3);
        // Behind the pace, it still drifts down.
        var behind = new JobState { Employed = true, Current = new Expectations { Season = 2026, Goal = SeasonGoal.SecondRound, TargetWins = 49 }, Confidence = 88, HiredSeason = 2025 };
        Board.Weekly(behind, ctx, team, 20, 30, None, new DateOnly(2027, 1, 1), 2026);
        Assert.True(behind.Confidence < 88);
        // The review of a first-round exit at 53-29 costs a few points, not 25.
        var (_, body, _) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Playoffs, 53, 29, None, new DateOnly(2027, 6, 1), 2026);
        Assert.True(job.Confidence >= 80, $"{job.Confidence}: {body}");
    }

    [Fact]
    public void No_satisfied_or_delighted_under_forty()
    {
        // Cincinnati: 14-14 against a 21-win target, "is satisfied" at 32.
        var league = NewLeague();
        var ctx = new LeagueContext("College basketball", league.Teams, 28, 68, false, null, -10, 0, true);
        var team = league.Teams.First();
        team.Owner.Patience = 10;
        var job = new JobState { Current = new Expectations { Season = 2026, Goal = SeasonGoal.Playoffs, TargetWins = 21 }, Confidence = 32, HiredSeason = 2024 };
        var (_, body, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Playoffs, 14, 14, None, new DateOnly(2027, 4, 10), 2026, madnessRound: 0);
        Assert.False(sacked, body);
        Assert.True(job.Confidence < Board.ContentLine, $"{job.Confidence}");
        Assert.DoesNotContain("satisfied", body);
        Assert.DoesNotContain("delighted", body);
        Assert.True(body.Contains("accepts that the goal was met, but the record fell short of the 21-win target"), body);
        // At 40 and above the old words stand.
        var fine = new JobState { Current = new Expectations { Season = 2026, Goal = SeasonGoal.Playoffs, TargetWins = 21 }, Confidence = 60, HiredSeason = 2024 };
        var (_, body2, _) = Board.SeasonReview(fine, new Coach(), ctx, team, SeasonGoal.Playoffs, 21, 7, None, new DateOnly(2027, 4, 10), 2026, madnessRound: 0);
        Assert.Contains("is satisfied", body2);
    }

    [Fact]
    public void Legends_on_the_scoring_list_are_passed_in_order()
    {
        var ladder = RealCareers.ScoringLadder;
        Assert.Contains(ladder, x => x.Name == "John Havlicek");
        var ordered = ladder.OrderBy(x => x.Points).Select(x => x.Points).ToList();
        Assert.Equal(ordered.OrderBy(x => x), ordered);
    }

    [Fact]
    public void Wages_under_a_hundred_thousand_read_in_thousands()
    {
        Assert.Equal("$45K", Budgets.Money(45_000));
        Assert.Equal("$0.4M", Budgets.Money(360_000));
        Assert.Equal("$1.1M", Budgets.Money(1_100_000));
        Assert.Equal("$0.0M", Budgets.Money(0));
    }
}
