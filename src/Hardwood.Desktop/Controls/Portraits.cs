using Avalonia.Media.Imaging;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// Optional rendered portraits (player cards) stored next to the save:
/// <c>&lt;save folder&gt;\&lt;save name&gt;-portraits\&lt;playerId&gt;.png</c>, e.g.
/// <c>Documents\Hardwood Manager\career-portraits\123.png</c>.
/// When a file exists the app shows it; otherwise the procedural avatar is drawn.
/// The cache is cleared on every page change, so new or rebuilt portraits appear without a restart.
/// </summary>
public static class Portraits
{
    private static readonly Dictionary<int, Bitmap?> Cache = new();

    public static string? Folder { get; private set; }

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
