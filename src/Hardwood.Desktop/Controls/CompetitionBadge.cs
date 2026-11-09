using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

public enum BadgeKind { League, Playoffs, Finals, AllStar, Draft, Lottery, Champions }

/// <summary>Procedural competition marks: a ball-and-ring league logo, a bracket for the playoffs, a trophy for the Finals, a star, a podium for the draft.</summary>
public sealed class CompetitionBadge : Control
{
    public static readonly StyledProperty<BadgeKind> KindProperty = AvaloniaProperty.Register<CompetitionBadge, BadgeKind>(nameof(Kind));
    public static readonly StyledProperty<string?> CaptionProperty = AvaloniaProperty.Register<CompetitionBadge, string?>(nameof(Caption));

    public BadgeKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string? Caption { get => GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }

    static CompetitionBadge()
    {
        AffectsRender<CompetitionBadge>(KindProperty, CaptionProperty);
        WidthProperty.OverrideDefaultValue<CompetitionBadge>(48);
        HeightProperty.OverrideDefaultValue<CompetitionBadge>(48);
    }

    private static readonly Color Orange = Color.Parse("#F0A33A");
    private static readonly Color Gold = Color.Parse("#E6B422");
    private static readonly Color Ink = Color.Parse("#0F1216");

    public override void Render(DrawingContext ctx)
    {
        double s = Math.Min(Bounds.Width, Bounds.Height);
        if (s < 8) return;
        double cx = Bounds.Width / 2, cy = Bounds.Height / 2;
        var ring = new Pen(new SolidColorBrush(Orange), Math.Max(1.5, s * 0.07));
        switch (Kind)
        {
            case BadgeKind.League:
            {
                // Ball with seams inside a ring.
                ctx.DrawEllipse(new SolidColorBrush(Ink), ring, new Point(cx, cy), s * 0.46, s * 0.46);
                var ball = new SolidColorBrush(Orange);
                double r = s * 0.28;
                ctx.DrawEllipse(ball, null, new Point(cx, cy), r, r);
                var seam = new Pen(new SolidColorBrush(Ink), s * 0.035);
                ctx.DrawLine(seam, new Point(cx - r, cy), new Point(cx + r, cy));
                ctx.DrawLine(seam, new Point(cx, cy - r), new Point(cx, cy + r));
                ctx.DrawGeometry(null, seam, new EllipseGeometry(new Rect(cx - r * 1.5, cy - r * 0.55, r * 1.2, r * 1.1)));
                ctx.DrawGeometry(null, seam, new EllipseGeometry(new Rect(cx + r * 0.3, cy - r * 0.55, r * 1.2, r * 1.1)));
                break;
            }
            case BadgeKind.Playoffs:
            {
                ctx.DrawRectangle(new SolidColorBrush(Ink), ring, new RoundedRect(new Rect(cx - s * 0.46, cy - s * 0.46, s * 0.92, s * 0.92), s * 0.12));
                var pen = new Pen(new SolidColorBrush(Orange), s * 0.06);
                double x0 = cx - s * 0.3, x1 = cx - s * 0.05, x2 = cx + s * 0.2, x3 = cx + s * 0.32;
                foreach (var y in new[] { cy - s * 0.3, cy - s * 0.1, cy + s * 0.1, cy + s * 0.3 }) ctx.DrawLine(pen, new Point(x0, y), new Point(x1, y));
                ctx.DrawLine(pen, new Point(x1, cy - s * 0.3), new Point(x1, cy - s * 0.1)); ctx.DrawLine(pen, new Point(x1, cy + s * 0.1), new Point(x1, cy + s * 0.3));
                ctx.DrawLine(pen, new Point(x1, cy - s * 0.2), new Point(x2, cy - s * 0.2)); ctx.DrawLine(pen, new Point(x1, cy + s * 0.2), new Point(x2, cy + s * 0.2));
                ctx.DrawLine(pen, new Point(x2, cy - s * 0.2), new Point(x2, cy + s * 0.2)); ctx.DrawLine(pen, new Point(x2, cy), new Point(x3, cy));
                break;
            }
            case BadgeKind.Finals:
            case BadgeKind.Champions:
            {
                ctx.DrawEllipse(new SolidColorBrush(Ink), new Pen(new SolidColorBrush(Gold), Math.Max(1.5, s * 0.07)), new Point(cx, cy), s * 0.46, s * 0.46);
                var gold = new SolidColorBrush(Gold);
                var cup = new StreamGeometry();
                using (var g = cup.Open())
                {
                    g.BeginFigure(new Point(cx - s * 0.2, cy - s * 0.26), true);
                    g.LineTo(new Point(cx + s * 0.2, cy - s * 0.26));
                    g.QuadraticBezierTo(new Point(cx + s * 0.2, cy + s * 0.08), new Point(cx, cy + s * 0.1));
                    g.QuadraticBezierTo(new Point(cx - s * 0.2, cy + s * 0.08), new Point(cx - s * 0.2, cy - s * 0.26));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(gold, null, cup);
                ctx.DrawRectangle(gold, null, new Rect(cx - s * 0.03, cy + s * 0.1, s * 0.06, s * 0.1));
                ctx.DrawRectangle(gold, null, new RoundedRect(new Rect(cx - s * 0.14, cy + s * 0.2, s * 0.28, s * 0.06), 2));
                var handle = new Pen(gold, s * 0.04);
                ctx.DrawGeometry(null, handle, new EllipseGeometry(new Rect(cx - s * 0.32, cy - s * 0.22, s * 0.16, s * 0.2)));
                ctx.DrawGeometry(null, handle, new EllipseGeometry(new Rect(cx + s * 0.16, cy - s * 0.22, s * 0.16, s * 0.2)));
                break;
            }
            case BadgeKind.AllStar:
            {
                ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#1D3FA3")), new Pen(new SolidColorBrush(Color.Parse("#C8102E")), Math.Max(1.5, s * 0.07)), new Point(cx, cy), s * 0.46, s * 0.46);
                var star = new StreamGeometry();
                using (var g = star.Open())
                {
                    for (int i = 0; i < 10; i++)
                    {
                        double a = -Math.PI / 2 + i * Math.PI / 5;
                        double r = i % 2 == 0 ? s * 0.3 : s * 0.13;
                        var p = new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
                        if (i == 0) g.BeginFigure(p, true); else g.LineTo(p);
                    }
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(Brushes.White, null, star);
                break;
            }
            case BadgeKind.Draft:
            case BadgeKind.Lottery:
            {
                ctx.DrawRectangle(new SolidColorBrush(Ink), ring, new RoundedRect(new Rect(cx - s * 0.46, cy - s * 0.46, s * 0.92, s * 0.92), s * 0.12));
                var b = new SolidColorBrush(Orange);
                ctx.DrawRectangle(b, null, new Rect(cx - s * 0.33, cy, s * 0.2, s * 0.3));
                ctx.DrawRectangle(b, null, new Rect(cx - s * 0.1, cy - s * 0.2, s * 0.2, s * 0.5));
                ctx.DrawRectangle(b, null, new Rect(cx + s * 0.13, cy - s * 0.05, s * 0.2, s * 0.35));
                var one = new FormattedText("1", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.Bold), s * 0.22, new SolidColorBrush(Ink));
                ctx.DrawText(one, new Point(cx - one.Width / 2, cy - s * 0.2));
                break;
            }
        }
        if (!string.IsNullOrEmpty(Caption) && Bounds.Width > s + 20)
        {
            var t = new FormattedText(Caption!, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Inter", FontStyle.Normal, FontWeight.Bold), s * 0.42, Brushes.White);
            ctx.DrawText(t, new Point(s + 12, cy - t.Height / 2));
        }
    }
}
