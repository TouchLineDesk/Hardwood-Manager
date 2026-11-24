using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 182: the launch hunt's first-hour Mediums (hunt 25).
/// - H188: the AD's March verdicts follow the run: a beaten goal is never "hoped for more", and a deeper run never reads
///   worse than a shallower one at the same seed, goal and record.
/// - H189: a "build the program" target starts from last season's wins.
/// - H194: a losing college coach is almost never offered a pro head job.
/// - H195: a new college career's inbox opens on the Welcome.
/// - H185: on review day the Season review is posted before next season's Expectations.
/// </summary>
public class Run182LaunchMediumsTests
{
    private readonly ITestOutputHelper _out;
    public Run182LaunchMediumsTests(ITestOutputHelper o) => _out = o;
    private static readonly Dictionary<int, PlayerSeasonStats> None = new();

    private static (LeagueContext Ctx, Team Team) College(int games = 31)
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        var ctx = new LeagueContext("College basketball", league.Teams, games, 68, false, null, -10, 0, true);
        var team = league.Teams[5];
        team.Owner.Patience = 10; team.Owner.Ambition = 10;
        return (ctx, team);
    }

    /// <summary>The verdict's place on the AD's ladder, from its first words.</summary>
    private static int Rank(string verdict) =>
        verdict.Contains(" is delighted") ? 6
        : verdict.Contains(" is pleased") ? 5
        : verdict.Contains(" is satisfied") || verdict.Contains(" accepts") ? 4
        : verdict.Contains(" hoped for more") ? 3
        : verdict.Contains(" is disappointed") ? 2
        : verdict.Contains(" expected more") || verdict.Contains(" wanted to see progress") ? 1
        : 0;

    private static readonly SeasonGoal[] Goals = { SeasonGoal.Title, SeasonGoal.Finals, SeasonGoal.ConferenceFinals, SeasonGoal.SecondRound, SeasonGoal.Playoffs };

    private static SeasonGoal Reached(int round) => round switch
    {
        7 => SeasonGoal.Title,
        >= 5 => SeasonGoal.Finals,
        4 => SeasonGoal.ConferenceFinals,
        3 => SeasonGoal.SecondRound,
        _ => SeasonGoal.Playoffs,
    };

    [Fact]
    public void H188_a_beaten_goal_never_reads_short_and_a_deeper_run_never_reads_worse()
    {
        var (ctx, team) = College();
        int checkedCases = 0;
        foreach (var goal in Goals)
        foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 8, 11, 12, 16 })
        foreach (int wins in new[] { 29, 24, 19 }) // past, on and short of a 24-win target
        {
            int last = -1, lastRound = 0; string lastText = "";
            for (int round = 1; round <= 7; round++)
            {
                var achieved = Reached(round);
                var job = new JobState
                {
                    Confidence = 70, HiredSeason = 2023,
                    Current = new Expectations { Season = 2026, Goal = goal, TargetWins = 24, TeamId = team.Id, League = "college" },
                };
                var (_, body, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, achieved, wins, 31 - wins, None, new DateOnly(2027, 4, 7), 2026,
                    madnessRound: round, seed: seed, overall: (wins + round, 31 - wins + 1));
                string verdict = body.Split('\n').Last();
                int rank = Rank(verdict);
                Assert.False(sacked);
                if (Board.CollegeStep(achieved) >= Board.CollegeStep(goal))
                {
                    Assert.True(rank >= 4, $"goal {goal}, seed {seed}, {wins} wins, round {round}: a beaten goal read \"{verdict}\"");
                }
                Assert.True(rank >= last, $"goal {goal}, seed {seed}, {wins} wins: round {round} \"{verdict}\" reads worse than round {lastRound} \"{lastText}\"");
                last = rank; lastRound = round; lastText = verdict;
                checkedCases++;
            }
        }
        _out.WriteLine($"{checkedCases} cases");
    }

    [Fact]
    public void H188_the_hunts_three_cases_read_in_order()
    {
        var (ctx, team) = College();
        string Review(SeasonGoal goal, SeasonGoal achieved, int round, int seed, int wins)
        {
            var job = new JobState { Confidence = 70, HiredSeason = 2023, Current = new Expectations { Season = 2026, Goal = goal, TargetWins = 24, TeamId = team.Id, League = "college" } };
            var body = Board.SeasonReview(job, new Coach(), ctx, team, achieved, wins, 31 - wins, None, new DateOnly(2027, 4, 7), 2026, madnessRound: round, seed: seed).body;
            _out.WriteLine(body.Split('\n').Last());
            return body.Split('\n').Last();
        }
        // Florida: a Sweet 16 goal, the Elite Eight as a 2-seed.
        Assert.Contains("is pleased", Review(SeasonGoal.SecondRound, SeasonGoal.ConferenceFinals, 4, 2, 26));
        // Duke: a title goal, a 1-seed out in the Sweet 16, after a regular season well past the target.
        string duke = Review(SeasonGoal.Title, SeasonGoal.SecondRound, 3, 1, 29);
        // North Carolina: the same goal and seed, out in the Elite Eight.
        string unc = Review(SeasonGoal.Title, SeasonGoal.ConferenceFinals, 4, 1, 29);
        Assert.Contains("falls short of what the seed promised", duke);
        Assert.True(Rank(unc) > Rank(duke), $"{unc} / {duke}");
    }

    [Fact]
    public void H189_a_build_the_program_target_starts_from_last_season()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        Board.EnsureOwnersAndCoaches(cw.Programs, 9);
        var ctx = LeagueContext.College(cw, cw.Programs[0]);
        int games = ctx.SeasonGames, floor = (int)Math.Round(games * 0.4);
        int seen = 0;
        foreach (var team in cw.Programs.Skip(150).Take(60))
        {
            team.Owner.Patience = 10; team.Owner.Ambition = 10;
            ctx = LeagueContext.College(cw, team);
            if (Board.SetExpectations(ctx, team, 2027).Goal != SeasonGoal.Develop) continue;
            foreach (int lastWins in new[] { 8, 13, 14, 17 })
            {
                var prev = new Expectations { Season = 2026, Goal = SeasonGoal.Develop, TargetWins = 20, Wins = lastWins, Achieved = SeasonGoal.Develop, TeamId = team.Id, League = "college" };
                var next = Board.NextExpectations(ctx, team, 2027, prev, "college", 0, 0);
                Assert.Equal(SeasonGoal.Develop, next.Goal);
                Assert.InRange(next.TargetWins, Math.Min(floor, Board.SetExpectations(ctx, team, 2027).TargetWins), Math.Max(lastWins + Board.CollegeProgressWins, floor));
                seen++;
            }
        }
        Assert.True(seen > 0);
        _out.WriteLine($"{seen} cases");
    }

    private static GameSession CollegeCareer(string school = "George Mason")
    {
        var (_, world0) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        int id = world0.College!.Programs.First(p => p.FullName.StartsWith(school)).Id;
        return GameSession.NewGame(9, id, null);
    }

    [Fact]
    public void H194_and_H195_a_losing_college_coach_isnt_wanted_by_the_nba_and_the_welcome_leads()
    {
        var s = CollegeCareer();
        // H195: the Welcome is the first thing a new college coach reads.
        Assert.Equal("Welcome to the job", s.Inbox[0].Title);
        Assert.Contains(s.Inbox, m => m.Title.StartsWith("Expectations for"));

        // H194: reputation 30, a losing season just judged: every NBA club's chance is under 5%.
        s.Coach.Reputation = 30;
        s.Job.LastSeasonWinning = false;
        double worst = s.League.Teams.Max(t => s.HireChanceFor(t));
        _out.WriteLine($"best NBA chance after a losing season: {worst:P1}");
        Assert.True(worst < 0.05, $"{worst:P1}");
        // An ordinary winning season is still a long shot, and a college program's own chance is untouched.
        s.Job.LastSeasonWinning = true;
        Assert.True(s.League.Teams.Max(t => s.HireChanceFor(t)) < 0.25);
        var program = s.World.College!.Programs.First(p => p != s.HumanTeam);
        Assert.Equal(Board.HireChance(s.Coach, s.ContextOf(program), program), s.HireChanceFor(program), 9);
        // A proven college coach (reputation 70+: titles and Final Fours) is judged as before.
        s.Coach.Reputation = 75;
        var nba = s.League.Teams[0];
        Assert.Equal(Board.HireChance(s.Coach, s.ContextOf(nba), nba), s.HireChanceFor(nba), 9);
    }
    [Fact]
    public void H185_next_seasons_goal_comes_after_the_season_review_and_is_dated_the_new_season()
    {
        var s = CollegeCareer();
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 800) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        int finished = s.League.Season;
        while (s.Phase == SessionPhase.Offseason && guard++ < 1600)
        {
            if (s.Offseason!.Phase == OffseasonPhase.Draft && !s.Offseason.DraftComplete) s.Offseason.AutoDraftUntil(null);
            s.AdvanceOffseason();
        }
        Assert.Equal(SessionPhase.Preseason, s.Phase);
        if (!s.Employed) return; // sacked (not expected at George Mason in year one, but the test isn't about that)
        string label = $"{finished + 1}-{(finished + 2) % 100:00}";
        var review = s.Inbox.First(m => m.Title is "Season review" or "You have been sacked");
        var goal = s.Inbox.First(m => m.Title == $"Expectations for {label}");
        _out.WriteLine($"{review.Date} {review.Title}; {goal.Date} {goal.Title}");
        // The newest first: the goal sits above the review, and is dated the new season, not the review's day.
        Assert.True(s.Inbox.IndexOf(goal) < s.Inbox.IndexOf(review));
        Assert.True(goal.Date > review.Date, $"{goal.Date} vs {review.Date}");
        Assert.Equal(s.Today, goal.Date);
    }
}
