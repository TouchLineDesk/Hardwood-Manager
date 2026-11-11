using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Roadmap 1: no clipped, trimmed or cut-off text on the key pages at 1280 and 1480 px, with and without the
/// assistant panel, and at the biggest text sizes (120-130%). Set HW_LAYOUT_REPORT to a file path to write every problem found.
/// </summary>
[Collection(SharedUiState.Name)]
public class LayoutCheckTests
{
    /// <summary>The pages the check covers: every sidebar page, plus a player, another team, and a box score.</summary>
    private static IEnumerable<(string name, Action<MainViewModel> open)> Pages(MainViewModel vm, LiveGameViewModel? live = null)
    {
        if (live != null) yield return ("Live game", v => { v.CurrentPage = live; live.Refresh(); });
        if (live != null) yield return ("Live stats", v => { v.CurrentPage = live; live.Refresh(); });
        foreach (var nav in vm.Nav.ToList())
            yield return (nav.Name, v => v.Navigate(nav.Name));
        yield return ("Player", v =>
        {
            v.Navigate("Roster");
            var roster = (RosterViewModel)v.CurrentPage!;
            roster.Open(roster.Rows[0]);
        });
        yield return ("Team page", v => v.ShowTeam(v.Session!.League.Teams.First(t => t != v.Session.HumanTeam)));
        yield return ("Box score", v =>
        {
            v.Navigate("Schedule & Results");
            var sched = (ScheduleViewModel)v.CurrentPage!;
            sched.OpenBox(sched.Games.First(g => g.Played));
        });
        // Roadmap 2b: the transfer market with a player selected, so the deal chips are on screen.
        yield return ("Transfer deal", v =>
        {
            v.Navigate("Transactions");
            var tx = (TransactionsViewModel)v.CurrentPage!;
            tx.SelectedMarket = tx.Market.FirstOrDefault(r => r.Player.Contract is { Buyout: > 0 }) ?? tx.Market.First();
        });
    }

    /// <summary>Tables size their columns over several layout passes; let them finish before looking.</summary>
    private static void Settle(MainWindow window)
    {
        for (int i = 0; i < 3; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    [AvaloniaFact]
    public void Key_pages_show_all_their_text()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-layout-{Guid.NewGuid():N}.save");
        var oldSettings = AppSettings.Current;
        var oldPath = AppSettings.FilePath;
        // Toggling the assistant saves settings; keep that away from the real settings file.
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false });
        var gate = new List<string>();
        var lines = new List<string>();
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
            var session = vm.Session!;
            for (int i = 0; i < 45; i++) session.AdvanceDay();
            vm.AfterTick();
            // A live game two quarters in (match day, roadmap 2), played on the side so the season isn't touched.
            var game = new Hardwood.Core.Engine.GameSimulator().StartLive(session.HumanTeam, session.League.Teams.First(t => t != session.HumanTeam),
                session.League.Rules, 77, session.Today);
            for (int i = 0; i < 115; i++) game.Step();
            var live = new LiveGameViewModel(vm, game, new());

