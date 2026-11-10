using Avalonia;
using Avalonia.Media;
using Hardwood.Core.Model;

namespace Hardwood.Desktop;

/// <summary>
/// The accent colour follows the team you manage (decision #68): whichever of its two colours reads better on the dark
/// background, lightened if both are too dark to see. Back on the main menu it returns to Hardwood orange.
/// </summary>
public static class ThemeService
{
    public static readonly Color DefaultAccent = Color.Parse("#F0A33A");
    public static Color Accent { get; private set; } = DefaultAccent;

    private static double Luminance(Color c)
    {
        static double Lin(byte v) { double s = v / 255.0; return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4); }
        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    private static Color Lighten(Color c, double t) =>
        Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));

    /// <summary>The team colour to use as an accent on the dark theme.</summary>
    public static Color AccentFor(Team team)
    {
        var a = Color.Parse(team.PrimaryColor); var b = Color.Parse(team.SecondaryColor);
        // Prefer a colourful, visible colour: skip near-white and near-grey; then lighten until readable.
        double Sat(Color c) { double mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B)); return mx == 0 ? 0 : (mx - mn) / mx; }
        var pick = new[] { a, b }.OrderByDescending(c => (Sat(c) > 0.25 ? 1 : 0) + Math.Min(Luminance(c), 0.5)).First();
        for (int i = 0; i < 10 && Luminance(pick) < 0.18; i++) pick = Lighten(pick, 0.15);
        return pick;
    }

    public static void Apply(Team? team) => Set(team == null ? DefaultAccent : AccentFor(team));

    public static void Set(Color c)
    {
        Accent = c;
        if (Application.Current is { } app)
        {
            app.Resources["HwAccent"] = c;
            app.Resources["HwAccentBrush"] = new SolidColorBrush(c);
        }
    }
}
