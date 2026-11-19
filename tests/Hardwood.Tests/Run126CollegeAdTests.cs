using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 126, CS2 (college plan): the athletic director speaks college.
/// - The win target covers the regular season only: conference tournament and March games never shrink the margin
///   ("33-5, target 24 … 3 wins past the target").
/// - March is judged against the seed: an 8-seed in the Sweet 16 has overachieved.
/// - A "build the program" season that missed the tournament well short of the target isn't "satisfied".
/// - Goals follow prestige as well as the squad; prestige reads as a letter.
/// </summary>
public class Run126CollegeAdTests
{
    private readonly ITestOutputHelper _out;
    public Run126CollegeAdTests(ITestOutputHelper o) => _out = o;
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

    private static JobState Job(Team team, SeasonGoal goal, int target, double confidence) => new()
    {
        Confidence = confidence, HiredSeason = 2024,
        Current = new Expectations { Season = 2026, Goal = goal, TargetWins = target, TeamId = team.Id, League = "college" },
    };

    [Fact]
    public void The_target_is_judged_on_the_regular_season_and_the_line_says_so()
    {
        var (ctx, team) = College();
        var job = Job(team, SeasonGoal.ConferenceFinals, 24, 70);
        // 28-3 in the regular season, then 5 more wins (conference tournament and March): 33-5 overall, Elite Eight as a 2-seed.
        var (_, body, _) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.ConferenceFinals, 28, 3, None, new DateOnly(2027, 4, 7), 2026,
            madnessRound: 4, seed: 2, overall: (33, 5));
        _out.WriteLine(body);
        Assert.Contains("regular season 28-3, 33-5 overall; target 24 wins", body);
        Assert.Contains("as a 2-seed", body);
        var j = Board.Judge(job.Current!, ctx, SeasonGoal.ConferenceFinals, 28, 3, seed: 2, madnessRound: 4);
        // 28 of 31 against 24 of 31 is 4 wins past (about 10.6 per 82), not 3.
        Assert.Equal(4 * 82 / 31.0, j.WinsVsTarget, 3);
    }

    [Fact]
    public void An_eight_seed_in_the_sweet_16_has_overachieved()
    {
        var (ctx, team) = College();
        var job = Job(team, SeasonGoal.Playoffs, 21, 36);
        var (_, body, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.SecondRound, 16, 15, None, new DateOnly(2027, 4, 7), 2026,
            madnessRound: 3, seed: 8, overall: (18, 16));
        _out.WriteLine(body);
        Assert.False(sacked);
        Assert.Contains("delighted with the March run: an 8-seed in the Sweet 16", body);
        Assert.DoesNotContain("fell short", body);
        Assert.True(job.Confidence >= Board.ContentLine + 5, $"confidence {job.Confidence}");
        Assert.True(Board.Judge(job.Current!, ctx, SeasonGoal.SecondRound, 16, 15, seed: 8, madnessRound: 3).Steps >= 1);
        // Seed lines: a 1-seed out in the Round of 32 is well short; a 12-seed that wins a game has done well.
        Assert.True(Board.SeedOverachievement(1, 2) <= -2);
        Assert.True(Board.SeedOverachievement(12, 2) >= 1);
        Assert.Equal(0, Board.SeedOverachievement(0, -1));
    }

    [Fact]
    public void A_missed_tournament_well_short_of_the_target_is_never_satisfied()
    {
        var (ctx, team) = College();
        // A "build the program" goal: 12-19 against 19 is no progress (Clemson, 4-11 in the ACC, read "satisfied", 39 → 45).
        var build = Job(team, SeasonGoal.Develop, 19, 39);
        var (_, body, _) = Board.SeasonReview(build, new Coach(), ctx, team, SeasonGoal.Develop, 12, 19, None, new DateOnly(2027, 4, 7), 2026);
        _out.WriteLine(body);
        Assert.DoesNotContain("satisfied", body);
        Assert.Contains("wanted to see progress", body);
        Assert.True(build.Confidence < 39, $"confidence rose to {build.Confidence}");
        // A build season near the target is still progress.
        var near = Job(team, SeasonGoal.Develop, 19, 50);
        var (_, ok, _) = Board.SeasonReview(near, new Coach(), ctx, team, SeasonGoal.Develop, 18, 13, None, new DateOnly(2027, 4, 7), 2026);
        Assert.Contains("satisfied", ok);
        // "Make March Madness" missed is never satisfied.
        var make = Job(team, SeasonGoal.Playoffs, 20, 50);
        var (_, missed, _) = Board.SeasonReview(make, new Coach(), ctx, team, SeasonGoal.Develop, 20, 11, None, new DateOnly(2027, 4, 7), 2026);
        _out.WriteLine(missed);
        Assert.DoesNotContain("satisfied", missed);
        Assert.DoesNotContain("delighted", missed);
    }

    [Fact]
    public void Prestige_reads_as_a_letter_and_steadies_the_goal()
    {
        Assert.Equal("A+", CollegeWorld.PrestigeLetter(92));
        Assert.Equal("A-", CollegeWorld.PrestigeLetter(76));
        Assert.Equal("B+", CollegeWorld.PrestigeLetter(70));
        Assert.Equal("C", CollegeWorld.PrestigeLetter(45));
        Assert.Equal("F", CollegeWorld.PrestigeLetter(15));
        var (ctx0, _) = College();
        // The weakest squad in the country, but the most prestigious program: the AD ranks it in between, not last.
        var team = ctx0.Teams.OrderBy(t => Board.ProjectedRank(ctx0, t)).Last();
        var prestige = ctx0.Teams.ToDictionary(t => t.Id, t => t.Id == team.Id ? 99.0 : 50.0 - t.Id * 0.1);
        var ctx = ctx0 with { ProgramPrestige = prestige };
        int squad = Board.ProjectedRank(ctx0, team), mid = Board.CollegeRank(ctx, team, squad);
        _out.WriteLine($"squad rank {squad}, AD's rank {mid}");
        Assert.InRange(mid, 2, squad - 1);
        // Exactly one favourite.
        Assert.Single(ctx.Teams, t => Board.CollegeRank(ctx, t, Board.ProjectedRank(ctx, t)) == 1);
        Assert.Equal(40, Board.CollegeRank(ctx0, team, 40));
    }

    [Fact]
    public void A_real_college_season_counts_conference_tournament_and_march_apart()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith("Clemson")).Id;
        var s = GameSession.NewGame(2026, id, null);
        var cw = s.World.College!;
        // The regular season's games for one program: about the length the AD's context expects.
        int regular = cw.Season.Games.Count(g => (g.HomeId == id || g.AwayId == id) && CollegeWorld.IsRegularSeason(g));
        var ctx = s.ContextOf(s.HumanTeam);
        _out.WriteLine($"Clemson regular-season games scheduled {regular}, context {ctx.SeasonGames}");
        Assert.InRange(regular, ctx.SeasonGames - 4, ctx.SeasonGames + 1);
        Assert.False(CollegeWorld.IsRegularSeason(new CollegeGame { Stage = "Conference tournament" }));
        Assert.False(CollegeWorld.IsRegularSeason(new CollegeGame { Stage = CollegeWorld.Madness }));
        Assert.True(CollegeWorld.IsRegularSeason(new CollegeGame { Stage = "Invitational" }));
        Assert.Equal(s.MyRecord.Wins, s.MyBoardRecord.Wins); // nothing played yet
    }
}

