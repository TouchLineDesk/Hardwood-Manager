using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 141, B2b (broadcast-presentation-design.md): the arena's jumbotron (the team comparison, live), the two "On the
/// floor" panels in team colours with the engine's energy as bars, the offence and defence tags and the momentum chip.
/// Presentation only: everything is read from the box score, the score flow and the engine.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run141ArenaBoardTests
{
    private static Team T(int id, string abbr) =>
        new() { Id = id, City = abbr, Name = abbr, Abbreviation = abbr, Division = "D", PrimaryColor = "#0033A0", SecondaryColor = "#FFFFFF" };

    [Fact]
    public void Momentum_reads_the_last_four_minutes_played()
    {
        var r = new GameResult { Home = new TeamBox { Team = T(1, "MIA") }, Away = new TeamBox { Team = T(2, "UNC") } };
        Assert.Equal("EVEN", ArenaBoard.Momentum(r).Label);
        Assert.Equal(0.5, ArenaBoard.HomeShare(ArenaBoard.Momentum(r)));
        // UNC led 20-10 at 6:00 played; then MIA scored 8 straight by 9:00.
        r.Flow.Add(new FlowPoint(300, 1, 10, 20));
        r.Flow.Add(new FlowPoint(360, 1, 10, 20));
        r.Flow.Add(new FlowPoint(420, 1, 14, 20));
        r.Flow.Add(new FlowPoint(540, 1, 18, 20));
        r.Elapsed = 560;
        var m = ArenaBoard.Momentum(r);
        // The score at the window's start is the last point at or before 320 s: 10-20 (at 300).
        Assert.Equal(8, m.Swing);
        Assert.Equal("MIA +8", m.Label);
        Assert.InRange(ArenaBoard.HomeShare(m), 0.83, 0.84);
        // A 12-point swing or more fills the bar.
        r.Flow.Add(new FlowPoint(550, 1, 24, 20));
        Assert.Equal(1.0, ArenaBoard.HomeShare(ArenaBoard.Momentum(r)));
        // Later, UNC answers: the window has moved on.
        r.Flow.Add(new FlowPoint(700, 1, 24, 27));
        r.Elapsed = 800;
        var later = ArenaBoard.Momentum(r);
        // The window opens at 560: the score then was 24-20 (at 550).
        Assert.Equal(-7, later.Swing);
        Assert.Equal("UNC +7", later.Label);
    }

    [Fact]
    public void A_floor_row_names_energy_bands_and_foul_trouble()
    {
        var p = new Player { Id = 1, FirstName = "Shai", LastName = "Gilgeous-Alexander", PrimaryPosition = Position.PG };
        Assert.Equal("Gilgeous-Al.", ArenaBoard.Surname(p));
        var box = new TeamBox { Team = T(1, "MIA") };
        box.Lines.Add(new PlayerLine { Player = p, Points = 12, Fouls = 4, FGM = 5, FGA = 9 });
        var row = ArenaBoard.OnFloor(box, new[] { p }, _ => 0.72).Single();
        Assert.Equal("tired", ArenaBoard.OnFloor(box, new[] { p }, _ => 0.695).Single().EnergyBand);   // shown as 70%, but under the sub line
        Assert.Equal("ok", row.EnergyBand);
        Assert.Equal(72, row.EnergyPercent);
        Assert.True(row.FoulTrouble, "four fouls when five foul out");
        Assert.Equal("5-9", row.Fg);
        Assert.False(ArenaBoard.OnFloor(box, new[] { p }, _ => 0.9, foulsToFoulOut: 6).Single().FoulTrouble, "six foul out in the pros");
        Assert.Equal("fresh", ArenaBoard.OnFloor(box, new[] { p }, _ => 0.9).Single().EnergyBand);
        Assert.Equal("tired", ArenaBoard.OnFloor(box, new[] { p }, _ => 0.5).Single().EnergyBand);
    }

    [Fact]
    public void Breaks_are_named_on_the_bug_and_the_jumbotron()
    {
        Assert.Equal("HALFTIME", LiveGameViewModel.BreakName(1, 2));
        Assert.Equal("END OF 2ND HALF", LiveGameViewModel.BreakName(2, 2));   // a tie, heading to overtime
        Assert.Equal("END OF OT", LiveGameViewModel.BreakName(3, 2));
        Assert.Equal("END OF 2OT", LiveGameViewModel.BreakName(4, 2));
        Assert.Equal("END OF 1ST QTR", LiveGameViewModel.BreakName(1, 4));
        Assert.Equal("HALFTIME", LiveGameViewModel.BreakName(2, 4));
        Assert.Equal("END OF 4TH QTR", LiveGameViewModel.BreakName(4, 4));
        Assert.Equal("END OF OT", LiveGameViewModel.BreakName(5, 4));
    }

    [Fact]
    public void Shooting_lines_are_lit_by_percentage()
    {
        var r = new GameResult { Home = new TeamBox { Team = T(1, "MIA") }, Away = new TeamBox { Team = T(2, "UNC") } };
        var a = new Player { Id = 2, FirstName = "A", LastName = "Away" };
        var h = new Player { Id = 1, FirstName = "H", LastName = "Home" };
        r.Away.Lines.Add(new PlayerLine { Player = a, ThreePM = 2, ThreePA = 20, FTM = 1, FTA = 2, Turnovers = 3 });
        r.Home.Lines.Add(new PlayerLine { Player = h, ThreePM = 1, ThreePA = 2, FTM = 4, FTA = 10, Turnovers = 5 });
        var rows = ArenaBoard.Compare(r).ToDictionary(x => x.Label);
        Assert.Equal(1, rows["3PT"].Lead);    // 1-2 beats 2-20
        Assert.Equal(-1, rows["FT"].Lead);    // 1-2 (50%) beats 4-10 (40%)
        Assert.Equal(-1, rows["TO"].Lead);    // fewer turnovers
        Assert.Equal("-", rows["FG%"].Away);
    }

    private static void CheckBoard(LiveGameViewModel live, GameSimulator.LiveGame game, string when)
    {
        var r = game.Result;
        var rows = live.Board.ToDictionary(b => b.Label);
        Assert.Equal($"{r.Away.FGM}-{r.Away.FGA}", rows["FG"].Away);
        Assert.Equal($"{r.Home.FGM}-{r.Home.FGA}", rows["FG"].Home);
        Assert.Equal($"{r.Away.ThreePM}-{r.Away.ThreePA}", rows["3PT"].Away);
        Assert.Equal($"{r.Home.FTM}-{r.Home.FTA}", rows["FT"].Home);
        Assert.Equal(r.Away.Lines.Sum(l => l.Rebounds).ToString(), rows["REB"].Away);
        Assert.Equal(r.Home.Lines.Sum(l => l.Rebounds).ToString(), rows["REB"].Home);
        Assert.Equal(r.Home.Assists.ToString(), rows["AST"].Home);
        Assert.Equal(r.Away.Steals.ToString(), rows["STL"].Away);
        Assert.Equal(r.Home.Blocks.ToString(), rows["BLK"].Home);
        Assert.Equal(r.Away.Turnovers.ToString(), rows["TO"].Away);
        // The jumbotron's lit side leads (fewer turnovers leads).
        int to = rows["TO"].Lead;
        Assert.Equal(Math.Sign(r.Away.Turnovers - r.Home.Turnovers), to);
        Assert.Equal(8, live.JumboRows.Count);
        // The panels hold the five on the floor, with their lines and the engine's energy.
        foreach (var (panel, box, five) in new[] { (live.AwayFloor, r.Away, game.AwayOnCourt), (live.HomeFloor, r.Home, game.HomeOnCourt) })
        {
            Assert.Equal(five.OrderBy(p => p.Id).Select(p => p.Id), panel.Select(x => x.Row.Player.Id).OrderBy(i => i));
            foreach (var x in panel)
            {
                var l = box.LineFor(x.Row.Player);
                Assert.Equal(l.Points, x.Row.Pts);
                Assert.Equal(l.Rebounds, x.Row.Reb);
                Assert.Equal(l.Fouls, x.Row.Pf);
                Assert.Equal(game.EnergyOf(x.Row.Player), x.Row.Energy, 6);
            }
        }
        // The points on the panels never exceed the team's (the bench has the rest).
        Assert.True(live.HomeFloor.Sum(x => x.Row.Pts) <= r.Home.Points, when);
    }

    // Run 148 (M1): this live-page test drew the retired court (the flat tracker or the perspective arena); the full-screen
    // match court replaced it (Danny's answer 2, #1025). Run148MatchViewTests covers the new live page.



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
