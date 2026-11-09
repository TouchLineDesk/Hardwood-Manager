using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// A 2K-style trading card: tier gradient and gem, avatar, name, position, ability badge, team crest and five attribute bars.
/// Draws itself; set Player, Team and (optionally) Knowledge for fog.
/// </summary>
public sealed class PlayerCard : Control
{
    public static readonly StyledProperty<Player?> PlayerProperty = AvaloniaProperty.Register<PlayerCard, Player?>(nameof(Player));
    public static readonly StyledProperty<Team?> TeamProperty = AvaloniaProperty.Register<PlayerCard, Team?>(nameof(Team));
    public static readonly StyledProperty<double> KnowledgeProperty = AvaloniaProperty.Register<PlayerCard, double>(nameof(Knowledge), 1.0);
    public static readonly StyledProperty<string?> FooterProperty = AvaloniaProperty.Register<PlayerCard, string?>(nameof(Footer));
    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<PlayerCard, bool>(nameof(Compact));

    public Player? Player { get => GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    public Team? Team { get => GetValue(TeamProperty); set => SetValue(TeamProperty, value); }
    public double Knowledge { get => GetValue(KnowledgeProperty); set => SetValue(KnowledgeProperty, value); }
    /// <summary>Optional line under the bars (stats, salary, status).</summary>
    public string? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }

    static PlayerCard()
    {
        AffectsRender<PlayerCard>(PlayerProperty, TeamProperty, KnowledgeProperty, FooterProperty, CompactProperty);
        WidthProperty.OverrideDefaultValue<PlayerCard>(190);
        HeightProperty.OverrideDefaultValue<PlayerCard>(270);
    }

    private static readonly Typeface Bold = new("Inter", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface Semi = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Regular = new("Inter");

    private static FormattedText Text(string s, Typeface tf, double size, IBrush brush) =>
        new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, brush);

