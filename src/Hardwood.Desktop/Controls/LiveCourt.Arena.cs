using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hardwood.Core.College;
using Hardwood.Desktop.Graphics;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// A projective map from the court's feet (x 0..94 baseline to baseline, y 0..50 far sideline to near) to the arena
/// frame's pixels, fixed by the four corners the render wrote (B2a, run 140). Straight lines stay straight, so a key or
/// the apron is four projected corners; text and crests use the map's local linear part.
/// </summary>
public sealed class CourtProjection
{
    private readonly double[] _h;   // h11..h32, h33 = 1

    public CourtProjection(IReadOnlyList<Point> corners)
    {
        var src = new[] { new Point(0, 0), new Point(94, 0), new Point(94, 50), new Point(0, 50) };
        var a = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            double x = src[i].X, y = src[i].Y, u = corners[i].X, v = corners[i].Y;
            double[] r1 = { x, y, 1, 0, 0, 0, -u * x, -u * y, u };
            double[] r2 = { 0, 0, 0, x, y, 1, -v * x, -v * y, v };
            for (int k = 0; k < 9; k++) { a[2 * i, k] = r1[k]; a[2 * i + 1, k] = r2[k]; }
        }
        // Gaussian elimination with partial pivoting.
        for (int c = 0; c < 8; c++)
        {
            int piv = c;
            for (int r = c + 1; r < 8; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[piv, c])) piv = r;
            for (int k = 0; k < 9; k++) (a[c, k], a[piv, k]) = (a[piv, k], a[c, k]);
            for (int r = 0; r < 8; r++)
            {
                if (r == c) continue;
                double f = a[r, c] / a[c, c];
                for (int k = c; k < 9; k++) a[r, k] -= f * a[c, k];
            }
        }
        _h = new double[8];
        for (int i = 0; i < 8; i++) _h[i] = a[i, 8] / a[i, i];
    }

    public Point Map(double x, double y)
    {
        double d = _h[6] * x + _h[7] * y + 1;
        return new Point((_h[0] * x + _h[1] * y + _h[2]) / d, (_h[3] * x + _h[4] * y + _h[5]) / d);
    }

    /// <summary>Pixels per foot along the court's length at a spot (the scale a player standing there is drawn at).</summary>
    public double Scale(double x, double y) { var a = Map(x - 0.5, y); var b = Map(x + 0.5, y); return Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y)); }

    /// <summary>The local linear part: where one foot along x and one foot along y go, in pixels.</summary>
    public (Vector dx, Vector dy) Jacobian(double x, double y)
    {
        var px0 = Map(x - 0.5, y); var px1 = Map(x + 0.5, y); var py0 = Map(x, y - 0.5); var py1 = Map(x, y + 0.5);
        return (px1 - px0, py1 - py0);
    }
}

public sealed partial class LiveCourt
{
    /// <summary>The arena frame (B2a, run 140): with it the court is drawn in perspective in the arena, not flat.</summary>
    public static readonly StyledProperty<BroadcastSet?> ArenaProperty = AvaloniaProperty.Register<LiveCourt, BroadcastSet?>(nameof(Arena));
    /// <summary>How the floor is dressed: the program's paint and lettering, or an event floor.</summary>
    public static readonly StyledProperty<ArenaDress?> DressProperty = AvaloniaProperty.Register<LiveCourt, ArenaDress?>(nameof(Dress));

    public BroadcastSet? Arena { get => GetValue(ArenaProperty); set => SetValue(ArenaProperty, value); }
    public ArenaDress? Dress { get => GetValue(DressProperty); set => SetValue(DressProperty, value); }

    public const double ArenaAspect = 16.0 / 9.0;
    /// <summary>A true vertical (a player, the ball's height) is drawn this much shorter than a foot along the court,
    /// as the render's camera looks down on the floor (the rims in arena.py's JSON).</summary>
    public const double Upright = 0.92;

    public bool InArena => Arena is { Frame: not null, Court: not null } && Dress != null;

    private CourtProjection? _proj;
    private Point[]? _projFor;
    private Rect _projRect;
    private IImage? _crest;
    private Hardwood.Core.Model.Team? _crestFor;

