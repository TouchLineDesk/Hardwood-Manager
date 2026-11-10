using Avalonia.Media.Imaging;
using Hardwood.Core.Graphics;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.Graphics;

/// <summary>
/// The logo pack chosen in settings: finds a club's image and keeps the decoded bitmaps.
/// Clubs without a matching file keep the drawn crest.
/// </summary>
public static class LogoPackService
{
    private static LogoPack? _pack;
    private static string? _root;
    private static readonly Dictionary<int, string?> Matches = new();
    private static readonly Dictionary<string, Bitmap?> Bitmaps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every club in the current game, so short names (nickname, abbreviation) only match when unique.</summary>
    public static IReadOnlyCollection<Team>? AllTeams { get; private set; }

    /// <summary>Raised after the folder or the club list changes, so crests redraw.</summary>
    public static event Action? Changed;

    public static LogoPack Pack
    {
        get
        {
            var root = AppSettings.Current.LogoFolder;
            if (_pack == null || _root != root) { _pack = LogoPack.Scan(root); _root = root; Matches.Clear(); }
            return _pack;
        }
    }

    public static void UseTeams(IEnumerable<Team>? teams)
    {
        AllTeams = teams?.DistinctBy(t => t.Id).ToList();
        Matches.Clear();
        Changed?.Invoke();
    }

    /// <summary>Rescan the folder (after the setting changes or files are added).</summary>
    public static void Reload()
    {
        _pack = null; Matches.Clear();
        foreach (var b in Bitmaps.Values) b?.Dispose();
        Bitmaps.Clear();
        Changed?.Invoke();
    }

    public static string? FileFor(Team team)
    {
        var pack = Pack;
        if (!Matches.TryGetValue(team.Id, out var f)) Matches[team.Id] = f = pack.Find(team, AllTeams);
        return f;
    }

    public static Bitmap? BitmapFor(Team team)
    {
        if (FileFor(team) is not { } file) return null;
        if (Bitmaps.TryGetValue(file, out var b)) return b;
        try { b = new Bitmap(file); } catch (Exception) { b = null; }
        return Bitmaps[file] = b;
    }

    /// <summary>How many of these clubs have a logo in the pack.</summary>
    public static int Matched(IEnumerable<Team> teams) => teams.Count(t => FileFor(t) != null);
}
