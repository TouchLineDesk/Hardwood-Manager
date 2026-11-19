using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class RosterView : UserControl
{
    public RosterView()
    {
        InitializeComponent();
        // CS3 (run 127): the class-year column shows for a college program. Columns aren't in the visual tree, so it's set here.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not RosterViewModel vm) return;
            void Show() { if (RosterGrid.Columns.FirstOrDefault(c => c.Header as string == "Yr") is { } col) col.IsVisible = vm.ShowClassYear; }
            Show();
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(RosterViewModel.ShowClassYear)) Show(); };
        };
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is RosterViewModel vm && vm.Selected != null) vm.Open(vm.Selected);
    }

    // The collector card flips on a click (D58 B); a double-click opens his page.
    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: PlayerRow row }) row.Open?.Execute(null);
    }
}
