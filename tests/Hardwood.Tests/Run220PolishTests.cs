using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Guide;
using Hardwood.Core.Presentation;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 220, a polish run: K43 (the broadcast agrees with the game) and K37's leftovers (the nagging).</summary>
public class Run220PolishTests
{
    private static PlayEvent P(int hs, int aw, int period) => new(period, 10, PlayKind.Shot, true, null, null, ShotZone.Rim, true, hs, aw);

    [Fact]
    public void K43_an_early_deficit_is_not_a_comeback_and_a_shrunk_lead_is_a_held_off_rally()
    {
        // The critic's opener: the Spurs trailed by 12 in the first quarter, led 96-77 after three, and won 125-119.
        var spurs = GameCalls.Final(new[] { P(10, 22, 1), P(60, 55, 2), P(96, 77, 3), P(125, 119, 4) }, "Spurs", "Seattle", 125, 119, 4);
        Assert.Equal("SPURS HOLD OFF THE RALLY! Final: Spurs 125, Seattle 119", spurs[0]);
        Assert.Equal("Spurs led by 19 and held off a late Seattle rally", spurs[1]);
        Assert.DoesNotContain(spurs, l => l.Contains("came all the way back"));

        // A 10-point hole after halftime is a comeback.
        var back = GameCalls.Final(new[] { P(50, 50, 2), P(70, 82, 3), P(110, 104, 4) }, "Spurs", "Seattle", 110, 104, 4);
        Assert.StartsWith("SPURS COMPLETE THE COMEBACK!", back[0]);
        Assert.Equal("Spurs were down 12 and came all the way back", back[1]);

        // So is a 15-point hole at any time.
        Assert.StartsWith("SPURS COMPLETE THE COMEBACK!", GameCalls.Final(new[] { P(10, 25, 1), P(100, 90, 4) }, "Spurs", "Seattle", 100, 90, 4)[0]);

        // A big lead kept big is just a roll; a 15-point lead won by 7 is a plain win.
        Assert.StartsWith("SPURS ROLL!", GameCalls.Final(new[] { P(60, 40, 2), P(120, 95, 4) }, "Spurs", "Seattle", 120, 95, 4)[0]);
        Assert.StartsWith("SPURS WIN IT!", GameCalls.Final(new[] { P(60, 45, 3), P(107, 100, 4) }, "Spurs", "Seattle", 107, 100, 4)[0]);
        // A 15-point lead in the first half only, then a 4-point win, isn't a held-off late rally.
        Assert.StartsWith("SPURS WIN IT!", GameCalls.Final(new[] { P(30, 15, 1), P(60, 58, 3), P(100, 96, 4) }, "Spurs", "Seattle", 100, 96, 4)[0]);
    }

    [Fact]
    public void K43_the_pro_win_chance_never_reads_as_a_lock_and_follows_the_engine()
    {
        // The critic's opener: a 40-point strength gap read "SEA 1% · SAS 99%".
        double p = StudioShow.ProChance(40);
        Assert.InRange(p, 0.90, 0.97);
        Assert.Equal(0.97, StudioShow.ProChance(200), 6);
        Assert.Equal(0.03, StudioShow.ProChance(-200), 6);
        // An even game leans to the home side by the engine's home edge (about 1.5 points: 57-58%).
        Assert.InRange(StudioShow.ProChance(0), 0.55, 0.60);
        // The college line is unchanged apart from the clamp.
        var (m, sd) = StudioShow.Line(true, 10);
        Assert.Equal(10 * 0.52 + 2.5, m, 6);
        Assert.Equal(9.0, sd, 6);
        Assert.Equal(0.97, StudioShow.Chance(100, 9), 6);
    }

    [Fact]
    public void K37_an_unhappy_player_is_a_standing_reminder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run220-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
            var me = world.College!.Programs.First(t => t.FullName.StartsWith("George Mason"));
            var s = GameSession.NewGame(9, me.Id, Path.Combine(dir, "career.hwsave"));
            var task = new GuideTask("morale-12.34", 15, "A and B are unhappy", "", "Squad");
            var slot = Assistant.ReminderSlot(s, task);
            Assert.NotNull(slot);
            Assert.Contains("morale-12.34", slot);
            Assert.Contains(s.Phase.ToString(), slot);
            Assert.Null(Assistant.ReminderSlot(s, new GuideTask("lineup", 10, "Set a lineup", "", "Rotation")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
