using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

public sealed class LineSeries
{
    public string Name { get; init; } = "";
    public double[] Values { get; init; } = Array.Empty<double>();
    public Color Color { get; init; } = Color.Parse("#F0A33A");
    public bool Emphasis { get; init; }
}

/// <summary>Multi-series line chart with a zero baseline option; x is the index.</summary>
public sealed class LineChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<LineSeries>?> SeriesProperty = AvaloniaProperty.Register<LineChart, IReadOnlyList<LineSeries>?>(nameof(Series));
    public static readonly StyledProperty<string[]?> XLabelsProperty = AvaloniaProperty.Register<LineChart, string[]?>(nameof(XLabels));
    public static readonly StyledProperty<string?> YTitleProperty = AvaloniaProperty.Register<LineChart, string?>(nameof(YTitle));
    public static readonly StyledProperty<bool> ZeroLineProperty = AvaloniaProperty.Register<LineChart, bool>(nameof(ZeroLine));
    /// <summary>Fixed bottom/top of the y axis (NaN = fit the data), e.g. 0 and 100 for a 0–100 meter.</summary>
    public static readonly StyledProperty<double> MinYProperty = AvaloniaProperty.Register<LineChart, double>(nameof(MinY), double.NaN);
    public static readonly StyledProperty<double> MaxYProperty = AvaloniaProperty.Register<LineChart, double>(nameof(MaxY), double.NaN);

    public IReadOnlyList<LineSeries>? Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public string[]? XLabels { get => GetValue(XLabelsProperty); set => SetValue(XLabelsProperty, value); }
    public string? YTitle { get => GetValue(YTitleProperty); set => SetValue(YTitleProperty, value); }
    public bool ZeroLine { get => GetValue(ZeroLineProperty); set => SetValue(ZeroLineProperty, value); }
    public double MinY { get => GetValue(MinYProperty); set => SetValue(MinYProperty, value); }
    public double MaxY { get => GetValue(MaxYProperty); set => SetValue(MaxYProperty, value); }

    static LineChart() => AffectsRender<LineChart>(SeriesProperty, XLabelsProperty, YTitleProperty, ZeroLineProperty, MinYProperty, MaxYProperty);

    public override void Render(DrawingContext ctx)
    {
        var series = Series;
        if (series == null || series.Count == 0 || series.All(s => s.Values.Length == 0)) return;
        double left = 44, right = 12, top = 10, bottom = 26;
        double w = Bounds.Width - left - right, h = Bounds.Height - top - bottom;
        if (w <= 0 || h <= 0) return;
        int n = series.Max(s => s.Values.Length);
        double min = series.SelectMany(s => s.Values).Min(), max = series.SelectMany(s => s.Values).Max();
        if (ZeroLine) { min = Math.Min(min, 0); max = Math.Max(max, 0); }
        if (!double.IsNaN(MinY)) min = Math.Min(min, MinY);
        if (!double.IsNaN(MaxY)) max = Math.Max(max, MaxY);
        var ticks = NiceTicks(min, max);
        min = ticks[0]; max = ticks[^1];
        double X(int i) => left + (n <= 1 ? w / 2 : w * i / (n - 1));
        double Y(double v) => top + h - (v - min) / (max - min) * h;

        var grid = new Pen(new SolidColorBrush(Color.Parse("#2A323D")), 1);
        var muted = new SolidColorBrush(Color.Parse("#8B96A5"));
        var tf = new Typeface("Inter");
        foreach (var v in ticks)
        {
            double y = Y(v);
            ctx.DrawLine(grid, new Point(left, y), new Point(left + w, y));
            var t = new FormattedText(TickText(v), System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10, muted);
            ctx.DrawText(t, new Point(left - t.Width - 6, y - t.Height / 2));
        }
        if (ZeroLine && min < 0 && max > 0) ctx.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#4B5563")), 1.5), new Point(left, Y(0)), new Point(left + w, Y(0)));
        var labels = XLabels;
        if (labels != null)
        {
            int step = Math.Max(1, labels.Length / 8);
            for (int i = 0; i < labels.Length && i < n; i += step)
            {
                var t = new FormattedText(labels[i], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10, muted);
                ctx.DrawText(t, new Point(Math.Clamp(X(i) - t.Width / 2, 0, Math.Max(0, Bounds.Width - t.Width)), top + h + 6));
            }
        }
        foreach (var s in series.OrderBy(s => s.Emphasis))
        {
            if (s.Values.Length == 0) continue;
            var geo = new StreamGeometry();
            using (var c = geo.Open())
            {
                c.BeginFigure(new Point(X(0), Y(s.Values[0])), false);
                for (int i = 1; i < s.Values.Length; i++) c.LineTo(new Point(X(i), Y(s.Values[i])));
                c.EndFigure(false);
            }
            var pen = new Pen(new SolidColorBrush(s.Emphasis ? s.Color : Color.FromArgb(120, s.Color.R, s.Color.G, s.Color.B)), s.Emphasis ? 2.5 : 1.2);
            ctx.DrawGeometry(null, pen, geo);
            if (s.Emphasis || series.Count == 1)
                for (int i = 0; i < s.Values.Length; i++) ctx.DrawEllipse(new SolidColorBrush(s.Color), null, new Point(X(i), Y(s.Values[i])), 2.5, 2.5);
        }
        if (!string.IsNullOrEmpty(YTitle))
        {
            var t = new FormattedText(YTitle!, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10, muted);
            ctx.DrawText(t, new Point(left, 0));
        }
    }

    /// <summary>
    /// Round y-axis ticks (1, 2, 2.5 or 5 × a power of ten) that cover [min, max] with 3–6 gridlines,
    /// so the axis reads 0 · 25 · 50 · 75 · 100 rather than 54 · 56.6 · 59.2.
    /// </summary>
    public static double[] NiceTicks(double min, double max)
    {
        if (double.IsNaN(min) || double.IsNaN(max)) return new[] { 0.0, 1.0 };
        if (max - min < 1e-9) { min -= 1; max += 1; }
        double raw = (max - min) / 4;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double step = new[] { 1, 2, 2.5, 5, 10 }.Select(f => f * mag).First(st => st >= raw - 1e-12);
        double lo = Math.Floor(min / step + 1e-9) * step, hi = Math.Ceiling(max / step - 1e-9) * step;
        if (hi - lo < 1e-12) hi = lo + step;
        var list = new List<double>();
        for (double v = lo; v <= hi + step / 2; v += step) list.Add(Math.Abs(v) < step * 1e-9 ? 0 : Math.Round(v, 10));
        return list.ToArray();
    }

    private static string TickText(double v) => Math.Abs(v) >= 1000 ? v.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture) : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