    /// <summary>The projection for the current bounds (the frame is drawn to fill them at 16:9).</summary>
    public CourtProjection? ArenaProjection
    {
        get
        {
            if (Arena is not { Court: { } corners } set) return null;
            var r = ArenaRect();
            if (_proj == null || _projFor != corners || _projRect != r)
            {
                double k = r.Width / set.Size.Width;
                _proj = new CourtProjection(corners.Select(c => new Point(r.X + c.X * k, r.Y + c.Y * k)).ToList());
                _projFor = corners; _projRect = r;
            }
            return _proj;
        }
    }

    private Rect ArenaRect()
    {
        double w = Bounds.Width, h = Bounds.Height;
        double fw = Math.Min(w, h * ArenaAspect), fh = fw / ArenaAspect;
        return new Rect((w - fw) / 2, (h - fh) / 2, fw, fh);
    }

    private static Color Hex(string hex, Color fallback) { try { return Color.Parse(hex); } catch (FormatException) { return fallback; } }
    private static Color A(Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);

    private void RenderArena(DrawingContext ctx)
    {
        var set = Arena!; var dress = Dress!; var proj = ArenaProjection!;
        var rect = ArenaRect();
        using var clip = ctx.PushClip(new RoundedRect(rect, 6));
        ctx.DrawImage(set.Frame!, new Rect(set.Frame!.Size), rect);

        var paint = Hex(dress.Paint, Colors.SteelBlue);
        var trim = Hex(dress.Trim, Colors.White);
        // The crowd's team shirts and the ribbon boards take the home side's (or the event's) colours.
        if (set.Mask is { } mask)
            using (ctx.PushOpacityMask(new ImageBrush(mask) { Stretch = Stretch.Fill }, rect))
                ctx.DrawRectangle(new SolidColorBrush(A(Hex(dress.CrowdColour, paint), 0.78)), null, rect);

        Point P(double x, double y) => proj.Map(x, y);
        StreamGeometry Poly(params (double x, double y)[][] rings)
        {
            var g = new StreamGeometry();
            using var gc = g.Open();
            gc.SetFillRule(FillRule.EvenOdd);
            foreach (var ring in rings)
            {
                gc.BeginFigure(P(ring[0].x, ring[0].y), true);
                foreach (var p in ring.Skip(1)) gc.LineTo(P(p.x, p.y));
                gc.EndFigure(true);
            }
            return g;
        }
        (double, double)[] Rect4(double x0, double y0, double x1, double y1) => new[] { (x0, y0), (x1, y0), (x1, y1), (x0, y1) };
        (double, double)[] Circle(double cx, double cy, double r, double a0 = 0, double a1 = 2 * Math.PI, int n = 48) =>
            Enumerable.Range(0, n + 1).Select(i => { double t = a0 + (a1 - a0) * i / n; return (cx + Math.Cos(t) * r, cy + Math.Sin(t) * r); }).ToArray();

        // The apron: a band of paint outside the lines (wider behind the baselines, where the lettering goes), with a
        // thin trim line along its inside edge.
        ctx.DrawGeometry(new SolidColorBrush(A(paint, 0.86)), null, Poly(Rect4(-7, -3.6, 101, 53.6), Rect4(-0.25, -0.25, 94.25, 50.25)));
        ctx.DrawGeometry(null, new Pen(new SolidColorBrush(A(trim, 0.85)), Math.Max(1, proj.Scale(47, 0) * 0.28)), Poly(Rect4(-0.9, -0.9, 94.9, 50.9)));
        // The keys and the centre circle, stained in the paint colour (the white lines stay on top).
        var keyBrush = new SolidColorBrush(A(paint, 0.7));
        ctx.DrawGeometry(keyBrush, null, Poly(Rect4(0.2, 19.2, 18.8, 30.8)));
        ctx.DrawGeometry(keyBrush, null, Poly(Rect4(75.2, 19.2, 93.8, 30.8)));
        ctx.DrawGeometry(new SolidColorBrush(A(paint, 0.55)), null, Poly(Circle(47, 25, 5.8)));
        // The lines the paint covered: the free-throw circles' inside halves and the restricted arcs.
        var line = new Pen(new SolidColorBrush(Color.FromArgb(215, 240, 240, 236)), Math.Max(1, proj.Scale(47, 25) * 0.16));
        foreach (bool right in new[] { false, true })
        {
            double fx = right ? 75 : 19, bx = right ? 94 - 5.25 : 5.25;
            var arc = right ? Circle(fx, 25, 6, -Math.PI / 2, Math.PI / 2, 24) : Circle(fx, 25, 6, Math.PI / 2, 3 * Math.PI / 2, 24);
            DrawPolyline(ctx, line, arc.Select(p => P(p.Item1, p.Item2)));
            var ra = right ? Circle(bx, 25, 4, Math.PI / 2, 3 * Math.PI / 2, 20) : Circle(bx, 25, 4, -Math.PI / 2, Math.PI / 2, 20);
            DrawPolyline(ctx, line, ra.Select(p => P(p.Item1, p.Item2)));
        }

        // The lettering: the school and nickname (or the event) along the baselines, the event's line along the far sideline.
        var letter = Hex(dress.Lettering, Colors.White);
        DrawFloorText(ctx, proj, dress.LeftBaseline, new Point(-3.9, 25), new Vector(0, -1), 3.6, 44, letter, trim);
        DrawFloorText(ctx, proj, dress.RightBaseline, new Point(97.9, 25), new Vector(0, 1), 3.6, 44, letter, trim);
        if (dress.Sideline is { Length: > 0 } side)
            DrawFloorText(ctx, proj, side, new Point(47, -2.0), new Vector(1, 0), 2.1, 70, letter, trim);

        // Centre court: the home program's crest, or the event's name under the HSN mark.
        if (dress.CrestTeam is { } team)
        {
            if (_crestFor != team) { _crest = CrestImage(team); _crestFor = team; }
            if (_crest is { } img) DrawFloorImage(ctx, proj, img, new Point(47, 25), 9.5);
        }
        else
        {
            DrawFloorText(ctx, proj, "HSN", new Point(47, 22.6), new Vector(1, 0), 1.4, 10, trim, null);
            DrawFloorText(ctx, proj, dress.CentreText, new Point(47, 25.6), new Vector(1, 0), 2.2, 30, letter, paint);
        }

        // The game on the floor: the faint shot record, then the players and the ball.
        var shots = Shots ?? Array.Empty<LiveShot>();
        var frame = Frame;
        using (ctx.PushOpacity(frame != null ? 0.45 : 1.0))
            foreach (var (sh, i) in shots.Select((sh, i) => (sh, i)))
            {
                var c = sh.Home ? HomeColor : AwayColor;
                var p = P(sh.Spot.X, sh.Spot.Y);
                double r = Math.Max(2, proj.Scale(sh.Spot.X, sh.Spot.Y) * 0.7);
                if (sh.Made) ctx.DrawEllipse(new SolidColorBrush(c), new Pen(Brushes.White, 1), p, r, r * 0.55);
                else ctx.DrawEllipse(null, new Pen(new SolidColorBrush(c), Math.Max(1.2, r * 0.35)), p, r, r * 0.55);
                if (frame == null && i == shots.Count - 1) ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#FFE066")), 2), p, r * 2.1, r * 1.2);
            }
        Advance();
        if (frame != null) DrawArenaFrame(ctx, proj, frame, _pose ?? TrackerMotion.Still(frame));
    }

