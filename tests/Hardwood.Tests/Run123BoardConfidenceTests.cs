using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 123, H70: board confidence no longer collapses for a playoff team close to its target. A season (the weekly drift and the
/// review together) costs such a team at most <see cref="Board.SeasonDropCap"/>; "Critical" needs a losing record; next season's
/// target doesn't fall more than 3 below the wins the club just managed.
/// </summary>
public class Run123BoardConfidenceTests
{
    private static readonly Dictionary<int, PlayerSeasonStats> None = new();

    private static (League league, Team team) Setup(int patience)
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        var team = league.Teams.OrderBy(t => Board.ProjectedRank(league, t)).First();
        team.Owner.Patience = patience; team.Owner.Ambition = 10;
        return (league, team);
    }

    [Fact]
    public void The_cap_is_smaller_for_a_patient_owner()
    {
        var (_, team) = Setup(17);
        double patient = Board.SeasonDropCap(team.Owner);
        team.Owner.Patience = 3;
        double impatient = Board.SeasonDropCap(team.Owner);
        Assert.InRange(patient, 18, 21);
        Assert.InRange(impatient, 25, 28);
    }

    [Fact]
    public void A_patient_owners_56_win_playoff_team_keeps_his_backing()
    {
        // Hunt 13, nba-real-strong (seed 31): 56-26 and a round won against a 57-win target, "conference finals": 85 → 48.
        var (league, team) = Setup(17);
        var job = new JobState
        {
            Current = new Expectations { Season = 2028, Goal = SeasonGoal.ConferenceFinals, TargetWins = 57 },
            Confidence = 70, SeasonStartConfidence = 85, SeasonStartSeason = 2028, HiredSeason = 2026,
        };
        var (_, body, sacked) = Board.SeasonReview(job, new Coach(), LeagueContext.Nabl(league), team, SeasonGoal.SecondRound, 56, 26, None, new DateOnly(2029, 5, 7), 2028);
        Assert.False(sacked);
        Assert.True(job.Confidence >= 85 - Board.SeasonDropCap(team.Owner) - 0.01, $"confidence {job.Confidence}");
        Assert.True(job.Confidence <= 70, "the review doesn't raise a disappointing season");
        Assert.Contains("still has the board's backing", body);
        Assert.Equal(true, job.LastSeasonWinning);
    }

    [Fact]
    public void A_54_win_first_round_exit_is_not_critical()
    {
        // Hunt 13, nba-contender (seed 2026, impatient owner): 54-28, 4th in the West, out in round one → "Critical".
        var (league, team) = Setup(3);
        var job = new JobState
        {
            Current = new Expectations { Season = 2026, Goal = SeasonGoal.Title, TargetWins = 60 },
            Confidence = 52, SeasonStartConfidence = 65, SeasonStartSeason = 2026, HiredSeason = 2025,
        };
        Board.SeasonReview(job, new Coach(), LeagueContext.Nabl(league), team, SeasonGoal.Playoffs, 54, 28, None, new DateOnly(2027, 5, 5), 2026);
        Assert.True(job.Confidence >= 65 - Board.SeasonDropCap(team.Owner) - 0.01, $"confidence {job.Confidence}");
        Assert.NotEqual("Critical", Board.Mood(job.Confidence, winning: true).Label);
        Assert.Equal("Critical", Board.Mood(20, winning: false).Label);
        Assert.Equal("Under pressure", Board.Mood(20, winning: true).Label);
        Assert.Equal("Secure", Board.Mood(80, winning: false).Label);
    }

    [Fact]
    public void Missing_the_playoffs_or_falling_far_short_is_not_capped()
    {
        var (league, team) = Setup(10);
        var ctx = LeagueContext.Nabl(league);
        // Missed the playoffs.
        var missed = new JobState { Current = new Expectations { Season = 2026, Goal = SeasonGoal.ConferenceFinals, TargetWins = 52 }, Confidence = 60, SeasonStartConfidence = 80, SeasonStartSeason = 2026, HiredSeason = 2025 };
        Board.SeasonReview(missed, new Coach(), ctx, team, SeasonGoal.PlayIn, 43, 39, None, new DateOnly(2027, 5, 5), 2026);
        Assert.True(missed.Confidence < 80 - Board.SeasonDropCap(team.Owner), $"confidence {missed.Confidence}");
        // Made the playoffs but 12 wins short.
        var far = new JobState { Current = new Expectations { Season = 2026, Goal = SeasonGoal.ConferenceFinals, TargetWins = 55 }, Confidence = 60, SeasonStartConfidence = 80, SeasonStartSeason = 2026, HiredSeason = 2025 };
        Board.SeasonReview(far, new Coach(), ctx, team, SeasonGoal.Playoffs, 43, 39, None, new DateOnly(2027, 5, 5), 2026);
        Assert.True(far.Confidence < 80 - Board.SeasonDropCap(team.Owner), $"confidence {far.Confidence}");
    }

    [Fact]
    public void The_weekly_drift_stops_at_the_cap_for_a_winning_team_near_its_target()
    {
        // Seed 31's patient Spurs after their title: 100 drifted to 56 while they went 52-30 against 57.
        var (league, team) = Setup(17);
        var ctx = LeagueContext.Nabl(league);
        var job = new JobState { Current = new Expectations { Season = 2027, Goal = SeasonGoal.Title, TargetWins = 57 }, Confidence = 100, HiredSeason = 2026 };
        var day = new DateOnly(2027, 10, 22);
        for (int g = 6; g <= 82; g += 4)
        {
            int wins = (int)Math.Round(g * 52 / 82.0);
            Board.Weekly(job, ctx, team, wins, g - wins, None, day, 2027);
            day = day.AddDays(7);
        }
        Assert.Equal(100, job.SeasonStartConfidence);
        Assert.True(job.Confidence >= 100 - Board.SeasonDropCap(team.Owner) - 0.01, $"confidence {job.Confidence}");

        // A losing team gets no floor.
        var losing = new JobState { Current = new Expectations { Season = 2027, Goal = SeasonGoal.Title, TargetWins = 57 }, Confidence = 100, HiredSeason = 2026 };
        day = new DateOnly(2027, 10, 22);
        for (int g = 6; g <= 82; g += 4)
        {
            int wins = (int)Math.Round(g * 38 / 82.0);
            Board.Weekly(losing, ctx, team, wins, g - wins, None, day, 2027);
            day = day.AddDays(7);
        }
        Assert.True(losing.Confidence < 100 - Board.SeasonDropCap(team.Owner), $"confidence {losing.Confidence}");
    }

    [Fact]
    public void Next_target_stays_near_the_wins_just_managed()
    {
        // Hunt 13: a 56-win team (target 57, goal missed) was set "around 49 wins".
        var (league, team) = Setup(17);
        var ctx = LeagueContext.Nabl(league);
        var prev = new Expectations { Season = 2028, Goal = SeasonGoal.ConferenceFinals, TargetWins = 57, TeamId = team.Id, League = "nabl", Achieved = SeasonGoal.SecondRound, Wins = 56 };
        var next = Board.NextExpectations(ctx, team, 2029, prev, "nabl", 1, 1);
        Assert.True(next.TargetWins >= 53, $"target {next.TargetWins}");
        Assert.True(next.TargetWins <= 57, $"target {next.TargetWins}");
        // A bad season still lets it fall (44 wins against 57: no higher than 57, at most 3 under 44 by this rule).
        var bad = new Expectations { Season = 2028, Goal = SeasonGoal.ConferenceFinals, TargetWins = 57, TeamId = team.Id, League = "nabl", Achieved = SeasonGoal.Develop, Wins = 44 };
        Assert.True(Board.NextExpectations(ctx, team, 2029, bad, "nabl", 1, 1).TargetWins <= 57);
    }

    [Fact]
    public void A_job_from_an_older_save_loads_without_the_new_fields()
    {
        const string json = "{\"Employed\":true,\"Confidence\":72.5,\"HiredSeason\":2026,\"Vacancies\":[],\"History\":[],\"WarningLevel\":0,\"ProtectedSeason\":0,\"ProtectedReason\":\"\",\"RebuildUsed\":false}";
        var job = System.Text.Json.JsonSerializer.Deserialize<JobState>(json)!;
        Assert.Null(job.SeasonStartConfidence);
        Assert.Null(job.LastSeasonWinning);
        Assert.Equal(72.5, job.Confidence);
    }
}

