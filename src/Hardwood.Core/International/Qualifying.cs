using Hardwood.Core.Season;

namespace Hardwood.Core.International;

/// <summary>One FIBA window played (international plan I3, run 210): its games, kept by code as the World Cup's are.</summary>
public sealed class WindowRecord
{
    /// <summary>"2026-11" or "2027-02": the window's month, its key.</summary>
    public string Key { get; set; } = "";
    /// <summary>What it qualifies for: "2027 World Cup", "2029 continental championships".</summary>
    public string Target { get; set; } = "";
    public string Dates { get; set; } = "";
    /// <summary>The window's place in its cycle, from 0.</summary>
    public int Index { get; set; }
    /// <summary>Round: "Europe A", "Americas B", … (the qualifying group).</summary>
    public List<CupGame> Games { get; set; } = new();
}

/// <summary>A qualifying cycle's groups, drawn by ranking at its first window and kept for the rest of it.</summary>
public sealed class QualifyingCycle
{
    public string Target { get; set; } = "";
    /// <summary>Group name ("Europe A") to its codes, in draw order.</summary>
    public Dictionary<string, List<string>> Groups { get; set; } = new();
}

/// <summary>
/// International plan I3 (run 210): the FIBA world ranking from results. It starts from the real men's ranking where the game
/// knows it (an approximation of FIBA's 2025 order), half and half with the twelve's strength, and every international game
/// moves it after that, Elo-style: a window game counts <see cref="WindowK"/>, a World Cup game <see cref="WorldCupK"/>, and a
/// bigger margin counts a little more. Pure functions: the session writes the ranking only when a window or a World Cup is
/// played, never when a page reads it (run 200's lesson).
/// </summary>
public static class FibaRanking
{
    public const double WindowK = 24, WorldCupK = 36;
    /// <summary>The top of the scale (No. 1 seeds here) and the step per place.</summary>
    public const double Top = 1900, Step = 8;

    /// <summary>The real men's ranking, best first (approximate, 2025). Countries not listed seed on strength alone.</summary>
    public static readonly string[] RealOrder =
    {
        "USA", "SRB", "GER", "FRA", "CAN", "ESP", "AUS", "ARG", "LTU", "GRE", "SLO", "ITA", "LAT", "BRA", "PUR", "MNE", "TUR", "DOM",
        "POL", "GEO", "FIN", "NZL", "CRO", "JPN", "MEX", "SSD", "CHN", "LBN", "PHI", "VEN", "ISR", "CZE", "JOR", "URU", "BIH", "CIV",
        "NGR", "UKR", "EGY", "SEN", "ANG", "IRI", "KOR", "BEL", "CPV", "EST", "MKD", "COL", "SWE", "PAN", "CMR", "TUN", "MLI", "GBR",
        "NED", "AUT", "HUN", "POR", "BAH", "QAT", "KSA", "TPE", "KAZ", "IND", "COD", "RWA", "GUI", "CHI", "PAR", "CUB",
    };

