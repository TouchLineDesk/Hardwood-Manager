using Avalonia.Media.Imaging;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// Optional rendered portraits (player cards) stored next to the save:
/// <c>&lt;save folder&gt;\&lt;save name&gt;-portraits\&lt;playerId&gt;.png</c>, e.g.
/// <c>Documents\Hardwood Manager\career-portraits\123.png</c>.
/// When a file exists the app shows it; otherwise the procedural avatar is drawn.
/// The game draws these itself (see <see cref="Hardwood.Desktop.Portraits.PortraitService"/>); as each batch is
/// written, <see cref="Invalidate"/> drops those players from the cache and on-screen avatars and cards redraw.
/// </summary>
public static class Portraits
{
    private static readonly Dictionary<int, Bitmap?> Cache = new();

    public static string? Folder { get; private set; }

    /// <summary>Raised (on the UI thread) when portraits on disk changed; avatars and cards redraw.</summary>
    public static event Action? Changed;

    /// <summary>Folder used for a save path (created on demand by the card pipeline, not by the app).</summary>
    public static string FolderFor(string savePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(savePath)) ?? ".", Path.GetFileNameWithoutExtension(savePath) + "-portraits");

    public static void UseSave(string? savePath)
    {
        Folder = savePath == null ? null : FolderFor(savePath);
        Reload();
    }

    /// <summary>Drop cached images so changed files on disk are read again.</summary>
    public static void Reload()
    {
        foreach (var bmp in Cache.Values) bmp?.Dispose();
        Cache.Clear();
    }

    /// <summary>Drop these players' cached images (their files were just redrawn) and tell the UI to redraw.</summary>
    public static void Invalidate(IEnumerable<int> playerIds)
    {
        bool any = false;
        foreach (var id in playerIds)
        {
            any = true;
            if (Cache.Remove(id, out var bmp)) bmp?.Dispose();
        }
        if (any) Changed?.Invoke();
    }

    public static Bitmap? Get(int playerId)
    {
        if (Folder == null) return null;
        if (Cache.TryGetValue(playerId, out var cached)) return cached;
        Bitmap? bmp = null;
        try
        {
            var file = Path.Combine(Folder, playerId + ".png");
            if (File.Exists(file))
            {
                using var fs = File.OpenRead(file);
                bmp = new Bitmap(fs);
            }
        }
        catch (Exception)
        {
            bmp = null; // a half-written or corrupt file falls back to the avatar
        }
        Cache[playerId] = bmp;
        return bmp;
    }
}
