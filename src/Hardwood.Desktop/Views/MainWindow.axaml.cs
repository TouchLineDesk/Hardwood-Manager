using Avalonia;
using Avalonia.Controls;

namespace Hardwood.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplyScale(UiScale.Current);
        UiScale.Changed += ApplyScale;
        // The top bar's buttons drop to a second row when they'd otherwise cover the team name (roadmap 1).
        Controls.Reflow.Watch(TopBar, TopLeft, TopRight);
    }

    /// <summary>The text and interface size from Settings, applied to everything in the window.</summary>
    public void ApplyScale(double scale) => ScaleRoot.LayoutTransform = UiScale.Transform(scale);

    /// <summary>Layout check: how many pixels the two halves of the top bar overlap (0 when they don't).</summary>
    public double TopBarOverlap()
    {
        if (!TopLeft.IsEffectivelyVisible || !TopRight.IsEffectivelyVisible) return 0;
        var l = TopLeft.TranslatePoint(new Point(TopLeft.Bounds.Width, 0), this);
        var r = TopRight.TranslatePoint(new Point(0, 0), this);
        if (l == null || r == null) return 0;
        // Stacked on two rows: no overlap possible.
        if (r.Value.Y >= TopLeft.TranslatePoint(new Point(0, TopLeft.Bounds.Height), this)!.Value.Y) return 0;
        return Math.Max(0, l.Value.X - r.Value.X);
    }

    protected override void OnClosed(EventArgs e)
    {
        UiScale.Changed -= ApplyScale;
        base.OnClosed(e);
    }
}
