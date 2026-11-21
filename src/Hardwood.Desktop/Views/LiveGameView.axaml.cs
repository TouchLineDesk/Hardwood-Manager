using Avalonia.Controls;

namespace Hardwood.Desktop.Views;

/// <summary>
/// The live game (M1 of the 2D match engine, run 148): the court fills the page, with the score bar, the controls, the
/// speeds, the drawers and the commentary bar over it. The court draws itself to fit (MatchCourt.Fit), so the page needs
/// no fitting code of its own.
/// </summary>
public partial class LiveGameView : UserControl
{
    /// <summary>The corner panels' width (the controls at the top left, the speeds at the top right).</summary>
    public const double CornerWidth = 236;
    /// <summary>The drawer on the right (box score, play by play, tactics, subs, shots, stats, game flow).</summary>
    public const double DrawerWidth = 440;
    /// <summary>The break card and the HSN desk over the court.</summary>
    public const double BreakWidth = 900;
    /// <summary>Each side's block in the score bar.</summary>
    public const double SideBlockWidth = 150;
    /// <summary>The break card scrolls past this height rather than covering the score bar.</summary>
    public const double BreakCardMax = 420;

    /// <summary>The least width the break overlay takes beside the controls (the desk's stage reads from about here).</summary>
    public const double MinBreakWidth = 620;
    /// <summary>The status line at the right of the commentary bar.</summary>
    public const double StatusWidth = 300;
    /// <summary>Room the commentary bar keeps at the bottom.</summary>
    public const double BottomBar = 64;

    private ViewModels.LiveGameViewModel? _vm;

    public LiveGameView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Focus();
        RootGrid.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) FitSoon(); };
        foreach (var c in new Control[] { ScoreBar, LeftStack, CommentaryBar })
            c.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) FitSoon(); };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as ViewModels.LiveGameViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        Fit();
    }

    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ViewModels.LiveGameViewModel.ShowBreak) or nameof(ViewModels.LiveGameViewModel.DeskUp) or nameof(ViewModels.LiveGameViewModel.DrawerOpen)) Fit();
    }

    /// <summary>The score bar sits centred in the room right of the controls' column.</summary>
    public static readonly Avalonia.Thickness ScoreMargin = new(CornerWidth + 20, 10, 10, 0);

    /// <summary>Lays the overlays out so none covers another: the break overlay starts under the top row, the drawer under
    /// the score bar, both above the commentary bar; the HSN desk's width follows the height it has (its stage is 16:9).</summary>
    /// <summary>True when the controls' column, the score bar and a drawer can't sit side by side at this width: the
    /// score bar then takes the top row, the controls go under it (the speeds as one button), a drawer takes the width
    /// (the controls step aside while it's open), and the HSN tag leaves the commentary bar.</summary>
    public static bool Compact(double width, double scoreBar) => width < Math.Max(scoreBar, DrawerWidth) + CornerWidth + 40;

    private bool _fitQueued;
    /// <summary>Fit after the layout pass that moved something (changing margins inside a pass left stale measures).</summary>
    private void FitSoon()
    {
        if (_fitQueued) return;
        _fitQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => { _fitQueued = false; Fit(); }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void Fit()
    {
        double w = RootGrid.Bounds.Width, h = RootGrid.Bounds.Height;
        if (w <= 0 || h <= 0) return;
        bool compact = Compact(w, ScoreBar.DesiredSize.Width - ScoreBar.Margin.Left - ScoreBar.Margin.Right);   // DesiredSize includes the margin, which compact mode changes
        bool drawerOpen = _vm?.DrawerOpen == true;
        var scoreMargin = compact ? new Avalonia.Thickness(10, 10, 10, 0) : ScoreMargin;
        if (ScoreBar.Margin != scoreMargin) ScoreBar.Margin = scoreMargin;
        var leftMargin = compact ? new Avalonia.Thickness(10, ScoreBar.Bounds.Bottom + 8, 0, 0) : new Avalonia.Thickness(10, 10, 0, 0);
        if (LeftStack.Margin != leftMargin) LeftStack.Margin = leftMargin;
        if (SpeedBar.IsVisible == compact) SpeedBar.IsVisible = !compact;
        if (SpeedCycle.IsVisible != compact) SpeedCycle.IsVisible = compact;
        if (CommentaryTag.IsVisible == compact) CommentaryTag.IsVisible = !compact;
        // The HSN desk wants the width: it takes the controls' corner while it's up (it carries Play on / Continue).
        bool deskUp = _vm?.DeskUp == true;
        bool showLeft = !(compact && drawerOpen) && !deskUp;
        if (LeftStack.IsVisible != showLeft) LeftStack.IsVisible = showLeft;
        double bottom = Math.Max(BottomBar, CommentaryBar.Bounds.Height) + 8;
        // The break sits beside the controls' column when there's room for the desk there, else under it.
        bool beside = !deskUp && w - CornerWidth - 36 >= MinBreakWidth;
        double top = (beside || !showLeft ? ScoreBar.Bounds.Bottom : Math.Max(LeftStack.Bounds.Bottom, ScoreBar.Bounds.Bottom)) + 8;
        var overlay = new Avalonia.Thickness(beside ? CornerWidth + 20 : 8, top, 8, bottom);
        if (BreakOverlay.Margin != overlay) BreakOverlay.Margin = overlay;
        var drawer = new Avalonia.Thickness(compact ? 8 : 0, ScoreBar.Bounds.Bottom + 8, 8, bottom);
        if (Drawer.Margin != drawer) Drawer.Margin = drawer;
        double drawerWidth = compact ? double.NaN : DrawerWidth;
        if (!Drawer.Width.Equals(drawerWidth)) Drawer.Width = drawerWidth;
        var drawerAlign = compact ? Avalonia.Layout.HorizontalAlignment.Stretch : Avalonia.Layout.HorizontalAlignment.Right;
        if (Drawer.HorizontalAlignment != drawerAlign) Drawer.HorizontalAlignment = drawerAlign;
        var tabs = new Avalonia.Thickness(0, top, 8, bottom);
        if (DrawerTabs.Margin != tabs) DrawerTabs.Margin = tabs;
        double room = h - top - bottom - 12 - 40;   // the overlay's padding and the desk's title row
        double desk = Math.Min(DeskWidthFor(room), Math.Min(BreakWidth, w - overlay.Left - overlay.Right) - 16);
        if (Math.Abs(HalftimeSet.MaxWidth - desk) > 1) HalftimeSet.MaxWidth = desk;
        double card = Math.Max(120, h - top - bottom - 16);
        if (Math.Abs(BreakCard.MaxHeight - card) > 1) BreakCard.MaxHeight = card;
    }

    /// <summary>The desk's width for the height it may take: its 16:9 stage over about 130 px of controls (as run 143 sized it).</summary>
    public static double DeskWidthFor(double height) => Math.Clamp((height - 150) * 16 / 9, 300, 760);
}