    private static readonly Dictionary<string, int> RealRank = RealOrder.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i + 1);

    /// <summary>The starting points for every ranked team: half the real ranking's place, half the strength's place.</summary>
    public static Dictionary<string, double> Seed(IReadOnlyList<NationalTeam> teams)
    {
        var ranked = teams.Where(t => !t.Nation.Suspended).OrderByDescending(t => t.Strength).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
        var d = new Dictionary<string, double>();
        for (int i = 0; i < ranked.Count; i++)
        {
            var code = ranked[i].Nation.Code;
            double byStrength = Top - i * Step;
            d[code] = RealRank.TryGetValue(code, out var r) ? Math.Round((byStrength + (Top - (r - 1) * Step)) / 2, 1) : byStrength;
        }
        return d;
    }

    /// <summary>The ranking as it stands: the saved points, with any team new to the ranking seeded where it would start.</summary>
    public static Dictionary<string, double> Current(InternationalHistory h, IReadOnlyList<NationalTeam> teams)
    {
        var seed = Seed(teams);
        if (h.Ranking.Count == 0) return seed;
        var d = new Dictionary<string, double>(h.Ranking);
        foreach (var (code, pts) in seed) d.TryAdd(code, pts);
        return d;
    }

    /// <summary>Rank the teams by points (suspended teams unranked) and set each zone's rank. Returns them in ranking order.</summary>
    public static IReadOnlyList<NationalTeam> Apply(IReadOnlyList<NationalTeam> teams, IReadOnlyDictionary<string, double> points)
    {
        int rank = 0;
        var order = teams.OrderBy(t => t.Nation.Suspended ? 1 : 0)
            .ThenByDescending(t => points.GetValueOrDefault(t.Nation.Code, double.MinValue))
            .ThenByDescending(t => t.Strength).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
        foreach (var t in order) { t.Rank = t.Nation.Suspended ? 0 : ++rank; t.Points = points.GetValueOrDefault(t.Nation.Code); }
        foreach (var zone in order.Where(t => !t.Nation.Suspended).GroupBy(t => t.Nation.Zone))
        {
            int z = 0;
            foreach (var t in zone.OrderBy(t => t.Rank)) t.ZoneRank = ++z;
        }
        return order;
    }

    /// <summary>The chance the first side wins, from the points gap (400 points is ten to one).</summary>
    public static double Expected(double a, double b) => 1.0 / (1.0 + Math.Pow(10, (b - a) / 400.0));

    /// <summary>Move both sides' points for one result. A margin of 1 counts 0.6×, of 10 about 1.0×, of 30+ 1.4×.</summary>
    public static void Update(Dictionary<string, double> points, CupGame g, double k)
    {
        if (!points.TryGetValue(g.Home, out var h) || !points.TryGetValue(g.Away, out var a)) return;
        double won = g.HomePts > g.AwayPts ? 1 : 0;
        int margin = Math.Abs(g.HomePts - g.AwayPts);
        double mov = Math.Clamp(0.6 + 0.4 * Math.Log(1 + Math.Min(margin, 30)) / Math.Log(11), 0.6, 1.4);
        double delta = k * mov * (won - Expected(h, a));
        points[g.Home] = Math.Round(h + delta, 1);
        points[g.Away] = Math.Round(a - delta, 1);
    }
}

/// <summary>
/// International plan I3 (run 210): the qualifying windows (#106, #109). Each zone's ranked teams are drawn into groups of about
/// four by ranking at a cycle's first window (no draw: a snake by rank), and each window plays two rounds of a home-and-away
/// round robin, by quick result: a score from the two twelves' strength and home court, on the window's own stream. A World Cup
/// cycle has the two windows before it (November and February); a continental cycle has the four. The tables decide each zone's
/// World Cup places (I2's quotas), and I4 reads the continental ones. Club seasons carry on (#107); nothing touches a player.
/// </summary>
public static class Qualifying
{
    /// <summary>A strength point is worth this many points of margin; home court is worth <see cref="HomeEdge"/>.</summary>
    public const double MarginPerStrength = 1.6, HomeEdge = 3.0, MarginSd = 11.0;
    /// <summary>A FIBA game's combined score (real windows: about 150–165).</summary>
    public const double TotalMean = 156, TotalSd = 14;
    public const int GroupSize = 4;
    /// <summary>A window not played within this many days of its end (a long jump, an older save) is left unplayed.</summary>
    public const int CatchUpDays = 14;

    /// <summary>The windows that end on or before <paramref name="today"/> and within <see cref="CatchUpDays"/> of it.</summary>
    public static IEnumerable<FibaEvent> DueWindows(DateOnly today) =>
        FibaCalendar.InYear(today.Year).Concat(FibaCalendar.InYear(today.Year - 1))
            .Where(e => e.Kind == FibaEventKind.Window && e.End <= today && e.End > today.AddDays(-CatchUpDays)).OrderBy(e => e.Start);