/// <summary>Run 123, H70: the Board page's mood label for a winning club at low confidence (screenshot `run123-board-mood.png`).</summary>
public class Run123BoardMoodScreenTests
{
    [AvaloniaFact]
    public void A_winning_club_at_low_confidence_reads_under_pressure()
    {
        var oldSettings = Hardwood.Desktop.AppSettings.Current; var oldPath = Hardwood.Desktop.AppSettings.FilePath;
        Hardwood.Desktop.AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        Hardwood.Desktop.AppSettings.Use(new Hardwood.Desktop.AppSettings { AutoPortraits = false });
        Hardwood.Desktop.Views.MainWindow? window = null;
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            int team = probe.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).First().Id;
            var s = GameSession.NewGame(2026, team, null);
            s.Job.Confidence = 30;
            s.Job.LastSeasonWinning = true;
            var vm = new Hardwood.Desktop.ViewModels.MainViewModel();
            window = new Hardwood.Desktop.Views.MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            vm.Navigate("Board");
            for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            var page = Assert.IsType<Hardwood.Desktop.ViewModels.BoardViewModel>(vm.CurrentPage);
            Assert.StartsWith("Under pressure", page.ConfidenceText);
            s.Job.LastSeasonWinning = false;
            Assert.Equal("Critical", Board.Mood(s.Job.Confidence, Hardwood.Desktop.ViewModels.BoardViewModel.WinningForMood(s)).Label);
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run123-board-mood.png"));
            }
        }
        finally
        {
            window?.Close();
            Hardwood.Desktop.AppSettings.Use(oldSettings); Hardwood.Desktop.AppSettings.FilePath = oldPath;
        }
    }
}
