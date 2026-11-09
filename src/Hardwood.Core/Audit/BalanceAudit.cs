using System.Text;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;

namespace Hardwood.Core.Audit;

/// <summary>One measured quantity, with the range real basketball lands in (null = for information only).</summary>
public sealed record AuditMetric(string Group, string Name, string Format, List<double> PerSeason, double? Low, double? High, string Source)
{
    public double Mean => PerSeason.Count == 0 ? double.NaN : PerSeason.Average();
    public string Status => Low is null || High is null || double.IsNaN(Mean) ? "info" : Mean < Low ? "LOW" : Mean > High ? "HIGH" : "ok";
    public bool OutOfRange => Status is "LOW" or "HIGH";
}

public sealed class AuditReport
{
    public ulong Seed { get; init; }
    public int Seasons { get; init; }
    public TimeSpan Elapsed { get; set; }
    public List<AuditMetric> Metrics { get; } = new();
    public List<string> Champions { get; } = new();
    public Dictionary<string, List<string>> WorldChampions { get; } = new();
    public List<string> Notes { get; } = new();

    public AuditMetric this[string name] => Metrics.First(m => m.Name == name);
    public IEnumerable<AuditMetric> Flags => Metrics.Where(m => m.OutOfRange);

    public string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Balance audit");
        sb.AppendLine();
        sb.AppendLine($"Seed {Seed} · {Seasons} seasons · {Elapsed.TotalSeconds:F0}s · {Flags.Count()} of {Metrics.Count(m => m.Low != null)} checked metrics out of range");
        sb.AppendLine();
        if (Flags.Any())
        {
            sb.AppendLine("**Out of range:** " + string.Join(", ", Flags.Select(f => $"{f.Name} ({f.Status.ToLower()})")));
            sb.AppendLine();
        }
        foreach (var g in Metrics.GroupBy(m => m.Group))
        {
            sb.AppendLine($"## {g.Key}");
            sb.AppendLine();
            sb.AppendLine("| Metric | Average | Seasons (min–max) | Real range | Status |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var m in g)
            {
                string f(double v) => v.ToString(m.Format);
                var range = m.Low is { } lo && m.High is { } hi ? $"{f(lo)}–{f(hi)}" : "";
                var spread = m.PerSeason.Count > 1 ? $"{f(m.PerSeason.Min())}–{f(m.PerSeason.Max())}" : "";
                var status = m.Status switch { "ok" => "ok", "info" => "", _ => $"**{m.Status}**" };
                sb.AppendLine($"| {m.Name} | {f(m.Mean)} | {spread} | {range} | {status} |");
            }
            sb.AppendLine();
        }
        sb.AppendLine("## Champions");
        sb.AppendLine();
        sb.AppendLine("- NABL: " + string.Join(" · ", Champions));
        foreach (var (k, v) in WorldChampions) sb.AppendLine($"- {k}: " + string.Join(" · ", v));
        sb.AppendLine();
        if (Notes.Count > 0)
        {
            sb.AppendLine("## Notes");
            sb.AppendLine();
            foreach (var n in Notes) sb.AppendLine("- " + n);
            sb.AppendLine();
        }
        sb.AppendLine("Real ranges: NBA 2018–2025 league averages (NABL); EuroLeague, ACB, BSL, NBL and CBA recent seasons (overseas). Sources are noted per metric in `BalanceAudit.cs`.");
        return sb.ToString();
    }
}

/// <summary>
/// Plays a whole career headlessly (the NABL season by season with offseasons, and the world around it) and measures
/// what a basketball fan would notice: scoring and shooting, records, dynasties, star counts, how players age and
/// develop, retirements, the draft, cap health and overseas scoring. Each metric carries the range real basketball
/// lands in, so drift after a change shows up as a flag. Run it with `hardwood audit`.
/// </summary>
public static class BalanceAudit
{
    public static AuditReport Run(ulong seed, int seasons, IEnumerable<string>? fullSimLeagues = null, Action<string>? progress = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var report = new AuditReport { Seed = seed, Seasons = seasons };
        var league = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, seed, fullSimLeagues ?? Array.Empty<string>(), new DateOnly(2026, 10, 1));
        var career = new Career(new Rng(seed ^ 0xA0D17));
        var sim = new GameSimulator();
        var fin = league.Finance;

