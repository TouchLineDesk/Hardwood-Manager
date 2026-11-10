using System.Text;
using Hardwood.Core.Model;

namespace Hardwood.Core.Graphics;

/// <summary>
/// A folder of team logo images (PNG or JPG, any subfolders) that replaces the drawn crests.
/// A file is matched to a club by its name, ignoring case, spaces and punctuation, in this order:
/// <list type="number">
/// <item>a line in <c>logos.txt</c> in the folder: <c>Club full name = file.png</c> (for packs whose file names don't match);</item>
/// <item>the club's full name (<c>Boston Harbor Hawks.png</c>);</item>
/// <item><c>team-&lt;id&gt;.png</c> (the id is shown in the team list the game writes);</item>
/// <item>the nickname (<c>Harbor Hawks.png</c>), when only one club uses it;</item>
/// <item>the abbreviation (<c>BHH.png</c>), when only one club uses it.</item>
/// </list>
/// </summary>
public sealed class LogoPack
{
    public const string MapFile = "logos.txt";
    public const string ListFile = "team-list.txt";
    private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

    public string Root { get; }
    private readonly Dictionary<string, string> _byKey = new();
    private readonly Dictionary<string, string> _mapped = new();

    public int FileCount { get; private set; }

    private LogoPack(string root) => Root = root;

    public static LogoPack Empty { get; } = new("");

    /// <summary>True when the folder exists and has at least one image.</summary>
    public static bool LooksLikePack(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return false;
        try { return Images(folder).Any(); }
        catch (Exception) { return false; }
    }

    private static IEnumerable<string> Images(string folder) =>
        Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));

    public static LogoPack Scan(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return Empty;
        var pack = new LogoPack(folder);
        try
        {
            // Shallow files win over ones deeper in subfolders.
            foreach (var f in Images(folder).OrderBy(f => f.Count(ch => ch == Path.DirectorySeparatorChar)).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                pack.FileCount++;
                pack._byKey.TryAdd(Key(Path.GetFileNameWithoutExtension(f)), f);
            }
            var map = Path.Combine(folder, MapFile);
            if (File.Exists(map))
                foreach (var line in File.ReadAllLines(map))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith('#')) continue;
                    int eq = t.IndexOf('=');
                    if (eq <= 0) continue;
                    var file = t[(eq + 1)..].Trim().Trim('"');
                    var path = Path.IsPathRooted(file) ? file : Path.Combine(folder, file);
                    if (File.Exists(path)) pack._mapped[Key(t[..eq])] = path;
                }
        }
        catch (Exception) { /* unreadable folders just match fewer clubs */ }
        return pack;
    }

    /// <summary>Lower-case letters and digits only, accents removed: "St. Louis Spirit" → "stlouisspirit".</summary>
    public static string Key(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    /// <summary>The image file for a club, or null to keep the drawn crest. <paramref name="all"/> decides which short names are unique.</summary>
    public string? Find(Team team, IReadOnlyCollection<Team>? all = null)
    {
        if (FileCount == 0) return null;
        if (_mapped.TryGetValue(Key(team.FullName), out var m)) return m;
        if (_byKey.TryGetValue(Key(team.FullName), out var f)) return f;
        if (_byKey.TryGetValue($"team{team.Id}", out f)) return f;
        bool Unique(Func<Team, string> by) => all == null || all.Count(t => Key(by(t)) == Key(by(team))) <= 1;
        if (_byKey.TryGetValue(Key(team.Name), out f) && Unique(t => t.Name)) return f;
        if (_byKey.TryGetValue(Key(team.Abbreviation), out f) && Unique(t => t.Abbreviation)) return f;
        return null;
    }

    /// <summary>Writes <see cref="ListFile"/> into the folder: every club, grouped by league, with the file name it looks for.</summary>
    public static string WriteTeamList(string folder, IEnumerable<(string league, IEnumerable<Team> teams)> leagues)
    {
        Directory.CreateDirectory(folder);
        var sb = new StringBuilder();
        sb.AppendLine("Hardwood Manager — team logo names");
        sb.AppendLine("Name each logo after the club's full name (PNG or JPG), e.g. \"Boston Harbor Hawks.png\". Case, spaces and punctuation don't matter; subfolders are fine.");
        sb.AppendLine("Also accepted: team-<id>.png, the nickname or the abbreviation when no other club shares it.");
        sb.AppendLine($"If your pack uses other names, add lines to {MapFile} in this folder:  Boston Harbor Hawks = my-file.png");
        foreach (var (league, teams) in leagues)
        {
            sb.AppendLine();
            sb.AppendLine($"== {league} ==");
            foreach (var t in teams.OrderBy(t => t.FullName))
                sb.AppendLine($"{t.FullName}.png    (or team-{t.Id}.png, {t.Abbreviation}.png)");
        }
        var path = Path.Combine(folder, ListFile);
        File.WriteAllText(path, sb.ToString());
        return path;
    }
}
