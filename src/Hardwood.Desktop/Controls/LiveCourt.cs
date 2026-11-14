using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hardwood.Core.Engine;

namespace Hardwood.Desktop.Controls;

/// <summary>One shot on the live court: which side took it, from which zone, made or missed, and a stable spot in the zone.</summary>
public sealed record LiveShot(bool Home, ShotZone Zone, bool Made, int Seq)
{
    /// <summary>The spot in court feet (x 0..94 baseline to baseline, y 0..50 sideline to sideline). Home attacks the
    /// right-hand basket, away the left; the spot inside the zone is fixed by the shot's sequence number.</summary>
    public Point Spot
    {
        get
        {
            var rng = new Random(Seq * 7919 + (int)Zone * 31 + (Home ? 1 : 0));
            double r, a; // distance from the basket (ft) and angle (radians, 0 = straight out to the floor)
            switch (Zone)
            {
                case ShotZone.Rim: r = 1 + rng.NextDouble() * 3.5; a = (rng.NextDouble() - 0.5) * 2.6; break;
                case ShotZone.ShortMid: r = 6 + rng.NextDouble() * 7; a = (rng.NextDouble() - 0.5) * 2.4; break;
                case ShotZone.LongMid: r = 15 + rng.NextDouble() * 6.5; a = (rng.NextDouble() - 0.5) * 2.2; break;
                case ShotZone.CornerThree:
                {
                    double along = 0.5 + rng.NextDouble() * 9;         // feet out from the baseline
                    double side = rng.NextDouble() < 0.5 ? 1.5 + rng.NextDouble() * 1.5 : 50 - 1.5 - rng.NextDouble() * 1.5;
                    return new Point(Home ? 94 - along : along, side);
                }
                default: r = 24.5 + rng.NextDouble() * 3.5; a = (rng.NextDouble() - 0.5) * 2.0; break;
            }
            double bx = Home ? 94 - 5.25 : 5.25;
            double dx = Math.Cos(a) * r * (Home ? -1 : 1), dy = Math.Sin(a) * r;
            return new Point(Math.Clamp(bx + dx, 0.8, 93.2), Math.Clamp(25 + dy, 0.8, 49.2));
        }
    }
}

/// <summary>
/// The whole court for the live game (Danny, run 45): the home program's own floor from his college pack, or a drawn
/// court, with every shot of the game placed as it happens: a filled dot for a make, a ring for a miss, in the shooting
/// side's colour, the latest one ringed.
/// </summary>
public sealed class LiveCourt : Control
{
    public static readonly StyledProperty<IImage?> FloorProperty = AvaloniaProperty.Register<LiveCourt, IImage?>(nameof(Floor));
    public static readonly StyledProperty<IReadOnlyList<LiveShot>?> ShotsProperty = AvaloniaProperty.Register<LiveCourt, IReadOnlyList<LiveShot>?>(nameof(Shots));
    public static readonly StyledProperty<int> VersionProperty = AvaloniaProperty.Register<LiveCourt, int>(nameof(Version));
    public static readonly StyledProperty<Color> HomeColorProperty = AvaloniaProperty.Register<LiveCourt, Color>(nameof(HomeColor), Colors.OrangeRed);
    public static readonly StyledProperty<Color> AwayColorProperty = AvaloniaProperty.Register<LiveCourt, Color>(nameof(AwayColor), Colors.DodgerBlue);

    public IImage? Floor { get => GetValue(FloorProperty); set => SetValue(FloorProperty, value); }
    public IReadOnlyList<LiveShot>? Shots { get => GetValue(ShotsProperty); set => SetValue(ShotsProperty, value); }
    public int Version { get => GetValue(VersionProperty); set => SetValue(VersionProperty, value); }
    public Color HomeColor { get => GetValue(HomeColorProperty); set => SetValue(HomeColorProperty, value); }
    public Color AwayColor { get => GetValue(AwayColorProperty); set => SetValue(AwayColorProperty, value); }

    static LiveCourt() => AffectsRender<LiveCourt>(FloorProperty, ShotsProperty, VersionProperty, HomeColorProperty, AwayColorProperty);

