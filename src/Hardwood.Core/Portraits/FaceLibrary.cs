using System.Security.Cryptography;
using System.Text;
using Hardwood.Core.Model;

namespace Hardwood.Core.Portraits;

/// <summary>
/// The FMNEWGAN face pack on disk: one folder per <see cref="Ethnicity"/> (e.g. <c>African\African123.png</c>).
/// Faces are referred to by their path relative to the pack root with forward slashes
/// (<c>African/African123.png</c>), which is what the save stores in <see cref="Player.FaceFile"/>.
/// </summary>
public sealed class FaceLibrary
{
    private readonly Dictionary<Ethnicity, string[]> _faces;

    public string Root { get; }
    public int Count => _faces.Values.Sum(f => f.Length);

    private FaceLibrary(string root, Dictionary<Ethnicity, string[]> faces)
    {
        Root = root;
        _faces = faces;
    }

    /// <summary>Faces available for an ethnicity, sorted, as relative paths.</summary>
    public IReadOnlyList<string> Faces(Ethnicity e) => _faces.TryGetValue(e, out var f) ? f : Array.Empty<string>();

    /// <summary>True if the folder looks like the face pack (at least one ethnicity folder exists).</summary>
    public static bool LooksLikePack(string? root) =>
        !string.IsNullOrWhiteSpace(root) && Directory.Exists(root) &&
        Enum.GetValues<Ethnicity>().Any(e => Directory.Exists(Path.Combine(root, EthnicityInfo.FolderName(e))));

    /// <summary>Scan the pack (only the ethnicity folders, *.png). Missing folders are simply empty.</summary>
    public static FaceLibrary Scan(string root)
    {
        var faces = new Dictionary<Ethnicity, string[]>();
        foreach (var e in Enum.GetValues<Ethnicity>())
        {
            var folder = EthnicityInfo.FolderName(e);
            var dir = Path.Combine(root, folder);
            if (!Directory.Exists(dir)) { faces[e] = Array.Empty<string>(); continue; }
            var list = Directory.EnumerateFiles(dir, "*.png", SearchOption.TopDirectoryOnly)
                .Select(f => folder + "/" + Path.GetFileName(f))
                .ToArray();
            Array.Sort(list, StringComparer.Ordinal);
            faces[e] = list;
        }
        return new FaceLibrary(root, faces);
    }

    /// <summary>Build a library from a list of relative paths (tests, or an index file).</summary>
    public static FaceLibrary FromList(string root, IEnumerable<string> relativePaths)
    {
        var faces = Enum.GetValues<Ethnicity>().ToDictionary(e => e, _ => new List<string>());
        foreach (var raw in relativePaths)
        {
            var rel = raw.Replace('\\', '/');
            var slash = rel.IndexOf('/');
            if (slash <= 0 || !rel.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
            if (EthnicityInfo.FromFolderName(rel[..slash]) is { } e) faces[e].Add(rel);
        }
        return new FaceLibrary(root, faces.ToDictionary(k => k.Key, v => { var a = v.Value.ToArray(); Array.Sort(a, StringComparer.Ordinal); return a; }));
    }

    /// <summary>Absolute path of a face on disk.</summary>
    public string FullPath(string faceFile) => Path.Combine(Root, faceFile.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// Give a face to every target player who has none. A face is chosen once and never re-picked,
    /// and no face is used twice in a save (<paramref name="everyone"/> includes retired players so
    /// their faces are never handed out again). The pick is stable for a player id: a hash of the id
    /// picks a starting point in the ethnicity's folder, then the first unused face from there.
    /// Players are served in the order given. Returns the number of faces assigned.
    /// </summary>
    public int AssignMissing(IEnumerable<Player> everyone, IEnumerable<Player> targets)
    {
        var used = new HashSet<string>(everyone.Where(p => p.FaceFile != null).Select(p => p.FaceFile!), StringComparer.OrdinalIgnoreCase);
        int assigned = 0;
        // In the order given (the game lists the top league first), so a small pack runs out abroad, not in your league:
        // promoted clubs' players have high ids from the world (#355).
        foreach (var p in targets)
        {
            if (p.FaceFile != null) continue;
            var list = Faces(p.Ethnicity);
            if (list.Count == 0) continue;
            int start = (int)(Stable(p.Id) % (ulong)list.Count);
            for (int k = 0; k < list.Count; k++)
            {
                var face = list[(start + k) % list.Count];
                if (used.Add(face)) { p.FaceFile = face; assigned++; break; }
            }
        }
        return assigned;
    }

    /// <summary>Same hash as the original Python kit: first 12 hex digits of SHA-1 of the id.</summary>
    internal static ulong Stable(int id)
    {
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(id.ToString()));
        ulong v = 0;
        for (int i = 0; i < 6; i++) v = (v << 8) | hash[i];
        return v;
    }
}
