using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 145: B4b, the postgame show on the HSN studio set (the final, the player of the game, how it was won, the turning
/// point, what it means, around college; no interviews), and K17: a stored college box score keeps its shot zones,
/// possessions, period lengths and score timeline, so a reloaded game shows rim points, possessions and its Game flow panel.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run145PostgameTests
{
    private static readonly string[] RealNetworks = { "ESPN", "CBS", "TNT", "FOX", "NBC", "ABC", "Big Ten Network", "SEC Network", "truTV", "TBS" };

    private static Team T(int id, string abbr) =>
        new() { Id = id, City = abbr == "MIA" ? "Miami" : "North Carolina", Name = abbr == "MIA" ? "Hurricanes" : "Tar Heels", Abbreviation = abbr, Division = "D", PrimaryColor = "#0033A0", SecondaryColor = "#FFFFFF" };

    private static PlayerLine L(int id, string last, int pts, int fgm, int fga, int tpm = 0, int tpa = 0, int ftm = 0, int fta = 0, int oreb = 0, int dreb = 0, int ast = 0,
                                int tov = 0, int rim = 0, bool started = true)
    {
        var l = new PlayerLine { Player = new Player { Id = id, FirstName = "A", LastName = last, PrimaryPosition = Position.SF }, Seconds = 1500, Started = started,
            Points = pts, FGM = fgm, FGA = fga, ThreePM = tpm, ThreePA = tpa, FTM = ftm, FTA = fta, OReb = oreb, DReb = dreb, Assists = ast, Turnovers = tov };
        l.ZoneMakes[0] = rim; l.ZoneAttempts[0] = rim + 2;
        l.ZoneMakes[4] = tpm; l.ZoneAttempts[4] = tpa;
        return l;
    }

    private static GameResult Game()
    {
        // Miami came back from 12 down at home: 74-66, with a 14-2 run in the second half.
        var r = new GameResult { Home = new TeamBox { Team = T(1, "MIA"), Possessions = 68 }, Away = new TeamBox { Team = T(2, "UNC"), Possessions = 67 },
            Periods = 2, PeriodSeconds = 1200 };
        r.Home.Lines.Add(L(1, "Star", 31, 12, 20, tpm: 3, tpa: 6, ftm: 4, fta: 5, dreb: 7, ast: 4, rim: 7));
        r.Home.Lines.Add(L(2, "Big", 14, 6, 9, ftm: 2, fta: 4, oreb: 5, dreb: 6, rim: 6));
        r.Home.Lines.Add(L(3, "Bench", 29, 11, 27, tpm: 3, tpa: 9, ftm: 4, fta: 4, rim: 4, started: false));
        r.Away.Lines.Add(L(4, "Guard", 24, 9, 21, tpm: 4, tpa: 10, ftm: 2, fta: 2, ast: 6, tov: 3, rim: 2));
        r.Away.Lines.Add(L(5, "Wing", 22, 8, 22, tpm: 2, tpa: 8, ftm: 4, fta: 6, tov: 4, rim: 3));
        r.Away.Lines.Add(L(6, "Sub", 20, 8, 20, tpm: 1, tpa: 5, ftm: 3, fta: 4, rim: 2, started: false));
        Assert.Equal(74, r.Home.Points); Assert.Equal(66, r.Away.Points);
        int h = 0, a = 0;
        void Score(double t, int p, bool home, int pts) { if (home) h += pts; else a += pts; r.Flow.Add(new FlowPoint(t, p, h, a)); }
        // UNC go 12 up in the first half, then Miami's 14-2 run in the second.
        for (int i = 0; i < 6; i++) Score(60 + i * 90, 1, false, 2);
        Score(700, 1, true, 2); Score(900, 1, false, 2); Score(1100, 1, true, 2);
        for (int i = 0; i < 7; i++) Score(1300 + i * 60, 2, true, 2);
        Score(1780, 2, false, 2);
        while (h < 74 || a < 66) { if (h < 74) Score(2000 + h, 2, true, Math.Min(2, 74 - h)); if (a < 66) Score(2001 + a, 2, false, Math.Min(2, 66 - a)); }
        r.CurrentPeriod = 2; r.Elapsed = 2400;
        return r;
    }

    private static IEnumerable<string> Words(IEnumerable<DeskBeat> beats) => beats.SelectMany(b =>
        new[] { b.Kicker, b.Title, b.Note, b.Line?.Text ?? "", b.Line?.Speaker.Name ?? "" }.Concat(b.Rows.SelectMany(r => new[] { r.Label, r.Away, r.Home })));

    [Fact]
    public void The_postgame_show_reads_the_final_box()
    {
        var r = Game();
        var table = new List<DeskRow> { new("", "North Carolina Tar Heels", "Miami Hurricanes"), new("Record", "4-3", "6-1", Key: true) };
        var beats = PostgameDesk.Build(r, table, "Miami Hurricanes win and move to 6-1.", new[] { "Final: #3 Duke 70, Clemson 61" });
        Assert.Equal(new[] { "POSTGAME", "PLAYER OF THE GAME", "BY THE NUMBERS", "TURNING POINT", "WHAT IT MEANS", "AROUND COLLEGE" }, beats.Select(b => b.Kicker));
        // The final: the score as the box has it, and the comeback from 12 down.
        var fin = beats[0];
        Assert.Equal("FINAL", fin.Rows[0].Label);
        Assert.Equal("66", fin.Rows[0].Away); Assert.Equal("74", fin.Rows[0].Home);
        Assert.StartsWith("Miami Hurricanes beat North Carolina Tar Heels 74-66, and they were down 12", fin.Line!.Text);
        // The player of the game: the best game score on the winning side.
        Assert.Equal("A Star", beats[1].Title);
        Assert.Equal("31", beats[1].Rows.Single(x => x.Label == "Points").Home);
        Assert.Contains("31 points", beats[1].Line!.Text);
        // How it was won: the full box's rim points (zones, K17), bench points and possessions.
        var how = beats[2];
        Assert.Equal("34", how.Rows.Single(x => x.Label == "Points at the rim").Home);   // (7+6+4) x 2
        Assert.Equal("14", how.Rows.Single(x => x.Label == "Points at the rim").Away);
        Assert.Equal("29", how.Rows.Single(x => x.Label == "Bench points").Home);
        Assert.Equal("67", how.Rows.Single(x => x.Label == "Possessions").Away);
        Assert.Single(how.Rows, x => x.Key && x.Label != "FINAL");
        // The turning point is Miami's run, as GameFlow describes it.
        var turn = beats[3];
        var best = GameFlow.Runs(r).OrderByDescending(x => x.For - x.Against).First();
        Assert.True(best.Home);
        Assert.Contains(GameFlow.Describe(r, best), turn.Line!.Text);
        Assert.Equal("12", turn.Rows.Single(x => x.Label == "Biggest lead").Away);
        // What it means and around college come from the session.
        Assert.Equal("6-1", beats[4].Rows[1].Home);
        Assert.Equal("Final: #3 Duke 70, Clemson 61", beats[5].Rows[0].Away);
        foreach (var w in Words(beats)) foreach (var n in RealNetworks) Assert.DoesNotContain(n, w);
        // No interviews (#983): nobody but the desk speaks.
        Assert.All(beats.Where(b => b.Line != null), b => Assert.Contains(b.Line!.Speaker, StudioShow.Cast));
    }

    [Fact]
    public void Overtime_and_a_game_without_a_timeline_still_make_a_show()
    {
        var r = Game();
        r.Overtimes = 1;
        r.Flow.Clear();
        var beats = PostgameDesk.Build(r);
        Assert.Equal("FINAL · OT", beats[0].Rows[0].Label);
        Assert.EndsWith("in overtime.", beats[0].Line!.Text);
        Assert.DoesNotContain(beats, b => b.Kicker is "TURNING POINT" or "WHAT IT MEANS" or "AROUND COLLEGE");
    }

    [Fact]
    public void A_stored_side_box_keeps_zones_possessions_and_the_timeline()
    {
        var r = Game();
        r.PregameEdge = 3.25; r.MarginSpread = 9;
        var box = CareerStore.SideBox.From("college", "k", r);
        var json = System.Text.Json.JsonSerializer.Serialize(box);
        var back = System.Text.Json.JsonSerializer.Deserialize<CareerStore.SideBox>(json)!;
        var teams = new Dictionary<int, Team> { [1] = r.Home.Team, [2] = r.Away.Team };
        var players = r.Home.Lines.Concat(r.Away.Lines).ToDictionary(l => l.Player.Id, l => l.Player);
        var g = back.ToResult(teams, players)!;
        Assert.Equal(68, g.Home.Possessions); Assert.Equal(67, g.Away.Possessions);
        Assert.Equal(2, g.Periods); Assert.Equal(1200, g.PeriodSeconds); Assert.Equal(3.25, g.PregameEdge, 2);
        Assert.Equal(PostgameDesk.RimPoints(r.Home), PostgameDesk.RimPoints(g.Home));
        Assert.Equal(r.Home.Lines[0].ZoneAttempts, g.Home.Lines[0].ZoneAttempts);
        Assert.Equal(r.Flow.Count, g.Flow.Count);
        Assert.Equal(r.Flow[^1].Home, g.Flow[^1].Home);
        Assert.Equal(GameFlow.LeadChanges(r), GameFlow.LeadChanges(g));
        Assert.Equal(2, g.CurrentPeriod);
        // The box score's panels read the same numbers as the game did.
        Assert.Equal(GameFlowInfo.TeamStats(r).Select(x => x.HomeText), GameFlowInfo.TeamStats(g).Select(x => x.HomeText));

        // An older save's box (19 numbers a line, no game row, no timeline) still loads: zones 0, halves from the period scores.
        var old = back with { Game = null, Flow = null, Lines = back.Lines!.Select(v => v.Take(19).ToArray()).ToList(),
                              HomePeriods = new[] { 30, 44 }, AwayPeriods = new[] { 38, 28 } };
        var o = old.ToResult(teams, players)!;
        Assert.Equal(74, o.Home.Points);
        Assert.Equal(0, PostgameDesk.RimPoints(o.Home));
        Assert.Equal(0, o.Home.Possessions);
        Assert.Equal(2, o.Periods);
        Assert.Empty(o.Flow);
    }

    [AvaloniaFact]
    public void A_college_final_airs_the_postgame_show_and_a_reload_keeps_the_box()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-post-{Guid.NewGuid():N}.save");
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
            var before = session.MyRecord;
            var (game, played) = session.BeginLiveDay();
            var live = new LiveGameViewModel(vm, game!, played);
            vm.CurrentPage = live;
            live.SimToEnd();
            Assert.StartsWith("Final", live.BreakTitle);
            Assert.StartsWith("FINAL", live.Clock);   // the top scoreboard read "H2 0:00" at the final
            Assert.True(live.DeskUp, "the postgame show comes on at the final");
            Assert.True(live.PostgameDesk);
            Assert.Equal("HSN postgame show", live.DeskName);
            Assert.Equal("Postgame show", live.DeskButton);
            var desk = live.HalftimeReel!;
            Assert.Equal("POSTGAME", desk.Plate);
            var box = game!.Result;
            Assert.Equal(box.Away.Points.ToString(), desk.Beats[0].Rows[0].Away);
            Assert.Equal(box.Home.Points.ToString(), desk.Beats[0].Rows[0].Home);
            var how = desk.Beats.Single(b => b.Kicker == "BY THE NUMBERS");
            Assert.Equal(PostgameDesk.RimPoints(box.Home).ToString(), how.Rows.Single(x => x.Label == "Points at the rim").Home);
            Assert.True(PostgameDesk.RimPoints(box.Home) + PostgameDesk.RimPoints(box.Away) > 0);
            Assert.InRange(box.Home.Possessions, 55, 85);
            // What it means counts tonight's result before Continue records it.
            var us = session.HumanTeam;
            bool won = box.Winner.Team == us;
            var meaning = desk.Beats.Single(b => b.Kicker == "WHAT IT MEANS");
            string rec = $"{before.Wins + (won ? 1 : 0)}-{before.Losses + (won ? 0 : 1)}";
            Assert.Equal(rec, us == box.Home.Team ? meaning.Rows.Single(x => x.Label == "Record").Home : meaning.Rows.Single(x => x.Label == "Record").Away);
            Assert.Contains(rec, meaning.Line!.Text);
            Assert.Contains(desk.Beats, b => b.Kicker == "TURNING POINT");
            foreach (var w in Words(desk.Beats)) foreach (var n in RealNetworks) Assert.DoesNotContain(n, w);
            foreach (var (width, scale) in new[] { (1480, 1.0), (1280, 1.0), (1280, 1.3) })
            {
                window.Width = width; window.ApplyScale(scale);
                for (int i = 0; i < desk.Beats.Count; i++)
                {
                    desk.Index = i;
                    Settle(window);
                    Scan(window, problems, $"{width}px {scale:P0} postgame beat {i} ({desk.Beats[i].Kicker})");
                    if (i == 0 || desk.Beats[i].Kicker == "BY THE NUMBERS") Snap(window, $"run145-postgame-{i}-{width}-{scale * 100:0}");
                }
            }
            window.ApplyScale(1.0); window.Width = 1480;
            // The final's card is one button away, and the show comes back.
            live.ToCardCommand.Execute(null);
            Assert.True(live.CardUp); Assert.False(live.DeskUp);
            Settle(window);
            Scan(window, problems, "final card");
            live.ToDeskCommand.Execute(null);
            Assert.True(live.DeskUp);

            // Continue records the game; the record moves as the show said.
            live.Continue();
            Assert.Equal(rec, $"{session.MyRecord.Wins}-{session.MyRecord.Losses}");
            session.SaveNow();

            // K17: after a reload the stored box keeps its zones, possessions and Game flow.
            var loaded = GameSession.Load(path);
            var stored = loaded.MyGames().Last(g => g.Played).Result!;
            Assert.Equal(box.Home.Points, stored.Home.Points);
            Assert.Equal(box.Home.Possessions, stored.Home.Possessions);
            Assert.Equal(PostgameDesk.RimPoints(box.Home), PostgameDesk.RimPoints(stored.Home));
            Assert.Equal(2, stored.Periods);
            Assert.NotEmpty(stored.Flow);
            var page = new BoxScoreViewModel(vm, stored);
            Assert.True(page.HasFlow);
            Assert.Equal(GameFlowInfo.TeamStats(box).Select(x => x.HomeText), GameFlowInfo.TeamStats(stored).Select(x => x.HomeText));
            var head = GameFlowInfo.Splits(stored)[0].Cells;
            Assert.Equal("H1", head[0]); Assert.Equal("H2", head[1]);
            Assert.DoesNotContain("–", GameFlowInfo.Splits(stored)[1].Cells);
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
    public void Skipping_the_shows_keeps_the_final_card()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-post-{Guid.NewGuid():N}.save");
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
            live.SimToEnd();
            Assert.True(live.CardUp);
            Assert.False(live.DeskUp);
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
