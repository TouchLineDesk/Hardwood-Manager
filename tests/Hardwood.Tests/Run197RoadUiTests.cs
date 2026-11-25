using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.College;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 197 on screen: the Recruiting tab's "On the road" panel (four events, a pick for each still to come).</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run197RoadUiTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_Recruiting_tab_shows_the_road_calendar_and_takes_a_pick()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run197ui-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
            int id = world.College!.Programs.First(p => p.FullName.StartsWith("George Mason")).Id;
            var s = GameSession.NewGame(9, id, path);

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
            CollegeViewModel Open()
            {
                vm.Navigate("Home"); vm.Navigate("College"); Settle();
                var col = (CollegeViewModel)vm.CurrentPage!;
                col.Tab = 5; Settle();
                return col;
            }

            var college = Open();
            Assert.True(college.HasRoad);
            Assert.Equal(4, college.RoadTiles.Count);
            Assert.Contains("the Class of 2027", college.RoadHeader);
            var visit = college.RoadTiles.Single(t => t.Row.Event == RoadEvent.VisitWeekend);
            Assert.True(visit.Open);
            Assert.All(college.RoadTiles.Take(3), t => Assert.True(t.Done));
            var panel = w.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "RoadPanel");
            Assert.True(panel.IsEffectivelyVisible && panel.Bounds.Height > 40);

            var problems = new List<string>();
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Recruiting | {p}");
            visit.Choice = CollegeWorld.BringLikely; Settle();
            Assert.Contains("likely yeses", college.RoadReply);
            Assert.Equal(CollegeWorld.BringLikely, s.RoadCalendar().Single(r => r.Event == RoadEvent.VisitWeekend).Choice);
            Shot("run197-road-1480.png");

            window.Width = 1280; Settle();
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px Recruiting | {p}");
            Shot("run197-road-1280.png");

            // After the weekend: every tile reads done, with what happened.
            int guard = 0;
            while (s.Today <= CollegeWorld.RoadDay(RoadEvent.VisitWeekend, 2027) && guard++ < 30) s.AdvanceDay();
            college = Open();
            Assert.All(college.RoadTiles, t => Assert.True(t.Done));
            Assert.Contains("all done", college.RoadHeader);
            Assert.Equal("Done · your pick", college.RoadTiles[3].State);
            Assert.NotEmpty(college.RoadTiles[3].Result);
            Assert.Equal("Passed", college.RoadTiles[0].State);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px Recruiting done | {p}");
            Shot("run197-road-done-1280.png");
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
