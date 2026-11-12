using System.Text.Json;
using Hardwood.Core.College;

namespace Hardwood.Core.RealWorld;

/// <summary>One real Division I program (data pack: Data/ncaa_programs.json). The players stay generated (Danny, #233).</summary>
public sealed record RealProgram(string School, string Nickname, string Conference, string State, string Arena,
    int Appearances, int FinalFours, int Titles, int Recent, string Primary, string Secondary);

/// <summary>One real conference in its 2026-27 alignment.</summary>
public sealed record RealConference(string Key, string Name, string Short, bool Power);

/// <summary>
/// R3 (Danny, #233; full Division I first, #247): every Division I men's program in its 2026-27 conference, with its
/// nickname, state, home arena, colours and NCAA tournament history. A real-world career's college world is built from
/// these (<see cref="CollegeWorld.Generate"/> with <c>real</c>); rosters, recruiting, the AD and March Madness are unchanged,
/// and every real conference's tournament champion takes an automatic bid.
/// </summary>
public static class RealColleges
{
    /// <summary>Real conferences' keys carry this prefix, so they never meet the fictional ones'.</summary>
    public const string KeyPrefix = "r-";

    private sealed record Pack(List<RealConference> Conferences, List<RealProgram> Programs, string Checked, string Source, string Notes);
    private static readonly Lazy<Pack> _pack = new(Load);
    private static readonly Lazy<Dictionary<string, string>> _states = new(() =>
        Programs.ToDictionary(p => p.School, p => p.State, StringComparer.Ordinal));

    public static IReadOnlyList<RealProgram> Programs => _pack.Value.Programs;
    public static IReadOnlyList<RealConference> Conferences => _pack.Value.Conferences;
    public static string Checked => _pack.Value.Checked;
    public static string Source => _pack.Value.Source;

    /// <summary>The game's conference records for the real conferences (keys prefixed).</summary>
    public static IEnumerable<ConferenceInfo> ConferenceInfos() =>
        Conferences.Select(c => new ConferenceInfo(KeyPrefix + c.Key, c.Name, c.Short, c.Power, Programs.Count(p => p.Conference == c.Key), Real: true));

    /// <summary>A real program's home state by its school name, or null for any other name.</summary>
    public static string? StateOf(string school) => _states.Value.TryGetValue(school, out var s) ? s : null;

    /// <summary>
    /// Starting prestige from real standing: the conference it plays in today, its NCAA tournament history (appearances,
    /// Final Fours, titles) and its recent record (appearances 2016-2026). Soft-capped near the top, so the blue bloods sit
    /// around 85-90 rather than all at the ceiling.
    /// </summary>
    public static double Prestige(RealProgram p)
    {
        var conf = Conferences.First(c => c.Key == p.Conference);
        double baseLevel = conf.Power ? 45 : TopMid.Contains(p.Conference) ? 28 : 18;
        double raw = baseLevel + 2.6 * Math.Sqrt(p.Appearances) + 0.5 * p.FinalFours + 0.7 * p.Titles + 3.0 * p.Recent;
        double soft = raw <= 70 ? raw : 70 + 25 * (1 - Math.Exp(-(raw - 70) / 25));
        return Math.Round(Math.Clamp(soft, 15, 95), 1);
    }

    /// <summary>The strongest of the other conferences (multi-bid leagues in recent years).</summary>
    private static readonly HashSet<string> TopMid = new() { "aac", "a10", "mwc", "wcc", "pac", "mvc" };

    /// <summary>Short, unique abbreviations: an acronym kept as is, initials for longer names, else the first four letters.</summary>
    public static Dictionary<string, string> Abbreviations()
    {
        var used = new HashSet<string>();
        var result = new Dictionary<string, string>();
        foreach (var p in Programs.OrderByDescending(Prestige))
        {
            string s = p.School;
            var words = s.Replace("(", "").Replace(")", "").Replace("&", "").Replace("-", " ").Replace(".", "")
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w is not ("of" or "The")).ToList();
            string abbr = s.Length <= 5 && s == s.ToUpperInvariant() ? s.Replace(" ", "")
                : words.Count >= 2 ? string.Concat(words.Select(w => char.ToUpperInvariant(w[0])))
                : new string(s.Where(char.IsLetter).Take(4).ToArray()).ToUpperInvariant();
            if (abbr.Length < 2) abbr = s[..Math.Min(3, s.Length)].ToUpperInvariant();
            string tryAbbr = abbr;
            // Collisions: lengthen with the next letters of the last word.
            string tail = new string(words.Last().Skip(1).Where(char.IsLetter).ToArray()).ToUpperInvariant();
            for (int i = 0; !used.Add(tryAbbr); i++)
                tryAbbr = i < tail.Length ? abbr + tail[..(i + 1)] : abbr + (char)('A' + i - tail.Length);
            result[s] = tryAbbr;
        }
        return result;
    }

    private static Pack Load()
    {
        var asm = typeof(RealColleges).Assembly;
        string name = asm.GetManifestResourceNames().Single(n => n.EndsWith("ncaa_programs.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Pack>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
