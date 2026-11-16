using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

/// <summary>Run 66 (review #13 rank 5): a 1–20 rating as a thin filled bar, so a staff card reads at a glance
/// instead of as a row of numbers. The fill is <see cref="Fill"/>; the track is a dark groove.</summary>
public sealed class RatingBar : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<RatingBar, double>(nameof(Value));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<RatingBar, double>(nameof(Maximum), 20);
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<RatingBar, IBrush?>(nameof(Fill));

    private static readonly IBrush Track = new SolidColorBrush(Color.Parse("#2A3340"));

    static RatingBar() => AffectsRender<RatingBar>(ValueProperty, MaximumProperty, FillProperty);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    /// <summary>The share of the track that is filled, 0–1 (public for the tests).</summary>
    public static double Share(double value, double max) => max <= 0 ? 0 : Math.Clamp(value / max, 0, 1);

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 80 : availableSize.Width, 7);

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width; var h = Math.Min(7, Bounds.Height);
        if (w <= 0) return;
        double y = (Bounds.Height - h) / 2;
        context.DrawRectangle(Track, null, new Rect(0, y, w, h), 3, 3);
        double fill = w * Share(Value, Maximum);
        if (fill > 0) context.DrawRectangle(Fill ?? Brushes.SteelBlue, null, new Rect(0, y, Math.Max(fill, h), h), 3, 3);
    }
}
