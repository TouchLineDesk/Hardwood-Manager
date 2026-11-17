using Hardwood.Core.RealWorld;

namespace Hardwood.Core.College;

/// <summary>A college rivalry between two programs. <see cref="Name"/> is the series' real name where it has one
/// ("Crosstown Shootout"), else null; <see cref="InState"/> marks the fallback for programs with no listed rival.</summary>
public sealed record Rivalry(int A, int B, string? Name, bool InState)
{
    /// <summary>"the Crosstown Shootout", "a rivalry game", "an in-state rivalry".</summary>
    public string Label => Name != null ? (Name.StartsWith("Philadelphia") || Name == "Bedlam" ? Name : $"the {Name}") : InState ? "an in-state rivalry" : "a rivalry game";
    /// <summary>The short tag on a schedule tile.</summary>
    public string Tag => InState ? "IN-STATE" : "RIVALRY";
    /// <summary>"Crosstown Shootout" / "Rivalry" / "In-state rivalry": a heading.</summary>
    public string Title => Name ?? (InState ? "In-state rivalry" : "Rivalry");
}

/// <summary>Both sides' wins in a rivalry since the career began, and the last meeting (C5 part 1). Saved with the college world.</summary>
public sealed class RivalrySeries
{
    /// <summary>Wins by the lower and the higher program id.</summary>
    public int LoWins { get; set; }
    public int HiWins { get; set; }
    public DateOnly? LastDate { get; set; }
    public int LastWinner { get; set; }
    public int LastWinnerPts { get; set; }
    public int LastLoserPts { get; set; }
    /// <summary>The college season (its first year) of the first meeting counted.</summary>
    public int Since { get; set; }
    public int Games => LoWins + HiWins;
}

/// <summary>
/// C5 part 1 (college-sim-design #165): real college rivalries, matched by school name in a real world. Well-known series
/// keep their real names (only series that are widely known by that name in basketball; football trophies are not used).
/// A program with no listed rival gets its in-state conference mates. Nothing here draws a random number or changes a result:
/// the schedule is unchanged, rivals meet when the season's schedule puts them together, and the series is counted from
/// the career's start.
/// </summary>
public sealed partial class CollegeWorld
{
    /// <summary>The listed rivalries: (school, school, real series name or null).</summary>
    public static readonly (string A, string B, string? Name)[] RealRivalries =
    {
        ("Cincinnati", "Xavier", "Crosstown Shootout"),
        ("Kansas", "Missouri", "Border War"),
        ("Kansas", "Kansas State", "Sunflower Showdown"),
        ("Iowa", "Iowa State", "Cy-Hawk Series"),
        ("Kentucky", "Louisville", "Battle for the Bluegrass"),
        ("Oklahoma", "Oklahoma State", "Bedlam"),
        ("Pittsburgh", "West Virginia", "Backyard Brawl"),
        ("Illinois", "Missouri", "Braggin' Rights"),
        ("New Mexico", "New Mexico State", "Rio Grande Rivalry"),
        ("Belmont", "Lipscomb", "Battle of the Boulevard"),
        ("Duke", "North Carolina", null),
        ("North Carolina", "NC State", null),
        ("Indiana", "Purdue", null),
        ("Kentucky", "Indiana", null),
        ("Kentucky", "Tennessee", null),
        ("Michigan", "Michigan State", null),
        ("Michigan", "Ohio State", null),
        ("Arizona", "Arizona State", null),
        ("UCLA", "USC", null),
        ("Gonzaga", "Saint Mary's", null),
        ("Georgetown", "Syracuse", null),
        ("Utah", "BYU", null),
        ("Oregon", "Oregon State", null),
        ("Washington", "Washington State", null),
        ("Alabama", "Auburn", null),
        ("Texas", "Texas A&M", null),
        ("Virginia", "Virginia Tech", null),
        ("Florida", "Florida State", null),
        ("Georgia", "Georgia Tech", null),
        ("Clemson", "South Carolina", null),
        ("Marquette", "Wisconsin", null),
        ("Richmond", "VCU", null),
        ("UNLV", "Nevada", null),
        ("Stanford", "California", null),
        ("Ole Miss", "Mississippi State", null),
        ("Colorado", "Colorado State", null),
        ("Creighton", "Nebraska", null),
    };

    /// <summary>Philadelphia's Big 5 (six schools since 2023): every pair is a rivalry under that name.</summary>
    public static readonly string[] PhiladelphiaBig5 = { "Villanova", "Saint Joseph's", "Temple", "Penn", "La Salle", "Drexel" };

    /// <summary>Series records since the career began, keyed by <see cref="PairKey"/>. Saved with the world.</summary>
    public Dictionary<string, RivalrySeries> Series { get; set; } = new();

