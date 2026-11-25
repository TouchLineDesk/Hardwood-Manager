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

/// <summary>Slice 11 (run 202), on screen: a college player's page shows where he came from and his stories, at 1480 and
/// 1280 px, with the layout gate.</summary>
[Collection(SharedUiState.Name)]
public class Run202PlayerStoriesUiTests
{
    private readonly ITestOutputHelper _out;
    public Run202PlayerStoriesUiTests(ITestOutputHelper o) => _out = o;

    [AvaloniaFact]
    public void A_players_page_shows_his_origin_and_his_stories()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-run202-{Guid.NewGuid():N}.save");
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
            var p = s.HumanTeam.Roster.OrderByDescending(x => x.CurrentAbility).First();
            cw.Stories.Told.Add(new PlayerStory
            {
                PlayerId = p.Id, TeamId = s.HumanTeam.Id, Key = "hometown", Year = 2026, Date = new DateOnly(2026, 12, 20),
                Title = $"The hometown hero: {p.Name}", Text = "…", Posted = true,
            });
            cw.Stories.Told.Add(new PlayerStory
            {
                PlayerId = p.Id, TeamId = s.HumanTeam.Id, Key = "breakout", Year = 2026, Date = new DateOnly(2027, 1, 20),
                Title = $"The senior's breakout: {p.Name}", Text = "…", Posted = true,
            });
            vm.ShowPlayer(p);
            var page = (PlayerViewModel)vm.CurrentPage!;
            _out.WriteLine(page.Story);
            var lines = page.Story.Split('\n');
            Assert.Equal(3, lines.Length);
            Assert.StartsWith("From ", lines[0]);
            Assert.Contains("-star recruit", lines[0]);
            Assert.Equal("2026-27 · The hometown hero", lines[1]);
            Assert.Equal("2026-27 · The senior's breakout", lines[2]);

            Settle(window);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == page.Story && t.IsEffectivelyVisible);
            foreach (var x in LayoutCheck.Scan(window)) problems.Add($"1480px Player story | {x}");
            Snap(window, "run202-player-story-1480");
            window.Width = 1280;
            Settle(window);
            foreach (var x in LayoutCheck.Scan(window)) problems.Add($"1280px Player story | {x}");
            Snap(window, "run202-player-story-1280");
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
