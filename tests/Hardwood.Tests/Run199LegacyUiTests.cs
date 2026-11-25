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

/// <summary>Run 199 on screen: the Team page's Legacy tab (the rafters, the Hall of Fame and the record book), empty and full.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run199LegacyUiTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_Legacy_tab_shows_the_rafters_the_hall_and_the_records_without_layout_problems()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run199ui-{Guid.NewGuid():N}.hwsave");
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
            TeamViewModel Open()
            {
                vm.Navigate("Home"); vm.ShowTeam(s.HumanTeam); Settle();
                var tabs = w.GetVisualDescendants().OfType<TabControl>().Single(t => t.Name == "TeamTabs");
                tabs.SelectedIndex = tabs.Items.Count - 1; Settle();
                return (TeamViewModel)vm.CurrentPage!;
            }
            var problems = new List<string>();

            // Empty: the first day says when the book opens.
            var page = Open();
            Assert.True(page.HasLegacy);
            Assert.False(page.HasHall);
            Assert.Contains("opens the night of this career's first national final", page.LegacyNote);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px empty | {p}");
            Shot("run199-legacy-empty-1480.png");

            // Full: three springs of a book, with two jerseys in the rafters.
            var col = s.World.College!;
            var book = new ProgramLegacy { TeamId = id, FirstYear = 2026, LastYear = 2029 };
            string[] names = { "Marcus Whitfield-Oyelaran", "Dee Carter", "Jalen Brooks", "Tomas Reyes", "Andre Kowalczyk-Bennett", "Isaiah Lund" };
            for (int i = 0; i < names.Length; i++)
                book.Careers.Add(new LegacyCareer { PlayerId = 900000 + i, Name = names[i], Pos = "SG", First = 2026, Last = 2029, G = 128 - i * 6, Pts = 2210 - i * 230, Reb = 980 - i * 90, Ast = 610 - i * 70, Done = i < 4 });
            for (int i = 0; i < 5; i++)
                book.Seasons.Add(new LegacySeason { Year = 2026 + i % 3, PlayerId = 900000 + i, Name = names[i], G = 34, Pts = 790 - i * 40, Reb = 330 - i * 20, Ast = 210 - i * 15 });
            book.Hall.Add(new LegacyInductee { PlayerId = 900000, Name = names[0], Pos = "SG", Class = 2030, First = 2026, Last = 2029, G = 128, Pts = 2210, Reb = 980, Ast = 610, Score = 23.5, Jersey = 23,
                Why = new() { "National Player of the Year 2029-30", "All-America first team 2029-30", "All-America second team 2028-29", "A-10 Player of the Year 2029-30", "2,210 career points" } });
            book.Hall.Add(new LegacyInductee { PlayerId = 900001, Name = names[1], Pos = "PF", Class = 2029, First = 2025, Last = 2028, G = 122, Pts = 1980, Reb = 890, Ast = 540, Score = 15, Jersey = 4,
                Why = new() { "Most Outstanding Player 2028-29", "National champion 2028-29", "1,980 career points" } });
            book.Hall.Add(new LegacyInductee { PlayerId = 900002, Name = names[2], Pos = "PG", Class = 2028, First = 2026, Last = 2027, G = 66, Pts = 1100, Reb = 300, Ast = 420, Score = 8,
                Why = new() { "A-10 Player of the Year 2027-28", "All-A-10 first team 2026-27", "1,100 career points" } });
            col.Legacy.Add(book);
            // The coached program's ceremony message (the night of the final).
            var cls = book.Hall.Where(h => h.Class == 2030).ToList();
            Assert.Equal($"Into the rafters: No. 23, {names[0]}", s.LegacyTitle(cls));
            Assert.Contains("No. 23 is retired", s.LegacyBody(cls));
            Assert.Contains("under Legacy", s.LegacyBody(cls));
            page = Open();
            Assert.Equal(2, page.Rafters.Count);
            Assert.Equal(3, page.Hall.Count);
            Assert.Equal("Class of 2030", page.Hall[0].Class);
            Assert.Equal(6, page.LegacyTables.Count); // no "Best seasons" before a season's honours
            Assert.Contains("still playing", page.LegacyTables[0].Rows[4].When);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px full | {p}");
            Shot("run199-legacy-1480.png");
            window.Width = 1280; Settle();
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px full | {p}");
            Shot("run199-legacy-1280.png");
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