    private Dictionary<(int, int), Rivalry>? _rivalries;

    public static string PairKey(int a, int b) => a < b ? $"{a}-{b}" : $"{b}-{a}";

    /// <summary>Every rivalry in this world (built once; the programs don't change).</summary>
    public IReadOnlyDictionary<(int, int), Rivalry> Rivalries => _rivalries ??= BuildRivalries();

    private Dictionary<(int, int), Rivalry> BuildRivalries()
    {
        var map = new Dictionary<(int, int), Rivalry>();
        var bySchool = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in Programs)
            if (ConferenceOf.TryGetValue(t.Id, out var ck) && ck.StartsWith(RealColleges.KeyPrefix)) bySchool.TryAdd(t.City, t.Id);
        void Add(int a, int b, string? name, bool inState)
        {
            var k = a < b ? (a, b) : (b, a);
            if (a != b && !map.ContainsKey(k)) map[k] = new Rivalry(k.Item1, k.Item2, name, inState);
        }
        foreach (var (a, b, name) in RealRivalries)
            if (bySchool.TryGetValue(a, out var ia) && bySchool.TryGetValue(b, out var ib)) Add(ia, ib, name, false);
        var big5 = PhiladelphiaBig5.Where(bySchool.ContainsKey).Select(x => bySchool[x]).ToList();
        for (int i = 0; i < big5.Count; i++)
            for (int j = i + 1; j < big5.Count; j++) Add(big5[i], big5[j], "Philadelphia Big 5", false);
        // Programs with no listed rival: their conference mates in the same state.
        var listed = map.Keys.SelectMany(k => new[] { k.Item1, k.Item2 }).ToHashSet();
        foreach (var t in Programs)
        {
            if (listed.Contains(t.Id) || RealColleges.StateOf(t.City) is not { } st) continue;
            foreach (var o in Programs)
                if (o.Id != t.Id && ConferenceOf.GetValueOrDefault(o.Id) == ConferenceOf.GetValueOrDefault(t.Id) && RealColleges.StateOf(o.City) == st)
                    Add(t.Id, o.Id, null, true);
        }
        return map;
    }

    /// <summary>The rivalry between two programs, or null.</summary>
    public Rivalry? RivalryOf(int a, int b) => Rivalries.TryGetValue(a < b ? (a, b) : (b, a), out var r) ? r : null;

    /// <summary>A program's rivals, the listed ones first.</summary>
    public List<(Rivalry Rivalry, int RivalId)> RivalsOf(int id) =>
        Rivalries.Values.Where(r => r.A == id || r.B == id).OrderBy(r => r.InState).ThenBy(r => r.Name == null)
            .Select(r => (r, r.A == id ? r.B : r.A)).ToList();

    /// <summary>The series between two programs since the career began (null if they haven't met as rivals).</summary>
    public RivalrySeries? SeriesOf(int a, int b) => Series.TryGetValue(PairKey(a, b), out var x) ? x : null;

    /// <summary>"Series 3-2 Cincinnati since 2026-27" from <paramref name="us"/>'s side: "You lead 3-2", "Level 2-2", "They lead 2-1", or null before a meeting.</summary>
    public string? SeriesLine(int us, int them)
    {
        if (SeriesOf(us, them) is not { Games: > 0 } s) return null;
        int w = us < them ? s.LoWins : s.HiWins, l = s.Games - w;
        string head = w > l ? $"You lead the series {w}-{l}" : w < l ? $"{Program(them)?.City ?? "They"} lead{(Program(them) == null ? "" : "s")} the series {l}-{w}" : $"Series level at {w}-{l}";
        return $"{head} since {s.Since}-{(s.Since + 1) % 100:00}";
    }

    /// <summary>"#5 vs #9" when both sides are ranked (the first side named first), else null.</summary>
    public static string? RankedMatchup(int? first, int? second) => first is { } a && second is { } b ? $"#{a} vs #{b}" : null;

    /// <summary>Count a finished game in its rivalry's series (both the full engine and the quick sim come through here).</summary>
    private void NoteRivalry(CollegeGame g)
    {
        if (g.WinnerId is not { } w || RivalryOf(g.HomeId, g.AwayId) == null) return;
        var key = PairKey(g.HomeId, g.AwayId);
        if (!Series.TryGetValue(key, out var s)) Series[key] = s = new RivalrySeries { Since = Season.Year };
        if (w == Math.Min(g.HomeId, g.AwayId)) s.LoWins++; else s.HiWins++;
        s.LastDate = g.Date; s.LastWinner = w;
        s.LastWinnerPts = Math.Max(g.HomePts!.Value, g.AwayPts!.Value); s.LastLoserPts = Math.Min(g.HomePts!.Value, g.AwayPts!.Value);
    }
}
