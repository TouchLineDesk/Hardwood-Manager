using Avalonia;
using Avalonia.Controls.Primitives;

namespace Hardwood.Desktop.Controls;

/// <summary>A grid of equal cards that picks its own column count from the width it gets (run 64): as many columns
/// as fit at <see cref="MinItemWidth"/>, at most <see cref="MaxColumns"/>, so cards never squeeze or run off the edge.</summary>
public sealed class CardGrid : UniformGrid
{
    public static readonly StyledProperty<double> MinItemWidthProperty = AvaloniaProperty.Register<CardGrid, double>(nameof(MinItemWidth), 280);
    public static readonly StyledProperty<int> MaxColumnsProperty = AvaloniaProperty.Register<CardGrid, int>(nameof(MaxColumns), 4);

    public double MinItemWidth { get => GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public int MaxColumns { get => GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }

    /// <summary>The column count for a width (public for the tests).</summary>
    public static int ColumnsFor(double width, double minItem, int max) =>
        double.IsInfinity(width) || width <= 0 ? max : Math.Clamp((int)(width / Math.Max(1, minItem)), 1, Math.Max(1, max));

    protected override Size MeasureOverride(Size availableSize)
    {
        int cols = ColumnsFor(availableSize.Width, MinItemWidth, MaxColumns);
        if (Columns != cols) Columns = cols;
        return base.MeasureOverride(availableSize);
    }
}
