using System.Text.Json;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// K7 (critic review #16; run 115): relegation is the stake. A club projected last in a division that sends its last club down is
/// set "stay up" and told what that takes; going down costs real confidence (more in a protected season); the protected rebuild
/// after a relegation comes once per job; and a winning record doesn't shield a relegated coach from an owner out of patience.
/// K6 rides along: the top league's rules describe the promotion playoff.
/// </summary>
public class K7RelegationStakesTests
{
    private static readonly Dictionary<int, PlayerSeasonStats> None = new();

    /// <summary>The top league in the division format (32 clubs, eight divisions of four), as a new world lays it out.</summary>
    private static League NewLeague()
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Hardwood.Core.World.GameWorld.Generate(league, 3, Array.Empty<string>(), new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        return league;
    }

    private static Team LastInADivision(LeagueContext ctx) =>
        ctx.Teams.GroupBy(t => (t.Conference, t.Division)).Select(g => g.OrderBy(t => Board.ProjectedRank(ctx, t)).Last()).First();

    [Fact]
    public void The_projected_last_club_in_each_division_is_set_to_stay_up_and_told_what_it_takes()
    {
        var league = NewLeague();
        var ctx = LeagueContext.Nabl(league);
        Assert.True(Postseason.UsesDivisions(league));
        Assert.True(Board.Relegates(ctx));
        var atRisk = league.Teams.Where(t => Board.AtRisk(ctx, t)).ToList();
        Assert.Equal(8, atRisk.Count);   // one per division of four

        var team = LastInADivision(ctx);
        team.Owner.Ambition = 18;   // an ambitious owner's "make the playoffs" demand would contradict the stake
        var e = Board.NextExpectations(ctx, team, 2026, null, "nabl", 1, 0, promotionPlayoff: true);
        Assert.True(e.Survive);
        Assert.Equal(SeasonGoal.Develop, e.Goal);
        Assert.DoesNotContain(Demand.MakePlayoffs, e.Demands);
        Assert.Equal($"finish above last in the {team.Division} division, or win the promotion playoff to keep your place", Board.SurviveText(e));

        // Without the promotion playoff (an older layout) it's the straight drop.
        var plain = Board.NextExpectations(ctx, team, 2026, null, "nabl", 1, 0);
        Assert.Equal($"finish above last in the {team.Division} division", plain.SurviveNote);

        // A club that isn't projected last keeps its ordinary goal.
        var safe = league.Teams.First(t => !Board.AtRisk(ctx, t));
        Assert.False(Board.NextExpectations(ctx, safe, 2026, null, "nabl", 1, 0, true).Survive);
    }

    [Fact]
    public void Tier_five_college_and_leagues_without_divisions_never_set_a_survival_goal()
    {
        var league = NewLeague();
        var tier5 = new LeagueContext("Tier 5", league.Teams, 30, 16, false, null, -30, 5);
        var tier3 = new LeagueContext("Tier 3", league.Teams, 62, 16, false, null, -15, 3);
        var abroad = new LeagueContext("Abroad", league.Teams.Take(12).ToList(), 22, 8, false, null, -10);
        Assert.False(Board.Relegates(tier5));
        Assert.True(Board.Relegates(tier3));
        Assert.False(Board.Relegates(abroad));
        Assert.All(league.Teams, t => Assert.False(Board.NextExpectations(tier5, t, 2026, null, "usa-5", 5, 0).Survive));
    }

    [Fact]
    public void A_newly_promoted_club_keeps_its_aim_and_learns_the_line_when_it_is_projected_last()
    {
        var league = NewLeague();
        var ctx = LeagueContext.Nabl(league);
        var team = LastInADivision(ctx);
        var prev = new Expectations { Season = 2025, TeamId = team.Id, League = "usa-2", Goal = SeasonGoal.SecondRound, TargetWins = 40 };
        var e = Board.NextExpectations(ctx, team, 2026, prev, "nabl", 1, 2, promotionPlayoff: true);
        Assert.True(e.Survive);
        Assert.StartsWith("newly promoted: finish above last in the", e.SurviveNote);
        // A promoted club that isn't projected last keeps the old wording.
        var safe = league.Teams.First(t => !Board.AtRisk(ctx, t));
        var prev2 = new Expectations { Season = 2025, TeamId = safe.Id, League = "usa-2", Goal = SeasonGoal.SecondRound, TargetWins = 40 };
        var e2 = Board.NextExpectations(ctx, safe, 2026, prev2, "nabl", 1, 2);
        Assert.True(e2.Survive);
        Assert.Null(e2.SurviveNote);
        Assert.Equal("newly promoted, avoid the relegation places", Board.SurviveText(e2));
    }

    [Fact]
    public void Going_down_costs_at_least_20_and_at_least_30_in_a_protected_season_whatever_the_record()
    {
        var league = NewLeague();
        var ctx = new LeagueContext("NBA G League", league.Teams, 62, 16, false, null, -10, 2);
        var team = league.Teams.First();
        team.Owner.Patience = 18;   // the most forgiving owner
        // The Kings, 2028-29 (seed 31): "make the playoffs (around 31 wins)" → 37-25, last in the division, confidence 71.
        var job = new JobState { Current = new Expectations { Season = 2028, Goal = SeasonGoal.Playoffs, TargetWins = 31 }, Confidence = 75, HiredSeason = 2026 };
        var (_, body, _) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Develop, 37, 25, None, new DateOnly(2029, 6, 1), 2028, relegated: true);
        Assert.True(job.Confidence <= 55, $"confidence {job.Confidence}");
        Assert.Contains("a winning record wasn't enough to stay up", body);

