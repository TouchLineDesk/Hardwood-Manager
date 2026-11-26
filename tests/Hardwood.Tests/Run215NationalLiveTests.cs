using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>I6b (run 215): your nation's qualifier on the live court: FIBA lines and the event floor, your nation as "us", no
/// studio desk, no club record; Continue keeps the score for the window. Text is checked for cuts at three sizes.</summary>
[Collection(SharedUiState.Name)]
public class Run215NationalLiveTests
{
    [AvaloniaFact]
    public void Your_nations_qualifier_plays_on_the_live_court()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false });
        var problems = new List<string>();
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SeedText = "31";
            start.PlayLeague = StartViewModel.CollegeChoice;
            start.SelectedTeam = start.Teams[1];
            start.NewGame();
            var s = vm.Session!;
            while (s.Today < new DateOnly(2026, 10, 16)) s.AdvanceDay();
            var job = s.NationalJobState;
            Assert.True(s.AcceptNationalOffer(job.Offers[0].Code));
            while (!(s.MyNationalGameToday != null && !s.ClubPlaysToday))
            {
                Assert.True(s.Today < new DateOnly(2026, 12, 15), "a national game on a day the club is idle");
                s.AdvanceDay();
            }
            vm.AfterTick();
            var recordBefore = s.MyRecord;
            var f = s.MyNationalGameToday!;
            var (game, played) = s.BeginLiveDay();
            var live = new LiveGameViewModel(vm, game!, played);
            vm.CurrentPage = live;
            Assert.True(live.IsNationalGame);
            Assert.False(live.IsCollegeGame);
            Assert.Equal(job.Code, live.Us.Abbreviation);
            Assert.Equal(CourtMarkings.Fiba, live.Markings);
            Assert.Contains("FIBA", live.FloorDress.LeftBaseline);
            Assert.All(live.OurPlayers, r => Assert.Contains(r.Player, live.Us.Roster));
            foreach (var (width, scale) in new[] { (1480, 1.0), (1280, 1.0), (1280, 1.3) })
            {
                window.Width = width; window.ApplyScale(scale);
                live.Drawer = -1; live.Refresh(); live.ShowBreakCard(final: false);
                Settle(window);
                foreach (var p in LayoutCheck.Scan(window)) problems.Add($"{width}px {scale:P0} national live | {p}");
                if (Environment.GetEnvironmentVariable("HW_LAYOUT_SHOTS") is { Length: > 0 } shots)
                {
                    Directory.CreateDirectory(shots);
                    live.HideBreakCommand.Execute(null); Settle(window);
                    using var frame = window.CaptureRenderedFrame();
                    frame?.Save(Path.Combine(shots, $"run215-national-{width}-{scale * 100:0}.png"));
                }
            }
            window.ApplyScale(1.0); window.Width = 1480;
            live.SimToEnd();
            Assert.False(live.DeskUp, "no studio desk for a national game");
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"final card | {p}");
            live.Continue();
            Assert.Equal((recordBefore.Wins, recordBefore.Losses), (s.MyRecord.Wins, s.MyRecord.Losses));
            Assert.Contains(job.Games, g => g.Home == f.Home && g.Away == f.Away && g.Live);
            Assert.Null(s.LiveNational);
        }
        finally
        {
            window?.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (report != null) File.AppendAllLines(report, problems);
        }
        Assert.True(problems.Count == 0, $"{problems.Count} pieces of text are cut off:\n" + string.Join("\n", problems.Take(60)));
    }

    private static void Settle(MainWindow window)
    {
        for (int i = 0; i < 3; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }
}
