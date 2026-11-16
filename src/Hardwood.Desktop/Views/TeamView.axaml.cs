using Avalonia.Controls;
using Avalonia.Input;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class TeamView : UserControl
{
    public TeamView() => InitializeComponent();

    // Run 70: the collector card flips on a click (D58 B); a double-click opens his page, as on Roster.
    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: TeamPlayerRow row }) row.Open?.Execute(null);
    }
}
