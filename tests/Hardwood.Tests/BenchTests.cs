using System.Diagnostics;
using Avalonia.Headless.XUnit;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Phase 0 of Danny's roadmap (#596): the desktop half of the baseline (the core half is `hardwood bench`).
/// Off by default: set HW_BENCH to a file and it times, headlessly at 1480 × 900, the main window and start page,
/// a new real career through the start page, and every sidebar page opened cold (the first time) and warm
/// (the median of three later visits: a page refresh). It changes nothing in the game.
/// </summary>
[Collection(SharedUiState.Name)]
public class BenchTests
{
    [AvaloniaFact]
    public void Desktop_timings()
    {
        var outFile = Environment.GetEnvironmentVariable("HW_BENCH");
        if (string.IsNullOrEmpty(outFile)) return;
        var lines = new List<string>
        {
            $"## Desktop (headless Avalonia, 1480 × 900), {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC",
            $"- machine: {Environment.ProcessorCount} cores; .NET {Environment.Version}; seed 2026; the start page's first team",
            "",
        };
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false });
        var path = Path.Combine(Path.GetTempPath(), $"hw-bench-ui-{Guid.NewGuid():N}.hwsave");
        try
        {
            var sw = Stopwatch.StartNew();
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            window.UpdateLayout();
            lines.Add($"- main window and start page: {sw.ElapsedMilliseconds} ms");

            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path;
            start.SeedText = "2026";
            start.SelectedTeam = start.Teams.First();
            sw.Restart();
            start.NewGame();
            window.UpdateLayout();
            lines.Add($"- new career ({vm.Session?.HumanTeam.FullName}) to its first page: {sw.ElapsedMilliseconds} ms");
            lines.Add("");
            lines.Add("| page | cold ms | warm ms (median of 3) |");
            lines.Add("|---|---|---|");

            var pages = vm.Nav.Select(n => n.Name).ToList();
            var cold = new Dictionary<string, double>();
            var warm = pages.ToDictionary(p => p, _ => new List<double>());
            foreach (var p in pages) cold[p] = Visit(vm, window, p);
            for (int r = 0; r < 3; r++) foreach (var p in pages) warm[p].Add(Visit(vm, window, p));
            foreach (var p in pages)
            {
                var w = warm[p].OrderBy(x => x).ToList();
                lines.Add($"| {p} | {cold[p]:F0} | {w[1]:F0} |");
            }
            lines.Add($"| **total** | {cold.Values.Sum():F0} | {warm.Values.Sum(w => w.OrderBy(x => x).ElementAt(1)):F0} |");

            // A day's Continue from the UI (the top bar's button does the same), with its save.
            vm.Navigate(pages[0]);
            var times = new List<double>();
            for (int i = 0; i < 5; i++)
            {
                sw.Restart();
                vm.Session!.AdvanceDay();
                window.UpdateLayout();
                times.Add(sw.Elapsed.TotalMilliseconds);
            }
            lines.Add("");
            lines.Add($"- five days advanced from Home with the window open (save included): {string.Join(", ", times.Select(t => t.ToString("F0")))} ms");
            File.WriteAllLines(outFile, lines);
        }
        finally
        {
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f);
        }
    }

    private static double Visit(MainViewModel vm, MainWindow window, string page)
    {
        var sw = Stopwatch.StartNew();
        vm.Navigate(page);
        window.UpdateLayout();
        return sw.Elapsed.TotalMilliseconds;
    }
}
