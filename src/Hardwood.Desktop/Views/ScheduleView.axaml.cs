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
        var games = GameTable;
        // The Event column is for college careers only (DataGrid columns can't bind IsVisible).
        // With it, the top performer column gives up a little room so the table still fits at 1480 px with the assistant.
        var eventColumn = games.Columns.First(c => c.Header as string == "Event");
        var topColumn = games.Columns.First(c => c.Header as string == "Top performer");
        void Apply(ScheduleViewModel vm) { eventColumn.IsVisible = vm.ShowEvent; topColumn.MinWidth = vm.ShowEvent ? 232 : 250; }
        // Run 72: the tiles open on your next game (or the last one played), not the season's first.
        void Reveal(ScheduleViewModel vm)
        {
            if (vm.Selected is { } row && vm.ShowCards)
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    // The row after it first, so the picked tile isn't left half under the page's edge.
                    int i = vm.Games.IndexOf(row);
                    if (i >= 0 && i + 3 < vm.Games.Count) GameTiles.ScrollIntoView(i + 3);
                    GameTiles.ScrollIntoView(row);
                }, Avalonia.Threading.DispatcherPriority.Background);
        }
        ScheduleViewModel? watched = null;
        void OnVmChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (s is not ScheduleViewModel vm) return;
            if (e.PropertyName == nameof(ScheduleViewModel.ShowEvent)) Apply(vm);
            if (e.PropertyName is nameof(ScheduleViewModel.Selected) or nameof(ScheduleViewModel.GameView)) Reveal(vm);
        }
        DataContextChanged += (_, _) =>
        {
            if (watched != null) watched.PropertyChanged -= OnVmChanged;
            watched = DataContext as ScheduleViewModel;
            if (watched == null) return;
            Apply(watched);
            watched.PropertyChanged += OnVmChanged;
            Reveal(watched);
        };
        // The tiles exist only once the list is laid out: reveal again then (the first visit, and back from a box score).
        GameTiles.Loaded += (_, _) => { if (DataContext is ScheduleViewModel vm) Reveal(vm); };
        // Tiles need room for two side by side; the table for every column.
        var reflow = Controls.Reflow.Watch(Page, Results, Side,
            leftNeed: () => DataContext is ScheduleViewModel { ShowCards: true } ? 2 * (204 + 14) + 20 : Controls.Reflow.TableWidth(games));
        // Stacked: the side panel spans the page and its score list stays short so the results keep the room.
        reflow.Stacking += stacked =>
        {
            Side.Width = stacked ? double.NaN : 300;
            Scores.MaxHeight = stacked ? 130 : 300;
            // Run 72: in the playoffs the series list stays short too, and the results keep at least 440 px
            // (the series cards had squeezed the table to nothing).
            SeriesList.MaxHeight = stacked ? 170 : 440;
            Page.RowDefinitions[0].MinHeight = stacked ? 440 : 0;
        };
    }

    private void OnFilterClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ScheduleViewModel vm && sender is Button { Tag: string name }) vm.SetFilter(name);
    }

    private void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ScheduleViewModel vm && vm.Selected != null) vm.OpenBox(vm.Selected);
    }
}
