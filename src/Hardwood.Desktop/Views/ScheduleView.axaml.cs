using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class ScheduleView : UserControl
{
    public ScheduleView()
    {
        InitializeComponent();
        var games = Results.Children.OfType<DataGrid>().First();
        var reflow = Controls.Reflow.Watch(Page, Results, Side, leftNeed: () => Controls.Reflow.TableWidth(games));
        // Stacked: the side panel spans the page and its score list stays short so the results keep the room.
        reflow.Stacking += stacked =>
        {
            Side.Width = stacked ? double.NaN : 300;
            Scores.MaxHeight = stacked ? 130 : 300;
        };
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ScheduleViewModel vm && vm.Selected != null) vm.OpenBox(vm.Selected);
    }
}
