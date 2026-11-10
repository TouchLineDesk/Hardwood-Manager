using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

/// <summary>A Lucide line icon (see <see cref="IconData"/>), stroked in the current foreground colour.</summary>
public sealed class Icon : Control
{
    public static readonly StyledProperty<string?> KindProperty = AvaloniaProperty.Register<Icon, string?>(nameof(Kind));
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        TextBlock.ForegroundProperty.AddOwner<Icon>();
    public static readonly StyledProperty<double> StrokeProperty = AvaloniaProperty.Register<Icon, double>(nameof(Stroke), 2.0);

    public string? Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public IBrush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public double Stroke { get => GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

    private static readonly Dictionary<string, Geometry> Cache = new();

    static Icon()
    {
        AffectsRender<Icon>(KindProperty, ForegroundProperty, StrokeProperty);
        WidthProperty.OverrideDefaultValue<Icon>(18);
        HeightProperty.OverrideDefaultValue<Icon>(18);
    }

    public static Geometry? GeometryFor(string? kind)
    {
        if (kind == null || !IconData.Paths.TryGetValue(kind, out var data)) return null;
        lock (Cache)
        {
            if (!Cache.TryGetValue(kind, out var g)) Cache[kind] = g = Geometry.Parse(data);
            return g;
        }
    }

    public override void Render(DrawingContext ctx)
    {
        var g = GeometryFor(Kind);
        if (g == null) return;
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        double scale = size / 24.0;
        var pen = new Pen(Foreground ?? Brushes.White, Stroke, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - size) / 2, (Bounds.Height - size) / 2)))
            ctx.DrawGeometry(null, pen, g);
    }
}
