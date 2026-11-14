using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Desktop.Controls;
using Xunit;

namespace Hardwood.Tests;

[Collection(SharedUiState.Name)]
public class PortraitRenderTests
{
    [AvaloniaFact]
    public void Card_shows_rendered_portrait_when_file_exists_and_avatar_otherwise()
    {
        var league = new WorldGenerator(new Rng(2026)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var team = league.Teams[0];
        var withPortrait = team.Roster[0];
        var without = team.Roster[1];

        var dir = Path.Combine(Path.GetTempPath(), $"hw-portraits-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var save = Path.Combine(dir, "career.hwsave");
        Assert.Equal(Path.Combine(dir, "career-portraits"), Portraits.FolderFor(save));
        Directory.CreateDirectory(Portraits.FolderFor(save));
        try
        {
            // A solid magenta 256x256 "portrait", so it is easy to find in the rendered card.
            var file = Path.Combine(Portraits.FolderFor(save), withPortrait.Id + ".png");
            using (var bmp = new RenderTargetBitmap(new PixelSize(256, 256)))
            {
                using (var ctx = bmp.CreateDrawingContext())
                    ctx.DrawRectangle(new SolidColorBrush(Color.Parse("#FF00FF")), null, new Rect(0, 0, 256, 256));
                bmp.Save(file);
            }
            Portraits.UseSave(save);
            Assert.NotNull(Portraits.Get(withPortrait.Id));
            Assert.Null(Portraits.Get(without.Id));

            var a = new PlayerCard { Player = withPortrait, Team = team };
            var b = new PlayerCard { Player = without, Team = team };
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { a, b } };
            var window = new Window { Content = row, Width = 380, Height = 290, Background = Brushes.Black };
            window.Show();
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame()!;
            var shots = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
            if (!string.IsNullOrEmpty(shots)) frame.Save(Path.Combine(shots, "portrait-card.png"));

            // The photo's centre on each collector card (see PlayerCard.DrawFront: bottom-centred in the top 58%).
            Assert.True(IsMagenta(frame, 95, 100));
            Assert.False(IsMagenta(frame, 190 + 95, 100));
        }
        finally
        {
            Portraits.UseSave(null);
            Directory.Delete(dir, recursive: true);
        }
    }

    private static bool IsMagenta(WriteableBitmap frame, int x, int y)
    {
        using var fb = frame.Lock();
        int px = Marshal.ReadInt32(fb.Address, y * fb.RowBytes + x * 4);
        byte c0 = (byte)px, c1 = (byte)(px >> 8), c2 = (byte)(px >> 16);
        return c0 > 200 && c1 < 60 && c2 > 200; // BGRA or RGBA: magenta has both outer channels high, green low
    }
}
