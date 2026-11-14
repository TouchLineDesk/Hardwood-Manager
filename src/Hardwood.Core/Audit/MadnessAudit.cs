using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;

namespace Hardwood.Core.Audit;

/// <summary>
/// The college game measured season by season (C4 step 0, review #7): how often March Madness favourites win, how
/// the titles are shared by seed, how far the best programs stand above the bubble, and how much college games score.
/// Fed by <see cref="BalanceAudit"/> (one tournament a season) and by <see cref="MadnessAudit.Run"/> (the college world
/// alone, hundreds of tournaments).
/// </summary>
public sealed class MadnessTally
{
    /// <summary>Games and wins for the better seed, by the two seed lines (better first). First Four games are left out.</summary>
    public Dictionary<(int Hi, int Lo), (int Games, int HiWins)> Matchups { get; } = new();
    /// <summary>Titles by the champion's seed line (index 1-16).</summary>
    public int[] TitlesBySeed { get; } = new int[17];
    public int Tournaments { get; private set; }
    public List<double> OneSeedTitleShare { get; } = new();
    public List<double> OneVsSixteen { get; } = new();
    public List<double> TwoVsFifteen { get; } = new();
    public List<double> FiveVsTwelve { get; } = new();
    public List<double> Upsets { get; } = new();
    /// <summary>Average strength (weighted top-eight ability) of the 1-seeds and of the 16-seeds.</summary>
    public List<double> OneSeedStrength { get; } = new();
    public List<double> SixteenSeedStrength { get; } = new();
    public List<double> FiveSeedStrength { get; } = new();
    public List<double> TwelveSeedStrength { get; } = new();
    /// <summary>The ten most prestigious programs' average strength, and the 60th-68th by strength (the bubble).</summary>
    public List<double> TopTenPrestigeStrength { get; } = new();
    public List<double> BubbleStrength { get; } = new();
    public List<double> FiveStarsAtTopTwenty { get; } = new();
    public List<double> ProgramsAt99 { get; } = new();
    /// <summary>Points a side: quick results (everyone else's season) and the full engine (March Madness).</summary>
    public List<double> QuickPoints { get; } = new();
    public List<double> EnginePoints { get; } = new();
    /// <summary>The best overall record of the season (win share, March Madness included).</summary>
    public List<double> BestRecord { get; } = new();

    public double Rate(int hi, int lo) => Matchups.TryGetValue((hi, lo), out var m) && m.Games > 0 ? m.HiWins / (double)m.Games : double.NaN;

