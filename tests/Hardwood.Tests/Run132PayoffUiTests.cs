using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Hardwood.Core.College;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 132 (CP3 and the college Lows that rode along) on screen: day one of a college career shows ranges for college
/// potential, an empty Your pros tab that explains itself, the conference table by overall record before conference play
/// (H134), and "a blue blood" only for the top handful by prestige (H136).
/// </summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run132PayoffUiTests
{
    [Fact]
    public void Blue_blood_is_for_the_top_handful()
    {
        Assert.Equal("a blue blood", BoardViewModel.PrestigeWords(90, 1));
        Assert.Equal("a blue blood", BoardViewModel.PrestigeWords(80, BoardViewModel.BlueBloods));
        Assert.Equal("a power program", BoardViewModel.PrestigeWords(78, 50));
        Assert.Equal("a known name", BoardViewModel.PrestigeWords(73, 60));
        Assert.Equal("building a name", BoardViewModel.PrestigeWords(40, 200));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Day_one_of_a_college_career_shows_ranges_an_empty_tracker_and_overall_records()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run132-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
            var cw = world.College!;
            int id = cw.Programs.First(p => p.FullName.StartsWith("Miami (FL)")).Id;
            var s = GameSession.NewGame(2026, id, path);
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            var w = window;
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

            // H134: no conference game yet, so the table is by overall record with no tournament shading.
            vm.Navigate("Standings"); Settle();
            var st = (StandingsViewModel)vm.CurrentPage!;
            Assert.Contains("overall records until conference play", st.LeftTitle);
            Assert.All(st.East, r => Assert.NotEqual("Playoffs", r.Zone));

            // CP3: potential is a range for college players and recruits on the College page.
            vm.Navigate("College"); Settle();
            var col = (CollegeViewModel)vm.CurrentPage!;
            col.Age = "All"; Settle();
            Assert.NotEmpty(col.Prospects);
            Assert.All(col.Prospects, r => Assert.Contains("-", r.PA));
            // The alumni tracker: before anyone has graduated, only Miami's real NBA alumni (K12b, run 137), marked Earlier.
            col.Tab = 8; Settle();
            Assert.True(col.HasAlumni);
            Assert.All(col.Alumni, r => Assert.Equal("Earlier", r.Class));
            Assert.Contains("Rows marked Earlier are real NBA players", col.AlumniNote);

            // The player page's chip and card foot show the range.
            var mine = s.HumanTeam.Roster[0];
            vm.ShowPlayer(mine); Settle();
            var page = (PlayerViewModel)vm.CurrentPage!;
            Assert.Contains("-", page.Chips.First(c => c.Label == "Potential").Value);
            Assert.StartsWith("POT ", page.CardFooter);
            // A pro keeps his exact number (#119).
            vm.ShowPlayer(s.League.Teams[0].Roster[0]); Settle();
            Assert.Null(((PlayerViewModel)vm.CurrentPage!).CardFooter);
        }
        finally
        {
            window?.Close();
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            try { File.Delete(path); } catch { }
        }
    }
}
