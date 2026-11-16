using Avalonia.Controls;
using Avalonia.Interactivity;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class OffseasonView : UserControl
{
    public OffseasonView() => InitializeComponent();

    /// <summary>Run 67: a card on Contracts up opens talks with him.</summary>
    private void OnExpiringClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OffseasonViewModel vm && sender is Button { DataContext: ExpiringRow row }) vm.PickExpiring(row);
    }

    /// <summary>Run 67: a free agent's card opens talks with him.</summary>
    private void OnFreeAgentClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OffseasonViewModel vm && sender is Button { DataContext: FreeAgentRow row }) vm.PickFreeAgent(row);
    }

    /// <summary>Run 67: a graduate's card puts him top of your board.</summary>
    private void OnProspectClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is OffseasonViewModel vm && sender is Button { DataContext: ProspectRow row }) vm.PickProspect(row);
    }
}
