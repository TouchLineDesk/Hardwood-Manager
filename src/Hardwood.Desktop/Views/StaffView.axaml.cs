using Avalonia.Controls;
using Avalonia.Interactivity;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class StaffView : UserControl
{
    public StaffView() => InitializeComponent();

    /// <summary>Run 66: a card on Your staff picks him (for Extend or Release).</summary>
    private void OnOursClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StaffViewModel vm && sender is Button { DataContext: StaffRow row }) vm.PickOurs(row);
    }

    /// <summary>Run 66: a card on the staff market picks him (for Interview).</summary>
    private void OnMarketClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StaffViewModel vm && sender is Button { DataContext: StaffRow row }) vm.PickMarket(row);
    }

    /// <summary>Run 66: a role chip on the staff market.</summary>
    private void OnRoleClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StaffViewModel vm && sender is Button { Tag: string role }) vm.SetRole(role);
    }

    /// <summary>Run 66: a vacant role's "Find one" shows the market for that role.</summary>
    private void OnFindClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StaffViewModel vm && sender is Button { DataContext: VacantRole v }) vm.FindFor(v.Role);
    }
}
