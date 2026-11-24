using Avalonia.Headless.XUnit;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 182: K25's leftover. A game simmed to the end gets its result call: the bar under FINAL said a first-half line
/// ("RUTLEDGE! He finishes at the rim. George Mason lead 39-37").
/// </summary>
[Collection(SharedUiState.Name)]
public class Run182SimToEndCallTests
{
    [AvaloniaFact]
    public void A_game_simmed_to_the_end_calls_the_result_once()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-182-{Guid.NewGuid():N}.save");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false, StudioShow = false });
        try
        {
            var vm = new MainViewModel();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "9";
            start.PlayLeague = StartViewModel.CollegeChoice;
            start.SelectedTeam = start.Teams[1];
            start.NewGame();
            var session = vm.Session!;
            while (!session.HumanPlaysToday) session.AdvanceDay();
            var (game, played) = session.BeginLiveDay();
            var live = new LiveGameViewModel(vm, game!, played);
            // A few possessions first, so the bar has said something from the game.
            for (int i = 0; i < 12; i++) live.StepOne();
            live.SimToEnd();
            Assert.True(live.Complete);
            Assert.True(live.CommentaryBig);
            Assert.Contains($"Final: ", live.Commentary);
            Assert.Contains($"{Math.Max(live.HomeScore, live.AwayScore)}", live.Commentary);
            Assert.Contains(live.SaidForTest, l => l.Text == live.Commentary);
            Assert.Single(live.Log.Where(l => l.EndsWith(live.Commentary, StringComparison.Ordinal)));
        }
        finally
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
