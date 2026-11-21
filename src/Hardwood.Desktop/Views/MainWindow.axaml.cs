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
        _topReflow = Controls.Reflow.Watch(TopBar, TopLeft, TopRight);
        PageScroll.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty) FitPage();
        };
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
            _vm = DataContext as ViewModels.MainViewModel;
            if (_vm != null) _vm.PropertyChanged += OnVmChanged;
            FitLive();
        };
    }

    private ViewModels.MainViewModel? _vm;
    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.MainViewModel.InLiveGame)) FitLive();
    }

    /// <summary>M1 (run 148): a live game takes the whole page: the sidebar folds away (it is locked during a game anyway)
    /// and the page loses its padding, so the court fills the window as in Football Manager's match view.</summary>
    public const double LivePageMinHeight = 540;
    private bool _live;
    private void FitLive()
    {
        bool live = _vm?.InLiveGame == true;
        if (live == _live && Sidebar.IsVisible == !live) return;
        _live = live;
        Sidebar.IsVisible = !live;
        ShellGrid.ColumnDefinitions[0].Width = new GridLength(live ? 0 : 210);
        PageHost.Padding = new Thickness(live ? 0 : 16);
        FitPage();
    }

    private readonly Controls.Reflow _topReflow;
    /// <summary>Layout check (H97): whether the top bar's buttons have dropped to a second row.</summary>
    public bool TopBarStacked => _topReflow.Stacked;
    /// <summary>Layout check (H97): the widths the top bar's two halves ask for, and the bar's own width.</summary>
    public (double left, double right, double bar) TopBarWidths => (TopLeft.DesiredSize.Width, TopRight.DesiredSize.Width, TopBar.Bounds.Width);

    /// <summary>The shortest a page gets (in layout units, before the text-size scale); below this it scrolls.</summary>
    public const double MinPageHeight = 760;

    private void FitPage()
    {
        double h = Math.Max(PageScroll.Bounds.Height, _live ? LivePageMinHeight : MinPageHeight);
        if (Math.Abs(PageHost.Height - h) > 0.5 || double.IsNaN(PageHost.Height)) PageHost.Height = h;
    }

    /// <summary>The text and interface size from Settings, applied to everything in the window.</summary>
    public void ApplyScale(double scale)
    {
        ScaleRoot.LayoutTransform = UiScale.Transform(scale);
        bool big = UiScale.Clamp(scale) >= BigTextScale;
        ScaleRoot.Classes.Set("bigText", big);   // H60: smaller tab headers at big text
        // H97 (run 116): the top bar compacts at big text before it wraps to a second row: an icon-only Assistant toggle
        // (its tooltip stays), "Watch", no ball mark and tighter spacing. Set here rather than by descendant styles, which
        // would attach a class watcher to every control in the window.
        TopMark.IsVisible = !big;
        WatchLong.IsVisible = !big; WatchShort.IsVisible = big;
        AssistantLabel.IsVisible = !big;
        TopLeft.Spacing = big ? 12 : 18;
        TopRight.Spacing = big ? 6 : 8;
        ContinueButton.MinWidth = big ? 110 : 150;
        // H97 (run 116): a text size changed during a live game folds or opens the assistant then. (Hooked here, not in the
        // view model, so the static event never keeps a closed career's world alive.)
        if (DataContext is ViewModels.MainViewModel vm) vm.Guide.LiveGame(vm.InLiveGame, scale);
    }

    /// <summary>From this text size the tab headers shrink so they stay on one row (H60).</summary>
    public const double BigTextScale = 1.15;

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

    /// <summary>Closing the window saves the career first (D74 B, #635).</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        (DataContext as ViewModels.MainViewModel)?.SaveOnLeave();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        UiScale.Changed -= ApplyScale;
        base.OnClosed(e);
    }
}
