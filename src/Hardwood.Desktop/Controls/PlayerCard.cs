using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// The collector card (Danny's D58 B, #466–#467). The front: a metal frame (its colour follows the ability your scouts see;
/// no tier label), his photo in his jersey on his club's colours, the ability badge with his position, the club crest, his
/// name, and three plain-English skill badges. A click flips it: the back shows his season line, what he does well, his
/// career and honours, and his status (injury, mood, contract). Draws itself; set Player, Team and (optionally) Knowledge.
/// </summary>
public sealed class PlayerCard : Control
{
    public static readonly StyledProperty<Player?> PlayerProperty = AvaloniaProperty.Register<PlayerCard, Player?>(nameof(Player));
    public static readonly StyledProperty<Team?> TeamProperty = AvaloniaProperty.Register<PlayerCard, Team?>(nameof(Team));
    public static readonly StyledProperty<double> KnowledgeProperty = AvaloniaProperty.Register<PlayerCard, double>(nameof(Knowledge), 1.0);
    public static readonly StyledProperty<string?> FooterProperty = AvaloniaProperty.Register<PlayerCard, string?>(nameof(Footer));
    public static readonly StyledProperty<bool> CompactProperty = AvaloniaProperty.Register<PlayerCard, bool>(nameof(Compact));
    public static readonly StyledProperty<bool> IsFlippedProperty = AvaloniaProperty.Register<PlayerCard, bool>(nameof(IsFlipped));
    public static readonly StyledProperty<bool> FlipOnClickProperty = AvaloniaProperty.Register<PlayerCard, bool>(nameof(FlipOnClick), true);

    public Player? Player { get => GetValue(PlayerProperty); set => SetValue(PlayerProperty, value); }
    public Team? Team { get => GetValue(TeamProperty); set => SetValue(TeamProperty, value); }
    public double Knowledge { get => GetValue(KnowledgeProperty); set => SetValue(KnowledgeProperty, value); }
    /// <summary>Optional line at the foot of the front (this season's stats on Squad); the potential line otherwise.</summary>
    public string? Footer { get => GetValue(FooterProperty); set => SetValue(FooterProperty, value); }
    /// <summary>A small card (galleries): two badges and no foot line.</summary>
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
    /// <summary>Showing the back.</summary>
    public bool IsFlipped { get => GetValue(IsFlippedProperty); set => SetValue(IsFlippedProperty, value); }
    /// <summary>A click turns the card over (on by default).</summary>
    public bool FlipOnClick { get => GetValue(FlipOnClickProperty); set => SetValue(FlipOnClickProperty, value); }

    /// <summary>Where the back's facts come from (set when a career opens). Null: the back shows what the player alone carries.</summary>
    public static Func<Player, CollectorCard.Back?>? BackSource { get; set; }
    /// <summary>Tests and screenshots turn the flip animation off.</summary>
    public static bool Animate { get; set; } = true;

    static PlayerCard()
    {
        AffectsRender<PlayerCard>(PlayerProperty, TeamProperty, KnowledgeProperty, FooterProperty, CompactProperty, IsFlippedProperty);
        WidthProperty.OverrideDefaultValue<PlayerCard>(190);
        HeightProperty.OverrideDefaultValue<PlayerCard>(276);
        CursorProperty.OverrideDefaultValue<PlayerCard>(new Cursor(StandardCursorType.Hand));
    }

    private static readonly Typeface Black = new("Inter", FontStyle.Normal, FontWeight.Black);
    private static readonly Typeface Bold = new("Inter", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface Semi = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface Regular = new("Inter");
    private static readonly Color Ink = Color.Parse("#0F1318");
    private static readonly Color Panel = Color.Parse("#161B22");
    private static readonly Color Muted = Color.Parse("#8B96A5");
    private static readonly Color Text1 = Color.Parse("#E6EDF3");

    private static FormattedText Text(string s, Typeface tf, double size, IBrush brush) =>
        new(s, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, size, brush);
    private static SolidColorBrush B(Color c) => new(c);
    private static Color A(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    private void OnPortraitsChanged() => InvalidateVisual();
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Portraits.Changed += OnPortraitsChanged;
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Portraits.Changed -= OnPortraitsChanged;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayerProperty) { _back = null; if (IsFlipped) IsFlipped = false; }
    }

    // ---- Flipping ----
    private double _squash = 1;          // horizontal scale during the flip
    private DispatcherTimer? _timer;
    private CollectorCard.Back? _back;

