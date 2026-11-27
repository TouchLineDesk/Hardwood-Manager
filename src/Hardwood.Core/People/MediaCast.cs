using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.People;

/// <summary>W1 (run 221, `claude/world-stories-plan.md`): how a columnist sees the game.</summary>
public enum ColumnVoice
{
    /// <summary>The stats writer: ranks rosters by the numbers, owns up when the model misses.</summary>
    Numbers,
    /// <summary>The old-school columnist: trusts experience, never quite admits a miss.</summary>
    OldSchool,
    /// <summary>Your local beat writer: knows your team best and rates it a little high.</summary>
    Beat,
    /// <summary>The national college writer: reputation and programs first.</summary>
    College,
}

/// <summary>A fictional columnist (invented names and outlets only: no real journalist or paper, world-stories rules).</summary>
public sealed record Columnist(string Key, string Name, string Outlet, ColumnVoice Voice, bool She)
{
    public string He => She ? "she" : "he";
    public string HeCap => She ? "She" : "He";
    public string His => She ? "her" : "his";
    /// <summary>"Priya Venkataraman (The Possession Log)".</summary>
    public string Byline => $"{Name} ({Outlet})";
}

/// <summary>One columnist's preseason order of your table, kept so it can be judged at the end of the regular season.</summary>
public sealed class ColumnPicks
{
    /// <summary>The season it belongs to (the pros' season year; a college season's starting year).</summary>
    public int Season { get; set; }
    /// <summary>Your team when the picks were made (a verdict is only written for the team that was picked).</summary>
    public int TeamId { get; set; }
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Outlet { get; set; } = "";
    /// <summary>The table's name when picked ("East", "ACC", "Liga ACB").</summary>
    public string Table { get; set; } = "";
    /// <summary>Team ids, the pick for first place first.</summary>
    public List<int> Order { get; set; } = new();
    /// <summary>The regular season's final order (empty until judged).</summary>
    public List<int> Finished { get; set; } = new();
    public DateOnly Made { get; set; }
    public DateOnly? Judged { get; set; }

    public int PickOf(int teamId) => Order.IndexOf(teamId) + 1;
    public int FinishOf(int teamId) => Finished.IndexOf(teamId) + 1;
}

/// <summary>The columnists' saved picks (an optional part of the saved <see cref="MediaDesk"/>; older saves load with none).</summary>
public sealed class ColumnBook
{
    public List<ColumnPicks> Picks { get; set; } = new();
    /// <summary>The last power rankings in your results message (pros and abroad), and where they had you.</summary>
    public DateOnly LastRankings { get; set; }
    public int LastRank { get; set; }

    public List<ColumnPicks> For(int season, int teamId) => Picks.Where(p => p.Season == season && p.TeamId == teamId).ToList();
}

/// <summary>
/// W1 (run 221): the media cast. Three fictional columnists per career, with steady voices: the stats writer, the
/// old-school columnist (pros) or the national college writer (college), and your local beat writer (one per team, by its
/// name). Before each season they pick your table; at the end of the regular season the picks are judged ("He picked you
/// 11th. You won the conference."). In pro careers the stats writer's power rankings ride in every fourth weekly results
/// message. Nothing here draws a random number: every "hunch" is a fixed hash of the career's seed, the season, the
/// writer and the team, so the shared streams and results are untouched.
/// </summary>
public static class MediaCast
{
    public static readonly Columnist Numbers = new("numbers", "Priya Venkataraman", "The Possession Log", ColumnVoice.Numbers, true);
    public static readonly Columnist OldSchool = new("oldschool", "Walt Brennecke", "the Courtside Sentinel", ColumnVoice.OldSchool, false);
    public static readonly Columnist CollegeWriter = new("college", "Dana Whitcombe", "Campus Hoops Weekly", ColumnVoice.College, true);

    private static readonly (string first, bool she)[] BeatFirst =
    {
        ("Marcus", false), ("Elena", true), ("Tom", false), ("Rachel", true), ("Andre", false), ("Megan", true),
        ("Luis", false), ("Hannah", true), ("Darnell", false), ("Sofia", true), ("Grant", false), ("Noor", true),
    };
    private static readonly string[] BeatLast =
        { "Lindqvist", "Okafor", "Barrios", "Kowalczyk", "Ashby", "Delacroix", "Fairweather", "Novak", "Tremblay", "Haddad", "Castellanos", "Whitlock" };
    private static readonly string[] BeatPaper = { "Hoops Ledger", "Fast Break", "Baseline", "Tip-Off Daily" };

