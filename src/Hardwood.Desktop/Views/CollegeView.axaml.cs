using Avalonia.Controls;

namespace Hardwood.Desktop.Views;

public partial class CollegeView : UserControl
{
    public CollegeView()
    {
        InitializeComponent();
        // The recruiting panel drops under the board when the board needs the width (nothing scrolled out of view).
        Controls.Reflow.Watch(RecruitRowGrid, RecruitBoard, RecruitPanel, gap: 10, leftNeed: () => Controls.Reflow.TableWidth(RecruitGrid));
    }
}
