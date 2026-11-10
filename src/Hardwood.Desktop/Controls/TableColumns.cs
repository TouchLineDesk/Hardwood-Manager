using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// Roadmap 1 (nothing cut off): when a table is narrower than its columns, Avalonia's DataGrid shrinks the
/// fixed-width columns to slivers to make room for its star (*) columns, so scores, dates and contracts vanished.
/// Pinning every fixed column's minimum to its width makes the table scroll sideways instead.
/// </summary>
public static class TableColumns
{
    /// <summary>The narrowest a star (*) column may get.</summary>
    public const double StarMin = 90;

    public static void Register() =>
        Control.LoadedEvent.AddClassHandler<DataGrid>((grid, _) => Pin(grid), RoutingStrategies.Direct);

    public static void Pin(DataGrid grid)
    {
        foreach (var col in grid.Columns)
        {
            if (col.Width.IsAbsolute && col.MinWidth < col.Width.Value)
                col.MinWidth = col.Width.Value;
            // A star column with no minimum was squeezed to nothing ("Your record", "Salary"); give it room for a header.
            else if (col.Width.IsStar && col.MinWidth < StarMin)
                col.MinWidth = StarMin;
        }
    }
}
