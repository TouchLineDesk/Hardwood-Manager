using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Presentation;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 203, the college bug and polish pass (item 12 of the revised launch plan):
/// - H204: a low-major's players aren't all "doesn't suit his role" on day one (the fit is judged against his own best role);
/// - H205: a blue blood's first-year coach is asked for a deep run, and a 20-win tournament season never ends in a final warning;
/// - H165: a March run lifts a team a few places in the final poll, not 10+ past better records.
/// </summary>
public class Run203CollegePolishTests
{
    private readonly ITestOutputHelper _out;
    public Run203CollegePolishTests(ITestOutputHelper o) => _out = o;
    private static readonly Dictionary<int, PlayerSeasonStats> None = new();

    [Fact]
    public void H204_a_low_majors_roster_mostly_suits_its_roles()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        int Miscast(Team t, bool college) => t.Roster.Count(p => Roles.Suitability(p, t.Tactic.RoleOf(p), college) < 0);
        int OldMiscast(Team t) => t.Roster.Count(p => Roles.Stars(p, t.Tactic.RoleOf(p)) < 2);
        var rider = cw.Programs.First(t => t.FullName.StartsWith("Rider"));
        var kentucky = cw.Programs.First(t => t.FullName.StartsWith("Kentucky"));
        _out.WriteLine($"Rider: {OldMiscast(rider)} → {Miscast(rider, true)} of {rider.Roster.Count}; Kentucky: {OldMiscast(kentucky)} → {Miscast(kentucky, true)}");
        Assert.True(Miscast(rider, true) <= 3, $"Rider: {Miscast(rider, true)} miscast");
        Assert.True(Miscast(kentucky, true) <= OldMiscast(kentucky));
        // Across the country, no program gets more miscast players than the absolute scale gave it.
        Assert.All(cw.Programs, t => Assert.True(Miscast(t, true) <= OldMiscast(t), t.FullName));
        // The pros keep the absolute scale.
        foreach (var t in world.Nabl.Teams.Take(5))
            Assert.Equal(OldMiscast(t), Miscast(t, false));
        // A player put in a role well below his own game still feels it.
        var p = rider.Roster.OrderByDescending(x => Roles.Stars(x, Roles.Best(x))).First();
        var worst = Roles.All.Select(i => i.Role).OrderBy(r => Roles.Stars(p, r)).First();
        if (Roles.Stars(p, worst) < 2 && Roles.FitGap(p, worst) >= Roles.CollegeMiscastGap)
            Assert.Equal(-1, Roles.Suitability(p, worst, true));
        Assert.Equal(1, Roles.Suitability(p, Roles.Best(p), true));
    }

    private static (LeagueContext Ctx, Team Team) College(int games = 31)
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        var ctx = new LeagueContext("College basketball", league.Teams, games, 68, false, null, -10, 0, true);
        var team = league.Teams[5];
        team.Owner.Patience = 6; team.Owner.Ambition = 18;
        return (ctx, team);
    }

    [Fact]
    public void H205_a_first_year_coach_after_23_11_and_a_bid_gets_no_final_warning()
    {
        var (ctx, team) = College();
        foreach (int seed in new[] { 1, 2, 4, 6 })
        foreach (int round in new[] { 1, 2 })
        {
            var job = new JobState
            {
                Confidence = 60, HiredSeason = 2026, SeasonStartSeason = 2026, SeasonStartConfidence = 60,
                Current = new Expectations { Season = 2026, Goal = SeasonGoal.Title, TargetWins = 27, TeamId = team.Id, League = "college" },
            };
            var (_, body, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Playoffs, 21, 10, None, new DateOnly(2027, 4, 7), 2026,
                madnessRound: round, seed: seed, overall: (23, 11));
            string verdict = body.Split('\n').Last();
            _out.WriteLine($"seed {seed}, round {round}: {job.Confidence:0} · {verdict}");
            Assert.False(sacked);
            Assert.DoesNotContain("final warning", verdict);
            Assert.True(job.Confidence >= Board.FirstSeasonSafeFloor);
            Assert.True(job.Confidence >= 60 - Board.FirstSeasonDropCap);
        }
        // A bad first season still earns a warning, but no "final" one (H212, run 212: the grace rule keeps him two seasons).
        var bad = new JobState
        {
            Confidence = 40, HiredSeason = 2026, SeasonStartSeason = 2026, SeasonStartConfidence = 40,
            Current = new Expectations { Season = 2026, Goal = SeasonGoal.Title, TargetWins = 27, TeamId = team.Id, League = "college" },
        };
        var badBody = Board.SeasonReview(bad, new Coach(), ctx, team, SeasonGoal.Develop, 10, 21, None, new DateOnly(2027, 4, 7), 2026).body;
        Assert.Contains("next season has to show real progress", badBody.Split('\n').Last());
        Assert.DoesNotContain("final warning", badBody);
    }

    [Fact]
    public void H205_a_new_college_coach_is_never_set_the_title()
    {
        var (ctx, _) = College();
        foreach (var t in ctx.Teams)
        {
            var e = Board.NextExpectations(ctx, t, 2026, null, "college", 0, 0);
            Assert.NotEqual(SeasonGoal.Title, e.Goal);
        }
    }

    [Fact]
    public void H165_the_final_poll_lifts_a_March_run_a_few_places()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var c = world.College!;
        c.PlayThrough(new DateOnly(2027, 4, 30), new GameSimulator(), new Rng(31));
        var s = c.Season;
        Assert.True(c.FinalPoll);
        var order = c.PollOrder();
        Assert.Equal(s.ChampionId, order[0]);
        Assert.Equal(s.RunnerUpId, order[1]);
        var baseOrder = c.Programs.Select(t => t.Id).OrderByDescending(c.PollScore).ThenBy(id => id).ToList();
        foreach (var t in c.Programs.Where(p => c.MadnessReached(p.Id) >= 2 && p.Id != s.ChampionId && p.Id != s.RunnerUpId))
        {
            int now = c.PollPosition(t.Id), before = baseOrder.IndexOf(t.Id) + 1;
            int reached = c.MadnessReached(t.Id);
            int allowed = Math.Min(CollegeWorld.MarchClimbMax, CollegeWorld.MarchClimbPerRound * (reached - 1));
            if (reached >= 5) Assert.True(now <= CollegeWorld.FinalFourFloor, $"{t.FullName} No. {now}");
            // +2: the champion and the runner-up go first from wherever they were, and the teams they pass shift down a place each.
            else if (now <= CollegeWorld.PollSize) Assert.True(before - now <= allowed + 2, $"{t.FullName}: {before} → {now} (allowed {allowed})");
        }
        foreach (var id in order.Take(CollegeWorld.PollSize))
        {
            var (w, l) = c.Record(id);
            _out.WriteLine($"{c.PollPosition(id),2}. {c.Program(id)!.FullName} {w}-{l} (March {c.MadnessReached(id)}; before {baseOrder.IndexOf(id) + 1})");
        }
    }

    [Fact]
    public void K28_a_hurried_late_trip_is_not_drawn_as_a_clear_out()
    {
        var l = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var h = l.Teams[0]; var a = l.Teams[1];
        h.Tactic = new Tactic(); a.Tactic = new Tactic();
        List<Player> Five(Team t) => t.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();
        var hf = Five(h); var af = Five(a);
        PossessionInput Input(double start, double end, int hs, int aws) =>
            new(9, 2026, new List<PlayEvent> { new(2, end, PlayKind.Shot, true, hf[1], null, ShotZone.AboveBreakThree, true, hs + 3, aws) }, start, end, hf, af,
                hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7)),
                hs, aws, true, "Home", "Away", DefensiveScheme.Man, DefensiveScheme.Man, PossessionStart.AfterMake, 2, true) { HomeSystem = OffensiveSystem.Isolation };
        // Down six with 40 seconds left: a hurried trip, no clear-out.
        var late = PossessionChoreographer.Build(Input(40, 33, 60, 66));
        Assert.True(late.Hurried);
        Assert.DoesNotContain(late.Lines, x => x.Text.Contains("clear out"));
        Assert.DoesNotContain(late.Actions, x => x.Tag == "clear-out");
        // Down six with five minutes left, or level late: the set is still run.
        var early = PossessionChoreographer.Build(Input(300, 280, 60, 66));
        Assert.False(early.Hurried);
        Assert.Contains(early.Lines, x => x.Text.Contains("clear out"));
        Assert.False(PossessionChoreographer.Build(Input(40, 25, 66, 66)).Hurried);
    }

    [Fact]
    public void K40_one_more_win_isnt_satisfied_and_confidence_rises_at_most_twenty()
    {
        var (ctx, team) = College(30);
        team.Owner.Patience = 10; team.Owner.Ambition = 10;
        var job = new JobState
        {
            Confidence = 39, HiredSeason = 2024, SeasonStartSeason = 2026, SeasonStartConfidence = 39,
            Current = new Expectations { Season = 2026, Goal = SeasonGoal.Develop, TargetWins = 17, TeamId = team.Id, League = "college" },
        };
        var body = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.Develop, 15, 15, None, new DateOnly(2027, 4, 8), 2026).body;
        string verdict = body.Split('\n').Last();
        _out.WriteLine($"{job.Confidence:0} · {verdict}");
        Assert.DoesNotContain("satisfied", verdict);
        Assert.True(job.Confidence <= 39 + Board.YearRiseCap, $"{job.Confidence}");
        // A great season from a low start still rises, by at most twenty.
        var low = new JobState
        {
            Confidence = 30, HiredSeason = 2024, SeasonStartSeason = 2026, SeasonStartConfidence = 30,
            Current = new Expectations { Season = 2026, Goal = SeasonGoal.Develop, TargetWins = 15, TeamId = team.Id, League = "college" },
        };
        Board.SeasonReview(low, new Coach(), ctx, team, SeasonGoal.SecondRound, 24, 6, None, new DateOnly(2027, 4, 8), 2026, madnessRound: 2, seed: 7);
        _out.WriteLine($"24-6 from 30: {low.Confidence:0}");
        Assert.InRange(low.Confidence, 31, 30 + Board.YearRiseCap);
    }

    [Fact]
    public void Review_of_272_caps_keep_a_met_goal_and_measure_from_the_seasons_start()
    {
        var (ctx, team) = College(31);
        // A title season after the drift reached 88 from a start of 50: never lowered.
        var champ = new JobState
        {
            Confidence = 88, HiredSeason = 2024, SeasonStartSeason = 2026, SeasonStartConfidence = 50,
            Current = new Expectations { Season = 2026, Goal = SeasonGoal.Title, TargetWins = 26, TeamId = team.Id, League = "college" },
        };
        Board.SeasonReview(champ, new Coach(), ctx, team, SeasonGoal.Title, 28, 3, None, new DateOnly(2027, 4, 8), 2026, madnessRound: 7, seed: 1);
        Assert.True(champ.Confidence >= 88, $"{champ.Confidence}");
        // A .500 first season that drifted from 70 to 35: no lower than 50 and no final warning.
        var first = new JobState
        {
            Confidence = 35, HiredSeason = 2026, SeasonStartSeason = 2026, SeasonStartConfidence = 70,
            Current = new Expectations { Season = 2026, Goal = SeasonGoal.Playoffs, TargetWins = 22, TeamId = team.Id, League = "college" },
        };
        var body = Board.SeasonReview(first, new Coach(), ctx, team, SeasonGoal.Develop, 16, 16, None, new DateOnly(2027, 4, 8), 2026).body;
        Assert.True(first.Confidence >= 70 - Board.FirstSeasonDropCap, $"{first.Confidence}");
        Assert.DoesNotContain("final warning", body);
    }
}
