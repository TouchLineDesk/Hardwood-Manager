using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 116 (H97): at 1480 px and 130% text the top bar stays on one row, and the assistant folds to its strip in a live game at big text.</summary>
public class Run116TopBarTests
{
    private readonly ITestOutputHelper _out;
    public Run116TopBarTests(ITestOutputHelper o) => _out = o;

    private static void Settle(MainWindow window)
    {
        for (int i = 0; i < 4; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    [AvaloniaFact]
    public void Top_bar_fits_one_row_at_130_percent_on_a_1480_window()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run116-{Guid.NewGuid():N}.save");
        var oldSettings = AppSettings.Current;
        var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            start.SelectedTeam = start.Teams.First(t => t.Team.Abbreviation == "BOS");
            start.NewGame();
            vm.Guide.On = true;
            foreach (var scale in new[] { 1.0, 1.2, 1.3 })
            {
                window.ApplyScale(scale);
                Settle(window);
                var (l, r, bar) = window.TopBarWidths;
                _out.WriteLine($"{scale:P0}: left {l:0} + right {r:0} = {l + r:0} of {bar:0}; stacked {window.TopBarStacked}");
                Assert.False(window.TopBarStacked, $"top bar wraps at {scale:P0}: left {l:0} + right {r:0} of {bar:0}");
                Assert.True(window.TopBarOverlap() <= 1);
            }
            // In a live game at 130% the assistant folds to its strip (one click opens it); after the game it comes back.
            AppSettings.Current.UiScale = 1.3;
            var s = vm.Session!;
            int guard = 0;
            while (!s.HumanPlaysToday && guard++ < 60) s.AdvanceDay();
            vm.AfterTick();
            Assert.True(vm.Guide.Expanded);
            var (live, others) = s.BeginLiveDay();
            vm.InLiveGame = true;
            vm.CurrentPage = new LiveGameViewModel(vm, live!, others);
            Settle(window);
            Assert.False(vm.Guide.Expanded);
            Assert.True(vm.Guide.ShowStrip);
            Assert.False(window.TopBarStacked);
            if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                using var f = window.CaptureRenderedFrame();
                f?.Save(Path.Combine(dir, "run116-live-1480-130.png"));
            }
            vm.Guide.ExpandCommand.Execute(null);
            Assert.True(vm.Guide.Expanded);              // opened by hand: stays open for this game
            vm.InLiveGame = false;
            Assert.True(vm.Guide.Expanded);
            AppSettings.Current.UiScale = 1.0;
            vm.InLiveGame = true;
            Assert.True(vm.Guide.Expanded);              // normal text: the panel stays open in a live game
            vm.InLiveGame = false;
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
