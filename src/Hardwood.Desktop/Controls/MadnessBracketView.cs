using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.Controls;

/// <summary>What the March bracket draws: the college world, the coached program (if any) and the view: -1 is the whole
/// field, 0-3 one region. A class (not a record) so every refill is a change and the bracket rebuilds.</summary>
public sealed class MadnessFieldData
{
    public required CollegeWorld World { get; init; }
    public int? Mine { get; init; }
    public int Region { get; init; } = -1;
}

/// <summary>
/// The March Madness bracket as the TV graphic draws it (Danny, Nov 24, `claude/bracket-redesign.md`): the whole field of 64 on
/// one screen, the East and South on the left flowing right, the West and Midwest on the right flowing left, and the national
/// semi-finals and the final in the middle. Each team is a bar in its primary colour with its crest, its name in condensed
/// caps and a seed chip on the outside edge; later rounds are dark slots joined by elbow lines that fill with the winner's bar.
/// A played game shows both scores and fades the loser. The coached program's bars are outlined in the accent colour, so its
/// path reads at a glance. A region view draws one quarter bigger. Real controls on a canvas (like <see cref="BracketView"/>),
/// so names are links and the layout gate can see them. Display only: nothing here touches the bracket's data.
/// <para>Our own centre emblem and "National champion" plate, in Big Shoulders: no NCAA, event or broadcaster marks.</para>
/// </summary>
public sealed class MadnessBracketView : Canvas
{
    public static readonly StyledProperty<MadnessFieldData?> FieldProperty =
        AvaloniaProperty.Register<MadnessBracketView, MadnessFieldData?>(nameof(Field));
    public MadnessFieldData? Field { get => GetValue(FieldProperty); set => SetValue(FieldProperty, value); }

    /// <summary>The card fonts as an Avalonia font collection, registered once. (An "avares://…#Family" key alone didn't
    /// resolve them: Avalonia reads "Thin Black" and "ExtraBold" in the family names as weights.)</summary>
    private static readonly bool Registered = Register();
    private static bool Register()
    {
        try
        {
            FontManager.Current.AddFontCollection(new Avalonia.Media.Fonts.EmbeddedFontCollection(
                new Uri("fonts:hardwood", UriKind.Absolute), new Uri("avares://HardwoodManager/Assets/Fonts", UriKind.Absolute)));
            return true;
        }
        catch (Exception) { return false; } // already registered, or no font manager: the default type is used
    }
    public static readonly FontFamily Condensed = Registered ? new("fonts:hardwood#Barlow Condensed") : FontFamily.Default;
    public static readonly FontFamily Display = Registered ? new("fonts:hardwood#Big Shoulders Display") : FontFamily.Default;

    private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#4A525E"));
    private static readonly IBrush SlotBrush = new SolidColorBrush(Color.Parse("#1E232B"));
    private static readonly IBrush SlotEdge = new SolidColorBrush(Color.Parse("#323A46"));
    private static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#9AA4B2"));
    private static readonly IBrush Orange = new SolidColorBrush(Color.Parse("#E8731C"));
    public static readonly string[] RoundLabels = { "FIRST ROUND", "SECOND ROUND", "SWEET 16", "ELITE EIGHT", "FINAL FOUR" };
    /// <summary>A tab colour per region (East, West, South, Midwest).</summary>
    private static readonly string[] RegionColours = { "#2F6FB3", "#B33A3A", "#2E8B57", "#C7891B" };

    /// <summary>The sizes of one layout: bar widths per column (Round of 64 … the regional champion), bar height, the gap
    /// between the two bars of a game, the gap between games, the gap between columns, and the type size.</summary>
    private sealed record Metrics(double[] W, double BarH, double PairGap, double GameGap, double ColGap, double Font);
    private static readonly Metrics FullM = new(new double[] { 122, 90, 80, 80, 88 }, 20, 2, 7, 8, 11.5);
    private static readonly Metrics RegionM = new(new double[] { 200, 184, 176, 176, 176 }, 30, 3, 12, 22, 14);
    public const double Header = 22, RegionGap = 34, CentreW = 136;

