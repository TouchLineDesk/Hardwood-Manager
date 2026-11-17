using Avalonia.Controls;
using Avalonia.Interactivity;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class TransactionsView : UserControl
{
    public TransactionsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as TransactionsViewModel);
        // A narrower or shorter table (a resize, a bigger text size, the panel moving under it) keeps the pick in view.
        // Only when it gets narrower or shorter, so a table you've scrolled away from doesn't snap back (code review, PR #162).
        MarketGrid.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width < e.PreviousSize.Width - 0.5 || e.NewSize.Height < e.PreviousSize.Height - 0.5)
                Avalonia.Threading.Dispatcher.UIThread.Post(KeepPickInView, Avalonia.Threading.DispatcherPriority.Loaded);
        };
        MarketLayout.SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) StackDealPanel(e.NewSize.Width < StackBelow); };
    }

    /// <summary>H68 (run 100): narrower than this (1280 px at 120-130%), the deal panel sits under the market instead of
    /// beside it; beside it, the table was 385 px wide and lost the club's stance and the fee.</summary>
    public const double StackBelow = 940;
    public bool DealPanelStacked { get; private set; }

    private void StackDealPanel(bool stack)
    {
        if (stack == DealPanelStacked) return;
        DealPanelStacked = stack;
        MarketLayout.ColumnDefinitions[1].Width = new GridLength(stack ? 0 : 300);
        Grid.SetColumn(DealPanel, stack ? 0 : 1);
        Grid.SetRow(DealPanel, stack ? 2 : 0);
        Grid.SetRowSpan(DealPanel, stack ? 1 : 2);
        DealPanel.Margin = stack ? new Avalonia.Thickness(0, 10, 0, 0) : new Avalonia.Thickness(12, 0, 0, 0);
        DealPanel.MaxHeight = stack ? 260 : double.PositiveInfinity;
    }

    private TransactionsViewModel? _vm;

    /// <summary>H68 (run 100): the picked player stays in view, in the table and on the cards.</summary>
    private void Watch(TransactionsViewModel? vm)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = vm;
        if (_vm != null) _vm.PropertyChanged += OnVmChanged;
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TransactionsViewModel.SelectedMarket) or nameof(TransactionsViewModel.MarketView))) return;
        // After layout, so a table that has just appeared has its rows.
        Avalonia.Threading.Dispatcher.UIThread.Post(KeepPickInView, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>Whether his row is already wholly on screen (scrolling then would only nudge the table).</summary>
    public static bool RowShown(DataGrid grid, object item)
    {
        var rows = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(grid).OfType<DataGridRow>();
        var presenter = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(grid).OfType<Avalonia.Controls.Primitives.DataGridRowsPresenter>().FirstOrDefault();
        if (presenter == null) return false;
        foreach (var r in rows)
        {
            if (r.DataContext != item || !r.IsVisible) continue;
            var top = Avalonia.VisualExtensions.TranslatePoint(r, new Avalonia.Point(0, 0), presenter);
            return top is { } t && t.Y >= -0.5 && t.Y + r.Bounds.Height <= presenter.Bounds.Height + 0.5;
        }
        return false;
    }

    /// <summary>Scroll the picked player's row (or card) into view.</summary>
    public void KeepPickInView()
    {
        if (_vm?.SelectedMarket is not { } row) return;
        var grid = this.FindControl<DataGrid>("MarketGrid");
        if (grid != null && grid.IsEffectivelyVisible && _vm.Market.Contains(row) && !RowShown(grid, row)) grid.ScrollIntoView(row, null);
        var cards = this.FindControl<ItemsControl>("MarketCards");
        if (cards != null && cards.IsEffectivelyVisible && cards.ContainerFromItem(row) is Control card) card.BringIntoView();
    }

    /// <summary>Run 65: a position chip on the transfer market.</summary>
    private void OnPositionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TransactionsViewModel vm && sender is Button { Tag: string pos }) vm.SetMarketPosition(pos);
    }

    /// <summary>Run 67: a free agent's card picks him for talks.</summary>
    private void OnFreeAgentCardClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TransactionsViewModel vm && sender is Button { DataContext: FreeAgentRow row }) vm.PickFreeAgentCard(row);
    }

    /// <summary>Run 65: a player card on the transfer market picks him.</summary>
    private void OnCardClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TransactionsViewModel vm && sender is Button { DataContext: MarketRow row }) vm.PickMarket(row);
    }
}
