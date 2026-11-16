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

/// <summary>Run 67 (review #13 rank 6, Danny's #464; bug hunt H9): the summer boards as player cards. Contracts up,
/// the draft board (in your scouts' rank order) and free agents (with ask and budget chips) open on cards, here and on
/// Transactions → Free agents; the full tables are one click away. Make offer with an empty Wage box now says why.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run67SummerCardTests
{
    [Fact]
    public void Chips_and_bars_read_the_ask_the_budget_and_the_scouts_range()
    {
        Assert.Equal(12.5, SummerCards.Mid("12.5"));
        Assert.Equal(13.0, SummerCards.Mid("11.0-15.0"), 6);
        Assert.Equal(0, SummerCards.Mid(""));
        Assert.Equal("Asks $2.35M × 3", SummerCards.AskChip(new ContractDemand(2_350_000, 3)));
        Assert.Equal("$750K", SummerCards.Money(750_000));

        var fits = SummerCards.Fit(2_000_000, 3_000_000);
        Assert.Equal("Fits your budget", fits.Text);
        Assert.Same(StandingRow.PlayoffBrush, fits.Brush);
        Assert.Contains("$1.00M", fits.Tip);
        var over = SummerCards.Fit(2_000_000, 800_000);
        Assert.Equal("$1.20M over budget", over.Text);
        Assert.Same(StandingRow.RelegationBrush, over.Brush);
        Assert.Equal("$2.00M over budget", SummerCards.Fit(2_000_000, -500_000).Text);
        Assert.Equal("Roster full", SummerCards.Blocked("Your roster is full. Release someone first."));
        Assert.Equal("Aims higher", SummerCards.Blocked("X is aiming higher than the ABA. Players of his level want a stronger league."));
        Assert.Equal("Talks over", SummerCards.Blocked("X's agent won't talk to you again this window."));

        var (world, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var p = world.Teams[0].Roster[0];
        var bars = SummerCards.Bars(p, 1);
        Assert.Equal(new[] { "Sho", "Ply", "Def", "Phy" }, bars.Select(b => b.Label));
        Assert.All(bars, b => { Assert.InRange(b.Value, 1, 20); Assert.DoesNotContain("-", b.Text); Assert.Contains("of 20", b.Tip); });
        Assert.Equal(p.Attributes.Average(AttrGroup.Shooting), bars[0].Value, 1);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_summer_boards_open_on_cards_and_an_empty_wage_says_why()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var path = Path.Combine(Path.GetTempPath(), $"hw-run67-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
        try
        {
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "SAS").Id, path);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            // A real NBA season has no free agents in season, so cut three men loose from other clubs (the longest name among them).
            foreach (var p in s.League.Teams.Where(t => t != s.HumanTeam).Take(3).Select(t => t.Roster.OrderByDescending(x => x.Name.Length).First()).ToList())
            {
                s.League.Teams.First(t => t.Roster.Contains(p)).Roster.Remove(p);
                p.TeamId = null; p.Contract = null; s.League.FreeAgents.Add(p);
            }
            // And make room on your own roster, so the talks are open.
            var cut = s.HumanTeam.Roster.OrderBy(x => x.CurrentAbility).First();
            s.HumanTeam.Roster.Remove(cut); cut.TeamId = null; cut.Contract = null; s.League.FreeAgents.Add(cut);
            vm.AfterTick();
            void Settle() { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            void Snap(string name) { if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, name)); } }

            // Transactions → Free agents: cards first, the best 24, each with an ask chip and a budget chip.
            vm.Navigate("Transactions"); Settle();
            var tx = Assert.IsType<TransactionsViewModel>(vm.CurrentPage);
            tx.Tab = 3; Settle();
            Assert.Equal("Cards", tx.FaView);
            Assert.True(Named<ItemsControl>("FreeAgentCards").IsEffectivelyVisible);
            Assert.False(Named<DataGrid>("FreeAgentGrid").IsEffectivelyVisible);
            Assert.True(tx.FreeAgentCards.Count > 0, $"FA {s.League.FreeAgents.Count} rows {tx.FreeAgents.Count} day {s.Today} phase {s.Phase}");
            Assert.True(tx.FreeAgentCards.Count <= SummerCards.FreeAgentCount);
            Assert.Equal(tx.FreeAgents.Take(tx.FreeAgentCards.Count).Select(r => r.Player), tx.FreeAgentCards.Select(c => c.Player));
            Assert.All(tx.FreeAgentCards, c => { Assert.StartsWith("Asks $", c.AskChip); Assert.False(string.IsNullOrEmpty(c.FitText)); Assert.Equal(4, c.Bars.Count); });

            // A card press opens talks with him and lights his card.
            var pick = tx.FreeAgentCards[Math.Min(2, tx.FreeAgentCards.Count - 1)];
            var card = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("summerCard") && b.DataContext == pick);
            card.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            Assert.Same(pick.Player, tx.Talk.Player);
            Assert.True(pick.Picked);
            Assert.Single(tx.FreeAgentCards, c => c.Picked);

            // H9: an empty Wage box used to make Make offer do nothing and say nothing.
            Assert.True(tx.Talk.CanTalk, tx.Talk.AskText);
            {
                var ask = s.PeekDemand(pick.Player);
                tx.Talk.Wage = null;
                tx.Talk.OfferCommand.Execute(null);
                Assert.StartsWith("Enter a wage and years first", tx.Talk.Reply);
                Assert.Equal(Math.Round((decimal)(ask.Wage / 1e6), 2), tx.Talk.Wage);
                Assert.Contains(pick.Player, s.League.FreeAgents);   // nothing was offered
                tx.Talk.Wage = 0; tx.Talk.Years = null;
                tx.Talk.OfferCommand.Execute(null);
                Assert.StartsWith("Enter a wage", tx.Talk.Reply);
                Assert.Equal(ask.Years, (int)tx.Talk.Years!.Value);
                // A real offer's answer survives the page's refresh (the rows are rebuilt).
                tx.Talk.Wage = 0.01m;
                tx.Talk.OfferCommand.Execute(null); Settle();
                Assert.False(string.IsNullOrEmpty(tx.Talk.Reply));
                Assert.DoesNotContain("Enter a wage", tx.Talk.Reply);
                if (s.League.FreeAgents.Contains(pick.Player)) Assert.Same(pick.Player, tx.Talk.Player);
            }
            Snap("fa-cards.png");
            tx.FaView = "Full table"; Settle();
            Assert.True(Named<DataGrid>("FreeAgentGrid").IsEffectivelyVisible);
            Assert.False(Named<ItemsControl>("FreeAgentCards").IsEffectivelyVisible);
            Snap("fa-table.png");

            // To the summer.
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Offseason"); Settle();
            var off = Assert.IsType<OffseasonViewModel>(vm.CurrentPage);
            Assert.True(off.InOffseason);
            Assert.Equal("Cards", off.View);

            // Contracts up: one card per expiring player, with his ask and whether it fits.
            if (off.Expiring.Count > 0)
            {
                off.Tab = 0; Settle();
                Assert.True(Named<ItemsControl>("ExpiringCards").IsEffectivelyVisible);
                Assert.False(Named<DataGrid>("ExpiringGrid").IsEffectivelyVisible);
                Assert.All(off.Expiring, r => { Assert.StartsWith("Asks $", r.AskChip); Assert.False(string.IsNullOrEmpty(r.FitTip)); });
                var last = off.Expiring[^1];
                var ec = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("summerCard") && b.DataContext == last);
                ec.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
                Assert.Same(last.Player, off.Talk.Player);
                Assert.Single(off.Expiring, r => r.Picked);
                Snap("offseason-contracts-cards.png");
                Assert.True(last.Picked, "after snap: " + off.Talk.Player?.Name + " / " + string.Join(",", off.Expiring.Where(r => r.Picked).Select(r => r.Name)));
                Assert.Same(last.Player, off.Talk.Player);
            }
            foreach (var p in s.MyExpiring.ToList()) s.ReleaseExpiring(p);
            s.AdvanceOffseason(); vm.AfterTick(); off.Refresh(); Settle();

            // The draft board: graduates as cards in your scouts' order.
            if (s.Offseason!.Phase == OffseasonPhase.Draft)
            {
                off.Tab = 4; Settle();
                Assert.True(Named<ItemsControl>("DraftCards").IsEffectivelyVisible);
                Assert.False(Named<DataGrid>("DraftGrid").IsEffectivelyVisible);
                Assert.Equal(off.DraftPool.Take(off.DraftCards.Count), off.DraftCards);
                Assert.True(off.DraftCards.Count <= SummerCards.DraftCount + 1);
                Assert.Equal(Enumerable.Range(1, off.DraftCards.Count).Select(i => $"#{i}"), off.DraftCards.Select(c => c.RankText));
                Assert.Contains("top of your board", off.DraftNote);
                var second = off.DraftCards[1];
                var dc = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("summerCard") && b.DataContext == second);
                dc.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
                Assert.Same(second, off.SelectedProspect);
                Assert.True(second.Picked);
                Assert.Single(off.DraftPool, r => r.Picked);
                Assert.Contains(second.Player.Name, off.DraftHint);
                // Others pick until you're on the clock (or the draft ends); the pick survives refreshes while he's there.
                while (!s.Offseason.DraftComplete && s.Offseason.OnTheClock != s.HumanTeam)
                    s.MakePick(s.Offseason.Prospects.First(p => p != second.Player));
                off.Refresh(); Settle();
                if (off.OnTheClock)
                {
                    Assert.Same(second.Player, off.SelectedProspect!.Player);
                    Assert.Equal($"Draft {second.Player.Name}", off.DraftButton);
                    Assert.StartsWith("Press Draft to take", off.DraftHint);
                    Snap("offseason-draft-cards.png");
                    off.DraftCommand.Execute(null); Settle();
                    Assert.Contains(second.Player, s.HumanTeam.Roster);
                }
                off.View = "Full table"; Settle();
                if (!s.Offseason.DraftComplete) Assert.True(Named<DataGrid>("DraftGrid").IsEffectivelyVisible);
                off.View = "Cards";
                while (s.Offseason.Phase == OffseasonPhase.Draft && !s.Offseason.DraftComplete) s.MakePick(s.Offseason.Prospects.First());
                vm.AfterTick(); off.Refresh();
            }

            // Free agency: cards with ask chips; graduates carry their chip.
            if (!off.ShowFreeAgency) { s.AdvanceOffseason(); vm.AfterTick(); off.Refresh(); }
            Assert.True(off.ShowFreeAgency);
            off.Tab = 1; Settle();
            Assert.True(Named<ItemsControl>("FreeAgentCards").IsEffectivelyVisible);
            Assert.False(Named<DataGrid>("SummerFreeAgentGrid").IsEffectivelyVisible);
            Assert.NotEmpty(off.FreeAgentCards);
            Assert.True(off.FreeAgentCards.Count <= SummerCards.FreeAgentCount);
            Assert.Contains("free agents by ability", off.FreeAgentNote);
            var grad = off.FreeAgents.FirstOrDefault(f => f.Graduate);
            if (grad != null) Assert.EndsWith("(graduate)", grad.Name);
            var fa = off.FreeAgentCards[0];
            var fc = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("summerCard") && b.DataContext == fa);
            fc.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            Assert.Same(fa.Player, off.Talk.Player);
            Assert.True(fa.Picked);
            Snap("offseason-fa-cards.png");
            off.View = "Full table"; Settle();
            Assert.True(Named<DataGrid>("SummerFreeAgentGrid").IsEffectivelyVisible);
            Snap("offseason-fa-table.png");
        }
        finally
        {
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
