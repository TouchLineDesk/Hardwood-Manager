using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Slice 9 (run 200), on screen: the Board page's Rival coaches panel and the Home card's "Their coach" line on a
/// college coach's first day, at 1480 and 1280 px.</summary>
[Collection(SharedUiState.Name)]
public class Run200RivalCoachesUiTests
{
    private readonly ITestOutputHelper _out;
    public Run200RivalCoachesUiTests(ITestOutputHelper o) => _out = o;

    [AvaloniaFact]
    public void The_board_and_home_name_the_rival_coaches()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-run200-{Guid.NewGuid():N}.save");
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
            start.SavePath = path; start.SeedText = "9";
            start.PlayLeague = "College basketball";
            start.SelectedTeam = start.Teams.First(t => t.Team.City == "George Mason" || t.Team.FullName.StartsWith("George Mason"));
            start.NewGame();
            var s = vm.Session!;
            var cw = s.World.College!;

            vm.Navigate("Home");
            var home = (HomeViewModel)vm.CurrentPage!;
            _out.WriteLine(home.CourtLine);
            Assert.StartsWith("Their coach: ", home.CourtLine);
            Assert.Contains(cw.BenchOf(s.NextMyGame!.Opponent.Id)!.Name, home.CourtLine);
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Home | {p}");
            Snap(window, "run200-home-coach-1480");
            window.Width = 1280;
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px Home | {p}");
            Snap(window, "run200-home-coach-1280");

            window.Width = 1480;
            vm.Navigate("Board");
            var board = (BoardViewModel)vm.CurrentPage!;
            Assert.True(board.HasRivalCoaches);
            foreach (var r in board.RivalCoachRows) _out.WriteLine($"{r.Name} [{r.Tag}] {r.Detail}");
            int rivals = cw.RivalsOf(s.HumanTeam.Id).Count;
            Assert.True(board.RivalCoachRows.Count > rivals);
            Assert.All(board.RivalCoachRows.Take(rivals), r => Assert.Equal("Rival", r.Tag));
            Assert.All(board.RivalCoachRows, r => Assert.Contains("Reputation", r.Detail));
            Settle(window);
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Board rival coaches | {p}");
            Snap(window, "run200-board-coaches-1480");
            window.Width = 1280;
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px Board rival coaches | {p}");
            Snap(window, "run200-board-coaches-1280");
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
