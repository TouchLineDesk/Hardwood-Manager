using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Writes PNGs of each screen when HW_SCREENSHOT_DIR is set; otherwise skipped. Used for visual review.</summary>
public class ScreenshotTests
{
    [AvaloniaFact]
    public void Capture_screens()
    {
        var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(Path.GetTempPath(), $"hw-shot-{Guid.NewGuid():N}.save");
        try
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            Snap(window, Path.Combine(dir, "00-start.png"));

            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            start.SelectedTeam = start.Teams.First(t => t.Team.Abbreviation == "BOS");
            start.NewGame();
            var session = vm.Session!;
            for (int i = 0; i < 45; i++) session.AdvanceDay();
            vm.AfterTick();

            int n = 1;
            // Live game mid-way through.
            while (!session.HumanPlaysToday) session.AdvanceDay();
            vm.AfterTick();
            var (live, others) = session.BeginLiveDay();
            var liveVm = new LiveGameViewModel(vm, live!, others);
            vm.CurrentPage = liveVm;
            for (int i = 0; i < 120; i++) live!.Step();
            liveVm.Refresh();
            liveVm.PlayPause();
            Snap(window, Path.Combine(dir, $"{n++:00}-live-game.png"));
            liveVm.SimToEnd();
            liveVm.Continue();
            foreach (var nav in vm.Nav.ToList())
            {
                vm.Navigate(nav.Name);
                Snap(window, Path.Combine(dir, $"{n++:00}-{nav.Name.Replace(" & ", "-").Replace(' ', '-').ToLower()}.png"));
            }
            vm.Navigate("Roster");
            var roster = (RosterViewModel)vm.CurrentPage!;
            roster.Open(roster.Rows[0]);
            Snap(window, Path.Combine(dir, $"{n++:00}-player.png"));
            vm.Navigate("Schedule & Results");
            var sched = (ScheduleViewModel)vm.CurrentPage!;
            sched.OpenBox(sched.Games.First(g => g.Played));
            Snap(window, Path.Combine(dir, $"{n++:00}-boxscore.png"));

            int guard = 0;
            while (session.Phase != SessionPhase.Offseason && guard++ < 400) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Awards");
            Snap(window, Path.Combine(dir, $"{n++:00}-awards.png"));
            vm.Navigate("Offseason");
            Snap(window, Path.Combine(dir, $"{n++:00}-offseason-resign.png"));
            session.AdvanceOffseason(); vm.AfterTick();
            Snap(window, Path.Combine(dir, $"{n++:00}-offseason-draft.png"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void Snap(MainWindow window, string file)
    {
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(file);
    }
}