    public static string KeyOf(FibaEvent w) => w.Start.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"2027 World Cup" from the calendar's "the 2027 World Cup".</summary>
    public static string TargetOf(FibaEvent w)
    {
        var t = FibaCalendar.NextMainEvent(w.Start);
        return t.StartsWith("the ", StringComparison.Ordinal) ? t[4..] : t;
    }

    public static string WorldCupTarget(int year) => $"{year} World Cup";

    /// <summary>Snake the zone's teams (best first) into groups of about four: "Europe A", "Europe B", ….</summary>
    public static Dictionary<string, List<string>> DrawGroups(IReadOnlyList<NationalTeam> rankedBestFirst)
    {
        var d = new Dictionary<string, List<string>>();
        foreach (var zone in rankedBestFirst.Where(t => t.Rank > 0).GroupBy(t => t.Nation.Zone).OrderBy(z => z.Key))
        {
            var teams = zone.OrderBy(t => t.Rank).ToList();
            if (teams.Count < 2) continue;
            int groups = Math.Max(1, (int)Math.Round(teams.Count / (double)GroupSize));
            var lists = Enumerable.Range(0, groups).Select(_ => new List<string>()).ToList();
            for (int i = 0; i < teams.Count; i++)
            {
                int row = i / groups, col = i % groups;
                lists[row % 2 == 0 ? col : groups - 1 - col].Add(teams[i].Nation.Code);
            }
            for (int g = 0; g < groups; g++) d[$"{NationalTeams.ZoneName(zone.Key)} {(char)('A' + g)}"] = lists[g];
        }
        return d;
    }

    /// <summary>A home-and-away round robin by the circle method: each round a list of (home, away). An odd group gets a bye.</summary>
    public static List<List<(string Home, string Away)>> Rounds(IReadOnlyList<string> codes)
    {
        var list = codes.ToList();
        if (list.Count % 2 == 1) list.Add("");
        int n = list.Count;
        var first = new List<List<(string, string)>>();
        for (int r = 0; r < n - 1; r++)
        {
            var round = new List<(string, string)>();
            for (int i = 0; i < n / 2; i++)
            {
                string a = list[i], b = list[n - 1 - i];
                if (a == "" || b == "") continue;
                round.Add(r % 2 == 0 ? (a, b) : (b, a));
            }
            first.Add(round);
            var last = list[n - 1]; list.RemoveAt(n - 1); list.Insert(1, last);
        }
        return first.Concat(first.Select(r => r.Select(x => (x.Item2, x.Item1)).ToList())).ToList();
    }

    /// <summary>A quick result (#109): the margin from the strength gap and home court, the total from FIBA's usual scoring.</summary>
    public static (int Home, int Away) QuickResult(double home, double away, Rng rng)
    {
        double margin = rng.NextGaussian((home - away) * MarginPerStrength + HomeEdge, MarginSd);
        double total = Math.Max(110, rng.NextGaussian(TotalMean, TotalSd));
        int h = (int)Math.Round((total + margin) / 2), a = (int)Math.Round((total - margin) / 2);
        if (h == a) { if (margin >= 0) h += 2; else a += 2; } // overtime, settled
        return (h, a);
    }

