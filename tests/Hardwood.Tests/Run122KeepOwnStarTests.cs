using Hardwood.Core;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Avalonia.Headless;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// H122 (run 122): the owner stretches the wage budget to keep the club's own young star (CA 150+, under 26). The bug: the
/// Spurs (nba-contender, seed 2026) had $2.4M of room on May 11 2028 when Stephon Castle (23, CA 162) asked $48.29M, and he
/// walked.
/// </summary>
[Collection(SharedUiState.Name)] // touches app settings or the portrait service (run 131: they raced PortraitServiceTests)
public class Run122KeepOwnStarTests
{
    private readonly ITestOutputHelper _out;
    public Run122KeepOwnStarTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Young_star_rule_and_the_owner_in_debt()
    {
        var on = new DateOnly(2028, 5, 11);
        var team = new Team { Id = 1, City = "C", Name = "N", Abbreviation = "A", Division = "D" };
        team.Money.Ready = true; team.Money.Balance = 1; team.Money.WageBudget = 100_000_000;
        var star = new Player { FirstName = "A", LastName = "B", TeamId = 1, CurrentAbility = 155, BirthDate = new DateOnly(2004, 6, 1) };   // 23
        var older = new Player { FirstName = "A", LastName = "B", TeamId = 1, CurrentAbility = 165, BirthDate = new DateOnly(2001, 6, 1) };  // 26
        var weaker = new Player { FirstName = "A", LastName = "B", TeamId = 1, CurrentAbility = 149, BirthDate = new DateOnly(2006, 6, 1) };
        var theirs = new Player { FirstName = "A", LastName = "B", TeamId = 2, CurrentAbility = 170, BirthDate = new DateOnly(2006, 6, 1) };
        Assert.Equal(35_000_000, ContractRules.KeepOwnAllowance(team, star, on, 2028));
        Assert.Equal(0, ContractRules.KeepOwnAllowance(team, older, on, 2028));
        Assert.Equal(0, ContractRules.KeepOwnAllowance(team, weaker, on, 2028));
        Assert.Equal(0, ContractRules.KeepOwnAllowance(team, theirs, on, 2028));   // not his own club
        // Review of #186: a second young star the same summer shares the same 35%; it doesn't compound on the raised budget.
        ContractRules.OwnerCovers(team, 120_000_000, 2028);
        Assert.Equal(120_000_000, team.Money.WageBudget);
        Assert.Equal(15_000_000, ContractRules.KeepOwnAllowance(team, star, on, 2028));
        ContractRules.OwnerCovers(team, 135_000_000, 2028);
        Assert.Equal(0, ContractRules.KeepOwnAllowance(team, star, on, 2028));
        // Next summer the stretch is worked out from that summer's budget.
        Assert.Equal(47_250_000, ContractRules.KeepOwnAllowance(team, star, on, 2029));
        team.Money.Balance = -1;
        Assert.Equal(0, ContractRules.KeepOwnAllowance(team, star, on, 2029));     // an owner in debt doesn't stretch
    }

    // The NBA half (the Spurs keeping Stephon Castle) moved to Run159NoNbaCapTests: D81 replaced the stretch there.
}
