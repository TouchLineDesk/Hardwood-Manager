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
}

public static class BroadcastArt
{
    private static BroadcastSet? _selection;

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
        Bitmap? frame = null;
        try { using var s = Res(".jpg"); if (s != null) frame = new Bitmap(s); } catch { frame = null; }
        var screen = new Rect(492, 192, 939, 422);
        Point host = new(758, 600), analyst = new(1162, 610);
        try
        {
            using var js = Res(".json");
            if (js != null)
            {
                using var doc = JsonDocument.Parse(js);
                var root = doc.RootElement;
                var pts = root.GetProperty("screen").EnumerateArray().Select(p => new Point(p[0].GetDouble(), p[1].GetDouble())).ToList();
                double l = pts.Min(p => p.X), t = pts.Min(p => p.Y), r = pts.Max(p => p.X), b = pts.Max(p => p.Y);
                screen = new Rect(l, t, r - l, b - t);
                Point P(string k) { var a = root.GetProperty(k); return new Point(a[0].GetDouble(), a[1].GetDouble()); }
                host = P("host_head"); analyst = P("analyst_head");
            }
        }
        catch (Exception) { /* keep the defaults */ }
        return new BroadcastSet { Frame = frame, Screen = screen, HostHead = host, AnalystHead = analyst };
    }
}
