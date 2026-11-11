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
        // The Event column is for college careers only (DataGrid columns can't bind IsVisible).
        // With it, the top performer column gives up a little room so the table still fits at 1480 px with the assistant.
        var eventColumn = games.Columns.First(c => c.Header as string == "Event");
        var topColumn = games.Columns.First(c => c.Header as string == "Top performer");
        void Apply(ScheduleViewModel vm) { eventColumn.IsVisible = vm.ShowEvent; topColumn.MinWidth = vm.ShowEvent ? 214 : 250; }
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not ScheduleViewModel vm) return;
            Apply(vm);
            vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ScheduleViewModel.ShowEvent)) Apply(vm); };
        };
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
