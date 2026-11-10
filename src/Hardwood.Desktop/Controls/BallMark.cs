using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

/// <summary>The ball from the Hardwood Manager wordmark (decision #67, option A): an orange ball with dark seams.</summary>
public sealed class BallMark : Control
{
    public static readonly StyledProperty<IBrush?> FillProperty = AvaloniaProperty.Register<BallMark, IBrush?>(nameof(Fill));
    public static readonly StyledProperty<IBrush?> SeamProperty = AvaloniaProperty.Register<BallMark, IBrush?>(nameof(Seam));
    public IBrush? Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush? Seam { get => GetValue(SeamProperty); set => SetValue(SeamProperty, value); }

    static BallMark() { AffectsRender<BallMark>(FillProperty, SeamProperty); }

    private static readonly Geometry Seams = Geometry.Parse("M-31,0 H31 M0,-31 V31 M-22,-22 C-8,-8 -8,8 -22,22 M22,-22 C8,-8 8,8 22,22");

    public override void Render(DrawingContext ctx)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        double s = size / 62.0;
        var fill = Fill ?? new SolidColorBrush(Color.Parse("#F0A33A"));
        var seam = Seam ?? new SolidColorBrush(Color.Parse("#0F1216"));
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        ctx.DrawEllipse(fill, null, center, 30 * s, 30 * s);
        using (ctx.PushGeometryClip(new EllipseGeometry(new Rect(center.X - 30 * s, center.Y - 30 * s, 60 * s, 60 * s))))
        using (ctx.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(center.X, center.Y)))
            ctx.DrawGeometry(null, new Pen(seam, 3.2), Seams);
    }
}
