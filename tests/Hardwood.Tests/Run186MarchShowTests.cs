using Avalonia.Headless.XUnit;
using Hardwood.Core.College;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 186 (critic review #23):
/// - K31: in March Madness the studio names the round and the seeds, and a neutral floor reads "V", never "AT" or "visit".
/// - K33: the top bar keeps one row in March ("ACC · March Madness · First Four", title case) at 1480 px (the critic's case; at 1280 the bar stacks on every page by design).
/// </summary>
[Collection(SharedUiState.Name)]
public class Run186MarchShowTests
{
    private readonly ITestOutputHelper _out;
    public Run186MarchShowTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void The_college_stage_words_title_case_march_only()
    {
        Assert.Equal("March Madness · First Four", MainViewModel.CollegeStageWords("March Madness: First Four"));
        Assert.Equal("conference tournament", MainViewModel.CollegeStageWords("Conference tournament"));
    }

    [AvaloniaFact]
    public void In_March_the_show_names_the_round_and_the_bar_stays_on_one_row()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run186-{Guid.NewGuid():N}.save");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "31";
            start.PlayLeague = "College basketball";
            start.SelectedTeam = start.Teams.First(t => t.Team.City == "Duke");
            start.NewGame();
            var s = vm.Session!;
            s.Delegated.Recruiting = true;
            var cw = s.World.College!;
            // A regular-season game is not on a neutral floor unless it's an event.
            int guard = 0;
            while (!cw.StageText.StartsWith(CollegeWorld.Madness) && guard++ < 220) s.AdvanceDay();
            Assert.StartsWith(CollegeWorld.Madness, cw.StageText);
            vm.AfterTick();
            _out.WriteLine($"{s.Today:MMM d yyyy}: {vm.PhaseText} | {vm.NextGameText}");
            Assert.Contains("March Madness · ", vm.PhaseText);
            Assert.DoesNotContain("march madness", vm.PhaseText);
            foreach (var width in new[] { 1480 })   // 1280 stacks on every page (the right half alone is ~880 px): the gate allows it
            {
                window.Width = width;
                window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.False(window.TopBarStacked, $"the top bar wrapped onto two rows at {width} px: {window.TopBarWidths}");
            }

            // K31: the March game's show (if this program made the field; seed 31's Duke is in the First Four).
            CollegeGame? g = null;
            guard = 0;
            while ((g = cw.Season.Games.Where(x => !x.Played && x.Event == CollegeWorld.Madness && (x.HomeId == s.HumanTeam.Id || x.AwayId == s.HumanTeam.Id))
                       .OrderBy(x => x.Date).FirstOrDefault()) == null && guard++ < 3) s.AdvanceDay();
            if (g == null) { _out.WriteLine($"{s.HumanTeam.FullName} isn't in the field: the show part is skipped"); return; }
            var home = s.FindTeam(g.HomeId)!; var away = s.FindTeam(g.AwayId)!;
            var show = s.BuildStudioShow(home, away, g.Date);
            var open = show.Segments[0].Lines[0].Text;
            _out.WriteLine($"{show.ShowName} · {show.SetLine} | {show.Versus} | {open}");
            Assert.Equal("HSN March Madness", show.ShowName);
            Assert.StartsWith(CollegeWorld.RoundName(g.Round) + " · No. ", show.SetLine);
            Assert.Contains(" v No. ", show.SetLine);
            Assert.True(show.Neutral);
            Assert.Equal("V", show.Versus);
            Assert.StartsWith("Welcome to March Madness on HSN", open);
            Assert.DoesNotContain(" visit ", open);
            Assert.DoesNotContain("night in March", show.SetLine);
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
