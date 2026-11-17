using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 103 (play-test freeze; bug hunt H84 and H85).
/// H84: after a signing, the contract panel still showed the budget from when talks opened ("$40.6M to spend" under a
/// header saying "$20M to spend"), and still read "Sign …". It now says he has signed and on what, and its budget line
/// starts from the header's own figure.
/// H85: tier 2's Standings legend said division winners are promoted; since #873 they play the NBA's last, best of 3.
/// </summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run103BudgetAndLegendTests
{
    [Fact]
    public void The_budget_line_starts_from_the_headers_figure()
    {
        // No reserves: the header's figure, nothing else.
        Assert.Equal(" You have $20M of wage budget to spend.", TalkViewModel.BudgetLine(166_000_000, 146_000_000, 0, 0));
        // Reserves: the header's figure first, then what's left for this deal.
        Assert.Equal(" You have $20M of wage budget to spend, $18M of it for this deal after keeping $2.3M for the minimum deals the roster still needs.",
            TalkViewModel.BudgetLine(166_000_000, 146_000_000, 0, 2_300_000));
        Assert.Equal(" You have $1.5M of wage budget to spend, $0.5M of it for this deal after keeping $0.4M for your committed college seniors and $0.6M for the minimum deals the roster still needs.",
            TalkViewModel.BudgetLine(10_000_000, 8_500_000, 400_000, 600_000));
        // Reserves bigger than the room.
        Assert.Equal(" You have $1.0M of wage budget to spend, but keeping $1.5M for the minimum deals the roster still needs leaves nothing for this deal ($0.5M short).",
            TalkViewModel.BudgetLine(10_000_000, 9_000_000, 0, 1_500_000));
        // Over: never "$-1.1M to spend".
        Assert.Equal(" You are $1.1M over your wage budget.", TalkViewModel.BudgetLine(10_000_000, 11_100_000, 0, 500_000));
        // The panel and the header write money the same way.
        Assert.Equal(Budgets.Money(146_000_000), TalkViewModel.M(146_000_000));
        Assert.Equal(Budgets.Money(4_250_000), TalkViewModel.M(4_250_000));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void After_a_signing_the_panel_says_so_and_its_budget_matches_the_header()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var path = Path.Combine(Path.GetTempPath(), $"hw-run103-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        try
        {
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "SAS").Id, path);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            // Free agents to talk to, and a roster place for one of them.
            foreach (var p in s.League.Teams.Where(t => t != s.HumanTeam).Take(3).Select(t => t.Roster.OrderBy(x => x.CurrentAbility).First()).ToList())
            {
                s.League.Teams.First(t => t.Roster.Contains(p)).Roster.Remove(p);
                p.TeamId = null; p.Contract = null; s.League.FreeAgents.Add(p);
            }
            var cut = s.HumanTeam.Roster.OrderBy(x => x.CurrentAbility).First();
            s.HumanTeam.Roster.Remove(cut); cut.TeamId = null; cut.Contract = null; s.League.FreeAgents.Add(cut);
            vm.AfterTick();
            void Settle() { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }

            vm.Navigate("Transactions"); Settle();
            var tx = Assert.IsType<TransactionsViewModel>(vm.CurrentPage);
            tx.Tab = 3; Settle();
            var pick = tx.FreeAgentCards.Last();
            tx.PickFreeAgentCard(pick); Settle();
            Assert.True(tx.Talk.CanTalk, tx.Talk.AskText);
            string Header() => Budgets.Money(s.HumanTeam.Money.WageBudget - s.MyCommittedWages);
            Assert.Contains($"You have {Header()} of wage budget to spend", tx.Talk.AskText);
            Assert.Contains($"· {Header()} to spend", s.MyBudgetLine);   // the Offseason header's line
            Assert.StartsWith("Sign ", tx.Talk.Title);

            var want = s.PeekDemand(pick.Player);
            long before = s.MyCommittedWages;
            tx.Talk.Wage = Math.Round((decimal)(want.Wage / 1e6), 2) + 0.05m; tx.Talk.Years = want.Years;
            tx.Talk.OfferCommand.Execute(null); Settle();
            Assert.True(tx.Talk.Signed, tx.Talk.Reply);
            Assert.True(s.MyCommittedWages > before);
            // The panel names the deal, and its budget is the header's, after the signing.
            Assert.Equal($"{pick.Player.Name} has signed", tx.Talk.Title);
            Assert.StartsWith($"Signed · ${tx.Talk.Wage:F2}M × {want.Years} year", tx.Talk.AskText);
            Assert.Contains($"You have {Header()} of wage budget to spend", tx.Talk.AskText);
            Assert.Contains($"· {Header()} to spend", s.MyBudgetLine);   // the Offseason header's line
            Assert.DoesNotContain("His agent asks", tx.Talk.AskText);
            // A later refresh of the page keeps both.
            var reply = tx.Talk.Reply;
            tx.Refresh(); Settle();
            Assert.Equal(reply, tx.Talk.Reply);
            Assert.Contains($"You have {Header()} of wage budget to spend", tx.Talk.AskText);
            window.Close();
        }
        finally
        {
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Tier_two_and_the_NBA_explain_the_promotion_playoff()
    {
        // Tier 2: the winner plays for the place.
        int clubId = GameSession.NewGame(2026, humanTeamId: 1, savePath: null).World[UsaPyramid.Tier2].Clubs[0].Id;
        var s = GameSession.NewGame(2026, humanTeamId: clubId, savePath: null);
        Assert.Equal(UsaPyramid.Tier2, s.HumanLeague!.Key);
        var vm = new MainViewModel();
        vm.AttachSession(s);
        // Some games so the zones are live.
        for (int i = 0; i < 40; i++) s.AdvanceDay();
        vm.AfterTick();
        vm.Navigate("Standings");
        var st = Assert.IsType<StandingsViewModel>(vm.CurrentPage);
        st.Refresh();
        Assert.True(st.ShowPromotion);
        Assert.Equal(StandingRow.PromotionPlayoffKey, st.PromotionLabel);
        Assert.Contains("best of 3", st.PromotionLabel);
        Assert.DoesNotContain("promoted", st.PromotionLabel);
        Assert.Contains(StandingRow.LevelRecords, st.Note);
        Assert.DoesNotContain("point difference", st.RelegationLabel);
        var winners = st.East.Where(r => r.Zone == "Promotion").ToList();
        Assert.NotEmpty(winners);
        Assert.All(winners, r => Assert.Equal(StandingRow.PromotionPlayoffKey, r.ZoneTip));
        // Rules & format says the same.
        var rules = LeagueBook.Get(s, UsaPyramid.Tier2)!.Rules;
        Assert.Contains(rules, r => r.Item1 == "Level" && r.Item2.Contains("best of 3") && !r.Item2.Contains("goes up to the same division"));

        // The NBA: the last in each division plays to stay up.
        var nba = GameSession.NewGame(2026, humanTeamId: 1, savePath: null);
        var vm2 = new MainViewModel();
        vm2.AttachSession(nba);
        for (int i = 0; i < 20; i++) nba.AdvanceDay();
        vm2.AfterTick();
        vm2.Navigate("Standings");
        var ns = Assert.IsType<StandingsViewModel>(vm2.CurrentPage);
        ns.View = "By division";
        ns.Refresh();
        Assert.True(ns.ShowRelegation);
        Assert.Equal(StandingRow.StayUpKey, ns.RelegationLabel);
        Assert.Contains("to stay up", ns.Note);
        var last = ns.East.Concat(ns.West).Where(r => r.Zone == "Relegation").ToList();
        Assert.NotEmpty(last);
        Assert.All(last, r => Assert.Equal(StandingRow.StayUpTip(StandingRow.Tier2Name(nba)), r.ZoneTip));
    }
}
