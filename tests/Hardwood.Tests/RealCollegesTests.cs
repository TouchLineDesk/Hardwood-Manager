using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.RealWorld;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// R3 (Danny, #233; full Division I, #247): a real-world career's college world is the 365 real Division I programs in
/// their 2026-27 conferences, with generated players; every conference's champion takes an automatic bid.
/// </summary>
public class RealCollegesTests
{
    private static (CollegeWorld World, GameSimulator Sim, Rng Rng) RealWorld(ulong seed = 24)
    {
        var rng = new Rng(seed);
        var gen = new PlayerGenerator(new Rng(seed ^ 0x51), 2_000_000);
        return (CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1), real: true), new GameSimulator(), rng);
    }

    [Fact]
    public void The_data_has_every_division_one_program_with_a_source()
    {
        Assert.Equal(365, RealColleges.Programs.Count);
        Assert.Equal(32, RealColleges.Conferences.Count);
        Assert.Equal(RealColleges.Programs.Count, RealColleges.Programs.Select(p => p.School).Distinct().Count());
        Assert.All(RealColleges.Programs, p =>
        {
            Assert.Contains(RealColleges.Conferences, c => c.Key == p.Conference);
            Assert.Matches("^#[0-9A-F]{6}$", p.Primary);
            Assert.Matches("^#[0-9A-F]{6}$", p.Secondary);
            Assert.Equal(2, p.State.Length);
            Assert.False(string.IsNullOrWhiteSpace(p.Nickname));
        });
        // Every conference can hold an eight-team tournament.
        Assert.All(RealColleges.Conferences, c => Assert.True(RealColleges.Programs.Count(p => p.Conference == c.Key) >= 8, c.Name));
        Assert.Equal(5, RealColleges.Conferences.Count(c => c.Power));
        Assert.Equal("2026-11-12", RealColleges.Checked);
        Assert.Contains("Wikipedia", RealColleges.Source);
        var abbrs = RealColleges.Abbreviations();
        Assert.Equal(abbrs.Count, abbrs.Values.Distinct().Count());
        Assert.All(abbrs.Values, a => Assert.InRange(a.Length, 2, 6));
    }

    [Fact]
    public void Prestige_follows_real_standing()
    {
        double P(string school) => RealColleges.Prestige(RealColleges.Programs.First(p => p.School == school));
        Assert.True(P("Kentucky") > P("Clemson") && P("Clemson") > P("Vermont") && P("Vermont") > P("Le Moyne"));
        Assert.True(P("Gonzaga") > P("Northwestern") - 5, $"{P("Gonzaga")} {P("Northwestern")}");
        Assert.All(RealColleges.Programs, p => Assert.InRange(RealColleges.Prestige(p), 15, 95));
        Assert.InRange(P("Duke"), 85, 95);
    }

    [Fact]
    public void A_real_world_season_crowns_a_champion_with_one_automatic_bid_per_real_conference()
    {
        var (c, sim, rng) = RealWorld();
        Assert.True(c.IsReal);
        Assert.Equal(365, c.Programs.Count);
        Assert.Equal(32, c.Active.Count());
        Assert.All(c.Active, conf => Assert.True(conf.Real));
        Assert.Contains(c.Programs, t => t.City == "Duke" && t.Name == "Blue Devils" && t.Division == "ACC");
        Assert.All(c.Programs, t => Assert.Equal(CollegeWorld.Squad, t.Roster.Count));
        Assert.Equal("NC", CollegeWorld.HomeState(c.Programs.First(t => t.City == "Duke")));
        Assert.Equal("WA", CollegeWorld.HomeState(c.Programs.First(t => t.City == "Gonzaga")));
        c.PlayThrough(new DateOnly(2027, 4, 30), sim, rng);
        var s = c.Season;
        Assert.True(s.Selected);
        Assert.Equal(68, s.Seeds.Count);
        Assert.Equal(32, s.ConferenceChampions.Count);
        Assert.All(s.ConferenceChampions.Values, id => Assert.Contains(id, s.Seeds.Keys));
        Assert.NotNull(s.ChampionId);
        Assert.InRange(c.Programs.Select(t => c.Record(t.Id)).Average(r => r.wins + r.losses), 24, 34);
    }

    [Fact]
    public void Prestige_drifts_back_toward_a_programs_tradition_and_a_fictional_world_is_unchanged()
    {
        var (c, sim, rng) = RealWorld(7);
        var duke = c.Programs.First(t => t.City == "Duke");
        var leMoyne = c.Programs.First(t => t.City == "Le Moyne");
        var dukeConf = CollegeWorld.Conferences.First(x => x.Key == c.ConferenceOf[duke.Id]);
        Assert.Equal(c.Tradition[duke.Id], c.Baseline(duke, dukeConf));
        Assert.True(c.Baseline(duke, dukeConf) > c.Baseline(leMoyne, CollegeWorld.Conferences.First(x => x.Key == c.ConferenceOf[leMoyne.Id])) + 40);
        // The fictional world: ten fictional conferences, no tradition, the old conference levels.
        var gen = new PlayerGenerator(new Rng(9), 3_000_000);
        var f = CollegeWorld.Generate(gen, new Rng(9), new DateOnly(2026, 10, 1));
        Assert.False(f.IsReal);
        Assert.Equal(148, f.Programs.Count);
        Assert.Empty(f.Tradition);
        Assert.DoesNotContain(f.Active, x => x.Real);
    }
}
