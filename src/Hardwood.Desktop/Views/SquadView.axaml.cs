using Avalonia.Controls;
using Avalonia.Interactivity;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class SquadView : UserControl
{
    /// <summary>Room the cards need beside the side panel: the chemistry dial and its bars, or two cards side by side.</summary>
    public const double MainNeed = 440;

    public SquadView()
    {
        InitializeComponent();
        var reflow = Controls.Reflow.Watch(Page, Main, Side, gap: 12, leftNeed: () => MainNeed);
        // Stacked, the side panel spans the page and the whole page scrolls (the cards and the table show in full).
        reflow.Stacking += stacked =>
        {
            Side.Width = stacked ? double.NaN : 300;
            // The cards' row sizes to its content while the page scrolls (a star row there let the table's row come up short).
            Page.RowDefinitions[0].Height = stacked ? Avalonia.Controls.GridLength.Auto : new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star);
            PageScroll.VerticalScrollBarVisibility = stacked ? Avalonia.Controls.Primitives.ScrollBarVisibility.Auto : Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        };
    }

    /// <summary>Run 74: a player's card picks him for the side panel (his reasons and the talk buttons).</summary>
    private void OnCardClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SquadViewModel vm && (sender as Control)?.DataContext is MoodRow row) vm.Pick(row);
    }
}
