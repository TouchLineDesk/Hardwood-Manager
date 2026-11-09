using System.Reflection;
using Hardwood.Core.Portraits;
using SkiaSharp;

namespace Hardwood.Desktop.Portraits;

/// <summary>
/// Draws the approved 2K-style player card (claude/kit/player_card.py, card-design.md) natively with SkiaSharp,
/// so the game can make its own portraits without a browser. Every measurement below is the CSS of the
/// original template translated to a 512x512 canvas; the result is scaled to 256x256 like the kit's output.
/// Thread-safe: each call uses its own surface, so cards can be rendered on a background thread.
/// </summary>
public static class CardRenderer
{
    /// <summary>Bump when the card's look changes, so every portrait is redrawn once.</summary>
    public const int Version = 1;

    private const float W = 512f;
    private static readonly Lazy<(SKTypeface bs, SKTypeface bc)> Fonts = new(LoadFonts);

    private static (SKTypeface, SKTypeface) LoadFonts()
    {
        static SKTypeface Load(string name)
        {
            var asm = Assembly.GetExecutingAssembly();
            var res = asm.GetManifestResourceNames().First(n => n.EndsWith(name, StringComparison.Ordinal));
            using var s = asm.GetManifestResourceStream(res)!;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return SKTypeface.FromData(SKData.CreateCopy(ms.ToArray()));
        }
        return (Load("BigShoulders-900.ttf"), Load("BarlowC-800.ttf"));
    }

    /// <summary>Render a card and write it as a PNG (atomically: a temp file, then a rename).</summary>
    public static void RenderToFile(PortraitCard card, string faceFullPath, string outFile, int size = 256)
    {
        using var face = SKBitmap.Decode(faceFullPath) ?? throw new InvalidDataException($"Can't read face {faceFullPath}");
        using var img = Render(card, face, size);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
        var tmp = outFile + ".tmp";
        using (var fs = File.Create(tmp)) data.SaveTo(fs);
        File.Move(tmp, outFile, overwrite: true);
    }

    /// <summary>Render a card to a square image of the given size (the card is drawn at 512 and scaled down).</summary>
    public static SKImage Render(PortraitCard card, SKBitmap face, int size = 256)
    {
        using var full = SKSurface.Create(new SKImageInfo(512, 512, SKColorType.Rgba8888, SKAlphaType.Premul));
        Draw(full.Canvas, card, face);
        var big = full.Snapshot();
        if (size == 512) return big;
        using (big)
        using (var bmp = SKBitmap.FromImage(big))
        using (var small = bmp.Resize(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.High))
            return SKImage.FromPixelCopy(small.Info, small.GetPixels(), small.RowBytes);
    }

    // ------------------------------------------------------------------------------------------
    private static void Draw(SKCanvas c, PortraitCard card, SKBitmap face)
    {
        var (bs, bc) = Fonts.Value;
        var ct = PortraitTiers.Body(card.CurrentAbility);
        bool dia = card.PotentialAbility >= PortraitTiers.Diamond.Lo;
        var pt = PortraitTiers.Badge(card.PotentialAbility);
        SKColor cd = Hex(ct.Dark), cb = Hex(ct.Base), cl = Hex(ct.Light);
        SKColor pd = Hex(pt.Dark), pb = Hex(pt.Base), pl = Hex(pt.Light), pi = Hex(pt.Ink);

        c.Clear(SKColors.Transparent);
        c.Save();
        c.ClipRoundRect(new SKRoundRect(new SKRect(0, 0, W, W), 40), SKClipOperation.Intersect, true);

        // .bg  radial-gradient(120% 90% at 70% 30%, cl 0%, cb 45%, cd 100%)
        using (var p = new SKPaint { IsAntialias = true })
        {
            var m = SKMatrix.CreateScale(614.4f, 460.8f).PostConcat(SKMatrix.CreateTranslation(358.4f, 153.6f));
            p.Shader = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1, new[] { cl, cb, cd }, new[] { 0f, .45f, 1f }, SKShaderTileMode.Clamp, m);
            c.DrawRect(0, 0, W, W, p);
        }

        // .s1 .s2 .s3 shards
        Shard(c, 0.55f, Linear(new SKRect(0, 0, W, W), 160, (0, cl), (.70f, cl.WithAlpha(0))), (.38f, 0), (.78f, 0), (.52f, 1), (.18f, 1));
        Shard(c, 0.35f, Linear(new SKRect(0, 0, W, W), 200, (0, White(.5f)), (.60f, White(0))), (.70f, 0), (1, 0), (1, .45f), (.58f, 1), (.44f, 1));
        Shard(c, 0.70f, Linear(new SKRect(0, 0, W, W), 0, (0, cd), (1, cd.WithAlpha(0))), (0, .60f), (.30f, 1), (0, 1));

