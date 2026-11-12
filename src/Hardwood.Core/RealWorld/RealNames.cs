using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Hardwood.Core.RealWorld;

/// <summary>
/// Every full name in the real-world data (players, rosters, careers, history, coaches), so a made-up player or coach
/// never carries a real NBA name (run 21, #263; review #9 found a generated "Rudy Gobert" in 2051). The check is on
/// the full name, accent- and punctuation-blind: "Nikola Jokic" matches "Nikola Jokić"; "Marcus Gobert" doesn't.
/// </summary>
public static class RealNames
{
    private static readonly Lazy<HashSet<string>> _names = new(Load);

    /// <summary>How many distinct real names are known (for tests and tools).</summary>
    public static int Count => _names.Value.Count;

    public static bool IsReal(string first, string last) => _names.Value.Contains(Key($"{first} {last}"));

    public static bool IsReal(string fullName) => _names.Value.Contains(Key(fullName));

    /// <summary>Lower case, no accents, no punctuation, no Jr./Sr./II/III, single spaces.</summary>
    public static string Key(string name)
    {
        var d = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetter(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c is ' ' or '-') sb.Append(' ');
            // apostrophes and dots vanish: "O'Neal" = "ONeal", "P.J." = "PJ"
        }
        // Letters outside Latin-1 that don't decompose (ć does; đ, ł, ø don't).
        var s = sb.ToString().Replace('đ', 'd').Replace('ł', 'l').Replace('ø', 'o');
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p is not ("jr" or "sr" or "ii" or "iii" or "iv")).ToList();
        return string.Join(' ', parts);
    }

    private static HashSet<string> Load()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var asm = typeof(RealNames).Assembly;
        foreach (var res in asm.GetManifestResourceNames().Where(n => n.Contains("RealWorld.Data.nba_", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = asm.GetManifestResourceStream(res)!;
            using var doc = JsonDocument.Parse(stream);
            Walk(doc.RootElement, set);
        }
        return set;
    }

    // Every string value and property name that looks like a person's name (2-4 words, short).
    private static void Walk(JsonElement e, HashSet<string> set)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject()) { Add(p.Name, set); Walk(p.Value, set); }
                break;
            case JsonValueKind.Array:
                foreach (var x in e.EnumerateArray()) Walk(x, set);
                break;
            case JsonValueKind.String:
                Add(e.GetString(), set);
                break;
        }
    }

    private static void Add(string? s, HashSet<string> set)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length > 40) return;
        var k = Key(s);
        int words = k.Count(c => c == ' ') + 1;
        if (words is >= 2 and <= 4) set.Add(k);
    }
}