    /// <summary>A court is 94 by 50 feet.</summary>
    public const double Aspect = 94.0 / 50.0;

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 470 : available.Width;
        double h = w / Aspect;
        if (!double.IsInfinity(available.Height) && h > available.Height) { h = available.Height; w = h * Aspect; }
        return new Size(w, h);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 60 || h < 32) return;
        double s = Math.Min(w / 94, h / 50);
        double ox = (w - 94 * s) / 2, oy = (h - 50 * s) / 2;
        var rect = new Rect(ox, oy, 94 * s, 50 * s);
        Point P(double x, double y) => new(ox + x * s, oy + y * s);

        if (Floor is { } img)
        {
            // The pack's court fills the court rectangle (cropped to 94:50 if its shape differs).
            var src = img.Size;
            double scale = Math.Max(rect.Width / src.Width, rect.Height / src.Height);
            double sw = rect.Width / scale, sh = rect.Height / scale;
            var srcRect = new Rect((src.Width - sw) / 2, (src.Height - sh) / 2, sw, sh);
            using (ctx.PushClip(new RoundedRect(rect, 6)))
                ctx.DrawImage(img, srcRect, rect);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 8, 10, 14)), null, rect, 6, 6);
        }
        else
        {
            ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#B9824F")), null, rect, 6, 6);
            var plank = new Pen(new SolidColorBrush(Color.FromArgb(40, 90, 50, 20)), 1);
            for (double y = 0; y <= 50; y += 2.5) ctx.DrawLine(plank, P(0, y), P(94, y));
            var line = new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), Math.Max(1, s * 0.25));
            ctx.DrawRectangle(null, line, rect);
            ctx.DrawLine(line, P(47, 0), P(47, 50));
            ctx.DrawEllipse(null, line, P(47, 25), 6 * s, 6 * s);
            foreach (bool right in new[] { false, true })
            {
                double bx = right ? 94 - 5.25 : 5.25, edge = right ? 94 : 0, dir = right ? -1 : 1;
                var paint = new Rect(right ? P(94 - 19, 17).X : P(0, 17).X, P(0, 17).Y, 19 * s, 16 * s);
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 120, 40, 20)), line, paint);
                ctx.DrawEllipse(null, line, P(edge + dir * 19, 25), 6 * s, 6 * s);
                ctx.DrawLine(line, P(edge, 3), P(edge + dir * 14, 3));
                ctx.DrawLine(line, P(edge, 47), P(edge + dir * 14, 47));
                // The arc: from the corner lines round through the top of the key.
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    double ang0 = Math.Asin(22 / 23.75);
                    g.BeginFigure(P(edge + dir * 14, 3), false);
                    for (int i = 0; i <= 40; i++)
                    {
                        double t = -ang0 + 2 * ang0 * i / 40.0;
                        g.LineTo(P(bx + dir * Math.Cos(t) * 23.75, 25 + Math.Sin(t) * 23.75));
                    }
                    g.LineTo(P(edge + dir * 14, 47));
                    g.EndFigure(false);
                }
                ctx.DrawGeometry(null, line, geo);
                ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#F0A33A")), Math.Max(1, s * 0.3)), P(bx, 25), 0.75 * s, 0.75 * s);
            }
        }

        var shots = Shots;
        if (shots == null || shots.Count == 0) return;
        double r = Math.Max(3, s * 0.85);
        for (int i = 0; i < shots.Count; i++)
        {
            var sh = shots[i];
            var c = sh.Home ? HomeColor : AwayColor;
            var p = P(sh.Spot.X, sh.Spot.Y);
            bool latest = i == shots.Count - 1;
            if (sh.Made)
                ctx.DrawEllipse(new SolidColorBrush(c), new Pen(Brushes.White, 1.2), p, r, r);
            else
            {
                ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), Math.Max(2.5, r * 0.65)), p, r * 0.9, r * 0.9);
                ctx.DrawEllipse(null, new Pen(new SolidColorBrush(c), Math.Max(1.5, r * 0.4)), p, r * 0.9, r * 0.9);
            }
            if (latest) ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#FFE066")), 2), p, r * 2.1, r * 2.1);
        }
    }
}