        // .sheen  inset:-20%, two white bands, mix-blend-mode: screen
        using (var p = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Screen })
        {
            var box = new SKRect(-102.4f, -102.4f, 614.4f, 614.4f);
            p.Shader = Linear(box, 115, (0, White(0)), (.30f, White(0)), (.34f, White(.5f)), (.40f, White(0)), (.52f, White(0)), (.55f, White(.22f)), (.60f, White(0)), (1, White(0)));
            c.DrawRect(box, p);
        }

        // .lines  opacity .18; repeating-linear-gradient(135deg, white .55 0 2px, transparent 2px 14px)
        using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, A(.18f)) })
        {
            var dir = Dir(135);
            float len = W * (MathF.Abs(dir.X) + MathF.Abs(dir.Y));
            var start = new SKPoint(W / 2 - dir.X * len / 2, W / 2 - dir.Y * len / 2);
            var end = new SKPoint(start.X + dir.X * 14, start.Y + dir.Y * 14);
            p.Shader = SKShader.CreateLinearGradient(start, end, new[] { White(.55f), White(.55f), White(0), White(0) }, new[] { 0f, 2f / 14, 2f / 14, 1f }, SKShaderTileMode.Repeat);
            c.DrawRect(0, 0, W, W, p);
        }

        // .glow  360px circle at (170,40); radial-gradient(circle, white .55, transparent 65%) (farthest-corner radius)
        using (var p = new SKPaint { IsAntialias = true })
        {
            var ctr = new SKPoint(350, 220);
            p.Shader = SKShader.CreateRadialGradient(ctr, 180 * MathF.Sqrt(2), new[] { White(.55f), White(0) }, new[] { 0f, .65f }, SKShaderTileMode.Clamp);
            c.DrawCircle(ctr, 180, p);
        }

        // .face  430x430 at right:-44 bottom:-6, drop-shadow(0 10px 16px rgba(0,0,0,.45))
        using (var resized = face.Resize(new SKImageInfo(430, 430, SKColorType.Rgba8888, SKAlphaType.Premul), SKFilterQuality.High))
        using (var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
        {
            p.ImageFilter = SKImageFilter.CreateDropShadow(0, 10, 8, 8, new SKColor(0, 0, 0, A(.45f)));
            c.DrawBitmap(resized ?? face, new SKRect(126, 88, 556, 518), p);
        }

        // .shade  bottom 150px, linear-gradient(0deg, rgba(0,0,0,.45), transparent)
        using (var p = new SKPaint { IsAntialias = true })
        {
            p.Shader = SKShader.CreateLinearGradient(new SKPoint(0, W), new SKPoint(0, W - 150), new[] { new SKColor(0, 0, 0, A(.45f)), new SKColor(0, 0, 0, 0) }, null, SKShaderTileMode.Clamp);
            c.DrawRect(0, W - 150, W, 150, p);
        }

        if (!dia) Badge(c, card.PotentialAbility, false, pd, pb, pl, pi, bs, bc);
        if (card.Draft is { } d) DraftStamp(c, $"{d.Year} · R{d.Round} P{d.Pick}", pl, bc);
        if (card.Crest is { } crest) Crest(c, crest, bs);
        Edge(c, cb);

        if (dia)
        {
            Halo(c);
            Badge(c, card.PotentialAbility, true, pd, pb, pl, pi, bs, bc);
            Sparks(c);
        }
        c.Restore();
    }

    // ---- badges -------------------------------------------------------------------------------
    private static void Badge(SKCanvas c, int pa, bool dia, SKColor pd, SKColor pb, SKColor pl, SKColor pi, SKTypeface bs, SKTypeface bc)
    {
        // .badges: left 22, top 22. The hex is wider than the 132px container, so the grid track grows to the hex
        // and the hex sits at left 22; the DIAMOND tag is centred on that track (measured in Chromium).
        const float top = 22;
        float hw = dia ? 146 : 136, hh = dia ? 168 : 156, iw = dia ? 128 : 120, ih = dia ? 150 : 140;
        float cx = 22 + hw / 2;
        var outer = new SKRect(cx - hw / 2, top, cx + hw / 2, top + hh);
        var inner = new SKRect(cx - iw / 2, top + (hh - ih) / 2, cx + iw / 2, top + (hh - ih) / 2 + ih);
        using var outerPath = HexPath(outer);
        using var innerPath = HexPath(inner);

        using (var p = new SKPaint { IsAntialias = true })
        {
            p.Shader = dia
                ? Conic(new SKPoint(outer.MidX, outer.MidY), 20, Hex("#FF7AD9"), Hex("#FFD36E"), Hex("#8CFFB4"), Hex("#6EE7FF"), Hex("#9B8CFF"), Hex("#FF7AD9"))
                : Linear(outer, 160, (0, SKColors.White), (.25f, pl), (.70f, pd), (1, Hex("#05080D")));
            c.DrawPath(outerPath, p);
        }
        using (var p = new SKPaint { IsAntialias = true })
        {
            if (dia)
            {
                p.Shader = Conic(new SKPoint(inner.Left + inner.Width * .40f, inner.Top + inner.Height * .45f), 200,
                    Hex("#BFEFFF"), Hex("#FFB8E8"), Hex("#FFE89A"), Hex("#B8FFD6"), Hex("#B8CCFF"), Hex("#E2B8FF"), Hex("#BFEFFF"));
                c.DrawPath(innerPath, p);
                p.Shader = Linear(inner, 200, (.55f, White(0)), (.66f, White(.55f)), (.76f, White(0)));
                c.DrawPath(innerPath, p);
                p.Shader = Linear(inner, 125, (.20f, White(0)), (.38f, White(.85f)), (.52f, White(0)));
                c.DrawPath(innerPath, p);
            }
            else
            {
                p.Shader = Linear(inner, 160, (0, pb), (1, pd));
                c.DrawPath(innerPath, p);
            }
        }

        // Number (Big Shoulders 900 74px) and "POT" (Barlow Condensed 800 20px, letter-spacing .22em, margin-top 6),
        // stacked and centred vertically in the inner hex. The CSS `font` shorthand resets line-height to normal,
        // so the .82 on .in never applies: each line is the font's (rounded) ascent + descent.
        using var numFont = new SKFont(bs, 74) { Subpixel = true, Edging = SKFontEdging.Antialias };
        using var potFont = new SKFont(bc, 20) { Subpixel = true, Edging = SKFontEdging.Antialias };
        var (numAsc, numLh) = LineBox(numFont);
        var (potAsc, potLh) = LineBox(potFont);
        float stackTop = inner.Top + (ih - (numLh + 6 + potLh)) / 2;
        float numBase = stackTop + numAsc;
        float potBase = stackTop + numLh + 6 + potAsc;
        var num = pa.ToString();
        float numX = cx - Measure(numFont, num) / 2;
        var ink = dia ? Hex("#0A1E30") : pi;
        using (var p = new SKPaint { IsAntialias = true })
        {
            if (dia)
            {
                p.Color = White(.8f);
                p.ImageFilter = SKImageFilter.CreateBlur(5, 5);
                c.DrawText(num, numX, numBase, numFont, p);
                p.ImageFilter = null;
                p.Color = White(.9f);
                c.DrawText(num, numX, numBase + 1, numFont, p);
            }
            else
            {
                p.Color = new SKColor(0, 0, 0, A(.25f));
                c.DrawText(num, numX, numBase + 2, numFont, p);
            }
            p.Color = ink;
            c.DrawText(num, numX, numBase, numFont, p);
            p.Color = dia ? ink : ink.WithAlpha((byte)(ink.Alpha * .9f));
            Spaced(c, "POT", cx, potBase, potFont, 20 * .22f, p);
        }

        if (dia) DiamondTag(c, cx, top + hh + 10, bc);
    }

    private static void DiamondTag(SKCanvas c, float cx, float top, SKTypeface bc)
    {
        using var font = new SKFont(bc, 17) { Subpixel = true, Edging = SKFontEdging.Antialias };
        const string text = "DIAMOND";
        float spacing = 17 * .22f;
        float tw = SpacedWidth(text, font, spacing);
        var (asc, lh) = LineBox(font);
        float h = lh + 4;
        var box = new SKRect(cx - (tw + 24) / 2, top, cx + (tw + 24) / 2, top + h);
        using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(160, 210, 255, A(.9f)) })
        {
            p.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 7);
            c.DrawRoundRect(box, 4, 4, p);
        }
        using (var p = new SKPaint { IsAntialias = true, Color = SKColors.White })
        {
            var ring = box; ring.Inflate(2, 2);
            c.DrawRoundRect(ring, 6, 6, p);
            p.Shader = Linear(box, 90, (0, Hex("#FFD9F2")), (.25f, Hex("#FFF6CC")), (.5f, Hex("#D9FFE9")), (.75f, Hex("#D6E4FF")), (1, Hex("#F1D9FF")));
            c.DrawRoundRect(box, 4, 4, p);
        }
        using (var p = new SKPaint { IsAntialias = true, Color = Hex("#0A1E30") })
        {
            float x = box.Left + 12;
            DrawSpacedFrom(c, text, x, top + 2 + asc, font, spacing, p);
        }
    }

    private static void Halo(SKCanvas c)
    {
        // .halo: 240x260 ellipse at (-30,-34), radial-gradient(closest-side, ...), blur(2px)
        var box = new SKRect(-30, -34, 210, 226);
        using var p = new SKPaint { IsAntialias = true };
        var m = SKMatrix.CreateScale(120, 130).PostConcat(SKMatrix.CreateTranslation(box.MidX, box.MidY));
        p.Shader = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1,
            new[] { White(.95f), new SKColor(180, 230, 255, A(.75f)), new SKColor(255, 150, 230, A(.45f)), new SKColor(255, 230, 120, A(.25f)), White(0) },
            new[] { 0f, .35f, .55f, .70f, 1f }, SKShaderTileMode.Clamp, m);
        p.ImageFilter = SKImageFilter.CreateBlur(2, 2);
        c.DrawOval(box, p);
    }

    private static void Sparks(SKCanvas c)
    {
        // Four-point stars inside .spark (left/top 12). Their CSS drop-shadows are clipped away by the clip-path, so plain white.
        (float x, float y, float z)[] stars = { (4, 10, 30), (150, 22, 22), (0, 150, 18), (156, 146, 26), (90, 0, 14), (134, 92, 12), (22, 96, 10), (170, 80, 9) };
        using var p = new SKPaint { IsAntialias = true, Color = SKColors.White };
        foreach (var (x, y, z) in stars)
        {
            using var path = Poly(new SKRect(12 + x, 12 + y, 12 + x + z, 12 + y + z), (.5f, 0), (.6f, .4f), (1, .5f), (.6f, .6f), (.5f, 1), (.4f, .6f), (0, .5f), (.4f, .4f));
            c.DrawPath(path, p);
        }
    }

    // ---- draft stamp, crest, edge -------------------------------------------------------------
    private static void DraftStamp(SKCanvas c, string text, SKColor border, SKTypeface bc)
    {
        // .draft: right 18, top 20; 800 22px; padding 3px 10px; border 3px; radius 8; letter-spacing .04em
        using var font = new SKFont(bc, 22) { Subpixel = true, Edging = SKFontEdging.Antialias };
        float spacing = 22 * .04f;
        float tw = SpacedWidth(text, font, spacing);
        var (asc, lh) = LineBox(font);
        var box = new SKRect(W - 18 - (tw + 26), 20, W - 18, 20 + lh + 12);
        using (var p = new SKPaint { IsAntialias = true, Color = new SKColor(8, 12, 18, A(.85f)) })
            c.DrawRoundRect(box, 8, 8, p);
        using (var p = new SKPaint { IsAntialias = true, Color = border, Style = SKPaintStyle.Stroke, StrokeWidth = 3 })
        {
            var r = box; r.Inflate(-1.5f, -1.5f);
            c.DrawRoundRect(r, 6.5f, 6.5f, p);
        }
        using (var p = new SKPaint { IsAntialias = true, Color = SKColors.White })
            DrawSpacedFrom(c, text, box.Left + 13, box.Top + 6 + asc, font, spacing, p);
    }

    private static void Crest(SKCanvas c, CrestSpec t, SKTypeface bs)
    {
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

    private static void Edge(SKCanvas c, SKColor cb)
    {
        // box-shadow: inset 0 0 0 6px cb, inset 0 0 0 8px white .55, inset 0 0 0 12px black .25 (last painted first)
        Ring(c, 12, new SKColor(0, 0, 0, A(.25f)));
        Ring(c, 8, White(.55f));
        Ring(c, 6, cb);
    }

    private static void Ring(SKCanvas c, float spread, SKColor color)
    {
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.AddRoundRect(new SKRect(0, 0, W, W), 40, 40);
        path.AddRoundRect(new SKRect(spread, spread, W - spread, W - spread), 40 - spread, 40 - spread);
        using var p = new SKPaint { IsAntialias = true, Color = color };
        c.DrawPath(path, p);
    }

    // ---- helpers ------------------------------------------------------------------------------
    private static void Shard(SKCanvas c, float opacity, SKShader shader, params (float x, float y)[] poly)
    {
        using var path = Poly(new SKRect(0, 0, W, W), poly);
        using var p = new SKPaint { IsAntialias = true, Shader = shader, Color = new SKColor(0, 0, 0, A(opacity)) };
        c.DrawPath(path, p);
    }

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
