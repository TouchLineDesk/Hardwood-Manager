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
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as RosterViewModel;
            if (_vm == null) return;
            _vm.PropertyChanged += OnVmChanged;
            ShowClassColumn();
        };
    }

    private RosterViewModel? _vm;
    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RosterViewModel.ShowClassYear)) ShowClassColumn();
    }
    private void ShowClassColumn()
    {
        if (_vm != null && RosterGrid.Columns.FirstOrDefault(c => c.Header as string == "Yr") is { } col) col.IsVisible = _vm.ShowClassYear;
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
