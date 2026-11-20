using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 97, on screen. H77: a college coach's spring with no games says the season is over. H76: a player who wants out
/// is "thinking of transferring" on Squad and Home, with only the talks a college coach has. H75: out of work in the
/// summer, the Board says applications are answered at the next step.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run97CollegeSpringUiTests
{
    private readonly ITestOutputHelper _out;
    public Run97CollegeSpringUiTests(ITestOutputHelper o) => _out = o;

    [AvaloniaFact]
    public void A_college_spring_and_an_out_of_work_summer_read_right()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-run97-{Guid.NewGuid():N}.save");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false });
        var problems = new List<string>();
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            start.PlayLeague = "College basketball";
            start.SelectedTeam = start.Teams.First(t => t.Team.City == "Clemson");
            start.NewGame();
            var s = vm.Session!;
            Assert.True(s.InCollege);
            int guard = 0;
            while ((s.World.College!.Season.ChampionId == null || s.NextMyGame != null || s.Today.Month < 4) && s.Phase != SessionPhase.Offseason && guard++ < 260) s.AdvanceDay();
            _out.WriteLine($"Spring: {s.Today:MMM d yyyy}, phase {s.Phase}");
            Assert.NotEqual(SessionPhase.Offseason, s.Phase);

            // H76: one player wants out.
            var unhappy = s.HumanTeam.Roster.OrderBy(p => p.Morale).First();
            unhappy.TradeRequested = true;
            vm.AfterTick();
            vm.Navigate("Home");
            var home = (HomeViewModel)vm.CurrentPage!;
            Assert.Equal("Season over", home.NextGame);
            Assert.Contains("mid-May", home.NextDetail);
            Assert.Contains(home.Pulse, r => r.Title == unhappy.Name && r.Detail.StartsWith("Thinking of transferring"));
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Home college spring | {p}");
            Snap(window, "run97-home-college-spring");

            vm.Navigate("Squad");
            var squad = (SquadViewModel)vm.CurrentPage!;
            Assert.Contains(squad.Players, r => r.Player == unhappy && r.Status == "Thinking of transferring");
            Settle(window);

            // H75: sacked going into the summer, the Board says when applications are answered.
            while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            s.Fire();
            foreach (var t in s.League.Teams.OrderByDescending(t => s.JobPrestige(t)).Take(3)) s.CoachLeaves(t, "fired");
            var open = s.Job.Vacancies.Where(v => v.Status == "open").Take(2).ToList();
            Assert.NotEmpty(open);
            foreach (var v in open) Assert.Null(s.Apply(v.TeamId));
            vm.AfterTick();
            vm.Navigate("Board");
            var board = (BoardViewModel)vm.CurrentPage!;
            Assert.Contains(board.Vacancies, r => r.Status == "Applied · they answer at the next step of the summer");
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Board out of work, summer | {p}");
            Snap(window, "run97-board-summer-applied");
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
        if (report is { Length: > 0 }) File.AppendAllLines(report, problems);
        Assert.Empty(problems);
    }

    private static void Settle(MainWindow window)
    {
        for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    private static void Snap(MainWindow window, string name)
    {
        Settle(window);
        if (Environment.GetEnvironmentVariable("HW_LAYOUT_SHOTS") is not { Length: > 0 } shots) return;
        Directory.CreateDirectory(shots);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(shots, $"{name}.png"));
    }
}
