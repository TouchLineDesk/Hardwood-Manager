using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hardwood.Core.Engine;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// Match day chart (roadmap 2): the home side's win probability over the game as a line between the two teams' colours,
/// momentum bars underneath (points for minus points against per two minutes), period dividers, and the game's scoring
/// runs shaded and labelled. Labels that would collide are dropped rather than drawn over each other.
/// </summary>
public sealed class GameFlowChart : Control
{
    public static readonly StyledProperty<GameResult?> ResultProperty = AvaloniaProperty.Register<GameFlowChart, GameResult?>(nameof(Result));
    /// <summary>Bump to redraw while a live game runs (the result object itself doesn't change).</summary>
    public static readonly StyledProperty<int> VersionProperty = AvaloniaProperty.Register<GameFlowChart, int>(nameof(Version));
    public static readonly StyledProperty<Color> HomeColorProperty = AvaloniaProperty.Register<GameFlowChart, Color>(nameof(HomeColor), Color.Parse("#F0A33A"));
    public static readonly StyledProperty<Color> AwayColorProperty = AvaloniaProperty.Register<GameFlowChart, Color>(nameof(AwayColor), Color.Parse("#4F8EF7"));

    public GameResult? Result { get => GetValue(ResultProperty); set => SetValue(ResultProperty, value); }
    public int Version { get => GetValue(VersionProperty); set => SetValue(VersionProperty, value); }
    public Color HomeColor { get => GetValue(HomeColorProperty); set => SetValue(HomeColorProperty, value); }
    public Color AwayColor { get => GetValue(AwayColorProperty); set => SetValue(AwayColorProperty, value); }

    static GameFlowChart() => AffectsRender<GameFlowChart>(ResultProperty, VersionProperty, HomeColorProperty, AwayColorProperty);

    public const double Left = 44, Right = 10, Top = 18, Bottom = 18, FontSize = 10;
    private static readonly Typeface Face = new("Inter");

    /// <summary>One piece of text the chart draws and where.</summary>
    public sealed record Label(string Text, Rect Box);

    /// <summary>The chart's plot areas for a size: the probability band on top, the momentum strip below.</summary>
    public static (Rect Prob, Rect Momentum) Areas(Size size)
    {
        double w = Math.Max(0, size.Width - Left - Right), h = Math.Max(0, size.Height - Top - Bottom);
        double mh = Math.Round(h * 0.24);
        return (new Rect(Left, Top, w, Math.Max(0, h - mh - 6)), new Rect(Left, Top + h - mh, w, mh));
    }

    /// <summary>How long the x axis runs: regulation, or to the end of the overtime being played.</summary>
    public static double Span(GameResult r) => Math.Max(GameFlow.RegulationSeconds(r), r.Elapsed);