    public override void Render(DrawingContext ctx)
    {
        var p = Player;
        if (p == null) return;
        var w = Bounds.Width; var h = Bounds.Height;
        if (w < 8 || h < 8) return;
        var tier = Tier.ForCa(p.CurrentAbility);
        var dark = Color.Parse(tier.Dark); var light = Color.Parse(tier.Light); var accent = Color.Parse(tier.Accent);
        double r = 10;
        var frame = new RoundedRect(new Rect(0, 0, w, h), r);

        // Background gradient + diagonal sheen
        var bg = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(dark, 0), new GradientStop(light, 0.55), new GradientStop(dark, 1) },
        };
        ctx.DrawRectangle(bg, new Pen(new SolidColorBrush(accent), 1.5), frame);
        using (ctx.PushGeometryClip(new RectangleGeometry(new Rect(0, 0, w, h), r, r)))
        {
            var sheen = new StreamGeometry();
            using (var g = sheen.Open())
            {
                g.BeginFigure(new Point(w * 0.55, 0), true); g.LineTo(new Point(w, 0)); g.LineTo(new Point(w, h * 0.35)); g.LineTo(new Point(w * 0.25, h)); g.LineTo(new Point(0, h)); g.LineTo(new Point(0, h * 0.75)); g.EndFigure(true);
            }
            ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), null, sheen);
            if (tier.Name == "Galaxy Opal")
                for (int i = 0; i < 18; i++)
                {
                    double x = (i * 37 % 97) / 97.0 * w, y = (i * 53 % 89) / 89.0 * h;
                    ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), null, new Point(x, y), 1.2, 1.2);
                }

            // Avatar circle
            double av = Compact ? h * 0.42 : h * 0.36;
            double acx = w / 2, acy = Compact ? h * 0.30 : h * 0.28;
            if (Portraits.Get(p.Id) != null)
            {
                // Rendered portrait: a rounded square, slightly larger than the avatar disc.
                double ps = av + 8;
                ctx.DrawRectangle(null, new Pen(new SolidColorBrush(accent), 2), new RoundedRect(new Rect(acx - ps / 2 - 1, acy - ps / 2 - 1, ps + 2, ps + 2), ps * 0.08));
                DrawAvatar(ctx, p, Team, new Rect(acx - ps / 2, acy - ps / 2, ps, ps));
            }
            else
            {
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), new Pen(new SolidColorBrush(accent), 2), new Point(acx, acy), av / 2 + 3, av / 2 + 3);
                DrawAvatar(ctx, p, Team, new Rect(acx - av / 2, acy - av / 2, av, av));
            }

            // Ability badge (top left) and tier gem (top right)
            var k = Knowledge;
            string ability = k >= 0.999 ? p.CurrentAbility.ToString() : Core.Season.Scouting.Ability(p, k).ToString();
            var badge = new RoundedRect(new Rect(8, 8, Compact ? 40 : 48, Compact ? 26 : 32), 6);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)), new Pen(new SolidColorBrush(accent), 1), badge);
            var abText = Text(ability, Bold, ability.Length > 3 ? (Compact ? 10 : 12) : (Compact ? 14 : 18), Brushes.White);
            ctx.DrawText(abText, new Point(badge.Rect.X + (badge.Rect.Width - abText.Width) / 2, badge.Rect.Y + (badge.Rect.Height - abText.Height) / 2));
            var posText = Text(p.PrimaryPosition.ToString(), Semi, Compact ? 9 : 10, new SolidColorBrush(accent));
            ctx.DrawText(posText, new Point(badge.Rect.X + (badge.Rect.Width - posText.Width) / 2, badge.Rect.Bottom + 2));
            DrawGem(ctx, new Point(w - 22, 22), Compact ? 10 : 12, light, accent);

            // Team crest bottom-left of avatar
            if (Team != null) DrawCrest(ctx, Team, new Rect(w - 38, acy + av / 2 - 20, 28, 28));

            // Name
            string first = p.FirstName.ToUpperInvariant(), last = p.LastName.ToUpperInvariant();
            double ny = acy + av / 2 + 10;
            var firstT = Text(first, Regular, Compact ? 9 : 10.5, new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)));
            var lastT = Text(last, Bold, Compact ? 12 : 15, Brushes.White);
            lastT.MaxTextWidth = w - 16; lastT.Trimming = TextTrimming.CharacterEllipsis;
            ctx.DrawText(firstT, new Point((w - firstT.Width) / 2, ny));
            ctx.DrawText(lastT, new Point((w - Math.Min(lastT.Width, w - 16)) / 2, ny + firstT.Height - 1));

            // Attribute bars
            double by = ny + firstT.Height + lastT.Height + (Compact ? 4 : 8);
            var groups = new[] { ("SHO", AttrGroup.Shooting), ("PLY", AttrGroup.Playmaking), ("DEF", AttrGroup.Defense), ("MEN", AttrGroup.Mental), ("PHY", AttrGroup.Physical) };
            double rowH = Compact ? 11 : 14;
            if (!Compact || h > 220)
            {
                foreach (var (label, grp) in groups)
                {
                    double v = p.Attributes.Average(grp);
                    var lab = Text(label, Semi, Compact ? 8 : 9, new SolidColorBrush(accent));
                    ctx.DrawText(lab, new Point(10, by + (rowH - lab.Height) / 2));
                    double bx = 40, bw = w - 40 - 36;
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null, new RoundedRect(new Rect(bx, by + rowH * 0.3, bw, rowH * 0.4), 2));
                    double fill = Math.Clamp((v - 1) / 19.0, 0, 1);
                    var fillBrush = new SolidColorBrush(k >= 0.999 ? accent : Color.FromArgb(160, accent.R, accent.G, accent.B));
                    ctx.DrawRectangle(fillBrush, null, new RoundedRect(new Rect(bx, by + rowH * 0.3, bw * fill, rowH * 0.4), 2));
                    string val = k >= 0.999 ? v.ToString("0.0") : Core.Season.Scouting.GroupAverage(p, grp, k);
                    var valT = Text(val, Semi, Compact ? 8 : 9, Brushes.White);
                    ctx.DrawText(valT, new Point(w - 10 - valT.Width, by + (rowH - valT.Height) / 2));
                    by += rowH;
                }
            }

            // Footer
            if (!string.IsNullOrEmpty(Footer))
            {
                var ft = Text(Footer!, Regular, Compact ? 8.5 : 9.5, new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)));
                ft.MaxTextWidth = w - 16; ft.Trimming = TextTrimming.CharacterEllipsis;
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), null, new Rect(0, h - ft.Height - 8, w, ft.Height + 8));
                ctx.DrawText(ft, new Point(8, h - ft.Height - 4));
            }
        }
    }

    private static void DrawGem(DrawingContext ctx, Point c, double r, Color light, Color accent)
    {
        var g = new StreamGeometry();
        using (var s = g.Open())
        {
            s.BeginFigure(new Point(c.X, c.Y - r), true);
            s.LineTo(new Point(c.X + r * 0.85, c.Y - r * 0.3));
            s.LineTo(new Point(c.X + r * 0.55, c.Y + r));
            s.LineTo(new Point(c.X - r * 0.55, c.Y + r));
            s.LineTo(new Point(c.X - r * 0.85, c.Y - r * 0.3));
            s.EndFigure(true);
        }
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(accent, 0.35), new GradientStop(light, 1) },
        };
        ctx.DrawGeometry(brush, new Pen(new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)), 1), g);
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1), new Point(c.X - r * 0.85, c.Y - r * 0.3), new Point(c.X + r * 0.85, c.Y - r * 0.3));
    }

    internal static void DrawAvatar(DrawingContext ctx, Player p, Team? team, Rect rect)
    {
        var avatar = new Avatar { Player = p, Team = team, Width = rect.Width, Height = rect.Height };
        avatar.Measure(rect.Size); avatar.Arrange(new Rect(rect.Size));
        using (ctx.PushTransform(Matrix.CreateTranslation(rect.X, rect.Y)))
            avatar.Render(ctx);
    }

    internal static void DrawCrest(DrawingContext ctx, Team team, Rect rect)
    {
        var logo = new TeamLogo { Team = team, Width = rect.Width, Height = rect.Height, ShowText = rect.Width >= 28 };
        logo.Measure(rect.Size); logo.Arrange(new Rect(rect.Size));
        using (ctx.PushTransform(Matrix.CreateTranslation(rect.X, rect.Y)))
            logo.Render(ctx);
    }
}