        var pts = new List<double>(); var poss = new List<double>(); var fg = new List<double>(); var tp = new List<double>();
        var tpRate = new List<double>(); var ftRate = new List<double>(); var home = new List<double>(); var ot = new List<double>();
        var best = new List<double>(); var worst = new List<double>(); var spread = new List<double>(); var leader = new List<double>();
        var meanCa = new List<double>(); var stars = new List<double>(); var meanAge = new List<double>();
        var devYoung = new List<double>(); var devPrime = new List<double>(); var devOld = new List<double>();
        var retiredN = new List<double>(); var retiredAge = new List<double>(); var firstPick = new List<double>(); var rookieTop5 = new List<double>();
        var overCap = new List<double>(); var payroll = new List<double>(); var faPool = new List<double>();
        var devByAge = new Dictionary<int, List<int>>();
        var worldPts = new Dictionary<string, List<double>>(); var worldCa = new Dictionary<string, List<double>>();

        for (int s = 0; s < seasons; s++)
        {
            int year = league.Season;
            var start = new DateOnly(year, 10, 1);
            var players = league.AllActivePlayers.ToList();
            meanCa.Add(players.Average(p => p.CurrentAbility));
            stars.Add(players.Count(p => p.CurrentAbility >= 160));
            meanAge.Add(players.Average(p => p.Age(start)));
            overCap.Add(league.Teams.Count(t => ContractMarket.Payroll(t) > fin.HardCap + fin.MinSalary * 2));
            payroll.Add(league.Teams.Average(t => ContractMarket.Payroll(t)) / fin.HardCap);
            faPool.Add(league.FreeAgents.Count);
            foreach (var c in world.Competitions.Where(c => c.Clubs.Count > 0))
                (worldCa.TryGetValue(c.Profile.Name, out var l) ? l : worldCa[c.Profile.Name] = new()).Add(c.Clubs.SelectMany(t => t.Roster).Average(p => p.CurrentAbility));

            progress?.Invoke($"Season {year}-{(year + 1) % 100:00}");
            var played = career.PlaySeason(league);
            var games = played.Outcome.RegularSeason;
            var boxes = games.SelectMany(g => new[] { g.Home, g.Away }).ToList();
            pts.Add(boxes.Average(b => b.Points));
            poss.Add(boxes.Average(b => b.Possessions));
            fg.Add(boxes.Sum(b => b.FGM) / (double)boxes.Sum(b => b.FGA));
            tp.Add(boxes.Sum(b => b.ThreePM) / (double)boxes.Sum(b => b.ThreePA));
            tpRate.Add(boxes.Sum(b => b.ThreePA) / (double)boxes.Sum(b => b.FGA));
            ftRate.Add(boxes.Sum(b => b.FTA) / (double)boxes.Sum(b => b.FGA));
            home.Add(games.Count(g => g.Winner == g.Home) / (double)games.Count);
            ot.Add(games.Count(g => g.Overtimes > 0) / (double)games.Count);
            var table = played.Outcome.Standings.All();
            best.Add(table.Max(r => r.Wins)); worst.Add(table.Min(r => r.Wins));
            double mw = table.Average(r => r.Wins);
            spread.Add(Math.Sqrt(table.Average(r => (r.Wins - mw) * (r.Wins - mw))));
            leader.Add(played.Stats.Values.Where(x => x.Games >= 58).Select(x => x.Ppg).DefaultIfEmpty(0).Max());
            report.Champions.Add($"{year}: {played.Outcome.Champion?.Abbreviation ?? "?"}");

            var off = played.Offseason;
            var dev = off.Development;
            foreach (var d in dev) (devByAge.TryGetValue(d.Age, out var l) ? l : devByAge[d.Age] = new()).Add(d.NewCa - d.OldCa);
            devYoung.Add(dev.Where(d => d.Age <= 22).Select(d => (double)(d.NewCa - d.OldCa)).DefaultIfEmpty(0).Average());
            devPrime.Add(dev.Where(d => d.Age is >= 26 and <= 29).Select(d => (double)(d.NewCa - d.OldCa)).DefaultIfEmpty(0).Average());
            devOld.Add(dev.Where(d => d.Age >= 32).Select(d => (double)(d.NewCa - d.OldCa)).DefaultIfEmpty(0).Average());
            retiredN.Add(off.Retired.Count);
            retiredAge.Add(off.Retired.Select(p => (double)p.Age(new DateOnly(year + 1, 7, 1))).DefaultIfEmpty(0).Average());
            var draft = off.Draft.OrderBy(d => d.Pick).ToList();
            if (draft.Count > 0) { firstPick.Add(draft[0].Player.CurrentAbility); rookieTop5.Add(draft.Take(5).Average(d => d.Player.CurrentAbility)); }

            // The world: finish its season, measure, then roll it over to the year the NABL just started.
            world.FinishSeasons(sim);
            foreach (var c in world.Playing)
            {
                var regular = c.Season.Fixtures.Where(f => f.Played && f.Stage == CompetitionSeason.RegularSeason).ToList();
                if (regular.Count > 0) (worldPts.TryGetValue(c.Profile.Name, out var l) ? l : worldPts[c.Profile.Name] = new()).Add(regular.Average(f => (f.HomePts!.Value + f.AwayPts!.Value) / 2.0));
                if (c.Season.ChampionId is { } id) (report.WorldChampions.TryGetValue(c.Profile.Name, out var w) ? w : report.WorldChampions[c.Profile.Name] = new()).Add($"{year}: {world.TeamById(id)?.Abbreviation}");
            }
            world.Rollover(league.Season, sim);
        }

