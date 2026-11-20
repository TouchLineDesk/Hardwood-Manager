using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// K11 (run 134, critic review #18): a college game has a college box score. More offensive rebounds, turnovers and
/// whistles than the pros, more threes at a lower make rate, and the one-and-one from the 7th team foul in a half
/// (two shots from the 10th). The NBA's settings are untouched.
/// </summary>
public class Run134CollegeEngineTests
{
    private readonly ITestOutputHelper _out;
    public Run134CollegeEngineTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void The_bonus_is_a_one_and_one_from_the_seventh_foul_and_two_shots_from_the_tenth()
    {
        var c = Rules.College;
        Assert.True(c.OneAndOne);
        Assert.Equal(0, c.BonusShots(6));
        Assert.Equal(1, c.BonusShots(7));
        Assert.Equal(1, c.BonusShots(9));
        Assert.Equal(2, c.BonusShots(10));
        Assert.Equal(2, c.BonusShots(14));
        Assert.True(c.FoulsCarryIntoOvertime);
        Assert.True(Rules.CollegeNeutral.OneAndOne);

        // The pros and FIBA: two shots from the 5th, no one-and-one, fouls reset for overtime.
        foreach (var r in new[] { Rules.Nba, Rules.Fiba })
        {
            Assert.False(r.OneAndOne);
            Assert.Equal(0, r.BonusShots(4));
            Assert.Equal(2, r.BonusShots(5));
            Assert.False(r.FoulsCarryIntoOvertime);
        }
    }

    [Fact]
    public void The_pros_play_with_the_same_engine_settings_as_before()
    {
        var cfg = EngineConfig.Default;
        Assert.Same(cfg, cfg.For(Rules.Nba));
        Assert.Same(cfg, cfg.For(Rules.Fiba));
        var col = cfg.For(Rules.College);
        Assert.True(col.OffensiveReboundBase > cfg.OffensiveReboundBase);
        Assert.True(col.TurnoverBase > cfg.TurnoverBase);
        Assert.True(col.NonShootingFoulRate > cfg.NonShootingFoulRate);
        Assert.True(col.ZoneBaseWeight[4] > cfg.ZoneBaseWeight[4]);
        Assert.True(col.ZoneBaseLogit[4] < cfg.ZoneBaseLogit[4]);
        // The clone never writes into the defaults' arrays.
        Assert.Equal(0.30, cfg.ZoneBaseWeight[4]);
        Assert.Equal(-0.715, cfg.ZoneBaseLogit[4]);
    }

    [Fact]
    public void A_conference_style_sits_on_top_of_the_college_profile()
    {
        var style = new ConferenceStyle(1.05, 0.5, 0.8);
        var r = style.Apply(Rules.College);
        Assert.Equal(Rules.College.FoulScale * style.FoulScale, r.FoulScale, 6);
        Assert.Equal(Rules.College.ShotLogitShift + style.ShotLogitShift, r.ShotLogitShift, 6);
        Assert.Equal(Rules.College.OffRebShift, r.OffRebShift);
        Assert.Equal(10, r.DoubleBonusFouls);
    }

    [Fact]
    public void College_games_have_a_college_box_score()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026);
        var cw = world.College!;
        var games = cw.Season.Games.Where(CollegeWorld.IsRegularSeason).Take(260).ToList();
        var sim = new GameSimulator(); var rng = new Rng(134);
        double pts = 0, fga = 0, tpa = 0, fta = 0, orb = 0, drb = 0, tov = 0, pf = 0, poss = 0; int sides = 0, badMinutes = 0;
        foreach (var g in games)
        {
            var r = sim.Simulate(cw.Program(g.HomeId)!, cw.Program(g.AwayId)!, cw.GameRules(g), rng.NextULong());
            // The review's catch: the late fouls before the bonus burn clock, and every second of it is someone's minutes.
            double want = 5 * (40 * 60 + r.Overtimes * 5 * 60);
            foreach (var b in new[] { r.Home, r.Away }) if (Math.Abs(b.Lines.Sum(l => l.Seconds) - want) > 1) badMinutes++;
            foreach (var (b, o) in new[] { (r.Home, r.Away), (r.Away, r.Home) })
            {
                sides++; pts += b.Points; fga += b.FGA; tpa += b.ThreePA; fta += b.FTA; tov += b.Turnovers; pf += b.Fouls;
                orb += b.OReb; drb += o.DReb; poss += b.FGA - b.OReb + b.Turnovers + 0.44 * b.FTA;
            }
        }
        Assert.True(badMinutes == 0, $"{badMinutes} team-games whose minutes don't add up to 200 (plus overtime)");
        double orbPct = 100 * orb / (orb + drb), tovPct = 100 * tov / poss, ftRate = 100 * fta / fga, threeRate = 100 * tpa / fga, ppg = pts / sides, fouls = pf / sides;
        string line = $"ORB% {orbPct:0.0} · TOV% {tovPct:0.0} · FTA/FGA {ftRate:0.0} · 3PA rate {threeRate:0.0} · points {ppg:0.0} · fouls {fouls:0.0}";
        _out.WriteLine(line);
        // D-I norms (critic review #18): ORB% 29-30, TOV% 17-18, FTA/FGA 32-34, 3PA rate 39-40, about 72 a side, 17-18 fouls.
        // Before K11: ORB% 19.1, TOV% 13.3, FTA/FGA 24.6, 3PA rate 31.3, fouls 12.2 (the NBA's shape).
        Assert.InRange(orbPct, 26.5, 32.5);
        Assert.InRange(tovPct, 15.5, 19.5);
        Assert.InRange(ftRate, 28, 37);
        Assert.InRange(threeRate, 36, 43);
        Assert.InRange(ppg, 68, 77);
        Assert.InRange(fouls, 14.5, 19.5);
    }
}
