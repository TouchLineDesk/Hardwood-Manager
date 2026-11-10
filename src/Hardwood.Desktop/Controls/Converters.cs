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
}
