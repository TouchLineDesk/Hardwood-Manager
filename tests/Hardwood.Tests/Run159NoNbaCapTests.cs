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
/// D81 (run 159, Danny, Nov 22): no salary cap in the NBA. "Teams build their revenue through the game and can apply that to
/// salaries for the players … keep the ones they want." An NBA club keeps its own players up to what its finances carry
/// (<see cref="ContractRules.NbaMeans"/>), not the 35% young-star stretch (#928). Before, on seed 2026 the Spurs' Stephon
/// Castle (CA 169) asked $51.74M, $1.2M past the stretch, and couldn't be kept at his ask.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run159NoNbaCapTests
{
    private readonly ITestOutputHelper _out;
    public Run159NoNbaCapTests(ITestOutputHelper o) => _out = o;

    private static Team Club(long income, long staff, long balance, long budget)
    {
        var team = new Team { Id = 1, City = "C", Name = "N", Abbreviation = "A", Division = "D" };
        team.Money.Ready = true; team.Money.Revenue = income; team.Money.Balance = balance; team.Money.WageBudget = budget;
        team.Staff.Add(new Staff { FirstName = "S", LastName = "T", Salary = staff });
        return team;
    }

    [Fact]
    public void An_nba_clubs_means_are_its_income_less_costs_plus_a_share_of_savings()
    {
        var team = Club(245_000_000, 10_000_000, 0, 160_000_000);
        long upkeep = Franchise.Upkeep(team.Money.Facilities, Budgets.LeagueRevenue(1.0));
        // No savings above the owner's reserve: income less staff and running costs, a payroll it can pay every season.
        Assert.Equal(245_000_000 - 10_000_000 - upkeep, ContractRules.NbaMeans(team));
        var own = new Player { FirstName = "A", LastName = "B", TeamId = 1, CurrentAbility = 120, BirthDate = new DateOnly(1996, 1, 1) };  // 30, not a star
        var theirs = new Player { FirstName = "A", LastName = "B", TeamId = 2, CurrentAbility = 170, BirthDate = new DateOnly(2004, 1, 1) };
        Assert.Equal(ContractRules.NbaMeans(team) - 160_000_000, ContractRules.NbaKeepAllowance(team, own));   // any of his own players
        Assert.True(ContractRules.NbaKeepAllowance(team, own) > 0);
        Assert.Equal(0, ContractRules.NbaKeepAllowance(team, theirs));         // not his club's
        // Savings above the reserve are spread over five seasons…
        team.Money.Balance = Franchise.Reserve(team) + 50_000_000;
        Assert.Equal(245_000_000 - 10_000_000 - upkeep + 10_000_000, ContractRules.NbaMeans(team));
        // …and add at most a tenth of a season's income, however much the club saved.
        team.Money.Balance = 2_000_000_000;
        Assert.Equal(245_000_000 - 10_000_000 - upkeep + 24_500_000, ContractRules.NbaMeans(team));
        // A club in debt keeps to its budget.
        team.Money.Balance = -1;
        Assert.Equal(0, ContractRules.NbaMeans(team));
        Assert.Equal(0, ContractRules.NbaKeepAllowance(team, own));
        // A budget already past the means gives nothing more.
        var tight = Club(100_000_000, 5_000_000, 0, 120_000_000);
        Assert.Equal(0, ContractRules.NbaKeepAllowance(tight, new Player { FirstName = "A", LastName = "B", TeamId = 1 }));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_contender_keeps_its_own_players_as_far_as_its_finances_carry()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        int teamId = probe.Teams.OrderByDescending(t => QuickSim.Strength(t)).First().Id;
        var s = GameSession.NewGame(2026, teamId, null);
        int start = s.League.Season, guard = 0;
        Player? kept = null, star = null;
        while (s.League.Season < start + 2 && guard++ < 5000)
        {
            if (s.Phase == SessionPhase.Offseason && s.Offseason?.Phase == OffseasonPhase.ReSigning)
            {
                Assert.True(s.InNabl && s.KeepsByMeans);
                long means = ContractRules.NbaMeans(s.HumanTeam);
                _out.WriteLine($"{s.Today}: budget {s.HumanTeam.Money.WageBudget:N0}, means {means:N0}, committed {s.MyCommittedWages:N0}, room {s.MyWageRoom:N0}");
                foreach (var p in s.MyExpiring.Where(p => !Offseason.WontReSign(p)))
                    Assert.Equal(Math.Max(0, means - s.HumanTeam.Money.WageBudget), s.MyStretchFor(p));   // every one of his own players
                // The best player whose ask is past the plain budget but within the club's means: kept at his ask.
                star = s.MyExpiring.Where(p => !Offseason.WontReSign(p)).OrderByDescending(p => p.CurrentAbility)
                    .FirstOrDefault(p => s.MyCommittedWages + s.PeekDemand(p).Wage > s.HumanTeam.Money.WageBudget && s.CanKeep(p, s.PeekDemand(p).Wage));
                if (star != null)
                {
                    var d = s.PeekDemand(star);
                    _out.WriteLine($"{star.Name} ({star.Age(s.Today)}, CA {star.CurrentAbility}) asks {d.Wage:N0} x{d.Years}; room {s.MyWageRoom:N0}, past the budget up to {s.MyStretchFor(star):N0}");
                    CheckCard(s, star);
                    var reply = s.Offer(star, d.Wage, d.Years);
                    Assert.True(reply.Accepted, reply.Text);
                    Assert.Equal(s.HumanTeam.Id, star.TeamId);
                    Assert.False(s.OverWageBudget);   // the owner raised the budget to cover him
                    kept = star;
                    break;
                }
                s.AdvanceOffseason();
                continue;
            }
            if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay();
        }
        Assert.NotNull(kept);
    }

    /// <summary>The summer page's card and the talk panel say there's no salary cap and how far the owner goes.</summary>
    private void CheckCard(GameSession s, Player star)
    {
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            vm.Navigate("Offseason");
            void Settle() { for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            Settle();
            var page = Assert.IsType<OffseasonViewModel>(vm.CurrentPage);
            var row = page.Expiring.Single(r => r.Player == star);
            _out.WriteLine($"{star.Name}: {row.Ask} · {row.Fits} · {row.FitTip}");
            Assert.Equal("Owner stretches", row.Fits);
            Assert.Contains("no salary cap in the NBA", row.FitTip);
            Assert.DoesNotContain("young star", row.FitTip);
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run159-no-nba-cap.png"));
            }
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
        }
    }
}
