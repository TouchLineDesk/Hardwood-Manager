using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// A playoff bracket drawn as one (Danny, #470): a column per round, a box per series (logo, the club's name, the series
/// score; the winner bright, the loser faded), and elbow lines from each pair of series to the one their winners meet in.
/// Rounds not played yet are empty "TBD" boxes. Built from real controls (not drawn text), so names are links and the
/// layout check can see them.
/// </summary>
public sealed class BracketView : Canvas
{
    public static readonly StyledProperty<IReadOnlyList<BracketColumn>?> ColumnsProperty =
        AvaloniaProperty.Register<BracketView, IReadOnlyList<BracketColumn>?>(nameof(Columns));
    /// <summary>A team to highlight (the user's club).</summary>
    public static readonly StyledProperty<Team?> MineProperty = AvaloniaProperty.Register<BracketView, Team?>(nameof(Mine));

    public IReadOnlyList<BracketColumn>? Columns { get => GetValue(ColumnsProperty); set => SetValue(ColumnsProperty, value); }
    public Team? Mine { get => GetValue(MineProperty); set => SetValue(MineProperty, value); }

    public const double BoxWidth = 200, BoxHeight = 66, Gap = 34, SlotGap = 12, HeaderHeight = 26;

    private static readonly IBrush LineBrush = new SolidColorBrush(Color.Parse("#4A5563"));
    private static readonly IBrush MineBrush = new SolidColorBrush(Color.Parse("#332F6FB3"));
    private static readonly IBrush BoxBrush = new SolidColorBrush(Color.Parse("#1C232C"));
    private static readonly IBrush WinBrush = new SolidColorBrush(Color.Parse("#3FB950"));
    private readonly List<(int col, int slot, Rect box)> _boxes = new();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ColumnsProperty || change.Property == MineProperty) Build();
    }

    /// <summary>The y of a slot's centre: the first round's slots are stacked, every later slot sits between its two feeders.</summary>
    private static double Centre(int col, int slot, int firstCount)
    {
        double unit = BoxHeight + SlotGap;
        double span = Math.Pow(2, col);
        return HeaderHeight + unit * span * slot + unit * span / 2 - SlotGap / 2.0;
    }

    private void Build()
    {
        Children.Clear();
        _boxes.Clear();
        var cols = Columns;
        if (cols == null || cols.Count == 0) { Width = 0; Height = 0; InvalidateVisual(); return; }
        int first = cols[0].Slots.Count;
        _peers = cols.SelectMany(c => c.Slots).Where(x => x != null).SelectMany(x => new[] { x!.High, x.Low }).Distinct().ToList();
        _labels = BracketLabels(_peers);
        for (int c = 0; c < cols.Count; c++)
        {
            double x = c * (BoxWidth + Gap);
            var head = new TextBlock
            {
                Text = cols[c].Round, FontWeight = FontWeight.SemiBold, FontSize = 12.5, Width = BoxWidth, TextWrapping = TextWrapping.Wrap,
            };
            head.Classes.Add("muted");
            SetLeft(head, x); SetTop(head, 0);
            Children.Add(head);
            // A column whose slots don't halve (an odd field) just stacks.
            bool halves = c == 0 || cols[c - 1].Slots.Count == cols[c].Slots.Count * 2;
            for (int i = 0; i < cols[c].Slots.Count; i++)
            {
                double cy = halves ? Centre(c, i, first) : HeaderHeight + (BoxHeight + SlotGap) * i + BoxHeight / 2;
                var box = new Rect(x, cy - BoxHeight / 2, BoxWidth, BoxHeight);
                var el = Box(cols[c].Slots[i]);
                SetLeft(el, box.X); SetTop(el, box.Y);
                Children.Add(el);
                _boxes.Add((c, i, box));
                if (NextGameLabel(cols[c].Slots[i]) is { } next)
                {
                    // H100 (run 109): a series under way says which game is next ("Game 7 next" at 3-3), as a tab on the box's bottom edge (clear of the round's heading).
                    var tab = new Border
                    {
                        Background = BoxBrush, BorderBrush = new SolidColorBrush(Color.Parse("#D29922")), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(5, 0), Height = 16,
                        Child = new TextBlock { Text = next, FontSize = 10.5, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center },
                    };
                    tab.Measure(Size.Infinity);
                    SetLeft(tab, box.Right - tab.DesiredSize.Width - 8); SetTop(tab, box.Bottom - 8);
                    Children.Add(tab);
                }
            }
        }
        Width = cols.Count * (BoxWidth + Gap) - Gap;
        Height = HeaderHeight + first * (BoxHeight + SlotGap);
        var lines = new Lines { Owner = this, Width = Width, Height = Height, IsHitTestVisible = false };
        Children.Insert(0, lines);
    }

    /// <summary>H100 (run 109): "Game 7 next" for a series that has started and isn't over; null otherwise.</summary>
    public static string? NextGameLabel(BookSeries? s) =>
        s == null || s.Complete || s.HighWins + s.LowWins == 0 ? null : $"Game {s.HighWins + s.LowWins + 1} next";

    private Control Box(BookSeries? s)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("28,28"), Margin = new Thickness(8, 4), VerticalAlignment = VerticalAlignment.Center };
        if (s == null)
        {
            var tbd = new TextBlock { Text = "TBD", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0) };
            tbd.Classes.Add("muted");
            Grid.SetRowSpan(tbd, 2);
            grid.Children.Add(tbd);
        }
        else
        {
            grid.Children.Add(Line(s.High, s.HighWins, s, 0));
            grid.Children.Add(Line(s.Low, s.LowWins, s, 1));
        }
        bool mine = s != null && Mine != null && (s.High == Mine || s.Low == Mine);
        var border = new Border
        {
            Width = BoxWidth, Height = BoxHeight, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Child = grid,
            BorderBrush = mine ? new SolidColorBrush(Color.Parse("#2F6FB3")) : LineBrush,
        };
        border.Background = mine ? MineBrush : BoxBrush;
        if (s != null) ToolTip.SetTip(border, $"{s.Round}: {s.High.FullName} {s.HighWins}-{s.LowWins} {s.Low.FullName}" + (s.Winner is { } w ? $" ({w.FullName} win)" : ""));
        return border;
    }

    /// <summary>The name in a box: the full name when it's short ("Austin FC"), else the nickname ("Trail Blazers"), unless the
    /// nickname says little on its own ("United", "City", "FC"), when it's the city.</summary>
    /// <para>H87 (run 106): a nickname another club in the same bracket or league also uses ("Mustangs" for Houston and
    /// Indianapolis) is never shown alone: the city is, or the code and nickname when the city is shared too.</para>
    public static string Label(Team t, IEnumerable<Team>? peers = null)
    {
        if (t.FullName.Length <= 18) return t.FullName;
        bool generic = t.Name.Length <= 4 || t.Name is "United" or "City" or "Athletic" or "Club" or "Basket" or "Basketball" || t.Name.EndsWith(" Club");
        var others = peers?.Where(p => p != t && p.Id != t.Id).ToList();
        bool sharedName = others?.Any(p => p.Name == t.Name) == true;
        if (!generic && !sharedName) return t.Name;
        bool sharedCity = others?.Any(p => p.City == t.City) == true;
        return sharedCity ? $"{t.Abbreviation} {t.Name}" : t.City;
    }

    private List<Team> _peers = new();
    private Dictionary<Team, string> _labels = new();

    /// <summary>H87 (run 112): one name style for a whole bracket. Per-team labels mixed "Houston Mustangs", "Lakers" and
    /// "Indianapolis" in one G League bracket. Full names when every one fits; otherwise nicknames when they're all
    /// distinct and none is generic ("United", "FC"); otherwise cities when they're all distinct; otherwise each team's own label.</summary>
    public static Dictionary<Team, string> BracketLabels(IReadOnlyList<Team> teams)
    {
        static bool Generic(Team t) => t.Name.Length <= 4 || t.Name is "United" or "City" or "Athletic" or "Club" or "Basket" or "Basketball" || t.Name.EndsWith(" Club");
        static bool Distinct(IEnumerable<string> xs) { var l = xs.ToList(); return l.Distinct().Count() == l.Count; }
        if (teams.All(t => t.FullName.Length <= 18)) return teams.ToDictionary(t => t, t => t.FullName);
        if (!teams.Any(Generic) && Distinct(teams.Select(t => t.Name))) return teams.ToDictionary(t => t, t => t.Name);
        if (Distinct(teams.Select(t => t.City)) && teams.All(t => t.City.Length > 0)) return teams.ToDictionary(t => t, t => t.City);
        return teams.ToDictionary(t => t, t => Label(t, teams));
    }

    private Control Line(Team team, int wins, BookSeries s, int row)
    {
        bool won = s.Winner == team, lost = s.Winner != null && !won;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,22"), Opacity = lost ? 0.5 : 1 };
        Grid.SetRow(g, row);
        g.Children.Add(new TeamLogo { Team = team, Width = 18, Height = 18, ShowText = false, VerticalAlignment = VerticalAlignment.Center });
        var name = new TeamLink { Team = team, Text = _labels.TryGetValue(team, out var lbl) ? lbl : Label(team, _peers), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0), Padding = new Thickness(2, 0), MinHeight = 0, FontSize = 13, FontWeight = won ? FontWeight.Bold : FontWeight.Normal };
        Grid.SetColumn(name, 1);
        g.Children.Add(name);
        var score = new TextBlock
        {
            Text = wins.ToString(), FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        if (won) score.Foreground = WinBrush;
        Grid.SetColumn(score, 2);
        g.Children.Add(score);
        return g;
    }

    /// <summary>The connecting lines, drawn by a child under the boxes (a panel's own Render is sealed).</summary>
    private sealed class Lines : Control
    {
        public required BracketView Owner { get; init; }
        public override void Render(DrawingContext ctx) => Owner.DrawLines(ctx);
    }

    private void DrawLines(DrawingContext ctx)
    {
        var cols = Columns;
        if (cols == null) return;
        var pen = new Pen(LineBrush, 1.5);
        for (int c = 1; c < cols.Count; c++)
        {
            if (cols[c - 1].Slots.Count != cols[c].Slots.Count * 2) continue;
            for (int i = 0; i < cols[c].Slots.Count; i++)
            {
                var to = _boxes.FirstOrDefault(b => b.col == c && b.slot == i).box;
                double midX = to.X - Gap / 2;
                foreach (int k in new[] { i * 2, i * 2 + 1 })
                {
                    var from = _boxes.FirstOrDefault(b => b.col == c - 1 && b.slot == k).box;
                    if (from.Width == 0) continue;
                    var y = from.Y + from.Height / 2;
                    ctx.DrawLine(pen, new Point(from.Right, y), new Point(midX, y));
                    ctx.DrawLine(pen, new Point(midX, y), new Point(midX, to.Y + to.Height / 2));
                }
                ctx.DrawLine(pen, new Point(midX, to.Y + to.Height / 2), new Point(to.X, to.Y + to.Height / 2));
            }
        }
    }
}
