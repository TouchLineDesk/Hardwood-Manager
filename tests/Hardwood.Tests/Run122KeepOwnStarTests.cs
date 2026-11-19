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
        Assert.Equal(35_000_000, ContractRules.KeepOwnAllowance(team, star, on));
        Assert.Equal(0, ContractRules.KeepOwnAllowance(team, older, on));
        Assert.Equal(0, ContractRules.KeepOwnAllowance(team, weaker, on));
        Assert.Equal(0, ContractRules.KeepOwnAllowance(team, theirs, on));   // not his own club
        team.Money.Balance = -1;
        Assert.Equal(0, ContractRules.KeepOwnAllowance(team, star, on));     // an owner in debt doesn't stretch
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_contender_keeps_its_young_star_and_the_owner_covers_him()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        int teamId = probe.Teams.OrderByDescending(t => QuickSim.Strength(t)).First().Id;
        var s = GameSession.NewGame(2026, teamId, null);
        int start = s.League.Season, guard = 0;
        Player? star = null;
        bool refusedOlder = false;
        while (s.League.Season < start + 2 && guard++ < 5000)
        {
            if (s.Phase == SessionPhase.Offseason && s.Offseason?.Phase == OffseasonPhase.ReSigning)
            {
                // Anyone else whose ask is over the budget is still refused (summer one: Keldon Johnson, 27, CA 133).
                foreach (var p in s.MyExpiring.Where(p => !ContractRules.IsYoungStar(p, s.Today)).ToList())
                {
                    var d = s.PeekDemand(p);
                    if (s.MyWageRoom >= d.Wage) continue;
                    Assert.Equal(0, s.MyStretchFor(p));
                    var r = s.Offer(p, d.Wage, d.Years);
                    Assert.False(r.Accepted);
                    Assert.Contains("won't sanction", r.Text);
                    refusedOlder = true;
                    break;
                }
                star = s.MyExpiring.FirstOrDefault(p => ContractRules.IsYoungStar(p, s.Today));
                if (star != null)
                {
                    var d = s.PeekDemand(star);
                    Assert.True(d.Wage > s.MyWageRoom);            // he doesn't fit the plain budget
                    Assert.True(s.MyStretchFor(star) > 0);
                    Assert.True(s.CanKeep(star, d.Wage));
                    CheckCard(s, star);
                    var reply = s.Offer(star, d.Wage, d.Years);
                    Assert.True(reply.Accepted, reply.Text);
                    Assert.Equal(s.HumanTeam.Id, star.TeamId);
                    Assert.True(star.Contract!.Years >= 1);
                    Assert.False(s.OverWageBudget);                   // the owner raised the budget to cover him
                    break;
                }
                s.AdvanceOffseason();
                continue;
            }
            if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay();
        }
        Assert.True(refusedOlder);
        Assert.NotNull(star);
    }

    /// <summary>The summer page's card says the owner stretches for him, and the talk panel says how far.</summary>
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
            Assert.Contains("the owner will go up to", row.FitTip);
            var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "run122-keep-own-star.png"));
            }
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
        }
    }
}
