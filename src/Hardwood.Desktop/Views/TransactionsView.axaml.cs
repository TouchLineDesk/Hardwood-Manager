using Avalonia.Controls;
using Avalonia.Interactivity;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class TransactionsView : UserControl
{
    public TransactionsView() => InitializeComponent();

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
