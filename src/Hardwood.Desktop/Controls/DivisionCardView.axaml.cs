using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Hardwood.Desktop.Controls;

/// <summary>One division card (rank chip in its zone colour, logo, team, W-L, Pct, GB and a last column), shared by Standings and Leagues.</summary>
public partial class DivisionCardView : UserControl
{
    public DivisionCardView() => AvaloniaXamlLoader.Load(this);
}
