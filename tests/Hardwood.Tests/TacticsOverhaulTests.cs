using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Xunit;

namespace Hardwood.Tests;

/// <summary>The Tactics overhaul (Danny, #475, D60 A): the play book, the suggested setups and the matchup board.</summary>
public class TacticsOverhaulTests
{
    [Fact]
    public void Every_system_has_a_play_that_stays_on_the_half_court()
    {
        foreach (var s in Enum.GetValues<OffensiveSystem>())
        {
            var beats = PlayBook.For(s);
            Assert.True(beats.Count >= 3, $"{s}: {beats.Count} beats");
            foreach (var b in beats)
            {
                Assert.Equal(5, b.X.Length); Assert.Equal(5, b.Y.Length);
                Assert.All(b.X, x => Assert.InRange(x, 0, 50));
                Assert.All(b.Y, y => Assert.InRange(y, 0, 47));
                Assert.InRange(b.Ball, 0, 4);
                Assert.InRange(b.Screener, -1, 4);
                Assert.False(string.IsNullOrWhiteSpace(b.Caption));
            }
            // The ball moves at least once and somebody moves every beat: it's a play, not a still.
            Assert.True(beats.Select(b => b.Ball).Distinct().Count() > 1 || s == OffensiveSystem.Isolation || s == OffensiveSystem.PickAndRoll, $"{s}: the ball never moves");
            for (int i = 1; i < beats.Count; i++)
                Assert.True(Enumerable.Range(0, 5).Any(p => beats[i].X[p] != beats[i - 1].X[p] || beats[i].Y[p] != beats[i - 1].Y[p]), $"{s} beat {i}: nobody moves");
        }
    }

    [Fact]
    public void A_zone_holds_its_shape_and_man_defenders_stay_between_their_man_and_the_basket()
    {
        var b = PlayBook.For(OffensiveSystem.Motion)[0];
        var zone = PlayBook.Defence(DefensiveScheme.Zone23, b.X, b.Y, 25, 30);
        Assert.Equal(5, zone.Length);
        Assert.True(zone.Count(d => d.y < 12) >= 3, "three across the paint");
        var man = PlayBook.Defence(DefensiveScheme.Man, b.X, b.Y, 25, 30);
        for (int i = 0; i < 5; i++)
        {
            double dMan = Dist(b.X[i], b.Y[i]), dDef = Dist(man[i].x, man[i].y);
            Assert.True(dDef < dMan, "a man defender sits nearer the basket than his man");
        }
        var drop = PlayBook.Defence(DefensiveScheme.Drop, b.X, b.Y, 25, 30);
        Assert.True(Dist(drop[4].x, drop[4].y) < Dist(man[4].x, man[4].y), "in drop the big sags further");
        static double Dist(double x, double y) => Math.Sqrt((x - PlayBook.BasketX) * (x - PlayBook.BasketX) + (y - PlayBook.BasketY) * (y - PlayBook.BasketY));
    }

    [Fact]
    public void Suggested_setups_cover_the_named_plans_and_apply_cleanly()
    {
        var lg = RealWorldData.NewWorld(31).League;
        var us = lg.Teams.First(t => t.Abbreviation == "BOS"); var them = lg.Teams.First(t => t.Abbreviation == "LAL");
        var setups = TacticSetups.Suggest(us, them, null);
        var keys = setups.Select(s => s.Key).ToList();
        foreach (var k in new[] { "fit", "plan", "five", "threes", "inside", "lock", "star", "move" }) Assert.Contains(k, keys);
        Assert.Single(setups, s => s.Recommended);
        Assert.Equal(setups.Max(s => s.Score), setups.Single(s => s.Recommended).Score);
        Assert.All(setups, s => Assert.InRange(s.Score, 0, 1));
        Assert.Contains("Lakers", setups.Single(s => s.Key == "plan").Title);
        // Without a next game there's no game-plan card and no edge.
        Assert.DoesNotContain(TacticSetups.Suggest(us, null, null), s => s.Key == "plan" || s.Edge != 0);

        // Applying a card sets the system, scheme and sliders, and leaves roles, the stopper and presets alone.
        var tac = us.Tactic;
        var role = new KeyValuePair<int, Role>(us.Roster[0].Id, Role.GlueGuy);
        tac.Roles[role.Key] = role.Value; tac.StarStopperId = us.Roster[1].Id; tac.Presets.Add(new TacticPreset { Name = "Mine" });
        var threes = setups.Single(s => s.Key == "threes");
        threes.ApplyTo(tac);
        Assert.True(threes.Matches(tac));
        Assert.Equal(OffensiveSystem.PaceAndSpace, tac.System);
        Assert.Equal(82, tac.ThreePointEmphasis);
        Assert.Equal(Role.GlueGuy, tac.Roles[role.Key]);
        Assert.Equal(us.Roster[1].Id, tac.StarStopperId);
        Assert.Single(tac.Presets);
        Assert.False(setups.Single(s => s.Key == "inside").Matches(tac));
    }

    [Fact]
    public void The_matchup_board_lines_up_five_starters_a_side()
    {
        var lg = RealWorldData.NewWorld(31).League;
        var us = lg.Teams.First(t => t.Abbreviation == "BOS"); var them = lg.Teams.First(t => t.Abbreviation == "LAL");
        var board = TacticSetups.Board(us, them, null);
        Assert.Equal(5, board.Count);
        Assert.Equal(Enum.GetValues<Position>(), board.Select(b => b.Position).ToArray());
        Assert.All(board, b => { Assert.NotNull(b.Ours); Assert.NotNull(b.Theirs); Assert.Contains(b.Ours!, us.Roster); Assert.Contains(b.Theirs!, them.Roster); });
        Assert.Equal(5, board.Select(b => b.Ours!.Id).Distinct().Count());
        Assert.Equal(board[0].Ours!.CurrentAbility - board[0].Theirs!.CurrentAbility, board[0].Edge);
    }
}
