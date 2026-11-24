using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Presentation;
using Hardwood.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 186 (critic review #23, K34 and K25's leftovers): a college bench doesn't spend its last timeouts on a
/// "breather" in the last 4:00 of a one-possession game, and keeps one for the last minute; the end of a game reads once.</summary>
public class Run186LateTimeoutTests
{
    private static readonly DateOnly Day = new(2026, 11, 5);
    private readonly ITestOutputHelper _out;
    public Run186LateTimeoutTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Close_college_games_keep_a_timeout_for_the_last_minute_and_never_call_a_late_breather()
    {
        var league = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var sim = new GameSimulator();
        int lateCalls = 0, kept = 0, twins = 0;
        for (int g = 0; g < 200; g++)
        {
            var r = sim.Simulate(league.Teams[g % 10], league.Teams[10 + g % 10], Rules.College, (ulong)(1860 + g), keepLog: true, date: Day);
            foreach (var t in r.Timeouts)
            {
                if (t.Period < Rules.College.Periods || t.Clock > 240) continue;
                if (Math.Abs(t.HomeScore - t.AwayScore) > 3) continue;
                lateCalls++;
                Assert.NotEqual(TimeoutReason.Breather, t.Reason);                       // no "breather" under 4:00 within 3
                if (t.Reason is TimeoutReason.LatePlay or TimeoutReason.StopClock or TimeoutReason.SetDefense && t.Clock > 60
                    && t.Period == Rules.College.Periods)
                {
                    Assert.True(t.Left >= 1, $"a tired five's timeout at {t.Clock:0} s of a close game left {t.Left}");
                    kept++;
                }
            }
            // The play-by-play never reads "X fouls X" with one surname (K25's leftover).
            foreach (var line in r.PlayLog)
            {
                Assert.DoesNotMatch(@"\b([A-Z][\w'-]+) fouls \1\b", line);
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\. (\S+) fouls .\. \1")) twins++;
            }
        }
        _out.WriteLine($"200 college games: {lateCalls} timeouts in the last 4:00 within 3; {kept} tired-five calls before the last minute kept one; {twins} same-surname fouls told apart");
        Assert.True(lateCalls > 0);
        Assert.True(kept > 0, "some tired-five calls came before the last minute");
    }

    [Fact]
    public void A_rebound_at_the_horn_has_no_outlet_line()
    {
        var l = new WorldGenerator(new Rng(17)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var h = l.Teams[0]; var a = l.Teams[1];
        h.Tactic = new Tactic(); a.Tactic = new Tactic();
        List<Player> Five(Team t) => t.Roster.OrderByDescending(p => p.Overall).Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();
        var hf = Five(h); var af = Five(a);
        PossessionInput Input(List<PlayEvent> ev, double start, double end) =>
            new(9, 2026, ev, start, end, hf, af, hf.Concat(af).Select((p, i) => (p, i)).ToDictionary(x => x.p, x => new Spot(47 + (x.i < 5 ? -4 : 4), 10 + (x.i % 5) * 7)),
                0, 0, true, "Home", "Away", DefensiveScheme.Man, DefensiveScheme.Man, PossessionStart.DeadBall, 2, false) { HomeSystem = OffensiveSystem.Motion };
        // The miss, the rebound by a big, then the horn: the horn's line follows the rebound, no outlet in between.
        var horn = new List<PlayEvent>
        {
            new(2, 1.5, PlayKind.Shot, true, hf[1], null, ShotZone.LongMid, false, 71, 73),
            new(2, 0.4, PlayKind.DefensiveRebound, false, af[4], null, ShotZone.Rim, false, 71, 73),
            new(2, 0, PlayKind.PeriodEnd, true, null, null, ShotZone.Rim, false, 71, 73),
        };
        var c = PossessionChoreographer.Build(Input(horn, 10, 0));
        Assert.DoesNotContain(c.Lines, x => x.Text.Contains("outlet", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(c.Lines, x => x.Text.StartsWith("That's the end of"));
        // With time left, the outlet is still said.
        var mid = new List<PlayEvent>
        {
            new(1, 990, PlayKind.Shot, true, hf[1], null, ShotZone.LongMid, false, 0, 0),
            new(1, 989, PlayKind.DefensiveRebound, false, af[4], null, ShotZone.Rim, false, 0, 0),
        };
        Assert.Contains(PossessionChoreographer.Build(Input(mid, 1010, 989)).Lines, x => x.Text.Contains("utlet"));
    }
}
