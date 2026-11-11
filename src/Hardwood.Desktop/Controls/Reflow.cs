using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// Roadmap 1 (nothing covered up): a two-part row — something on the left, something on the right — that drops the
/// right part onto a second line when both don't fit side by side, instead of overlapping or clipping. The grid
/// needs two rows (Auto,Auto); the right part starts in the last column of the first row.
/// </summary>
public sealed class Reflow
{
    private readonly Grid _grid;
    private readonly Control _left, _right;
    private readonly int _rightColumn;
    private readonly Thickness _rightMargin;
    private readonly HorizontalAlignment _rightAlign;
    private readonly double _gap;
    private readonly Func<double>? _leftNeed;
    public bool Stacked { get; private set; }
    /// <summary>Raised when the row stacks (true) or goes back side by side (false).</summary>
    public event Action<bool>? Stacking;

    /// <summary>The width a table needs to show every column without scrolling.</summary>
    public static double TableWidth(DataGrid grid) =>
        grid.Columns.Where(c => c.IsVisible).Sum(c => Math.Max(c.MinWidth, c.Width.IsAbsolute ? c.Width.Value : 0)) + 4;

    private Reflow(Grid grid, Control left, Control right, double gap, Func<double>? leftNeed)
    {
        _grid = grid; _left = left; _right = right; _gap = gap; _leftNeed = leftNeed;
        _rightColumn = Grid.GetColumn(right);
        _rightMargin = right.Margin;
        _rightAlign = right.HorizontalAlignment;
    }

    /// <param name="leftNeed">How wide the left part needs to be, when its desired size can't say (a table
    /// that would rather scroll than ask for room).</param>
    public static Reflow Watch(Grid grid, Control left, Control right, double gap = 24, Func<double>? leftNeed = null)
    {
        var r = new Reflow(grid, left, right, gap, leftNeed);
        grid.LayoutUpdated += (_, _) => r.Fit();
        return r;
    }

    private void Fit()
    {
        if (!_grid.IsEffectivelyVisible || _grid.Bounds.Width <= 0) return;
        // The right part's width side by side: its desired width with the side-by-side margin, whichever margin it has now
        // (otherwise the stacked margin changes the sum and the row flips back and forth for ever).
        double rightW = _right.DesiredSize.Width - _right.Margin.Left - _right.Margin.Right + _rightMargin.Left + _rightMargin.Right;
        double need = (_leftNeed?.Invoke() ?? _left.DesiredSize.Width) + rightW + _gap;
        // A little hysteresis so it doesn't flip back and forth at the boundary.
        bool stack = Stacked ? need > _grid.Bounds.Width - 8 : need > _grid.Bounds.Width;
        if (stack == Stacked) return;
        Stacked = stack;
        Grid.SetRow(_right, stack ? 1 : 0);
        Grid.SetColumn(_right, stack ? 0 : _rightColumn);
        Grid.SetColumnSpan(_right, stack ? Math.Max(1, _grid.ColumnDefinitions.Count) : 1);
        _right.HorizontalAlignment = stack ? HorizontalAlignment.Left : _rightAlign;
        _right.Margin = stack ? new Thickness(0, 8, 0, 0) : _rightMargin;
        Stacking?.Invoke(stack);
    }
}