    public PlayerCard() => Tapped += OnCardTapped;

    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        if (!FlipOnClick || Player == null) return;
        Flip();
        e.Handled = true;
    }

    /// <summary>Turns the card over (animated: the card narrows to its edge, changes face, and widens again).</summary>
    public void Flip()
    {
        if (!Animate) { IsFlipped = !IsFlipped; return; }
        _timer?.Stop();
        var start = DateTime.UtcNow; bool swapped = false;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) =>
        {
            double t = (DateTime.UtcNow - start).TotalMilliseconds / 240.0;
            if (t >= 0.5 && !swapped) { swapped = true; IsFlipped = !IsFlipped; }
            _squash = t >= 1 ? 1 : Math.Abs(Math.Cos(t * Math.PI));
            if (t >= 1) { _timer?.Stop(); _timer = null; }
            InvalidateVisual();
        });
        _timer.Start();
    }

    // ---- Name fitting ----
    /// <summary>The name line and its size for a card <paramref name="width"/> wide: the full name if it fits at the
    /// smallest size, else the last name. Shai Gilgeous-Alexander fits as SHAI GILGEOUS-ALEXANDER or GILGEOUS-ALEXANDER.</summary>
    public static (string Text, double Size) FitName(Player p, double width, bool compact)
    {
        double max = compact ? 13 : width / 10.5, min = compact ? 8.5 : 10.5;
        double room = width - 20;
        foreach (var s in new[] { p.Name.ToUpperInvariant(), p.LastName.ToUpperInvariant() })
        {
            double w1 = Text(s, Black, 10, Brushes.White).Width / 10.0;   // width per point of size
            double size = Math.Min(max, room / Math.Max(1, w1));
            if (size >= min) return (s, Math.Floor(size * 2) / 2);
        }
        return (p.LastName.ToUpperInvariant(), min);
    }

    public override void Render(DrawingContext ctx)
    {
        var p = Player;
        if (p == null) return;
        var w = Bounds.Width; var h = Bounds.Height;
        if (w < 8 || h < 8) return;

        using (ctx.PushTransform(Matrix.CreateTranslation(-w / 2, 0) * Matrix.CreateScale(Math.Max(0.02, _squash), 1) * Matrix.CreateTranslation(w / 2, 0)))
        {
            int ability = Scouting.Ability(p, Knowledge).High;
            var frame = Color.Parse(CollectorCard.FrameColor(ability));
            double r = Compact ? 10 : 14, pad = Compact ? 3 : 5;

            // The metal frame: a diagonal sheen across the frame colour.
            var metal = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(frame, 0), new GradientStop(Color.Parse("#FFF4C9"), 0.45), new GradientStop(frame, 1) },
            };
            if (ability >= 160) metal.GradientStops[1] = new GradientStop(Color.Parse("#F2FBFF"), 0.45);
            else if (ability < 135) metal.GradientStops[1] = new GradientStop(Color.Parse("#F4F6F8"), 0.45);
            ctx.DrawRectangle(metal, null, new RoundedRect(new Rect(0, 0, w, h), r));
            var inner = new Rect(pad, pad, w - pad * 2, h - pad * 2);
            ctx.DrawRectangle(B(Ink), null, new RoundedRect(inner, r - 3));
            using (ctx.PushGeometryClip(new RectangleGeometry(inner, r - 3, r - 3)))
            {
                if (IsFlipped) DrawBack(ctx, p, inner, frame);
                else DrawFront(ctx, p, inner, frame);
            }
        }
    }

    private void DrawFront(DrawingContext ctx, Player p, Rect box, Color frame)
    {
        double w = box.Width, x0 = box.X, y0 = box.Y;
        bool small = Compact;
        double photoH = box.Height * (small ? 0.60 : 0.58);
        var club = Team != null ? TeamLogo.Parse(Team.PrimaryColor) : Color.Parse("#334155");
        var photo = new Rect(x0, y0, w, photoH);

        // The club's colours behind him: a radial glow fading to near-black.
        var glow = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0.38, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, 0.38, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Lighten(club, 0.25), 0), new GradientStop(club, 0.45), new GradientStop(Darken(club, 0.75), 1) },
        };
        ctx.DrawRectangle(glow, null, photo);

        // His photo (your pack's photo in his jersey, else the drawn face and jersey), bottom-centred in the photo area.
        double ps = Math.Min(w * 0.86, photoH * 0.92);
        PlayerCardDraw.Avatar(ctx, p, Team, new Rect(x0 + (w - ps) / 2, y0 + photoH - ps, ps, ps));
        // A fade into the card at the photo's foot.
        ctx.DrawRectangle(new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(A(Ink, 0), 0), new GradientStop(Ink, 1) },
        }, null, new Rect(x0, y0 + photoH - 18, w, 18));

        // Ability badge (top left) and the club crest (top right).
        string ab = Scouting.Ability(p, Knowledge).ToString();
        double bw = small ? 34 : Math.Max(40, w * 0.2), bh = small ? 34 : bw * 1.12;
        var badge = new Rect(x0 + 7, y0 + 7, bw, bh);
        ctx.DrawRectangle(B(A(Ink, 235)), new Pen(B(frame), small ? 1.2 : 1.6), new RoundedRect(badge, 7));
        var abT = Text(ab, Black, ab.Length > 3 ? bw * 0.3 : bw * 0.48, Brushes.White);
        var posT = Text(p.PrimaryPosition.ToString(), Bold, small ? 8.5 : Math.Max(9, bw * 0.22), B(frame));
        double stack = abT.Height + posT.Height - 4;
        ctx.DrawText(abT, new Point(badge.X + (bw - abT.Width) / 2, badge.Y + (bh - stack) / 2 - 1));
        ctx.DrawText(posT, new Point(badge.X + (bw - posT.Width) / 2, badge.Y + (bh - stack) / 2 + abT.Height - 4));
        if (Team != null)
        {
            double cs = small ? 26 : Math.Max(30, w * 0.17);
            PlayerCardDraw.Crest(ctx, Team, new Rect(x0 + w - cs - 7, y0 + 7, cs, cs));
        }

        // Name and the line under it.
        double y = y0 + photoH + (small ? 2 : 4);
        var (name, size) = FitName(p, w + 2 * (Compact ? 3 : 5), small);
        var nameT = Text(name, Black, size, Brushes.White);
        ctx.DrawText(nameT, new Point(x0 + (w - nameT.Width) / 2, y));
        y += nameT.Height - 2;
        string sub = $"{(Team?.Abbreviation is { Length: > 0 } a ? a + " · " : "")}{PosLine(p)}{AgeText(p)}";
        var subT = Text(sub, Regular, small ? 8.5 : 10, B(Muted));
        subT.MaxTextWidth = w - 12; subT.Trimming = TextTrimming.CharacterEllipsis; subT.MaxLineCount = 1;
        ctx.DrawText(subT, new Point(x0 + (w - Math.Min(subT.Width, w - 12)) / 2, y));
        y += subT.Height + (small ? 3 : 6);

        // Skill badges as outlined pills, wrapped and centred.
        var badges = CollectorCard.Badges(p, Knowledge, small ? 2 : 3);
        double pillH = small ? 15 : 19, gap = 4, fs = small ? 8 : 9.5;
        var rows = new List<List<(FormattedText t, double pw)>> { new() };
        double rowW = 0, maxRow = w - 12;
        foreach (var bName in badges)
        {
            var t = Text(bName, Semi, fs, B(frame));
            double pw = t.Width + (small ? 10 : 14);
            if (rows[^1].Count > 0 && rowW + gap + pw > maxRow) { rows.Add(new()); rowW = 0; }
            rowW += (rows[^1].Count > 0 ? gap : 0) + pw;
            rows[^1].Add((t, Math.Min(pw, maxRow)));
        }
        double footRoom = small ? 0 : 16;
        foreach (var row in rows)
        {
            if (y + pillH > box.Bottom - footRoom - 2) break;
            double total = row.Sum(x => x.pw) + gap * (row.Count - 1);
            double x = x0 + (w - total) / 2;
            foreach (var (t, pw) in row)
            {
                ctx.DrawRectangle(B(A(frame, 28)), new Pen(B(frame), 1), new RoundedRect(new Rect(x, y, pw, pillH), pillH / 2));
                t.MaxTextWidth = pw - 6; t.Trimming = TextTrimming.CharacterEllipsis; t.MaxLineCount = 1;
                ctx.DrawText(t, new Point(x + (pw - Math.Min(t.Width, pw - 6)) / 2, y + (pillH - t.Height) / 2));
                x += pw + gap;
            }
            y += pillH + gap;
        }

        // Foot: the given footer (or the potential line) and the flip hint.
        if (!small)
        {
            string left = string.IsNullOrEmpty(Footer) ? CollectorCard.PotentialLine(p, Knowledge) : Footer!;
            var hint = FlipOnClick ? Text("Flip ↻", Semi, 9, B(A(frame, 220))) : null;
            var lt = Text(left, Regular, 9, B(Muted));
            lt.MaxTextWidth = w - 16 - (hint?.Width + 8 ?? 0); lt.Trimming = TextTrimming.CharacterEllipsis; lt.MaxLineCount = 1;
            double fy = box.Bottom - lt.Height - 5;
            ctx.DrawText(lt, new Point(x0 + 8, fy));
            if (hint != null) ctx.DrawText(hint, new Point(x0 + w - hint.Width - 8, fy));
        }
    }

    private static string PosLine(Player p) => p.SecondaryPosition is { } s ? $"{p.PrimaryPosition}/{s}" : p.PrimaryPosition.ToString();
    private static string AgeText(Player p) => Today?.Invoke() is { } d ? $" · age {p.Age(d)}" : "";
    /// <summary>The date ages are shown on (the session's today; set with <see cref="BackSource"/>).</summary>
    public static Func<DateOnly>? Today { get; set; }

    private void DrawBack(DrawingContext ctx, Player p, Rect box, Color frame)
    {
        _back ??= BackSource?.Invoke(p) ?? Fallback(p);
        var b = _back;
        bool small = Compact;
        double x0 = box.X + 8, w = box.Width - 16, y = box.Y + 7;
        double k = small ? 0.78 : Math.Clamp(box.Width / 190.0, 0.9, 1.25);

        // Header: last name and club.
        var nameT = Text(p.LastName.ToUpperInvariant(), Black, 15 * k, Brushes.White);
        nameT.MaxTextWidth = w * 0.62; nameT.Trimming = TextTrimming.CharacterEllipsis; nameT.MaxLineCount = 1;
        ctx.DrawText(nameT, new Point(x0, y));
        if (Team != null)
        {
            var clubT = Text($"{p.PrimaryPosition} · {Team.Abbreviation}", Semi, 9 * k, B(Muted));
            ctx.DrawText(clubT, new Point(x0 + w - clubT.Width, y + nameT.Height - clubT.Height - 1));
        }
        y += nameT.Height + 3 * k;

        // Season line: three stat tiles.
        double tileW = (w - 8) / 3, tileH = 34 * k;
        var stats = new[] { ("PTS", b.Ppg), ("REB", b.Rpg), ("AST", b.Apg) };
        for (int i = 0; i < 3; i++)
        {
            var r = new Rect(x0 + i * (tileW + 4), y, tileW, tileH);
            ctx.DrawRectangle(B(Panel), null, new RoundedRect(r, 6));
            var v = Text(stats[i].Item2 is { } d ? d.ToString("0.0") : "–", Black, 15 * k, Brushes.White);
            var l = Text(stats[i].Item1, Semi, 7.5 * k, B(Muted));
            ctx.DrawText(v, new Point(r.X + (r.Width - v.Width) / 2, r.Y + 2));
            ctx.DrawText(l, new Point(r.X + (r.Width - l.Width) / 2, r.Y + 2 + v.Height - 3));
        }
        y += tileH + 2;
        var sl = Text(b.StatsLabel, Regular, 8 * k, B(Muted));
        ctx.DrawText(sl, new Point(x0 + (w - sl.Width) / 2, y));
        y += sl.Height + 4 * k;

        // What he does: his best areas as bars.
        y = Heading(ctx, "WHAT HE DOES", x0, y, k, frame);
        double rowH = 13 * k, labelW = 62 * k, numW = 16 * k;
        foreach (var (name, val) in b.Strengths.Take(small ? 3 : 4))
        {
            var lt = Text(name, Regular, 8.5 * k, B(Text1));
            ctx.DrawText(lt, new Point(x0, y + (rowH - lt.Height) / 2));
            double bx = x0 + labelW, bwid = w - labelW - numW - 4;
            ctx.DrawRectangle(B(Color.Parse("#1E2630")), null, new RoundedRect(new Rect(bx, y + rowH * 0.32, bwid, rowH * 0.36), 2));
            ctx.DrawRectangle(B(val >= 14 ? frame : Muted), null, new RoundedRect(new Rect(bx, y + rowH * 0.32, bwid * Math.Clamp(val / 20.0, 0.03, 1), rowH * 0.36), 2));
            var nt = Text(val.ToString(), Semi, 8.5 * k, Brushes.White);
            ctx.DrawText(nt, new Point(x0 + w - nt.Width, y + (rowH - nt.Height) / 2));
            y += rowH;
        }
        y += 4 * k;

        // Career and honours.
        if (!small)
        {
            y = Heading(ctx, "CAREER", x0, y, k, frame);
            var ct = Text(b.Career, Regular, 9 * k, B(Text1));
            ct.MaxTextWidth = w; ct.Trimming = TextTrimming.CharacterEllipsis; ct.MaxLineCount = 1;
            ctx.DrawText(ct, new Point(x0, y)); y += ct.Height;
            var ht = Text(b.Honours, Regular, 8.5 * k, B(Muted));
            ht.MaxTextWidth = w; ht.Trimming = TextTrimming.CharacterEllipsis; ht.MaxLineCount = 2;
            ctx.DrawText(ht, new Point(x0, y)); y += ht.Height;
        }

        // Status chips at the foot, wrapped upwards from the bottom.
        double chipH = 15 * k, gap = 4, bottom = box.Bottom - 6;
        var chips = b.Chips.Select(c => (c, t: Text(c.Text, Semi, 8 * k, B(ChipFore(c.Kind))))).ToList();
        var rows = new List<List<(CollectorCard.Chip c, FormattedText t)>> { new() };
        double rw = 0;
        foreach (var it in chips)
        {
            double cw = it.t.Width + 12;
            if (rows[^1].Count > 0 && rw + gap + cw > w) { rows.Add(new()); rw = 0; }
            rw += (rows[^1].Count > 0 ? gap : 0) + cw; rows[^1].Add(it);
        }
        double cy = bottom - rows.Count * (chipH + gap) + gap;
        if (cy < y + 2) { rows = rows.Take(1).ToList(); cy = bottom - chipH; }
        foreach (var row in rows)
        {
            double cx = x0;
            foreach (var (c, t) in row)
            {
                double cw = Math.Min(t.Width + 12, w);
                ctx.DrawRectangle(B(ChipBack(c.Kind)), null, new RoundedRect(new Rect(cx, cy, cw, chipH), chipH / 2));
                t.MaxTextWidth = cw - 8; t.Trimming = TextTrimming.CharacterEllipsis; t.MaxLineCount = 1;
                ctx.DrawText(t, new Point(cx + 6, cy + (chipH - t.Height) / 2));
                cx += cw + gap;
            }
            cy += chipH + gap;
        }
    }

    private static double Heading(DrawingContext ctx, string s, double x, double y, double k, Color frame)
    {
        var t = Text(s, Bold, 7.5 * k, B(frame));
        ctx.DrawText(t, new Point(x, y));
        return y + t.Height + 1;
    }

    private static Color ChipFore(string kind) => kind switch { "bad" => Color.Parse("#F85149"), "warn" => Color.Parse("#E3B341"), "good" => Color.Parse("#3FB950"), _ => Color.Parse("#C9D1D9") };
    private static Color ChipBack(string kind) => kind switch { "bad" => Color.Parse("#3A1A1A"), "warn" => Color.Parse("#3A2E12"), "good" => Color.Parse("#12301F"), _ => Color.Parse("#1E2630") };

    private CollectorCard.Back Fallback(Player p) => new("No games yet", null, null, null,
        CollectorCard.Strengths(p, Knowledge), p.RookieSeason is { } rs ? $"Rookie season {rs}-{(rs + 1) % 100:00}" : "Career totals count from this season", "No honours yet",
        new[] { new CollectorCard.Chip(p.Injury == null ? "Healthy" : p.Injury.Name, p.Injury == null ? "good" : "bad"),
                new CollectorCard.Chip(p.Contract is { } c ? $"{CollectorCard.Money(c.Salary)} · {c.Years} yrs" : "Free agent", p.Contract == null ? "warn" : "") });

    private static Color Lighten(Color c, double f) => Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
    private static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));
}

/// <summary>Drawing helpers shared by cards.</summary>
internal static class PlayerCardDraw
{
    internal static void Avatar(DrawingContext ctx, Player p, Team? team, Rect rect)
    {
        var avatar = new Avatar { Player = p, Team = team, Width = rect.Width, Height = rect.Height };
        avatar.Measure(rect.Size); avatar.Arrange(new Rect(rect.Size));
        using (ctx.PushTransform(Matrix.CreateTranslation(rect.X, rect.Y)))
            avatar.Render(ctx);
    }

    internal static void Crest(DrawingContext ctx, Team team, Rect rect)
    {
        var logo = new TeamLogo { Team = team, Width = rect.Width, Height = rect.Height, ShowText = rect.Width >= 28 };
        logo.Measure(rect.Size); logo.Arrange(new Rect(rect.Size));
        using (ctx.PushTransform(Matrix.CreateTranslation(rect.X, rect.Y)))
            logo.Render(ctx);
    }
}