public class Run126CollegeBoardPageTests
{
    private readonly ITestOutputHelper _out;
    public Run126CollegeBoardPageTests(ITestOutputHelper o) => _out = o;

    [AvaloniaFact]
    public void The_board_page_speaks_college_mid_season()
    {
        var oldSettings = Hardwood.Desktop.AppSettings.Current; var oldPath = Hardwood.Desktop.AppSettings.FilePath;
        Hardwood.Desktop.AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        Hardwood.Desktop.AppSettings.Use(new Hardwood.Desktop.AppSettings { AutoPortraits = false });
        Hardwood.Desktop.Views.MainWindow? window = null;
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
            int id = world.College!.Programs.First(p => p.FullName.StartsWith("Clemson")).Id;
            var s = GameSession.NewGame(2026, id, null);
            s.Delegated.Recruiting = true;
            while (s.MyRecord.Games < 12) s.AdvanceDay();
            var vm = new Hardwood.Desktop.ViewModels.MainViewModel();
            window = new Hardwood.Desktop.Views.MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            vm.Navigate("Board");
            var board = Assert.IsType<Hardwood.Desktop.ViewModels.BoardViewModel>(vm.CurrentPage);
            for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            _out.WriteLine($"{s.Today}: {board.Pace} | {board.ConfidenceHeading}");
            Assert.StartsWith("Regular season ", board.Pace);
            Assert.Equal(Hardwood.Desktop.ViewModels.BoardViewModel.CollegeConfidenceNote, board.ConfidenceNote);
            var prestige = Assert.Single(board.FinanceChips, c => c.Label == "Prestige");
            _out.WriteLine($"Prestige chip: {prestige.Value} · {prestige.Sub}");
            Assert.Matches("^[A-DF][+-]?$", prestige.Value);
            Assert.Contains("/100", prestige.Sub);
            Assert.DoesNotContain("board starts", board.ConfidenceHeading);
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run126-college-board.png"));
            }
        }
        finally
        {
            window?.Close();
            Hardwood.Desktop.AppSettings.Use(oldSettings); Hardwood.Desktop.AppSettings.FilePath = oldPath;
        }
    }
}

public class Run126CollegeCreditTests
{
    [Fact]
    public void A_one_seed_on_target_that_reaches_the_elite_eight_earns_next_seasons_protection_and_a_final_is_named()
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        var ctx = new LeagueContext("College basketball", league.Teams, 28, 68, false, null, -10, 0, true);
        var team = league.Teams[2];
        team.Owner.Patience = 10;
        var job = new JobState { Confidence = 70, HiredSeason = 2025, Current = new Expectations { Season = 2026, Goal = SeasonGoal.Title, TargetWins = 25, TeamId = team.Id } };
        Board.SeasonReview(job, new Coach(), ctx, team, SeasonGoal.ConferenceFinals, 25, 2, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2027, 4, 7), 2026,
            madnessRound: 4, seed: 1, overall: (30, 4));
        Assert.Equal(2027, job.ProtectedSeason);
        Assert.Equal("reached the national final", Board.DescribeResult(SeasonGoal.Finals, ctx, madnessRound: 6));
        Assert.Equal("reached the Final Four", Board.DescribeResult(SeasonGoal.Finals, ctx, madnessRound: 5));
    }
}
