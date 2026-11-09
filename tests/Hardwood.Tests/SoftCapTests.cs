using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>The NABL's NBA-style soft cap (decision #52): Bird rights, cap room, the mid-level exception, minimum deals, the apron, trades.</summary>
public class SoftCapTests
{
    private static League NewLeague(ulong seed = 3) => new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));

    private static (League league, Offseason off) OffseasonAtFreeAgency()
    {
        var league = NewLeague();
        var rng = new Rng(21);
        var outcome = new SeasonSimulator(rng).Run(league, ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), rng));
        var off = new Offseason(league, outcome, new Dictionary<int, PlayerSeasonStats>(), rng);
        off.Begin();
        return (league, off);
    }

    private static void SetPayroll(Team team, long target)
    {
        long each = target / team.Roster.Count;
        foreach (var p in team.Roster) p.Contract!.Salary = each;
    }

    [Fact]
    public void The_cap_tax_line_apron_and_mid_level_follow_nba_ratios()
    {
        var fin = Finance.HardCapDefault;
        Assert.True(fin.SoftCap);
        Assert.Equal(165_000_000, fin.SalaryCap);
        Assert.Equal(200_500_000, fin.TaxLine);
        Assert.Equal(221_800_000, fin.Apron);
        Assert.Equal(15_000_000, fin.MidLevel);
        Assert.Equal(0, fin.TaxBill(fin.TaxLine));
        Assert.Equal(15_000_000, fin.TaxBill(fin.TaxLine + 10_000_000));
        var hard = fin with { SoftCap = false };
        Assert.Equal(hard.SalaryCap, hard.Apron);
    }

    [Fact]
    public void Bird_rights_cap_room_mid_level_and_minimum_deals()
    {
        var (league, off) = OffseasonAtFreeAgency();
        var fin = league.Finance;
        var team = league.Teams[2];

        // Bird rights: re-signing your own player may go over the cap, but not past the apron.
        var expiring = off.Expiring[team.Id].FirstOrDefault() ?? team.Roster[0];
        long committed = off.CommittedPayroll(team) + off.ReservedForDraft(team);
        long overCap = fin.SalaryCap - committed + 5_000_000;
        Assert.True(off.CanAfford(team, overCap));
        Assert.False(off.CanAfford(team, fin.Apron - committed + 1));

        off.AdvanceToDraft();
        off.AdvanceToFreeAgency();
        while (team.Roster.Count > 12) off.Waive(team, team.Roster.OrderBy(p => p.CurrentAbility).First());

        // Under the cap: cap room.
        SetPayroll(team, fin.SalaryCap - 20_000_000);
        Assert.Equal(SigningRoute.CapRoom, off.SigningOption(team, 15_000_000).Route);

        // Over the cap: the mid-level exception, once.
        SetPayroll(team, fin.SalaryCap + 10_000_000);
        Assert.Equal(SigningRoute.MidLevel, off.SigningOption(team, 12_000_000).Route);
        Assert.Equal(SigningRoute.None, off.SigningOption(team, fin.MidLevel + 1).Route);
        var fa = league.FreeAgents.First(p => ContractMarket.MarketSalary(p.CurrentAbility, p.Age(off.Date), fin) is > 3_000_000 and <= 15_000_000);
        Assert.True(off.SignFreeAgent(team, fa));
        Assert.Contains(team.Id, off.MidLevelUsed);
        Assert.Equal(SigningRoute.None, off.SigningOption(team, 12_000_000).Route);

        // Minimum deals stay open over the cap (players asking up to twice the minimum take one), but not past the apron.
        Assert.Equal(SigningRoute.Minimum, off.SigningOption(team, fin.MinSalary * 2).Route);
        SetPayroll(team, fin.Apron);
        while (team.Roster.Count < fin.RosterMin) team.Roster.Add(team.Roster[0]); // pad so the roster minimum doesn't apply
        Assert.Equal(SigningRoute.None, off.SigningOption(team, fin.MinSalary).Route);
    }

    [Fact]
    public void Over_the_cap_teams_must_match_salaries_in_trades_and_stay_under_the_apron()
    {
        var league = NewLeague();
        var fin = league.Finance;
        var a = league.Teams[0]; var b = league.Teams[1];
        SetPayroll(a, fin.SalaryCap + 10_000_000);
        SetPayroll(b, fin.SalaryCap + 10_000_000);
        var engine = new TradeEngine(league, new DateOnly(2026, 11, 1));
        var cheap = a.Roster.OrderBy(p => p.CurrentAbility).First();
        var dear = b.Roster.OrderByDescending(p => p.CurrentAbility).First();
        cheap.Contract!.Salary = 5_000_000; dear.Contract!.Salary = 30_000_000;

        var lopsided = new TradeProposal { From = a, To = b };
        lopsided.FromPlayers.Add(cheap); lopsided.ToPlayers.Add(dear);
        var (legal, note) = engine.CheckLegality(lopsided);
        Assert.False(legal);
        Assert.Contains("125%", note);

        dear.Contract!.Salary = 6_000_000; // within 125% + $100K of 5M
        Assert.True(engine.CheckLegality(lopsided).legal);

        // A sits $1M under the apron; taking back $1.2M more than it sends would pass it.
        cheap.Contract!.Salary = 5_000_000; dear.Contract!.Salary = 6_200_000;
        var filler = a.Roster.First(p => p != cheap);
        filler.Contract!.Salary += fin.Apron - 1_000_000 - ContractMarket.Payroll(a);
        Assert.Equal(fin.Apron - 1_000_000, ContractMarket.Payroll(a));
        var (ok, why) = engine.CheckLegality(lopsided);
        Assert.False(ok);
        Assert.Contains("apron", why);
    }

    [Fact]
    public void Contenders_keep_their_cores_over_the_cap_after_a_full_offseason()
    {
        var league = NewLeague(5);
        var career = new Career(new Rng(8));
        career.PlaySeason(league);
        var fin = league.Finance;
        Assert.All(league.Teams, t => Assert.True(ContractMarket.Payroll(t) <= fin.Apron + fin.MinSalary * 2, t.FullName));
        Assert.Contains(league.Teams, t => ContractMarket.Payroll(t) > fin.SalaryCap); // Bird rights let teams go over
    }
}
