using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// The full-screen, top-down court of the 2D match view (M1, run 148; Danny's FM-style reference and the approved
/// mock-up, `claude/match-engine-mockup.md`): the stands and boards round the edge, the home floor (or the event's),
/// the lines for the league, and the ten players and the ball from the choreography's frame. The static layer (crowd,
/// planks) is built once per size as geometry; each frame only the players, the ball and any burst are new.
/// </summary>
public sealed class MatchCourt : Control
{
    public static readonly StyledProperty<ChoreoFrame?> FrameProperty = AvaloniaProperty.Register<MatchCourt, ChoreoFrame?>(nameof(Frame));
    public static readonly StyledProperty<ArenaDress?> DressProperty = AvaloniaProperty.Register<MatchCourt, ArenaDress?>(nameof(Dress));
    public static readonly StyledProperty<CourtMarkings> MarkingsProperty = AvaloniaProperty.Register<MatchCourt, CourtMarkings>(nameof(Markings));
    public static readonly StyledProperty<Color> HomeKitProperty = AvaloniaProperty.Register<MatchCourt, Color>(nameof(HomeKit), Colors.Maroon);
    public static readonly StyledProperty<Color> HomeTrimProperty = AvaloniaProperty.Register<MatchCourt, Color>(nameof(HomeTrim), Colors.Gold);
    public static readonly StyledProperty<Color> AwayKitProperty = AvaloniaProperty.Register<MatchCourt, Color>(nameof(AwayKit), Colors.White);
    public static readonly StyledProperty<Color> AwayTrimProperty = AvaloniaProperty.Register<MatchCourt, Color>(nameof(AwayTrim), Colors.Navy);
    public static readonly StyledProperty<IReadOnlyDictionary<Player, int>?> NumbersProperty = AvaloniaProperty.Register<MatchCourt, IReadOnlyDictionary<Player, int>?>(nameof(Numbers));
    /// <summary>Room kept clear at the top (the score bar) and bottom (the commentary bar), in pixels.</summary>
    public static readonly StyledProperty<Thickness> ReserveProperty = AvaloniaProperty.Register<MatchCourt, Thickness>(nameof(Reserve), new Thickness(44, 124, 44, 66));

    public ChoreoFrame? Frame { get => GetValue(FrameProperty); set => SetValue(FrameProperty, value); }
    public ArenaDress? Dress { get => GetValue(DressProperty); set => SetValue(DressProperty, value); }
    public CourtMarkings Markings { get => GetValue(MarkingsProperty); set => SetValue(MarkingsProperty, value); }
    public Color HomeKit { get => GetValue(HomeKitProperty); set => SetValue(HomeKitProperty, value); }
    public Color HomeTrim { get => GetValue(HomeTrimProperty); set => SetValue(HomeTrimProperty, value); }
    public Color AwayKit { get => GetValue(AwayKitProperty); set => SetValue(AwayKitProperty, value); }
    public Color AwayTrim { get => GetValue(AwayTrimProperty); set => SetValue(AwayTrimProperty, value); }
    public IReadOnlyDictionary<Player, int>? Numbers { get => GetValue(NumbersProperty); set => SetValue(NumbersProperty, value); }
    public Thickness Reserve { get => GetValue(ReserveProperty); set => SetValue(ReserveProperty, value); }

    static MatchCourt()
    {
        AffectsRender<MatchCourt>(FrameProperty, DressProperty, MarkingsProperty, HomeKitProperty, HomeTrimProperty, AwayKitProperty, AwayTrimProperty, NumbersProperty, ReserveProperty);
        ClipToBoundsProperty.OverrideDefaultValue<MatchCourt>(true);
    }