        var m = report.Metrics;
        // NBA league averages 2018-19 to 2024-25 (basketball-reference): pts 111-118, pace 98-101, FG% .460-.476,
        // 3P% .353-.367, 3PA/FGA .359-.42, FTA/FGA .24-.26, home win% .54-.58 (2021 bubble excluded), OT ~5-6% of games.
        m.Add(new("NABL games", "Points per team per game", "0.0", pts, 108, 120, "NBA 2018-25"));
        m.Add(new("NABL games", "Possessions per team per game", "0.0", poss, 96, 103, "NBA pace 2018-25"));
        m.Add(new("NABL games", "Field goal %", "0.000", fg, 0.450, 0.485, "NBA 2018-25"));
        m.Add(new("NABL games", "Three-point %", "0.000", tp, 0.345, 0.375, "NBA 2018-25"));
        m.Add(new("NABL games", "Threes as share of shots", "0.000", tpRate, 0.34, 0.44, "NBA 2018-25"));
        m.Add(new("NABL games", "Free throws per shot (FTA/FGA)", "0.000", ftRate, 0.22, 0.28, "NBA 2018-25"));
        m.Add(new("NABL games", "Home win %", "0.000", home, 0.53, 0.60, "NBA 2018-25"));
        m.Add(new("NABL games", "Games going to overtime", "0.000", ot, 0.04, 0.08, "NBA ~5-6%"));
        // Records: NBA best record 57-68 wins, worst 15-25, spread (SD of wins) ~11-14 over recent seasons.
        m.Add(new("NABL competitive balance", "Best record (wins)", "0", best, 56, 68, "NBA 2015-25"));
        m.Add(new("NABL competitive balance", "Worst record (wins)", "0", worst, 12, 26, "NBA 2015-25"));
        m.Add(new("NABL competitive balance", "Spread of wins (std dev)", "0.0", spread, 10, 14.5, "NBA 2015-25"));
        m.Add(new("NABL competitive balance", "Scoring leader (points per game)", "0.0", leader, 28, 36, "NBA 2015-25"));
        int distinct = report.Champions.Select(c => c[(c.IndexOf(':') + 2)..]).Distinct().Count();
        // Real leagues have dynasties and repeat winners: the NBA had 7 different champions in 2015-24, 6 in 2005-14.
        m.Add(new("NABL competitive balance", "Different champions", "0", new() { distinct }, seasons >= 5 ? Math.Ceiling(seasons * 0.4) : null, seasons >= 5 ? Math.Ceiling(seasons * 0.8) : null, "NBA: 6-7 different per 10 seasons"));
        int mostTitles = report.Champions.GroupBy(c => c[(c.IndexOf(':') + 2)..]).Select(g => g.Count()).DefaultIfEmpty(0).Max();
        m.Add(new("NABL competitive balance", "Most titles by one team", "0", new() { mostTitles }, seasons >= 5 ? 1 : null, seasons >= 5 ? Math.Ceiling(seasons * 0.4) : null, "NBA: 4 in 10 (GSW 2015-24)"));
        // Players: league talent should neither inflate nor collapse; stars ~1-2 per 3 teams.
        m.Add(new("NABL players", "Mean ability (CA) of rostered players", "0.0", meanCa, meanCa[0] - 5, meanCa[0] + 5, "stable within ±5 of season 1"));
        m.Add(new("NABL players", "Stars (CA 160+)", "0.0", stars, Math.Max(3, stars[0] * 0.6), stars[0] * 1.5 + 1, "stable around season 1"));
        m.Add(new("NABL players", "Mean age", "0.0", meanAge, 25.5, 27.5, "NBA ~26-27"));
        // Peak age: the youngest age at which players, on average, stop improving (a plain "best average ability by age"
        // is skewed by survivorship: only great players are still around at 35).
        var peak = devByAge.Where(kv => kv.Key >= 20 && kv.Value.Count >= 10).OrderBy(kv => kv.Key)
            .FirstOrDefault(kv => kv.Value.Average() <= 0).Key;
        m.Add(new("NABL players", "Age players stop improving", "0", new() { peak }, 26, 30, "NBA aging curves peak ~27"));
        m.Add(new("NABL development", "Yearly ability change, age 22 and under", "+0.0;-0.0", devYoung, 2, 12, "young players improve"));
        m.Add(new("NABL development", "Yearly ability change, age 26-29", "+0.0;-0.0", devPrime, -2, 3, "prime: roughly flat"));
        m.Add(new("NABL development", "Yearly ability change, age 32+", "+0.0;-0.0", devOld, -10, -1, "decline"));
        m.Add(new("NABL development", "Retirements per season", "0", retiredN, 15, 50, "NBA ~20-40"));
        m.Add(new("NABL development", "Average retirement age", "0.0", retiredAge, 32, 37, "NBA ~33-35"));
        m.Add(new("NABL draft", "No. 1 pick ability (CA)", "0", firstPick, null, null, ""));
        m.Add(new("NABL draft", "Top-5 picks average ability (CA)", "0", rookieTop5, null, null, ""));
        m.Add(new("NABL money", "Teams over the cap allowance at season start", "0", overCap, 0, 0, "must be 0 (hard cap)"));
        m.Add(new("NABL money", "Average payroll as share of the cap", "0.00", payroll, 0.80, 1.00, "hard-cap league"));
        m.Add(new("NABL money", "Free agents available at season start", "0", faPool, null, null, ""));
        // Overseas: EuroLeague ~80-84 pts per team, ACB ~82-85, Turkish BSL ~82-86, NBL ~88-92, CBA ~100-105 (fast, high-scoring).
        foreach (var (name, list) in worldPts)
        {
            (double lo, double hi) = name switch { "Australian League" => (78, 96), "China Pro League" => (78, 108), _ => (74, 90) };
            m.Add(new("Overseas", $"{name}: points per team per game", "0.0", list, lo, hi, "recent seasons"));
        }
        foreach (var (name, list) in worldCa)
            m.Add(new("Overseas", $"{name}: mean ability (CA)", "0.0", list, list[0] - 6, list[0] + 6, "stable within ±6 of season 1"));
        report.Elapsed = sw.Elapsed;
        return report;
    }
}