    /// <summary>Every bar placed, by (region or -1 for the centre, column, index), for the lines and for tests.</summary>
    private readonly List<(Rect A, Rect B, bool Mirror, bool Mine)> _links = new();
    /// <summary>The names drawn, for tests: every team bar's text.</summary>
    public List<string> Names { get; } = new();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FieldProperty) Build();
    }

    private CollegeWorld? _w;
    private int? _mine;
    private List<BracketBox> _boxes = new();

    private void Build()
    {
        Children.Clear(); _links.Clear(); Names.Clear();
        var f = Field;
        if (f == null || !f.World.Season.Selected) { Width = 0; Height = 0; InvalidateVisual(); return; }
        _w = f.World; _mine = f.Mine;
        _boxes = MadnessBracket.Boxes(f.World.Season);
        if (_boxes.Count == 0) { Width = 0; Height = 0; return; }
        if (f.Region is >= 0 and <= 3) BuildRegion(f.Region);
        else BuildFull();
        Children.Insert(0, new Lines { Owner = this, Width = Width, Height = Height, IsHitTestVisible = false });
    }

    // ---- The whole field ----

    private void BuildFull()
    {
        var m = FullM;
        double regionH = RegionHeight(m);
        double side = m.W.Sum() + m.ColGap * (m.W.Length - 1);
        double centreX = side + m.ColGap + 6, rightX = centreX + CentreW + m.ColGap + 6;
        double width = rightX + side;
        // Round labels over each column, both halves.
        double x = 0;
        for (int k = 0; k < 5; k++)
        {
            Label(RoundLabels[k], x, 0, m.W[k], HorizontalAlignment.Left);
            Label(RoundLabels[k], width - x - m.W[k], 0, m.W[k], HorizontalAlignment.Right);
            x += m.W[k] + m.ColGap;
        }
        Label("CHAMPIONSHIP", centreX, 0, CentreW, HorizontalAlignment.Center);
        // East (0) top-left, South (2) bottom-left, West (1) top-right, Midwest (3) bottom-right.
        double top = Header, bottom = Header + regionH + RegionGap;
        var east = Region(0, 0, top, false, m);
        var south = Region(2, 0, bottom, false, m);
        var west = Region(1, rightX, top, true, m);
        var midwest = Region(3, rightX, bottom, true, m);
        // The final: semi-final 0 (East v West) feeds the top bar, semi-final 1 (South v Midwest) the bottom one.
        double mid = Header + regionH + RegionGap / 2;
        var fin = _boxes.FirstOrDefault(b => b.Round == 6);
        var semis = _boxes.Where(b => b.Round == 5).OrderBy(b => b.Slot).ToList();
        double fh = m.BarH + 4;
        var finA = new Rect(centreX, mid - fh - 3, CentreW, fh);
        var finB = new Rect(centreX, mid + 3, CentreW, fh);
        AddBar(finA, fin?.TeamA, fin?.SeedA ?? 0, fin?.ScoreA, Lost(fin, fin?.TeamA), false, m, m.Font + 1);
        AddBar(finB, fin?.TeamB, fin?.SeedB ?? 0, fin?.ScoreB, Lost(fin, fin?.TeamB), false, m, m.Font + 1);
        Link(east, finA, false, IsMine(semis.ElementAtOrDefault(0)?.TeamA));
        Link(west, finA, true, IsMine(semis.ElementAtOrDefault(0)?.TeamB));
        Link(south, finB, false, IsMine(semis.ElementAtOrDefault(1)?.TeamA));
        Link(midwest, finB, true, IsMine(semis.ElementAtOrDefault(1)?.TeamB));
        // Our own emblem above the final, the champion's plate below.
        int year = _boxes.FirstOrDefault(b => b.Date != null)?.Date?.Year ?? DateTime.Now.Year;
        Emblem(centreX, finA.Y - 150, year);
        Champion(centreX, finB.Bottom + 26, fin?.Winner);
        Height = Header + regionH * 2 + RegionGap;
        Height = FirstFour(-1, 0, Height + 16, width, m);
        Width = width;
    }

    private static double RegionHeight(Metrics m) => 8 * (2 * m.BarH + m.PairGap) + 7 * m.GameGap;

    /// <summary>One region's four rounds and its champion's bar, from the outside edge in. Returns the champion's bar.</summary>
    private Rect Region(int region, double x0, double y0, bool mirror, Metrics m)
    {
        double regionW = m.W.Sum() + m.ColGap * (m.W.Length - 1);
        // Centres of the 16 first-round bars, then each later column's bar between its two feeders.
        var centres = new List<double[]>();
        var first = new double[16];
        for (int j = 0; j < 16; j++)
            first[j] = y0 + (j / 2) * (2 * m.BarH + m.PairGap + m.GameGap) + (j % 2) * (m.BarH + m.PairGap) + m.BarH / 2;
        centres.Add(first);
        for (int k = 1; k < 5; k++)
        {
            var prev = centres[k - 1];
            centres.Add(Enumerable.Range(0, prev.Length / 2).Select(j => (prev[2 * j] + prev[2 * j + 1]) / 2).ToArray());
        }
        double ColX(int k)
        {
            double off = 0;
            for (int i = 0; i < k; i++) off += m.W[i] + m.ColGap;
            return mirror ? x0 + regionW - off - m.W[k] : x0 + off;
        }
        var rects = new List<Rect[]>();
        for (int k = 0; k < 5; k++)
        {
            var col = new Rect[centres[k].Length];
            for (int j = 0; j < col.Length; j++)
                col[j] = new Rect(ColX(k), centres[k][j] - m.BarH / 2, m.W[k], m.BarH);
            rects.Add(col);
        }
        // Columns 0-3 are the teams of rounds 1-4 (two bars per game); column 4 is the regional champion: the team in the
        // national semi-final, with that game's score.
        for (int k = 0; k < 4; k++)
        {
            int round = k + 1, per = 8 >> k;
            var games = _boxes.Where(b => b.Round == round && MadnessBracket.RegionOf(b) == region).OrderBy(b => b.Slot).ToList();
            for (int g = 0; g < per; g++)
            {
                var b = games.ElementAtOrDefault(g);
                int seedA = b?.TeamA != null ? b.SeedA : 0, seedB = b?.TeamB != null ? b.SeedB : 0;
                if (round == 1)
                {
                    // An empty first-round line is a First Four winner still to come: keep its seed.
                    seedA = seedA > 0 ? seedA : MadnessBracket.Order[(2 * g) % 16];
                    seedB = seedB > 0 ? seedB : MadnessBracket.Order[(2 * g + 1) % 16];
                }
                AddBar(rects[k][2 * g], b?.TeamA, seedA, b?.ScoreA, Lost(b, b?.TeamA), mirror, m, m.Font, round == 1 && b?.TeamA == null ? "FIRST FOUR" : null);
                AddBar(rects[k][2 * g + 1], b?.TeamB, seedB, b?.ScoreB, Lost(b, b?.TeamB), mirror, m, m.Font, round == 1 && b?.TeamB == null ? "FIRST FOUR" : null);
                if (k < 4)
                {
                    var to = rects[k + 1][g];
                    _links.Add((rects[k][2 * g], to, mirror, IsMine(b?.TeamA) && b?.Winner == b?.TeamA && b?.Played == true));
                    _links.Add((rects[k][2 * g + 1], to, mirror, IsMine(b?.TeamB) && b?.Winner == b?.TeamB && b?.Played == true));
                }
            }
        }
        var semi = _boxes.FirstOrDefault(b => b.Round == 5 && b.Slot == region / 2);
        bool isA = region % 2 == 0;
        int? champ = semi == null ? null : isA ? semi.TeamA : semi.TeamB;
        AddBar(rects[4][0], champ, champ == null ? 0 : isA ? semi!.SeedA : semi!.SeedB, isA ? semi?.ScoreA : semi?.ScoreB, Lost(semi, champ), mirror, m, m.Font);
        // The region's tab, between its two Elite Eight bars.
        var e8 = rects[3];
        double tabY = (e8[0].Bottom + e8[1].Y) / 2;
        RegionTab(region, e8[0].X, tabY, e8[0].Width, m);
        return rects[4][0];
    }

    // ---- One region, bigger ----

    private void BuildRegion(int region)
    {
        var m = RegionM;
        double x = 0;
        for (int k = 0; k < 5; k++)
        {
            Label(k == 4 ? "REGIONAL CHAMPION" : RoundLabels[k], x, 0, m.W[k], HorizontalAlignment.Left);
            x += m.W[k] + m.ColGap;
        }
        Region(region, 0, Header + 4, false, m);
        Width = x - m.ColGap;
        Height = Header + 4 + RegionHeight(m);
        Height = FirstFour(region, 0, Height + 18, Width, m);
    }

    // ---- Pieces ----

    private bool IsMine(int? id) => id != null && id == _mine;
    private static bool Lost(BracketBox? b, int? id) => b is { Played: true } && id != null && b.Winner != id;

    /// <summary>A team bar (or an empty dark slot). The seed chip sits on the outside edge, the score on the inside.</summary>
    private void AddBar(Rect r, int? id, int seed, int? score, bool lost, bool mirror, Metrics m, double font, string? placeholder = null)
    {
        var team = id is { } t ? _w!.Program(t) : null;
        var border = new Border { Width = r.Width, Height = r.Height, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1) };
        var grid = new Grid { ColumnDefinitions = mirror ? new ColumnDefinitions("Auto,*,Auto,Auto") : new ColumnDefinitions("Auto,Auto,*,Auto"), VerticalAlignment = VerticalAlignment.Stretch };
        border.Child = grid;
        double chip = Math.Round(r.Height * 0.8);
        int seedCol = mirror ? 3 : 0, crestCol = mirror ? 2 : 1, nameCol = mirror ? 1 : 2, scoreCol = mirror ? 0 : 3;
        if (team == null)
        {
            border.Background = SlotBrush; border.BorderBrush = SlotEdge;
            if (placeholder != null)
            {
                if (seed > 0) grid.Children.Add(SeedChip(seed, chip, seedCol, font, muted: true));
                var ph = new TextBlock { Text = placeholder, FontFamily = Condensed, FontWeight = FontWeight.ExtraBold, FontSize = font - 1.5, Foreground = LabelBrush, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0), HorizontalAlignment = mirror ? HorizontalAlignment.Right : HorizontalAlignment.Left };
                Grid.SetColumn(ph, nameCol); grid.Children.Add(ph);
            }
            SetLeft(border, r.X); SetTop(border, r.Y); Children.Add(border);
            return;
        }
        var bg = SafeColor(team.PrimaryColor, "#2F3640");
        var ink = ThemeService.ReadableOn(bg, Colors.White);
        border.Background = new SolidColorBrush(bg);
        bool mine = IsMine(id);
        border.BorderBrush = mine ? new SolidColorBrush(ThemeService.Accent) : new SolidColorBrush(SafeColor(team.SecondaryColor, "#000000"), 0.7);
        border.BorderThickness = new Thickness(mine ? 2 : 1);
        border.Opacity = lost ? 0.4 : 1;
        grid.Children.Add(SeedChip(seed, chip, seedCol, font, muted: false));
        double crest = r.Height - 4;
        var scoreText = new TextBlock
        {
            Text = score?.ToString() ?? "", FontFamily = Condensed, FontWeight = FontWeight.ExtraBold, FontSize = font, Foreground = new SolidColorBrush(ink),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(score == null ? 0 : 3, 0),
        };
        Grid.SetColumn(scoreText, scoreCol); grid.Children.Add(scoreText);
        // The name: the school in caps, or its abbreviation when the school doesn't fit the bar.
        // The room left: the bar less its edge (2 px a side for ours), the seed chip and its margins, the crest and its margins,
        // the score and its margins, and the name's own padding, with a pixel to spare.
        double room = r.Width - 4 - (chip + 6) - (crest + 4) - (score == null ? 0 : Measure(score.ToString()!, font) + 6) - 6 - 1;
        string name = team.City.ToUpperInvariant();
        if (Measure(name, font) > room) name = team.Abbreviation.ToUpperInvariant();
        // A narrow later-round bar with a score: the crest gives way before the name does.
        bool showCrest = Measure(name, font) <= room;
        if (!showCrest && Measure(team.City.ToUpperInvariant(), font) <= room + crest + 4) name = team.City.ToUpperInvariant();
        if (showCrest)
        {
            var logo = new TeamLogo { Team = team, Width = crest, Height = crest, ShowText = false, ShowRank = false, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0) };
            Grid.SetColumn(logo, crestCol); grid.Children.Add(logo);
        }
        var link = new TeamLink
        {
            Team = team, Text = name, ShowRank = false, FontFamily = Condensed, FontWeight = FontWeight.ExtraBold, FontSize = font, Foreground = new SolidColorBrush(ink),
            Padding = new Thickness(3, 0), Margin = new Thickness(0), MinHeight = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = mirror ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        };
        Grid.SetColumn(link, nameCol); grid.Children.Add(link);
        Names.Add(name);
        ToolTip.SetTip(border, $"({seed}) {team.FullName}" + (score != null ? $": {score}" + (lost ? ", lost" : ", won") : ""));
        SetLeft(border, r.X); SetTop(border, r.Y); Children.Add(border);
    }

    private static Color SafeColor(string hex, string fallback) => Color.TryParse(hex, out var c) ? c : Color.Parse(fallback);

    private static double Measure(string text, double font)
    {
        var tb = new TextBlock { Text = text, FontFamily = Condensed, FontWeight = FontWeight.ExtraBold, FontSize = font };
        tb.Measure(Size.Infinity);
        return tb.DesiredSize.Width;
    }

    private static Control SeedChip(int seed, double size, int col, double font, bool muted)
    {
        var chip = new Border
        {
            Width = size + 2, Height = size, CornerRadius = new CornerRadius(2), Margin = new Thickness(2, 0),
            Background = muted ? SlotEdge : Brushes.White, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = seed > 0 ? seed.ToString() : "", FontFamily = Condensed, FontWeight = FontWeight.ExtraBold, FontSize = font - 1.5, Foreground = muted ? LabelBrush : new SolidColorBrush(Color.Parse("#14171C")),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Grid.SetColumn(chip, col);
        return chip;
    }

    private void Label(string text, double x, double y, double w, HorizontalAlignment align)
    {
        var tb = new TextBlock { Text = text, FontFamily = Condensed, FontWeight = FontWeight.ExtraBold, FontSize = 11, LetterSpacing = 0.8, Foreground = LabelBrush, Width = w, TextAlignment = align switch { HorizontalAlignment.Right => TextAlignment.Right, HorizontalAlignment.Center => TextAlignment.Center, _ => TextAlignment.Left } };
        SetLeft(tb, x); SetTop(tb, y); Children.Add(tb);
    }

    private void RegionTab(int region, double x, double cy, double w, Metrics m)
    {
        var tab = new Border
        {
            Width = w, Height = m.BarH + 4, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.Parse(RegionColours[region])),
            Child = new TextBlock { Text = MadnessBracket.Regions[region].ToUpperInvariant(), FontFamily = Display, FontWeight = FontWeight.Black, FontSize = m.Font + 4, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        SetLeft(tab, x); SetTop(tab, cy - tab.Height / 2); Children.Add(tab);
    }

    /// <summary>Our own centre emblem: the year, "National semifinals" and the host city, drawn in type.</summary>
    private void Emblem(double x, double y, int year)
    {
        var host = MadnessBracket.FinalFourHost(year).Split(',')[0].ToUpperInvariant();
        var stack = new StackPanel { Width = CentreW, Spacing = 0 };
        stack.Children.Add(new TextBlock { Text = year.ToString(), FontFamily = Display, FontWeight = FontWeight.Black, FontSize = 40, Foreground = Orange, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = "NATIONAL SEMIFINALS", FontFamily = Condensed, FontWeight = FontWeight.ExtraBold, FontSize = 13, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        stack.Children.Add(new TextBlock { Text = host, FontFamily = Condensed, FontWeight = FontWeight.ExtraBold, FontSize = 12, Foreground = LabelBrush, HorizontalAlignment = HorizontalAlignment.Center });
        var frame = new Border { BorderBrush = Orange, BorderThickness = new Thickness(0, 2), Padding = new Thickness(0, 6), Child = stack };
        SetLeft(frame, x); SetTop(frame, y); Children.Add(frame);
    }

    /// <summary>The "National champion" plate under the final: the champions once the title game is played.</summary>
    private void Champion(double x, double y, int? winner)
    {
        var team = winner is { } id ? _w!.Program(id) : null;
        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(new TextBlock { Text = "NATIONAL CHAMPION", FontFamily = Display, FontWeight = FontWeight.Black, FontSize = 15, Foreground = Orange, HorizontalAlignment = HorizontalAlignment.Center });
        if (team != null)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            row.Children.Add(new TeamLogo { Team = team, Width = 30, Height = 30, ShowText = false, ShowRank = false });
            var link = new TeamLink { Team = team, Text = team.City.ToUpperInvariant(), ShowRank = false, FontFamily = Condensed, FontWeight = FontWeight.ExtraBold, FontSize = 15, Foreground = Brushes.White, Padding = new Thickness(0), MinHeight = 0, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(link);
            stack.Children.Add(row);
            Names.Add(team.City.ToUpperInvariant());
        }
        else stack.Children.Add(new TextBlock { Text = "to be crowned", FontFamily = Condensed, FontWeight = FontWeight.ExtraBold, FontSize = 12, Foreground = LabelBrush, HorizontalAlignment = HorizontalAlignment.Center });
        var plate = new Border
        {
            Width = CentreW, Padding = new Thickness(6, 8), CornerRadius = new CornerRadius(4), BorderBrush = Orange, BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(Color.Parse("#1E232B")), Child = stack,
        };
        SetLeft(plate, x); SetTop(plate, y); Children.Add(plate);
    }

    /// <summary>The First Four under the field (or a region's own): each pair and the line it plays for. Returns the new height.</summary>
    private double FirstFour(int region, double x0, double y, double width, Metrics m)
    {
        var s = _w!.Season;
        var games = s.Games.Where(g => g.Event == CollegeWorld.Madness && g.Round == 0 && (region < 0 || g.Slot / 16 == region)).OrderBy(g => g.Slot).ToList();
        if (games.Count == 0) return y - 16;
        Label("FIRST FOUR · DAYTON", x0, y, 300, HorizontalAlignment.Left);
        y += 18;
        double w = Math.Min(m.W[0] + 20, 190), gap = 22, x = x0;
        if (region < 0) x = (width - (games.Count * w + (games.Count - 1) * gap)) / 2;
        foreach (var g in games)
        {
            int seed = MadnessBracket.Order[g.Slot % 16];
            bool played = g.HomePts != null;
            int? winner = played ? (g.HomePts > g.AwayPts ? g.HomeId : g.AwayId) : null;
            var a = new Rect(x, y, w, m.BarH);
            var b = new Rect(x, y + m.BarH + m.PairGap, w, m.BarH);
            AddBar(a, g.HomeId, seed, g.HomePts, played && winner != g.HomeId, false, m, m.Font);
            AddBar(b, g.AwayId, seed, g.AwayPts, played && winner != g.AwayId, false, m, m.Font);
            var to = new TextBlock { Text = $"→ {MadnessBracket.Regions[g.Slot / 16]} · No. {seed}" + (played ? "" : $" · {g.Date:MMM d}"), FontFamily = Condensed, FontWeight = FontWeight.ExtraBold, FontSize = 11, Foreground = LabelBrush };
            SetLeft(to, x); SetTop(to, b.Bottom + 3); Children.Add(to);
            x += w + gap;
        }
        return y + 2 * m.BarH + m.PairGap + 20;
    }

    private void Link(Rect from, Rect to, bool mirror, bool mine) => _links.Add((from, to, mirror, mine));

    /// <summary>The elbow lines, drawn by a child under the bars (a panel's own Render is sealed).</summary>
    private sealed class Lines : Control
    {
        public required MadnessBracketView Owner { get; init; }
        public override void Render(DrawingContext ctx) => Owner.DrawLines(ctx);
    }

    private void DrawLines(DrawingContext ctx)
    {
        var pen = new Pen(LineBrush, 1.2);
        var minePen = new Pen(new SolidColorBrush(ThemeService.Accent), 2);
        foreach (var (a, b, mirror, mine) in _links.OrderBy(l => l.Mine))
        {
            var p = mine ? minePen : pen;
            double y1 = a.Center.Y, y2 = b.Center.Y;
            // Left half flows right (from a's right edge into b's left edge); the right half flows left.
            double x1 = mirror ? a.Left : a.Right, x2 = mirror ? b.Right : b.Left;
            double mx = (x1 + x2) / 2;
            ctx.DrawLine(p, new Point(x1, y1), new Point(mx, y1));
            ctx.DrawLine(p, new Point(mx, y1), new Point(mx, y2));
            ctx.DrawLine(p, new Point(mx, y2), new Point(x2, y2));
        }
    }
}
