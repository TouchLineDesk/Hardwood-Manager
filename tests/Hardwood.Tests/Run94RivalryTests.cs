using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.College;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 94. C5 part 1 (college-sim-design #165): real college rivalries (named series where they have a real name),
/// in-state rivals for the rest, the series counted from the career's start and saved, ranked-matchup badges, and the
/// rivalry on Home, Schedule and in the inbox. No result or random number changes.</summary>
[Collection(SharedUiState.Name)]
public class Run94RivalryTests
{
    [Fact]
    public void Every_listed_rivalry_names_real_programs_and_the_world_finds_them()
    {
        var schools = RealColleges.Programs.Select(p => p.School).ToHashSet();
        foreach (var (a, b, _) in CollegeWorld.RealRivalries)
        {
            Assert.Contains(a, schools);
            Assert.Contains(b, schools);
        }
        Assert.All(CollegeWorld.PhiladelphiaBig5, x => Assert.Contains(x, schools));

        var (_, world) = RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        int Id(string school) => cw.Programs.Single(t => t.City == school).Id;
        Assert.Equal("Crosstown Shootout", cw.RivalryOf(Id("Xavier"), Id("Cincinnati"))!.Name);
        Assert.Equal("Sunflower Showdown", cw.RivalryOf(Id("Kansas"), Id("Kansas State"))!.Name);
        var dukeUnc = cw.RivalryOf(Id("Duke"), Id("North Carolina"))!;
        Assert.Null(dukeUnc.Name);
        Assert.False(dukeUnc.InState);
        Assert.Equal("RIVALRY", dukeUnc.Tag);
        Assert.Equal("Philadelphia Big 5", cw.RivalryOf(Id("Penn"), Id("Drexel"))!.Name);
        Assert.Null(cw.RivalryOf(Id("Duke"), Id("Gonzaga")));
        // Listed: 37 pairs and the Big 5's 15. Every listed school has a rival, listed ones first.
        Assert.Equal(52, cw.Rivalries.Values.Count(r => !r.InState));
        Assert.Equal("Battle for the Bluegrass", cw.RivalsOf(Id("Kentucky")).First().Rivalry.Name);
        // In-state fallback: conference mates in one state, only for programs with no listed rival.
        var listed = cw.Rivalries.Values.Where(r => !r.InState).SelectMany(r => new[] { r.A, r.B }).ToHashSet();
        var inState = cw.Rivalries.Values.Where(r => r.InState).ToList();
        Assert.NotEmpty(inState);
        Assert.All(inState, r =>
        {
            Assert.Equal(cw.ConferenceOf[r.A], cw.ConferenceOf[r.B]);
            Assert.Equal(RealColleges.StateOf(cw.Program(r.A)!.City), RealColleges.StateOf(cw.Program(r.B)!.City));
            Assert.False(listed.Contains(r.A) && listed.Contains(r.B));
        });
        Assert.Equal("an in-state rivalry", inState[0].Label);
        Assert.Equal("#5 vs #9", CollegeWorld.RankedMatchup(5, 9));
        Assert.Null(CollegeWorld.RankedMatchup(5, null));
    }

    [AvaloniaFact]
    public void A_college_coach_sees_his_rivals_on_Home_Schedule_and_in_the_inbox()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-rivalry-{Guid.NewGuid():N}.save");
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
            start.SelectedTeam = start.Teams.First(t => t.Team.City == "Duke");
            start.NewGame();
            var session = vm.Session!;
            Assert.True(session.InCollege);
            var cw = session.World.College!;
            int duke = session.HumanTeam.Id, unc = cw.Programs.Single(t => t.City == "North Carolina").Id;

            // The schedule marks the rivalry game and offers a filter for it.
            var games = session.MyGames();
            var rivalGame = games.First(g => g.Opponent.Id == unc);
            Assert.NotNull(rivalGame.Rivalry);
            Assert.All(games.Where(g => g.Rivalry != null), g => Assert.NotNull(cw.RivalryOf(duke, g.Opponent.Id)));
            vm.Navigate("Schedule & Results");
            var sched = (ScheduleViewModel)vm.CurrentPage!;
            Assert.Contains(sched.Filters, f => f.Name == ScheduleViewModel.RivalryFilter);
            sched.SetFilter(ScheduleViewModel.RivalryFilter);
            Assert.All(sched.Games, g => Assert.Equal("RIVALRY", g.Tag is "IN-STATE" ? "RIVALRY" : g.Tag));
            Assert.Contains(sched.Games, g => g.OpponentTeam!.Id == unc);
            sched.Selected = sched.Games.First(g => g.OpponentTeam!.Id == unc);
            Assert.Equal("Rivalry · no meetings yet in this career", sched.RivalryLine);
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Schedule rivalry filter | {p}");

            // The day before: Home's card names the rivalry.
            while (session.NextMyGame!.Opponent.Id != unc) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Home");
            var home = (HomeViewModel)vm.CurrentPage!;
            Assert.StartsWith("Rivalry · first meeting in this career", home.GameTag);
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Home rivalry | {p}");
            Snap(window, "run94-home-rivalry");

            // Play it: one inbox line, the series counted, and the Schedule's line after.
            int inbox = session.Inbox.Count(m => m.Title.Contains("rival North Carolina"));
            while (!session.MyGames().First(g => g.Opponent.Id == unc).Played) session.AdvanceDay();
            vm.AfterTick();
            var played = session.MyGames().First(g => g.Opponent.Id == unc);
            Assert.Equal(inbox + 1, session.Inbox.Count(m => m.Title.Contains("rival North Carolina")));
            var msg = session.Inbox.First(m => m.Title.Contains("rival North Carolina"));
            Assert.StartsWith(played.Won ? "Beat rival North Carolina" : "Lost to rival North Carolina", msg.Title);
            Assert.Contains(played.Won ? "You lead the series 1-0 since" : "North Carolina leads the series 1-0 since", msg.Body);
            var series = cw.SeriesOf(duke, unc)!;
            Assert.Equal(1, series.Games);
            Assert.Equal(played.Won ? duke : unc, series.LastWinner);
            vm.Navigate("Schedule & Results");
            sched = (ScheduleViewModel)vm.CurrentPage!;
            sched.Selected = sched.Games.First(g => g.OpponentTeam!.Id == unc);
            Assert.StartsWith("Rivalry · " + (played.Won ? "You lead the series 1-0" : "North Carolina leads the series 1-0"), sched.RivalryLine);
            Assert.Contains("· last: ", sched.RivalryLine);
            sched.SetFilter(ScheduleFilter.All);
            Snap(window, "run94-schedule-rivalry");

            // Every series counted is a rivalry, from both engines; the world's other rivals have met too.
            Assert.All(cw.Series.Keys, k =>
            {
                var ids = k.Split('-').Select(int.Parse).ToArray();
                Assert.NotNull(cw.RivalryOf(ids[0], ids[1]));
            });
            Assert.True(cw.Series.Count > 20, $"only {cw.Series.Count} series");

            // Saved and loaded.
            session.SaveNow();
            var loaded = GameSession.Load(path);
            Assert.Equal(cw.Series.Count, loaded.World.College!.Series.Count);
            Assert.Equal(1, loaded.World.College.SeriesOf(duke, unc)!.Games);

            // Ranked matchups: the tag reads from both ranks.
            var ranked = new MyGame(session.Today, cw.Program(unc)!, true, "x", "y", null, null, null) { OurRank = 5, OpponentRank = 9 };
            Assert.Equal("#5 v #9", new GameRow { Game = ranked }.Tag);
            Assert.Equal("Ranked matchup: #5 vs #9", HomeViewModel.CollegeGameTag(session, session.HumanTeam, ranked));
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
