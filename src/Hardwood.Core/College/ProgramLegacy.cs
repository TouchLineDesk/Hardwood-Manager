using System.Text.Json.Serialization;
using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>Slice 8 of the revised launch plan (#1164, run 199): one player's college career at one program, as the
/// program's record book counts it (this career's seasons only). Names are kept, so a graduate is still named after he leaves.</summary>
public sealed class LegacyCareer
{
    public int PlayerId { get; init; }
    public string Name { get; set; } = "";
    public string Pos { get; set; } = "";
    /// <summary>His first and last college seasons at the program (the year each began).</summary>
    public int First { get; set; }
    public int Last { get; set; }
    public int G { get; set; }
    public int Pts { get; set; }
    public int Reb { get; set; }
    public int Ast { get; set; }
    public int Stl { get; set; }
    public int Blk { get; set; }
    /// <summary>His college career is over (graduated, or gone from the program).</summary>
    public bool Done { get; set; }

    [JsonIgnore] public string Years => ProgramLegacy.Span(First, Last);
    [JsonIgnore] public string Line => G == 0 ? "" : $"{Pts:N0} points, {Reb:N0} rebounds, {Ast:N0} assists in {G} games";
}

/// <summary>One player's season at a program (the single-season records).</summary>
public sealed class LegacySeason
{
    public int Year { get; init; }
    public int PlayerId { get; init; }
    public string Name { get; init; } = "";
    public int G { get; init; }
    public int Pts { get; init; }
    public int Reb { get; init; }
    public int Ast { get; init; }
}

/// <summary>A member of a program's Hall of Fame, inducted the night his last season ends.</summary>
public sealed class LegacyInductee
{
    public int PlayerId { get; init; }
    public string Name { get; init; } = "";
    public string Pos { get; init; } = "";
    /// <summary>The class he was inducted with: the tournament year (the 2027-28 season's class is 2028).</summary>
    public int Class { get; init; }
    public int First { get; init; }
    public int Last { get; init; }
    public int G { get; init; }
    public int Pts { get; init; }
    public int Reb { get; init; }
    public int Ast { get; init; }
    /// <summary>The legacy score that put him in (see <see cref="ProgramLegacy.Score"/>).</summary>
    public double Score { get; init; }
    /// <summary>Why he is in, biggest first ("National Player of the Year 2027-28", "2,104 points, the program record").</summary>
    public List<string> Why { get; init; } = new();
    /// <summary>His jersey number, when the program retired it (null: inducted, jersey still worn).</summary>
    public int? Jersey { get; init; }

    [JsonIgnore] public string Years => ProgramLegacy.Span(First, Last);
    [JsonIgnore] public string Line => G == 0 ? "" : $"{Pts:N0} points, {Reb:N0} rebounds, {Ast:N0} assists in {G} games";
}

/// <summary>
/// Slice 8 (run 199): a program's legacy: its record book (career and single-season leaders), its Hall of Fame and the jerseys
/// hanging in its rafters. Built from this career's seasons only, the night of each national final, for every program. No random
/// draw: a graduate's legacy score decides it.
/// <list type="bullet">
/// <item><b>The score:</b> National Player of the Year 8, Most Outstanding Player 4, All-America 5 / 3.5 / 2.5 (first, second,
/// third team), Defensive Player of the Year 3, Freshman of the Year 1.5, conference Player of the Year 3, all-conference first
/// team 1.5; a national title in one of his seasons 3, a national final 1.5, a Final Four 1; 1,000 / 1,500 / 2,000 career points
/// 1 / 2 / 3; 1,000 rebounds 1.5; 600 assists 1; the program's career leader in points, rebounds or assists +1 each.</item>
/// <item><b>The Hall of Fame</b> takes a graduate at <see cref="HallScore"/> or more; <b>the rafters</b> (his jersey retired) at
/// <see cref="JerseyScore"/> or more.</item>
/// </list>
/// The book keeps every current player's running totals, and of the finished careers and seasons only the top
/// <see cref="Keep"/> in each column (and the Hall), so a long career's save stays small.
/// </summary>
public sealed class ProgramLegacy
{
    public const double HallScore = 7, JerseyScore = 14;
    public const int Keep = 5;
    /// <summary>The numbers a college player can wear (digits 0-5 only).</summary>
    public static readonly int[] Numbers = { 0, 1, 2, 3, 4, 5, 10, 11, 12, 13, 14, 15, 20, 21, 22, 23, 24, 25, 30, 31, 32, 33, 34, 35, 40, 41, 42, 43, 44, 45, 50, 51, 52, 53, 54, 55 };

    public int TeamId { get; init; }
    /// <summary>The last college season the book counted (the year it began; 0: none yet).</summary>
    public int LastYear { get; set; }
    /// <summary>The first college season the book counted (0: none yet).</summary>
    public int FirstYear { get; set; }
    public List<LegacyCareer> Careers { get; set; } = new();
    public List<LegacySeason> Seasons { get; set; } = new();
    public List<LegacyInductee> Hall { get; set; } = new();

    [JsonIgnore] public IEnumerable<LegacyInductee> Rafters => Hall.Where(h => h.Jersey != null).OrderBy(h => h.Class).ThenBy(h => h.Jersey);
    [JsonIgnore] public bool Empty => Careers.Count == 0 && Hall.Count == 0;

    /// <summary>"2026-27" for one season, "2026-30" for several.</summary>
    public static string Span(int first, int last) => first == last ? $"{first}-{(first + 1) % 100:00}" : $"{first}-{(last + 1) % 100:00}";

    /// <summary>The top careers in a column (finished and current alike), most first.</summary>
    public List<LegacyCareer> Leaders(Func<LegacyCareer, int> of, int take = Keep) =>
        Careers.Where(c => of(c) > 0).OrderByDescending(of).ThenBy(c => c.First).ThenBy(c => c.PlayerId).Take(take).ToList();

    public List<LegacySeason> BestSeasons(Func<LegacySeason, int> of, int take = Keep) =>
        Seasons.Where(s => of(s) > 0).OrderByDescending(of).ThenBy(s => s.Year).ThenBy(s => s.PlayerId).Take(take).ToList();

    /// <summary>A jersey number for a player, the same every time (from his id), skipping the numbers already retired here.</summary>
    public int JerseyFor(int playerId)
    {
        var taken = Hall.Where(h => h.Jersey != null).Select(h => h.Jersey!.Value).ToHashSet();
        uint h = unchecked((uint)playerId * 2654435761u);
        for (int i = 0; i < Numbers.Length; i++)
        {
            int n = Numbers[(int)((h + (uint)i) % (uint)Numbers.Length)];
            if (!taken.Contains(n)) return n;
        }
        return Numbers[(int)(h % (uint)Numbers.Length)];
    }

    /// <summary>Drop finished careers and seasons outside every column's top <see cref="Keep"/> (Hall members stay).</summary>
    internal void Prune()
    {
        var keepCareers = new HashSet<LegacyCareer>(Careers.Where(c => !c.Done));
        foreach (var of in new Func<LegacyCareer, int>[] { c => c.Pts, c => c.Reb, c => c.Ast })
            keepCareers.UnionWith(Leaders(of));
        var hall = Hall.Select(h => h.PlayerId).ToHashSet();
        Careers = Careers.Where(c => keepCareers.Contains(c) || hall.Contains(c.PlayerId)).ToList();
        var keepSeasons = new HashSet<LegacySeason>();
        foreach (var of in new Func<LegacySeason, int>[] { s => s.Pts, s => s.Reb, s => s.Ast })
            keepSeasons.UnionWith(BestSeasons(of));
        Seasons = Seasons.Where(keepSeasons.Contains).ToList();
    }
}

public sealed partial class CollegeWorld
{
    /// <summary>Slice 8 (run 199): every program's legacy book (saved; missing before v0.187.0, so an older save starts its
    /// books at its next national final).</summary>
    public List<ProgramLegacy> Legacy { get; } = new();

    private static readonly ProgramLegacy NoLegacy = new();

    /// <summary>A program's legacy book (an empty one if it has none yet; not added).</summary>
    public ProgramLegacy LegacyOf(int teamId) => Legacy.FirstOrDefault(l => l.TeamId == teamId) ?? NoLegacy;

    private ProgramLegacy LegacyBook(int teamId)
    {
        var book = Legacy.FirstOrDefault(l => l.TeamId == teamId);
        if (book == null) Legacy.Add(book = new ProgramLegacy { TeamId = teamId });
        return book;
    }

    /// <summary>Called the night of the national final, after the honours are voted: every program's book counts the season,
    /// its graduates' careers close, and the best join the Hall of Fame (the very best into the rafters). Returns this
    /// season's inductees by program. No random draw.</summary>
    public List<(Team Program, LegacyInductee Who)> CloseLegacy()
    {
        var s = Season;
        var inducted = new List<(Team, LegacyInductee)>();
        foreach (var t in Programs)
        {
            var book = LegacyBook(t.Id);
            if (book.LastYear >= s.Year) continue;
            book.LastYear = s.Year;
            if (book.FirstYear == 0) book.FirstYear = s.Year;
            var roster = t.Roster.ToDictionary(p => p.Id);
            foreach (var (pid, st) in s.Stats)
            {
                if (st.TeamId != t.Id || st.G <= 0 || !roster.TryGetValue(pid, out var p)) continue;
                var c = book.Careers.FirstOrDefault(x => x.PlayerId == pid);
                if (c == null) book.Careers.Add(c = new LegacyCareer { PlayerId = pid, First = s.Year });
                c.Name = p.Name; c.Pos = p.PrimaryPosition.ToString(); c.Last = s.Year;
                c.G += st.G; c.Pts += st.Pts; c.Reb += st.Reb; c.Ast += st.Ast; c.Stl += st.Stl; c.Blk += st.Blk;
                book.Seasons.Add(new LegacySeason { Year = s.Year, PlayerId = pid, Name = p.Name, G = st.G, Pts = st.Pts, Reb = st.Reb, Ast = st.Ast });
            }
            var graduating = FinalYear(t).Select(p => p.Id).ToHashSet();
            foreach (var c in book.Careers.Where(c => !c.Done && (graduating.Contains(c.PlayerId) || !roster.ContainsKey(c.PlayerId))).ToList())
            {
                c.Done = true;
                if (!graduating.Contains(c.PlayerId)) continue; // gone from the program without finishing: no ceremony
                var (score, why) = LegacyScore(t, book, c);
                if (score < ProgramLegacy.HallScore) continue;
                var who = new LegacyInductee
                {
                    PlayerId = c.PlayerId, Name = c.Name, Pos = c.Pos, Class = s.Year + 1, First = c.First, Last = c.Last,
                    G = c.G, Pts = c.Pts, Reb = c.Reb, Ast = c.Ast, Score = Math.Round(score, 1), Why = why,
                    Jersey = score >= ProgramLegacy.JerseyScore ? book.JerseyFor(c.PlayerId) : null,
                };
                book.Hall.Add(who);
                inducted.Add((t, who));
            }
            book.Prune();
        }
        var rafters = inducted.Where(x => x.Item2.Jersey != null).ToList();
        if (rafters.Count > 0)
            News.Add((rafters.Count == 1 ? $"Into the rafters: {rafters[0].Item1.FullName} retire No. {rafters[0].Item2.Jersey} for {rafters[0].Item2.Name}"
                    : $"Into the rafters: {rafters.Count} programs retire a jersey",
                string.Join("\n", rafters.Select(x => $"{x.Item1.FullName}: No. {x.Item2.Jersey}, {x.Item2.Name} ({x.Item2.Years}). {string.Join("; ", x.Item2.Why.Take(3))}."))
                + $"\n{inducted.Count} graduate{(inducted.Count == 1 ? "" : "s")} joined their program's Hall of Fame tonight."));
        return inducted;
    }

    /// <summary>A finished career's legacy score and its reasons, biggest first.</summary>
    internal (double Score, List<string> Why) LegacyScore(Team t, ProgramLegacy book, LegacyCareer c)
    {
        var why = new List<(double Pts, string Text)>();
        void Add(double pts, string text) => why.Add((pts, text));
        foreach (var a in Awards.Where(a => a.Year >= c.First && a.Year <= c.Last).OrderByDescending(a => a.Year))
        {
            string y = CollegeAwards.SeasonText(a.Year);
            int id = c.PlayerId;
            if (a.PlayerOfYearId == id) Add(8, $"National Player of the Year {y}");
            if (a.OutstandingId == id) Add(4, $"Most Outstanding Player {y}");
            int team = a.AllAmerica.FindIndex(x => x.Contains(id));
            if (team >= 0) Add(team switch { 0 => 5, 1 => 3.5, _ => 2.5 }, $"All-America {CollegeAwardsText.TeamName(team)} {y}");
            if (a.DefensiveId == id) Add(3, $"Defensive Player of the Year {y}");
            if (a.FreshmanId == id) Add(1.5, $"Freshman of the Year {y}");
            foreach (var (key, pid) in a.ConferencePlayer)
                if (pid == id) Add(3, $"{ConferenceShortName(key)} Player of the Year {y}");
            foreach (var (key, five) in a.AllConference)
                if (five.Contains(id) && a.ConferencePlayer.GetValueOrDefault(key) != id) Add(1.5, $"All-{ConferenceShortName(key)} first team {y}");
            int reach = a.Records.TryGetValue(t.Id, out var r) && r.Length >= 3 ? r[2] : -1;
            if (reach >= 7) Add(3, $"National champion {y}");
            else if (reach == 6) Add(1.5, $"National final {y}");
            else if (reach == 5) Add(1, $"Final Four {y}");
        }
        if (c.Pts >= 2000) Add(3, $"{c.Pts:N0} career points");
        else if (c.Pts >= 1500) Add(2, $"{c.Pts:N0} career points");
        else if (c.Pts >= 1000) Add(1, $"{c.Pts:N0} career points");
        if (c.Reb >= 1000) Add(1.5, $"{c.Reb:N0} career rebounds");
        if (c.Ast >= 600) Add(1, $"{c.Ast:N0} career assists");
        // The program's career leader (this career's book) in a column, once the total means something.
        bool Leads(Func<LegacyCareer, int> of, int least) => of(c) >= least && book.Careers.Where(x => x != c).All(x => of(x) < of(c));
        if (Leads(x => x.Pts, 1000)) Add(1, "the program's career scoring leader");
        if (Leads(x => x.Reb, 500)) Add(1, "the program's career rebounding leader");
        if (Leads(x => x.Ast, 300)) Add(1, "the program's career assists leader");
        return (why.Sum(w => w.Pts), why.OrderByDescending(w => w.Pts).Select(w => w.Text).ToList());
    }

    /// <summary>A player's legacy honours ("Program Hall of Fame 2028, Duke", "No. 23 retired by Duke"), for <see cref="HonoursOf"/>.</summary>
    public IEnumerable<string> LegacyHonoursOf(int playerId)
    {
        foreach (var book in Legacy)
            foreach (var h in book.Hall.Where(h => h.PlayerId == playerId))
            {
                string name = Program(book.TeamId)?.FullName ?? "his program";
                if (h.Jersey is { } n) yield return $"No. {n} retired by {name}";
                yield return $"{name} Hall of Fame, class of {h.Class}";
            }
    }
}
