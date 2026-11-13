using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Critic review #3: local clubs that win break even, and committed wages don't leave a healthy club "over budget".</summary>
public class MoneyTests
{
    [Fact]
    public void A_local_club_pays_local_staff_wages_and_a_winning_season_breaks_even()
    {
        var league = new WorldGenerator(new Rng(2026)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, 2026, Array.Empty<string>(), new DateOnly(2026, 10, 1));
        var club = world[UsaPyramid.Tier5].Clubs[0]; // the bottom tier (D39 A)
        var s = GameSession.NewGame(2026, club.Id, null);
        var t = s.HumanTeam;
        long staff = t.Staff.Sum(x => x.Salary);
        Assert.Equal(5, t.Staff.Count);
        Assert.True(staff < t.Money.Revenue * 0.1, $"staff ${staff:N0} against revenue ${t.Money.Revenue:N0}");
        // A winning season: revenue in, wages and staff out, and the books don't go into the red.
        t.Money.Balance = 0;
        Budgets.Summer(new[] { t }, s.MySalaryScale, _ => 0.75, _ => false, 2026);
        Assert.True(t.Money.Balance >= 0, $"balance {t.Money.Balance:N0}");
        Assert.False(t.Money.Embargo);
    }

    [Fact]
    public void A_healthy_club_is_never_set_a_budget_below_wages_it_already_owes()
    {
        var league = new WorldGenerator(new Rng(6)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Budgets.Initialize(league.Teams, 1, new Rng(6));
        var t = league.Teams[0];
        foreach (var p in t.Roster) if (p.Contract is { } c) { c.StartSeason = 2026; c.Years = 3; }
        long committed = ContractMarket.Payroll(t);
        t.Money.Balance = 50_000_000;
        Budgets.Summer(new[] { t }, 1, _ => 0.2, _ => false, 2026);   // a bad season: revenue dips
        Assert.True(t.Money.WageBudget >= committed, $"budget {t.Money.WageBudget:N0} < committed {committed:N0}");

        t.Money.Balance = -t.Money.Revenue;                              // in debt: the owner squeezes
        Budgets.Summer(new[] { t }, 1, _ => 0.2, _ => false, 2026);
        Assert.True(t.Money.WageBudget < committed);
    }

    [Fact]
    public void A_real_nba_club_at_its_budget_floor_can_still_fill_its_roster()
    {
        // Run 17 (#221): a real payroll sits at the floor of what's signed; ten signed players and a $200M payroll
        // must leave room for three minimum deals, or the club is over budget the day it makes up its numbers.
        var league = new WorldGenerator(new Rng(6)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Budgets.Initialize(league.Teams, 1, new Rng(6));
        var t = league.Teams[0];
        foreach (var (p, i) in t.Roster.Select((p, i) => (p, i)))
            p.Contract = new Contract { StartSeason = 2026, Years = i < 10 ? 3 : 1, Salary = i < 10 ? 20_000_000 : 2_000_000 };
        t.Money.Balance = 50_000_000;
        Budgets.Summer(new[] { t }, 1, _ => 0.5, _ => false, 2026, fillRoster: true);
        Assert.True(t.Money.WageBudget >= 200_000_000 + 3 * league.Finance.MinSalary, $"budget {t.Money.WageBudget:N0}");
        Budgets.Summer(new[] { t }, 1, _ => 0.5, _ => false, 2026);   // fictional leagues keep the old floor
        Assert.True(t.Money.WageBudget >= 200_000_000);
    }

    [Fact]
    public void A_committed_college_senior_fits_inside_the_summer_budget()
    {
        // Run 19 (#256): a senior committed at $3.1M a year signs after the budgets are set. The floor counts him as
        // signed, so the club still has room for its minimum deals (run 18: SAS and POR opened $0.9-1.9M over).
        var league = new WorldGenerator(new Rng(6)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Budgets.Initialize(league.Teams, 1, new Rng(6));
        var t = league.Teams[0];
        foreach (var (p, i) in t.Roster.Select((p, i) => (p, i)))
            p.Contract = new Contract { StartSeason = 2026, Years = i < 10 ? 3 : 1, Salary = i < 10 ? 20_000_000 : 2_000_000 };
        t.Money.Balance = 50_000_000;
        Budgets.Summer(new[] { t }, 1, _ => 0.5, _ => false, 2026, fillRoster: true, pending: x => x == t ? (3_100_000, 1) : (0, 0));
        Assert.True(t.Money.WageBudget >= 200_000_000 + 3_100_000 + 2 * league.Finance.MinSalary, $"budget {t.Money.WageBudget:N0}");
    }
}
