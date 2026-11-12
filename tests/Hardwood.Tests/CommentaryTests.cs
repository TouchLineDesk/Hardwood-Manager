using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Match day part 3: the commentary reads well, tells the truth, and never changes a result.</summary>
public class CommentaryTests
{
    private static readonly League World = new WorldGenerator(new Rng(91)).Generate("L", 2026, new DateOnly(2026, 10, 1));

    [Fact]
    public void Commentary_never_changes_a_result()
    {
        var sim = new GameSimulator();
        for (ulong seed = 1; seed <= 8; seed++)
        {
            // Each game puts both teams on film (#267), so start both runs from the same tactics state.
            World.Teams[0].Tactic = new Tactic(); World.Teams[1].Tactic = new Tactic();
            var quiet = sim.Simulate(World.Teams[0], World.Teams[1], Rules.Nba, seed, keepLog: false);
            World.Teams[0].Tactic = new Tactic(); World.Teams[1].Tactic = new Tactic();
            var loud = sim.Simulate(World.Teams[0], World.Teams[1], Rules.Nba, seed, keepLog: true);
            Assert.Equal(quiet.Home.Points, loud.Home.Points);
            Assert.Equal(quiet.Away.Points, loud.Away.Points);
            Assert.Equal(quiet.Home.Assists, loud.Home.Assists);
        }
    }

    [Fact]
    public void Each_period_gets_a_recap_that_matches_the_scores()
    {
        var sim = new GameSimulator();
        for (ulong seed = 10; seed < 16; seed++)
        {
            var r = sim.Simulate(World.Teams[2], World.Teams[3], Rules.Nba, seed, keepLog: true);
            var recaps = r.PlayLog.Where(l => l.Contains(" goes to ") || l.Contains(" is even")).ToList();
            Assert.Equal(r.Home.PointsByPeriod.Length, recaps.Count);
            for (int p = 0; p < recaps.Count; p++)
            {
                int h = r.Home.PointsByPeriod[p], a = r.Away.PointsByPeriod[p];
                string expected = h == a ? $"{h}-{a}" : h > a ? $"goes to {r.Home.Team.Abbreviation}, {h}-{a}" : $"goes to {r.Away.Team.Abbreviation}, {a}-{h}";
                Assert.Contains(expected, recaps[p]);
            }
        }
    }

    [Fact]
    public void Calls_name_the_scorer_and_milestones_are_true()
    {
        var r = new GameSimulator().Simulate(World.Teams[4], World.Teams[5], Rules.Nba, 33, keepLog: true);
        var lines = r.Home.Lines.Concat(r.Away.Lines).ToList();
        var verbs = new[] { "buries", "knocks down", "drills", "downtown", "splashes", "finishes", "lays it in", "attacks the basket", "gets to the rim", "hits the jumper", "pulls up", "mid-range", "rises and hits", "sinks" };
        var calls = r.PlayLog.Where(l => verbs.Any(l.Contains)).ToList();
        Assert.True(calls.Count >= 40, $"{calls.Count} calls");
        // Every "has 20" or "30 for" belongs to a player who finished with at least that many.
        foreach (var l in r.PlayLog.Where(l => l.Contains(" has 20.") || l.Contains("That's 30 for")))
        {
            int need = l.Contains("has 20") ? 20 : 30;
            Assert.Contains(lines, x => l.Contains($"{x.Player.LastName} has 20.") || l.Contains($"That's 30 for {x.Player.LastName}") ? x.Points >= need : false);
        }
        // Several different phrasings in one game.
        Assert.True(verbs.Count(v => calls.Any(c => c.Contains(v))) >= 6);
    }
}
