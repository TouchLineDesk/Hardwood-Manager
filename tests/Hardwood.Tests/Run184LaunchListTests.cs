using Avalonia.Media;
using Hardwood.Desktop;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 184, the launch list's leftovers. H193: the arena boards' lettering reads on its panel (Louisville's red trim on
/// its red boards was unreadable). H192 and H187 are checked in <see cref="Run79CollegeSummerTests"/>; H198 and H193's buttons
/// in the layout gate's shots.</summary>
public class Run184LaunchListTests
{
    [Theory]
    [InlineData("#AD0000", "#C8102E")]   // red on red (Louisville)
    [InlineData("#0B2240", "#13294B")]   // navy on navy
    [InlineData("#FFFFFF", "#F2F2F2")]   // white on white
    [InlineData("#FFCD00", "#F1B82D")]   // gold on gold
    public void Board_lettering_always_reads_on_its_panel(string paint, string trim)
    {
        var bg = Color.Parse(paint);
        var ink = ThemeService.ReadableOn(bg, Color.Parse(trim));
        Assert.True(ThemeService.Contrast(ink, bg) >= 3, $"{ink} on {paint}: {ThemeService.Contrast(ink, bg):F2}");
    }

    [Fact]
    public void A_trim_that_already_reads_is_kept()
    {
        var trim = Color.Parse("#FFFFFF");
        Assert.Equal(trim, ThemeService.ReadableOn(Color.Parse("#AD0000"), trim));
    }
}
