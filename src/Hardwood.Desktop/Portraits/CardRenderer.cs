using System.Reflection;
using Hardwood.Core.Portraits;
using SkiaSharp;

namespace Hardwood.Desktop.Portraits;

/// <summary>
/// Draws the player card natively with SkiaSharp. Version 3 is the 2K-style redesign (#1036, option A;
/// claude/card-redesign-mockup.md): a tall 2:3 card with angled corners, a tier body (metals drawn in code for Bronze,
/// Silver and Gold; the Vecteezy gem facets for Amethyst and Ruby; the HOLOFOIL foil for Pearl), the face on a drawn
/// neck, shoulders and tank top in the player's own skin tone, the hex POT badge (holographic for Diamond), the crest,
/// the draft stamp and a name plate. Still no current-rating number.
/// All measurements are the mock-up's 228x342 card (card_mockup_build.py), scaled to the output size.
/// Thread-safe: each call uses its own surface, so cards can be rendered on a background thread.
/// </summary>
public static class CardRenderer
{
    /// <summary>Bump when the card's look changes, so every portrait is redrawn once.</summary>
    public const int Version = 3; // 2: jerseys and real photos (#350-351); 3: the 2:3 2K-style card (#1036)

    /// <summary>The card in the mock-up's units: 228 wide, 342 tall (2:3).</summary>
    public const float UW = 228f, UH = 342f;

    /// <summary>The name plate's top edge (units); the art above it is what small avatars show.</summary>
    public const float PlateTop = UH - 64f;

    /// <summary>The square of the card (units) that small avatars show: the face, the shoulders and the POT badge, no plate.</summary>
    public static readonly SKRect HeadSquare = new(10, 8, 210, 208);

    /// <summary>The old square card's size, used to place the crest drawing (it was laid out on a 512 canvas).</summary>
    private const float W = 512f;

    private static readonly Lazy<(SKTypeface bs, SKTypeface bc)> Fonts = new(LoadFonts);
    private static readonly Dictionary<string, SKBitmap?> Textures = new();

