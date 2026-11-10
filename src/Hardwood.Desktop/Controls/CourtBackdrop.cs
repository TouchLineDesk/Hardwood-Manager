using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// A dark, low-contrast arena floor drawn in code (decision #63's code-drawn half): maple planks, faint court lines,
/// an accent-coloured glow and a vignette so text on top stays readable.
/// </summary>
public sealed class CourtBackdrop : Control
{
    public static readonly StyledProperty<Color> GlowProperty = AvaloniaProperty.Register<CourtBackdrop, Color>(nameof(Glow), Color.Parse("#F0A33A"));
    public static readonly StyledProperty<double> IntensityProperty = AvaloniaProperty.Register<CourtBackdrop, double>(nameof(Intensity), 1.0);
    public Color Glow { get => GetValue(GlowProperty); set => SetValue(GlowProperty, value); }
    /// <summary>0..1: how visible the floor is (headers use less).</summary>
    public double Intensity { get => GetValue(IntensityProperty); set => SetValue(IntensityProperty, value); }

    static CourtBackdrop() { AffectsRender<CourtBackdrop>(GlowProperty, IntensityProperty); }

    public override void Render(DrawingContext ctx)
    {
        var b = new Rect(Bounds.Size);
        if (b.Width <= 0 || b.Height <= 0) return;
        double k = Math.Clamp(Intensity, 0, 1);
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#0F1216")), b);

        // Planks: alternating warm stripes at very low opacity, with thin seams.
        double plank = Math.Max(26, b.Height / 18);
        int i = 0;
        for (double y = 0; y < b.Height; y += plank, i++)
        {
            byte a = (byte)((i % 3 == 0 ? 26 : i % 3 == 1 ? 18 : 22) * k);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(a, 0xC9, 0x8A, 0x3C)), new Rect(0, y, b.Width, plank));
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)(40 * k), 0, 0, 0)), 1), new Point(0, y), new Point(b.Width, y));
            // Staggered plank ends.
            double off = (i * 137) % 260;
            for (double x = off; x < b.Width; x += 260 + (i % 4) * 40)
                ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)(30 * k), 0, 0, 0)), 1), new Point(x, y), new Point(x, y + plank));
        }

        // Court lines: centre circle and a three-point arc on the right, in faint white.
        var line = new Pen(new SolidColorBrush(Color.FromArgb((byte)(28 * k), 255, 255, 255)), 3);
        double r = b.Height * 0.32;
        ctx.DrawEllipse(null, line, new Point(b.Width * 0.18, b.Height * 0.5), r, r);
        ctx.DrawLine(line, new Point(b.Width * 0.18 + r * 1.9, 0), new Point(b.Width * 0.18 + r * 1.9, b.Height));
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            double cx = b.Width, cy = b.Height / 2, ar = b.Height * 0.55;
            g.BeginFigure(new Point(cx, cy - ar), false);
            g.ArcTo(new Point(cx, cy + ar), new Size(ar, ar), 0, false, SweepDirection.CounterClockwise);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, line, arc);

        // Accent glow from the top-left, then a vignette.
        var glow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.15, 0.1, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.15, 0.1, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.9, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb((byte)(55 * k), Glow.R, Glow.G, Glow.B), 0), new GradientStop(Color.FromArgb(0, Glow.R, Glow.G, Glow.B), 1) },
        };
        ctx.FillRectangle(glow, b);
        var vignette = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.5), new GradientStop(Color.FromArgb(170, 0, 0, 0), 1) },
        };
        ctx.FillRectangle(vignette, b);
    }
}
