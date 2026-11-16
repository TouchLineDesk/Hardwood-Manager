using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 81. Bug hunt H43: out of work, Transactions still let you waive and trade your old club's players (and it
/// was saved). H44: a college coach reaching Transactions could waive a scholarship freshman into the pros' free-agent
/// pool. H45: the summer's budget line counted the deals that are up as spent. H39: the Waive tab showed the Free agents
/// hint. H15b (part): the inbox summary out of work.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run81RosterMovesTests
{
    private static string? Dir => Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;

    private static void Snap(MainWindow window, Action settle, string name)
    {
        if (Dir is not { } dir) return;
        settle();
        using var f = window.CaptureRenderedFrame();
        f?.Save(Path.Combine(dir, name));
    }

    [Fact]
    public void The_budget_line_can_count_committed_wages()
    {
        var t = new Team { City = "Test", Name = "Testers", Abbreviation = "TST", Division = "D" };
        t.Money.WageBudget = 58_000_000;
        t.Roster.Add(new Player { FirstName = "A", LastName = "One", Contract = new Contract { Salary = 83_000_000, Years = 1 } });
        Assert.StartsWith("wages $83M of a $58M budget ($25M over)", Budgets.Describe(t));
        Assert.StartsWith("wages committed $48M of a $58M budget · $10M to spend", Budgets.Describe(t, 48_000_000, "wages committed"));
        Assert.Equal("$4.2M", Budgets.Money(4_200_000));
        Assert.Equal("$34M", Budgets.Money(34_000_000));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Out_of_work_no_roster_moves_and_the_summer_budget_counts_commitments()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run81-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            var w = window;
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

            // In work, in season: Transactions is open, and the budget line is the plain one.
            Assert.Null(s.RosterMovesClosed);
            vm.Navigate("Transactions"); Settle();
            var tx = Assert.IsType<TransactionsViewModel>(vm.CurrentPage);
            Assert.False(tx.Closed);
            Assert.True(tx.Ours.Count > 0);
            Assert.Equal(Budgets.Describe(s.HumanTeam), s.MyBudgetLine);

            // H45: the summer's re-signing step counts commitments, and names what's up beside them.
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 600) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            Assert.NotEmpty(s.MyExpiring);   // Boston has deals up in the summer of 2027 (seed 2026)
            {
                long up = s.MyExpiring.Sum(p => p.Contract?.Salary ?? 0);
                Assert.Contains($"wages committed {Budgets.Money(s.MyCommittedWages)} of a", s.MyBudgetLine);
                Assert.Contains($"not counting {Budgets.Money(up)} of {s.MyExpiring.Count} expiring deal", s.MyBudgetLine);
                Assert.Contains(s.Inbox, m => m.Title == "Contract decisions" && m.Body.Contains("wages committed"));
            }

            // H43: sacked. The club's players stay where they are whatever you press.
            var club = s.HumanTeam;
            s.Fire();   // the club goes back to the computer, which settles its own expiring deals at once (run 61)
            Assert.False(s.Employed);
            var roster = club.Roster.ToList();
            int faBefore = s.League.FreeAgents.Count;
            vm.AfterTick(); Settle();
            Assert.Equal("You aren't coaching a club right now.", s.RosterMovesClosed);
            Assert.Equal("You aren't coaching a club right now.", s.Waive(roster[0]));
            var partner = s.League.Teams.First(t => t != club);
            var trade = new TradeProposal { From = club, To = partner };
            trade.FromPlayers.Add(roster[^1]); trade.ToPlayers.Add(partner.Roster.OrderBy(p => p.CurrentAbility).First());
            var eval = s.ProposeTrade(trade);
            Assert.False(eval.Accepted);
            s.IncomingBids.Add(new IncomingBid { PlayerId = roster[0].Id, FromTeamId = partner.Id, Fee = 5_000_000 });
            Assert.Equal("You aren't coaching a club right now.", s.AcceptBid(s.IncomingBids[^1]));
            if (club.Staff.FirstOrDefault() is { } coachOnStaff)
            {
                s.FireStaff(coachOnStaff);   // code review: the old club's staff stay too
                Assert.Contains(coachOnStaff, club.Staff);
                Assert.StartsWith("You aren't coaching", s.ExtendStaff(coachOnStaff));
            }
            Assert.Equal(roster, club.Roster.ToList());
            Assert.Equal(faBefore, s.League.FreeAgents.Count);

            // The page: no Transactions in the menu; reached anyway (a link), it says why and shows no tabs.
            Assert.False(vm.Nav.First(n => n.Name == "Transactions").IsVisible);
            vm.Navigate("Transactions"); Settle();
            tx = Assert.IsType<TransactionsViewModel>(vm.CurrentPage);
            tx.Refresh(); Settle();
            Assert.True(tx.Closed);
            Assert.Contains("aren't coaching", tx.ClosedText);
            Assert.Contains("Board page", tx.ClosedText);
            Assert.Empty(tx.Ours);
            Assert.Equal("", tx.CapText);
            var view = window.GetVisualDescendants().OfType<TransactionsView>().First(v => v.DataContext == tx && v.Bounds.Width > 0);
            Assert.False(view.GetVisualDescendants().OfType<TabControl>().First().IsEffectivelyVisible);
            Assert.True(view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "ClosedPanel").IsEffectivelyVisible);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && (b.Content as string) == "Waive");
            var problems = LayoutCheck.Scan(window);
            Assert.True(problems.Count == 0, "Transactions out of work:\n" + string.Join("\n", problems));
            Snap(window, Settle, "run81-transactions-out-of-work.png");

            // H15b (part): the inbox summary is no longer the old club's.
            vm.Navigate("Inbox"); Settle();
            var inbox = Assert.IsType<InboxViewModel>(vm.CurrentPage);
            inbox.Refresh();
            Assert.Contains("out of work", inbox.Summary);
            Assert.DoesNotContain(club.FullName, inbox.Summary);
        }
        finally
        {
            window?.Close();
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_college_coach_cannot_waive_a_scholarship_player_into_the_pros()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run81c-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            start.PlayLeague = StartViewModel.CollegeChoice;
            start.SelectedTeam = start.Teams[1];
            start.NewGame();
            var s = vm.Session!;
            Assert.True(s.InCollege);
            var w = window;
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

            var freshman = s.HumanTeam.Roster.OrderBy(p => p.Age(s.Today)).First();
            int faBefore = s.League.FreeAgents.Count;
            Assert.StartsWith("A college program doesn't sign, waive or trade professionals", s.Waive(freshman));
            Assert.Contains(freshman, s.HumanTeam.Roster);
            Assert.DoesNotContain(freshman, s.League.FreeAgents);
            Assert.Equal(faBefore, s.League.FreeAgents.Count);

            Assert.False(vm.Nav.First(n => n.Name == "Transactions").IsVisible);
            vm.Navigate("Transactions"); Settle();
            var tx = Assert.IsType<TransactionsViewModel>(vm.CurrentPage);
            tx.Refresh(); Settle();
            Assert.True(tx.Closed);
            Assert.Contains("Recruiting", tx.ClosedText);
            Assert.DoesNotContain("Board page", tx.ClosedText);
            Assert.DoesNotContain("$0.0M", tx.CapText);
            var problems = LayoutCheck.Scan(window);
            Assert.True(problems.Count == 0, "Transactions, college:\n" + string.Join("\n", problems));
            Snap(window, Settle, "run81-transactions-college.png");
        }
        finally
        {
            window?.Close();
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
