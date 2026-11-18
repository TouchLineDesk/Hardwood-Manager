using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 85. The 2D tracker's third slice (`docs/tracker-plan.md` 3): the big view. The court takes the room the
/// window has and the play by play / box score folds under it (never under <see cref="LiveGameView.MinLogHeight"/>);
/// "Smaller court" keeps the old 300 px. Presentation only.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run85BigCourtTests
{
    private readonly ITestOutputHelper _out;
    public Run85BigCourtTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void The_cap_leaves_the_play_by_play_its_share_and_never_shrinks_the_court()
    {
        Assert.Equal(LiveGameView.SmallCourtHeight, LiveGameView.BigCourtCap(500, 120, 10));
        Assert.Equal(1000 - 120 - 10 - LiveGameView.MinLogHeight, LiveGameView.BigCourtCap(1000, 120, 10));
        Assert.Equal(LiveGameView.SmallCourtHeight, LiveGameView.BigCourtCap(0, 0, 0));
    }

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData(1480, 900, 1.0, "run85-big-1480")]
    [InlineData(1920, 1080, 1.0, "run85-big-1920")]
    [InlineData(1480, 900, 1.3, "run85-big-1480-130")]
    [InlineData(2560, 1440, 1.0, "run85-big-2560")]
    public void The_court_grows_on_wide_windows_and_the_play_by_play_folds_under_it(double width, double height, double scale, string shot)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run85-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        var settingsPath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.FilePath = settingsPath;
        AppSettings.Use(new AppSettings { AutoPortraits = false, UiScale = scale });
        string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
        if (dir != null) Directory.CreateDirectory(dir);   // T1 (run 106): a missing folder no longer fails the test
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = width, Height = height };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            start.SelectedTeam = start.Teams.First(t => t.Team.Abbreviation == "BOS");
            start.NewGame();
            var s = vm.Session!;
            int guard = 0;
            while (!s.HumanPlaysToday && guard++ < 60) s.AdvanceDay();
            Assert.True(s.HumanPlaysToday);
            vm.AfterTick();
            var w = window;
            void Settle() { for (int i = 0; i < 5; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

            var (live, others) = s.BeginLiveDay();
            var page = new LiveGameViewModel(vm, live!, others);
            vm.CurrentPage = page; Settle();
            var view = window.GetVisualDescendants().OfType<LiveGameView>().First(v => v.Bounds.Width > 0);
            var court = view.GetVisualDescendants().OfType<LiveCourt>().First();
            var log = view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "LogPanel");
            for (int i = 0; i < 30; i++) page.StepOne();
            Settle();

            // Big by default: the court is over 300 px wherever the window has the room, and the panel under it keeps its share.
            Assert.True(page.BigCourt);
            Assert.Equal("Smaller court", page.BigCourtLabel);
            double big = court.Bounds.Height;
            _out.WriteLine($"{width}x{height} @{scale:P0}: court {court.Bounds.Width:0}x{big:0}, play by play {log.Bounds.Height:0}");
            // Wide windows: over 300 px. At 130% on 1480 the page is narrow (about 1140 px), so the court keeps the 3:2 column.
            if (scale == 1.0) Assert.True(big > LiveGameView.SmallCourtHeight + 20, $"court only {big:0} px tall at {width}x{height}");
            Assert.True(log.Bounds.Height >= LiveGameView.MinLogHeight - 1, $"play by play only {log.Bounds.Height:0} px");
            Assert.True(Math.Abs(court.Bounds.Width / big - LiveCourt.Aspect) < 0.02);   // still 94:50
            // The court keeps its size from play to play (the cap only moves when the room does).
            for (int i = 0; i < 12; i++) { page.StepOne(); Settle(); Assert.True(Math.Abs(court.Bounds.Height - big) < 1, $"court went {big:0} -> {court.Bounds.Height:0} on a new play"); }
            var problems = LayoutCheck.Scan(window);
            Assert.True(problems.Count == 0, $"Live game, big court {width}x{height} @{scale}:\n" + string.Join("\n", problems));
            if (dir != null) { using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, shot + ".png")); }

            // The box score folds under it too.
            page.ToBoxCommand.Execute(null); Settle();
            Assert.True(view.GetVisualDescendants().OfType<ScrollViewer>().First(x => x.Name == "LiveBox").IsEffectivelyVisible);
            Assert.True(Math.Abs(court.Bounds.Height - big) < 2);
            problems = LayoutCheck.Scan(window);
            Assert.True(problems.Count == 0, $"Live game, big court, box score {width}x{height} @{scale}:\n" + string.Join("\n", problems));
            if (dir != null && width == 1480) { using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, shot + "-box.png")); }
            page.ToLogCommand.Execute(null);

            // Game flow and back: the court comes back at its size.
            page.ToFlowCommand.Execute(null); Settle();
            page.ToCourtCommand.Execute(null); Settle();
            Assert.True(Math.Abs(court.Bounds.Height - big) < 2);

            // Smaller court: back to 300 px, and the choice is remembered.
            page.ToggleBigCourtCommand.Execute(null); Settle();
            Assert.False(page.BigCourt);
            Assert.Equal("Bigger court", page.BigCourtLabel);
            Assert.True(court.Bounds.Height <= LiveGameView.SmallCourtHeight + 0.5);
            Assert.False(AppSettings.Current.LiveBigCourt);
            page.ToggleBigCourtCommand.Execute(null); Settle();
            Assert.True(Math.Abs(court.Bounds.Height - big) < 2);
            Assert.True(AppSettings.Current.LiveBigCourt);

            // The motion still ends on the frame at the big size.
            page.StepOne(); Settle();
            Thread.Sleep(700);
            Assert.True(court.Pose!.Done);
            Assert.Equal(page.Frame!.Ball, court.Pose.Ball);
            page.SimToEnd(); Settle();
            Assert.True(page.Complete);
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
            try { File.Delete(settingsPath); } catch { }
        }
    }
}
