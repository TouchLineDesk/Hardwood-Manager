using System.Text.Json;
using Hardwood.Core.Model;
using Hardwood.Core.Portraits;
using Hardwood.Desktop.Portraits;
using SkiaSharp;
using Xunit;

namespace Hardwood.Tests;

public class CardRendererTests
{
    private static SKBitmap Face()
    {
        // A plain opaque disc on transparency stands in for a face cut-out.
        var bmp = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        using var p = new SKPaint { Color = new SKColor(0xFF, 0x00, 0xFF), IsAntialias = true };
        c.DrawCircle(128, 128, 90, p);
        return bmp;
    }

    private static SKColor Pixel(SKImage img, int x, int y)
    {
        using var bmp = SKBitmap.FromImage(img);
        return bmp.GetPixel(x, y);
    }

    [Theory]
    [InlineData(50, 60)]     // Pearl / Pearl
    [InlineData(120, 150)]   // Gold / Amethyst
    [InlineData(160, 190)]   // Ruby / Diamond
    public void Renders_a_256_card_with_rounded_transparent_corners(int ca, int pa)
    {
        using var face = Face();
        var card = new PortraitCard(1, ca, pa, new CrestSpec("TST", "#1D428A", "#C8102E", 0), new DraftRecord(2026, 1, 4), "African/African1.png");
        using var img = CardRenderer.Render(card, face);
        Assert.Equal(256, img.Width);
        Assert.Equal(256, img.Height);
        Assert.Equal(0, Pixel(img, 0, 0).Alpha);          // corner cut by the 40px radius
        Assert.Equal(255, Pixel(img, 128, 128).Alpha);    // card body is opaque
        // Face sits right of centre: magenta dominates at its centre (face box centre is (341,303) at 512).
        var f = Pixel(img, 170, 151);
        Assert.True(f.Red > 150 && f.Blue > 150 && f.Green < 110, $"face pixel {f}");
    }

    [Fact]
    public void Body_colour_follows_current_tier()
    {
        using var face = Face();
        using var pearl = CardRenderer.Render(new PortraitCard(1, 50, 60, null, null, "x"), face);
        using var ruby = CardRenderer.Render(new PortraitCard(1, 170, 175, null, null, "x"), face);
        var a = Pixel(pearl, 60, 128); var b = Pixel(ruby, 60, 128);
        Assert.True(a.Red > 150 && a.Green > 150, $"pearl {a}");
        Assert.True(b.Red > b.Green + 40, $"ruby {b}");
    }

    /// <summary>
    /// Proof sheet support: with HW_PROOF_DIR set (containing cards.json and face.png), renders each card to
    /// &lt;dir&gt;/cs/&lt;id&gt;.png and &lt;id&gt;-512.png for a side-by-side check against the original kit.
    /// </summary>
    [Fact]
    public void Proof_cards_when_requested()
    {
        var dir = Environment.GetEnvironmentVariable("HW_PROOF_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        using var face = SKBitmap.Decode(Path.Combine(dir, "face.png"));
        Directory.CreateDirectory(Path.Combine(dir, "cs"));
        foreach (var c in JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "cards.json"))).RootElement.EnumerateArray())
        {
            CrestSpec? crest = c.GetProperty("crest").ValueKind == JsonValueKind.Null ? null :
                new CrestSpec(c.GetProperty("crest").GetProperty("abbr").GetString()!, c.GetProperty("crest").GetProperty("pc").GetString()!,
                              c.GetProperty("crest").GetProperty("sc").GetString()!, c.GetProperty("crest").GetProperty("style").GetInt32());
            DraftRecord? draft = c.GetProperty("draft").ValueKind == JsonValueKind.Null ? null :
                new DraftRecord(c.GetProperty("draft")[0].GetInt32(), c.GetProperty("draft")[1].GetInt32(), c.GetProperty("draft")[2].GetInt32());
            var card = new PortraitCard(c.GetProperty("id").GetInt32(), c.GetProperty("ca").GetInt32(), c.GetProperty("pa").GetInt32(), crest, draft, "proof");
            using var own = c.TryGetProperty("face", out var fp) ? SKBitmap.Decode(fp.GetString()) : null;
            foreach (var size in new[] { 256, 512 })
            {
                using var img = CardRenderer.Render(card, own ?? face, size);
                using var data = img.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(dir, "cs", size == 512 ? $"{card.PlayerId}-512.png" : $"{card.PlayerId}.png"), data.ToArray());
            }
        }
    }

    [Fact]
    public void Faces_are_assigned_once_by_ethnicity_and_never_shared()
    {
        var lib = FaceLibrary.FromList("C:/faces", new[]
        {
            "African/African1.png", "African/African2.png", "Asian/Asian1.png", "Central European/Central European5.png", "junk.txt",
        });
        Assert.Equal(4, lib.Count);
        var players = Enumerable.Range(1, 4).Select(i => new Player { Id = i, FirstName = "A", LastName = "B", Ethnicity = Ethnicity.African }).ToList();
        players[3].Ethnicity = Ethnicity.CentralEuropean;
        var retired = new Player { Id = 99, FirstName = "R", LastName = "R", Ethnicity = Ethnicity.African, FaceFile = "African/African1.png" };

        int n = lib.AssignMissing(players.Append(retired), players);
        Assert.Equal(2, n);                                           // one African face left + the Central European one
        Assert.Equal("Central European/Central European5.png", players[3].FaceFile);
        var african = players.Take(3).Where(p => p.FaceFile != null).ToList();
        Assert.Single(african);
        Assert.Equal("African/African2.png", african[0].FaceFile);   // the retired player's face is never reused

        var before = players.Select(p => p.FaceFile).ToList();
        lib.AssignMissing(players.Append(retired), players);         // never re-picks
        Assert.Equal(before, players.Select(p => p.FaceFile).ToList());
    }

    [Fact]
    public void Card_key_changes_only_when_the_image_would()
    {
        var a = new PortraitCard(7, 101, 130, new CrestSpec("DET", "#111111", "#222222", 0), null, "African/African1.png");
        Assert.Equal(a.Key(1), (a with { CurrentAbility = 112 }).Key(1));   // still Silver
        Assert.NotEqual(a.Key(1), (a with { CurrentAbility = 115 }).Key(1)); // Gold
        Assert.NotEqual(a.Key(1), (a with { PotentialAbility = 131 }).Key(1)); // POT number shown
        Assert.NotEqual(a.Key(1), (a with { Crest = null }).Key(1));
        Assert.NotEqual(a.Key(1), (a with { Draft = new DraftRecord(2026, 1, 2) }).Key(1));
        Assert.NotEqual(a.Key(1), a.Key(2));
    }
}
