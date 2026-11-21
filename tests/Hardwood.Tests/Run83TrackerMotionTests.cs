using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 83. The 2D tracker's second slice (D67 B, D78 default A): players glide between plays, the ball travels
/// passer → shooter → rim, and a compact live box score sits beside the court. All presentation: nothing here reads or
/// changes the game.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run83TrackerMotionTests
{
    private static double D(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Fact]
    public void Motion_starts_where_the_court_was_and_ends_on_the_plays_frame()
    {
        var league = new WorldGenerator(new Rng(18)).Generate("L", 2026, new DateOnly(2026, 11, 1));
        var live = new GameSimulator().StartLive(league.Teams[2], league.Teams[3], Rules.Nba, 9, new DateOnly(2026, 11, 5));
        TrackerPose? shown = null;
        int seen = 0, assisted = 0, glided = 0;
        while (live.Step())
        {
            var plays = live.Result.Plays;
            for (int idx = seen; idx < plays.Count; idx++)
            {
                var play = plays[idx];
                var to = TrackerLayout.Build(play, idx, live.HomeOnCourt, live.AwayOnCourt, "x");
                Assert.All(to.Markers, m => Assert.NotEqual(0, m.Key));
                var start = TrackerMotion.At(shown, to, 0);
                var end = TrackerMotion.At(shown, to, 1);
                Assert.True(end.Done);
                // It ends exactly on the frame the layout made.
                Assert.Equal(to.Markers.Select(m => m.Spot), end.Markers.Select(m => m.Spot));
                Assert.Equal(to.Ball, end.Ball);
                Assert.Equal(to.From, end.ShotFrom);
                if (to.From != null) Assert.Equal(to.Ball, end.ShotTo);
                if (shown != null)
                {
                    // It starts where the court was: every player still on the floor at his old spot, the ball where it was.
                    Assert.Equal(shown.Ball, start.Ball);
                    Assert.False(start.Done);
                    Assert.Null(start.ShotFrom);   // the shot isn't up yet
                    foreach (var m in start.Markers)
                        if (shown.Markers.FirstOrDefault(o => o.Key == m.Key && o.Home == m.Home) is { } was)
                        {
                            Assert.Equal(was.Spot, m.Spot);
                            if (D(was.Spot, to.Markers.First(x => x.Key == m.Key && x.Home == m.Home).Spot) > 0.5) glided++;
                        }
                    // Halfway through the glide, a moving player is between the two spots, never off the court.
                    var mid = TrackerMotion.At(shown, to, TrackerMotion.GlideShare / 2);
                    Assert.All(mid.Markers, m => { Assert.InRange(m.Spot.X, 0, 94); Assert.InRange(m.Spot.Y, 0, 50); });
                }
                if (play is { Kind: PlayKind.Shot, Made: true, Other: { } assister }
                    && to.Markers.FirstOrDefault(m => m.Key == assister.Id && m.Home == play.Home) is { } passer)
                {
                    // The pass starts at the assister's marker, and the ball passes through him, then the shooter.
                    // (An assister subbed off at the dead ball after the basket isn't drawn, and nor is his pass.)
                    Assert.Equal(passer.Spot, to.Pass);
                    var path = TrackerMotion.BallPath(shown?.Ball ?? to.Ball, to);
                    Assert.Contains(to.Pass!.Value, path);
                    Assert.True(path.ToList().IndexOf(to.Pass!.Value) < path.ToList().IndexOf(to.From!.Value));
                    var poses = Enumerable.Range(0, 201).Select(i => TrackerMotion.At(shown ?? TrackerMotion.Still(to), to, i / 200.0)).ToList();
                    Assert.Contains(poses, p => D(p.Ball, to.Pass!.Value) < 0.6);
                    Assert.Contains(poses, p => p.PassFrom == to.Pass && p.ShotFrom == null);   // passed, not yet shot
                    Assert.Equal(to.Pass, end.PassFrom);
                    Assert.Equal(to.From, end.PassTo);
                    assisted++;
                }
                else Assert.Null(to.Pass);
                shown = end;
            }
            seen = plays.Count;
        }
        Assert.True(assisted > 20, $"assisted {assisted}");
        Assert.True(glided > 200, $"glided {glided}");
        // No previous pose (the first frame, or the game played at Fast): the frame at once.
        var still = TrackerMotion.At(null, TrackerLayout.Build(null, 0, live.HomeOnCourt, live.AwayOnCourt, "x"), 0);
        Assert.True(still.Done);
        Assert.Equal(0, TrackerMotion.DurationFor(60));
        Assert.Equal(225, TrackerMotion.DurationFor(250));
        Assert.Equal(630, TrackerMotion.DurationFor(700));
    }

    [Fact]
    public void The_compact_box_score_adds_up_and_marks_the_five_on_the_floor()
    {
        var league = new WorldGenerator(new Rng(19)).Generate("L", 2026, new DateOnly(2026, 11, 1));
        var live = new GameSimulator().StartLive(league.Teams[4], league.Teams[5], Rules.Nba, 3, new DateOnly(2026, 11, 5));
        for (int i = 0; i < 150; i++) live.Step();
        foreach (var (box, floor) in new[] { (live.Result.Home, live.HomeOnCourt), (live.Result.Away, live.AwayOnCourt) })
        {
            var rows = LiveGameViewModel.BoxRows(box, floor).ToList();
            var total = rows[^1];
            Assert.True(total.Total);
            Assert.Equal(box.Points.ToString(), total.Pts);
            Assert.Equal(box.Points, rows.Take(rows.Count - 1).Sum(r => int.Parse(r.Pts)));
            Assert.Equal($"{box.FGM}-{box.FGA}", total.Fg);
            Assert.Equal(5, rows.Count(r => r.OnCourt));
            Assert.All(floor, p => Assert.Contains(rows, r => r.OnCourt && r.Name.StartsWith(p.Name)));
            // Starters first.
            int firstBench = rows.FindIndex(r => !r.Total && !box.Lines.First(l => r.Name.StartsWith(l.Player.Name)).Started);
            if (firstBench >= 0) Assert.All(rows.Skip(firstBench).Where(r => !r.Total), r => Assert.False(box.Lines.First(l => r.Name.StartsWith(l.Player.Name)).Started));
        }
    }

    // Run 148 (M1): this live-page test drew the retired court (the flat tracker or the perspective arena); the full-screen
    // match court replaced it (Danny's answer 2, #1025). Run148MatchViewTests covers the new live page.

}
