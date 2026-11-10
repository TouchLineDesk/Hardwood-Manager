using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Look-and-feel pass (decisions #66-69): icons, team-colour accent, logo.</summary>
[Collection(SharedUiState.Name)]
public class LookAndFeelTests
{
    [AvaloniaFact]
    public void Every_icon_parses_and_fits_its_24px_grid()
    {
        foreach (var (name, _) in IconData.Paths)
        {
            var g = Icon.GeometryFor(name);
            Assert.NotNull(g);
            var b = g!.Bounds;
            Assert.True(b.Left >= -0.5 && b.Top >= -0.5 && b.Right <= 24.5 && b.Bottom <= 24.5, $"{name}: {b}");
        }
    }

    [AvaloniaFact]
    public void Team_accents_are_readable_on_the_dark_theme()
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        foreach (var t in league.Teams)
        {
            var c = ThemeService.AccentFor(t);
            double lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
            Assert.True(lum > 0.25, $"{t.FullName}: {c}");
        }
        ThemeService.Apply(league.Teams[0]);
        Assert.Equal(ThemeService.AccentFor(league.Teams[0]), ThemeService.Accent);
        ThemeService.Apply(null);
        Assert.Equal(ThemeService.DefaultAccent, ThemeService.Accent);
    }
}
