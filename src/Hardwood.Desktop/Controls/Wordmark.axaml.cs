using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Hardwood.Desktop.Controls;

/// <summary>The Hardwood Manager wordmark, scalable by <see cref="Size"/> (the height of the "Hardwood" letters).</summary>
public partial class Wordmark : UserControl
{
    public static readonly StyledProperty<double> SizeProperty = AvaloniaProperty.Register<Wordmark, double>(nameof(Size), 72);
    public double Size { get => GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    public Wordmark()
    {
        AvaloniaXamlLoader.Load(this);
        Apply();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SizeProperty) Apply();
    }

    private void Apply()
    {
        double s = Size;
        if (this.FindControl<TextBlock>("Left") is { } l) l.FontSize = s;
        if (this.FindControl<TextBlock>("Right") is { } r) r.FontSize = s;
        if (this.FindControl<BallMark>("Ball") is { } b) { b.Width = b.Height = s * 0.68; b.Margin = new Thickness(s * 0.02, 0, s * 0.02, s * 0.14); }
        if (this.FindControl<Border>("Rule") is { } rule) { rule.Height = Math.Max(1.5, s * 0.035); rule.Margin = new Thickness(0, s * 0.1, 0, s * 0.08); }
        if (this.FindControl<TextBlock>("Sub") is { } sub) { sub.FontSize = s * 0.3; sub.LetterSpacing = s * 0.16; }
    }
}
