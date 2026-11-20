using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 96. C5's last piece (college-sim-design #165): each conference has a style (pace, defence, physicality)
/// that shows in its conference games and tournament, on the full engine and the quick sim alike. Both sides play the
/// same style, so the favourite's edge barely moves. Non-conference games, invitationals and March Madness are plain.</summary>
[Collection(SharedUiState.Name)] // touches app settings or the portrait service (run 131: they raced PortraitServiceTests)
public class Run96ConferenceStyleTests
{
    private readonly ITestOutputHelper _out;
    public Run96ConferenceStyleTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Every_conference_has_a_style_and_only_its_own_games_use_it()
    {
        foreach (var c in CollegeWorld.Conferences)
        {
            Assert.True(ConferenceStyle.All.ContainsKey(c.Key), $"{c.Name} has no style");
            var st = ConferenceStyle.Of(c.Key);
            Assert.InRange(st.Pace, 0.95, 1.05); Assert.InRange(st.Defence, -1, 1); Assert.InRange(st.Physical, -1, 1);
        }
        Assert.Equal("Big Ten style: deliberate, defence first, physical (fewer possessions, lower scores, more whistles)", CollegeWorld.StyleLine("r-b1g"));
        Assert.Equal("ACC style: balanced", CollegeWorld.StyleLine("r-acc"));
        Assert.Equal("SWAC style: up-tempo, open, offence first, physical (more possessions, higher scores, more whistles)", CollegeWorld.StyleLine("r-swac"));

        var (_, world) = RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        int Id(string school) => cw.Programs.Single(t => t.City == school).Id;
        var b1g = cw.Season.Games.First(g => g.Stage == "Conference" && g.HomeId == Id("Purdue"));
        Assert.Equal("r-b1g", cw.StyleConference(b1g));
        var r = cw.GameRules(b1g);
        Assert.Equal(Rules.College.PaceScale * 0.96, r.PaceScale, 9);
        Assert.Equal(-0.02, r.ShotLogitShift, 9);
        Assert.Equal(Rules.College.FoulScale * 1.10, r.FoulScale, 9); // K11 (run 134): the style sits on top of the college profile
        Assert.Equal(cw.HomeCourtPoints(Id("Purdue")), r.HomePoints, 6); // the arena's edge is kept
        // A non-conference game and March Madness play the plain college game.
        var nonConf = cw.Season.Games.First(g => g.Stage == "Non-conference");
        Assert.Null(cw.StyleConference(nonConf));
        Assert.Equal(Rules.College.PaceScale, cw.GameRules(nonConf).PaceScale);
        Assert.Equal(Rules.CollegeNeutral, cw.GameRules(new CollegeGame { Event = CollegeWorld.Madness, HomeId = Id("Purdue"), AwayId = Id("Duke") }));

        // The engine's config follows the style without changing the shared default arrays; the pros are untouched.
        var cfg = new EngineConfig();
        var before = cfg.ZoneBaseLogit.ToArray();
        var styled = cfg.For(r);
        Assert.Equal(before[0] - 0.02, styled.ZoneBaseLogit[0], 9);
        Assert.Equal(cfg.NonShootingFoulRate * 1.10, styled.NonShootingFoulRate, 9);
        Assert.Equal(before, cfg.ZoneBaseLogit);
        Assert.Same(cfg, cfg.For(Rules.Nba));
    }

