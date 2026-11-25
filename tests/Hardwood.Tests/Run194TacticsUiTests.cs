using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 194, on screen: a college Tactics page offers the 1-3-1 and the match-up zone and the full-court press,
/// at 1480 and 1280 px.</summary>
[Collection(SharedUiState.Name)]
public class Run194TacticsUiTests
{
    private readonly ITestOutputHelper _out;
    public Run194TacticsUiTests(ITestOutputHelper o) => _out = o;

    [AvaloniaFact]
    public void A_college_tactics_page_offers_the_zones_and_the_press()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-run194-{Guid.NewGuid():N}.save");
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
            vm.Navigate("Tactics");
            var tac = (TacticsViewModel)vm.CurrentPage!;
            Assert.True(tac.IsCollege);
            Assert.Equal(6, tac.Schemes.Count);
            Assert.Contains(tac.Schemes, o => o.Scheme == Hardwood.Core.Model.DefensiveScheme.Zone131);
            Assert.StartsWith("Your rotation suits the press", tac.PressFitText);
            _out.WriteLine(tac.PressFitText);
            // Pick the 1-3-1 and the press: the team's tactic follows.
            tac.SelectedScheme = tac.Schemes.First(o => o.Scheme == Hardwood.Core.Model.DefensiveScheme.Zone131);
            tac.FullCourtPress = true;
            Assert.Equal(Hardwood.Core.Model.DefensiveScheme.Zone131, s.HumanTeam.Tactic.Scheme);
            Assert.True(s.HumanTeam.Tactic.FullCourtPress);
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Tactics | {p}");
            Snap(window, "run194-tactics-1480");
            window.Width = 1280;
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px Tactics | {p}");
            Snap(window, "run194-tactics-1280");
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
