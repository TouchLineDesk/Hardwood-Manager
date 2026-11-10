using Avalonia;
using Avalonia.Controls;

namespace Hardwood.Desktop.Views;

public partial class HomeView : UserControl
{
    /// <summary>Below this width the three panels can't show full team names side by side.</summary>
    public const double ThreeAcrossWidth = 760;
    private bool? _threeAcross;

    public HomeView()
    {
        InitializeComponent();
        Controls.Reflow.Watch(Hero, HeroLeft, HeroRight);
        Panels.PropertyChanged += (_, e) => { if (e.Property == BoundsProperty) ArrangePanels(Panels.Bounds.Width); };
    }

    private void ArrangePanels(double width)
    {
        bool across = width >= ThreeAcrossWidth;
        if (_threeAcross == across) return;
        _threeAcross = across;
        Panels.ColumnDefinitions = across ? new ColumnDefinitions("3*,2*,2*") : new ColumnDefinitions("*,*");
        Grid.SetColumnSpan(TablePanel, across ? 1 : 2);
        TablePanel.Margin = across ? new Thickness(0, 0, 10, 0) : new Thickness(0, 0, 0, 10);
        Grid.SetRow(PulsePanel, across ? 0 : 1); Grid.SetColumn(PulsePanel, across ? 1 : 0);
        Grid.SetRow(LatestPanel, across ? 0 : 1); Grid.SetColumn(LatestPanel, across ? 2 : 1);
    }
}
