using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Names;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// The real world, slice R2 part 1 (decisions #204-#209): the NBA's real players on their real teams, rated from their
/// real stats. Runs with the name-pack tests because a real-world career changes the static top-league name.
/// </summary>
[Collection(NamePackCollection.Name)]
public class RealPlayersTests
{
    [Fact]
    public void The_pack_holds_every_player_who_has_played_this_season_with_its_source()
    {
        var lines = RealPlayers.Lines;
        Assert.InRange(lines.Count, 440, 480);
        var abbrs = RealWorldData.Teams.Select(t => t.Abbr).ToHashSet();
        Assert.All(lines, l => Assert.Contains(l.Team, abbrs));
        Assert.Equal(lines.Count, lines.Select(l => l.Name).Distinct().Count());
        Assert.All(abbrs, a => Assert.InRange(lines.Count(l => l.Team == a), 13, 20));
        Assert.Contains(lines, l => l.Name == "Nikola Jokić" && l.Team == "DEN" && l.Pos == "C");
        Assert.Contains(lines, l => l.Name == "Giannis Antetokounmpo" && l.Team == "MIA");
        Assert.Equal(30, RealPlayers.LastSeason.Count);
        Assert.Equal(30 * 41, RealPlayers.LastSeason.Sum(r => r.W));
        Assert.Contains(RealPlayers.LastSeason, r => r.Team == "OKC" && r.W == 64);
        Assert.Equal("2026-11-12", RealPlayers.Checked);
        Assert.Contains("Basketball-Reference", RealPlayers.Source);
    }

    [Fact]
    public void A_real_career_fields_the_real_players_on_the_generated_talent_ladder()
    {
        try
        {
            var fictional = GameSession.NewGame(2026, 1, null);
            var real = GameSession.NewGame(2026, 16, null, realWorld: true);
            var names = RealPlayers.Lines.Select(l => l.Name).ToHashSet();
            Assert.All(real.League.Teams, t =>
            {
                Assert.Equal(15, t.Roster.Count);
                Assert.InRange(t.Roster.Count(p => names.Contains(p.Name)), 13, 15);
            });
            var den = real.League.Teams.First(t => t.Abbreviation == "DEN");
            Assert.Same(den, real.HumanTeam);
            var jokic = den.Roster.Single(p => p.Name == "Nikola Jokić");
            Assert.Equal(("Serbia", Position.C), (jokic.Nationality, jokic.PrimaryPosition));
            Assert.InRange(jokic.CurrentAbility, 165, 190);
            Assert.InRange(jokic.Age(new DateOnly(2026, 10, 1)), 30, 31);
            // Everyone has a valid contract and a unique id; payrolls are the real ones (R2 part 2a, #217), so most
            // clubs open over the cap, as in the NBA, and every owner's wage budget covers what his club already pays.
            var all = real.League.Teams.SelectMany(t => t.Roster).ToList();
            Assert.Equal(all.Count, all.Select(p => p.Id).Distinct().Count());
            Assert.All(all, p => Assert.True(p.Contract!.Salary >= real.League.Finance.MinSalary));
            Assert.All(real.League.Teams, t => Assert.InRange(t.Roster.Sum(p => p.Contract!.Salary), 100_000_000, 240_000_000));
            Assert.All(real.League.Teams, t => Assert.True(t.Money.WageBudget >= t.Roster.Sum(p => p.Contract!.Salary)));
            // Real contracts, birthdays and heights (Basketball-Reference, checked Nov 12, 2026).
            Assert.Equal((59_030_000L, 2), (jokic.Contract!.Salary, jokic.Contract.Years));
            Assert.Equal(new DateOnly(1995, 2, 19), jokic.BirthDate);
            Assert.Equal(211, jokic.HeightCm);
            var shai = real.League.Teams.First(t => t.Abbreviation == "OKC").Roster.Single(p => p.Name == "Shai Gilgeous-Alexander");
            Assert.Equal((40_810_000L, 5, "Canada"), (shai.Contract!.Salary, shai.Contract.Years, shai.Nationality));
            var tatum = real.League.Teams.First(t => t.Abbreviation == "BOS").Roster.Single(p => p.Name == "Jayson Tatum");
            Assert.Equal(new DateOnly(1998, 3, 3), tatum.BirthDate);
            var towns = real.League.Teams.First(t => t.Abbreviation == "NYK").Roster.Single(p => p.Name == "Karl-Anthony Towns");
            Assert.Equal("Dominican Republic", towns.Nationality);
            Assert.Equal("Joe Mazzulla", real.League.Teams.First(t => t.Abbreviation == "BOS").CoachName);
            Assert.Equal("Mike Brown", real.League.Teams.First(t => t.Abbreviation == "NYK").CoachName);
            // The talent ladder is the generator's: the same spread of CA as a fictional league from the same seed.
            double Mean(GameSession s) => s.League.Teams.SelectMany(t => t.Roster).Average(p => p.CurrentAbility);
            int Stars(GameSession s) => s.League.Teams.SelectMany(t => t.Roster).Count(p => p.CurrentAbility >= 160);
            Assert.InRange(Mean(real) - Mean(fictional), -2.0, 2.0);
            Assert.InRange(Stars(real), Stars(fictional) - 4, Stars(fictional) + 4);
        }
        finally { Brand.Real = false; }
    }

