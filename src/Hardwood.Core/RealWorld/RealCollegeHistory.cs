using System.Text.Json;

namespace Hardwood.Core.RealWorld;

/// <summary>One real NCAA tournament's last weekend: the champion, the runner-up, the title game and the other two semifinalists
/// (tournament year: 2026 is the 2025-26 season). <see cref="Vacated"/> lists appearances the NCAA vacated, as the source marks them.</summary>
public sealed record RealTournament(int Year, string Champion, string RunnerUp, string Score, List<string> Semifinalists, List<string> Vacated)
{
    /// <summary>The four Final Four teams.</summary>
    public IEnumerable<string> FinalFour => new[] { Champion, RunnerUp }.Concat(Semifinalists);
    /// <summary>The game's season label for this tournament ("2025-26").</summary>
    public string Season => $"{Year - 1}-{Year % 100:00}";
}

/// <summary>
/// K12b (run 137, critic review #18 item 7): a real college career has a past. The NCAA's champions and Final Fours since
/// 1985 and each program's real NBA alumni (data pack: Data/ncaa_history.json). Program totals (titles, Final Fours,
/// tournaments) stay the ones in ncaa_programs.json (<see cref="RealColleges"/>).
/// </summary>
public static class RealCollegeHistory
{
    private sealed record Pack(string Checked, string Source, string Notes, List<RealTournament> Tournaments, Dictionary<string, List<string>> Alumni);
    private static readonly Lazy<Pack> _pack = new(Load);

    public static IReadOnlyList<RealTournament> Tournaments => _pack.Value.Tournaments;
    /// <summary>School → named NBA alumni (only those the sources name; the game keeps the ones on a real roster).</summary>
    public static IReadOnlyDictionary<string, List<string>> Alumni => _pack.Value.Alumni;
    public static string Checked => _pack.Value.Checked;
    public static string Source => _pack.Value.Source;
    public static int FirstYear => Tournaments.Min(t => t.Year);
    public static int LastYear => Tournaments.Max(t => t.Year);

    /// <summary>The school's titles in the list (since 1985), oldest first.</summary>
    public static List<int> TitleYears(string school) => Tournaments.Where(t => t.Champion == school).Select(t => t.Year).ToList();
    /// <summary>The school's Final Fours in the list (since 1985), oldest first.</summary>
    public static List<int> FinalFourYears(string school) => Tournaments.Where(t => t.FinalFour.Contains(school)).Select(t => t.Year).ToList();

    private static Pack Load()
    {
        var asm = typeof(RealCollegeHistory).Assembly;
        string name = asm.GetManifestResourceNames().Single(n => n.EndsWith("ncaa_history.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        return JsonSerializer.Deserialize<Pack>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
