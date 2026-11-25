using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>CS9 (run 191), on screen: the Board page's coaching-carousel panel in a college coach's summer, at 1480 and 1280 px,
/// with your conference first and every change shown on "Show all".</summary>
[Collection(SharedUiState.Name)]
public class Run191CarouselUiTests
{
    private readonly ITestOutputHelper _out;
    public Run191CarouselUiTests(ITestOutputHelper o) => _out = o;

    [AvaloniaFact]
    public void The_board_shows_the_spring_carousel()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-run191-{Guid.NewGuid():N}.save");
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
            s.Job.Current!.TargetWins = 0; // kept on, whatever the record
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 700) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            Assert.True(s.Employed);
            vm.AfterTick();
            vm.Navigate("Board");
            var board = (BoardViewModel)vm.CurrentPage!;
            Assert.True(board.HasCarousel);
            Assert.StartsWith("The coaching carousel, spring 2027", board.CarouselTitle);
            Assert.InRange(board.CarouselRows.Count, 1, BoardViewModel.CarouselShown);
            foreach (var r in board.CarouselRows) _out.WriteLine($"{r.Name} [{r.Tag}] {r.Detail}");
            // Your conference (the ACC) and your rivals come first.
            var cw = s.World.College!;
            var acc = s.LatestCarousel.Count(m => cw.ConferenceOf[m.ProgramId] == cw.ConferenceOf[s.HumanTeam.Id]
                || cw.RivalsOf(s.HumanTeam.Id).Any(r => r.RivalId == m.ProgramId));
            Assert.All(board.CarouselRows.Take(Math.Min(acc, BoardViewModel.CarouselShown)), r => Assert.True(r.HasTag, r.Name));
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Board carousel | {p}");
            Snap(window, "run191-board-carousel-1480");

            Assert.True(board.HasCarouselMore);
            board.ToggleCarouselCommand.Execute(null);
            Assert.Equal(s.LatestCarousel.Count, board.CarouselRows.Count);
            Assert.Equal("Show fewer", board.CarouselToggle);
            window.Width = 1280;
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px Board carousel, all | {p}");
            Snap(window, "run191-board-carousel-1280-all");
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
