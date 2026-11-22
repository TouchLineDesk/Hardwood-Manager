using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;

namespace Hardwood.Desktop.Graphics;

/// <summary>
/// The pre-rendered broadcast sets (B1, run 139; #984): Blender frames built by <c>tools/Render/</c>, shipped as embedded
/// JPEGs, each with a JSON of where its big screen and presenters sit (in the frame's own 1920 × 1080 pixels).
/// </summary>
public sealed class BroadcastSet
{
    public required Bitmap? Frame { get; init; }
    public Size Size { get; init; } = new(1920, 1080);
    /// <summary>The big screen's bounding box.</summary>
    public Rect Screen { get; init; }
    public Point HostHead { get; init; }
    public Point AnalystHead { get; init; }
    /// <summary>The arena (B2a, run 140): the court's corners for feet (0,0), (94,0), (94,50), (0,50), in frame pixels.</summary>
    public Point[]? Court { get; init; }
    /// <summary>The arena's tint mask (white where the crowd's team shirts and the ribbon boards take the home colours).</summary>
    public Bitmap? Mask { get; init; }
    /// <summary>The draft stage (B3, run 142): the representatives' crest cards, picks 1–4 from the left, as rectangles.</summary>
    public Rect[] Cards { get; init; } = Array.Empty<Rect>();
}

public static class BroadcastArt
{
    private static BroadcastSet? _selection, _draft;

    /// <summary>The draft-night stage (B3, run 142; <c>tools/Render/draft_stage.py</c>): the big screen, the podium (its
    /// official is the "host" head) and four representatives behind blank crest cards.</summary>
    public static BroadcastSet Draft => _draft ??= Load("draft-stage");

    /// <summary>The Selection Show studio. Without the frame (a stripped build), the layout still works on a plain backdrop.</summary>
    public static BroadcastSet Selection => _selection ??= Load("selection-studio");

    public static BroadcastSet Load(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        Stream? Res(string ext)
        {
            var n = asm.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(name + ext, StringComparison.Ordinal));
            return n == null ? null : asm.GetManifestResourceStream(n);
        }
        Bitmap? frame = null, mask = null;
        try { using var s = Res(".jpg"); if (s != null) frame = new Bitmap(s); } catch { frame = null; }
        try { using var s = Res("-mask.png"); if (s != null) mask = new Bitmap(s); } catch { mask = null; }
        Point[]? court = null;
        var cards = new List<Rect>();
        var screen = new Rect(492, 192, 939, 422);
        Point host = new(758, 600), analyst = new(1162, 610);
        try
        {
            using var js = Res(".json");
            if (js != null)
            {
                using var doc = JsonDocument.Parse(js);
                var root = doc.RootElement;
                static Point Pt(JsonElement p) => new(p[0].GetDouble(), p[1].GetDouble());
                if (root.TryGetProperty("court", out var c)) court = c.EnumerateArray().Select(Pt).ToArray();
                if (root.TryGetProperty("screen", out var sc))
                {
                    var pts = sc.EnumerateArray().Select(Pt).ToList();
                    double l = pts.Min(p => p.X), t = pts.Min(p => p.Y), r = pts.Max(p => p.X), b = pts.Max(p => p.Y);
                    screen = new Rect(l, t, r - l, b - t);
                }
                if (root.TryGetProperty("host_head", out var h)) host = Pt(h);
                if (root.TryGetProperty("cards", out var cs))
                    foreach (var card in cs.EnumerateArray())
                    {
                        var pts = card.EnumerateArray().Select(Pt).ToList();
                        double l = pts.Min(p => p.X), t = pts.Min(p => p.Y);
                        cards.Add(new Rect(l, t, pts.Max(p => p.X) - l, pts.Max(p => p.Y) - t));
                    }
                if (root.TryGetProperty("analyst_head", out var a)) analyst = Pt(a);
            }
        }
        catch (Exception) { /* keep the defaults */ }
        return new BroadcastSet { Frame = frame, Screen = screen, HostHead = host, AnalystHead = analyst, Court = court is { Length: 4 } ? court : null, Mask = mask, Cards = cards.ToArray() };
    }
}