    [Fact]
    public void Real_stars_are_priced_like_stars()
    {
        // Critic review #8: generated deals put the MVP at "Would listen, $17.6-22.1M". With his real five-season deal
        // the club wants what a franchise player costs, and no star under 31 on a deal of two or more years is cheap.
        try
        {
            var league = RealCheck.Build(2026);
            var on = new DateOnly(2026, 10, 1);
            var rows = league.Teams.SelectMany(t => t.Roster.Select(p => (t, p, ask: TransferMarket.Ask(p, t, on, 1, league.Finance)))).ToList();
            var shai = rows.Single(x => x.p.Name == "Shai Gilgeous-Alexander");
            Assert.True(shai.ask >= 80_000_000, $"SGA ${shai.ask / 1e6:F1}M");
            Assert.Equal("Won't sell", TransferMarket.Stance(shai.p, shai.t).Label);
            var stars = rows.Where(x => x.p.CurrentAbility >= 165 && x.p.Age(on) <= 30 && x.p.Contract!.Years >= 2).ToList();
            Assert.NotEmpty(stars);
            Assert.All(stars, x => Assert.True(x.ask >= 60_000_000, $"{x.p.Name} ${x.ask / 1e6:F1}M"));
        }
        finally { Brand.Real = false; }
    }

    [Fact]
    public void Team_strength_tracks_last_seasons_real_records()
    {
        try
        {
            var league = RealCheck.Build(2026);
            var last = RealPlayers.LastSeason.ToDictionary(r => r.Team);
            var str = league.Teams.Select(RealCheck.Strength).ToList();
            var wins = league.Teams.Select(t => (double)last[t.Abbreviation].W).ToList();
            Assert.True(RealCheck.Correlation(str, wins) >= 0.8, $"r = {RealCheck.Correlation(str, wins):F2}");
            // The best-rated players are real stars, not small-sample flukes.
            var top10 = league.Teams.SelectMany(t => t.Roster).OrderByDescending(p => p.CurrentAbility).Take(10).Select(p => p.Name).ToList();
            Assert.Contains("Shai Gilgeous-Alexander", top10);
            Assert.Contains("Nikola Jokić", top10);
            Assert.DoesNotContain("Ty Jerome", top10); // 26.7 points in his first nine games
        }
        finally { Brand.Real = false; }
    }

    [Fact]
    public void Attributes_follow_what_the_numbers_show_and_keep_the_mean_his_ability_implies()
    {
        var rated = RealPlayers.Rate().ToDictionary(r => r.Line.Name);
        Player Make(string name, int ca) => RealPlayers.Build(rated[name], ca, 1, new Rng(9), new DateOnly(2027, 2, 1));
        var curry = Make("Stephen Curry", 160);
        var gobert = Make("Rudy Gobert", 140);
        var jokic = Make("Nikola Jokić", 180);
        Assert.True(curry.Attributes[Attr.ThreePoint] >= 16, $"Curry 3PT {curry.Attributes[Attr.ThreePoint]}");
        Assert.True(curry.Attributes[Attr.FreeThrow] >= 16);
        Assert.True(gobert.Attributes[Attr.ThreePoint] < curry.Attributes[Attr.ThreePoint] - 6);
        Assert.True(gobert.Attributes[Attr.DefensiveRebounding] >= 15);
        Assert.True(gobert.Tendencies.ThreePointRate <= 5);
        Assert.True(curry.Tendencies.ThreePointRate >= 50);
        Assert.True(jokic.Attributes[Attr.Passing] >= 17, $"Jokic passing {jokic.Attributes[Attr.Passing]}");
        foreach (var (p, ca) in new[] { (curry, 160), (gobert, 140), (jokic, 180) })
            Assert.InRange(AttrInfo.All.Average(a => (double)p.Attributes[a]), PlayerGenerator.MeanAttributeFor(ca) - 0.8, PlayerGenerator.MeanAttributeFor(ca) + 0.8);
    }
}
