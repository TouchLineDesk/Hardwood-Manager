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

/// <summary>Run 100 (bug hunt H68, Medium): the transfer market's bid panel names the player and his club, the picked
/// row stays in view, a shut window disables the fee and Bid, and at 1280 px and 130% the deal columns stay on screen
/// (the least-needed columns step aside and the panel moves under the table) without changing the saved view.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run100BidPanelTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_bid_panel_names_the_player_and_the_deal_columns_fit()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var path = Path.Combine(Path.GetTempPath(), $"hw-run100-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
        try
        {
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            TransactionsView View() => window.GetVisualDescendants().OfType<TransactionsView>().Single(v => v.IsEffectivelyVisible);
            T Named<T>(string name) where T : Control => View().GetVisualDescendants().OfType<T>().Single(c => c.Name == name);
            void Snap(string name) { if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, name)); } }
            while (s.WindowOpen) s.AdvanceDay();   // into the season, when the window is shut
            vm.AfterTick();
            vm.Navigate("Transactions"); Settle();
            var tx = Assert.IsType<TransactionsViewModel>(vm.CurrentPage);
            var view = View();

            // Nobody picked: the plain title, and Bid is off.
            Assert.Equal("Make a bid", tx.BidTitle);
            Assert.False(tx.CanBid);

            // The table, a player far down: the title names him and his club, and his row is scrolled into view.
            tx.MarketView = "Full table"; Settle();
            var far = tx.Market[60];
            tx.SelectedMarket = far; Settle();
            Assert.Equal($"Make a bid · {far.Name}, {far.ClubName}", tx.BidTitle);
            Assert.Equal(tx.BidTitle, Named<TextBlock>("BidTitle").Text);
            var grid = Named<DataGrid>("MarketGrid");
            Assert.True(TransactionsView.RowShown(grid, far));

            // The window is shut: the fee and Bid are off, and the panel says when it opens.
            Assert.False(tx.CanBid);
            Assert.False(Named<NumericUpDown>("BidFeeBox").IsEnabled);
            Assert.False(Named<Button>("BidButton").IsEnabled);
            Assert.StartsWith(s.WindowText, tx.DealText);
            Assert.Equal(s.WindowText, tx.BidTip);
            int before = s.MyBids.Count;
            tx.PlaceBidCommand.Execute(null);
            Assert.Equal(before, s.MyBids.Count);

            // At 1480 px with every column fitting nothing is set aside; the picker reads as before.
            var picker = view.GetVisualDescendants().OfType<ColumnPicker>().Single(p => p.Key == "market");
            vm.Guide.On = false; Settle();
            Assert.False(view.DealPanelStacked);
            Snap("run100-market-1480.png");

            // 1280 px at 130%: the deal panel moves under the table and the least-needed columns step aside, in order.
            window.Width = 1280; window.ApplyScale(1.3); vm.Guide.On = true; Settle();
            Assert.True(view.DealPanelStacked);
            Assert.NotEmpty(picker.FitHidden);
            Assert.Equal(new[] { "Clause", "Contract", "Pos", "Age", "PA", "Club" }.Take(picker.FitHidden.Count), picker.FitHidden);
            foreach (var key in new[] { "Joins?", "Club stance", "Likely fee" })
            {
                var col = grid.Columns.Single(c => c.Header as string == key);
                Assert.True(col.IsVisible, key);
            }
            Assert.True(TransactionsView.RowShown(grid, far), "the picked row stays in view when the table shrinks");
            double used = grid.Columns.Where(c => c.IsVisible).Sum(ColumnPicker.WidthOf);
            Assert.True(used <= grid.Bounds.Width, $"{used} > {grid.Bounds.Width}");
            Assert.Equal(ColumnPicker.AllName, picker.Current);   // the saved view is untouched
            Snap("run100-market-1280-130.png");

            // Unticking a column while some are set aside keeps them in "My view" (they're only out of room).
            var aside = picker.FitHidden.ToList();
            picker.SetShown("CA", false); Settle();
            var mine = AppSettings.Current.TableViews["market"].Shown;
            Assert.DoesNotContain("CA", mine);
            Assert.All(aside, h => Assert.Contains(h, mine));

            // Back to 1480 px at 100%: the panel returns beside the table and the set-aside columns come back.
            picker.Choose(ColumnPicker.AllName);
            window.Width = 1480; window.ApplyScale(1.0); vm.Guide.On = false; Settle();
            Assert.False(view.DealPanelStacked);
            Assert.True(picker.FitHidden.Count < aside.Count, string.Join(",", picker.FitHidden));
            Assert.True(grid.Columns.Single(c => c.Header as string == "Club").IsVisible);

            // January: the window opens, and Bid works for a player who'd come.
            while (s.Today.Month != 1) s.AdvanceDay();
            vm.AfterTick(); vm.Navigate("Transactions"); Settle();
            tx = Assert.IsType<TransactionsViewModel>(vm.CurrentPage);
            var yes = tx.Market.First(r => r.WontCome == null && !r.Agreed);
            tx.SelectedMarket = yes; Settle();
            Assert.True(s.WindowOpen);
            Assert.True(tx.CanBid);
            Assert.Equal($"Bid for {yes.Name}", tx.BidTip);
            Assert.DoesNotContain("shut", tx.DealText);
            Assert.Equal($"Make a bid · {yes.Name}, {yes.ClubName}", tx.BidTitle);
        }
        finally
        {
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
