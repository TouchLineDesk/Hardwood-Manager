using Avalonia;
using Avalonia.Controls;

namespace Hardwood.Desktop.Views;

public partial class StandingsView : UserControl
{
    /// <summary>Below this width the two tables stack, so every column (and the team names) fits.</summary>
    public const double SideBySideWidth = 1060;
    private bool? _sideBySide;
    private bool? _cardsSide;
    /// <summary>Below this width the East and West division cards stack.</summary>
    public const double CardsSideBySideWidth = 820;

    public StandingsView()
    {
        InitializeComponent();
        Tables.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) Arrange(Tables.Bounds.Width); };
        Cards.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) ArrangeCards(Cards.Bounds.Width); };
    }

    private void ArrangeCards(double width)
    {
        bool side = width >= CardsSideBySideWidth;
        if (_cardsSide == side) return;
        _cardsSide = side;
        Cards.ColumnDefinitions = side ? new ColumnDefinitions("*,*") : new ColumnDefinitions("*");
        Cards.RowDefinitions = side ? new RowDefinitions("Auto") : new RowDefinitions("Auto,Auto");
        Grid.SetColumn(RightCardsPanel, side ? 1 : 0);
        Grid.SetRow(RightCardsPanel, side ? 0 : 1);
        LeftCardsPanel.Margin = side ? new Thickness(0, 0, 8, 0) : new Thickness(0);
    }

    private void Arrange(double width)
    {
        bool side = width >= SideBySideWidth;
        if (_sideBySide == side) return;
        _sideBySide = side;
        Tables.ColumnDefinitions = side ? new ColumnDefinitions("*,*") : new ColumnDefinitions("*");
        Tables.RowDefinitions = side ? new RowDefinitions("Auto") : new RowDefinitions("Auto,Auto");
        Grid.SetColumn(RightTable, side ? 1 : 0);
        Grid.SetRow(RightTable, side ? 0 : 1);
        LeftTable.Margin = side ? new Thickness(0, 0, 8, 12) : new Thickness(0, 0, 0, 12);
    }
}