    /// <summary>
    /// Every label the chart would draw at this size, after dropping any that would overlap another or spill outside
    /// the control. The layout tests call this to check nothing on the chart is covered.
    /// </summary>
    public static List<Label> PlaceLabels(GameResult r, Size size)
    {
        var (prob, mom) = Areas(size);
        var placed = new List<Label>();
        if (prob.Width < 40 || prob.Height < 20) return placed;
        double span = Span(r);
        double X(double t) => prob.X + prob.Width * Math.Clamp(t / span, 0, 1);

        bool TryAdd(string text, Point at, bool required = false)
        {
            var ft = Text(text, Colors.White);
            var box = new Rect(at, new Size(ft.Width, ft.Height));
            if (box.Right > size.Width) box = box.WithX(size.Width - box.Width);
            if (box.X < 0) box = box.WithX(0);
            if (!required && placed.Any(l => l.Box.Inflate(2).Intersects(box))) return false;
            placed.Add(new Label(text, box));
            return true;
        }

        // Y axis: who the top and bottom of the band belong to.
        TryAdd(r.Home.Team.Abbreviation, new Point(4, prob.Y - 1), true);
        TryAdd(r.Away.Team.Abbreviation, new Point(4, prob.Bottom - Text("X", Colors.White).Height + 1), true);
        TryAdd("50%", new Point(4, prob.Y + prob.Height / 2 - 7), true);
        // Period names above the band.
        int periods = Math.Max(r.Periods, r.CurrentPeriod);
        for (int p = 1; p <= periods; p++)
        {
            double start = p <= r.Periods ? (p - 1) * r.PeriodSeconds : GameFlow.RegulationSeconds(r) + (p - r.Periods - 1) * r.OvertimeSeconds;
            if (start >= span) break;
            TryAdd(GameFlow.PeriodName(r, p), new Point(X(start) + 3, 1));
        }
        // Runs: label each over the middle of its span, on the team's side of the band if the win-probability line
        // leaves room there, else the other side; never on the line (review #6). No room either side: no label (the
        // shaded wash still marks the run).
        var line = LinePoints(r, (prob, span));
        foreach (var run in GameFlow.Runs(r).OrderByDescending(x => x.For - x.Against))
        {
            double mid = X((run.StartElapsed + run.EndElapsed) / 2);
            var ft = Text(run.Score, Colors.White);
            double top = prob.Y + 3, bottom = prob.Bottom - ft.Height - 3;
            foreach (double y in run.Home ? new[] { top, bottom } : new[] { bottom, top })
            {
                var box = new Rect(mid - ft.Width / 2, y, ft.Width, ft.Height);
                if (CrossesLine(line, box.Inflate(2))) continue;
                if (TryAdd(run.Score, box.TopLeft)) break;
            }
        }
        // Momentum strip title, if there's room.
        TryAdd("+/-", new Point(4, mom.Y + mom.Height / 2 - 7));
        return placed;
    }

    /// <summary>The win-probability line as drawn: a step line through each scoring possession, in control coordinates.</summary>
    public static List<Point> LinePoints(GameResult r, (Rect Prob, double Span) area)
    {
        var (prob, span) = area;
        double X(double t) => prob.X + prob.Width * Math.Clamp(t / span, 0, 1);
        double Y(double pct) => prob.Bottom - prob.Height * Math.Clamp(pct, 0, 100) / 100;
        var wp = GameFlow.WinProbability(r);
        var pts = new List<Point>();
        if (wp.Count == 0) return pts;
        pts.Add(new Point(X(wp[0].Elapsed), Y(wp[0].Percent)));
        for (int i = 1; i < wp.Count; i++)
        {
            pts.Add(new Point(X(wp[i].Elapsed), Y(wp[i - 1].Percent)));
            pts.Add(new Point(X(wp[i].Elapsed), Y(wp[i].Percent)));
        }
        return pts;
    }

    /// <summary>Whether the step line passes through a box (any segment touching it).</summary>
    public static bool CrossesLine(List<Point> line, Rect box)
    {
        for (int i = 1; i < line.Count; i++)
        {
            var a = line[i - 1]; var b = line[i];
            double x0 = Math.Min(a.X, b.X), x1 = Math.Max(a.X, b.X), y0 = Math.Min(a.Y, b.Y), y1 = Math.Max(a.Y, b.Y);
            // Segments are horizontal or vertical, so their bounding box is the segment.
            if (x1 >= box.X && x0 <= box.Right && y1 >= box.Y && y0 <= box.Bottom) return true;
        }
        return false;
    }