    /// <summary>Your team's beat writer: the same writer for the same team in every career (a hash of its full name).</summary>
    public static Columnist BeatWriter(Team t)
    {
        ulong h = Hash(t.FullName);
        var (first, she) = BeatFirst[(int)(h % (ulong)BeatFirst.Length)];
        string last = BeatLast[(int)(h / 13 % (ulong)BeatLast.Length)];
        string paper = BeatPaper[(int)(h / 211 % (ulong)BeatPaper.Length)];
        return new Columnist("beat", $"{first} {last}", $"the {t.City} {paper}", ColumnVoice.Beat, she);
    }

    /// <summary>The career's cast: the beat writer first (he covers you), then the two national voices.</summary>
    public static List<Columnist> CastFor(Team t, bool college) =>
        college ? new() { BeatWriter(t), CollegeWriter, Numbers } : new() { BeatWriter(t), Numbers, OldSchool };

    /// <summary>
    /// A columnist's preseason order of a table, best first. Each writer weighs the same facts his own way (all in
    /// standard units within the table): roster strength (<paramref name="strength"/>), the program's standing
    /// (<paramref name="reputation"/>, college), the rotation's experience, a homer's lift for his own team, and a fixed
    /// hunch per team.
    /// </summary>
    public static List<int> Pick(Columnist w, IReadOnlyList<Team> table, Team ours, Func<Team, double> strength, Func<Team, double>? reputation,
        ulong seed, int season)
    {
        if (table.Count == 0) return new();
        var str = Z(table, strength);
        var rep = reputation != null ? Z(table, reputation) : str;
        var exp = Z(table, Experience);
        double Score(Team t)
        {
            double hunch = Hunch(seed, season, w.Key, t.Id);
            return w.Voice switch
            {
                ColumnVoice.Numbers => str[t.Id] + 0.25 * hunch,
                ColumnVoice.OldSchool => 0.7 * str[t.Id] + 0.3 * exp[t.Id] + 0.6 * hunch,
                ColumnVoice.Beat => str[t.Id] + (t.Id == ours.Id ? 0.35 : 0) + 0.45 * hunch,
                _ => 0.6 * rep[t.Id] + 0.4 * str[t.Id] + 0.5 * hunch,
            };
        }
        return table.Select(t => (t.Id, s: Score(t))).OrderByDescending(x => x.s).ThenBy(x => x.Id).Select(x => x.Id).ToList();
    }

    /// <summary>The mean age of a team's eight best players (the old-school columnist trusts a veteran rotation).</summary>
    public static double Experience(Team t)
    {
        var top = t.Roster.OrderByDescending(p => p.CurrentAbility).Take(8).ToList();
        return top.Count == 0 ? 0 : top.Average(p => -p.BirthDate.DayNumber / 365.25);
    }

    /// <summary>The stats writer's power score: winning percentage, then point difference a game.</summary>
    public static double PowerScore(TeamRecord r) => r.Games == 0 ? 0 : r.WinPct * 100 + r.PointDiff * 1.5;

    private static Dictionary<int, double> Z(IReadOnlyList<Team> table, Func<Team, double> f)
    {
        var v = table.ToDictionary(t => t.Id, f);
        double mean = v.Values.Average(), sd = Math.Sqrt(v.Values.Average(x => (x - mean) * (x - mean)));
        return v.ToDictionary(kv => kv.Key, kv => sd < 1e-9 ? 0 : (kv.Value - mean) / sd);
    }

    /// <summary>A fixed "hunch" in about −1…1 (the sum of two even draws from a hash, so middling hunches are commonest).</summary>
    public static double Hunch(ulong seed, int season, string key, int teamId)
    {
        ulong h = Mix(seed ^ Hash($"{season}:{key}:{teamId}"));
        double a = (h & 0xFFFFFFFF) / (double)0xFFFFFFFF, b = (h >> 32) / (double)0xFFFFFFFF;
        return a + b - 1;
    }

    /// <summary>FNV-1a (stable across processes, unlike <c>string.GetHashCode</c>) with a final mix, so near names spread.</summary>
    public static ulong Hash(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return Mix(h);
    }

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static string Ord(int n) => AwardsEngine.Ordinal(n);

