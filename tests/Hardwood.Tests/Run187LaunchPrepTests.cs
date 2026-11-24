using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 187 (release-candidate prep, no engine or balance change):
/// - K29's leftover: the pregame show's opening screen, which every game starts with, fills the wall (form, strength,
///   injuries and each side's style), not three rows on an empty screen.
/// - The desk's speech bubble no longer sits over the host's arm (it's narrower and further out on his side).
/// - Run 185's review, Low 2: the summer's "awaiting a decision" lists come back from a save in the order they were saved.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run187LaunchPrepTests
{
    private readonly ITestOutputHelper _out;
    public Run187LaunchPrepTests(ITestOutputHelper o) => _out = o;

    [AvaloniaFact]
    public void The_opening_screen_fills_the_wall_and_the_hosts_bubble_clears_his_arm()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run187-{Guid.NewGuid():N}.save");
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
            start.SavePath = path; start.SeedText = "2026";
            start.PlayLeague = StartViewModel.CollegeChoice;
            start.SelectedTeam = start.Teams[1];
            start.NewGame();
            var s = vm.Session!;
            s.Delegated.Recruiting = true;

            // Opening night: no form yet, so the wall carries strength, injuries and each side's style.
            while (!s.HumanPlaysToday) s.AdvanceDay();
            vm.AfterTick();
            var (game, played) = s.BeginLiveDay();
            var studio = new StudioShowViewModel(vm, game, played);
            vm.CurrentPage = studio;
            var open = studio.Reel.Beats[0];
            _out.WriteLine($"{s.Today:MMM d}: " + string.Join(" | ", open.Rows.Select(r => $"{r.Label}={r.Away}/{r.Home}")));
            Assert.Equal("THE OPEN", open.Kicker);
            Assert.True(open.Rows.Count >= HalftimeDesk.MinRows, $"the opening screen has {open.Rows.Count} rows");
            Assert.True(open.Rows.Count <= HalftimeDesk.MaxRows);
            Assert.Contains(open.Rows, r => r.Label == "Record" && r.Away == "0-0" && r.Home == "0-0");
            Assert.Contains(open.Rows, r => r.Label == "Win chance");
            Assert.Equal(open.Rows.Count, open.Rows.Select(r => r.Label).Distinct().Count());

            // The host speaks first: his bubble is the narrow one, and it ends well short of his head.
            for (int i = 0; i < 4; i++) { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            var desk = window.GetVisualDescendants().OfType<StudioDeskView>().First();
            var bubble = desk.FindControl<Border>("Bubble")!;
            Assert.True(studio.Reel.BubbleLeft);
            Assert.Equal(StudioDeskView.HostBubbleWidth, bubble.Width);
            double right = Canvas.GetLeft(bubble) + bubble.Width;
            _out.WriteLine($"host bubble: left {Canvas.GetLeft(bubble):0}, right {right:0}");
            Assert.True(right <= 1440 / 2.0 - 100, $"the host's bubble ends at {right:0}");

            // A few weeks on, the screen is still full and every row has both sides.
            studio.TipOff();
            var guard = 0;
            while (s.Today < new DateOnly(s.Today.Year, 12, 1) && guard++ < 60) s.AdvanceDay();
            while (!s.HumanPlaysToday && guard++ < 90) s.AdvanceDay();
            vm.AfterTick();
            var (g2, p2) = s.BeginLiveDay();
            var later = new StudioShowViewModel(vm, g2, p2).Reel.Beats[0];
            _out.WriteLine($"{s.Today:MMM d}: " + string.Join(" | ", later.Rows.Select(r => $"{r.Label}={r.Away}/{r.Home}")));
            Assert.DoesNotContain(later.Rows, r => r.Away == "" && r.Label != "");
            Assert.True(later.Rows.Count >= HalftimeDesk.MinRows);
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }

    [Fact]
    public void Awaiting_decision_lists_come_back_in_the_order_saved()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run187-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        using (var src = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SaveFixtures", CollegeSaveCompatibilityTests.FixtureName)), CompressionMode.Decompress))
        using (var dst = File.Create(path)) src.CopyTo(dst);
        try
        {
            var a = GameSession.Load(path);
            var club = a.World.Clubs.First(c => c.Roster.Count >= 4);
            // Not id order: the live list keeps the order players were added in.
            var waiting = club.Roster.OrderByDescending(p => p.Id).Take(4).ToList();
            a.World.AwaitingDecision[club.Id] = waiting.ToList();
            a.SaveNow();
            SqliteConnection.ClearAllPools();
            var b = GameSession.Load(path);
            Assert.Equal(waiting.Select(p => p.Id), b.World.AwaitingDecision[club.Id].Select(p => p.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
