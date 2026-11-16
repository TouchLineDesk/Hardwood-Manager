using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 65 (review #13 rank 4, Danny's #464): the transfer market opens on player cards, best first, with the
/// club's stance and his answer as coloured chips and the positions as chips with counts. The full table is one
/// click away, and its stance and "Joins?" columns are chips too.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run65MarketCardTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_market_opens_on_cards_and_the_table_is_one_click_away()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var path = Path.Combine(Path.GetTempPath(), $"hw-run65m-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        try
        {
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            vm.Navigate("Transactions"); Settle();
            var tx = Assert.IsType<TransactionsViewModel>(vm.CurrentPage);

            // Cards first: the best 24 by ability, the same players as the table's first 24, in order.
            Assert.Equal("Cards", tx.MarketView);
            Assert.True(tx.ShowMarketCards);
            Assert.True(tx.Market.Count > TransactionsViewModel.CardCount);
            Assert.Equal(tx.Market.Take(TransactionsViewModel.CardCount), tx.MarketCards);
            Assert.Contains($"best {TransactionsViewModel.CardCount} of {int.Parse(tx.PositionChips[0].Record):N0}", tx.MarketCardsNote);
            var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "MarketGrid");
            Assert.False(grid.IsEffectivelyVisible);
            var cards = window.GetVisualDescendants().OfType<ItemsControl>().Single(g => g.Name == "MarketCards");
            Assert.True(cards.IsEffectivelyVisible);
            var panel = cards.GetVisualDescendants().OfType<CardGrid>().First();
            Assert.InRange(CardGrid.ColumnsFor(panel.Bounds.Width, 290, 3), 2, 3);   // beside the deal panel at 1480 px

            // The chips: stance colours by what the club says, his answer green or red.
            foreach (var r in tx.MarketCards)
            {
                var st = s.StanceOn(r.Player)!;
                Assert.Same(st.Stance switch
                {
                    Hardwood.Core.Season.TransferStance.WontSell => StandingRow.RelegationBrush,
                    Hardwood.Core.Season.TransferStance.WantsHimGone => StandingRow.PlayoffBrush,
                    _ => r.StanceBrush,
                }, r.StanceBrush);
                Assert.Equal(st.Why, r.StanceTip);
                Assert.Equal(s.WontCome(r.Player) == null ? "Would come" : r.Interest, r.InterestChip);
                Assert.Same(s.WontCome(r.Player) == null ? StandingRow.PlayoffBrush : StandingRow.RelegationBrush, r.InterestBrush);
            }

            // Pressing a card picks him: the deal panel fills in and only that card is lit.
            var first = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("marketCard") && b.DataContext == tx.MarketCards[1]);
            first.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            Assert.Same(tx.MarketCards[1], tx.SelectedMarket);
            Assert.True(tx.MarketCards[1].Picked);
            Assert.Single(tx.MarketCards, r => r.Picked);
            Assert.Contains(tx.DealChips, c => c.Label == "Likely fee" && c.Value == tx.MarketCards[1].Ask);
            tx.PickMarket(tx.MarketCards[2]);
            Assert.False(tx.MarketCards[1].Picked);
            Assert.True(tx.MarketCards[2].Picked);

            // Positions as chips, with counts that add up to All.
            Assert.Equal(tx.Positions, tx.PositionChips.Select(c => c.Name));
            Assert.True(tx.PositionChips.Single(c => c.Name == "All").Selected);
            int all = int.Parse(tx.PositionChips[0].Record);
            Assert.Equal(all, tx.PositionChips.Skip(1).Sum(c => int.Parse(c.Record)));
            Assert.Equal(Math.Min(300, all), tx.Market.Count);
            var pg = window.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is FilterChip { Name: "PG" });
            pg.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            Assert.Equal("PG", tx.MarketPosition);
            Assert.True(tx.PositionChips.Single(c => c.Name == "PG").Selected);
            Assert.All(tx.Market, r => Assert.Equal("PG", r.Pos));
            Assert.All(tx.MarketCards, r => Assert.Equal("PG", r.Pos));
            Assert.Equal(Math.Min(300, int.Parse(tx.PositionChips.Single(c => c.Name == "PG").Record)), tx.Market.Count);
            tx.SetMarketPosition("nonsense");
            Assert.Equal("PG", tx.MarketPosition);

            // Nobody fits: the cards say so.
            tx.MinAbility = 200; Settle();
            Assert.Empty(tx.MarketCards);
            Assert.StartsWith("Nobody on the market fits", tx.MarketCardsNote);
            tx.MinAbility = 110; tx.SetMarketPosition("All"); Settle();

            // The full table, with the same players and the column picker.
            tx.MarketView = "Full table"; Settle();
            Assert.True(grid.IsEffectivelyVisible);
            Assert.False(cards.IsEffectivelyVisible);
            Assert.Contains(grid.Columns, c => ColumnPicker.HeaderOf(c) == "Club stance");
            Assert.Contains(grid.Columns, c => ColumnPicker.HeaderOf(c) == "Joins?");
            Assert.True(window.GetVisualDescendants().OfType<ColumnPicker>().Single(p => p.Key == "market").IsEffectivelyVisible);

            if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } dir)
            {
                using (var f = window.CaptureRenderedFrame()) f?.Save(Path.Combine(dir, "market-table.png"));
                tx.MarketView = "Cards"; tx.PickMarket(tx.MarketCards[0]); Settle();
                using (var f = window.CaptureRenderedFrame()) f?.Save(Path.Combine(dir, "market-cards.png"));
                vm.Guide.On = true; Settle();
                using (var f = window.CaptureRenderedFrame()) f?.Save(Path.Combine(dir, "market-cards-assistant.png"));
            }
            window.Close();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
        }
    }
}