    /// <summary>The columnist's preseason line about your team, in his voice.</summary>
    public static string PreseasonLine(Columnist w, int pick, int of, string team, string table)
    {
        bool top = pick <= Math.Max(1, of / 5), bottom = pick > of - Math.Max(1, of / 4);
        string q = w.Voice switch
        {
            ColumnVoice.Numbers => top ? $"The roster grades out near the top of the {table}. {Ord(pick)} is where the numbers put {team}."
                : bottom ? $"I ran it every way I could. The rotation projects {Ord(pick)} of {of}."
                : $"{team} project {Ord(pick)}: a middle-of-the-table rotation on paper.",
            ColumnVoice.OldSchool => top ? $"{team} have the bodies to win it. {Ord(pick)} and I won't lose sleep over it."
                : bottom ? $"Too young, too soft. {Ord(pick)}, and that's being kind."
                : $"{Ord(pick)}. Nobody wins anything in October.",
            ColumnVoice.Beat => top ? $"I've watched every practice. This group is good enough to finish {Ord(pick)}, and I don't say that lightly."
                : bottom ? $"It's going to be a long winter. I have {team} {Ord(pick)}."
                : $"I have {team} {Ord(pick)}. Another year like last and the questions start.",
            _ => top ? $"{team} are a real contender in the {table}. I have them {Ord(pick)}."
                : bottom ? $"A rebuilding year: {Ord(pick)} of {of} in the {table}."
                : $"{Ord(pick)} in the {table}: good enough to be in the conversation, not yet at the top of it.",
        };
        return $"{w.Name} ({w.Outlet}): \"{q}\"";
    }

    /// <summary>The verdict on a columnist's call about your team, in his voice ("He picked you 11th. You won the conference.").</summary>
    public static string VerdictLine(Columnist w, int pick, int finish, string team, string table)
    {
        int delta = pick - finish; // positive: better than picked
        // Three steps each way: about right (within two places), a clear miss (3-4), a big miss (5+, or a title from 4th or lower).
        int step = delta >= 5 || (finish == 1 && pick >= 4) ? 2 : delta >= 3 ? 1 : delta <= -5 ? -2 : delta <= -3 ? -1 : 0;
        string fact = pick == 1 && finish == 1 ? $"{w.Name} ({w.Outlet}) picked you to win the {table}, and you did."
            : finish == 1 ? $"{w.Name} ({w.Outlet}) picked you {Ord(pick)}. You won the {table}."
            : $"{w.Name} ({w.Outlet}) picked you {Ord(pick)}. You finished {Ord(finish)}.";
        string q = (w.Voice, step) switch
        {
            (ColumnVoice.Numbers, 2) => $"My model had {team} {Ord(pick)}. The model was wrong, and I'll say so: they were far better than their numbers.",
            (ColumnVoice.Numbers, 1) => "Better than the model said. I'll be looking at why.",
            (ColumnVoice.Numbers, -1) => "A little under what the roster projected.",
            (ColumnVoice.Numbers, -2) => $"I had them {Ord(pick)}. On paper this roster was much better than {Ord(finish)}. Something didn't translate.",
            (ColumnVoice.Numbers, _) => "About where the numbers said. The model will take it.",
            (ColumnVoice.OldSchool, 2) => "I was wrong about them. Good for them. Don't expect me to make a habit of it.",
            (ColumnVoice.OldSchool, 1) => "Tougher than I gave them credit for.",
            (ColumnVoice.OldSchool, -1) => "Soft. I've seen it before.",
            (ColumnVoice.OldSchool, -2) => "I said they'd be good. They weren't tough enough to be.",
            (ColumnVoice.OldSchool, _) => "Told you so.",
            (ColumnVoice.Beat, 2) => "I'd like my October column back. Nobody in this town saw that coming, me included.",
            (ColumnVoice.Beat, 1) => "Better than I wrote in October, and I'm glad to say so.",
            (ColumnVoice.Beat, -1) => "Not quite what I expected in October.",
            (ColumnVoice.Beat, -2) => "I believed in this group. The season said otherwise, and the questions start now.",
            (ColumnVoice.Beat, _) => "Close to what I wrote in October. This town knew what it had.",
            (_, 2) => $"One of the stories of the season in the {table}.",
            (_, 1) => "A step ahead of where I had them.",
            (_, -1) => "A step short of where I had them.",
            (_, -2) => "A disappointing year for a program with that kind of talent.",
            _ => "Right where they belonged.",
        };
        return $"{fact} \"{q}\"";
    }
}
