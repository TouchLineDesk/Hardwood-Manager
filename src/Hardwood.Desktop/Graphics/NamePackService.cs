using Hardwood.Core.Names;

namespace Hardwood.Desktop.Graphics;

/// <summary>
/// The name pack chosen in settings (a real-names file the player keeps in Documents\Hardwood Manager\Names). Applied to
/// the competitions and the top league at launch, to a new career's clubs when it starts, and to the current career on
/// request. The game itself ships with fictional names only.
/// </summary>
public static class NamePackService
{
    public static string DefaultFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Hardwood Manager", "Names");

    /// <summary>The file in use: the setting if it points at a file, else names.json or the first .json in the folder.</summary>
    public static string? File()
    {
        var setting = AppSettings.Current.NamePackFile;
        if (!string.IsNullOrWhiteSpace(setting))
        {
            if (System.IO.File.Exists(setting)) return setting;
            if (Directory.Exists(setting)) return FirstIn(setting);
        }
        return FirstIn(DefaultFolder());
    }

    private static string? FirstIn(string folder)
    {
        if (!Directory.Exists(folder)) return null;
        var named = Path.Combine(folder, "names.json");
        if (System.IO.File.Exists(named)) return named;
        return Directory.EnumerateFiles(folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    /// <summary>The current pack, or null (with the reason in <paramref name="status"/>).</summary>
    public static NamePack? Current(out string status)
    {
        var file = File();
        if (file == null) { status = $"No name pack. Put a names file (.json) in {DefaultFolder()} to rename clubs, leagues and conferences."; return null; }
        var pack = NamePack.Load(file, out var error);
        status = pack == null ? error ?? "Couldn't read the name pack." : $"{pack.Name}: {pack.Count} names ({Path.GetFileName(file)}).";
        return pack;
    }

    /// <summary>At launch (and after the setting changes): rename the competitions and the top league, or restore the fictional names.</summary>
    public static void ApplyStatic()
    {
        if (Current(out _) is { } pack) NamePack.ApplyStatic(pack);
        else NamePack.ResetStatic();
    }
}
