using System.Text;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.RealWorld;

/// <summary>
/// How well the real NBA's ratings track reality (`hardwood realcheck`): builds the real league, lists the best-rated
/// players and every team's strength, then plays whole regular seasons and compares each team's wins with its real
/// record last season and this season so far (correlation; the target is 0.8+ against a full real season).
/// </summary>
public static class RealCheck
{
    /// <summary>A team's strength: the minutes-weighted CA of its best ten (36, 34, 32, 30, 28, 24, 20, 14, 10, 8 minutes).</summary>
    public static double Strength(Team t)
    {
        double[] mins = { 36, 34, 32, 30, 28, 24, 20, 14, 10, 8 };
        var top = t.Roster.OrderByDescending(p => p.CurrentAbility).Take(mins.Length).ToList();
        return top.Select((p, i) => p.CurrentAbility * mins[i]).Sum() / mins.Take(top.Count).Sum();
    }

    public static League Build(ulong seed)
    {
        var league = new WorldGenerator(new Rng(seed)).Generate(RealWorldData.LeagueName, 2026, new DateOnly(2026, 10, 1));
        var world = Hardwood.Core.World.GameWorld.Generate(league, seed, Array.Empty<string>(), new DateOnly(2026, 10, 1));
        RealWorldData.Apply(league, world, seed);
        return league;
    }

    public static double Correlation(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        double mx = x.Average(), my = y.Average();
        double sxy = x.Zip(y).Sum(p => (p.First - mx) * (p.Second - my));
        double sx = Math.Sqrt(x.Sum(v => (v - mx) * (v - mx))), sy = Math.Sqrt(y.Sum(v => (v - my) * (v - my)));
        return sx * sy == 0 ? 0 : sxy / (sx * sy);
    }

    public static string Run(ulong seed, int seasons, Action<string>? progress = null)
    {
        var league = Build(seed);
        var sb = new StringBuilder();
        sb.AppendLine($"# Real NBA ratings check (seed {seed}, {seasons} seasons; data checked {RealPlayers.Checked})");
        sb.AppendLine();
        var all = league.Teams.SelectMany(t => t.Roster.Select(p => (t, p))).OrderByDescending(x => x.p.CurrentAbility).ToList();
        sb.AppendLine("## Top 30 by CA");
        foreach (var (t, p) in all.Take(30))
            sb.AppendLine($"- {p.Name} ({t.Abbreviation}, {p.PrimaryPosition}, {p.Age(new DateOnly(2026, 10, 1))}): CA {p.CurrentAbility}, PA {p.PotentialAbility}");
        sb.AppendLine();
        var realNames = RealPlayers.Lines.Select(l => l.Name).ToHashSet();
        int generated = league.Teams.Sum(t => t.Roster.Count(p => !realNames.Contains(p.Name)));
        sb.AppendLine($"Real players placed: {league.Teams.Sum(t => t.Roster.Count(p => realNames.Contains(p.Name)))} of {RealPlayers.Lines.Count}; generated fillers: {generated}.");
        sb.AppendLine();
        // R2 part 2b: the players on real rosters who hadn't played yet this season, rated from last season alone.
        var notYet = RealPlayers.Lines.Where(l => l.NotYet).ToList();
        var byName = all.GroupBy(x => x.p.Name).ToDictionary(g => g.Key, g => g.First());
        sb.AppendLine($"## Not yet seen in 2026-27 ({notYet.Count}; rated from 2025-26 alone, bench level with no line)");
        sb.AppendLine(string.Join("; ", notYet.Select(l => byName.TryGetValue(l.Name, out var x)
            ? $"{l.Name} {x.t.Abbreviation} {x.p.CurrentAbility}" : $"{l.Name} ({l.Team}, cut)")));
        sb.AppendLine();
        // Real careers carried in (R2 part 2b): the all-time view the records book starts from.
        var careers = all.Where(x => league.Careers.ContainsKey(x.p.Id)).Select(x => (x.p, c: league.Careers[x.p.Id])).ToList();
        sb.AppendLine($"## Real careers carried in: {careers.Count} players");
        sb.AppendLine(string.Join("; ", careers.OrderByDescending(x => x.c.Points).Take(10)
            .Select(x => $"{x.p.Name} {x.c.Points:N0} pts, {x.c.Games:N0} g, {x.c.Seasons} seasons, {x.c.Mvps} MVP")));
        sb.AppendLine();
        sb.Append(Market(league));

        var wins = league.Teams.ToDictionary(t => t, _ => 0.0);
        return Seasons(league, seed, seasons, wins, sb, progress);
    }

