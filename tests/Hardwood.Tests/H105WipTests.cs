using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>H105 work in progress (run 112, not merged): stars should lead the scoring. See claude/runs/run-112.md.</summary>
public class H105WipTests
{
    private readonly ITestOutputHelper _out;
    public H105WipTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void A_player_who_suits_every_role_plays_the_one_that_shoots_most()
    {
        var p = new Player { Id = 1, FirstName = "A", LastName = "B", PrimaryPosition = Position.PG };
        foreach (Attr a in Enum.GetValues<Attr>()) p.Attributes[a] = 20;
        Assert.Equal(Role.ScoringGuard, Roles.Best(p));
        // Without a tie, the best fit still wins: a pure passer stays a Floor General.
        var passer = new Player { Id = 2, FirstName = "C", LastName = "D", PrimaryPosition = Position.PG };
        foreach (Attr a in Enum.GetValues<Attr>()) passer.Attributes[a] = 8;
        foreach (var a in new[] { Attr.Passing, Attr.CourtVision, Attr.Decisions, Attr.BallHandling, Attr.PickAndRollHandler, Attr.BasketballIQ }) passer.Attributes[a] = 20;
        Assert.Equal(Role.FloorGeneral, Roles.Best(passer));
    }

    [Fact]
    public void The_scoring_leaders_are_the_leagues_stars()
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026);
        var rng = new Rng(2026UL ^ 0xABCDEF);
        var schedule = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), rng);
        var outcome = new SeasonSimulator(rng).Run(league, schedule);
        var agg = new Dictionary<Player, (int g, int pts)>();
        foreach (var g in outcome.RegularSeason)
            foreach (var box in new[] { g.Home, g.Away })
                foreach (var l in box.Lines.Where(l => l.Seconds > 0))
                {
                    agg.TryGetValue(l.Player, out var a);
                    agg[l.Player] = (a.g + 1, a.pts + l.Points);
                }
        var top15 = league.Teams.SelectMany(t => t.Roster).OrderByDescending(p => p.CurrentAbility).Take(15).ToHashSet();
        var scorers = agg.Where(x => x.Value.g >= 50).OrderByDescending(x => x.Value.pts / (double)x.Value.g).Select(x => x.Key).ToList();
        foreach (var p in scorers.Take(10)) _out.WriteLine($"{p.Name} CA {p.CurrentAbility}: {agg[p].pts / (double)agg[p].g:F1}");
        // Main had 1 of the top 5 and 3 of the top 10 from the top 15 by ability, with Gilgeous-Alexander 47th (19.2 ppg).
        Assert.True(scorers.Take(5).Count(top15.Contains) >= 3, "most of the top 5 scorers are top-15 players");
        Assert.True(scorers.Take(10).Count(top15.Contains) >= 6, "most of the top 10 scorers are top-15 players");
        Assert.True(scorers.FindIndex(p => p.Name.StartsWith("Shai")) < 5, "Gilgeous-Alexander is near the top");
        double lead = agg[scorers[0]].pts / (double)agg[scorers[0]].g;
        Assert.InRange(lead, 30, 38.5);
    }

}
