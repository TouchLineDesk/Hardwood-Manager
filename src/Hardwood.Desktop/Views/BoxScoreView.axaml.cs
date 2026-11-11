using Avalonia.Controls;

namespace Hardwood.Desktop.Views;

public partial class BoxScoreView : UserControl
{
    public BoxScoreView()
    {
        InitializeComponent();
        // The quarter table and runs drop under the flow chart when there isn't room beside it.
        Controls.Reflow.Watch(FlowRow, FlowChart, FlowSide, leftNeed: () => 460);
    }
}
