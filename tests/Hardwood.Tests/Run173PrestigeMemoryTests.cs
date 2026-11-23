using Hardwood.Core.College;
using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 173, CS7 part 2b (#166, "prestige with a long memory: one great season doesn't make a blue blood"). Each summer a
/// program's season mark (the record against .500, March wins, the title) joins its last four, and prestige moves by the
/// five-season average, so one great season lifts it a fifth as far, five summers running. The Board's Program building
/// panel says so, with the marks.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run173PrestigeMemoryTests
{
    private readonly ITestOutputHelper _out;
    public Run173PrestigeMemoryTests(ITestOutputHelper o) => _out = o;

    private static GameSession Career(string name, ulong seed, string? path = null)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith(name)).Id;
        return GameSession.NewGame(seed, id, path);
    }

    [Fact]
    public void One_great_season_counts_a_fifth_for_five_summers()
    {
        // A 30-5 Final Four run that wins four March games: the old rule moved prestige by all of it in one summer.
        double mark = CollegeWorld.SeasonMark(30, 5, 4, champion: false);
        Assert.Equal((30 / 35.0 - 0.5) * 10 + 6, mark, 9);
        var cw = new CollegeWorld();
        cw.Form[1] = new List<double> { mark };
        Assert.Equal(mark / CollegeWorld.PrestigeMemory, cw.FormOf(1), 9);
        // Five steady seasons move it by the mark itself: a program that keeps winning climbs as fast as before.
        cw.Form[2] = Enumerable.Repeat(2.0, CollegeWorld.PrestigeMemory).ToList();
        Assert.Equal(2.0, cw.FormOf(2), 9);
        Assert.Equal(0, cw.FormOf(3)); // no memory yet (a fresh world, an older save)
        // The save text round-trips exactly, and an older save's missing text reads as no memory.
        var marks = new List<double> { mark, -1.25, 1.0 / 3 };
        Assert.Equal(marks, WorldStore.ParseForm(WorldStore.FormText(marks)));
        Assert.Empty(WorldStore.ParseForm(null));
        Assert.Null(WorldStore.FormText(new List<double>()));
    }

    [Fact]
    public void A_summer_records_every_programs_mark_moves_prestige_by_the_form_and_survives_a_reload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hw-run173-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var s = Career("Providence", 77, path);
            var c = s.World.College!;
            Assert.Empty(c.Form);
            Dictionary<int, double>? before = null; Dictionary<int, double>? marks = null;
            int guard = 0;
            // Stop the day before the summer (the boosters' message is posted at it) and read each program's season.
            while (guard++ < 420)
            {
                if (c.Season.ChampionId != null && before == null)
                {
                    before = c.Prestige.ToDictionary(kv => kv.Key, kv => kv.Value);
                    marks = c.Programs.ToDictionary(t => t.Id, t =>
                    {
                        var (w, l) = c.Record(t.Id);
                        int runs = c.Season.Games.Count(g => g.Event == CollegeWorld.Madness && g.Round >= 1 && g.WinnerId == t.Id);
                        return CollegeWorld.SeasonMark(w, l, runs, c.Season.ChampionId == t.Id);
                    });
                }
                s.AdvanceDay();
                if (c.Form.Count > 0) break;
            }
            Assert.NotNull(before);
            Assert.Equal(c.Programs.Count, c.Form.Count);
            int checkedPrograms = 0;
            foreach (var t in c.Programs)
            {
                var m = Assert.Single(c.Form[t.Id]);
                Assert.Equal(marks![t.Id], m, 9);
                // The move: a fifth of the mark (scaled near the top when it's a gain), then the drift toward the baseline.
                var conf = CollegeWorld.Conferences.First(x => x.Key == c.ConferenceOf[t.Id]);
                double change = m / CollegeWorld.PrestigeMemory, was = before![t.Id];
                if (change > 0) change *= Math.Clamp((100 - was) / 30.0, 0, 1);
                double expect = Math.Clamp(was + change + (c.Baseline(t, conf) - was) * 0.08, 10, 99);
                Assert.Equal(expect, c.Prestige[t.Id], 6);
                checkedPrograms++;
            }
            var champ = c.Program(c.History[^1].ChampionId) ?? c.Programs.OrderByDescending(t => c.Form[t.Id][0]).First();
            _out.WriteLine($"{checkedPrograms} programs. Champion {champ.FullName}: mark {c.Form[champ.Id][0]:0.0}, prestige {before![champ.Id]:0.0} → {c.Prestige[champ.Id]:0.0}");
            _out.WriteLine(BoardViewModel.PrestigeMemoryLine(c, s.HumanTeam.Id));
            Assert.Contains("newest first", BoardViewModel.PrestigeMemoryLine(c, s.HumanTeam.Id));

            s.SaveNow();
            var back = GameSession.Load(path).World.College!;
            foreach (var t in c.Programs) Assert.Equal(c.Form[t.Id], back.Form[t.Id]);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [AvaloniaFact]
    public void The_board_says_how_prestige_remembers()
    {
        var oldSettings = Hardwood.Desktop.AppSettings.Current; var oldPath = Hardwood.Desktop.AppSettings.FilePath;
        Hardwood.Desktop.AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        Hardwood.Desktop.AppSettings.Use(new Hardwood.Desktop.AppSettings { AutoPortraits = false });
        Hardwood.Desktop.Views.MainWindow? window = null;
        try
        {
            var s = Career("Providence", 77);
            var c = s.World.College!;
            var vm = new MainViewModel();
            window = new Hardwood.Desktop.Views.MainWindow { DataContext = vm, Width = 1280, Height = 800 };
            window.Show();
            vm.AttachSession(s);
            vm.Navigate("Board");
            var board = Assert.IsType<BoardViewModel>(vm.CurrentPage);
            void Settle() { for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window!.UpdateLayout(); } }
            Settle();
            _out.WriteLine(board.ProgramPrestige);
            Assert.StartsWith("Prestige remembers 5 seasons: each summer", board.ProgramPrestige);
            // Three remembered seasons, as after three summers.
            c.Form[s.HumanTeam.Id] = new List<double> { 6.2, -1.5, 0.8 };
            vm.Navigate("Home"); vm.Navigate("Board");
            board = Assert.IsType<BoardViewModel>(vm.CurrentPage);
            Settle();
            _out.WriteLine(board.ProgramPrestige);
            Assert.Contains("together they move it +1.1 a summer (marks +0.8, −1.5, +6.2, newest first)", board.ProgramPrestige);
            Assert.Empty(LayoutCheck.Scan(window));
            var title = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.TextBlock>().First(x => x.Text == "Program building");
            var scroll = Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<Avalonia.Controls.ScrollViewer>(title)!;
            if (title.TranslatePoint(new Avalonia.Point(0, 0), scroll) is { } at) scroll.Offset = new Avalonia.Vector(0, scroll.Offset.Y + at.Y - 40);
            Settle();
            if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run173-prestige-memory.png"));
            }
        }
        finally
        {
            window?.Close();
            Hardwood.Desktop.AppSettings.Use(oldSettings); Hardwood.Desktop.AppSettings.FilePath = oldPath;
        }
    }
}
