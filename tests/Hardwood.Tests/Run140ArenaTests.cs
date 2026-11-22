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

    // Run 152 (M5): the perspective arena's classes (LiveCourt.Arena, CourtProjection) and its Blender frame are removed;
    // the full-screen match court dresses every floor (Run151ArenaDressingTests).

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