    private static FormattedText Text(string s, Color c) =>
        new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, FontSize, new SolidColorBrush(c));

    public override void Render(DrawingContext ctx)
    {
        var r = Result;
        if (r == null) return;
        var (prob, mom) = Areas(Bounds.Size);
        if (prob.Width < 40 || prob.Height < 20) return;
        double span = Span(r);
        double X(double t) => prob.X + prob.Width * Math.Clamp(t / span, 0, 1);
        double Y(double pct) => prob.Bottom - prob.Height * Math.Clamp(pct, 0, 100) / 100;

        var grid = new Pen(new SolidColorBrush(Color.Parse("#2A323D")), 1);
        var muted = Color.Parse("#8B96A5");
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#141920")), null, prob);
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#4B5563")), 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(prob.X, Y(50)), new Point(prob.Right, Y(50)));

        // Period dividers through both strips.
        int periods = Math.Max(r.Periods, r.CurrentPeriod);
        for (int p = 2; p <= periods; p++)
        {
            double start = p <= r.Periods ? (p - 1) * r.PeriodSeconds : GameFlow.RegulationSeconds(r) + (p - r.Periods - 1) * r.OvertimeSeconds;
            if (start >= span) break;
            ctx.DrawLine(grid, new Point(X(start), prob.Y), new Point(X(start), mom.Bottom));
        }

        // Runs: a light wash in the team's colour across the run.
        foreach (var run in GameFlow.Runs(r))
        {
            var c = run.Home ? HomeColor : AwayColor;
            double x0 = X(run.StartElapsed), x1 = Math.Max(X(run.EndElapsed), x0 + 3);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(38, c.R, c.G, c.B)), null, new Rect(x0, prob.Y, x1 - x0, prob.Height));
        }

        // Win probability: a step line (it only moves when someone scores), coloured by who's favoured.
        var wp = GameFlow.WinProbability(r);
        if (wp.Count > 0)
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(X(wp[0].Elapsed), Y(wp[0].Percent)), false);
                for (int i = 1; i < wp.Count; i++)
                {
                    g.LineTo(new Point(X(wp[i].Elapsed), Y(wp[i - 1].Percent)));
                    g.LineTo(new Point(X(wp[i].Elapsed), Y(wp[i].Percent)));
                }
                g.EndFigure(false);
            }
            using (ctx.PushClip(new Rect(prob.X, prob.Y, prob.Width, Y(50) - prob.Y)))
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(HomeColor), 2.2), geo);
            using (ctx.PushClip(new Rect(prob.X, Y(50), prob.Width, prob.Bottom - Y(50))))
                ctx.DrawGeometry(null, new Pen(new SolidColorBrush(AwayColor), 2.2), geo);
            var last = wp[^1];
            ctx.DrawEllipse(new SolidColorBrush(last.Percent >= 50 ? HomeColor : AwayColor), null, new Point(X(last.Elapsed), Y(last.Percent)), 3.5, 3.5);
        }

        // Momentum bars.
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#141920")), null, mom);
        var m = GameFlow.Momentum(r);
        if (m.Length > 0)
        {
            double max = Math.Max(6, m.Max(Math.Abs));
            double mid = mom.Y + mom.Height / 2, bw = prob.Width * 120 / span;
            ctx.DrawLine(grid, new Point(mom.X, mid), new Point(mom.Right, mid));
            for (int i = 0; i < m.Length; i++)
            {
                if (m[i] == 0) continue;
                double hgt = mom.Height / 2 * Math.Abs(m[i]) / max;
                var c = m[i] > 0 ? HomeColor : AwayColor;
                double x = X(i * 120) + 1, wdt = Math.Max(1, Math.Min(bw, prob.Right - X(i * 120)) - 2);
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(200, c.R, c.G, c.B)), null,
                    m[i] > 0 ? new Rect(x, mid - hgt, wdt, hgt) : new Rect(x, mid, wdt, hgt));
            }
        }

        foreach (var l in PlaceLabels(r, Bounds.Size))
        {
            bool team = l.Text == r.Home.Team.Abbreviation || l.Text == r.Away.Team.Abbreviation;
            var colour = l.Text == r.Home.Team.Abbreviation ? HomeColor : l.Text == r.Away.Team.Abbreviation ? AwayColor : muted;
            if (!team && GameFlow.Runs(r).Any(x => x.Score == l.Text)) colour = Colors.White;
            ctx.DrawText(Text(l.Text, colour), l.Box.TopLeft);
        }
    }
}
