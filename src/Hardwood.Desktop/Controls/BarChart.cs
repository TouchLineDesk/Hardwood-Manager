using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

public sealed class Bar
{
    public string Label { get; init; } = "";
    public double Value { get; init; }
    public string? ValueText { get; init; }
    public Color Color { get; init; } = Color.Parse("#F0A33A");
}

public sealed class RefLine
{
    public string Label { get; init; } = "";
    public double Value { get; init; }
    public Color Color { get; init; } = Color.Parse("#8B96A5");
}

/// <summary>Simple vertical bar chart with optional horizontal reference lines (e.g. salary cap and tax line).</summary>
public sealed class BarChart : Control
{
    public static readonly StyledProperty<IReadOnlyList<Bar>?> BarsProperty = AvaloniaProperty.Register<BarChart, IReadOnlyList<Bar>?>(nameof(Bars));
    public static readonly StyledProperty<IReadOnlyList<RefLine>?> LinesProperty = AvaloniaProperty.Register<BarChart, IReadOnlyList<RefLine>?>(nameof(Lines));
    public IReadOnlyList<Bar>? Bars { get => GetValue(BarsProperty); set => SetValue(BarsProperty, value); }
    public IReadOnlyList<RefLine>? Lines { get => GetValue(LinesProperty); set => SetValue(LinesProperty, value); }

    static BarChart() => AffectsRender<BarChart>(BarsProperty, LinesProperty);

    private static FormattedText Text(string s, double size, Color c) =>
        new(s, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI"), size, new SolidColorBrush(c));

    public override void Render(DrawingContext ctx)
    {
        var bars = Bars;
        if (bars == null || bars.Count == 0) return;
        double w = Bounds.Width, h = Bounds.Height;
        double top = 16, bottom = 22, left = 6, right = 6;
        double max = Math.Max(bars.Max(b => b.Value), Lines?.Select(l => l.Value).DefaultIfEmpty(0).Max() ?? 0) * 1.1;
        if (max <= 0) max = 1;
        double plotH = h - top - bottom;
        double slot = (w - left - right) / bars.Count;
        double bw = Math.Min(48, slot * 0.6);
        var muted = Color.Parse("#8B96A5");
        for (int i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            double bh = plotH * Math.Max(0, b.Value) / max;
            double x = left + slot * i + (slot - bw) / 2;
            ctx.FillRectangle(new SolidColorBrush(b.Color), new Rect(x, top + plotH - bh, bw, bh), 3);
            var v = Text(b.ValueText ?? b.Value.ToString("0.#"), 11, Color.Parse("#E6EAF0"));
            ctx.DrawText(v, new Point(x + bw / 2 - v.Width / 2, top + plotH - bh - v.Height - 1));
            var l = Text(b.Label, 11, muted);
            ctx.DrawText(l, new Point(left + slot * i + slot / 2 - l.Width / 2, h - bottom + 4));
        }
        if (Lines != null)
            foreach (var line in Lines)
            {
                double y = top + plotH - plotH * line.Value / max;
                ctx.DrawLine(new Pen(new SolidColorBrush(line.Color), 1.2, new DashStyle(new double[] { 4, 3 }, 0)), new Point(left, y), new Point(w - right, y));
                var t = Text(line.Label, 10, line.Color);
                ctx.DrawText(t, new Point(w - right - t.Width, y - t.Height - 1));
            }
    }
}
