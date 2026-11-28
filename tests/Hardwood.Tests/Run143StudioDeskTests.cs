using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 143, B4a (broadcast-presentation-design.md): the pre-game show on the HSN studio set, beat by beat, and the new
/// halftime desk (the half's story, the stat that decided it, each bench's adjustment, around college, the pick).
/// Presentation only: both read the show and the box score; nothing is drawn from the game's random numbers.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run143StudioDeskTests
{
    private static readonly string[] RealNetworks = { "ESPN", "CBS", "TNT", "FOX", "NBC", "ABC", "Big Ten Network", "SEC Network", "truTV", "TBS" };

    private static Team T(int id, string abbr) =>
        new() { Id = id, City = abbr == "MIA" ? "Miami" : "North Carolina", Name = abbr == "MIA" ? "Hurricanes" : "Tar Heels", Abbreviation = abbr, Division = "D", PrimaryColor = "#0033A0", SecondaryColor = "#FFFFFF" };

    private static PlayerLine L(int id, string last, int pts, int fgm, int fga, int tpm = 0, int tpa = 0, int ftm = 0, int fta = 0, int oreb = 0, int tov = 0) =>
        new() { Player = new Player { Id = id, FirstName = "A", LastName = last, PrimaryPosition = Position.SF }, Seconds = 600,
                Points = pts, FGM = fgm, FGA = fga, ThreePM = tpm, ThreePA = tpa, FTM = ftm, FTA = fta, OReb = oreb, Turnovers = tov };

    private static IEnumerable<string> Words(IEnumerable<DeskBeat> beats) => beats.SelectMany(b =>
        new[] { b.Kicker, b.Title, b.Note, b.Line?.Text ?? "", b.Line?.Speaker.Name ?? "" }.Concat(b.Rows.SelectMany(r => new[] { r.Label, r.Away, r.Home })));

    [Fact]
    public void The_stat_that_decided_the_half_points_the_leaders_way()
    {
        // Home lead 40-31 on threes (7 to 1) while shooting a little worse inside.
        var r = new GameResult { Home = new TeamBox { Team = T(1, "MIA") }, Away = new TeamBox { Team = T(2, "UNC") } };
        r.Home.Lines.Add(L(1, "Shooter", 25, 9, 22, tpm: 7, tpa: 12));
        r.Home.Lines.Add(L(2, "Big", 15, 6, 11, ftm: 3, fta: 4));
        r.Away.Lines.Add(L(3, "Guard", 19, 8, 17, tpm: 1, tpa: 6, ftm: 2, fta: 2));
        r.Away.Lines.Add(L(4, "Wing", 12, 5, 11, ftm: 2, fta: 2, tov: 2));
        Assert.Equal(40, r.Home.Points); Assert.Equal(31, r.Away.Points);
        var d = HalftimeDesk.Decided(r);
        Assert.Equal("three", d.Key);
        Assert.True(d.Worth > 0);
        var beats = HalftimeDesk.Build(r, 0.78);
        // The story names the score as the box has it, and the leading scorer.
        Assert.Equal("HALFTIME", beats[0].Kicker);
        Assert.StartsWith("Miami Hurricanes lead North Carolina Tar Heels 40-31 at the half", beats[0].Line!.Text);
        Assert.Equal("31", beats[0].Rows[0].Away); Assert.Equal("40", beats[0].Rows[0].Home);
        Assert.Equal("A Shooter", beats[0].Rows[1].Home);
        // The numbers beat marks the threes row and quotes the box.
        var numbers = beats.Single(b => b.Kicker == "BY THE NUMBERS");
        var key = numbers.Rows.Single(x => x.Key && x.Label != "HALFTIME");
        Assert.Equal("Threes", key.Label);
        Assert.Equal("7-12", key.Home); Assert.Equal("1-6", key.Away);
        Assert.Equal("13-28 (46%)", numbers.Rows.Single(x => x.Label == "Field goals").Away);
        // Each bench: the trailing side fixes the threes, the leading side keeps shooting them.
        var adj = beats.Where(b => b.Kicker == "ADJUSTMENTS").ToList();
        Assert.Equal(2, adj.Count);
        Assert.StartsWith("North Carolina Tar Heels: run shooters off the line", adj[0].Line!.Text);
        Assert.StartsWith("Miami Hurricanes: keep the floor spaced", adj[1].Line!.Text);
        // No around-college beat without scores; the pick goes with the win chance.
        Assert.DoesNotContain(beats, b => b.Kicker == "AROUND COLLEGE");
        var pick = beats.Last();
        Assert.Equal("SECOND HALF", pick.Kicker);
        Assert.StartsWith("MIA by", pick.Note);
        Assert.Contains("hold on", pick.Line!.Text);
        // Against the scoreboard when the engine likes the trailing side.
        Assert.Contains("come back", HalftimeDesk.Build(r, 0.4).Last().Line!.Text);
        // Around college shows when there are scores.
        Assert.Contains(HalftimeDesk.Build(r, 0.6, new[] { "Final: #3 Duke 70, Clemson 61" }), b => b.Kicker == "AROUND COLLEGE");
        foreach (var w in Words(beats)) foreach (var n in RealNetworks) Assert.DoesNotContain(n, w);
    }

    [Fact]
    public void A_level_half_still_names_a_stat_and_a_pick()
    {
        var r = new GameResult { Home = new TeamBox { Team = T(1, "MIA") }, Away = new TeamBox { Team = T(2, "UNC") } };
        r.Home.Lines.Add(L(1, "One", 30, 12, 25, tpm: 2, tpa: 8, ftm: 4, fta: 4, tov: 6));
        r.Away.Lines.Add(L(2, "Two", 30, 13, 28, tpm: 2, tpa: 9, ftm: 2, fta: 3, tov: 1));
        var beats = HalftimeDesk.Build(r, 0.5);
        Assert.Contains("All square at the break, 30-30", beats[0].Line!.Text);
        Assert.Contains("Coin flip", beats.Last().Line!.Text);
        Assert.Equal(2, beats.Count(b => b.Kicker == "ADJUSTMENTS"));
    }

    [AvaloniaFact]
    public void Pregame_and_halftime_air_on_the_studio_set()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-desk-{Guid.NewGuid():N}.save");
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
            start.SavePath = path; start.SeedText = "2026";
            start.PlayLeague = StartViewModel.CollegeChoice;
            start.SelectedTeam = start.Teams[1];
            start.NewGame();
            var session = vm.Session!;
            while (!session.HumanPlaysToday) session.AdvanceDay();
            vm.AfterTick();
            var (game, played) = session.BeginLiveDay();

            // The pre-game show: every line of today's show airs on the set, exactly once, and the tape is on the screen.
            var studio = new StudioShowViewModel(vm, game, played);
            vm.CurrentPage = studio;
            var show = studio.Show;
            var reel = studio.Reel.Beats;
            var spoken = reel.Where(b => b.Line != null).Select(b => b.Line!).ToList();
            foreach (var l in show.Segments.SelectMany(x => x.Lines).Concat(show.ImpactLines).Concat(show.RaceLines))
                Assert.Single(spoken, x => ReferenceEquals(x, l));
            foreach (var p in show.Picks) Assert.Single(spoken, x => x.Speaker == p.Analyst && x.Text == p.Why);
            foreach (var t in show.Tape) Assert.Contains(reel, b => b.Rows.Any(r => r.Label == t.Label && r.Away == t.Away && r.Home == t.Home));
            foreach (var w in Words(reel)) foreach (var n in RealNetworks) Assert.DoesNotContain(n, w);
            Assert.Equal("PREGAME", studio.Reel.Plate);
            // The panels below are unchanged: the cards still carry every segment.
            Assert.Equal(show.Segments.Count, studio.Cards.Count);
            Settle(window);
            Snap(window, "run143-pregame-open");
            var r = studio.Reel;
            r.Next(); r.Next();
            Assert.Equal(2, r.Index);
            int tape = reel.ToList().FindIndex(b => b.Kicker == "TALE OF THE TAPE");
            r.Index = tape;
            Settle(window);
            Snap(window, "run143-pregame-tape");
            Scan(window, problems, "pregame tape");
            foreach (var (width, scale) in new[] { (1480, 1.0), (1280, 1.3) })
            {
                window.Width = width; window.ApplyScale(scale);
                for (int i = 0; i < reel.Count; i++)
                {
                    r.Index = i;
                    Settle(window);
                    Scan(window, problems, $"{width}px {scale:P0} pregame beat {i} ({reel[i].Kicker})");
                }
            }
            window.ApplyScale(1.0); window.Width = 1480;
            r.SkipToEnd();
            Assert.False(r.CanNext);
            // Run 235: the picks are followed by the debate and the hot take on most nights.
            Assert.Contains(r.Kicker, new[] { "THE PICKS", "THE DEBATE", "HOT TAKE" });
            Settle(window);
            Snap(window, "run143-pregame-picks");
            Scan(window, problems, "pregame picks");
            r.Autorun();
            Assert.True(r.Autorunning); Assert.Equal(0, r.Index);
            r.Autorun();
            Assert.False(r.Autorunning);

            // Tip off, and play to halftime.
            studio.TipOff();
            var live = (LiveGameViewModel)vm.CurrentPage!;
            while (!(live.ShowBreak && live.BreakTitle == "Halftime") && !game!.Complete) live.StepOne();
            Assert.Equal("Halftime", live.BreakTitle);
            Assert.True(live.DeskUp, "the desk comes on at halftime");
            Assert.False(live.CardUp);
            var desk = live.HalftimeReel!;
            Assert.Equal("HALFTIME", desk.Plate);
            var box = game!.Result;
            Assert.Equal(box.Away.Points.ToString(), desk.Beats[0].Rows[0].Away);
            Assert.Equal(box.Home.Points.ToString(), desk.Beats[0].Rows[0].Home);
            var numbers = desk.Beats.Single(b => b.Kicker == "BY THE NUMBERS");
            Assert.Equal($"{box.Home.ThreePM}-{box.Home.ThreePA}", numbers.Rows.Single(x => x.Label == "Threes").Home);
            Assert.Equal(box.Away.Turnovers.ToString(), numbers.Rows.Single(x => x.Label == "Turnovers").Away);
            Assert.Equal(2, desk.Beats.Count(b => b.Kicker == "ADJUSTMENTS"));
            Assert.Equal("SECOND HALF", desk.Beats[^1].Kicker);
            foreach (var w in Words(desk.Beats)) foreach (var n in RealNetworks) Assert.DoesNotContain(n, w);
            foreach (var (width, scale) in new[] { (1480, 1.0), (1280, 1.0), (1280, 1.3) })
            {
                window.Width = width; window.ApplyScale(scale);
                for (int i = 0; i < desk.Beats.Count; i++)
                {
                    desk.Index = i;
                    Settle(window);
                    Scan(window, problems, $"{width}px {scale:P0} halftime beat {i}");
                }
                Snap(window, $"run143-halftime-{width}-{scale * 100:0}");
            }
            window.ApplyScale(1.0); window.Width = 1480;
            // The card (with the assistant's read) is one button away, and the desk comes back.
            live.ToCardCommand.Execute(null);
            Assert.True(live.CardUp); Assert.False(live.DeskUp);
            live.ToDeskCommand.Execute(null);
            Assert.True(live.DeskUp);
            live.HideBreakCommand.Execute(null);
            Assert.False(live.DeskUp); Assert.False(live.CardUp);
            // Play on: the desk goes with the break.
            live.StepOne();
            Assert.Null(live.HalftimeReel);
            while (!game.Complete) { live.StepOne(); Assert.False(live.DeskUp && live.BreakTitle != "Halftime" && !game.Complete); }
            session.FinishLiveDay(game, played);
        }
        finally
        {
            window?.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
            if (report != null) File.AppendAllLines(report, problems);
        }
        Assert.True(problems.Count == 0, $"{problems.Count} pieces of text are cut off:\n" + string.Join("\n", problems.Take(60)));
    }

    [AvaloniaFact]
    public void Skipping_the_shows_keeps_the_halftime_card()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-desk-{Guid.NewGuid():N}.save");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false, StudioShow = false });
        try
        {
            var vm = new MainViewModel();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "31";
            start.PlayLeague = StartViewModel.CollegeChoice;
            start.SelectedTeam = start.Teams[1];
            start.NewGame();
            var session = vm.Session!;
            while (!session.HumanPlaysToday) session.AdvanceDay();
            var (game, played) = session.BeginLiveDay();
            var live = new LiveGameViewModel(vm, game!, played);
            while (!(live.ShowBreak && live.BreakTitle == "Halftime") && !game!.Complete) live.StepOne();
            Assert.True(live.CardUp);
            Assert.False(live.DeskUp);
            Assert.Null(live.HalftimeReel);
            Assert.False(live.HasDesk);
        }
        finally
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void Scan(MainWindow window, List<string> problems, string where)
    {
        foreach (var p in LayoutCheck.Scan(window)) problems.Add($"{where} | {p}");
    }

    private static void Settle(MainWindow window)
    {
        for (int i = 0; i < 3; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static void Snap(MainWindow window, string name)
    {
        Settle(window);
        if (Environment.GetEnvironmentVariable("HW_LAYOUT_SHOTS") is not { Length: > 0 } shots) return;
        Directory.CreateDirectory(shots);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(shots, $"{name}.png"));
    }
}
