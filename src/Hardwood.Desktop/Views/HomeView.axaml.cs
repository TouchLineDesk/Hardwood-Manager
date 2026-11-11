using Avalonia.Controls;

namespace Hardwood.Desktop.Views;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
        Controls.Reflow.Watch(Hero, HeroLeft, HeroRight);
    }
}
