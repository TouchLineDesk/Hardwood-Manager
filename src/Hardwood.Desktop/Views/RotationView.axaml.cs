using Avalonia.Controls;
using Avalonia;
using Avalonia.Interactivity;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class RotationView : UserControl
{
    public RotationView()
    {
        InitializeComponent();
        // Run 73: the court needs its full width beside the minutes; narrower, the minutes go underneath.
        var reflow = Controls.Reflow.Watch(DepthGrid, CourtColumn, MinutesPanel, gap: 16, leftNeed: () => RotationViewModel.CourtWidth);
        // Side by side the court keeps its natural width; stacked it takes the page's width (and scrolls if that is narrower).
        // The second column closes when stacked, or the minutes panel spanning both would size it and squeeze the court.
        reflow.Stacking += stacked =>
        {
            DepthGrid.ColumnDefinitions[0].Width = stacked ? GridLength.Star : GridLength.Auto;
            DepthGrid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : GridLength.Auto;
        };
    }

    // Run 73: the depth chart. A starter's card picks his spot; a bench chip starts that player there.
    private void OnSlotClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RotationViewModel vm && (sender as Control)?.DataContext is StarterSlot slot) vm.PickSlot(slot);
    }

    private void OnBenchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is RotationViewModel vm && (sender as Control)?.DataContext is RotationRow row) vm.SwapIn(row);
    }
}
