using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// One broadcast-style stat: a small caps label, a big number and an optional one-line note. Tone colours the number
/// and the left edge: "good" (green), "bad" (red), "warn" (amber), "info" (blue), "team" (the chip's Accent) or "" (plain).
/// </summary>
public sealed record Chip(string Label, string Value, string Sub = "", string Tone = "");

/// <summary>Roadmap 1: a stat chip (label, big number, note) used across Player, Team and Board pages instead of a run-on sentence.</summary>
public sealed class StatChip : Border
{
    public static readonly Color Good = Color.Parse("#3FB950"), Bad = Color.Parse("#F85149"), Warn = Color.Parse("#D29922"),
        Info = Color.Parse("#58A6FF"), Plain = Color.Parse("#3A4654"), Text = Color.Parse("#E6EAF0"), Muted = Color.Parse("#8B96A5");

    private readonly TextBlock _label = new() { FontSize = 10.5, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.8, Foreground = new SolidColorBrush(Muted) };
    private readonly TextBlock _value = new() { FontSize = 22, FontWeight = FontWeight.Bold, Foreground = new SolidColorBrush(Text) };
    private readonly TextBlock _sub = new() { FontSize = 11, Foreground = new SolidColorBrush(Muted), TextWrapping = TextWrapping.Wrap, MaxWidth = 170 };

    public StatChip(Chip chip, Color accent)
    {
        Background = new SolidColorBrush(Color.Parse("#1C232C"));
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(12, 7, 14, 8);
        Margin = new Thickness(0, 0, 8, 8);
        MinWidth = 92;
        BorderThickness = new Thickness(3, 0, 0, 0);
        var edge = ToneColor(chip.Tone, accent);
        BorderBrush = new SolidColorBrush(edge);
        _label.Text = chip.Label.ToUpperInvariant();
        _value.Text = chip.Value;
        if (chip.Tone is "good" or "bad" or "warn") _value.Foreground = new SolidColorBrush(edge);
        _sub.Text = chip.Sub;
        _sub.IsVisible = chip.Sub.Length > 0;
        Child = new StackPanel { Spacing = 0, Children = { _label, _value, _sub } };
    }

    public static Color ToneColor(string tone, Color accent) => tone switch
    {
        "good" => Good, "bad" => Bad, "warn" => Warn, "info" => Info, "team" => accent, _ => Plain,
    };
}

/// <summary>A wrapping row of <see cref="StatChip"/>s: on a narrow window or a big text size the chips flow onto more lines rather than being cut.</summary>
public sealed class StatStrip : ItemsControl
{
    protected override Type StyleKeyOverride => typeof(ItemsControl);

    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<StatStrip, Color>(nameof(Accent), Color.Parse("#F0A33A"));
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    public StatStrip()
    {
        ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal });
        ItemTemplate = new FuncDataTemplate<Chip>((c, _) => c == null ? new Panel() : new StatChip(c, Accent));
    }
}
