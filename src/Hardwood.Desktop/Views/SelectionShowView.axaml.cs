using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hardwood.Desktop.Graphics;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

/// <summary>The Selection Show's stage (B1, run 139): places the screen and the speech bubble on the rendered set.</summary>
public partial class SelectionShowView : UserControl
{
    private readonly BroadcastSet _set = BroadcastArt.Selection;
    /// <summary>The part of the 1920 × 1080 frame on the stage: the screen, the desk and the presenters, not the empty wings.</summary>
    private static readonly Rect Crop = new(240, 130, 1440, 810);
    private Point At(Point p) => new(p.X - Crop.X, p.Y - Crop.Y);

    public SelectionShowView()
    {
        InitializeComponent();
        Backdrop.Source = _set.Frame;
        Canvas.SetLeft(Backdrop, -Crop.X); Canvas.SetTop(Backdrop, -Crop.Y);
        var r = _set.Screen.Translate(new Vector(-Crop.X, -Crop.Y));
        Canvas.SetLeft(ScreenPanel, r.X); Canvas.SetTop(ScreenPanel, r.Y);
        ScreenPanel.Width = r.Width; ScreenPanel.Height = r.Height;
        DataContextChanged += (_, _) => Hook();
        Bubble.SizeChanged += (_, _) => PlaceBubble();
    }

    private CollegeViewModel? _vm;

    private void Hook()
    {
        if (_vm != null) _vm.PropertyChanged -= OnVm;
        _vm = DataContext as CollegeViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVm;
        PlaceBubble();
    }

    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CollegeViewModel.BubbleLeft) or nameof(CollegeViewModel.BubbleText)) PlaceBubble();
    }

    /// <summary>The bubble sits beside the speaker, over the desk, with its tail pointing at his head.</summary>
    private void PlaceBubble()
    {
        bool left = _vm?.BubbleLeft ?? true;
        var head = At(left ? _set.HostHead : _set.AnalystHead);
        double w = Bubble.Width, h = Math.Max(Bubble.Bounds.Height, 120);
        double x = left ? head.X - 60 - w : head.X + 60;
        x = Math.Clamp(x, 16, Crop.Width - 16 - w);
        double y = Math.Min(head.Y + 30, Crop.Height - 16 - h);
        Canvas.SetLeft(Bubble, x); Canvas.SetTop(Bubble, y);
        // The tail: from the bubble's edge nearest the speaker toward his head.
        double ex = left ? x + w - 2 : x + 2, ey = y + 34;
        var tip = new Point(head.X + (left ? -22 : 22), head.Y + 24);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        BubbleTail.Data = Geometry.Parse(string.Format(ci, "M {0},{1} L {2},{3} L {0},{4} Z", ex, ey, tip.X, tip.Y, ey + 40));
    }
}
