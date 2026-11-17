using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 87. Bug hunt H56: a solvent club's wage budget sat below wages the coach couldn't shed: the contracts he
/// inherits when he takes a job (nba-real-mid 25 31: the Knicks hired him on Oct 21 2033 with $190.2M of contracts on a
/// $181.0M budget, 28 weeks "over"), and the minimum deals that make up a short roster (the Aces' sporting director filled
/// the roster past a budget set at exactly the signed deals, 30 weeks over). The owner now funds both, unless he's in debt.</summary>
public class Run87OwnerFundsTests
{
    [Fact]
    public void A_solvent_owner_funds_what_is_committed_and_an_owner_in_debt_does_not()
    {
        var t = new Team { City = "X", Name = "Test", Abbreviation = "X", Division = "X" };
        t.Money.Ready = true; t.Money.WageBudget = 15_100_000; t.Money.Balance = 30_700_000;
        Assert.Equal(0, Budgets.OwnerFunds(t, 15_000_000));            // already covered
        Assert.Equal(15_100_000, t.Money.WageBudget);
        Assert.Equal(700_000, Budgets.OwnerFunds(t, 15_710_000));      // rounded up to $0.1M
        Assert.Equal(15_800_000, t.Money.WageBudget);

        var poor = new Team { City = "X", Name = "Poor", Abbreviation = "X", Division = "X" };
        poor.Money.Ready = true; poor.Money.WageBudget = 15_100_000; poor.Money.Balance = -1;
        Assert.Equal(0, Budgets.OwnerFunds(poor, 20_000_000));
        Assert.Equal(15_100_000, poor.Money.WageBudget);

        var fresh = new Team { City = "X", Name = "Fresh", Abbreviation = "X", Division = "X" };                          // no money model yet: nothing to fund
        Assert.Equal(0, Budgets.OwnerFunds(fresh, 20_000_000));
    }

    [Fact]
    public void Taking_a_job_at_a_club_over_its_budget_starts_you_inside_it()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run87-{Guid.NewGuid():N}.hwsave");
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            s.Fire();
            Assert.False(s.Employed);

            // The Knicks' case: a computer-run summer left the club's contracts above the budget the owner set.
            var nyk = s.League.Teams.First(t => t.Abbreviation == "NYK");
            long payroll = ContractMarket.Payroll(nyk);
            nyk.Money.WageBudget = (long)Math.Ceiling(payroll / 100_000.0) * 100_000 - 9_200_000;
            nyk.Money.Balance = 143_700_000;
            s.Job.Vacancies.Add(new Vacancy { TeamId = nyk.Id, Opened = s.Today, Status = "offered", DecideOn = s.Today.AddDays(7) });
            Assert.Null(s.AcceptOffer(nyk.Id));

            Assert.Same(nyk, s.HumanTeam);
            Assert.False(s.OverWageBudget);
            Assert.True(nyk.Money.WageBudget >= s.MyCommittedWages);
            Assert.True(nyk.Money.WageBudget - s.MyCommittedWages < 100_000);
            var welcome = s.Inbox.First(m => m.Title == $"Welcome to the {nyk.FullName}");
            Assert.Contains("raised the wage budget by $9.2M", welcome.Body);

            // An owner in debt doesn't: the board's wage goal is his problem to explain (unchanged).
            s.Fire();
            var bkn = s.League.Teams.First(t => t.Abbreviation == "BKN");
            long low = ContractMarket.Payroll(bkn) - 5_000_000;
            bkn.Money.WageBudget = low; bkn.Money.Balance = -10_000_000;
            s.Job.Vacancies.Add(new Vacancy { TeamId = bkn.Id, Opened = s.Today, Status = "offered", DecideOn = s.Today.AddDays(7) });
            Assert.Null(s.AcceptOffer(bkn.Id));
            Assert.Equal(low, bkn.Money.WageBudget);
            Assert.DoesNotContain("raised the wage budget", s.Inbox.First(m => m.Title == $"Welcome to the {bkn.FullName}").Body);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
