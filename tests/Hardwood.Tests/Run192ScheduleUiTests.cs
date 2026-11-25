using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.College;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 192, on screen: the Board page's non-conference planner for a college coach, at 1480 and 1280 px. The
/// choices are kept with the program's books, and a major holiday event out of reach says why and keeps the old choice.</summary>
[Collection(SharedUiState.Name)]
public class Run192ScheduleUiTests
{
    private readonly ITestOutputHelper _out;
    public Run192ScheduleUiTests(ITestOutputHelper o) => _out = o;

    [AvaloniaFact]
    public void The_board_plans_next_seasons_non_conference_games()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-run192-{Guid.NewGuid():N}.save");
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
            start.SelectedTeam = start.Teams.First(t => t.Team.City == "George Mason");
            start.NewGame();
            var s = vm.Session!;
            for (int i = 0; i < 20; i++) s.AdvanceDay(); // a few games in, so the strength of schedule reads
            vm.AfterTick();
            vm.Navigate("Board");
            var page = (BoardViewModel)vm.CurrentPage!;
            Assert.True(page.HasPlan);
            Assert.Equal("Your non-conference schedule for 2027-28", page.PlanTitle);
            Assert.Equal(BoardViewModel.MixDraw, page.PlanMix);
            Assert.Equal(BoardViewModel.HolidayAny, page.PlanHoliday);
            Assert.Contains("Strength of schedule", page.PlanNow);
            _out.WriteLine(page.PlanNow);
            Assert.Null(s.HumanTeam.Money.Program.Schedule); // nothing chosen yet: the draw as before

            page.PlanMix = BoardViewModel.MixTough;
            page.PlanShowcase = true;
            var open = page.PlanHolidays.First(h => CollegeWorld.HolidayEvents.Contains(h) && !CollegeWorld.IsMajorHoliday(h));
            page.PlanHoliday = open;
            var plan = s.HumanTeam.Money.Program.Schedule!;
            Assert.Equal(ScheduleMix.Tough, plan.Mix);
            Assert.True(plan.Showcase);
            Assert.Equal(open, plan.Holiday);
            Assert.Contains("allows one win", page.PlanEffect);
            // George Mason (prestige under B) can't enter the biggest events: the page says why and keeps the open event.
            var cw = s.World.College!;
            Assert.True(cw.Prestige[s.HumanTeam.Id] < CollegeWorld.MajorHolidayPrestige);
            page.PlanHoliday = page.PlanHolidays.First(h => h.StartsWith(CollegeWorld.HolidayEvents[0]));
            Assert.Contains("invites programs of prestige", page.PlanHolidayNote);
            Assert.Equal(open, plan.Holiday);
            Assert.Equal(open, page.PlanHoliday);
            _out.WriteLine(page.PlanHolidayNote);

            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Board planner | {p}");
            Snap(window, "run192-board-plan-1480");
            window.Width = 1280;
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px Board planner | {p}");
            Snap(window, "run192-board-plan-1280");

            // Back on the page later: the choices read as saved.
            vm.Navigate("Home"); vm.Navigate("Board");
            page = (BoardViewModel)vm.CurrentPage!;
            Assert.Equal(BoardViewModel.MixTough, page.PlanMix);
            Assert.True(page.PlanShowcase);
            Assert.Equal(open, page.PlanHoliday);
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
