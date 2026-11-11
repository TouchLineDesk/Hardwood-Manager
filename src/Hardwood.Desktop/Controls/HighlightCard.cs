using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Hardwood.Core.Engine;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// Match day part 2 (roadmap 2): a highlight card — the player's face (when there is one), a small caps headline, the
/// number that matters and a line of detail, edged in the tone (or the team's colour).
/// </summary>
public sealed class HighlightCard : Border
{
    public HighlightCard(Highlight h)
    {
        var accent = h.Team != null ? ThemeService.AccentFor(h.Team) : StatChip.Info;
        var edge = StatChip.ToneColor(h.Tone, accent);
        Background = new SolidColorBrush(Color.Parse("#1C232C"));
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(10, 8, 14, 9);
        Margin = new Thickness(0, 0, 8, 8);
        MinWidth = 120;
        BorderThickness = new Thickness(3, 0, 0, 0);
        BorderBrush = new SolidColorBrush(edge);
        var text = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = h.Title.ToUpperInvariant(), TextWrapping = TextWrapping.Wrap, FontSize = 10.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Foreground = new SolidColorBrush(StatChip.Muted) },
                new TextBlock { Text = h.Value, TextWrapping = TextWrapping.Wrap, FontSize = 18, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(h.Tone is "good" or "bad" or "warn" ? edge : StatChip.Text) },
                new TextBlock { Text = h.Detail, FontSize = 11, Foreground = new SolidColorBrush(StatChip.Muted), TextWrapping = TextWrapping.Wrap, MaxWidth = 220, IsVisible = h.Detail.Length > 0 },
            },
        };
        // A grid, not a horizontal stack, so the text is measured against the room left and wraps in a narrow panel.
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        if (h.Player != null) row.Children.Add(new Avatar { Player = h.Player, Team = h.Team, Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        Child = row;
    }
}

/// <summary>A wrapping row of <see cref="HighlightCard"/>s.</summary>
public sealed class HighlightStrip : ItemsControl
{
    protected override Type StyleKeyOverride => typeof(ItemsControl);

    public HighlightStrip()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal });
        ItemTemplate = new FuncDataTemplate<Highlight>((h, _) => h == null ? new Panel() : new HighlightCard(h));
    }
}

/// <summary>A two-sided bar comparing the teams on one stat: away fills from the left, home from the right.</summary>
public sealed class CompareBar : Control
{
    public static readonly StyledProperty<double> AwayProperty = AvaloniaProperty.Register<CompareBar, double>(nameof(Away));
    public static readonly StyledProperty<double> HomeProperty = AvaloniaProperty.Register<CompareBar, double>(nameof(Home));
    public static readonly StyledProperty<Color> AwayColorProperty = AvaloniaProperty.Register<CompareBar, Color>(nameof(AwayColor), Color.Parse("#4F8EF7"));
    public static readonly StyledProperty<Color> HomeColorProperty = AvaloniaProperty.Register<CompareBar, Color>(nameof(HomeColor), Color.Parse("#F0A33A"));
    public double Away { get => GetValue(AwayProperty); set => SetValue(AwayProperty, value); }
    public double Home { get => GetValue(HomeProperty); set => SetValue(HomeProperty, value); }
    public Color AwayColor { get => GetValue(AwayColorProperty); set => SetValue(AwayColorProperty, value); }
    public Color HomeColor { get => GetValue(HomeColorProperty); set => SetValue(HomeColorProperty, value); }

    static CompareBar()
    {
        AffectsRender<CompareBar>(AwayProperty, HomeProperty, AwayColorProperty, HomeColorProperty);
        HeightProperty.OverrideDefaultValue<CompareBar>(6);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#2A323D")), null, new Rect(0, 0, w, h), h / 2, h / 2);
        double total = Away + Home;
        if (total <= 0) return;
        double split = w * Away / total;
        ctx.DrawRectangle(new SolidColorBrush(AwayColor), null, new Rect(0, 0, Math.Max(0, split - 1), h), h / 2, h / 2);
        ctx.DrawRectangle(new SolidColorBrush(HomeColor), null, new Rect(split + 1, 0, Math.Max(0, w - split - 1), h), h / 2, h / 2);
    }
}
