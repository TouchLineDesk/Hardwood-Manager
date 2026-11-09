using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.Controls;

/// <summary>Stylised procedural portrait: features are drawn from a hash of the player id so each face is stable.</summary>
public sealed class Avatar : Control
{
    public static readonly StyledProperty<Player?> PlayerProperty = AvaloniaProperty.Register<Avatar, Player?>(nameof(Player));
    public static readonly StyledProperty<Team?> TeamProperty = AvaloniaProperty.Register<Avatar, Team?>(nameof(Team));

    public Player? Player { get => GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    public Team? Team { get => GetValue(TeamProperty); set => SetValue(TeamProperty, value); }

    static Avatar()
    {
        AffectsRender<Avatar>(PlayerProperty, TeamProperty);
        WidthProperty.OverrideDefaultValue<Avatar>(64);
        HeightProperty.OverrideDefaultValue<Avatar>(64);
    }

    private static readonly Color[] Skins =
    {
        Color.Parse("#F1D3B3"), Color.Parse("#E0B48C"), Color.Parse("#C68E5E"), Color.Parse("#A26B3F"), Color.Parse("#7A4A2A"), Color.Parse("#4E2E1E"),
    };
    private static readonly Color[] Hairs = { Color.Parse("#1A1A1A"), Color.Parse("#2E1E12"), Color.Parse("#5C3A1E"), Color.Parse("#8B5A2B"), Color.Parse("#C9A227"), Color.Parse("#555555") };

    private static uint Hash(int id, int salt)
    {
        unchecked
        {
            uint h = (uint)id * 2654435761u ^ (uint)(salt + 11) * 2246822519u;
            h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
            return h;
        }
    }
    private static int Pick(int id, int salt, int n) => (int)(Hash(id, salt) % (uint)n);

    public override void Render(DrawingContext ctx)
    {
        var p = Player;
        if (p == null) return;
        double s = Math.Min(Bounds.Width, Bounds.Height);
        if (s < 8) return;
        double cx = Bounds.Width / 2, cy = Bounds.Height / 2;
        int id = p.Id;

        var skin = Skins[Pick(id, 1, Skins.Length)];
        var hair = Hairs[Pick(id, 2, Hairs.Length)];
        int hairStyle = Pick(id, 3, 5);      // 0 bald, 1 short, 2 high fade, 3 curls, 4 long
        int beard = Pick(id, 4, 4);          // 0 none, 1 stubble, 2 goatee, 3 full
        bool headband = Pick(id, 5, 5) == 0;
        var skinB = new SolidColorBrush(skin);
        var hairB = new SolidColorBrush(hair);
        var jersey = Team != null ? TeamLogo.Parse(Team.PrimaryColor) : Color.Parse("#334155");
        var trim = Team != null ? TeamLogo.Parse(Team.SecondaryColor) : Color.Parse("#F0A33A");

        // Background disc
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), null, new Point(cx, cy), s / 2, s / 2);
        using (ctx.PushGeometryClip(new EllipseGeometry(new Rect(cx - s / 2, cy - s / 2, s, s))))
        {
            // Shoulders / jersey
            var torso = new StreamGeometry();
            using (var g = torso.Open())
            {
                g.BeginFigure(new Point(cx - s * 0.46, cy + s * 0.62), true);
                g.QuadraticBezierTo(new Point(cx - s * 0.42, cy + s * 0.30), new Point(cx - s * 0.18, cy + s * 0.27));
                g.LineTo(new Point(cx + s * 0.18, cy + s * 0.27));
                g.QuadraticBezierTo(new Point(cx + s * 0.42, cy + s * 0.30), new Point(cx + s * 0.46, cy + s * 0.62));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(jersey), null, torso);
            ctx.DrawRectangle(new SolidColorBrush(trim), null, new Rect(cx - s * 0.08, cy + s * 0.27, s * 0.16, s * 0.3));

            // Neck
            ctx.DrawRectangle(skinB, null, new Rect(cx - s * 0.09, cy + s * 0.12, s * 0.18, s * 0.2));

            // Hair back (long)
            if (hairStyle == 4) ctx.DrawEllipse(hairB, null, new Point(cx, cy - s * 0.02), s * 0.30, s * 0.34);

            // Head
            double hw = s * 0.24, hh = s * 0.29;
            ctx.DrawEllipse(skinB, null, new Point(cx, cy - s * 0.05), hw, hh);

            // Hair
            switch (hairStyle)
            {
                case 1: ctx.DrawGeometry(hairB, null, Arc(cx, cy - s * 0.05, hw, hh, 0.52)); break;
                case 2: ctx.DrawRectangle(hairB, null, new Rect(cx - hw * 0.95, cy - s * 0.05 - hh * 1.05, hw * 1.9, hh * 0.55)); break;
                case 3:
                    for (int i = -3; i <= 3; i++) ctx.DrawEllipse(hairB, null, new Point(cx + i * hw * 0.3, cy - s * 0.05 - hh * 0.82 + Math.Abs(i) * hh * 0.06), hw * 0.22, hw * 0.22);
                    break;
                case 4: ctx.DrawGeometry(hairB, null, Arc(cx, cy - s * 0.05, hw, hh, 0.45)); break;
            }
            if (headband)
                ctx.DrawRectangle(new SolidColorBrush(trim), null, new Rect(cx - hw, cy - s * 0.05 - hh * 0.55, hw * 2, hh * 0.18));

            // Eyes and brows
            var dark = new SolidColorBrush(Color.Parse("#1F1F1F"));
            double ey = cy - s * 0.07;
            ctx.DrawEllipse(dark, null, new Point(cx - hw * 0.4, ey), s * 0.022, s * 0.022);
            ctx.DrawEllipse(dark, null, new Point(cx + hw * 0.4, ey), s * 0.022, s * 0.022);
            var brow = new Pen(hairB, s * 0.025);
            ctx.DrawLine(brow, new Point(cx - hw * 0.62, ey - s * 0.06), new Point(cx - hw * 0.2, ey - s * 0.065));
            ctx.DrawLine(brow, new Point(cx + hw * 0.2, ey - s * 0.065), new Point(cx + hw * 0.62, ey - s * 0.06));
            // Nose and mouth
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), s * 0.018), new Point(cx, ey + s * 0.02), new Point(cx + hw * 0.08, ey + s * 0.12));
            ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(150, 60, 20, 20)), s * 0.02), new Point(cx - hw * 0.28, ey + s * 0.19), new Point(cx + hw * 0.28, ey + s * 0.19));

            // Beard
            var beardB = new SolidColorBrush(Color.FromArgb(200, hair.R, hair.G, hair.B));
            if (beard == 1) ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, hair.R, hair.G, hair.B)), null, Arc(cx, cy - s * 0.05, hw, hh, -0.35));
            else if (beard == 2) ctx.DrawEllipse(beardB, null, new Point(cx, cy - s * 0.05 + hh * 0.75), hw * 0.35, hh * 0.28);
            else if (beard == 3) ctx.DrawGeometry(beardB, null, Arc(cx, cy - s * 0.05, hw, hh, -0.2));
        }
    }

    /// <summary>Cap of an ellipse: above (positive) or below (negative) a horizontal line at fraction f of the half-height.</summary>
    private static Geometry Arc(double cx, double cy, double rx, double ry, double f)
    {
        var ellipse = new EllipseGeometry(new Rect(cx - rx, cy - ry, rx * 2, ry * 2));
        var band = f >= 0
            ? new RectangleGeometry(new Rect(cx - rx, cy - ry, rx * 2, ry * (1 - f)))
            : new RectangleGeometry(new Rect(cx - rx, cy + ry * (-f), rx * 2, ry * (1 + f)));
        return new CombinedGeometry(GeometryCombineMode.Intersect, ellipse, band);
    }
}
