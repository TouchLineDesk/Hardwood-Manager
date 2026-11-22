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

    // Run 152 (M5): the old tracker's motion (TrackerMotion) is removed with the court it drew.

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
