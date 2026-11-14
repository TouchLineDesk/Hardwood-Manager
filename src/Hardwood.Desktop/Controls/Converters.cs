using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

public static class Converters
{
    /// <summary>Bold when true (your own team in a table).</summary>
    public static readonly IValueConverter BoldIf = new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.Bold : FontWeight.Normal);

    /// <summary>The team accent colour when true (a card that involves your team); the normal border otherwise.</summary>
    public static readonly IValueConverter AccentIfTrue = new FuncValueConverter<bool, IBrush>(b =>
        b ? new SolidColorBrush(Hardwood.Desktop.ThemeService.Accent) : new SolidColorBrush(Color.Parse("#2A323D")));

    /// <summary>A club's own accent colour (the head-to-head card's edge, #529); the normal border with no club.</summary>
    public static readonly IValueConverter TeamAccentBrush = new FuncValueConverter<Hardwood.Core.Model.Team?, IBrush>(t =>
        t != null ? new SolidColorBrush(Hardwood.Desktop.ThemeService.AccentFor(t)) : new SolidColorBrush(Color.Parse("#2A323D")));
}
