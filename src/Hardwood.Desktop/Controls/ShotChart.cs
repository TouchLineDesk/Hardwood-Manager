using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

/// <summary>Half-court shot chart by zone: rim, short mid, long mid, corner three, above-the-break three. Colour = FG% vs league norm; label = makes/attempts.</summary>
public sealed class ShotChart : Control
{
    public static readonly StyledProperty<int[]?> AttemptsProperty = AvaloniaProperty.Register<ShotChart, int[]?>(nameof(Attempts));
    public static readonly StyledProperty<int[]?> MakesProperty = AvaloniaProperty.Register<ShotChart, int[]?>(nameof(Makes));

    public int[]? Attempts { get => GetValue(AttemptsProperty); set => SetValue(AttemptsProperty, value); }
    public int[]? Makes { get => GetValue(MakesProperty); set => SetValue(MakesProperty, value); }

    static ShotChart() => AffectsRender<ShotChart>(AttemptsProperty, MakesProperty);

    private static readonly double[] Norm = { 0.65, 0.44, 0.41, 0.39, 0.35 };
    private static readonly string[] Names = { "Rim", "Short mid", "Long mid", "Corner 3", "Above break 3" };

    public override void Render(DrawingContext ctx)
    {
        var att = Attempts; var mk = Makes;
        if (att == null || mk == null || att.Length < 5) return;
        double w = Bounds.Width, h = Bounds.Height;
        if (Bounds.Width < 40 || Bounds.Height < 40) return;
        // Court: baseline at bottom, basket near bottom centre; scale so the three-point arc fits.
        double scale = Math.Min(w / 50.0, h / 47.0);          // 50 ft wide, 47 ft half court
        double ox = (w - 50 * scale) / 2, oy = h - 47 * scale;
        Point P(double xFt, double yFt) => new(ox + (25 + xFt) * scale, oy + (47 - yFt) * scale); // x from centre, y from baseline
        var line = new Pen(new SolidColorBrush(Color.Parse("#8B96A5")), 1.5);
        var court = new SolidColorBrush(Color.Parse("#171D25"));
        ctx.DrawRectangle(court, line, new Rect(ox, oy, 50 * scale, 47 * scale));

        Color ZoneColor(int z)
        {
            if (att[z] == 0) return Color.FromArgb(40, 120, 130, 140);
            double pct = (double)mk[z] / att[z];
            double d = Math.Clamp((pct - Norm[z]) / 0.12, -1, 1);
            // blue (cold) → grey → red (hot)
            Color cold = Color.Parse("#2563EB"), mid = Color.Parse("#64748B"), hot = Color.Parse("#DC2626");
            Color c = d < 0 ? Lerp(mid, cold, -d) : Lerp(mid, hot, d);
            return Color.FromArgb(170, c.R, c.G, c.B);
        }
        static Color Lerp(Color a, Color b, double t) => Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

        // Zones as geometry: three-point arc radius 23.75 ft from basket (basket at x=0, y=5.25), corners at x=±22 up to y=14.
        var basket = P(0, 5.25);
        double r3 = 23.75 * scale, rLong = 16 * scale, rShort = 9 * scale, rRim = 4.5 * scale;
        var half = new RectangleGeometry(new Rect(ox, oy, 50 * scale, 47 * scale));
        var arc3 = new EllipseGeometry(new Rect(basket.X - r3, basket.Y - r3, r3 * 2, r3 * 2));
        var cornerBand = new RectangleGeometry(new Rect(ox, P(0, 14).Y, 50 * scale, 14 * scale)); // y 0..14 ft
        var leftCorner = new RectangleGeometry(new Rect(ox, P(0, 14).Y, 3 * scale, 14 * scale));
        var rightCorner = new RectangleGeometry(new Rect(P(22, 0).X, P(0, 14).Y, 3 * scale, 14 * scale));
        var inside3 = new CombinedGeometry(GeometryCombineMode.Union, arc3, new RectangleGeometry(new Rect(P(-22, 0).X, P(0, 14).Y, 44 * scale, 14 * scale)));
        var insideClip = new CombinedGeometry(GeometryCombineMode.Intersect, inside3, half);
        var beyond = new CombinedGeometry(GeometryCombineMode.Exclude, half, inside3);
        var corners = new CombinedGeometry(GeometryCombineMode.Union, leftCorner, rightCorner);
        var aboveBreak = new CombinedGeometry(GeometryCombineMode.Exclude, beyond, corners);
        var longMid = new CombinedGeometry(GeometryCombineMode.Exclude, insideClip, new EllipseGeometry(new Rect(basket.X - rLong, basket.Y - rLong, rLong * 2, rLong * 2)));
        var shortMid = new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(new Rect(basket.X - rLong, basket.Y - rLong, rLong * 2, rLong * 2)), new EllipseGeometry(new Rect(basket.X - rRim, basket.Y - rRim, rRim * 2, rRim * 2)));
        shortMid = new CombinedGeometry(GeometryCombineMode.Intersect, shortMid, half);
        var rim = new CombinedGeometry(GeometryCombineMode.Intersect, new EllipseGeometry(new Rect(basket.X - rRim, basket.Y - rRim, rRim * 2, rRim * 2)), half);

        ctx.DrawGeometry(new SolidColorBrush(ZoneColor(4)), line, aboveBreak);
        ctx.DrawGeometry(new SolidColorBrush(ZoneColor(3)), line, new CombinedGeometry(GeometryCombineMode.Intersect, corners, half));
        ctx.DrawGeometry(new SolidColorBrush(ZoneColor(2)), line, longMid);
        ctx.DrawGeometry(new SolidColorBrush(ZoneColor(1)), line, shortMid);
        ctx.DrawGeometry(new SolidColorBrush(ZoneColor(0)), line, rim);

        // Paint, free-throw circle, rim
        ctx.DrawRectangle(null, line, new Rect(P(-8, 19).X, P(0, 19).Y, 16 * scale, 19 * scale));
        ctx.DrawGeometry(null, line, new EllipseGeometry(new Rect(P(-6, 19).X, P(0, 25).Y, 12 * scale, 12 * scale)));
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#F0A33A")), 2), basket, 0.75 * scale, 0.75 * scale);

        var tf = new Typeface("Inter", FontStyle.Normal, FontWeight.Bold);
        void Label(int z, Point at)
        {
            string txt = att[z] == 0 ? "–" : $"{mk[z]}/{att[z]}\n{100.0 * mk[z] / att[z]:0}%";
            var t = new FormattedText(txt, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, Math.Max(9, scale * 1.9), Brushes.White) { TextAlignment = TextAlignment.Center };
            ctx.DrawText(t, new Point(at.X - t.Width / 2, at.Y - t.Height / 2));
        }
        Label(0, P(0, 7.5)); Label(1, P(0, 15.5)); Label(2, P(0, 24.5)); Label(3, P(-23.5, 7)); Label(4, P(0, 33));
        var cap = new FormattedText("Colour: hot/cold against league norm per zone", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter"), 10, new SolidColorBrush(Color.Parse("#8B96A5")));
        ctx.DrawText(cap, new Point(ox + 4, oy + 4));
    }
}
