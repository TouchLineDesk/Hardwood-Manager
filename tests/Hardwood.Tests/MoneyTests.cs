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
        var club = world[UsaPyramid.LocalKey("cal")].Clubs[0];
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
}
