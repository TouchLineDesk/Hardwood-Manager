using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.Graphics;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 140, B2a (broadcast-presentation-design.md): a college live game in the arena. The Blender frame
/// (tools/Render/arena.py) with the floor dressed for the home program or the event, the crowd tinted, the players on the
/// projected floor, the play caption bar and the score bug (ranks, score, half, clock, team fouls, the bonus from the 7th
/// team foul). Presentation only: it reads the game.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run140ArenaTests
{
    private static Team Program(string city, string name, string primary = "#0033A0", string secondary = "#FFFFFF") =>
        new() { Id = 9001, City = city, Name = name, Abbreviation = "TST", Division = "D", PrimaryColor = primary, SecondaryColor = secondary };

    [Fact]
    public void The_floor_is_dressed_for_the_home_program_or_the_event()
    {
        var home = Program("Kentucky", "Wildcats");
        var campus = ArenaDress.For(home, new CollegeGame { Date = new DateOnly(2026, 11, 12), HomeId = 9001, AwayId = 2, Stage = "Non-conference" });
        Assert.False(campus.Neutral);
        Assert.Equal("KENTUCKY", campus.LeftBaseline);
        Assert.Equal("WILDCATS", campus.RightBaseline);
        Assert.Equal("#0033A0", campus.Paint);
        Assert.Same(home, campus.CrestTeam);
        Assert.Equal(campus, ArenaDress.For(home, null));

        var conf = ArenaDress.For(home, new CollegeGame { Date = new DateOnly(2027, 3, 12), Stage = "Conference tournament", Event = "SEC tournament", Round = 2 });
        Assert.True(conf.Neutral);
        Assert.Null(conf.CrestTeam);
        Assert.Equal("SEC TOURNAMENT", conf.LeftBaseline);
        Assert.Equal(ArenaDress.EventPaint, conf.Paint);

        var r64 = ArenaDress.For(home, new CollegeGame { Date = new DateOnly(2027, 3, 19), Stage = CollegeWorld.Madness, Event = CollegeWorld.Madness, Round = 1 });
        Assert.Equal("MARCH MADNESS", r64.RightBaseline);
        Assert.Equal("ROUND OF 64", r64.Sideline);
        Assert.Equal(ArenaDress.MadnessPaint, r64.Paint);

        var semi = ArenaDress.For(home, new CollegeGame { Date = new DateOnly(2027, 4, 3), Stage = CollegeWorld.Madness, Event = CollegeWorld.Madness, Round = 5 });
        var final = ArenaDress.For(home, new CollegeGame { Date = new DateOnly(2027, 4, 5), Stage = CollegeWorld.Madness, Event = CollegeWorld.Madness, Round = 6 });
        Assert.Equal("FINAL FOUR", semi.LeftBaseline);
        Assert.Equal("NATIONAL SEMIFINAL · DETROIT 2027", semi.Sideline);
        Assert.Equal("NATIONAL CHAMPIONSHIP · DETROIT 2027", final.Sideline);
        Assert.Equal(ArenaDress.FinalFourPaint, final.Paint);
    }

    [Fact]
    public void The_score_bug_names_the_half_and_the_bonus()
    {
        Assert.Equal("1ST HALF", LiveGameViewModel.BugPeriodName(1, 2));
        Assert.Equal("2ND HALF", LiveGameViewModel.BugPeriodName(2, 2));
        Assert.Equal("OT", LiveGameViewModel.BugPeriodName(3, 2));
        Assert.Equal("2OT", LiveGameViewModel.BugPeriodName(4, 2));
        Assert.Equal("3RD QTR", LiveGameViewModel.BugPeriodName(3, 4));
        // College: a one-and-one from the 7th team foul (BONUS), two shots from the 10th (DBL BONUS).
        Assert.Equal(0, Rules.College.BonusShots(6));
        Assert.Equal(1, Rules.College.BonusShots(7));
        Assert.Equal(2, Rules.College.BonusShots(10));
        Assert.Equal("", LiveGameViewModel.BonusLabel(0, true));
        Assert.Equal("BONUS", LiveGameViewModel.BonusLabel(1, true));
        Assert.Equal("DBL BONUS", LiveGameViewModel.BonusLabel(2, true));
        Assert.Equal("BONUS", LiveGameViewModel.BonusLabel(2, false));
    }

    [AvaloniaFact]
    public void The_projection_puts_the_court_on_the_rendered_floor()
    {
        var arena = BroadcastArt.Arena;
        Assert.NotNull(arena.Frame);
        Assert.NotNull(arena.Mask);
        Assert.NotNull(arena.Court);
        var c = arena.Court!;
        var proj = new CourtProjection(c);
        // The corners land exactly; the far sideline is shorter than the near one (perspective); centre court is between.
        var corners = new[] { (0.0, 0.0), (94.0, 0.0), (94.0, 50.0), (0.0, 50.0) };
        for (int i = 0; i < 4; i++)
        {
            var p = proj.Map(corners[i].Item1, corners[i].Item2);
            Assert.InRange(Math.Abs(p.X - c[i].X) + Math.Abs(p.Y - c[i].Y), 0, 0.01);
        }
        Assert.True(c[1].X - c[0].X < c[2].X - c[3].X);
        var mid = proj.Map(47, 25);
        Assert.InRange(mid.X, 955, 965);
        Assert.True(mid.Y > c[0].Y && mid.Y < c[3].Y);
        // Perspective: centre court sits above the midpoint of the far and near sidelines' screen heights.
        Assert.True(mid.Y < (c[0].Y + c[3].Y) / 2);
        Assert.True(proj.Scale(47, 50) > proj.Scale(47, 0) * 1.3, "near players are drawn bigger than far ones");
        // Every corner is inside the 1920 x 1080 frame.
        Assert.All(c, p => { Assert.InRange(p.X, 0, 1920); Assert.InRange(p.Y, 0, 1080); });
    }

    [AvaloniaFact]
    public void A_college_live_game_plays_in_the_arena_with_a_score_bug()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-arena-{Guid.NewGuid():N}.save");
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
            Assert.True(session.InCollege);
            while (!session.HumanPlaysToday) session.AdvanceDay();
            vm.AfterTick();
            var cg = session.World.College!.GameOn(session.HumanTeam.Id, session.Today)!;
            var (game, played) = session.BeginLiveDay();
            Assert.NotNull(game);
            var live = new LiveGameViewModel(vm, game!, played);
            vm.CurrentPage = live;
            Assert.True(live.InArena);
            Assert.False(live.FlatCourt);
            var home = game!.Result.Home.Team;
            Assert.Equal(ArenaDress.For(home, cg), live.Dress);
            Assert.Equal("1ST HALF", live.BugPeriod);
            Assert.Equal("20:00", live.BugClock);
            Assert.Equal("FOULS 0", live.HomeFouls);
            Assert.False(live.HomeInBonus || live.AwayInBonus);
            Snap(window, "run140-arena-tipoff");

            // Play until a side reaches the bonus: the bug says so beside the side that shoots, from the 7th team foul.
            bool sawBonus = false;
            while (!game.Complete && !sawBonus)
            {
                live.StepOne();
                if (live.ShowBreak) live.HideBreakCommand.Execute(null);
                int awayFouls = game.AwayTeamFouls, homeFouls = game.HomeTeamFouls;
                Assert.Equal($"FOULS {homeFouls}", live.HomeFouls);
                Assert.Equal(homeFouls >= 7, live.AwayInBonus);
                Assert.Equal(awayFouls >= 7, live.HomeInBonus);
                if (homeFouls >= 7) { sawBonus = true; Assert.Equal(homeFouls >= 10 ? "DBL BONUS" : "BONUS", live.AwayBonus); }
                if (awayFouls >= 7) { sawBonus = true; Assert.Equal(awayFouls >= 10 ? "DBL BONUS" : "BONUS", live.HomeBonus); }
            }
            Assert.True(sawBonus, "a college game reaches the bonus");
            Assert.Equal(game.AwayScore, live.AwayScore);
            Assert.Equal(game.HomeScore, live.HomeScore);
            Assert.NotNull(live.Frame);
            var court = window.GetVisualDescendants().OfType<LiveCourt>().First(c => c.Name == "ArenaCourt");
            Assert.True(court.IsEffectivelyVisible);
            Assert.True(court.InArena);
            Assert.NotNull(court.Pose);
            Snap(window, "run140-arena-bonus");

            foreach (var (width, scale, big) in new[] { (1480, 1.0, false), (1480, 1.0, true), (1280, 1.0, false), (1280, 1.3, false) })
            {
                window.Width = width;
                window.ApplyScale(scale);
                if (live.BigCourt != big) live.ToggleBigCourtCommand.Execute(null);
                Settle(window);
                if (Environment.GetEnvironmentVariable("HW_LAYOUT_SHOTS") is { Length: > 0 } shots)
                {
                    Directory.CreateDirectory(shots);
                    using var frame = window.CaptureRenderedFrame();
                    frame?.Save(Path.Combine(shots, $"run140-arena-{width}-{scale * 100:0}{(big ? "-big" : "")}.png"));
                }
                foreach (var p in LayoutCheck.Scan(window)) problems.Add($"{width}px {scale:P0}{(big ? " big" : "")} arena | {p}");
                // The caption: in the arena's bar when there's room, above the arena at full size when it's small.
                var view = window.GetVisualDescendants().OfType<LiveGameView>().First(v => v.IsEffectivelyVisible && v.Bounds.Width > 0);
                double arenaWidth = view.FindControl<Viewbox>("ArenaBox")!.Bounds.Width;
                bool room = arenaWidth >= LiveGameView.ArenaCaptionMinWidth;
                Assert.True(room == view.FindControl<Border>("ArenaCaption")!.IsVisible, $"{width} {scale} {big}: arena {arenaWidth} shown {live.CourtShown} vis {view.FindControl<Viewbox>("ArenaBox")!.IsEffectivelyVisible} max {view.FindControl<Viewbox>("ArenaBox")!.MaxHeight} views {window.GetVisualDescendants().OfType<LiveGameView>().Count()} court {view.FindControl<LiveCourt>("ArenaCourt")!.Bounds}");
                Assert.Equal(!room, view.FindControl<Panel>("CaptionPanel")!.IsVisible);
            }
            window.ApplyScale(1.0); window.Width = 1480;
            if (live.BigCourt) live.ToggleBigCourtCommand.Execute(null);

            // To the final: the bug says FINAL and the score matches the result.
            live.SimToEndCommand.Execute(null);
            Settle(window);
            Assert.True(game.Complete);
            Assert.StartsWith("FINAL", live.BugPeriod);
            Assert.Equal("", live.BugClock);
            Assert.False(live.AwayInBonus || live.HomeInBonus);
            Snap(window, "run140-arena-final");
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
    public void Event_floors_draw_for_a_neutral_site()
    {
        // The Final Four floor and a conference tournament floor, drawn on their own for the shots.
        var home = Program("Kentucky", "Wildcats");
        foreach (var (name, g) in new[]
        {
            ("final-four", new CollegeGame { Date = new DateOnly(2027, 4, 5), Stage = CollegeWorld.Madness, Event = CollegeWorld.Madness, Round = 6 }),
            ("madness", new CollegeGame { Date = new DateOnly(2027, 3, 21), Stage = CollegeWorld.Madness, Event = CollegeWorld.Madness, Round = 2 }),
            ("conference", new CollegeGame { Date = new DateOnly(2027, 3, 13), Stage = "Conference tournament", Event = "SEC tournament", Round = 3 }),
            ("campus", new CollegeGame { Date = new DateOnly(2026, 12, 5), Stage = "Non-conference" }),
        })
        {
            var court = new LiveCourt { Arena = BroadcastArt.Arena, Dress = ArenaDress.For(home, g), Width = 1600, Height = 900 };
            var window = new Window { Content = court, Width = 1600, Height = 900 };
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(court.InArena);
            Assert.Equal(1600, court.Bounds.Width, 0.5);
            if (Environment.GetEnvironmentVariable("HW_LAYOUT_SHOTS") is { Length: > 0 } shots)
            {
                Directory.CreateDirectory(shots);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(shots, $"run140-floor-{name}.png"));
            }
            window.Close();
        }
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
