using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Hardwood.Desktop.Controls;

public static class Converters
{
    /// <summary>Bold when true (your own team in a table).</summary>
    public static readonly IValueConverter BoldIf = new FuncValueConverter<bool, FontWeight>(b => b ? FontWeight.Bold : FontWeight.Normal);
}
