using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;

namespace Hardwood.Core.College;

/// <summary>
/// K12b (run 137): a program's banners. Real counts from ncaa_programs.json (titles, Final Fours, NCAA tournaments, all time),
/// the years since 1985 from ncaa_history.json, and this career's own seasons on top. A fictional world's programs have only
/// this career's seasons. Years are tournament years (the 2026-27 season's tournament is 2027).
/// </summary>
public sealed record ProgramPast(Team Program, int Titles, IReadOnlyList<int> TitleYears, int FinalFours, IReadOnlyList<int> FinalFourYears,
                                 int Appearances, int SaveTitles, int SaveFinalFours)
{
    /// <summary>Titles won before the years the game lists (before 1985 in a real world).</summary>
    public int TitlesEarlier => Math.Max(0, Titles - TitleYears.Count);
    public int? LastTitle => TitleYears.Count > 0 ? TitleYears[^1] : null;

    /// <summary>The Team page's chip: "8 titles" / "17 Final Fours · 64 NCAA tournaments".</summary>
    public (string Value, string Sub) Chip() =>
        Titles > 0 ? ($"{Titles} title{S(Titles)}", $"{FinalFours} Final Four{S(FinalFours)} · {Appearances} NCAA tournament{S(Appearances)}")
        : FinalFours > 0 ? ($"{FinalFours} Final Four{S(FinalFours)}", $"{Appearances} NCAA tournament{S(Appearances)} · no title yet")
        : Appearances > 0 ? ($"{Appearances} NCAA tournament{S(Appearances)}", "no Final Four yet")
        : ("None yet", "no NCAA tournament yet");

    /// <summary>"National champions 1996, 1998, 2012 (and 5 times before 1985) · Final Fours since 1985: 1993, 1996 … ·
    /// 64 NCAA tournaments." Empty for a program with nothing to hang.</summary>
    public string Line(int firstListed)
    {
        if (Titles + FinalFours + Appearances == 0) return "";
        var parts = new List<string>();
        if (Titles > 0)
        {
            string years = string.Join(", ", TitleYears);
            string earlier = TitlesEarlier > 0 ? $"{TitlesEarlier} time{S(TitlesEarlier)} before {firstListed}" : "";
            parts.Add("National champions " + (years.Length > 0 && earlier.Length > 0 ? $"{years} (and {earlier})" : years.Length > 0 ? years : earlier));
        }
        if (FinalFours > 0)
            parts.Add(FinalFourYears.Count == 0 ? $"{FinalFours} Final Four{S(FinalFours)}, all before {firstListed}"
                : $"{FinalFours} Final Four{S(FinalFours)} (since {firstListed}: {string.Join(", ", FinalFourYears)})");
        parts.Add($"{Appearances} NCAA tournament{S(Appearances)}");
        return string.Join(" · ", parts);
    }

    private static string S(int n) => n == 1 ? "" : "s";
}

public sealed partial class CollegeWorld
{
    /// <summary>K12b: real NBA alumni were added to <see cref="Alumni"/> (once per career; saved). False in fictional worlds.</summary>
    public bool RealAlumniSeeded { get; set; }

    /// <summary>The program's entry in the real data pack (a real world's programs only).</summary>
    public RealProgram? RealOf(Team t) => IsReal ? RealColleges.Programs.FirstOrDefault(p => p.School == t.City) : null;

    /// <summary>The first tournament year with a listed champion: 1985 in a real world, else this career's first.</summary>
    public int FirstListedYear => IsReal ? RealCollegeHistory.FirstYear : History.Count > 0 ? History.Min(h => h.Year) + 1 : Season.Year + 1;

    /// <summary>This career's tournaments by program: (tournament year, round reached), from the season honours (CS5) and the
    /// champions list (older saves without honours still count their finals).</summary>
    private Dictionary<int, Dictionary<int, int>> SaveMarch()
    {
        var byTeam = new Dictionary<int, Dictionary<int, int>>();
        void Add(int team, int year, int reach)
        {
            if (!byTeam.TryGetValue(team, out var d)) byTeam[team] = d = new();
            d[year] = Math.Max(d.GetValueOrDefault(year, -1), reach);
        }
        foreach (var a in Awards)
            foreach (var (team, r) in a.Records)
                if (r.Length >= 3 && r[2] >= 0) Add(team, a.Year + 1, r[2]);
        foreach (var h in History) { Add(h.ChampionId, h.Year + 1, 7); Add(h.RunnerUpId, h.Year + 1, 6); }
        return byTeam;
    }

    /// <summary>A program's banners: real history (a real world) plus this career's tournaments.</summary>
    public ProgramPast PastOf(Team t) => PastOf(t, SaveMarch());

    private ProgramPast PastOf(Team t, Dictionary<int, Dictionary<int, int>> march)
    {
        var real = RealOf(t);
        var mine = march.GetValueOrDefault(t.Id) ?? new();
        // Only this career's own tournaments are added: the real counts already include 2026 and earlier.
        int after = real != null ? RealCollegeHistory.LastYear : int.MinValue;
        var saveTitles = mine.Where(kv => kv.Key > after && kv.Value >= 7).Select(kv => kv.Key).OrderBy(y => y).ToList();
        var saveFours = mine.Where(kv => kv.Key > after && kv.Value >= 5).Select(kv => kv.Key).OrderBy(y => y).ToList();
        int saveApps = mine.Count(kv => kv.Key > after && kv.Value >= 0);
        var titleYears = (real != null ? RealCollegeHistory.TitleYears(t.City) : new List<int>()).Concat(saveTitles).ToList();
        var fourYears = (real != null ? RealCollegeHistory.FinalFourYears(t.City) : new List<int>()).Concat(saveFours).ToList();
        // The all-time counts never fall below the years listed since 1985 (ncaa_programs.json has Florida at 5 Final Fours;
        // 1994, 2000, 2006, 2007, 2014 and 2025 are 6). The pack itself is left alone: its counts set starting prestige.
        int realTitles = real == null ? 0 : Math.Max(real.Titles, RealCollegeHistory.TitleYears(t.City).Count);
        int realFours = real == null ? 0 : Math.Max(real.FinalFours, RealCollegeHistory.FinalFourYears(t.City).Count);
        return new ProgramPast(t, realTitles + saveTitles.Count, titleYears, realFours + saveFours.Count, fourYears,
            (real?.Appearances ?? 0) + saveApps, saveTitles.Count, saveFours.Count);
    }

    /// <summary>Every program's banners (the records book).</summary>
    public List<ProgramPast> Pasts()
    {
        var march = SaveMarch();
        return Programs.Select(t => PastOf(t, march)).ToList();
    }

    /// <summary>This career's best seasons by record: (tournament year, program, wins, losses, round reached), most wins first.</summary>
    public List<(int Year, int TeamId, int W, int L, int Reach)> BestSeasons(int take) =>
        Awards.SelectMany(a => a.Records.Where(kv => kv.Value.Length >= 3 && kv.Value[0] + kv.Value[1] > 0)
                .Select(kv => (Year: a.Year + 1, TeamId: kv.Key, W: kv.Value[0], L: kv.Value[1], Reach: kv.Value[2])))
            .OrderByDescending(x => x.W - x.L).ThenByDescending(x => x.W).ThenBy(x => x.Year).Take(take).ToList();
}
