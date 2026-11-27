using Avalonia.Headless;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>W2 (run 225, `claude/world-stories-plan.md`): the yearly almanac. Each summer a season's edition is built from the
/// saved history (champions, honours, your season, the columnists' calls, records, the Hall of Fame or the carousel), posted
/// as one message, and every edition stays on the History page's Almanac tab. Nothing new is saved.</summary>
public class Run225AlmanacTests
{
    private readonly ITestOutputHelper _out;
    public Run225AlmanacTests(ITestOutputHelper output) => _out = output;

    private static int BenchClub(ulong seed)
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026);
        return probe.Teams.OrderBy(t => QuickSim.Strength(t)).ElementAt(14).Id;
    }

    private static string Dump(AlmanacEdition e) =>
        $"{e.Title}: {e.Cover}\n" + string.Join("\n", e.Sections.Select(x => $"[{x.Title}]\n  " + string.Join("\n  ", x.Lines)));

    [Fact]
    public void A_pro_summer_brings_the_almanac_and_a_reload_keeps_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run225-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = GameSession.NewGame(2026, BenchClub(2026), savePath: null);
            s.Job.Current!.TargetWins = 0;
            Assert.Empty(Almanac.Editions(s));
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 600) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            var e = Assert.Single(Almanac.Editions(s));
            _out.WriteLine(Dump(e));
            Assert.Equal("2026-27", e.Label);
            Assert.Equal(s.League.History[0].ChampionTeamId, e.ChampionId);
            Assert.EndsWith("are champions", e.Cover);
            Assert.Contains("won the", e.Section(Almanac.Stories)!.Lines[0]);
            Assert.Contains(e.Section(Almanac.Honours)!.Lines, l => l.StartsWith("MVP: "));
            var yours = Assert.Single(e.Section(Almanac.Yours)!.Lines);
            var rec = s.League.History[0].Records.First(r => r.teamId == s.HumanTeam.Id);
            Assert.Contains($"{rec.wins}-{rec.losses}", yours);
            var calls = e.Section(Almanac.Calls)!.Lines;
            Assert.Equal(3, calls.Count(l => l.Contains(" picked ")));
            Assert.Contains(calls, l => l.StartsWith("Best call: "));
            Assert.Contains(e.Section(Almanac.Abroad)!.Lines, l => l.StartsWith("EuroLeague: ") || l.StartsWith("Euro Elite League: "));

            var msg = Assert.Single(s.Inbox, m => m.Title == "The 2026-27 Almanac is out");
            _out.WriteLine($"\n{msg.Title}\n{msg.Body}");
            Assert.StartsWith(e.Cover + ".", msg.Body);
            Assert.Contains("History page's Almanac tab", msg.Body);

            // Built from the saved history: a reload has the same edition, and the summer doesn't post it twice.
            s.Save = new Hardwood.Core.Persistence.SaveFile(path);
            s.SaveNow();
            var back = GameSession.Load(path);
            Assert.Equal(Dump(e), Dump(Assert.Single(Almanac.Editions(back))));
            back.AdvanceOffseason();
            Assert.Single(back.Inbox, m => m.Title == msg.Title);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

/// <summary>W2 on screen: a college career's first summer brings its edition, and History's Almanac tab shows it.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run225AlmanacUiTests
{
    private readonly ITestOutputHelper _out;
    public Run225AlmanacUiTests(ITestOutputHelper output) => _out = output;

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_college_summer_brings_the_almanac_to_history()
    {
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
            var me = world.College!.Programs.First(t => t.FullName.StartsWith("George Mason"));
            var s = GameSession.NewGame(9, me.Id, savePath: null);
            s.Job.Current!.TargetWins = 0;
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            Assert.True(s.Employed);
            var cw = s.World.College!;
            var e = Assert.Single(Almanac.Editions(s));
            _out.WriteLine(Almanac.MessageBody(e) + "\n\n" + string.Join("\n", e.Sections.Select(x => $"[{x.Title}] " + string.Join(" | ", x.Lines))));
            Assert.Equal(cw.History[0].ChampionId, e.ChampionId);
            Assert.EndsWith("national champions", e.Cover);
            Assert.Contains(e.Section(Almanac.Honours)!.Lines, l => l.StartsWith("National Player of the Year: "));
            Assert.StartsWith("George Mason", Assert.Single(e.Section(Almanac.Yours)!.Lines));
            Assert.Equal(3, e.Section(Almanac.Calls)!.Lines.Count(l => l.Contains(" picked ")));
            Assert.Single(s.Inbox, m => m.Title == $"The {e.Label} Almanac is out");

            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
            window.Show();
            vm.AttachSession(s);
            var w = window;
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }
            vm.Navigate("History"); Settle();
            var hist = (HistoryViewModel)vm.CurrentPage!;
            hist.Tab = 5; Settle();
            Assert.Equal(5, hist.Tab);
            Assert.True(hist.HasAlmanacs);
            Assert.Equal(e.Label, hist.SelectedAlmanac!.Label);
            Assert.NotNull(hist.SelectedAlmanac.Champion);
            hist.Refresh(); Settle();
            Assert.Equal(5, hist.Tab); // the Almanac tab survives a refresh in a college career
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                using var frame = w.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run225-almanac-college.png"));
            }
        }
        finally
        {
            window?.Close();
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
        }
    }
}
