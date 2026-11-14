using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// The Tactics page's live play diagram (Danny, #475, D60 A): a half court where your five run the selected system as a
/// looping play (dots move along their cuts, a bar marks a screen, the ball travels with its passes, a trail shows the
/// next move) and the selected scheme's five defend it as X's. A picture of what the system asks of the players.
/// </summary>
public sealed class PlayDiagram : Control
{
    public static readonly StyledProperty<OffensiveSystem> SystemProperty = AvaloniaProperty.Register<PlayDiagram, OffensiveSystem>(nameof(System));
    public static readonly StyledProperty<DefensiveScheme> SchemeProperty = AvaloniaProperty.Register<PlayDiagram, DefensiveScheme>(nameof(Scheme));
    public static readonly StyledProperty<IReadOnlyList<string>?> LabelsProperty = AvaloniaProperty.Register<PlayDiagram, IReadOnlyList<string>?>(nameof(Labels));
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<PlayDiagram, Color>(nameof(Accent), Color.Parse("#F0A33A"));
    /// <summary>Where the play is: 0..beats (fractional between beats). Driven by a timer while the control is shown.</summary>
    public static readonly StyledProperty<double> PhaseProperty = AvaloniaProperty.Register<PlayDiagram, double>(nameof(Phase));
    public static readonly StyledProperty<bool> AnimateProperty = AvaloniaProperty.Register<PlayDiagram, bool>(nameof(Animate), true);

    public OffensiveSystem System { get => GetValue(SystemProperty); set => SetValue(SystemProperty, value); }
    public DefensiveScheme Scheme { get => GetValue(SchemeProperty); set => SetValue(SchemeProperty, value); }
    public IReadOnlyList<string>? Labels { get => GetValue(LabelsProperty); set => SetValue(LabelsProperty, value); }
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public double Phase { get => GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }
    public bool Animate { get => GetValue(AnimateProperty); set => SetValue(AnimateProperty, value); }

    static PlayDiagram() => AffectsRender<PlayDiagram>(SystemProperty, SchemeProperty, LabelsProperty, AccentProperty, PhaseProperty);

