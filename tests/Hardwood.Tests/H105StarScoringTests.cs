using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>H105 (runs 112-113): the NBA's scoring leaders are its stars, not role players on thin teams.</summary>
public class H105StarScoringTests
{
    private readonly ITestOutputHelper _out;
    public H105StarScoringTests(ITestOutputHelper o) => _out = o;

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
    public void The_soft_cap_trims_only_the_top_share_and_keeps_part_of_the_excess()
    {
        var caps = new[] { 0.30, 0.30, 0.30, 0.30, 0.30 };
        var w = new[] { 6.0, 1, 1, 1, 1 };                 // 60% of the shots
        GameSimulator.SoftCapShare(w, caps, 0.25);
        double share = w[0] / w.Sum();
        Assert.Equal(0.30 + 0.30 * 0.25, share, 6);          // 0.375: the cap plus a quarter of the excess
        Assert.Equal(new[] { 1.0, 1, 1, 1 }, w.Skip(1));     // the others' weights are untouched
        var even = new[] { 1.0, 1, 1, 1, 1 };
        GameSimulator.SoftCapShare(even, caps, 0.25);
        Assert.All(even, x => Assert.Equal(1.0, x));         // under the cap: nothing changes
        var star = new[] { 6.0, 1, 1, 1, 1 };
        GameSimulator.SoftCapShare(star, new[] { 0.35, 0.30, 0.30, 0.30, 0.30 }, 0.25);
        Assert.True(star[0] / star.Sum() > share, "a star's higher cap leaves him more of the shots");
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
        // v0.108.1 had 1 of the top 5 and 3 of the top 10 from the top 15 by ability, with Gilgeous-Alexander 47th (19.2 ppg).
        // v0.108.2 (run 113): 4 of 5, 7 of 10, Gilgeous-Alexander first (30.3).
        Assert.True(scorers.Take(5).Count(top15.Contains) >= 3, "most of the top 5 scorers are top-15 players");
        Assert.True(scorers.Take(10).Count(top15.Contains) >= 6, "most of the top 10 scorers are top-15 players");
        Assert.True(scorers.FindIndex(p => p.Name.StartsWith("Shai")) < 5, "Gilgeous-Alexander is near the top");
        double lead = agg[scorers[0]].pts / (double)agg[scorers[0]].g;
        Assert.InRange(lead, 30, 38.5);
    }

    /// <summary>A tuning probe (runs only with HWT_PROBE set): seasons on HWT_PROBE's seeds, the scoring top 10.</summary>
    [Fact]
    public void Probe()
    {
        var seeds = Environment.GetEnvironmentVariable("HWT_PROBE");
        if (string.IsNullOrEmpty(seeds)) return;
        foreach (var seed in seeds.Split(',').Select(ulong.Parse))
        {
            var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026);
            var rng = new Rng(seed ^ 0xABCDEF);
            var schedule = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), rng);
            var outcome = new SeasonSimulator(rng).Run(league, schedule);
            var agg = new Dictionary<Player, (int g, int pts)>();
            foreach (var g in outcome.RegularSeason)
                foreach (var box in new[] { g.Home, g.Away })
                    foreach (var l in box.Lines.Where(l => l.Seconds > 0))
                    { agg.TryGetValue(l.Player, out var a); agg[l.Player] = (a.g + 1, a.pts + l.Points); }
            var top15 = league.Teams.SelectMany(t => t.Roster).OrderByDescending(p => p.CurrentAbility).Take(15).ToHashSet();
            var sc = agg.Where(x => x.Value.g >= 50).OrderByDescending(x => x.Value.pts / (double)x.Value.g).Select(x => x.Key).ToList();
            double Ppg(Player p) => agg[p].pts / (double)agg[p].g;
            int Rank(string n) => sc.FindIndex(p => p.Name.Contains(n)) + 1;
            _out.WriteLine($"PROBE seed {seed}: top5 {sc.Take(5).Count(top15.Contains)}/5 top10 {sc.Take(10).Count(top15.Contains)}/10 lead {Ppg(sc[0]):F1} SGA#{Rank("Gilgeous")} Doncic#{Rank("Don")} Jokic#{Rank("Joki")} Giannis#{Rank("Antetok")}");
            _out.WriteLine("PROBE   " + string.Join(", ", sc.Take(10).Select(p => $"{p.LastName} {p.CurrentAbility} {Ppg(p):F1}")));
        }
    }
}
