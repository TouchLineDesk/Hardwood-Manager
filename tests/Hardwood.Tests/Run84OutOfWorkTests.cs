using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 84. Bug hunt H52: out of work, Rotation, Tactics and Staff & Training still opened on the old club, and
/// Rotation's refresh wrote a depth chart to it (the engine plays every club by its chart). H45 (rest): in the summer the
/// Board page counted the deals that are up as spent, while every other screen didn't.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run84OutOfWorkTests
{
    private static string? Dir => Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Out_of_work_the_old_clubs_pages_are_closed_and_nothing_is_written_to_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run84-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            var w = window;
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }
            string[] closed = { "Rotation", "Tactics", "Staff & Training", "Transactions" };

            // In work: the pages open, Home has its Game plan and Rotation buttons.
            foreach (var name in closed) Assert.True(vm.Nav.First(n => n.Name == name).IsVisible, name);
            vm.Navigate("Home"); Settle();
            Assert.True(Assert.IsType<HomeViewModel>(vm.CurrentPage).Coaching);
            vm.Navigate("Rotation"); Settle();
            Assert.IsType<RotationViewModel>(vm.CurrentPage);
            Assert.NotNull(s.HumanTeam.DepthChart);
            vm.Navigate("Staff & Training"); Settle();
            var staff = Assert.IsType<StaffViewModel>(vm.CurrentPage);
            vm.Navigate("Tactics"); Settle();
            vm.Navigate("Rotation"); Settle();
            vm.ShowPlayer(s.HumanTeam.Roster[0]); Settle();   // Back would return to Rotation

            // Sacked mid-season, sitting on a player page with Rotation behind it.
            var club = s.HumanTeam;
            s.Fire();
            Assert.False(s.Employed);
            Assert.Null(club.DepthChart);                     // the computer picks its own five again
            int intensity = club.Training.Intensity;
            vm.AfterTick(); Settle();
            Assert.Null(club.DepthChart);                     // H52: refreshing the cached Rotation page wrote one back
            foreach (var name in closed) Assert.False(vm.Nav.First(n => n.Name == name).IsVisible, name);

            vm.GoBack(); Settle();
            Assert.IsType<BoardViewModel>(vm.CurrentPage);    // Back to Rotation opens the Board (open jobs) instead
            foreach (var name in closed)
            {
                vm.Navigate(name); Settle();
                Assert.IsType<BoardViewModel>(vm.CurrentPage);
            }
            Assert.Null(club.DepthChart);

            // The cached pages can't write either, even if a control fires (belt and braces).
            staff.Intensity = intensity >= 50 ? 10 : 90;
            Assert.Equal(intensity, club.Training.Intensity);
            var rotation = (RotationViewModel)vm.Page("Rotation");
            rotation.AutoRotation();
            rotation.Recalc();
            Assert.Null(club.DepthChart);

            // Home: no Game plan / Rotation buttons out of work.
            vm.Navigate("Home"); Settle();
            var home = Assert.IsType<HomeViewModel>(vm.CurrentPage);
            Assert.False(home.Coaching);
            var homeView = window.GetVisualDescendants().OfType<HomeView>().First(v => v.DataContext == home);
            Assert.DoesNotContain(homeView.GetVisualDescendants().OfType<Button>(),
                b => b.IsEffectivelyVisible && b.CommandParameter is "Tactics" or "Rotation");

            // A day on, out of work, still nothing written; the save keeps no chart for the old club.
            s.AdvanceDay(); vm.AfterTick(); Settle();
            Assert.Null(club.DepthChart);
            Assert.True(LayoutCheck.Scan(window).Count == 0, string.Join("\n", LayoutCheck.Scan(window)));
            if (Dir is { } dir) { using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, "run84-home-out-of-work.png")); }
        }
        finally
        {
            window?.Close();
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void In_the_summer_the_Board_counts_committed_wages_like_every_other_screen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run84b-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            var w = window;
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }
            static string M(long v) => Math.Abs(v) >= 10_000_000 ? $"${v / 1e6:F0}M" : $"${v / 1e6:F1}M";

            // In season: payroll, as before.
            vm.Navigate("Board"); Settle();
            var board = Assert.IsType<BoardViewModel>(vm.CurrentPage);
            var wages = board.FinanceChips.First(c => c.Label == "Wages");
            Assert.Equal(M(ContractMarket.Payroll(s.HumanTeam)), wages.Value);
            Assert.DoesNotContain("expiring", wages.Sub);

            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 600) s.AdvanceDay();
            Assert.NotEmpty(s.MyExpiring);
            vm.AfterTick(); vm.Navigate("Board"); Settle();
            board = Assert.IsType<BoardViewModel>(vm.CurrentPage);
            wages = board.FinanceChips.First(c => c.Label == "Wages");
            long up = s.MyExpiring.Sum(p => p.Contract?.Salary ?? 0);
            Assert.Equal(M(s.MyCommittedWages), wages.Value);
            Assert.Contains($"not counting {M(up)} of {s.MyExpiring.Count} expiring", wages.Sub);
            var room = board.FinanceChips.First(c => c.Label == "Wage room");
            Assert.Equal(M(Math.Max(0, s.HumanTeam.Money.WageBudget - s.MyCommittedWages)), room.Value);
            Assert.Contains($"wages committed {Budgets.Money(s.MyCommittedWages)}", s.MyBudgetLine);   // the same figure
            Assert.True(LayoutCheck.Scan(window).Count == 0, string.Join("\n", LayoutCheck.Scan(window)));
            if (Dir is { } dir) { using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, "run84-board-summer.png")); }
        }
        finally
        {
            window?.Close();
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