    /// <summary>Play one window from the teams as they stand on its first day (ranked by <paramref name="points"/>). Draws the
    /// cycle's groups if this is its first window. Adds the window to the history and moves the ranking.</summary>
    public static WindowRecord Play(InternationalHistory h, FibaEvent w, IReadOnlyList<NationalTeam> teams, ulong seed)
    {
        var points = FibaRanking.Current(h, teams);
        var ranked = FibaRanking.Apply(teams, points);
        string target = TargetOf(w);
        var cycle = h.Cycles.FirstOrDefault(c => c.Target == target);
        if (cycle == null) h.Cycles.Add(cycle = new QualifyingCycle { Target = target, Groups = DrawGroups(ranked) });
        int index = h.Windows.Count(x => x.Target == target);
        var rec = new WindowRecord { Key = KeyOf(w), Target = target, Dates = w.Dates, Index = index };
        var byCode = ranked.ToDictionary(t => t.Nation.Code);
        var rng = new Rng(seed ^ 0xF1BA_3A1D_0000_0003UL ^ ((ulong)(uint)(w.Start.Year * 100 + w.Start.Month) * 0x9E3779B97F4A7C15UL));
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] days = { w.Start.AddDays(3).ToString("MMM d", inv), w.End.AddDays(-1).ToString("MMM d", inv) };
        foreach (var (name, codes) in cycle.Groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var rounds = Rounds(codes);
            for (int k = 0; k < 2; k++)
                foreach (var (home, away) in rounds[(2 * index + k) % rounds.Count])
                {
                    // A team whose pool fell under twelve since the draw forfeits nothing: its games aren't played.
                    if (!byCode.TryGetValue(home, out var ht) || !byCode.TryGetValue(away, out var at)) continue;
                    var (hp, ap) = QuickResult(ht.Strength, at.Strength, rng);
                    var g = new CupGame(name, home, away, hp, ap, days[k]);
                    rec.Games.Add(g);
                    FibaRanking.Update(points, g, FibaRanking.WindowK);
                }
        }
        h.Windows.Add(rec);
        h.Ranking = points;
        h.LastRanking = ranked.Where(t => t.Rank > 0).ToDictionary(t => t.Nation.Code, t => t.Rank);
        return rec;
    }

    /// <summary>A qualifying group's games so far in a cycle, as a World Cup record the group tables can read.</summary>
    public static WorldCupRecord GamesOf(InternationalHistory h, string target) =>
        new() { Games = h.Windows.Where(w => w.Target == target).SelectMany(w => w.Games).ToList() };

    /// <summary>A group's table (wins, then the tied teams' games, then point difference).</summary>
    public static List<string> Table(InternationalHistory h, string target, string group, IReadOnlyList<string> codes)
    {
        var games = GamesOf(h, target);
        return WorldCup.Table(games, codes, g => g.Round == group);
    }

    /// <summary>Each zone's qualifying order for a cycle: group winners first (best record first), then the runners-up, and so on;
    /// teams that played no game come last. Empty when the cycle has no games (the field falls back on the ranking).</summary>
    public static Dictionary<FibaZone, List<string>> ZoneOrder(InternationalHistory h, string target, IReadOnlyDictionary<string, double> points)
    {
        var d = new Dictionary<FibaZone, List<string>>();
        var cycle = h.Cycles.FirstOrDefault(c => c.Target == target);
        var games = GamesOf(h, target);
        if (cycle == null || games.Games.Count == 0) return d;
        foreach (var zone in Enum.GetValues<FibaZone>())
        {
            string zn = NationalTeams.ZoneName(zone);
            var groups = cycle.Groups.Where(g => g.Key.StartsWith(zn + " ", StringComparison.Ordinal)).ToList();
            if (groups.Count == 0) continue;
            var placed = new List<(string Code, int Place, double Pct, int Diff, double Pts)>();
            foreach (var (name, codes) in groups)
            {
                var table = WorldCup.Table(games, codes, g => g.Round == name);
                for (int i = 0; i < table.Count; i++)
                {
                    var r = WorldCup.Record(games, table[i], g => g.Round == name);
                    if (r.W + r.L == 0) continue;
                    placed.Add((table[i], i, r.W / (double)(r.W + r.L), r.Diff, points.GetValueOrDefault(table[i])));
                }
            }
            d[zone] = placed.OrderBy(x => x.Place).ThenByDescending(x => x.Pct).ThenByDescending(x => x.Diff).ThenByDescending(x => x.Pts)
                .ThenBy(x => x.Code, StringComparer.Ordinal).Select(x => x.Code).ToList();
        }
        return d;
    }
}