    /// <summary>Read a finished college season (call after its champion is crowned, before the summer).</summary>
    public void Add(CollegeWorld cw)
    {
        var s = cw.Season;
        if (s.ChampionId is not { } champ || s.Seeds.Count == 0) return;
        Tournaments++;
        int champSeed = s.Seeds.GetValueOrDefault(champ, 16);
        TitlesBySeed[champSeed]++;
        OneSeedTitleShare.Add(champSeed == 1 ? 1 : 0);
        int games = 0, upsets = 0;
        var round1 = new Dictionary<(int, int), (int g, int w)>();
        foreach (var g in s.Games.Where(g => g.Event == CollegeWorld.Madness && g.Played && g.Round >= 1))
        {
            int sh = s.Seeds.GetValueOrDefault(g.HomeId, 16), sa = s.Seeds.GetValueOrDefault(g.AwayId, 16);
            int hiId = sh <= sa ? g.HomeId : g.AwayId, hi = Math.Min(sh, sa), lo = Math.Max(sh, sa);
            bool hiWon = g.WinnerId == hiId;
            if (hi != lo)
            {
                var m = Matchups.GetValueOrDefault((hi, lo));
                Matchups[(hi, lo)] = (m.Games + 1, m.HiWins + (hiWon ? 1 : 0));
                if (g.Round == 1) { var r = round1.GetValueOrDefault((hi, lo)); round1[(hi, lo)] = (r.g + 1, r.w + (hiWon ? 1 : 0)); }
            }
            games++;
            if (!hiWon && lo - hi >= 4) upsets++;
        }
        double R(int a, int b) => round1.TryGetValue((a, b), out var x) && x.g > 0 ? x.w / (double)x.g : double.NaN;
        if (!double.IsNaN(R(1, 16))) OneVsSixteen.Add(R(1, 16));
        if (!double.IsNaN(R(2, 15))) TwoVsFifteen.Add(R(2, 15));
        if (!double.IsNaN(R(5, 12))) FiveVsTwelve.Add(R(5, 12));
        Upsets.Add(games == 0 ? 0 : upsets / (double)games);

        double Str(int id) => QuickSim.Strength(cw.Program(id)!);
        OneSeedStrength.Add(s.Seeds.Where(kv => kv.Value == 1).Average(kv => Str(kv.Key)));
        SixteenSeedStrength.Add(s.Seeds.Where(kv => kv.Value == 16).Average(kv => Str(kv.Key)));
        FiveSeedStrength.Add(s.Seeds.Where(kv => kv.Value == 5).Average(kv => Str(kv.Key)));
        TwelveSeedStrength.Add(s.Seeds.Where(kv => kv.Value == 12).Average(kv => Str(kv.Key)));
        TopTenPrestigeStrength.Add(cw.Programs.OrderByDescending(t => cw.Prestige[t.Id]).Take(10).Average(t => QuickSim.Strength(t)));
        BubbleStrength.Add(cw.Programs.Select(t => QuickSim.Strength(t)).OrderByDescending(x => x).Skip(59).Take(9).Average());
        BestRecord.Add(cw.Programs.Select(t => cw.Record(t.Id)).Where(x => x.wins + x.losses > 0).Max(x => x.wins / (double)(x.wins + x.losses)));
        ProgramsAt99.Add(cw.Programs.Count(t => cw.Prestige[t.Id] >= 98.5));
        if (cw.Recruiting)
        {
            var top20 = cw.Programs.OrderByDescending(t => cw.Prestige[t.Id]).Take(20).Select(t => t.Id).ToHashSet();
            var fives = cw.Recruits.Values.Where(r => r.ClassOf == s.Year + 1 && r.Stars >= 5 && r.CommittedTo != null).ToList();
            if (fives.Count > 0) FiveStarsAtTopTwenty.Add(fives.Count(r => top20.Contains(r.CommittedTo!.Value)) / (double)fives.Count);
        }

        var quick = s.Games.Where(g => g.Played && g.Event != CollegeWorld.Madness && g.HomeId != cw.ManagedProgramId && g.AwayId != cw.ManagedProgramId).ToList();
        var engine = s.Games.Where(g => g.Played && g.Event == CollegeWorld.Madness).ToList();
        if (quick.Count > 0) QuickPoints.Add(quick.Average(g => (g.HomePts!.Value + g.AwayPts!.Value) / 2.0));
        if (engine.Count > 0) EnginePoints.Add(engine.Average(g => (g.HomePts!.Value + g.AwayPts!.Value) / 2.0));
    }

    /// <summary>The college rows of an audit report. Seed rates are gated more loosely on short runs (four games a season).</summary>
    public void AddMetrics(List<AuditMetric> m, int seasons)
    {
        bool longRun = seasons >= 50;
        const string g = "College";
        m.Add(new(g, "March Madness: 1-seeds beat 16-seeds", "0.000", OneVsSixteen, longRun ? 0.93 : 0.85, 1.0, "NCAA 1985-2024: 99%; target 95% (#182) less sampling"));
        m.Add(new(g, "March Madness: 2-seeds beat 15-seeds", "0.000", TwoVsFifteen, longRun ? 0.86 : 0.75, 1.0, "NCAA: 93%"));
        m.Add(new(g, "March Madness: 5-seeds beat 12-seeds", "0.000", FiveVsTwelve, longRun ? 0.56 : 0.40, longRun ? 0.74 : 0.90, "NCAA: 64%"));
        m.Add(new(g, "March Madness: titles won by a 1-seed", "0.00", OneSeedTitleShare, longRun ? 0.42 : 0.20, longRun ? 0.68 : 0.90, "NCAA 1985-2024: about 60%"));
        m.Add(new(g, "March Madness: titles won by a 16-seed", "0", new() { TitlesBySeed[16] }, 0, 0, "never"));
        m.Add(new(g, "March Madness: upsets (4+ seed lines) per game", "0.000", Upsets, null, null, "NCAA ~20-25% of games"));
        m.Add(new(g, "Strength: 1-seeds", "0.0", OneSeedStrength, null, null, ""));
        m.Add(new(g, "Strength: 16-seeds", "0.0", SixteenSeedStrength, null, null, ""));
        m.Add(new(g, "Strength: 5-seeds", "0.0", FiveSeedStrength, null, null, ""));
        m.Add(new(g, "Strength: 12-seeds", "0.0", TwelveSeedStrength, null, null, ""));
        m.Add(new(g, "Strength: ten most prestigious programs", "0.0", TopTenPrestigeStrength, null, null, "review #7: 80-84 from year 5"));
        m.Add(new(g, "Strength: 60th-68th best programs", "0.0", BubbleStrength, null, null, ""));
        if (FiveStarsAtTopTwenty.Count > 0)
            m.Add(new(g, "Five-stars signing with a top-20 prestige program", "0.00", FiveStarsAtTopTwenty, null, null, "real: most 5-stars go to blue bloods"));
        m.Add(new(g, "Best record (win share)", "0.000", BestRecord, null, null, "NCAA: usually 30-3 to 34-1 (.91-.97)"));
        m.Add(new(g, "Programs at prestige 99", "0.0", ProgramsAt99, null, null, "review #7: eight after 8 seasons"));
        m.Add(new(g, "Points per team per game (quick results)", "0.0", QuickPoints, 66, 78, "NCAA D-I ~70-74"));
        m.Add(new(g, "Points per team per game (full engine, March Madness)", "0.0", EnginePoints, 66, 78, "NCAA tournament ~68-74"));
    }
}

