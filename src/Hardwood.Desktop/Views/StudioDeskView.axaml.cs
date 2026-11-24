using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Hardwood.Desktop.Graphics;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

/// <summary>The studio set for the pre-game show and the halftime desk (B4a, run 143): places the screen, the desk's
/// panel and the speech bubble on the rendered set, as the Selection Show does.</summary>
public partial class StudioDeskView : UserControl
{
    private readonly BroadcastSet _set = BroadcastArt.Selection;
    /// <summary>The part of the 1920 × 1080 frame on the stage (the same crop as the Selection Show).</summary>
    private static readonly Rect Crop = new(240, 130, 1440, 810);
    /// <summary>The desk's front panel in the frame (its white "SELECTION SHOW" plate).</summary>
    private static readonly Point PlateAt = new(657, 853);
    private Point At(Point p) => new(p.X - Crop.X, p.Y - Crop.Y);

    public StudioDeskView()
    {
        InitializeComponent();
        Backdrop.Source = _set.Frame;
        Canvas.SetLeft(Backdrop, -Crop.X); Canvas.SetTop(Backdrop, -Crop.Y);
        var r = _set.Screen.Translate(new Vector(-Crop.X, -Crop.Y));
        Canvas.SetLeft(ScreenPanel, r.X); Canvas.SetTop(ScreenPanel, r.Y);
        ScreenPanel.Width = r.Width; ScreenPanel.Height = r.Height;
        var p = At(PlateAt);
        Canvas.SetLeft(Plate, p.X); Canvas.SetTop(Plate, p.Y);
        DataContextChanged += (_, _) => Hook();
        Bubble.SizeChanged += (_, _) => PlaceBubble();
        DetachedFromVisualTree += (_, _) => _vm?.StopAutorun();
    }

    private DeskReelViewModel? _vm;

    /// <summary>H193 (run 184): whether the show's Next is the page's primary button. In the live game the desk's own header
    /// carries the way on (Play on / Continue), so there Next is an ordinary button and only one button reads primary.</summary>
    public static readonly StyledProperty<bool> NextIsPrimaryProperty =
        AvaloniaProperty.Register<StudioDeskView, bool>(nameof(NextIsPrimary), true);

    public bool NextIsPrimary { get => GetValue(NextIsPrimaryProperty); set => SetValue(NextIsPrimaryProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NextIsPrimaryProperty) NextButton?.Classes.Set("primary", change.GetNewValue<bool>());
    }

    private void Hook()
    {
        if (_vm != null) _vm.PropertyChanged -= OnVm;
        _vm = DataContext as DeskReelViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVm;
        PlaceBubble();
    }

    private void OnVm(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DeskReelViewModel.Index)) PlaceBubble();
    }

    /// <summary>Run 187: the host's bubble width and its gap from his head, in stage pixels.</summary>
    public const double HostBubbleWidth = 390, HostBubbleGap = 110;

    /// <summary>The bubble sits beside the speaker, over the desk, with its tail pointing at his head.</summary>
    private void PlaceBubble()
    {
        bool left = _vm?.BubbleLeft ?? true;
        var head = At(left ? _set.HostHead : _set.AnalystHead);
        // Run 187: on the host's side the bubble is narrower and further out, so it no longer covers his arm (the desk's
        // speech bubble sat over the host's gesturing arm at every size, since the stage is a fixed 1440 × 810 canvas).
        double want = left ? HostBubbleWidth : 440;
        if (Bubble.Width != want) Bubble.Width = want;
        double w = want, h = Math.Max(Bubble.Bounds.Height, 120);
        double x = left ? head.X - HostBubbleGap - w : head.X + 60;
        x = Math.Clamp(x, 16, Crop.Width - 16 - w);
        double y = Math.Min(head.Y + 30, Crop.Height - 16 - h);
        Canvas.SetLeft(Bubble, x); Canvas.SetTop(Bubble, y);
        double ex = left ? x + w - 2 : x + 2, ey = y + 34;
        var tip = new Point(head.X + (left ? -22 : 22), head.Y + 24);
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        BubbleTail.Data = Geometry.Parse(string.Format(ci, "M {0},{1} L {2},{3} L {0},{4} Z", ex, ey, tip.X, tip.Y, ey + 40));
    }
}
