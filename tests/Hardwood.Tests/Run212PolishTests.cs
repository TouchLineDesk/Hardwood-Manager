using Hardwood.Core.International;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 212, a polish run. H219: national-team games score as FIBA tournaments do (about 75–85 a side), not like the
/// NBA; the club game on FIBA rules and the NBA are unchanged. (H212's and H213's checks live in Run203CollegePolishTests and
/// the Board's layout tests.)</summary>
public class Run212PolishTests
{
    private readonly ITestOutputHelper _out;
    public Run212PolishTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void H219_a_world_cup_on_a_second_seed_scores_like_fiba()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("Virginia Tech"));
        var s = GameSession.NewGame(2026, me.Id, null);
        var rec = WorldCup.Play(s, 2027)!;
        double ppg = rec.Games.Average(g => g.HomePts + g.AwayPts) / 2.0;
        _out.WriteLine($"seed 2026: {rec.Games.Count} games, {ppg:F1} a side; gold {rec.Gold}");
        Assert.InRange(ppg, 75, 85);
        Assert.InRange(rec.Games.Max(g => Math.Max(g.HomePts, g.AwayPts)), 90, 130); // still room for a big night
    }

    [Fact]
    public void H219_only_national_team_games_use_the_national_rules()
    {
        // The clubs abroad keep FIBA's club rules; the national rules are FIBA's with a neutral court and a slower, harder game.
        Assert.Equal(1.0, Rules.Fiba.PaceScale);
        Assert.False(Rules.Fiba.NeutralSite);
        Assert.True(Rules.FibaNational.NeutralSite);
        Assert.Equal((Rules.Fiba.Periods, Rules.Fiba.MinutesPerPeriod, Rules.Fiba.ShotClockSeconds, Rules.Fiba.FoulsToFoulOut),
            (Rules.FibaNational.Periods, Rules.FibaNational.MinutesPerPeriod, Rules.FibaNational.ShotClockSeconds, Rules.FibaNational.FoulsToFoulOut));
        Assert.True(Rules.FibaNational.PaceScale < 1.0);
    }
}
