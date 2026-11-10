using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.Controls;

/// <summary>Procedural crest for a team: brand colours, a shape by style, a motif stripe and the abbreviation.</summary>
public sealed class TeamLogo : Control
{
    public static readonly StyledProperty<Team?> TeamProperty = AvaloniaProperty.Register<TeamLogo, Team?>(nameof(Team));
    public static readonly StyledProperty<bool> ShowTextProperty = AvaloniaProperty.Register<TeamLogo, bool>(nameof(ShowText), true);

    public Team? Team { get => GetValue(TeamProperty); set => SetValue(TeamProperty, value); }
    public bool ShowText { get => GetValue(ShowTextProperty); set => SetValue(ShowTextProperty, value); }

    static TeamLogo()
    {
        AffectsRender<TeamLogo>(TeamProperty, ShowTextProperty);
        WidthProperty.OverrideDefaultValue<TeamLogo>(48);
        HeightProperty.OverrideDefaultValue<TeamLogo>(48);
    }

    public static Color Parse(string hex) => Color.Parse(hex);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Graphics.LogoPackService.Changed += InvalidateVisual;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Graphics.LogoPackService.Changed -= InvalidateVisual;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>Logo pack image, fitted and centred, keeping its proportions.</summary>
    private bool DrawPackLogo(DrawingContext ctx, Team team)
    {
        if (Graphics.LogoPackService.BitmapFor(team) is not { } bmp) return false;
        var src = bmp.Size;
        if (src.Width <= 0 || src.Height <= 0) return false;
        double scale = Math.Min(Bounds.Width / src.Width, Bounds.Height / src.Height);
        double w = src.Width * scale, h = src.Height * scale;
        ctx.DrawImage(bmp, new Rect(0, 0, src.Width, src.Height), new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h));
        return true;
    }

    public override void Render(DrawingContext ctx)
    {
        var team = Team;
        if (team == null) return;
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size < 8) return;
        if (DrawPackLogo(ctx, team)) return;
        var cx = Bounds.Width / 2; var cy = Bounds.Height / 2;
        var primary = Parse(team.PrimaryColor);
        var secondary = Parse(team.SecondaryColor);
        var pb = new SolidColorBrush(primary);
        var sb = new SolidColorBrush(secondary);
        var outline = new Pen(new SolidColorBrush(Darken(primary, 0.45)), Math.Max(1, size * 0.045));

        Geometry shape = team.LogoStyle switch
        {
            1 => new EllipseGeometry(new Rect(cx - size / 2, cy - size / 2, size, size)),
            2 => Polygon(cx, cy, size / 2, 6, -Math.PI / 2),
            3 => Polygon(cx, cy, size / 2, 4, -Math.PI / 2),
            4 => Banner(cx, cy, size),
            _ => Shield(cx, cy, size),
        };

        ctx.DrawGeometry(pb, outline, shape);
        using (ctx.PushGeometryClip(shape))
        {
            // Motif: a diagonal sash in the secondary colour, plus a subtle highlight.
            var sash = new StreamGeometry();
            using (var g = sash.Open())
            {
                g.BeginFigure(new Point(cx - size * 0.6, cy + size * 0.05), true);
                g.LineTo(new Point(cx + size * 0.6, cy - size * 0.35));
                g.LineTo(new Point(cx + size * 0.6, cy - size * 0.15));
                g.LineTo(new Point(cx - size * 0.6, cy + size * 0.25));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(sb, null, sash);
            var hi = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(70, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.6) },
            };
            ctx.DrawGeometry(hi, null, shape);
        }

        if (ShowText && size >= 28)
        {
            var text = new FormattedText(team.Abbreviation, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.ExtraBold), size * 0.30, Brushes.White);
            var shadow = new FormattedText(team.Abbreviation, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Inter", FontStyle.Normal, FontWeight.ExtraBold), size * 0.30, new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)));
            var origin = new Point(cx - text.Width / 2, cy - text.Height / 2 + size * 0.02);
            ctx.DrawText(shadow, origin + new Vector(size * 0.02, size * 0.02));
            ctx.DrawText(text, origin);
        }
    }

    private static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));

    private static Geometry Polygon(double cx, double cy, double r, int n, double rot)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        for (int i = 0; i < n; i++)
        {
            var a = rot + i * 2 * Math.PI / n;
            var p = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            if (i == 0) c.BeginFigure(p, true); else c.LineTo(p);
        }
        c.EndFigure(true);
        return g;
    }

    private static Geometry Shield(double cx, double cy, double s)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        double w = s * 0.5, top = cy - s * 0.48, bottom = cy + s * 0.5;
        c.BeginFigure(new Point(cx - w, top), true);
        c.LineTo(new Point(cx + w, top));
        c.LineTo(new Point(cx + w, cy + s * 0.05));
        c.QuadraticBezierTo(new Point(cx + w, cy + s * 0.38), new Point(cx, bottom));
        c.QuadraticBezierTo(new Point(cx - w, cy + s * 0.38), new Point(cx - w, cy + s * 0.05));
        c.EndFigure(true);
        return g;
    }

    private static Geometry Banner(double cx, double cy, double s)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        double w = s * 0.5, top = cy - s * 0.46, bottom = cy + s * 0.5;
        c.BeginFigure(new Point(cx - w, top), true);
        c.LineTo(new Point(cx + w, top));
        c.LineTo(new Point(cx + w, bottom));
        c.LineTo(new Point(cx, bottom - s * 0.18));
        c.LineTo(new Point(cx - w, bottom));
        c.EndFigure(true);
        return g;
    }
}