    /// <summary>What a star costs (R2 part 2a): each club's asking price for the top 20 by CA, with his real contract,
    /// the cheapest star of CA 165+, and payrolls against the cap. The target: no CA 165+ player under $60M.</summary>
    public static string Market(League league)
    {
        var sb = new StringBuilder();
        var on = new DateOnly(league.Season, 10, 1);
        var rows = league.Teams.SelectMany(t => t.Roster.Select(p => (t, p, ask: TransferMarket.Ask(p, t, on, 1, league.Finance))))
                               .OrderByDescending(x => x.p.CurrentAbility).ToList();
        sb.AppendLine("## The market (asking prices on opening day)");
        sb.AppendLine("| Player | Team | CA | Born | Height | Contract | Asking price |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var (t, p, ask) in rows.Take(20))
            sb.AppendLine($"| {p.Name} | {t.Abbreviation} | {p.CurrentAbility} | {p.BirthDate:yyyy-MM-dd} | {p.HeightCm} cm | ${p.Contract!.Salary / 1e6:F1}M × {p.Contract.Years} | ${ask / 1e6:F1}M |");
        var stars = rows.Where(x => x.p.CurrentAbility >= 165).ToList();
        if (stars.Count > 0)
        {
            var cheap = stars.MinBy(x => x.ask);
            sb.AppendLine();
            sb.AppendLine($"Stars (CA 165+): {stars.Count}; cheapest asking price {cheap.p.Name} ${cheap.ask / 1e6:F1}M; under $60M: {stars.Count(x => x.ask < 60_000_000)}.");
        }
        var top10 = rows.Take(10).Select(x => (double)x.ask).ToList();
        var byAsk = rows.OrderByDescending(x => x.ask).Take(10).Count(x => rows.Take(10).Contains(x));
        sb.AppendLine($"Of the 10 highest asking prices, {byAsk} belong to the top 10 by CA.");
        var pays = league.Teams.Select(t => ContractMarket.Payroll(t)).OrderBy(x => x).ToList();
        sb.AppendLine($"Payrolls: ${pays[0] / 1e6:F0}M-{pays[^1] / 1e6:F0}M, average ${pays.Average() / 1e6:F0}M (cap ${league.Finance.SalaryCap / 1e6:F0}M; the real 2026-27 cap is ${RealPlayers.RealCap / 1e6:F2}M).");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string Seasons(League league, ulong seed, int seasons, Dictionary<Team, double> wins, StringBuilder sb, Action<string>? progress)
    {
        for (int s = 0; s < seasons; s++)
        {
            var copy = s == 0 ? league : Build(seed + (ulong)s * 101);
            var rng = new Rng(seed ^ (0xC0FFEEUL + (ulong)s));
            var schedule = ScheduleBuilder.Build(copy, new DateOnly(2026, 10, 21), rng);
            var outcome = new SeasonSimulator(rng).Run(copy, schedule);
            foreach (var r in outcome.Standings.Conference(Conference.East).Concat(outcome.Standings.Conference(Conference.West)))
                wins[league.Teams.First(t => t.Id == r.Team.Id)] += (double)r.Wins / seasons;
            progress?.Invoke($"season {s + 1} of {seasons}");
        }

        var last = RealPlayers.LastSeason.ToDictionary(r => r.Team);
        var now = RealPlayers.ThisSeason.ToDictionary(r => r.Team);
        var rows = league.Teams.Select(t => (t, str: Strength(t), w: wins[t], last: last[t.Abbreviation], now: now[t.Abbreviation]))
                               .OrderByDescending(x => x.w).ToList();
        sb.AppendLine("## Teams (simulated wins, average of the seasons)");
        sb.AppendLine("| Team | Strength | Sim W | 2025-26 W | 2026-27 so far |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var x in rows)
            sb.AppendLine($"| {x.t.FullName} | {x.str:F1} | {x.w:F1} | {x.last.W} | {x.now.W}-{x.now.L} |");
        sb.AppendLine();
        var sim = rows.Select(x => x.w).ToList();
        double rLast = Correlation(sim, rows.Select(x => (double)x.last.W).ToList());
        double rNow = Correlation(sim, rows.Select(x => x.now.Pct).ToList());
        double rStr = Correlation(rows.Select(x => x.str).ToList(), rows.Select(x => (double)x.last.W).ToList());
        sb.AppendLine($"Correlation of simulated wins with 2025-26 wins: **{rLast:F2}**; with 2026-27 win % so far: **{rNow:F2}**; strength with 2025-26 wins: {rStr:F2}.");
        sb.AppendLine($"Simulated wins range {sim.Min():F0}-{sim.Max():F0} (2025-26 real: {last.Values.Min(r => r.W)}-{last.Values.Max(r => r.W)}).");
        return sb.ToString();
    }
}
