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
            }
        }
        Width = cols.Count * (BoxWidth + Gap) - Gap;
        Height = HeaderHeight + first * (BoxHeight + SlotGap);
        var lines = new Lines { Owner = this, Width = Width, Height = Height, IsHitTestVisible = false };
        Children.Insert(0, lines);
    }

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
    public static string Label(Team t)
    {
        if (t.FullName.Length <= 18) return t.FullName;
        bool generic = t.Name.Length <= 4 || t.Name is "United" or "City" or "Athletic" or "Club" or "Basket" or "Basketball";
        return generic ? t.City : t.Name;
    }

    private static Control Line(Team team, int wins, BookSeries s, int row)
    {
        bool won = s.Winner == team, lost = s.Winner != null && !won;
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*,22"), Opacity = lost ? 0.5 : 1 };
        Grid.SetRow(g, row);
        g.Children.Add(new TeamLogo { Team = team, Width = 18, Height = 18, ShowText = false, VerticalAlignment = VerticalAlignment.Center });
        var name = new TeamLink { Team = team, Text = Label(team), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0), Padding = new Thickness(2, 0), MinHeight = 0, FontSize = 13, FontWeight = won ? FontWeight.Bold : FontWeight.Normal };
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
