using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

/// <summary>Run 73 (review #13 rank 10): a still half court, the same drawing as Tactics' play diagram, for the
/// Rotation page's depth chart. The five starters' cards sit on it at their spots.</summary>
public sealed class HalfCourt : Control
{
    public static readonly StyledProperty<Color> AccentProperty = AvaloniaProperty.Register<HalfCourt, Color>(nameof(Accent), Color.Parse("#F0A33A"));
    public Color Accent { get => GetValue(AccentProperty); set => SetValue(AccentProperty, value); }

    static HalfCourt() => AffectsRender<HalfCourt>(AccentProperty);

    public override void Render(DrawingContext ctx) => PlayDiagram.DrawCourt(ctx, Bounds.Size, Accent);
}
