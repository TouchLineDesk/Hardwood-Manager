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
        // Bug hunt H18: college conferences have no divisions, so their Div column was empty. Hide it when no row has one.
        foreach (var grid in new[] { EastGrid, WestGrid })
        {
            var g = grid;
            g.PropertyChanged += (_, e) => { if (e.Property == DataGrid.ItemsSourceProperty) Watch(g); };
            Watch(g);
        }
    }

    private static void Watch(DataGrid grid)
    {
        if (grid.ItemsSource is System.Collections.Specialized.INotifyCollectionChanged live)
            live.CollectionChanged += (_, _) => ShowDivIfAny(grid);
        ShowDivIfAny(grid);
    }

    /// <summary>The Div column shows only when some row in the table has a division (public for the tests).</summary>
    public static void ShowDivIfAny(DataGrid grid)
    {
        if (grid.Columns.FirstOrDefault(c => c.Header as string == "Div") is not { } col) return;
        col.IsVisible = grid.ItemsSource is IEnumerable<ViewModels.StandingRow> rows && rows.Any(r => r.Div.Length > 0);
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