    [Fact]
    public void Styles_show_in_the_box_score_and_the_quick_sim_matches_the_engine()
    {
        var (_, world) = RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        var home = cw.Programs.Single(t => t.City == "Purdue"); var away = cw.Programs.Single(t => t.City == "Indiana");
        var sim = new GameSimulator();
        var plain = Rules.College;
        (double pts, double poss, double fta, double homeWin) Play(Rules rules, int n = 800)
        {
            double pts = 0, poss = 0, fta = 0, hw = 0;
            for (int i = 0; i < n; i++)
            {
                var g = sim.Simulate(home, away, rules, (ulong)(1000 + i), false, new DateOnly(2027, 1, 15));
                pts += g.Home.Points + g.Away.Points; poss += g.Home.Possessions + g.Away.Possessions;
                fta += g.Home.FTA + g.Away.FTA; if (g.Home.Points > g.Away.Points) hw++;
            }
            return (pts / n, poss / n, fta / n, hw / n);
        }
        var p = Play(plain);
        foreach (var key in new[] { "r-b1g", "r-b12", "r-swac", "r-ivy" })
        {
            var st = ConferenceStyle.Of(key);
            var s = Play(st.Apply(plain));
            double engineRatio = s.pts / p.pts;
            _out.WriteLine($"{key}: points {s.pts:0.0} (plain {p.pts:0.0}, ×{engineRatio:0.000}; quick sim ×{st.PointsScale:0.000}), possessions {s.poss:0.0} ({p.poss:0.0}), FTA {s.fta:0.0} ({p.fta:0.0}), home wins {s.homeWin:P0} ({p.homeWin:P0})");
            Assert.InRange(st.PointsScale - engineRatio, -0.025, 0.025); // the quick sim's total follows the engine
            Assert.InRange(s.homeWin - p.homeWin, -0.06, 0.06);        // who wins barely moves (800 games: about ±0.025 of noise)
            if (st.Physical >= 0.6) Assert.True(s.fta > p.fta, $"{key}: physical but no more free throws");
            if (st.Pace <= 0.97) Assert.True(s.poss < p.poss);
            if (st.Pace >= 1.03) Assert.True(s.poss > p.poss);
        }
    }

    [Fact]
    public void A_season_shows_the_styles_and_home_courts_still_hold()
    {
        var (_, world) = RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        cw.PlayThrough(new DateOnly(2027, 3, 1), new GameSimulator(), new Rng(2026));
        var conf = cw.Season.Games.Where(g => g.Played && g.Stage == "Conference").ToList();
        double Avg(string key) => conf.Where(g => cw.StyleConference(g) == key).Average(g => g.HomePts!.Value + g.AwayPts!.Value);
        double b1g = Avg("r-b1g"), b12 = Avg("r-b12"), swac = Avg("r-swac"), summit = Avg("r-summit"), all = conf.Average(g => g.HomePts!.Value + g.AwayPts!.Value);
        double home = conf.Count(g => g.HomePts > g.AwayPts) / (double)conf.Count;
        _out.WriteLine($"conference games {conf.Count}: total points all {all:0.0}, Big Ten {b1g:0.0}, Big 12 {b12:0.0}, SWAC {swac:0.0}, Summit {summit:0.0}; home sides won {home:P1}");
        Assert.True(b1g < swac && b12 < swac && b1g < summit);
        Assert.InRange(all, 125, 155);   // Division I: about 140 points a game
        Assert.InRange(home, 0.56, 0.70);
    }

    [AvaloniaFact]
    public void A_college_coach_sees_the_style_on_Home_and_on_Conferences()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-style-{Guid.NewGuid():N}.save");
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
            start.PlayLeague = "College basketball";
            start.SelectedTeam = start.Teams.First(t => t.Team.City == "Purdue");
            start.NewGame();
            var session = vm.Session!;
            Assert.True(session.InCollege);

            // Non-conference games carry no style; conference games carry the Big Ten's.
            var games = session.MyGames();
            Assert.All(games.Where(g => g.Group == ScheduleFilter.CollegeGroup(null, "Non-conference")), g => Assert.Null(g.StyleConference));
            Assert.Contains(games, g => g.StyleConference == "r-b1g");
            vm.Navigate("Home");
            var home = (HomeViewModel)vm.CurrentPage!;
            Assert.Equal("", home.StyleLine);

            while (session.NextMyGame!.StyleConference == null) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Home");
            home = (HomeViewModel)vm.CurrentPage!;
            Assert.Equal(CollegeWorld.StyleLine("r-b1g"), home.StyleLine);
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Home conference style | {p}");
            Snap(window, "run96-home-style");

            vm.Navigate("College");
            var college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 1;
            Settle(window);
            Assert.StartsWith("Big Ten style: deliberate, defence first, physical", college.ConferenceStyle);
            Assert.NotEmpty(college.ConferenceRows);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px College Conferences | {p}");
            Snap(window, "run96-college-conferences");
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
        if (report is { Length: > 0 }) File.AppendAllLines(report, problems);
        Assert.Empty(problems);
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
