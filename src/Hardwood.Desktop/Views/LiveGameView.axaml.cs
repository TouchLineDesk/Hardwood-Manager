using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class LiveGameView : UserControl
{
    /// <summary>Below this width the scoreboard drops the club badges and uses a smaller score, so names and scores don't collide.</summary>
    public const double CompactScoreWidth = 640;

    /// <summary>The court's height when the big view is off, and the least it ever gets (run 80).</summary>
    public const double SmallCourtHeight = 300;

    /// <summary>The big view (run 85) never leaves the play by play / box score panel under the court less than this.</summary>
    public const double MinLogHeight = 180;

    /// <summary>From this page width the big view also widens the court's column (2:1 instead of 3:2), so wide windows
    /// get a bigger court, not just a taller cap; the side tabs keep at least <see cref="MinSideWidth"/>.</summary>
    public const double WideViewWidth = 900;
    public const double MinSideWidth = 330;

    /// <summary>H57 (run 86): the break card takes the court's place in its panel, up to this height (it scrolls past it).</summary>
    public const double BreakCardMax = 280;

    private LiveGameViewModel? _vm;
    private bool _fitPending = true;

    public LiveGameView()
    {
        InitializeComponent();
        // Nothing covered up (roadmap 1): the buttons drop under the score, and the quarter table under the chart, when space runs out.
        Controls.Reflow.Watch(ScoreRow, ScoreLine, ScoreButtons, leftNeed: () => 660);
        Controls.Reflow.Watch(FlowRow, FlowChart, FlowSplits, leftNeed: () => 360);
        ScoreLine.PropertyChanged += (_, e) =>
        {
            if (e.Property != BoundsProperty) return;
            bool compact = ScoreLine.Bounds.Width < CompactScoreWidth;
            if (AwayLogo.IsVisible != compact) return;
            AwayLogo.IsVisible = HomeLogo.IsVisible = !compact;
            AwayScoreText.FontSize = HomeScoreText.FontSize = compact ? 34 : 42;
        };
        // The big view (tracker slice 3): fit the court to the column's height whenever the room changes.
        RootGrid.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) FitColumns(); };
        // The cap is worked out again only when the room changes (the column, the big view, the court tab), not when the
        // caption or tab row wraps to another line, so the court doesn't pulse from play to play (code review, run 85).
        LeftCol.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) { _fitPending = true; FitCourt(); } };
        CourtPanel.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty && _fitPending) FitCourt(); };
        // B2a (run 140): the arena's caption bar is drawn at the arena's scale; when the arena is small the caption moves
        // above it, at full size, so the play stays readable.
        ArenaBox.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) PlaceCaption(); };
    }

    /// <summary>Below this width the arena's own caption bar would be too small to read (its text is drawn at 34 px on a 1600 px stage).</summary>
    public const double ArenaCaptionMinWidth = 640;

    private void PlaceCaption()
    {
        bool arena = _vm?.InArena == true;
        bool small = arena && ArenaBox.Bounds.Width > 0 && ArenaBox.Bounds.Width < ArenaCaptionMinWidth;
        ArenaCaption.IsVisible = arena && !small;
        CaptionPanel.IsVisible = !arena || small;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm != null) _vm.PropertyChanged -= OnVmChanged;
        _vm = DataContext as LiveGameViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmChanged;
        FitColumns();
        FitCourt();
        PlaceCaption();
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LiveGameViewModel.BigCourt)) FitColumns();
        if (e.PropertyName is nameof(LiveGameViewModel.BigCourt) or nameof(LiveGameViewModel.CourtShown))
        {
            // After the layout pass, when the court's bounds are current again.
            _fitPending = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(FitCourt, Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    /// <summary>True when the big view widens the court's column at this page width.</summary>
    public static bool WidensColumn(bool big, double pageWidth) => big && pageWidth >= WideViewWidth;

    private void FitColumns()
    {
        bool wide = WidensColumn(_vm?.BigCourt == true, RootGrid.Bounds.Width);
        var left = new GridLength(wide ? 2 : 3, GridUnitType.Star);
        var right = new GridLength(wide ? 1 : 2, GridUnitType.Star);
        var cols = RootGrid.ColumnDefinitions;
        if (cols[0].Width != left) cols[0].Width = left;
        if (cols[1].Width != right) cols[1].Width = right;
        double minSide = wide ? MinSideWidth : 0;
        if (cols[1].MinWidth != minSide) cols[1].MinWidth = minSide;
    }

    /// <summary>The court's height cap for a column of this height: the column less the panel's own rows (tabs,
    /// caption, legend, padding), its margin and the play by play's least share; never under the small court.
    /// The court's width still limits it (it keeps 94:50).</summary>
    public static double BigCourtCap(double columnHeight, double panelOverhead, double panelMargin) =>
        Math.Max(SmallCourtHeight, Math.Floor(columnHeight - panelOverhead - panelMargin - MinLogHeight));

    private void FitCourt()
    {
        double cap = SmallCourtHeight;
        // B2a (run 140): a college game's arena takes the same room as the flat court.
        Control court = _vm?.InArena == true ? ArenaBox : Court;
        if (_vm is { BigCourt: true, CourtShown: true } && LeftCol.Bounds.Height > 0 && court.Bounds.Height > 0)
        {
            double overhead = CourtPanel.Bounds.Height - court.Bounds.Height;
            cap = BigCourtCap(LeftCol.Bounds.Height, overhead, CourtPanel.Margin.Bottom);
            _fitPending = false;
        }
        else if (_vm is { BigCourt: true }) return;   // the court is hidden or not laid out yet: keep its size for now
        else _fitPending = false;
        if (Math.Abs(court.MaxHeight - cap) > 1) court.MaxHeight = cap;
    }
}