    /// <summary>Half court, 50 wide by 47 deep.</summary>
    public const double Aspect = 50.0 / 47.0;
    private const double BeatSeconds = 1.6;
    private DispatcherTimer? _timer;
    private DateTime _start;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!Animate) return;
        _start = DateTime.UtcNow;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background, (_, _) =>
        {
            if (!Animate) return; // held still (a screenshot, or a test)
            int beats = PlayBook.For(System).Count;
            Phase = (DateTime.UtcNow - _start).TotalSeconds / BeatSeconds % beats;
        });
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop(); _timer = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SystemProperty) _start = DateTime.UtcNow; // a new play starts from its first beat
    }

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 360 : available.Width;
        double h = w / Aspect;
        if (!double.IsInfinity(available.Height) && h > available.Height) { h = available.Height; w = h * Aspect; }
        return new Size(w, h);
    }

    /// <summary>The play's caption at the current phase.</summary>
    public static string Caption(OffensiveSystem s, double phase)
    {
        var beats = PlayBook.For(s);
        return beats[(int)Math.Floor(phase) % beats.Count].Caption;
    }

    public override void Render(DrawingContext ctx)
    {
        var size = Bounds.Size;
        double w = Math.Min(size.Width, size.Height * Aspect), h = w / Aspect;
        double ox = (size.Width - w) / 2, oy = (size.Height - h) / 2;
        double k = w / 50.0;
        // Court coordinates: the basket at the bottom of the panel.
        Point P(double x, double y) => new(ox + x * k, oy + h - y * k);

        var floor = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#3B2A1C"), 0), new GradientStop(Color.Parse("#5A3E26"), 1) },
        };
        ctx.FillRectangle(floor, new Rect(ox, oy, w, h), 8);
        var line = new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1.4);
        var accent = new SolidColorBrush(Accent);
        // The paint, in the team's colour.
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(70, Accent.R, Accent.G, Accent.B)), new Rect(P(17, 19), P(33, 0)));
        ctx.DrawRectangle(line, new Rect(P(17, 19), P(33, 0)));
        ctx.DrawEllipse(null, line, P(25, 19), 6 * k, 6 * k);
        // The three-point line: corners, then the arc.
        double r3 = 23.75, cornerY = PlayBook.BasketY + Math.Sqrt(r3 * r3 - 22 * 22);
        ctx.DrawLine(line, P(3, 0), P(3, cornerY));
        ctx.DrawLine(line, P(47, 0), P(47, cornerY));
        var arc = new StreamGeometry();
        using (var g = arc.Open())
        {
            double a0 = Math.Acos(-22 / r3), a1 = Math.Acos(22 / r3);
            bool first = true;
            for (double ang = a0; ang >= a1 - 1e-9; ang -= (a0 - a1) / 48)
            {
                var pt = P(PlayBook.BasketX + Math.Cos(ang) * r3, PlayBook.BasketY + Math.Sin(ang) * r3);
                if (first) { g.BeginFigure(pt, false); first = false; } else g.LineTo(pt);
            }
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, line, arc);
        // Half-court line, backboard and rim.
        ctx.DrawLine(line, P(0, 47), P(50, 47));
        ctx.DrawLine(new Pen(Brushes.White, 2), P(22, 4), P(28, 4));
        ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#FF7A1A")), 2), P(PlayBook.BasketX, PlayBook.BasketY), 0.9 * k, 0.9 * k);

        var beats = PlayBook.For(System);
        int n = beats.Count;
        double phase = ((Phase % n) + n) % n;
        int i0 = (int)Math.Floor(phase), i1 = (i0 + 1) % n;
        double f = phase - i0;
        double t = Math.Clamp((f - 0.15) / 0.7, 0, 1); // hold, then move
        t = t * t * (3 - 2 * t);
        if (i1 == 0) t = f > 0.85 ? (f - 0.85) / 0.15 : 0; // snap back to the start quickly
        var a = beats[i0]; var b = beats[i1];
        double[] xs = new double[5], ys = new double[5];
        for (int i = 0; i < 5; i++) { xs[i] = a.X[i] + (b.X[i] - a.X[i]) * t; ys[i] = a.Y[i] + (b.Y[i] - a.Y[i]) * t; }

        // Trails: where each player goes next (dashed), the screener's bar.
        var trail = new Pen(new SolidColorBrush(Color.FromArgb(200, Accent.R, Accent.G, Accent.B)), 1.6, new DashStyle(new double[] { 3, 3 }, 0));
        if (i1 != 0)
            for (int i = 0; i < 5; i++)
                if (Math.Abs(b.X[i] - a.X[i]) + Math.Abs(b.Y[i] - a.Y[i]) > 1.5)
                {
                    ctx.DrawLine(trail, P(xs[i], ys[i]), P(b.X[i], b.Y[i]));
                    Arrow(ctx, trail.Brush!, P(a.X[i], a.Y[i]), P(b.X[i], b.Y[i]));
                }
        if (a.Screener >= 0)
        {
            int s = a.Screener;
            var dir = new Vector(a.X[a.Ball] - a.X[s], a.Y[a.Ball] - a.Y[s]);
            double len = Math.Max(0.01, Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y));
            var perp = new Vector(-dir.Y / len, dir.X / len) * 2.2;
            ctx.DrawLine(new Pen(Brushes.White, 3), P(xs[s] + perp.X, ys[s] + perp.Y), P(xs[s] - perp.X, ys[s] - perp.Y));
        }

        // The ball moves with its passes.
        double bx = xs[a.Ball], by = ys[a.Ball];
        if (a.Ball != b.Ball) { bx = xs[a.Ball] + (xs[b.Ball] - xs[a.Ball]) * t; by = ys[a.Ball] + (ys[b.Ball] - ys[a.Ball]) * t; }

        // Defence as X's.
        var d = PlayBook.Defence(Scheme, xs, ys, bx, by);
        var xPen = new Pen(new SolidColorBrush(Color.Parse("#9FB3C8")), 2.2);
        foreach (var (dx, dy) in d)
        {
            var c = P(dx, dy); double r = 1.1 * k;
            ctx.DrawLine(xPen, c + new Vector(-r, -r), c + new Vector(r, r));
            ctx.DrawLine(xPen, c + new Vector(-r, r), c + new Vector(r, -r));
        }

        // Offence: numbered dots in the team's colour with the player's name.
        var labels = Labels;
        for (int i = 0; i < 5; i++)
        {
            var c = P(xs[i], ys[i]); double r = 1.7 * k;
            ctx.DrawEllipse(accent, new Pen(Brushes.White, 1.5), c, r, r);
            var num = Text((i + 1).ToString(CultureInfo.InvariantCulture), 11, FontWeight.Bold, Brushes.White);
            ctx.DrawText(num, c - new Point(num.Width / 2, num.Height / 2));
            if (labels != null && i < labels.Count && labels[i].Length > 0)
            {
                var name = Text(labels[i], 10.5, FontWeight.SemiBold, Brushes.White);
                var at = new Point(Math.Clamp(c.X - name.Width / 2, ox + 2, ox + w - name.Width - 2), c.Y + r + 1);
                ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), new Rect(at.X - 2, at.Y, name.Width + 4, name.Height), 3);
                ctx.DrawText(name, at);
            }
        }
        var ball = P(bx, by) + new Vector(1.2 * k, -1.2 * k);
        ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#FF8A1F")), new Pen(Brushes.Black, 1), ball, 0.75 * k, 0.75 * k);

        // The beat's caption across the top: "2 / 4  The big comes up to set the ball screen".
        var cap = Text($"{i0 + 1} / {n}   {a.Caption}", 12, FontWeight.SemiBold, Brushes.White);
        cap.MaxTextWidth = Math.Max(20, w - 16);
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(170, 10, 14, 20)), new Rect(ox, oy, w, cap.Height + 8), 8);
        ctx.DrawText(cap, new Point(ox + 8, oy + 4));
    }

    private static void Arrow(DrawingContext ctx, IBrush brush, Point from, Point to)
    {
        var v = to - from; double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        if (len < 1) return;
        var u = v / len; var p = new Vector(-u.Y, u.X);
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(to, true);
            c.LineTo(to - u * 8 + p * 4);
            c.LineTo(to - u * 8 - p * 4);
            c.EndFigure(true);
        }
        ctx.DrawGeometry(brush, null, g);
    }

    private static FormattedText Text(string s, double size, FontWeight weight, IBrush brush) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Inter, Segoe UI", FontStyle.Normal, weight), size, brush);
}
