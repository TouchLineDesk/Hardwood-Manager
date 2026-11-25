using Avalonia.Headless;
using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 195 (CS11) on screen: the two All-American MVPs on a college coach's Awards page, and the honour on the player page.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run195AllAmericanUiTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_Awards_page_shows_both_All_American_MVPs_and_the_player_page_the_honour()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run195-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
            int id = world.College!.Programs.First(p => p.FullName.StartsWith("George Mason")).Id;
            var s = GameSession.NewGame(2026, id, path);
            var cw = s.World.College!;
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

            // The world on its own, through the Classic (March 31), the seniors' game (April 3) and the title (April 6).
            var sim = new GameSimulator(); var rng = new Rng(195);
            cw.PlayThrough(new DateOnly(2027, 3, 31), sim, rng);
            cw.PlayThrough(new DateOnly(2027, 4, 6), sim, rng);
            cw.PlayThrough(new DateOnly(2027, 4, 30), sim, rng);
            var sr = cw.AllAmericanOf(CollegeWorld.SeniorGame, 2026)!;
            var hs = cw.AllAmericanOf(CollegeWorld.HighSchoolGame, 2026)!;
            Assert.True(sr.Played); Assert.True(hs.Played);

            vm.Navigate("Awards"); Settle();
            var aw = (AwardsViewModel)vm.CurrentPage!;
            aw.Refresh(); Settle();
            var awards = aw.Winners.Select(x => x.Award).ToList();
            Assert.Equal(new[] { "National Player of the Year", "Defensive Player of the Year", "Freshman of the Year", "Most Outstanding Player", "Coach of the Year",
                                 "College All-American Game MVP", "Hardwood All-American Classic MVP" }, awards);
            var srCard = aw.Winners[5]; var hsCard = aw.Winners[6];
            Assert.Equal(sr.NameOf(sr.MvpId!.Value), srCard.Name);
            Assert.Contains(sr.ScoreLine, srCard.StatLine);
            Assert.Equal(hs.NameOf(hs.MvpId!.Value), hsCard.Name);
            Assert.StartsWith(hs.SchoolOf[hs.MvpId.Value], hsCard.TeamLine);
            Assert.NotNull(hsCard.Player); // a high-schooler still has his face on the card
            var problems = new List<string>();
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Awards | {p}");
            Shot("run195-awards-1480.png");
            window.Width = 1280; Settle();
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1280px Awards | {p}");
            Shot("run195-awards-1280.png");
            window.Width = 1480; Settle();

            // The seniors' MVP's page lists the game among his honours.
            var mvp = cw.Players.FirstOrDefault(p => p.Id == sr.MvpId.Value);
            if (mvp != null)
            {
                Assert.Contains("College All-American Game MVP 2026-27", cw.HonoursOf(mvp.Id));
                vm.ShowPlayer(mvp); Settle();
                var page = (PlayerViewModel)vm.CurrentPage!;
                Assert.Contains("College All-American Game MVP 2026-27", page.Honours);
                Shot("run195-player.png");
            }
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
