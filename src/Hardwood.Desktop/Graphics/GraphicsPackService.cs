using Avalonia.Media.Imaging;
using Hardwood.Core.Graphics;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.Graphics;

/// <summary>
/// Danny's graphics packs (<c>prographics</c>, <c>collegegraphics</c>) read from his own folders when the game starts:
/// NBA and college logos, banners, jerseys, player photos and trophies (decision #350). Nothing is shipped with the game.
/// </summary>
public static class GraphicsPackService
{
    private static GraphicsPack? _pack;
    private static readonly Dictionary<string, Bitmap?> Bitmaps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The pack under the face folder, the logo folder or Documents\Hardwood Manager (first found).</summary>
    public static GraphicsPack Pack => _pack ??= AppSettings.Current.UseGraphicsPack
        ? GraphicsPack.Find(Environment.GetEnvironmentVariable("HW_GRAPHICS_PACK"), AppSettings.Current.FaceFolder, AppSettings.Current.LogoFolder,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hardwood Manager"))
        : GraphicsPack.Empty;

    public static void Reload()
    {
        _pack = null;
        foreach (var b in Bitmaps.Values) b?.Dispose();
        Bitmaps.Clear();
    }

    public static Bitmap? Load(string? file)
    {
        if (file == null) return null;
        if (Bitmaps.TryGetValue(file, out var b)) return b;
        try { b = new Bitmap(file); } catch (Exception) { b = null; }
        return Bitmaps[file] = b;
    }

    public static string? LogoFor(Team t) => Pack.LogoFor(t);
    /// <summary>The home program's court from the college pack, for the live game's scoreboard (Danny, run 45).</summary>
    public static Bitmap? CourtFor(Team? t) => t == null ? null : Load(Pack.CourtFor(t));
    public static Bitmap? BannerFor(Team? t) => t == null ? null : Load(Pack.BannerFor(t));
    public static IReadOnlyList<Bitmap> JerseysFor(Team? t) =>
        t == null ? Array.Empty<Bitmap>() : Pack.JerseysFor(t).Select(Load).Where(b => b != null).Cast<Bitmap>().ToList();

    /// <summary>A real player's photo, when photos are switched on.</summary>
    public static string? PhotoFor(Player p) => AppSettings.Current.RealPlayerPhotos ? Pack.PhotoFor(p) : null;
}
