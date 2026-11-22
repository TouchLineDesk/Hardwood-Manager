using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Portraits;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.Portraits;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 154: the 2K-style card redesign (#1036, option A; claude/card-redesign-mockup.md). A tall 2:3 card with angled
/// corners; metals drawn in code for Bronze, Silver and Gold, the Vecteezy gem facets for Amethyst and Ruby, the HOLOFOIL foil for
/// Pearl; the face on a neck, shoulders and tank top in the player's own skin tone; the hex POT badge (holographic at 180+); the
/// crest, the draft stamp and a name plate; no current-rating number. Small avatars show the head square, tall slots the whole card.</summary>
[Collection(SharedUiState.Name)]
public class Run154CardRedesignTests
{
    private readonly ITestOutputHelper _out;
    public Run154CardRedesignTests(ITestOutputHelper output) => _out = output;

    /// <summary>A stand-in face cut-out: a head and a neck down to the bottom edge in one skin tone, on transparency.</summary>
    internal static SKBitmap Face(SKColor skin)
    {
        var bmp = new SKBitmap(new SKImageInfo(256, 256, SKColorType.Rgba8888, SKAlphaType.Premul));
        using var c = new SKCanvas(bmp);
        c.Clear(SKColors.Transparent);
        using var p = new SKPaint { Color = skin, IsAntialias = true };
        c.DrawOval(new SKRect(70, 20, 186, 214), p);
        c.DrawRect(new SKRect(96, 180, 160, 256), p);
        p.Color = new SKColor(25, 18, 14);
        c.DrawOval(new SKRect(72, 14, 184, 80), p); // hair
        return bmp;
    }

    private static SKColor Px(SKImage img, float ux, float uy)
    {
        using var bmp = SKBitmap.FromImage(img);
        float k = img.Width / CardRenderer.UW;
        return bmp.GetPixel((int)(ux * k), (int)(uy * k));
    }

    private static PortraitCard Card(int ca, int pa, JerseySpec? kit = null, DraftRecord? draft = null, string? last = "Okafor") =>
        new(1, ca, pa, new CrestSpec("RIV", "#7A1F2B", "#E8B23A", 0), draft, "x", kit, null, "Tomás", last, "C");

    [Fact]
    public void The_card_is_2_by_3_with_angled_corners_and_the_version_is_bumped()
    {
        Assert.True(CardRenderer.Version >= 3);
        using var face = Face(new SKColor(150, 100, 75));
        using var img = CardRenderer.Render(Card(120, 150), face);
        Assert.Equal(256, img.Width);
        Assert.Equal(384, img.Height);
        Assert.Equal(0, Px(img, 227, 1).Alpha);    // the top-right angle
        Assert.Equal(0, Px(img, 1, 341).Alpha);    // the bottom-left angle
        Assert.Equal(255, Px(img, 2, 2).Alpha);    // the square corners are filled
        Assert.Equal(255, Px(img, 226, 340).Alpha);
        Assert.Equal(255, Px(img, 114, 171).Alpha);
    }

    [Fact]
    public void Each_body_tier_has_its_own_material()
    {
        using var face = Face(new SKColor(150, 100, 75));
        SKColor At(int ca) { using var img = CardRenderer.Render(Card(ca, 120, last: null), face); return Px(img, 24, 120); }
        var pearl = At(60); var bronze = At(90); var silver = At(105); var gold = At(125); var amethyst = At(140); var ruby = At(170);
        _out.WriteLine($"pearl {pearl} bronze {bronze} silver {silver} gold {gold} amethyst {amethyst} ruby {ruby}");
        Assert.True(pearl.Red > 150 && pearl.Green > 150 && pearl.Blue > 150, $"pearl {pearl}");
        Assert.True(bronze.Red > bronze.Blue + 30, $"bronze {bronze}");
        Assert.True(Math.Abs(silver.Red - silver.Blue) < 50, $"silver {silver}");
        Assert.True(gold.Red > gold.Blue + 60 && gold.Green > gold.Blue + 30, $"gold {gold}");
        Assert.True(amethyst.Blue > amethyst.Green + 30 && amethyst.Red > amethyst.Green, $"amethyst {amethyst}");
        Assert.True(ruby.Red > ruby.Green + 50 && ruby.Red > ruby.Blue + 30, $"ruby {ruby}");
    }

    [Fact]
    public void The_shoulders_take_the_skin_tone_sampled_from_the_neck_and_the_tank_the_club_kit()
    {
        var skin = new SKColor(150, 100, 75);
        using var face = Face(skin);
        Assert.Equal(skin, CardRenderer.SkinTone(face));
        using var img = CardRenderer.Render(Card(120, 130, new JerseySpec("#00C040", "#FFFFFF", 23)), face, 512);
        var shoulder = Px(img, 30, 18 + 200);   // the left delt, bare skin
        Assert.True(Math.Abs(shoulder.Red - 150) < 60 && shoulder.Red > shoulder.Blue + 30, $"shoulder {shoulder}");
        var chest = Px(img, 84, 18 + 214);      // the tank, left of the number
        Assert.True(chest.Green > 120 && chest.Red < 90 && chest.Blue < 110, $"chest {chest}");
        // No kit: the neutral practice tank, not the club colour.
        using var plain = CardRenderer.Render(Card(120, 130), face, 512);
        var grey = Px(plain, 84, 18 + 214);
        Assert.True(Math.Abs(grey.Red - grey.Green) < 30 && grey.Green < 120, $"plain tank {grey}");
        // A face with no opaque neck falls back to a mid tone instead of failing.
        using var empty = new SKBitmap(256, 256); empty.Erase(SKColors.Transparent);
        Assert.Equal(SKColor.Parse("#9B6A4C"), CardRenderer.SkinTone(empty));
    }

    [Fact]
    public void The_name_plate_shows_the_name_and_position_and_shrinks_a_long_surname()
    {
        using var face = Face(new SKColor(150, 100, 75));
        static int Bright(SKImage img, float x0, float y0, float x1, float y1)
        {
            using var bmp = SKBitmap.FromImage(img);
            float k = img.Width / CardRenderer.UW; int n = 0;
            for (int y = (int)(y0 * k); y < (int)(y1 * k); y++)
                for (int x = (int)(x0 * k); x < (int)(x1 * k); x++)
                { var c = bmp.GetPixel(x, y); if (c.Red > 220 && c.Green > 220 && c.Blue > 220) n++; }
            return n;
        }
        using var named = CardRenderer.Render(Card(140, 150), face, 512);
        using var blank = CardRenderer.Render(Card(140, 150, last: null), face, 512);
        float top = CardRenderer.PlateTop;
        Assert.True(Bright(named, 34, top + 26, 140, top + 56) > 200, "the last name is drawn");
        Assert.Equal(0, Bright(blank, 34, top + 26, 140, top + 56));
        // The plate is dark glass.
        var glass = Px(blank, 120, top + 40);
        Assert.True(glass.Red < 90 && glass.Green < 90, $"plate {glass}");
        // A very long surname stays inside the plate (nothing white past the position's left edge).
        using var longName = CardRenderer.Render(Card(140, 150, last: "Wolowitz-Hernandez-Papadopoulos"), face, 512);
        Assert.Equal(0, Bright(longName, 196, top + 26, 214, top + 56));
    }

    [Fact]
    public void The_diamond_badge_is_holographic_with_its_tag_and_the_draft_stamp_sits_top_right()
    {
        using var face = Face(new SKColor(150, 100, 75));
        using var ruby = CardRenderer.Render(Card(170, 175, draft: new DraftRecord(2024, 1, 2)), face, 512);
        using var dia = CardRenderer.Render(Card(170, 186, draft: new DraftRecord(2024, 1, 2)), face, 512);
        var rubyBadge = Px(ruby, 20, 44); var diaBadge = Px(dia, 20, 44);
        Assert.True(rubyBadge.Red > rubyBadge.Green + 60, $"ruby badge {rubyBadge}");
        Assert.True(diaBadge.Green > 120 && diaBadge.Blue > 120, $"diamond badge {diaBadge}");
        var tag = Px(dia, 18, 82);   // the DIAMOND tag under the hex
        Assert.True(tag.Red > 170 && tag.Blue > 200, $"tag {tag}");
        var stamp = Px(dia, 150, 17);  // the stamp's dark glass, left of the angled corner
        Assert.True(stamp.Red < 90 && stamp.Green < 90, $"stamp {stamp}");
    }

    [Fact]
    public void The_plate_name_is_part_of_the_cards_key()
    {
        var a = Card(120, 130);
        Assert.NotEqual(a.Key(3), (a with { LastName = "Ward" }).Key(3));
        Assert.NotEqual(a.Key(3), (a with { Position = "PF" }).Key(3));
        Assert.Equal(a.Key(3), (a with { CurrentAbility = 121 }).Key(3));
        var league = new WorldGenerator(new Rng(2026)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var p = league.Teams[0].Roster[0]; p.FaceFile = "African/African1.png";
        var card = PortraitCard.For(league, null).First(c => c.PlayerId == p.Id);
        Assert.Equal((p.FirstName, p.LastName, p.PrimaryPosition.ToString()), (card.FirstName, card.LastName, card.Position));
    }

    [Fact]
    public void Small_slots_show_the_head_square_and_tall_slots_the_whole_card()
    {
        var card = new Size(256, 384);
        var (src, dst, whole) = Avatar.PortraitRects(card, new Size(52, 52));
        Assert.False(whole);
        Assert.Equal(new Rect(0, 0, 52, 52), dst);
        double u = 256 / (double)CardRenderer.UW;
        Assert.Equal(new Rect(10 * u, 8 * u, 200 * u, 200 * u), src);
        Assert.True(src.Bottom < CardRenderer.PlateTop * u); // no plate in the head square
        (_, dst, whole) = Avatar.PortraitRects(card, new Size(120, 180));
        Assert.True(whole);
        Assert.Equal(new Rect(0, 0, 120, 180), dst);
        (_, _, whole) = Avatar.PortraitRects(card, new Size(150, 150));   // a square slot keeps the head
        Assert.False(whole);
        (_, _, whole) = Avatar.PortraitRects(card, new Size(60, 90));     // under 96 high: the head
        Assert.False(whole);
        // An old square card (not yet redrawn) fills the slot as before.
        (src, dst, whole) = Avatar.PortraitRects(new Size(256, 256), new Size(48, 48));
        Assert.Equal((new Rect(0, 0, 256, 256), new Rect(0, 0, 48, 48), false), (src, dst, whole));
    }

    /// <summary>The proof sheet: every body tier with a badge, a kit, a draft stamp and a name, plus the small and the
    /// draft-night avatars, written to HW_SCREENSHOT_DIR (run154-cards.png) for the screenshot check.</summary>
    [AvaloniaFact]
    public void Proof_sheet_of_every_tier_and_the_avatar_sizes()
    {
        var skins = new[] { new SKColor(201, 154, 121), new SKColor(141, 90, 59), new SKColor(226, 185, 155), new SKColor(94, 58, 37) };
        var rows = new (int ca, int pa, string first, string last, string pos, string prim, string sec, DraftRecord? d)[]
        {
            (70, 80, "Kyle", "Benson", "PG", "#1D428A", "#FFC72C", null),
            (90, 108, "Marcus", "Ellis", "SG", "#7A1F2B", "#E8B23A", null),
            (108, 118, "Dmitri", "Vasquez-Delacroix", "SF", "#FFFFFF", "#FFFFFF", null),
            (128, 140, "Andre", "Brooks", "PG", "#006BB6", "#F58426", new DraftRecord(2025, 1, 9)),
            (148, 161, "Tomás", "Okafor", "C", "#00471B", "#EEE1C6", new DraftRecord(2024, 1, 2)),
            (172, 186, "Jalen", "Ward", "SF", "#552583", "#FDB927", new DraftRecord(2023, 1, 1)),
        };
        var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d0 ? d0 : Path.Combine(Path.GetTempPath(), "hw154");
        Directory.CreateDirectory(dir);
        const int cw = 228, ch = 342, gap = 14;
        using var sheet = SKSurface.Create(new SKImageInfo(rows.Length * (cw + gap) + gap, ch + 2 * gap + 140, SKColorType.Rgba8888, SKAlphaType.Premul));
        sheet.Canvas.Clear(new SKColor(14, 17, 22));
        for (int i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            using var face = Face(skins[i % skins.Length]);
            var card = new PortraitCard(i + 1, r.ca, r.pa, new CrestSpec("RIV", "#7A1F2B", "#E8B23A", i % 5), r.d, "x",
                new JerseySpec(r.prim, r.sec, JerseySpec.NumberFor(i + 1)), null, r.first, r.last, r.pos);
            using var img = CardRenderer.Render(card, face, cw);
            Assert.Equal(ch, img.Height);
            sheet.Canvas.DrawImage(img, gap + i * (cw + gap), gap);
            // The head square at 52 px under each card, as the roster lists show it.
            var u = cw / CardRenderer.UW; var hs = CardRenderer.HeadSquare;
            var src = new SKRect(hs.Left * u, hs.Top * u, hs.Right * u, hs.Bottom * u);
            using var p = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            sheet.Canvas.DrawImage(img, src, new SKRect(gap + i * (cw + gap), ch + 2 * gap, gap + i * (cw + gap) + 52, ch + 2 * gap + 52), p);
            sheet.Canvas.DrawImage(img, src, new SKRect(gap + i * (cw + gap) + 62, ch + 2 * gap, gap + i * (cw + gap) + 62 + 26, ch + 2 * gap + 26), p);
        }
        using var shot = sheet.Snapshot();
        using var data = shot.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(dir, "run154-cards.png"), data.ToArray());
        _out.WriteLine(Path.Combine(dir, "run154-cards.png"));
    }
}