            var sizes = new List<(int width, bool guide, double scale, bool gated)>
            {
                (1480, true, 1.0, true), (1480, false, 1.0, true), (1280, true, 1.0, true), (1280, false, 1.0, true),
            };
            // The biggest text sizes (roadmap 1): 130% at 1480 px is a 1138 px window, 120% at 1280 px a 1067 px one.
            sizes.Add((1480, true, 1.3, true));
            sizes.Add((1280, true, 1.2, true));
            sizes.Add((1280, true, 1.3, true));
            if (report != null) sizes.Add((1280, false, 1.3, false));
            foreach (var (width, guide, scale, gated) in sizes)
            {
                window.Width = width;
                window.ApplyScale(scale);
                vm.Guide.On = guide;
                foreach (var (name, open) in Pages(vm, live))
                {
                    open(vm);
                    try { Settle(window); }
                    catch (InvalidOperationException e) { throw new InvalidOperationException($"{name} at {width}px {(guide ? "with" : "without")} the assistant, {scale:P0}: {e.Message}", e); }
                    // The live screen's Stats tab (highlights and team stats, match day part 2).
                    if (name == "Live stats")
                    {
                        var tabs = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<TabControl>()
                            .First(t => Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<LiveGameView>(t) != null);
                        tabs.SelectedIndex = 2;
                        Settle(window);
                    }
                    if (Environment.GetEnvironmentVariable("HW_LAYOUT_SHOTS") is { Length: > 0 } shots)
                    {
                        Directory.CreateDirectory(shots);
                        using var frame = window.CaptureRenderedFrame();
                        frame?.Save(Path.Combine(shots, $"{width}-{(guide ? "a" : "n")}-{scale * 100:0}-{name.Replace(" & ", "-").Replace(' ', '-').ToLower()}.png"));
                    }
                    if (window.TopBarOverlap() is > 1 and var overlap)
                    {
                        string line = $"{width}px {(guide ? "assistant" : "no-assist")} {scale:P0} | {name,-20} | top bar overlaps itself by {overlap:0}px";
                        lines.Add(line);
                        if (gated) gate.Add(line);
                    }
                    foreach (var p in LayoutCheck.Scan(window))
                    {
                        string line = $"{width}px {(guide ? "assistant" : "no-assist")} {scale:P0} | {name,-20} | {p}";
                        lines.Add(line);
                        if (gated) gate.Add(line);
                    }
                }
            }
            window.ApplyScale(1.0);
        }
        finally
        {
            // Close the window and let its last render finish here, not in the next test's session.
            window?.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath;
            AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
            if (report != null) File.WriteAllLines(report, lines);
        }
        Assert.True(gate.Count == 0, $"{gate.Count} pieces of text are cut off:\n" + string.Join("\n", gate.Take(60)));
    }

    [AvaloniaFact]
    public void Flow_chart_run_labels_stay_off_the_line()
    {
        // Review #6: "11-2", "9-2 8-0" and "14-0" sat on the win-probability line.
        var league = new Hardwood.Core.Generation.WorldGenerator(new Hardwood.Core.Rng(51)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var sim = new Hardwood.Core.Engine.GameSimulator();
        int shown = 0;
        for (int g = 0; g < 30; g++)
        {
            var r = sim.Simulate(league.Teams[g % 8], league.Teams[8 + g % 8], Hardwood.Core.Model.Rules.Nba, (ulong)(300 + g), keepLog: true);
            foreach (var size in new[] { new Avalonia.Size(420, 150), new Avalonia.Size(640, 150), new Avalonia.Size(900, 190) })
            {
                var labels = Hardwood.Desktop.Controls.GameFlowChart.PlaceLabels(r, size);
                var (prob, _) = Hardwood.Desktop.Controls.GameFlowChart.Areas(size);
                var line = Hardwood.Desktop.Controls.GameFlowChart.LinePoints(r, (prob, Hardwood.Desktop.Controls.GameFlowChart.Span(r)));
                var runScores = Hardwood.Core.Engine.GameFlow.Runs(r).Select(x => x.Score).ToHashSet();
                foreach (var l in labels.Where(l => runScores.Contains(l.Text)))
                {
                    shown++;
                    Assert.False(Hardwood.Desktop.Controls.GameFlowChart.CrossesLine(line, l.Box), $"game {g}, {size}: \"{l.Text}\" on the line");
                }
            }
        }
        Assert.True(shown > 30, $"only {shown} run labels shown");
    }

    [AvaloniaFact]
    public void Chart_labels_fit_their_slot()
    {
        var c = Avalonia.Media.Colors.Gray;
        foreach (var label in new[] { "League avg", "2026-27", "We allow", "Conference finals" })
            foreach (double room in new[] { 40.0, 55, 80 })
            {
                var lines = Hardwood.Desktop.Controls.BarChart.FitLabel(label, room, c);
                Assert.InRange(lines.Count, 1, 2);
                // Either everything fits, or it's the last resort (one line at the smallest size) for a tiny slot.
                Assert.True(lines.All(l => l.Width <= room) || (lines.Count == 1 && room < 50), $"{label} at {room}px");
            }
    }

    [AvaloniaFact]
    public void Text_size_setting_scales_the_window_and_is_saved()
    {
        var old = AppSettings.Current;
        var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings());
        try
        {
            Assert.Equal(1.0, UiScale.Current);
            Assert.Equal(0.9, UiScale.Clamp(0.5));
            Assert.Equal(1.3, UiScale.Clamp(4));
            Assert.Equal(1.0, UiScale.Clamp(double.NaN));

            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var settings = new SettingsViewModel(vm);
            Assert.Equal("100% (standard)", settings.Size);
            settings.Size = "120%";
            Assert.Equal(1.2, AppSettings.Current.UiScale, 3);
            var t = Assert.IsType<Avalonia.Media.ScaleTransform>(window.FindControl<LayoutTransformControl>("ScaleRoot")!.LayoutTransform);
            Assert.Equal(1.2, t.ScaleX, 3);
            Assert.Equal(1.2, AppSettings.Load().UiScale, 3); // written to settings.json
            settings.Size = "not a size";
            Assert.Equal("120%", settings.Size);
            window.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath;
            AppSettings.Use(old);
        }
    }
}
