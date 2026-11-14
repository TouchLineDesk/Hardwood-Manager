using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// A dial for one tactical slider (Danny, #475, D60 A: "the sliders as gauges"): a 240° arc filled to the value in a
/// colour that runs from cool (low) to hot (high), the value's word in the middle and the number under it.
/// </summary>
public sealed class Gauge : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Value));
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<Gauge, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<string?> WordProperty = AvaloniaProperty.Register<Gauge, string?>(nameof(Word));
    public static readonly StyledProperty<string?> NumberProperty = AvaloniaProperty.Register<Gauge, string?>(nameof(Number));

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string? Word { get => GetValue(WordProperty); set => SetValue(WordProperty, value); }
    public string? Number { get => GetValue(NumberProperty); set => SetValue(NumberProperty, value); }

    static Gauge() => AffectsRender<Gauge>(ValueProperty, MinimumProperty, MaximumProperty, WordProperty, NumberProperty);

    private static readonly Color Low = Color.Parse("#58A6FF"), Mid = Color.Parse("#3FB950"), High = Color.Parse("#F0883E");

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 120 : Math.Min(available.Width, 150);
        return new Size(w, w * 0.78);
    }

    public static Color Blend(double f)
    {
        f = Math.Clamp(f, 0, 1);
        (Color a, Color b, double t) = f < 0.5 ? (Low, Mid, f * 2) : (Mid, High, (f - 0.5) * 2);
        byte L(byte x, byte y) => (byte)(x + (y - x) * t);
        return Color.FromRgb(L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        double r = Math.Min(w / 2, h / 1.25) - 7;
        var c = new Point(w / 2, r + 6);
        double frac = Maximum > Minimum ? Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0, 1) : 0;
        const double start = 150, sweep = 240; // degrees, clockwise from the +x axis (screen coordinates)
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#2A333D")), 9, lineCap: PenLineCap.Round), Arc(c, r, start, sweep));
        if (frac > 0.005)
            ctx.DrawGeometry(null, new Pen(new SolidColorBrush(Blend(frac)), 9, lineCap: PenLineCap.Round), Arc(c, r, start, sweep * frac));
        // The needle's tip.
        double ang = (start + sweep * frac) * Math.PI / 180;
        ctx.DrawEllipse(Brushes.White, null, new Point(c.X + Math.Cos(ang) * r, c.Y + Math.Sin(ang) * r), 4, 4);

        var word = Text(Word ?? "", 13, FontWeight.Bold, Brushes.White, r * 1.7);
        ctx.DrawText(word, new Point(c.X - r * 0.85, c.Y - word.Height / 2 - 3));
        if (!string.IsNullOrEmpty(Number))
        {
            var num = Text(Number, 10.5, FontWeight.Normal, new SolidColorBrush(Color.Parse("#8B96A5")), r * 1.7);
            ctx.DrawText(num, new Point(c.X - r * 0.85, c.Y + word.Height / 2 - 1));
        }
    }

    private static StreamGeometry Arc(Point c, double r, double startDeg, double sweepDeg)
    {
        var g = new StreamGeometry();
        using var s = g.Open();
        int steps = Math.Max(2, (int)(sweepDeg / 4));
        for (int i = 0; i <= steps; i++)
        {
            double a = (startDeg + sweepDeg * i / steps) * Math.PI / 180;
            var p = new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
            if (i == 0) s.BeginFigure(p, false); else s.LineTo(p);
        }
        s.EndFigure(false);
        return g;
    }

    private static FormattedText Text(string s, double size, FontWeight weight, IBrush brush, double maxWidth)
    {
        var ft = new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI", FontStyle.Normal, weight), size, brush);
        ft.MaxTextWidth = Math.Max(10, maxWidth);
        ft.TextAlignment = TextAlignment.Center;
        return ft;
    }
}
