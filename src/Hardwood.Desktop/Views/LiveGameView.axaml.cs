using Avalonia.Controls;

namespace Hardwood.Desktop.Views;

public partial class LiveGameView : UserControl
{
    /// <summary>Below this width the scoreboard drops the club badges and uses a smaller score, so names and scores don't collide.</summary>
    public const double CompactScoreWidth = 640;

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
    }
}
