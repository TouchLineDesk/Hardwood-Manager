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

/// <summary>Run 80. The 2D tracker, first slice (D67 B): the engine records each play as data beside its commentary
/// (no random numbers, no second simulator, #595), and the live game's court draws ten players and the ball for the
/// latest play, with a Step control while paused.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run80TrackerTests
{
    [Fact]
    public void Recorded_plays_agree_with_the_box_score_and_change_no_result()
    {
        var league = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        foreach (ulong seed in new ulong[] { 4242, 77, 2026 })
        {
            var quick = sim.Simulate(league.Teams[0], league.Teams[1], Rules.Nba, seed, keepLog: false);
            var logged = sim.Simulate(league.Teams[0], league.Teams[1], Rules.Nba, seed, keepLog: true);
            Assert.Empty(quick.Plays);
            // Same game with or without the record.
            Assert.Equal(quick.Home.Points, logged.Home.Points);
            Assert.Equal(quick.Away.Points, logged.Away.Points);
            Assert.Equal(quick.Home.FGA, logged.Home.FGA);
            Assert.Equal(quick.Away.Turnovers, logged.Away.Turnovers);

            var plays = logged.Plays;
            foreach (var box in new[] { logged.Home, logged.Away })
            {
                bool home = box == logged.Home;
                var mine = plays.Where(p => p.Home == home).ToList();
                Assert.Equal(box.FGA, mine.Count(p => p.Kind == PlayKind.Shot));
                Assert.Equal(box.FGM, mine.Count(p => p.Kind == PlayKind.Shot && p.Made));
                Assert.Equal(box.FTA, mine.Count(p => p.Kind == PlayKind.FreeThrow));
                Assert.Equal(box.FTM, mine.Count(p => p.Kind == PlayKind.FreeThrow && p.Made));
                Assert.Equal(box.Steals, mine.Count(p => p.Kind == PlayKind.Steal));
                Assert.Equal(box.Blocks, plays.Count(p => p.Blocked && p.Home != home));
                Assert.Equal(box.OReb, mine.Count(p => p.Kind == PlayKind.OffensiveRebound));
                Assert.Equal(box.DReb, mine.Count(p => p.Kind == PlayKind.DefensiveRebound));
                Assert.Equal(box.Assists, mine.Count(p => p.Kind == PlayKind.Shot && p.Made && p.Other != null));
                Assert.Equal(box.Fouls, plays.Count(p => p.Kind == PlayKind.Foul && p.Home == home));
            }
            Assert.Equal(logged.Periods + logged.Overtimes, plays.Count(p => p.Kind == PlayKind.PeriodEnd));
            Assert.Equal(logged.Home.Points, plays[^1].HomeScore);
            Assert.Equal(logged.Away.Points, plays[^1].AwayScore);
            // The clock only runs down within a period.
            for (int i = 1; i < plays.Count; i++)
                if (plays[i].Period == plays[i - 1].Period) Assert.True(plays[i].Clock <= plays[i - 1].Clock + 1e-9);
        }
    }

    // Run 152 (M5): the old tracker (TrackerLayout, the headline play) is removed; the possession choreographer draws
    // every play now (Run148MatchViewTests, Run149ChoreographyTests).

    // Run 148 (M1): this live-page test drew the retired court (the flat tracker or the perspective arena); the full-screen
    // match court replaced it (Danny's answer 2, #1025). Run148MatchViewTests covers the new live page.

}