    /// <summary>Where the 94×50 ft court sits in a control of this size: pixels per foot and the court's top-left corner.</summary>
    public static (double Scale, Point Origin) Fit(Size size, Thickness reserve)
    {
        const double wFt = 100, hFt = 56.5;   // the court, the apron and the benches
        double w = Math.Max(10, size.Width - reserve.Left - reserve.Right), h = Math.Max(10, size.Height - reserve.Top - reserve.Bottom);
        double s = Math.Max(1, Math.Min(w / wFt, h / hFt));
        double x0 = (size.Width - 94 * s) / 2;
        double y0 = reserve.Top + (h - hFt * s) / 2 + 2.2 * s;
        return (s, new Point(x0, y0));
    }

    /// <summary>Frames drawn (for the frame-time log).</summary>
    public int FramesDrawn { get; private set; }
    public List<double> FrameMs { get; } = new();

    // Brushes that never change, made once (review, run 148: no per-frame allocations for the fixed parts).
    private static readonly IBrush Back = new SolidColorBrush(Color.Parse("#12161D"));
    private static readonly IBrush Bench = new SolidColorBrush(Color.Parse("#2A2F38"));
    private static readonly IBrush Table = new SolidColorBrush(Color.Parse("#E6E6E6"));
    private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(72, 0, 0, 0));
    private static readonly IBrush BallShadow = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
    private static readonly IBrush BallFill = new SolidColorBrush(Color.Parse("#FF8A1E"));
    private static readonly IPen BallEdge = new Pen(new SolidColorBrush(Color.Parse("#6B2C00")), 1.2);
    private static readonly IPen BallSeam = new Pen(new SolidColorBrush(Color.Parse("#6B2C00")), 1);
    private static readonly IBrush TagBack = new SolidColorBrush(Color.FromArgb(205, 10, 12, 16));
    private static readonly IPen PlankPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 120, 80, 40)), 1);
    private static readonly IBrush Wood = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#C99A63"), 0), new GradientStop(Color.Parse("#DDB27A"), 0.5), new GradientStop(Color.Parse("#C99A63"), 1) },
    };
    private Size _staticSize;
    private string _staticKey = "";
    private (Geometry Geo, IBrush Brush)[] _crowd = Array.Empty<(Geometry, IBrush)>();
    private Geometry? _planks;
    private readonly Dictionary<string, FormattedText> _text = new();

    /// <summary>The last drawing error (shown in tests; a frame that fails is skipped, never the game).</summary>
    public static string? LastRenderError { get; private set; }

    public override void Render(DrawingContext ctx)
    {
        try { RenderCourt(ctx); }
        catch (Exception e) { LastRenderError = e.ToString(); System.Diagnostics.Trace.WriteLine(e); }
    }

    private void RenderCourt(DrawingContext ctx)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var size = Bounds.Size;
        if (size.Width < 20 || size.Height < 20) return;
        var dress = Dress ?? new ArenaDress("#1D3FA3", "#F0A33A", "#F4F1EA", "HOME", "HOME", "", null, "", false);
        var (s, o) = Fit(size, Reserve);
        Point P(double x, double y) => new(o.X + x * s, o.Y + y * s);
        var paint = Color.Parse(dress.Paint); var trim = Color.Parse(dress.Trim);
        string key = $"{dress.Paint}{dress.Trim}{dress.CrowdColour}{Markings}";
        if (_staticSize != size || _staticKey != key) BuildStatic(size, s, o, dress);

        // The arena: dark, then the crowd in its colours.
        ctx.FillRectangle(Back, new Rect(size));
        foreach (var (geo, brush) in _crowd) ctx.DrawGeometry(brush, null, geo);
        // Ribbon boards above and below the floor.
        var ribbon = new Rect(P(-5, -4.6), P(99, -2.6));
        var ribbon2 = new Rect(P(-5, 54.2), P(99, 56.0));
        var boardBrush = new SolidColorBrush(paint);
        ctx.FillRectangle(boardBrush, ribbon); ctx.FillRectangle(boardBrush, ribbon2);
        string boardText = dress.Neutral ? $"{dress.CentreText}  ·  HSN LIVE" : $"{dress.RightBaseline}  ·  HSN {(Markings == CourtMarkings.College ? "COLLEGE HOOPS" : "LIVE")}";
        Repeat(ctx, boardText, ribbon, Math.Clamp(s * 0.95, 9, 15), trim);
        Repeat(ctx, dress.Neutral ? "HSN LIVE" : $"{dress.LeftBaseline}  ·  HSN LIVE", ribbon2, Math.Clamp(s * 0.9, 9, 14), trim);

        // The apron, the wood and its planks.
        ctx.FillRectangle(new SolidColorBrush(paint), new Rect(P(-3, -1.8), P(97, 51.8)));
        var wood = Wood;
        var court = new Rect(P(0, 0), P(94, 50));
        ctx.FillRectangle(wood, court);
        if (_planks != null) ctx.DrawGeometry(null, PlankPen, _planks);

        // The keys in the floor's paint; the lines.
        double lane = Markings == CourtMarkings.College ? 12 : 16;
        var keyBrush = new SolidColorBrush(Color.FromArgb(230, paint.R, paint.G, paint.B));
        ctx.FillRectangle(keyBrush, new Rect(P(0, 25 - lane / 2), P(19, 25 + lane / 2)));
        ctx.FillRectangle(keyBrush, new Rect(P(75, 25 - lane / 2), P(94, 25 + lane / 2)));
        var line = new Pen(Brushes.White, Math.Max(1.5, s * 0.17));
        ctx.DrawRectangle(null, line, court);
        ctx.DrawLine(line, P(47, 0), P(47, 50));
        ctx.DrawEllipse(new SolidColorBrush(paint), line, P(47, 25), 6 * s, 6 * s);
        foreach (bool right in new[] { false, true })
        {
            double bx = right ? 88.75 : 5.25, dir = right ? -1 : 1, baseX = right ? 94 : 0;
            ctx.DrawRectangle(null, line, new Rect(P(right ? 75 : 0, 25 - lane / 2), P(right ? 94 : 19, 25 + lane / 2)));
            // The free-throw circle (the far half).
            var ft = P(right ? 75 : 19, 25);
            ctx.DrawGeometry(null, line, Arc(ft, 6 * s, right ? Math.PI / 2 : -Math.PI / 2, Math.PI));
            // The three-point line by league.
            double r = Markings switch { CourtMarkings.College => 22.146, CourtMarkings.Fiba => 22.15, _ => 23.75 };
            double corner = Markings switch { CourtMarkings.College => 21.65, CourtMarkings.Fiba => 21.65, _ => 22 };
            double cy = Math.Min(25, corner);           // the straight's distance from the centre line of the floor
            double ang = Math.Asin(Math.Min(1, cy / r));
            double straight = Math.Cos(ang) * r;         // feet out from the basket where the arc meets the straight
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(P(baseX, 25 - cy), false);
                g.LineTo(P(bx + dir * straight, 25 - cy));
                g.ArcTo(P(bx + dir * straight, 25 + cy), new Size(r * s, r * s), 0, false, right ? SweepDirection.CounterClockwise : SweepDirection.Clockwise);
                g.LineTo(P(baseX, 25 + cy));
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, line, geo);
            // Backboard and rim.
            ctx.DrawLine(new Pen(Brushes.White, Math.Max(2, s * 0.3)), P(baseX + dir * 4, 22), P(baseX + dir * 4, 28));
            ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#FF6A1A")), Math.Max(1.5, s * 0.18)), P(bx, 25), 0.75 * s, 0.75 * s);
        }

        // Centre court: the home crest (a shield with its letter) or the event's name.
        if (dress.CrestTeam is { } crest)
        {
            double cs = 4.2 * s;
            var c0 = P(47, 25);
            var shield = new StreamGeometry();
            using (var g = shield.Open())
            {
                g.BeginFigure(new Point(c0.X, c0.Y - cs), true);
                g.LineTo(new Point(c0.X + cs * 0.85, c0.Y - cs * 0.62)); g.LineTo(new Point(c0.X + cs * 0.72, c0.Y + cs * 0.48));
                g.LineTo(new Point(c0.X, c0.Y + cs)); g.LineTo(new Point(c0.X - cs * 0.72, c0.Y + cs * 0.48)); g.LineTo(new Point(c0.X - cs * 0.85, c0.Y - cs * 0.62));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(trim), null, shield);
            var letter = Text(crest.Abbreviation.Length > 0 ? crest.Abbreviation[..1] : "?", cs * 1.1, paint, true, "Georgia");
            ctx.DrawText(letter, new Point(c0.X - letter.Width / 2, c0.Y - letter.Height / 2));
        }
        else if (dress.CentreText.Length > 0)
        {
            var t = Text(dress.CentreText, Math.Max(10, s * 1.5), trim, true);
            ctx.DrawText(t, new Point(P(47, 25).X - t.Width / 2, P(47, 25).Y - t.Height / 2));
        }
        // The baselines' lettering, in the apron.
        double letterSize = Math.Clamp(s * 1.3, 9, 26);
        Vertical(ctx, Spaced(dress.LeftBaseline), P(-1.5, 25), letterSize, Color.Parse(dress.Lettering), -Math.PI / 2);
        Vertical(ctx, Spaced(dress.RightBaseline), P(95.5, 25), letterSize, Color.Parse(dress.Lettering), Math.PI / 2);
        if (dress.Sideline.Length > 0)
        {
            var side = Text(dress.Sideline, Math.Clamp(s, 9, 18), Color.Parse(dress.Lettering), true);
            ctx.DrawText(side, new Point(P(47, 0).X - side.Width / 2, P(0, -1.75).Y));
        }
        // Benches and the scorer's table along the near sideline.
        var bench = Bench;
        ctx.FillRectangle(bench, new Rect(P(20, 52.4), P(38, 53.6)));
        ctx.FillRectangle(bench, new Rect(P(56, 52.4), P(74, 53.6)));
        ctx.FillRectangle(Table, new Rect(P(44, 52.2), P(50, 53.8)));

        DrawFrame(ctx, s, P);
        FramesDrawn++;
        sw.Stop();
        if (FrameMs.Count < 20000) FrameMs.Add(sw.Elapsed.TotalMilliseconds);
    }

    private void DrawFrame(DrawingContext ctx, double s, Func<double, double, Point> P)
    {
        var f = Frame;
        if (f == null) return;
        // A make's gold ring or a block's white ring.
        if (f.Burst is { } b)
        {
            var col = b.Made ? Color.FromArgb((byte)(255 * (1 - f.BurstAge)), 242, 182, 50) : Color.FromArgb((byte)(255 * (1 - f.BurstAge)), 255, 255, 255);
            ctx.DrawEllipse(null, new Pen(new SolidColorBrush(col), Math.Max(2, s * 0.3)), P(b.At.X, b.At.Y), (0.8 + f.BurstAge * 4) * s, (0.8 + f.BurstAge * 4) * s);
        }
        double r = Math.Max(7, s * 1.05);
        var shadow = Shadow;
        var homeFill = new SolidColorBrush(HomeKit); var homeEdge = new Pen(new SolidColorBrush(HomeTrim), Math.Max(2, r * 0.22));
        var awayFill = new SolidColorBrush(AwayKit); var awayEdge = new Pen(new SolidColorBrush(AwayTrim), Math.Max(2, r * 0.22));
        var homeArrow = new SolidColorBrush(HomeTrim); var awayArrow = new SolidColorBrush(Lift(AwayTrim));
        Color homeNum = Luma(HomeKit) < 0.6 ? Colors.White : Color.Parse("#14171C");
        Color awayNum = Luma(AwayKit) < 0.6 ? Colors.White : AwayTrim;
        // M2: players coming off at a substitution walk to the scorer's table, faded.
        foreach (var x in f.Exits)
        {
            var c = P(x.At.X, x.At.Y);
            var fill = new SolidColorBrush(x.Home ? HomeKit : AwayKit, 0.45);
            ctx.DrawEllipse(fill, new Pen(new SolidColorBrush(x.Home ? HomeTrim : AwayTrim, 0.5), Math.Max(1.5, r * 0.15)), c, r * 0.9, r * 0.9);
            int n = Numbers != null && Numbers.TryGetValue(x.Who, out var num) ? num : 0;
            var t = Text(n.ToString(CultureInfo.InvariantCulture), r * 0.85, Color.FromArgb(150, 255, 255, 255), true);
            ctx.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
        }
        for (int i = 0; i < 10; i++)
        {
            bool home = i < 5;
            var c = P(f.Players[i].X, f.Players[i].Y);
            ctx.DrawEllipse(shadow, null, new Point(c.X + r * 0.2, c.Y + r * 0.3), r, r * 0.78);
            double a = f.Headings[i];
            var arrow = new StreamGeometry();
            using (var g = arrow.Open())
            {
                g.BeginFigure(new Point(c.X + Math.Cos(a) * r * 1.6, c.Y + Math.Sin(a) * r * 1.6), true);
                g.LineTo(new Point(c.X + Math.Cos(a + 0.45) * r * 1.02, c.Y + Math.Sin(a + 0.45) * r * 1.02));
                g.LineTo(new Point(c.X + Math.Cos(a - 0.45) * r * 1.02, c.Y + Math.Sin(a - 0.45) * r * 1.02));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(home ? homeArrow : awayArrow, null, arrow);
            ctx.DrawEllipse(home ? homeFill : awayFill, home ? homeEdge : awayEdge, c, r, r);
            var who = f.Who[i];
            int n = Numbers != null && Numbers.TryGetValue(who, out var num) ? num : i;
            var t = Text(n.ToString(CultureInfo.InvariantCulture), r * 0.95, home ? homeNum : awayNum, true);
            ctx.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
        }
        // The ball handler's name tag.
        if (f.Holder is >= 0 and < 10)
        {
            var c = P(f.Players[f.Holder].X, f.Players[f.Holder].Y);
            var name = Text(f.Who[f.Holder].LastName, Math.Max(10, r * 0.85), Colors.White, true);
            var tag = new Rect(c.X - name.Width / 2 - 6, c.Y - r * 2.7, name.Width + 12, name.Height + 4);
            ctx.DrawRectangle(TagBack, null, tag, 4, 4);
            ctx.DrawText(name, new Point(tag.X + 6, tag.Y + 2));
        }
        // The ball, lifted by its height, over its shadow.
        var bp = P(f.Ball.X, f.Ball.Y);
        double br = Math.Max(4, s * 0.45) * (1 + f.BallZ * 0.08);
        ctx.DrawEllipse(BallShadow, null, new Point(bp.X, bp.Y + 2), br, br * 0.7);
        var lifted = new Point(bp.X, bp.Y - f.BallZ * s * 0.9);
        ctx.DrawEllipse(BallFill, BallEdge, lifted, br, br);
        ctx.DrawLine(BallSeam, new Point(lifted.X - br, lifted.Y), new Point(lifted.X + br, lifted.Y));
    }

    private void BuildStatic(Size size, double s, Point o, ArenaDress dress)
    {
        _staticSize = size;
        _staticKey = $"{dress.Paint}{dress.Trim}{dress.CrowdColour}{Markings}";
        var floor = new Rect(o.X - 5.5 * s, o.Y - 5 * s, 105 * s, 61.5 * s);
        var crowd = Color.Parse(dress.CrowdColour);
        var cols = new[] { crowd, Color.Parse(dress.Trim), Color.Parse("#E9ECF1"), Color.Parse("#3A3F49") };
        var geos = cols.Select(_ => new StreamGeometry()).ToArray();
        var ctxs = geos.Select(g => g.Open()).ToArray();
        var rng = new Random(7);
        double cw = 6, ch = 7;
        for (double y = 2; y < size.Height - 2; y += ch)
            for (double x = 2; x < size.Width - 2; x += cw)
            {
                if (floor.Contains(new Point(x, y))) continue;
                double q = rng.NextDouble();
                if (q < 0.12) continue;
                int k = q < 0.6 ? 0 : q < 0.82 ? 1 : q < 0.91 ? 2 : 3;
                double px = x + rng.NextDouble() * 1.5, py = y + rng.NextDouble() * 1.5;
                var g = ctxs[k];
                g.BeginFigure(new Point(px, py), true);
                g.LineTo(new Point(px + 4, py)); g.LineTo(new Point(px + 4, py + 4.5)); g.LineTo(new Point(px, py + 4.5));
                g.EndFigure(true);
            }
        foreach (var c in ctxs) c.Dispose();
        _crowd = geos.Select((g, i) => ((Geometry)g, (IBrush)new SolidColorBrush(cols[i], i == 3 ? 1 : 0.9))).ToArray();
        // The planks: long lines along the floor, with short joints.
        var planks = new StreamGeometry();
        var pr = new Random(3);
        using (var g = planks.Open())
        {
            for (double y = 0.75; y < 50; y += 0.75)
            {
                g.BeginFigure(new Point(o.X, o.Y + y * s), false); g.LineTo(new Point(o.X + 94 * s, o.Y + y * s)); g.EndFigure(false);
                for (int k = 0; k < 4; k++)
                {
                    double x = o.X + pr.NextDouble() * 94 * s;
                    g.BeginFigure(new Point(x, o.Y + y * s), false); g.LineTo(new Point(x, o.Y + (y + 0.75) * s)); g.EndFigure(false);
                }
            }
        }
        _planks = planks;
        _text.Clear();
    }

    private static Geometry Arc(Point centre, double radius, double from, double sweep)
    {
        var geo = new StreamGeometry();
        using var g = geo.Open();
        g.BeginFigure(new Point(centre.X + Math.Cos(from) * radius, centre.Y + Math.Sin(from) * radius), false);
        double to = from + sweep;
        g.ArcTo(new Point(centre.X + Math.Cos(to) * radius, centre.Y + Math.Sin(to) * radius), new Size(radius, radius), 0, false, SweepDirection.Clockwise);
        g.EndFigure(false);
        return geo;
    }

    private FormattedText Text(string s, double size, Color c, bool bold, string? family = null)
    {
        string key = $"{s}|{size:0.0}|{c}|{bold}|{family}";
        if (_text.TryGetValue(key, out var t)) return t;
        if (_text.Count > 400) _text.Clear();
        t = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(family ?? "Inter", FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal), size, new SolidColorBrush(c));
        _text[key] = t;
        return t;
    }

    private void Repeat(DrawingContext ctx, string text, Rect band, double size, Color c)
    {
        var t = Text(text, size, c, true);
        double step = t.Width + 80;
        int n = Math.Max(1, (int)(band.Width / step));
        for (int i = 0; i < n; i++)
        {
            double cx = band.X + (i + 0.5) * band.Width / n;
            ctx.DrawText(t, new Point(cx - t.Width / 2, band.Y + (band.Height - t.Height) / 2));
        }
    }

    private void Vertical(DrawingContext ctx, string text, Point centre, double size, Color c, double angle)
    {
        var t = Text(text, size, c, true);
        using (ctx.PushTransform(Matrix.CreateTranslation(-t.Width / 2, -t.Height / 2) * Matrix.CreateRotation(angle) * Matrix.CreateTranslation(centre.X, centre.Y)))
            ctx.DrawText(t, new Point(0, 0));
    }

    private static string Spaced(string s) => s.Length <= 12 ? string.Join(" ", s.ToCharArray()).Replace("   ", "  ") : s;
    private static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
    private static Color Lift(Color c) => Luma(c) >= 0.35 ? c : Color.FromRgb((byte)(c.R + (255 - c.R) * 0.5), (byte)(c.G + (255 - c.G) * 0.5), (byte)(c.B + (255 - c.B) * 0.5));
}