    private static Stream? Resource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(name, StringComparison.Ordinal));
        return res == null ? null : asm.GetManifestResourceStream(res);
    }

    private static (SKTypeface, SKTypeface) LoadFonts()
    {
        static SKTypeface Load(string name)
        {
            using var s = Resource(name) ?? throw new InvalidDataException($"Missing font {name}");
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return SKTypeface.FromData(SKData.CreateCopy(ms.ToArray()));
        }
        return (Load("BigShoulders-900.ttf"), Load("BarlowC-800.ttf"));
    }

    /// <summary>An embedded card texture (Assets/Cards), decoded once; null if missing (the card falls back to a gradient).</summary>
    private static SKBitmap? Texture(string name)
    {
        lock (Textures)
        {
            if (Textures.TryGetValue(name, out var b)) return b;
            try
            {
                using var s = Resource("Cards." + name);
                b = s == null ? null : SKBitmap.Decode(s);
                b?.SetImmutable(); // shared across render threads, read-only; drawing it then needs no copy
            }
            catch (Exception) { b = null; }
            return Textures[name] = b;
        }
    }

    /// <summary>Render a card and write it as a PNG (atomically: a temp file, then a rename).</summary>
    public static void RenderToFile(PortraitCard card, string faceFullPath, string outFile, int width = 256)
    {
        using var face = SKBitmap.Decode(faceFullPath) ?? throw new InvalidDataException($"Can't read face {faceFullPath}");
        using var img = Render(card, face, width);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        var tmp = outFile + ".tmp";
        using (var fs = File.Create(tmp)) data.SaveTo(fs);
        File.Move(tmp, outFile, overwrite: true);
    }

    /// <summary>Render a card <paramref name="width"/> wide and 1.5x as tall (drawn at twice the size, then scaled down).</summary>
    public static SKImage Render(PortraitCard card, SKBitmap face, int width = 256)
    {
        int height = (int)Math.Round(width * UH / UW);
        int bw = width * 2, bh = height * 2;
        using var full = SKSurface.Create(new SKImageInfo(bw, bh, SKColorType.Rgba8888, SKAlphaType.Premul));
        full.Canvas.Clear(SKColors.Transparent);
        full.Canvas.Scale(bw / UW, bh / UH);
        Draw(full.Canvas, card, face);
        using var big = full.Snapshot();
        using var bmp = SKBitmap.FromImage(big);
        using var small = bmp.Resize(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.High);
        return SKImage.FromPixelCopy(small.Info, small.GetPixels(), small.RowBytes);
    }

    // ---- the tier bodies ---------------------------------------------------------------------
    /// <summary>How a body tier is drawn: a metal (three colours) or a texture, a tint at the foot, the frame's edge and the ink.</summary>
    internal sealed record Material(string? Texture, string Dark, string Base, string Light, string Edge, string Ink, SKColor Tint);

    internal static Material MaterialFor(string tier) => tier switch
    {
        "Pearl" => new("foil-pearl.jpg", "#8C96A3", "#D9DFE6", "#FFFFFF", "#FFFFFF", "#1B2430", new SKColor(140, 150, 163, 64)),
        "Bronze" => new(null, "#3A1A07", "#8A4B22", "#E0A57A", "#E0A57A", "#FFF1E6", new SKColor(60, 25, 5, 64)),
        "Silver" => new(null, "#2C3746", "#8C9DB1", "#F4F8FC", "#F4F8FC", "#0F1724", new SKColor(20, 30, 45, 46)),
        "Gold" => new(null, "#7A5200", "#E0AE12", "#FFF3A6", "#FFF3A6", "#2A1C00", new SKColor(90, 60, 0, 26)),
        "Amethyst" => new("gem-amethyst.jpg", "#2A0F52", "#6D34B3", "#C9A2FF", "#D9B8FF", "#FFFFFF", new SKColor(55, 15, 110, 107)),
        _ => new("gem-ruby.jpg", "#420606", "#A5161A", "#FF7A6B", "#FFB3A8", "#FFFFFF", new SKColor(120, 8, 20, 140)),
    };

    /// <summary>The card's outline: angled corners at the top right and bottom left (14% across, 9% down).</summary>
    private static SKPath Outline(SKRect r) => Poly(r, (0, 0), (.86f, 0), (1, .09f), (1, 1), (.14f, 1), (0, .91f));

    // ------------------------------------------------------------------------------------------
    private static void Draw(SKCanvas c, PortraitCard card, SKBitmap face)
    {
        var (bs, bc) = Fonts.Value;
        var ct = PortraitTiers.Body(card.CurrentAbility);
        var m = MaterialFor(ct.Name);
        var card0 = new SKRect(0, 0, UW, UH);
        SKColor edge = Hex(m.Edge);

        using var outline = Outline(card0);
        c.Save();
        c.ClipPath(outline, SKClipOperation.Intersect, true);

        Body(c, m, card0);

        // .tint: transparent down to 30%, then the tier's tint at the foot.
        using (var p = new SKPaint { IsAntialias = true })
        {
            p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, UH), new[] { m.Tint.WithAlpha(0), m.Tint.WithAlpha(0), m.Tint }, new[] { 0f, .30f, 1f }, SKShaderTileMode.Clamp);
            c.DrawRect(card0, p);
        }
        // .sheen: one diagonal white band, screened.
        using (var p = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Screen })
        {
            p.Shader = Linear(card0, 115, (0, White(0)), (.30f, White(0)), (.42f, White(.28f)), (.52f, White(0)), (1, White(0)));
            c.DrawRect(card0, p);
        }
        // .glow: a soft light behind the head.
        using (var p = new SKPaint { IsAntialias = true })
        {
            var ctr = new SKPoint(UW / 2, UH * .38f);
            p.Shader = SKShader.CreateRadialGradient(ctr, UW * .6f, new[] { White(.28f), White(0) }, new[] { 0f, .70f }, SKShaderTileMode.Clamp);
            c.DrawRect(card0, p);
        }

        if (card.Photo != null) PhotoArt(c, face);
        else
        {
            UpperBody(c, face, card.Jersey, bs);
            Face(c, face);
        }

        // .frame: an inner outline in the tier's edge colour.
        using (var frame = Outline(new SKRect(7, 7, UW - 7, UH - 7)))
        using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2, Color = edge.WithAlpha(A(.75f)) })
            c.DrawPath(frame, p);

        Badge(c, card.PotentialAbility, bs, bc);
        if (card.Draft is { } d) DraftStamp(c, $"{d.Year} · R{d.Round} P{d.Pick}", Hex(PortraitTiers.Badge(card.PotentialAbility).Light), bc);
        if (card.Crest is { } crest)
        {
            // The crest's drawing was laid out in a 150x175 box at (22, 315) on the old 512 card; place that box at (12, 234), 38 wide.
            c.Save();
            c.Translate(12, PlateTop - 44);
            c.Scale(38f / 150f);
            c.Translate(-22, -(W - 22 - 175));
            Crest(c, crest, bs);
            c.Restore();
        }
        Plate(c, card, edge, bs, bc);
        c.Restore();
    }

    private static void Body(SKCanvas c, Material m, SKRect r)
    {
        if (m.Texture != null && Texture(m.Texture) is { } tex && tex.Width > 0)
        {
            // background: url(...) center/cover
            float k = Math.Max(r.Width / tex.Width, r.Height / tex.Height);
            float w = tex.Width * k, h = tex.Height * k;
            using var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            c.DrawBitmap(tex, new SKRect(r.MidX - w / 2, r.MidY - h / 2, r.MidX + w / 2, r.MidY + h / 2), p);
            return;
        }
        SKColor dk = Hex(m.Dark), bs = Hex(m.Base), lt = Hex(m.Light);
        // A polished metal: linear-gradient(155deg, light, base 28%, dark 52%, base 70%, light 86%, base) ...
        using (var p = new SKPaint { IsAntialias = true })
        {
            p.Shader = Linear(r, 155, (0, lt), (.28f, bs), (.52f, dk), (.70f, bs), (.86f, lt), (1, bs));
            c.DrawRect(r, p);
        }
        // ... under a brushed grain: repeating-linear-gradient(90deg, white .06 0 1px, black .05 1px 3px).
        using (var p = new SKPaint { IsAntialias = true })
        {
            p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(3, 0), new[] { White(.06f), White(.06f), new SKColor(0, 0, 0, A(.05f)), new SKColor(0, 0, 0, A(.05f)) },
                new[] { 0f, 1f / 3, 1f / 3, 1f }, SKShaderTileMode.Repeat);
            c.DrawRect(r, p);
        }
    }

    // ---- the art: face, neck, shoulders and tank top (the mock-up's upper_body(); Danny approved it Nov 21) ---------
    /// <summary>The face: 128 units at (50, 44), its bottom 18% faded out so the neck carries on with no seam.</summary>
    private static readonly SKRect FaceBox = new(50, 44, 178, 172);

    private static void Face(SKCanvas c, SKBitmap face)
    {
        c.SaveLayer(FaceBox, null);
        using (var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
            c.DrawBitmap(face, FaceBox, p);
        using (var p = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.DstIn })
        {
            p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, FaceBox.Top), new SKPoint(0, FaceBox.Bottom), new[] { SKColors.Black, SKColors.Black, SKColors.Transparent }, new[] { 0f, .82f, 1f }, SKShaderTileMode.Clamp);
            c.DrawRect(FaceBox, p);
        }
        c.Restore();
    }

    /// <summary>A real photo from the graphics pack (a head-and-shoulders cut-out, already in his jersey), standing on the plate.</summary>
    private static void PhotoArt(SKCanvas c, SKBitmap photo)
    {
        if (photo.Width == 0) return;
        float w = 250, h = w * photo.Height / photo.Width;
        var dst = new SKRect(UW / 2 - w / 2, PlateTop - h, UW / 2 + w / 2, PlateTop);
        using var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
        p.ImageFilter = SKImageFilter.CreateDropShadow(0, 4, 3, 3, new SKColor(0, 0, 0, A(.45f)));
        c.DrawBitmap(photo, dst, p);
    }

    /// <summary>A neutral practice tank for a player with no club kit on his card (free agents, prospects, jerseys off).</summary>
    private static readonly (string prim, string sec) PlainKit = ("#3A4250", "#9AA3B2");

    private static void UpperBody(SKCanvas c, SKBitmap face, JerseySpec? kit, SKTypeface bs)
    {
        var skin = SkinTone(face);
        SKColor prim = Hex(kit?.PrimaryColor ?? PlainKit.prim), sec = Hex(kit?.SecondaryColor ?? PlainKit.sec);
        // A trim that disappears into the shirt (e.g. white on white) falls back to dark or light.
        if (Distance(prim, sec) < 90) sec = Luma(prim) > 140 ? Hex("#111418") : SKColors.White;

        c.Save();
        c.Translate(0, 18); // the mock-up's 228x262 drawing starts 18 units down the card
        using var skinP = new SKPaint { IsAntialias = true, Color = skin };
        using var shoulders = Svg("M114 148 C88 150 66 158 44 172 C22 186 10 214 6 262 L222 262 C218 214 206 186 184 172 C162 158 140 150 114 148 Z");
        c.DrawPath(shoulders, skinP);
        using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, A(.12f)) })
        using (var traps = Svg("M114 148 C88 150 66 158 44 172 C70 166 92 162 114 162 C136 162 158 166 184 172 C162 158 140 150 114 148 Z"))
            c.DrawPath(traps, p);
        // The arms' shading: dark at the outside, a touch of light inside.
        using (var armL = Svg("M6 262 C10 214 18 190 34 176 L58 186 C50 210 46 236 46 262 Z"))
        using (var armR = Svg("M222 262 C218 214 210 190 194 176 L170 186 C178 210 182 236 182 262 Z"))
        using (var p = new SKPaint { IsAntialias = true })
        {
            var cols = new[] { new SKColor(0, 0, 0, A(.45f)), new SKColor(0, 0, 0, 0), White(.08f) };
            p.Shader = SKShader.CreateLinearGradient(new SKPoint(6, 0), new SKPoint(58, 0), cols, new[] { 0f, .5f, 1f }, SKShaderTileMode.Clamp);
            c.DrawPath(armL, p);
            p.Shader = SKShader.CreateLinearGradient(new SKPoint(222, 0), new SKPoint(170, 0), cols, new[] { 0f, .5f, 1f }, SKShaderTileMode.Clamp);
            c.DrawPath(armR, p);
        }
        // The neck, with a shadow under the jaw and darker sides.
        using (var neck = Svg("M92 96 C94 124 92 142 80 160 L148 160 C136 142 134 124 136 96 Z"))
        {
            c.DrawPath(neck, skinP);
            using var p = new SKPaint { IsAntialias = true };
            p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 96), new SKPoint(0, 160), new[] { new SKColor(0, 0, 0, A(.55f)), new SKColor(0, 0, 0, A(.12f)), new SKColor(0, 0, 0, 0) }, new[] { 0f, .45f, 1f }, SKShaderTileMode.Clamp);
            c.DrawPath(neck, p);
        }
        using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, A(.18f)) })
        using (var l = Svg("M92 96 C94 124 92 142 80 160 L96 160 C102 142 102 124 100 96 Z"))
        using (var r = Svg("M136 96 C134 124 136 142 148 160 L132 160 C126 142 126 124 128 96 Z"))
        {
            c.DrawPath(l, p);
            c.DrawPath(r, p);
        }
        // The tank top: straps over the traps, armholes that leave the delts bare, a scoop neck with trim.
        using var tank = Svg("M74 160 C72 168 70 176 66 186 C60 204 58 232 60 262 L168 262 C170 232 168 204 162 186 C158 176 156 168 154 160 " +
                             "C146 158 142 158 138 160 C134 186 94 186 90 160 C86 158 82 158 74 160 Z");
        using (var p = new SKPaint { IsAntialias = true, Color = prim })
            c.DrawPath(tank, p);
        using (var p = new SKPaint { IsAntialias = true })
        {
            // radial-gradient(at .5 .25, r .85): a highlight on the chest, darker edges (over the tank's 60..168 x 158..262 box).
            var b = tank.Bounds;
            p.Shader = SKShader.CreateRadialGradient(new SKPoint(b.MidX, b.Top + b.Height * .25f), Math.Max(b.Width, b.Height) * .85f,
                new[] { White(.16f), new SKColor(0, 0, 0, 0), new SKColor(0, 0, 0, A(.45f)) }, new[] { 0f, .55f, 1f }, SKShaderTileMode.Clamp);
            c.DrawPath(tank, p);
        }
        using (var p = new SKPaint { IsAntialias = true, Color = sec, Style = SKPaintStyle.Stroke, StrokeWidth = 5 })
        using (var scoop = Svg("M90 160 C94 186 134 186 138 160"))
        using (var straps = Svg("M74 160 C72 168 70 176 66 186 C60 204 58 232 60 262 M154 160 C156 168 158 176 162 186 C168 204 170 232 168 262"))
        {
            c.DrawPath(scoop, p);
            p.StrokeWidth = 4.5f;
            c.DrawPath(straps, p);
        }
        if (kit != null)
        {
            using var font = new SKFont(bs, 40) { Subpixel = true, Edging = SKFontEdging.Antialias };
            var text = kit.Number.ToString();
            float tx = 114 - Measure(font, text) / 2;
            bool light = Luma(prim) > 170;
            using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeJoin = SKStrokeJoin.Round, Color = light ? Hex("#111418") : sec };
            using var fill = new SKPaint { IsAntialias = true, Color = (light ? sec : SKColors.White).WithAlpha(A(.95f)) };
            c.DrawText(text, tx, 226, font, stroke);
            c.DrawText(text, tx, 226, font, fill);
        }
        c.Restore();
    }

    /// <summary>
    /// The player's skin tone, sampled from the neck at the bottom of the face cut-out (rows 226-252, columns 104-152 of a 256 face):
    /// the opaque pixels between the 55th and 80th brightness percentile, so the jaw's shadow doesn't darken it.
    /// </summary>
    public static SKColor SkinTone(SKBitmap face)
    {
        var fallback = Hex("#9B6A4C");
        if (face.Width < 8 || face.Height < 8) return fallback;
        float sx = face.Width / 256f, sy = face.Height / 256f;
        var px = new List<(double lum, SKColor c)>();
        for (int y = (int)(226 * sy); y < Math.Min(face.Height, (int)(252 * sy)); y++)
            for (int x = (int)(104 * sx); x < Math.Min(face.Width, (int)(152 * sx)); x++)
            {
                var c = face.GetPixel(x, y);
                if (c.Alpha > 200) px.Add(((c.Red + c.Green + c.Blue) / 3.0, c));
            }
        if (px.Count < 8) return fallback;
        px.Sort((a, b) => a.lum.CompareTo(b.lum));
        double lo = Percentile(px, .55), hi = Percentile(px, .80);
        var pick = px.Where(p => p.lum >= lo && p.lum <= hi).Select(p => p.c).ToList();
        if (pick.Count == 0) return fallback;
        return new SKColor((byte)pick.Average(p => p.Red), (byte)pick.Average(p => p.Green), (byte)pick.Average(p => p.Blue));
    }

    /// <summary>numpy's default (linear) percentile of a sorted list.</summary>
    private static double Percentile(List<(double lum, SKColor c)> sorted, double q)
    {
        double pos = q * (sorted.Count - 1);
        int i = (int)Math.Floor(pos);
        return i + 1 < sorted.Count ? sorted[i].lum + (pos - i) * (sorted[i + 1].lum - sorted[i].lum) : sorted[i].lum;
    }

    private static SKPath Svg(string d) => SKPath.ParseSvgPathData(d);

    /// <summary>Letters the card fonts lack (they cover Latin-1 only: Ć, Š, Ş, Ž, Ł, Đ …) folded to their base letter,
    /// so "JOKIĆ" reads "JOKIC" instead of losing a letter. Anything still missing becomes a space.</summary>
    public static string Fold(string text, SKTypeface face)
    {
        if (face.ContainsGlyphs(text)) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsSurrogate(ch)) { continue; }
            if (face.ContainsGlyph(ch)) { sb.Append(ch); continue; }
            var mapped = ch switch
            {
                'Đ' or 'Ð' => "D", 'đ' => "d", 'Ł' => "L", 'ł' => "l", 'İ' => "I", 'ı' => "i", 'Ø' => "O", 'ø' => "o", 'Æ' => "AE", 'æ' => "ae", 'ß' => "SS",
                _ => new string(ch.ToString().Normalize(System.Text.NormalizationForm.FormD)
                        .Where(d => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(d) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()),
            };
            foreach (var m in mapped) sb.Append(face.ContainsGlyph(m) ? m : ' ');
        }
        return sb.ToString();
    }

    /// <summary>The card fonts, for tests.</summary>
    internal static (SKTypeface bigShoulders, SKTypeface barlow) CardFonts => Fonts.Value;

    /// <summary>Whether an embedded card texture loads (tests).</summary>
    public static bool HasTexture(string name) => Texture(name) != null;

    private static double Luma(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;
    private static double Distance(SKColor a, SKColor b) =>
        Math.Sqrt(Math.Pow(a.Red - b.Red, 2) + Math.Pow(a.Green - b.Green, 2) + Math.Pow(a.Blue - b.Blue, 2));

    // ---- the POT badge, the draft stamp and the name plate ------------------------------------
    /// <summary>The hex POT badge at the top left (56x64 at (12,12)), in the potential's tier; holographic with a DIAMOND tag at 180+.</summary>
    private static void Badge(SKCanvas c, int pa, SKTypeface bs, SKTypeface bc)
    {
        var pt = PortraitTiers.Badge(pa);
        bool dia = pa >= PortraitTiers.Diamond.Lo;
        var box = new SKRect(12, 12, 68, 76);
        using var hex = HexPath(box);
        using (var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, A(.35f)), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2) })
        {
            c.Save(); c.Translate(0, 1.5f); c.DrawPath(hex, shadow); c.Restore();
        }
        c.Save();
        c.ClipPath(hex, SKClipOperation.Intersect, true);
        if (dia && Texture("foil-diamond.jpg") is { } foil && foil.Width > 0)
        {
            float k = Math.Max(box.Width / foil.Width, box.Height / foil.Height);
            float w = foil.Width * k, h = foil.Height * k;
            using var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
            c.DrawBitmap(foil, new SKRect(box.MidX - w / 2, box.MidY - h / 2, box.MidX + w / 2, box.MidY + h / 2), p);
        }
        else
        {
            using var p = new SKPaint { IsAntialias = true };
            p.Shader = dia
                ? Conic(new SKPoint(box.MidX, box.MidY), 200, Hex("#BFEFFF"), Hex("#FFB8E8"), Hex("#FFE89A"), Hex("#B8FFD6"), Hex("#B8CCFF"), Hex("#E2B8FF"), Hex("#BFEFFF"))
                : Linear(box, 160, (0, Hex(pt.Base)), (1, Hex(pt.Dark)));
            c.DrawRect(box, p);
        }
        // inset 0 0 0 2px rgba(255,255,255,.4)
        using (var p = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4, Color = White(.4f) })
            c.DrawPath(hex, p);
        c.Restore();

        using var numFont = new SKFont(bs, 26) { Subpixel = true, Edging = SKFontEdging.Antialias };
        using var potFont = new SKFont(bc, 11) { Subpixel = true, Edging = SKFontEdging.Antialias };
        var (potAsc, potLh) = LineBox(potFont);
        // line-height 1 for the number: its 26-unit line holds the glyphs' ascent and descent, centred.
        var nm = numFont.Metrics;
        float numLh = 26, numBase = 0 + (numLh - (nm.Descent - nm.Ascent)) / 2 - nm.Ascent;
        float stack = numLh + 1 + potLh, top = box.Top + (box.Height - stack) / 2;
        var ink = dia ? Hex("#0A1E30") : Hex(pt.Ink);
        var num = pa.ToString();
        using (var p = new SKPaint { IsAntialias = true, Color = dia ? White(.8f) : new SKColor(0, 0, 0, A(.25f)) })
        {
            c.DrawText(num, box.MidX - Measure(numFont, num) / 2, top + numBase + (dia ? 0 : 1), numFont, p);
            p.Color = ink;
            c.DrawText(num, box.MidX - Measure(numFont, num) / 2, top + numBase, numFont, p);
            Spaced(c, "POT", box.MidX, top + numLh + 1 + potAsc, potFont, 1.5f, p);
        }
        if (dia) DiamondTag(c, box, bc);
    }

    private static void DiamondTag(SKCanvas c, SKRect badge, SKTypeface bc)
    {
        using var font = new SKFont(bc, 9) { Subpixel = true, Edging = SKFontEdging.Antialias };
        var (asc, lh) = LineBox(font);
        var box = new SKRect(badge.Left, badge.Bottom + 3, badge.Right, badge.Bottom + 3 + lh + 2);
        using (var p = new SKPaint { IsAntialias = true })
        {
            p.Shader = Linear(box, 90, (0, Hex("#BFE9FF")), (1, Hex("#F4D9FF")));
            c.DrawRoundRect(box, 3, 3, p);
        }
        using (var p = new SKPaint { IsAntialias = true, Color = Hex("#0A1E30") })
            Spaced(c, "DIAMOND", box.MidX, box.Top + 1 + asc, font, 1.2f, p);
    }

    /// <summary>The draft stamp at the top right, clear of the angled corner: white on dark glass, edged in the badge's light colour.</summary>
    private static void DraftStamp(SKCanvas c, string text, SKColor border, SKTypeface bc)
    {
        using var font = new SKFont(bc, 11) { Subpixel = true, Edging = SKFontEdging.Antialias };
        float spacing = .8f;
        float tw = SpacedWidth(text, font, spacing);
        var (asc, lh) = LineBox(font);
        var box = new SKRect(UW - 30 - (tw + 12), 14, UW - 30, 14 + lh + 4);
        using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(8, 12, 18, A(.8f)) })
            c.DrawRoundRect(box, 3, 3, p);
        using (var p = new SKPaint { IsAntialias = true, Color = border, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f })
        {
            var r = box; r.Inflate(-.6f, -.6f);
            c.DrawRoundRect(r, 2.5f, 2.5f, p);
        }
        using (var p = new SKPaint { IsAntialias = true, Color = SKColors.White })
            DrawSpacedFrom(c, text, box.Left + 6, box.Top + 2 + asc, font, spacing, p);
    }

    /// <summary>The name plate: first name small, LAST NAME large, the position on the right, on a dark glass band edged in the tier's colour.</summary>
    private static void Plate(SKCanvas c, PortraitCard card, SKColor edge, SKTypeface bs, SKTypeface bc)
    {
        var band = new SKRect(0, PlateTop, UW, UH);
        using (var p = new SKPaint { IsAntialias = true })
        {
            p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, band.Top), new SKPoint(0, band.Bottom), new[] { new SKColor(10, 12, 18, A(.55f)), new SKColor(10, 12, 18, A(.88f)) }, null, SKShaderTileMode.Clamp);
            c.DrawRect(band, p);
            p.Shader = null; p.Color = edge;
            c.DrawRect(new SKRect(0, band.Top, UW, band.Top + 2), p);
        }
        if (card.LastName is not { Length: > 0 } last) return;
        float x = 34, right = UW - 14;
        if (card.Position is { Length: > 0 } pos)
        {
            using var pf = new SKFont(bs, 20) { Subpixel = true, Edging = SKFontEdging.Antialias };
            var (pAsc, _) = LineBox(pf);
            float pw = Measure(pf, pos);
            using var p = new SKPaint { IsAntialias = true, Color = edge };
            c.DrawText(pos, right - pw, band.Top + 14 + pAsc, pf, p);
            right -= pw + 8;
        }
        float room = right - x;
        using (var ff = new SKFont(bc, 13) { Subpixel = true, Edging = SKFontEdging.Antialias })
        using (var p = new SKPaint { IsAntialias = true, Color = Hex("#DDE3EC") })
        {
            var first = Fold((card.FirstName ?? "").ToUpperInvariant(), bc);
            var (fAsc0, _) = LineBox(ff);
            float fw = SpacedWidth(first, ff, 1);
            if (fw > room) ff.Size = 13 * room / fw;
            // A shrunken first name keeps the line's 13-unit box: its baseline moves up with the smaller ascent, centred.
            var (fAsc, _) = LineBox(ff);
            float lineTop = band.Top + 2 + 7 + (fAsc0 - fAsc) / 2;
            DrawSpacedFrom(c, first, x, lineTop + fAsc, ff, ff.Size / 13f, p);
        }
        using (var lf = new SKFont(bs, 27) { Subpixel = true, Edging = SKFontEdging.Antialias })
        using (var p = new SKPaint { IsAntialias = true, Color = SKColors.White })
        {
            var full = Fold(last.ToUpperInvariant(), bs);
            var text = full;
            float lw = SpacedWidth(text, lf, .5f);
            // A long surname shrinks to fit (never below 60%), then is cut with an ellipsis.
            float size = 27;
            if (lw > room) size = Math.Max(27 * .6f, 27 * room / lw);
            lf.Size = size;
            float sp = .5f * size / 27;
            for (int keep = full.Length - 1; keep >= 1 && SpacedWidth(text, lf, sp) > room; keep--)
                text = full[..keep].TrimEnd(' ', '-') + "…";
            // line-height 1 below the first name's line (13 * 1.2 = 16 units).
            var lm = lf.Metrics;
            float lineTop = band.Top + 2 + 7 + 16 + (27 - size) / 2;
            float baseline = lineTop + (size - (lm.Descent - lm.Ascent)) / 2 - lm.Ascent;
            DrawSpacedFrom(c, text, x, baseline, lf, sp, p);
        }
    }

    // ---- the crest (drawn in the old 512 card's crest box; Draw() places it) -------------------
    private static readonly Dictionary<string, SKBitmap?> LogoCache = new();

    /// <summary>A logo-pack image, decoded once per file (null if unreadable).</summary>
    private static SKBitmap? Logo(string path)
    {
        lock (LogoCache)
        {
            if (LogoCache.TryGetValue(path, out var b)) return b;
            try { b = SKBitmap.Decode(path); } catch (Exception) { b = null; }
            if (LogoCache.Count > 400) LogoCache.Clear();
            return LogoCache[path] = b;
        }
    }

    /// <summary>Logo-pack crest: the image fitted into the crest box (150x175, bottom-left), with the same shadow.</summary>
    private static bool PackCrest(SKCanvas c, CrestSpec t)
    {
        if (t.LogoFile is not { } file || !File.Exists(file) || Logo(file) is not { } bmp || bmp.Width == 0 || bmp.Height == 0) return false;
        var box = new SKRect(22, W - 22 - 175, 22 + 150, W - 22);
        float scale = Math.Min(box.Width / bmp.Width, box.Height / bmp.Height);
        float w = bmp.Width * scale, h = bmp.Height * scale;
        var dest = new SKRect(box.MidX - w / 2, box.Bottom - h, box.MidX + w / 2, box.Bottom);
        using var layer = new SKPaint { ImageFilter = SKImageFilter.CreateDropShadow(0, 4, 3, 3, new SKColor(0, 0, 0, A(.55f))) };
        c.SaveLayer(layer);
        using (var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
            c.DrawBitmap(bmp, dest, p);
        c.Restore();
        return true;
    }

    private static void Crest(SKCanvas c, CrestSpec t, SKTypeface bs)
    {
        if (PackCrest(c, t)) return;
        // <svg class="crest" viewBox="0 0 48 56"> at left 22, bottom 22, 150x175, using a <symbol viewBox="-2 -2 52 60">
        // (meet: scale 48/52, centred vertically), with drop-shadow(0 4px 6px rgba(0,0,0,.55)).
        const float meet = 48f / 52f, outer = 150f / 48f, k = meet * outer;
        float offY = (56 - 60 * meet) / 2 * outer;
        using var layer = new SKPaint { ImageFilter = SKImageFilter.CreateDropShadow(0, 4, 3, 3, new SKColor(0, 0, 0, A(.55f))) };
        c.SaveLayer(layer);
        c.Translate(22 + 2 * k, (W - 22 - 175) + offY + 2 * k);
        c.Scale(k);

        const float s = 48, cx = 24, cy = 28;
        using var shape = CrestShape(t.LogoStyle, s, cx, cy);
        var pc = Hex(t.PrimaryColor);
        var dark = new SKColor((byte)(int)(pc.Red * .55), (byte)(int)(pc.Green * .55), (byte)(int)(pc.Blue * .55));
        using (var p = new SKPaint { IsAntialias = true, Color = pc })
        {
            c.DrawPath(shape, p);
            p.Style = SKPaintStyle.Stroke; p.StrokeWidth = 2.2f; p.Color = dark; p.StrokeJoin = SKStrokeJoin.Miter;
            c.DrawPath(shape, p);
        }
        c.Save();
        c.ClipPath(shape, SKClipOperation.Intersect, true);
        using (var p = new SKPaint { IsAntialias = true, Color = Hex(t.SecondaryColor) })
        {
            using var sash = Poly(cx - s * .6f, cy + s * .05f, cx + s * .6f, cy - s * .35f, cx + s * .6f, cy - s * .15f, cx - s * .6f, cy + s * .25f);
            c.DrawPath(sash, p);
            var b = shape.Bounds;
            p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, b.Top), new SKPoint(0, b.Bottom), new[] { White(.27f), White(0) }, new[] { 0f, .6f }, SKShaderTileMode.Clamp);
            c.DrawPath(shape, p);
        }
        c.Restore();
        using (var font = new SKFont(bs, 15) { Subpixel = true, Edging = SKFontEdging.Antialias })
        using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, A(.6f)) })
        {
            float w = Measure(font, t.Abbreviation);
            c.DrawText(t.Abbreviation, cx + 1 - w / 2, cy + 6, font, p);
            p.Color = SKColors.White;
            c.DrawText(t.Abbreviation, cx - w / 2, cy + 5, font, p);
        }
        c.Restore();
    }

    private static SKPath CrestShape(int style, float s, float cx, float cy)
    {
        var path = new SKPath();
        switch (style)
        {
            case 1:
                path.AddCircle(cx, cy, s / 2);
                break;
            case 2:
            case 3:
                int n = style == 2 ? 6 : 4;
                for (int i = 0; i < n; i++)
                {
                    double a = -Math.PI / 2 + i * 2 * Math.PI / n;
                    var pt = new SKPoint(cx + s / 2 * (float)Math.Cos(a), cy + s / 2 * (float)Math.Sin(a));
                    if (i == 0) path.MoveTo(pt); else path.LineTo(pt);
                }
                path.Close();
                break;
            case 4:
            {
                float w = s * .5f, top = cy - s * .46f, bot = cy + s * .5f;
                path.MoveTo(cx - w, top); path.LineTo(cx + w, top); path.LineTo(cx + w, bot); path.LineTo(cx, bot - s * .18f); path.LineTo(cx - w, bot);
                path.Close();
                break;
            }
            default:
            {
                float w = s * .5f, top = cy - s * .48f, bot = cy + s * .5f;
                path.MoveTo(cx - w, top); path.LineTo(cx + w, top); path.LineTo(cx + w, cy + s * .05f);
                path.QuadTo(cx + w, cy + s * .38f, cx, bot);
                path.QuadTo(cx - w, cy + s * .38f, cx - w, cy + s * .05f);
                path.Close();
                break;
            }
        }
        return path;
    }

    // ---- helpers ------------------------------------------------------------------------------
    private static SKPath HexPath(SKRect r) => Poly(r, (.5f, 0), (1, .25f), (1, .75f), (.5f, 1), (0, .75f), (0, .25f));

    private static SKPath Poly(SKRect r, params (float x, float y)[] pts)
    {
        var path = new SKPath();
        for (int i = 0; i < pts.Length; i++)
        {
            var pt = new SKPoint(r.Left + pts[i].x * r.Width, r.Top + pts[i].y * r.Height);
            if (i == 0) path.MoveTo(pt); else path.LineTo(pt);
        }
        path.Close();
        return path;
    }

    private static SKPath Poly(params float[] xy)
    {
        var path = new SKPath();
        path.MoveTo(xy[0], xy[1]);
        for (int i = 2; i < xy.Length; i += 2) path.LineTo(xy[i], xy[i + 1]);
        path.Close();
        return path;
    }

    /// <summary>CSS gradient direction for an angle (0deg = to top, clockwise).</summary>
    private static SKPoint Dir(float deg)
    {
        var a = deg * MathF.PI / 180;
        return new SKPoint(MathF.Sin(a), -MathF.Cos(a));
    }

    /// <summary>CSS linear-gradient(angle, stops) over a box: the gradient line passes through the centre and spans the box's corners.</summary>
    private static SKShader Linear(SKRect box, float deg, params (float pos, SKColor color)[] stops)
    {
        var dir = Dir(deg);
        float len = MathF.Abs(box.Width * dir.X) + MathF.Abs(box.Height * dir.Y);
        var start = new SKPoint(box.MidX - dir.X * len / 2, box.MidY - dir.Y * len / 2);
        var end = new SKPoint(box.MidX + dir.X * len / 2, box.MidY + dir.Y * len / 2);
        return SKShader.CreateLinearGradient(start, end, stops.Select(s => s.color).ToArray(), stops.Select(s => s.pos).ToArray(), SKShaderTileMode.Clamp);
    }

    /// <summary>CSS conic-gradient(from deg at centre, evenly spaced colours). CSS 0deg is up; Skia's sweep starts at +x.</summary>
    private static SKShader Conic(SKPoint centre, float fromDeg, params SKColor[] colors)
    {
        var pos = Enumerable.Range(0, colors.Length).Select(i => i / (float)(colors.Length - 1)).ToArray();
        var m = SKMatrix.CreateRotationDegrees(fromDeg - 90, centre.X, centre.Y);
        return SKShader.CreateSweepGradient(centre, colors, pos, m);
    }

    /// <summary>A line with line-height: normal, as Chromium lays it out: ascent and descent each rounded to whole pixels.
    /// Returns the baseline offset from the line's top and the line's height. (Both card fonts have no line gap.)</summary>
    private static (float ascent, float height) LineBox(SKFont font)
    {
        var m = font.Metrics;
        float asc = MathF.Round(-m.Ascent), desc = MathF.Round(m.Descent);
        return (asc, asc + desc + MathF.Round(m.Leading));
    }

    /// <summary>Width with CSS letter-spacing (added after every character, the last one included).</summary>
    private static float SpacedWidth(string text, SKFont font, float spacing) =>
        text.Sum(ch => Measure(font, ch.ToString())) + spacing * text.Length;

    private static void Spaced(SKCanvas c, string text, float centreX, float baseline, SKFont font, float spacing, SKPaint p) =>
        DrawSpacedFrom(c, text, centreX - SpacedWidth(text, font, spacing) / 2, baseline, font, spacing, p);

    private static void DrawSpacedFrom(SKCanvas c, string text, float x, float baseline, SKFont font, float spacing, SKPaint p)
    {
        foreach (var ch in text)
        {
            var s = ch.ToString();
            c.DrawText(s, x, baseline, font, p);
            x += Measure(font, s) + spacing;
        }
    }

    private static float Measure(SKFont font, string text)
    {
        using var paint = new SKPaint { Typeface = font.Typeface, TextSize = font.Size, SubpixelText = true };
        return paint.MeasureText(text);
    }

    private static SKColor Hex(string hex) => SKColor.Parse(hex);
    private static SKColor White(float a) => new(255, 255, 255, A(a));
    private static byte A(float a) => (byte)Math.Round(a * 255);
}
