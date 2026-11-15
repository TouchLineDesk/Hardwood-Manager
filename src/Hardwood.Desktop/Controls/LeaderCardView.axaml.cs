using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Hardwood.Desktop.Controls;

/// <summary>One stat's leader card (run 64), shared by League Leaders and Leagues → Stats.</summary>
public partial class LeaderCardView : UserControl
{
    public LeaderCardView() => AvaloniaXamlLoader.Load(this);
}
