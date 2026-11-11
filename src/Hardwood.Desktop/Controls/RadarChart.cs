using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

/// <summary>Radar / spider chart of values on a 1–20 scale.</summary>
public sealed class RadarChart : Control
{
    public static readonly StyledProperty<double[]?> ValuesProperty = AvaloniaProperty.Register<RadarChart, double[]?>(nameof(Values));
    public static readonly StyledProperty<string[]?> LabelsProperty = AvaloniaProperty.Register<RadarChart, string[]?>(nameof(Labels));
    public static readonly StyledProperty<double> MaxProperty = AvaloniaProperty.Register<RadarChart, double>(nameof(Max), 20);
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<RadarChart, Color>(nameof(Accent), Color.Parse("#F0A33A"));

    public double[]? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public string[]? Labels { get => GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }
    public double Max { get => GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    static RadarChart() => AffectsRender<RadarChart>(ValuesProperty, LabelsProperty, MaxProperty, AccentProperty);

    public override void Render(DrawingContext ctx)
    {
        var values = Values;
        if (values == null || values.Length < 3) return;
        int n = values.Length;
        if (Bounds.Width < 40 || Bounds.Height < 40) return;
        double cx = Bounds.Width / 2, cy = Bounds.Height / 2;
        // Leave room for the widest label on each side so labels stay inside the control (they used to spill onto
        // the player card on the Player page).
        var labelFace = new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold);
        double widest = 0, tall = 14;
        for (int i = 0; i < n; i++)
        {
            string l = Labels != null && i < Labels.Length ? Labels[i] : "";
            var ft = new FormattedText($"{l} {values[i]:0.0}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, labelFace, 10.5, Brushes.White);
            widest = Math.Max(widest, ft.Width); tall = ft.Height;
        }
        double r = Math.Min(Bounds.Width / 2 - widest - 10, Bounds.Height / 2 - tall - 8) / 1.12;
        if (r < 20) return;
        var grid = new Pen(new SolidColorBrush(Color.Parse("#2A323D")), 1);
        var axis = new Pen(new SolidColorBrush(Color.Parse("#3A4654")), 1);
        Point At(int i, double frac) { var a = -Math.PI / 2 + i * 2 * Math.PI / n; return new Point(cx + r * frac * Math.Cos(a), cy + r * frac * Math.Sin(a)); }

        for (int ring = 1; ring <= 4; ring++)
        {
            var g = new StreamGeometry();
            using (var s = g.Open()) { s.BeginFigure(At(0, ring / 4.0), false); for (int i = 1; i < n; i++) s.LineTo(At(i, ring / 4.0)); s.EndFigure(true); }
            ctx.DrawGeometry(null, grid, g);
        }
        for (int i = 0; i < n; i++) ctx.DrawLine(axis, new Point(cx, cy), At(i, 1));

        var shape = new StreamGeometry();
        using (var s = shape.Open())
        {
            s.BeginFigure(At(0, Math.Clamp(values[0] / Max, 0, 1)), true);
            for (int i = 1; i < n; i++) s.LineTo(At(i, Math.Clamp(values[i] / Max, 0, 1)));
            s.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, Accent.R, Accent.G, Accent.B)), new Pen(new SolidColorBrush(Accent), 2), shape);
        for (int i = 0; i < n; i++) ctx.DrawEllipse(new SolidColorBrush(Accent), null, At(i, Math.Clamp(values[i] / Max, 0, 1)), 3, 3);

        var labels = Labels;
        var tf = new Typeface("Inter", FontStyle.Normal, FontWeight.SemiBold);
        for (int i = 0; i < n; i++)
        {
            string label = labels != null && i < labels.Length ? labels[i] : "";
            string val = values[i].ToString("0.0");
            var t = new FormattedText($"{label} {val}", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10.5, new SolidColorBrush(Color.Parse("#B6C0CC")));
            var p = At(i, 1.12);
            double ax = p.X < cx - 2 ? p.X - t.Width : p.X > cx + 2 ? p.X : p.X - t.Width / 2;
            ax = Math.Clamp(ax, 0, Math.Max(0, Bounds.Width - t.Width));
            double ay = Math.Clamp(p.Y - t.Height / 2, 0, Math.Max(0, Bounds.Height - t.Height));
            ctx.DrawText(t, new Point(ax, ay));
        }
    }
}
