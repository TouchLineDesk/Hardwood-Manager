using Avalonia;
using Avalonia.Controls;

namespace Hardwood.Desktop.Views;

public partial class BoardView : UserControl
{
    /// <summary>Below this width the coach's attribute bars would shrink to slivers beside the club column.</summary>
    public const double SideBySideWidth = 860;
    private bool? _sideBySide;

    public BoardView()
    {
        InitializeComponent();
        Columns.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) Arrange(Columns.Bounds.Width); };
    }

    private void Arrange(double width)
    {
        bool side = width >= SideBySideWidth;
        if (_sideBySide == side) return;
        _sideBySide = side;
        Columns.ColumnDefinitions = side ? new ColumnDefinitions("3*,2*") : new ColumnDefinitions("*");
        ClubColumn.Margin = side ? new Thickness(0, 0, 12, 0) : new Thickness(0, 0, 0, 12);
        Grid.SetRow(CoachColumn, side ? 0 : 1);
        Grid.SetColumn(CoachColumn, side ? 1 : 0);
    }
}
