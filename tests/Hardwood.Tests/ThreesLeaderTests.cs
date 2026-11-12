using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 19 (#255): the season threes leader averaged 424-514 (seasons up to 618; NBA 240-402). The engine now bends the
/// three-point tendency above 45, counts shooting skill on threes at 0.75 and runs a shooter off the line after eight
/// attempts in a game.
/// </summary>
public class ThreesLeaderTests
{
    private static EngineConfig Before()
    {
        var c = new EngineConfig
        {
            ThreeSkillShare = 1.0, ThreeRateKnee = 80, ThreeRateAboveKnee = 1.0, ThreeGameKnee = 0, ThreeGameDecay = 0,
        };
        c.ZoneBaseLogit[3] -= 0.05; c.ZoneBaseLogit[4] -= 0.05;
        c.ZoneBaseWeight = new[] { 0.285, 0.19, 0.12, 0.10, 0.30 };
        return c;
    }

    private static (double tpa, double pct, int bigNights) Shooter(EngineConfig cfg)
    {
        var league = new WorldGenerator(new Rng(19)).Generate("Test League", 2026, new DateOnly(2026, 10, 1));
        var home = league.Teams[0];
        var gunner = home.Roster.OrderByDescending(p => p.CurrentAbility).First();
        gunner.Tendencies.ThreePointRate = 80;
        gunner.Attributes[Attr.ThreePoint] = 19; gunner.Attributes[Attr.CatchAndShoot] = 19; gunner.Attributes[Attr.ShotCreation] = 18;
        var sim = new GameSimulator(cfg);
        int att = 0, made = 0, games = 0, big = 0;
        for (int g = 0; g < 60; g++)
        {
            var r = sim.Simulate(home, league.Teams[1 + g % 29], Rules.Nba, 1000UL + (ulong)g);
            var line = r.Home.Lines.First(l => l.Player == gunner);
            if (line.Seconds <= 0) continue;
            games++; att += line.ThreePA; made += line.ThreePM;
            if (line.ThreePM >= 10) big++;
        }
        return (att / (double)games, made / (double)Math.Max(1, att), big);
    }

    [Fact]
    public void An_elite_volume_shooter_takes_and_makes_fewer_threes()
    {
        var before = Shooter(Before());
        var now = Shooter(EngineConfig.Default);
        Assert.True(now.tpa < before.tpa - 1.0, $"3PA a game {before.tpa:0.0} -> {now.tpa:0.0}");
        Assert.True(now.pct < before.pct, $"3P% {before.pct:0.000} -> {now.pct:0.000}");
        Assert.InRange(now.tpa, 7, 13.5);
        Assert.True(now.bigNights <= before.bigNights);
    }

    [Fact]
    public void The_tendency_knee_leaves_ordinary_shooters_alone()
    {
        var c = EngineConfig.Default;
        Assert.True(c.ThreeRateKnee >= 40);
        Assert.InRange(c.ThreeSkillShare, 0.5, 1.0);
        Assert.True(c.ThreeGameKnee >= 6);
    }
}
