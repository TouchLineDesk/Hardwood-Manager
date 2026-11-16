using Avalonia.Controls;
using Avalonia.Interactivity;
using Hardwood.Desktop.ViewModels;

namespace Hardwood.Desktop.Views;

public partial class CollegeView : UserControl
{
    /// <summary>Two star cards side by side: the board's width before the recruiting panel drops underneath.</summary>
    public const double TrailCardsNeed = 560;

    public CollegeView()
    {
        InitializeComponent();
        // The recruiting panel drops under the board when the board needs the width (nothing scrolled out of view).
        Controls.Reflow.Watch(RecruitRowGrid, RecruitBoard, RecruitPanel, gap: 10, leftNeed: () => Controls.Reflow.TableWidth(RecruitGrid));
        Controls.Reflow.Watch(TrailRowGrid, TrailBoard, TrailPanel, gap: 10,
            leftNeed: () => TrailGrid.IsVisible ? Controls.Reflow.TableWidth(TrailGrid) : TrailCardsNeed);
    }

    // Run 68: a star card press picks the prospect, as a row click in the table does.
    private void OnHighSchoolClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CollegeViewModel vm && sender is Button { DataContext: HighSchoolRow row }) vm.PickHighSchool(row);
    }

    private void OnTrailClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CollegeViewModel vm && sender is Button { DataContext: TrailRow row }) vm.PickTrail(row);
    }
}