    private static void DrawPolyline(DrawingContext ctx, IPen pen, IEnumerable<Point> pts)
    {
        var g = new StreamGeometry();
        using (var gc = g.Open())
        {
            bool first = true;
            foreach (var p in pts) { if (first) { gc.BeginFigure(p, false); first = false; } else gc.LineTo(p); }
            if (!first) gc.EndFigure(false);
        }
        ctx.DrawGeometry(null, pen, g);
    }

    private static readonly Typeface FloorFace = new("Inter", FontStyle.Normal, FontWeight.Black);

    /// <summary>Paint text on the floor: each letter laid on the court with the projection's local linear part, so the
    /// lettering foreshortens with the floor. <paramref name="along"/> is the reading direction in court feet.</summary>
    public static void DrawFloorText(DrawingContext ctx, CourtProjection proj, string text, Point centre, Vector along, double heightFt,
        double maxLengthFt, Color fill, Color? outline)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        const double F = 64;
        var whole = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FloorFace, F, Brushes.White);
        double H = whole.Height, total = whole.WidthIncludingTrailingWhitespace;
        double sc = heightFt / (H * 0.72);                       // the cap height fills heightFt
        if (total * sc > maxLengthFt) sc = maxLengthFt / total;
        var down = new Vector(-along.Y, along.X);                // the letters' "down", in feet
        var brush = new SolidColorBrush(fill);
        var pen = outline is { } o ? new Pen(new SolidColorBrush(Color.FromArgb(150, o.R, o.G, o.B)), 2.2) : null;
        double x = 0;
        for (int i = 0; i < text.Length; i++)
        {
            string ch = text[i].ToString();
            var g = new FormattedText(ch, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, FloorFace, F, brush);
            double gw = g.WidthIncludingTrailingWhitespace;
            if (!char.IsWhiteSpace(text[i]))
            {
                double offFt = (x + gw / 2 - total / 2) * sc;
                var gc = new Point(centre.X + along.X * offFt, centre.Y + along.Y * offFt);
                var (jx, jy) = proj.Jacobian(gc.X, gc.Y);
                Vector J(Vector f) => jx * f.X + jy * f.Y;
                var ax = J(along * sc); var ay = J(down * sc);
                var p = proj.Map(gc.X, gc.Y);
                var m = new Matrix(ax.X, ax.Y, ay.X, ay.Y, p.X - ax.X * gw / 2 - ay.X * H / 2, p.Y - ax.Y * gw / 2 - ay.Y * H / 2);
                using (ctx.PushTransform(m))
                {
                    if (pen != null && g.BuildGeometry(new Point(0, 0)) is { } geo) ctx.DrawGeometry(brush, pen, geo);
                    else ctx.DrawText(g, new Point(0, 0));
                }
            }
            x += gw;
        }
    }

    /// <summary>Lay an image flat on the floor, centred on a spot, <paramref name="sizeFt"/> across.</summary>
    public static void DrawFloorImage(DrawingContext ctx, CourtProjection proj, IImage img, Point centre, double sizeFt)
    {
        var sz = img.Size; if (sz.Width <= 0) return;
        double sc = sizeFt / Math.Max(sz.Width, sz.Height);
        var (jx, jy) = proj.Jacobian(centre.X, centre.Y);
        var ax = jx * sc; var ay = jy * sc;
        var p = proj.Map(centre.X, centre.Y);
        var m = new Matrix(ax.X, ax.Y, ay.X, ay.Y, p.X - ax.X * sz.Width / 2 - ay.X * sz.Height / 2, p.Y - ax.Y * sz.Width / 2 - ay.Y * sz.Height / 2);
        using (ctx.PushTransform(m))
        using (ctx.PushOpacity(0.92))
            ctx.DrawImage(img, new Rect(sz));
    }

    /// <summary>The program's crest drawn once to a bitmap (the same crest as everywhere else in the game).</summary>
    private static IImage? CrestImage(Hardwood.Core.Model.Team team)
    {
        try
        {
            var logo = new TeamLogo { Team = team, Width = 256, Height = 256, ShowRank = false };
            logo.Measure(new Size(256, 256)); logo.Arrange(new Rect(0, 0, 256, 256));
            var rtb = new RenderTargetBitmap(new PixelSize(256, 256));
            rtb.Render(logo);
            return rtb;
        }
        catch (Exception) { return null; }
    }

    private static readonly Color[] Skins = { Color.Parse("#5C3A1E"), Color.Parse("#8D5524"), Color.Parse("#C68642"), Color.Parse("#E0AC69"), Color.Parse("#F1C27D") };

    /// <summary>The ten players as figures standing on the floor (far ones smaller), the ball, and the pass and shot paths.</summary>
    private void DrawArenaFrame(DrawingContext ctx, CourtProjection proj, TrackerFrame f, TrackerPose pose)
    {
        Point Up(Point spot, double ft) { var p = proj.Map(spot.X, spot.Y); return new Point(p.X, p.Y - ft * proj.Scale(spot.X, spot.Y) * Upright); }
        if (pose.PassFrom is { } pf && pose.PassTo is { } pt)
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)), 1.6) { DashStyle = DashStyle.Dot }, Up(pf, 4.5), Up(pt, 4.5));
        if (pose.ShotFrom is { } from && pose.ShotTo is { } to)
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 224, 102)), 2) { DashStyle = f.Made ? null : DashStyle.Dash }, Up(from, 7.5), Up(to, 10));
        var face = new Typeface("Inter", FontStyle.Normal, FontWeight.Bold);
        foreach (var m in pose.Markers.OrderBy(m => m.Spot.Y).ThenBy(m => m.Actor ? 1 : 0))
        {
            var c = m.Home ? HomeColor : AwayColor;
            var foot = proj.Map(m.Spot.X, m.Spot.Y);
            double k = proj.Scale(m.Spot.X, m.Spot.Y), u = k * Upright;
            // Shadow, then the actor's ring on the floor.
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)), null, new Point(foot.X + k * 0.4, foot.Y + k * 0.1), k * 1.5, k * 0.55);
            if (m.Actor) ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#FFE066")), Math.Max(1.5, k * 0.22)), foot, k * 1.9, k * 0.75);
            var skin = new SolidColorBrush(Skins[Math.Abs(m.Key * 31 + m.Label.Sum(ch => ch)) % Skins.Length]);
            var jersey = new SolidColorBrush(c);
            var shorts = new SolidColorBrush(Color.FromArgb(255, (byte)(c.R * 0.8), (byte)(c.G * 0.8), (byte)(c.B * 0.8)));
            double hw = k * 0.62;
            // Legs, shorts, jersey, arms and head: a simple upright figure 6' 6" tall.
            ctx.DrawRectangle(skin, null, new Rect(foot.X - hw * 0.8, foot.Y - u * 2.9, hw * 0.55, u * 2.9), hw * 0.2, hw * 0.2);
            ctx.DrawRectangle(skin, null, new Rect(foot.X + hw * 0.25, foot.Y - u * 2.9, hw * 0.55, u * 2.9), hw * 0.2, hw * 0.2);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(255, 20, 20, 24)), null, new Rect(foot.X - hw * 0.9, foot.Y - u * 0.35, hw * 1.8, u * 0.35), hw * 0.15, hw * 0.15);
            ctx.DrawRectangle(shorts, null, new Rect(foot.X - hw, foot.Y - u * 3.6, hw * 2, u * 1.25), hw * 0.25, hw * 0.25);
            ctx.DrawRectangle(skin, null, new Rect(foot.X - hw * 1.45, foot.Y - u * 5.6, hw * 0.42, u * 2.3), hw * 0.2, hw * 0.2);
            ctx.DrawRectangle(skin, null, new Rect(foot.X + hw * 1.03, foot.Y - u * 5.6, hw * 0.42, u * 2.3), hw * 0.2, hw * 0.2);
            var torso = new Rect(foot.X - hw * 1.05, foot.Y - u * 5.75, hw * 2.1, u * 2.4);
            ctx.DrawRectangle(jersey, new Pen(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), 0.8), torso, hw * 0.35, hw * 0.35);
            ctx.DrawEllipse(skin, null, new Point(foot.X, foot.Y - u * 6.15), hw * 0.58, u * 0.48);
            double lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
            double fs = Math.Clamp(u * 1.15, 7, 22);
            var txt = new FormattedText(m.Label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, fs, lum > 0.6 ? new SolidColorBrush(Color.Parse("#111418")) : Brushes.White);
            if (txt.Width <= torso.Width * 1.1)
                ctx.DrawText(txt, new Point(torso.Center.X - txt.Width / 2, torso.Center.Y - txt.Height / 2));
        }
        // The ball: its shadow on the floor and the ball itself at hand height (rim height once it gets there).
        var ballFloor = proj.Map(pose.Ball.X, pose.Ball.Y);
        double bk = proj.Scale(pose.Ball.X, pose.Ball.Y);
        bool atRim = pose.ShotTo is { } st && Point.Distance(pose.Ball, st) < 0.6;
        var ball = Up(pose.Ball, atRim ? 10 : 4.2);
        double br = Math.Max(3, bk * 0.55);
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, ballFloor, br * 1.1, br * 0.45);
        ctx.DrawEllipse(new SolidColorBrush(Color.Parse("#F08A24")), new Pen(new SolidColorBrush(Color.Parse("#2A1A0C")), 1.2), ball, br, br);
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#2A1A0C")), 0.8), new Point(ball.X - br, ball.Y), new Point(ball.X + br, ball.Y));
    }
}
