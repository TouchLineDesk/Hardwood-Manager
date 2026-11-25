using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hardwood.Core.College;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Slice 10 (run 201), on screen: the Board page's "Your coaching career" panel, on day one and with a book of
/// seasons, milestones and a tree, at 1480 and 1280 px.</summary>
[Collection(SharedUiState.Name)]
public class Run201CoachingLegacyUiTests
{
    private readonly ITestOutputHelper _out;
    public Run201CoachingLegacyUiTests(ITestOutputHelper o) => _out = o;

    [AvaloniaFact]
    public void The_board_shows_your_coaching_career()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-run201-{Guid.NewGuid():N}.save");
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

            vm.Navigate("Board");
            var board = (BoardViewModel)vm.CurrentPage!;
            Assert.True(board.HasMyCareer);
            Assert.Contains("first college season", board.MyCareerHeadline);
            Assert.Single(board.MyCareerSeasons);
            Assert.False(board.HasMyTree);

            // A career with a past: two seasons, milestones and a branch on a real bench.
            var book = cw.MyCareer;
            book.Seasons.Add(new CoachSeason { Year = 2024, ProgramId = s.HumanTeam.Id, Program = s.HumanTeam.FullName, Wins = 24, Losses = 10, Seed = 9, Reached = 2 });
            book.Seasons.Add(new CoachSeason { Year = 2025, ProgramId = s.HumanTeam.Id, Program = s.HumanTeam.FullName, Wins = 29, Losses = 6, Seed = 3, Reached = 5 });
            book.Milestones.Add(new CoachMilestone { Key = "win-1", Date = new DateOnly(2024, 11, 5), Text = "Your first win as a head coach" });
            book.Milestones.Add(new CoachMilestone { Key = "final-four", Date = new DateOnly(2026, 3, 29), Text = "Your first Final Four" });
            var bench = cw.Benches.First(b => b.Seasons >= 2);
            book.Tree.Add(new TreeBranch { Name = bench.Name, Year = 2025, FromProgramId = s.HumanTeam.Id, FromProgram = s.HumanTeam.FullName, ProgramId = bench.ProgramId, Program = cw.Program(bench.ProgramId)!.FullName });
            vm.Navigate("Home");
            vm.Navigate("Board");
            board = (BoardViewModel)vm.CurrentPage!;
            _out.WriteLine(board.MyCareerHeadline);
            foreach (var l in board.MyCareerSeasons.Concat(board.MyCareerMilestones).Concat(board.MyCareerTree)) _out.WriteLine(l);
            Assert.Contains("53-16", board.MyCareerHeadline);
            Assert.Contains("2 NCAA bids", board.MyCareerHeadline);
            Assert.Contains("1 Final Four", board.MyCareerHeadline);
            Assert.Equal(3, board.MyCareerSeasons.Count);
            Assert.Contains("3 seed, a Final Four", board.MyCareerSeasons[1]);
            Assert.True(board.HasMyTree);
            Assert.StartsWith($"{bench.Name}: head coach at ", board.MyCareerTree[0]);

            Settle(window);
            var panel = window.GetVisualDescendants().OfType<Border>().First(b => b.Name == "MyCareerPanel");
            ScrollTo(panel);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Board career | {p}");
            Snap(window, "run201-board-career-1480");
            window.Width = 1280;
            Settle(window);
            ScrollTo(panel);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px Board career | {p}");
            Snap(window, "run201-board-career-1280");
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

    /// <summary>Scrolls the page so the panel is at the top (BringIntoView doesn't scroll in headless tests).</summary>
    private static void ScrollTo(Control panel)
    {
        var sv = panel.GetVisualAncestors().OfType<ScrollViewer>().First();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        if (panel.TranslatePoint(new Avalonia.Point(0, 0), (Visual)sv.Content!) is { } at) sv.Offset = new Avalonia.Vector(0, Math.Max(0, at.Y - 8));
        for (int i = 0; i < 3; i++) Avalonia.Threading.Dispatcher.UIThread.RunJobs();
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