/// <summary>The college world on its own, season after season: March Madness numbers over many tournaments.</summary>
public static class MadnessAudit
{
    public static (MadnessTally Tally, TimeSpan Elapsed) Run(ulong seed, int tournaments, Action<string>? progress = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rng = new Rng(seed);
        var start = new DateOnly(2026, 10, 1);
        int nextId = 2_000_000;
        var gen = new PlayerGenerator(new Rng(seed ^ 0x51), nextId);
        var cw = CollegeWorld.Generate(gen, rng, start, real: true); // the real Division I only (D61 B, #478)
        var sim = new GameSimulator();
        var tally = new MadnessTally();
        int year = 2026;
        for (int t = 0; t < tournaments; t++, year++)
        {
            cw.PlayThrough(new DateOnly(year + 1, 4, 30), sim, rng);
            tally.Add(cw);
            if ((t + 1) % 10 == 0) progress?.Invoke($"{t + 1} tournaments: 1v16 {tally.Rate(1, 16):P0}, 1-seed titles {tally.TitlesBySeed[1]}/{tally.Tournaments}");
            cw.PlayThrough(new DateOnly(year + 1, 6, 30), sim, rng);
            var pool = new List<Player>();
            gen = new PlayerGenerator(new Rng(rng.NextULong()), gen.NextId);
            cw.Summer(new DateOnly(year + 1, 7, 1), pool, gen, new DevelopmentEngine(rng), rng);
            cw.News.Clear();
            cw.StartSeason(year + 1, rng);
        }
        return (tally, sw.Elapsed);
    }

    public static string Report(MadnessTally t, ulong seed, TimeSpan elapsed)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# March Madness audit: seed {seed}, {t.Tournaments} tournaments ({elapsed.TotalSeconds:0} s)");
        sb.AppendLine();
        sb.AppendLine("| Matchup | Games | Better seed wins |");
        sb.AppendLine("|---|---|---|");
        foreach (var (hi, lo) in new[] { (1, 16), (2, 15), (3, 14), (4, 13), (5, 12), (6, 11), (7, 10), (8, 9), (1, 8), (1, 9), (1, 4), (1, 2) })
            if (t.Matchups.TryGetValue((hi, lo), out var m)) sb.AppendLine($"| {hi} v {lo} | {m.Games} | {m.HiWins / (double)m.Games:P0} |");
        sb.AppendLine();
        sb.AppendLine("Titles by seed: " + string.Join(", ", Enumerable.Range(1, 16).Where(i => t.TitlesBySeed[i] > 0).Select(i => $"{i}: {t.TitlesBySeed[i]}")));
        sb.AppendLine();
        var rows = new List<AuditMetric>();
        t.AddMetrics(rows, t.Tournaments);
        sb.AppendLine("| Metric | Mean | Range | Status |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var r in rows)
            sb.AppendLine($"| {r.Name} | {r.Mean.ToString(r.Format)} | {(r.Low is null ? "" : $"{r.Low}-{r.High}")} | {r.Status} |");
        return sb.ToString();
    }
}
