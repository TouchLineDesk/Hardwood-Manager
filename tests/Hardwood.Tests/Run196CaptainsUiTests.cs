using Avalonia.Headless;
using Hardwood.Core.College;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 196 on screen: the Squad page's captaincy button, the "Captain" status and the chemistry line.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run196CaptainsUiTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_Squad_page_names_the_captains_and_changes_them()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run196ui-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
            int id = world.College!.Programs.First(p => p.FullName.StartsWith("George Mason")).Id;
            var s = GameSession.NewGame(9, id, path);
            var day = s.World.College!.CaptainsDay!.Value;
            int guard = 0;
            while (s.Today <= day && guard++ < 120) s.AdvanceDay();
            var caps = s.MyCaptains;
            Assert.Equal(2, caps.Count);

            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            var w = window;
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }
            void Shot(string file)
            {
                Settle();
                if (string.IsNullOrEmpty(dir)) return;
                Directory.CreateDirectory(dir);
                using var frame = w.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, file));
            }

            vm.Navigate("Squad"); Settle();
            var sq = (SquadViewModel)vm.CurrentPage!;
            sq.Refresh(); Settle();
            Assert.Contains(sq.ChemistryFactors, f => f.Reason.StartsWith("Captains "));
            Assert.All(caps, c => Assert.Equal("Captain", sq.Players.First(r => r.Player == c).Status));

            // A captain: the button takes the captaincy away.
            sq.Pick(sq.Players.First(r => r.Player == caps[0])); Settle();
            var take = sq.Topics.Single(t => t.Label == "Take away the captaincy");
            Assert.True(take.Command.CanExecute(null));
            // Another player: blocked while two are captains (the tooltip says why).
            var other = sq.Players.First(r => !caps.Contains(r.Player));
            sq.Pick(other); Settle();
            var make = sq.Topics.Single(t => t.Label == "Make captain");
            Assert.False(make.Command.CanExecute(null));
            Assert.Contains("take the captaincy from one", make.Tip);

            var problems = new List<string>();
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Squad | {p}");
            Shot("run196-squad-1480.png");

            // Take one away, then name the other player.
            sq.Pick(sq.Players.First(r => r.Player == caps[1])); Settle();
            sq.Topics.Single(t => t.Label == "Take away the captaincy").Command.Execute(null); Settle();
            Assert.Contains("takes it badly", sq.Reply);
            sq.Pick(sq.Players.First(r => r.Player == other.Player)); Settle();
            sq.Topics.Single(t => t.Label == "Make captain").Command.Execute(null); Settle();
            Assert.True(s.IsCaptain(other.Player));
            Assert.Contains("proud to be named captain", sq.Reply);
            window.Width = 1280; Settle();
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px Squad | {p}");
            Shot("run196-squad-1280.png");
            Assert.Empty(problems);
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
