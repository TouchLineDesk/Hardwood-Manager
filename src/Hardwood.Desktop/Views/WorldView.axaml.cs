using Avalonia;
using Avalonia.Controls;

namespace Hardwood.Desktop.Views;

public partial class WorldView : UserControl
{
    private bool? _side;

    public WorldView()
    {
        InitializeComponent();
        // The division cards sit East beside West when there's room, else stack (as on Standings).
        LeagueCards.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) Arrange(LeagueCards.Bounds.Width); };
    }

    private void Arrange(double width)
    {
        bool side = width >= StandingsView.CardsSideBySideWidth;
        if (_side == side) return;
        _side = side;
        LeagueCards.ColumnDefinitions = side ? new ColumnDefinitions("*,*") : new ColumnDefinitions("*");
        LeagueCards.RowDefinitions = side ? new RowDefinitions("Auto") : new RowDefinitions("Auto,Auto");
        Grid.SetColumn(RightLeagueCards, side ? 1 : 0);
        Grid.SetRow(RightLeagueCards, side ? 0 : 1);
        LeftLeagueCards.Margin = side ? new Thickness(0, 0, 8, 0) : new Thickness(0);
    }
}