        var shielded = new JobState { Current = new Expectations { Season = 2028, Goal = SeasonGoal.Playoffs, TargetWins = 31 }, Confidence = 75, HiredSeason = 2026,
            ProtectedSeason = 2028, ProtectedReason = "credit" };
        Board.SeasonReview(shielded, new Coach(), ctx, team, SeasonGoal.Develop, 37, 25, None, new DateOnly(2029, 6, 1), 2028, relegated: true);
        Assert.True(shielded.Confidence <= 45, $"confidence {shielded.Confidence}");
    }

    [Fact]
    public void A_second_relegation_in_the_rebuild_season_can_end_the_job_even_with_a_winning_record()
    {
        var league = NewLeague();
        var ctx = new LeagueContext("NBA G League", league.Teams, 62, 16, false, null, -10, 2);
        var team = league.Teams.First();
        team.Owner.Patience = 10;
        var job = new JobState { Current = new Expectations { Season = 2027, Goal = SeasonGoal.Playoffs, TargetWins = 31 }, Confidence = 40, HiredSeason = 2026,
            ProtectedSeason = 2027, ProtectedReason = "rebuild", RebuildUsed = true };
        var (title, body, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Develop, 37, 25, None, new DateOnly(2028, 6, 1), 2027, relegated: true);
        Assert.True(sacked, body);
        Assert.Equal("You have been sacked", title);

        // With confidence in hand the owner keeps him, and says why it hurts.
        var kept = new JobState { Current = new Expectations { Season = 2027, Goal = SeasonGoal.Playoffs, TargetWins = 31 }, Confidence = 90, HiredSeason = 2026,
            ProtectedSeason = 2027, ProtectedReason = "rebuild", RebuildUsed = true };
        var (_, body2, sacked2) = Board.SeasonReview(kept, new Coach(), ctx, team, SeasonGoal.Develop, 37, 25, None, new DateOnly(2028, 6, 1), 2027, relegated: true);
        Assert.False(sacked2);
        Assert.Contains("gave you a season to rebuild after the last relegation, and the club has gone down again", body2);
        Assert.True(kept.Confidence <= 60, $"confidence {kept.Confidence}");
    }

    [Fact]
    public void A_survival_goal_missed_reads_as_the_one_job_and_one_kept_reads_stayed_up()
    {
        var league = NewLeague();
        var ctx = LeagueContext.Nabl(league);
        var team = league.Teams.First();
        var e = new Expectations { Season = 2026, Goal = SeasonGoal.Develop, TargetWins = 22, Survive = true, SurviveNote = "finish above last in the Mojave division" };
        var job = new JobState { Current = e, Confidence = 70, HiredSeason = 2025 };
        var (_, body, _) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Develop, 12, 70, None, new DateOnly(2027, 6, 1), 2026, relegated: true);
        Assert.Contains("Goal: stay up (finish above last in the Mojave division).", body);
        Assert.Contains("set one job, to stay up, and the club is going down", body);
        Assert.True(job.Confidence <= 50, $"confidence {job.Confidence}");

        var e2 = new Expectations { Season = 2026, Goal = SeasonGoal.Develop, TargetWins = 22, Survive = true, SurviveNote = "finish above last in the Mojave division" };
        var job2 = new JobState { Current = e2, Confidence = 60, HiredSeason = 2025 };
        var (_, body2, _) = Board.SeasonReview(job2, new Coach(), ctx, team, SeasonGoal.Develop, 25, 57, None, new DateOnly(2027, 6, 1), 2026);
        Assert.Contains("Result: stayed up", body2);
        Assert.True(job2.Confidence >= 60, $"confidence {job2.Confidence}");
    }

    [Fact]
    public void An_older_save_loads_without_the_new_fields()
    {
        var job = JsonSerializer.Deserialize<JobState>("""{"Employed":true,"Confidence":50,"ProtectedSeason":2027,"ProtectedReason":"rebuild","Current":{"Season":2027,"Survive":true}}""")!;
        Assert.False(job.RebuildUsed);
        Assert.Null(job.Current!.SurviveNote);
        Assert.Equal("newly promoted, avoid the relegation places", Board.SurviveText(job.Current));
    }

    [Fact]
    public void K6_the_top_leagues_rules_describe_the_promotion_playoff()
    {
        string now = LeagueBook.NablLevel(true, "NBA G League");
        Assert.Contains("plays the winner of the same division of the NBA G League in a promotion playoff, best of three", now);
        Assert.Contains("NBA G League club at home in Games 1 and 3", now);
        Assert.DoesNotContain("drops into", now);
        Assert.Contains("drops into the same division of the NBA G League", LeagueBook.NablLevel(false, "NBA G League"));
    }
}
