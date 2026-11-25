using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.College;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 193, on screen: bracketology's bubble with each team's NET and Quad 1 record, our NET chip with the four
/// quadrants, the Top 25's Monday note, and after Selection Sunday the NIT, at 1480 and 1280 px.</summary>
[Collection(SharedUiState.Name)]
public class Run193BubbleUiTests
{
    private readonly ITestOutputHelper _out;
    public Run193BubbleUiTests(ITestOutputHelper o) => _out = o;

    [AvaloniaFact]
    public void Bracketology_shows_the_NET_and_the_quadrants_and_the_poll_says_Monday()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-run193-{Guid.NewGuid():N}.save");
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
            var cw = s.World.College!;
            while (s.Today < new DateOnly(cw.Season.Year + 1, 2, 16)) s.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("College");
            var college = (CollegeViewModel)vm.CurrentPage!;
            Assert.Contains("Released every Monday", college.PollNote);
            _out.WriteLine(college.PollNote);
            college.Tab = 2;
            Assert.True(college.HasBracketology);
            var net = college.BracketologyChips.Single(c => c.Label == "Our NET");
            Assert.Equal($"No. {cw.NetRank(s.HumanTeam.Id)}", net.Value);
            Assert.StartsWith("Q1 ", net.Sub);
            _out.WriteLine($"{net.Value} · {net.Sub}");
            Assert.All(college.Bubble.SelectMany(b => b.Teams), t => Assert.StartsWith("NET ", t.Detail));
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Bracketology | {p}");
            Snap(window, "run193-bubble-1480");
            window.Width = 1280;
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px Bracketology | {p}");
            Snap(window, "run193-bubble-1280");
            window.Width = 1480;

            // Through Selection Sunday and the NIT's first round: the field and the NIT are set, and a program in the NIT sees its chip.
            while (s.Today < new DateOnly(cw.Season.Year + 1, 3, 20)) s.AdvanceDay();
            vm.AfterTick();
            Assert.Equal(CollegeWorld.NitSize, cw.Season.NitField.Count);
            vm.Navigate("Home"); vm.Navigate("College");
            college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 2;
            college.BracketView = CollegeViewModel.FullView;
            if (cw.InNit(s.HumanTeam.Id))
            {
                var chip = college.MadnessChips.Single(c => c.Label == "NIT");
                Assert.Equal($"No. {cw.NitSeedOf(s.HumanTeam.Id)} seed", chip.Value);
                _out.WriteLine($"NIT chip: {chip.Value} · {chip.Sub}");
            }
            _out.WriteLine($"George Mason: seed {cw.SeedOf(s.HumanTeam.Id)}, NIT {cw.NitSeedOf(s.HumanTeam.Id)}");
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px March | {p}");
            Snap(window, "run193-march-1480");
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
