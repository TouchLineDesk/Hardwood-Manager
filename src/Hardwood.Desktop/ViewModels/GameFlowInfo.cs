using Avalonia.Media;
using Hardwood.Core.Engine;
using Hardwood.Desktop.Controls;

namespace Hardwood.Desktop.ViewModels;

/// <summary>One row of the quarter-by-quarter table.</summary>
public sealed record SplitRow(string Team, IReadOnlyList<string> Cells, string Total, bool Header = false)
{
    public double Opacity => Header ? 0.6 : 1;
}

/// <summary>One team-comparison row: the stat, each side's figure as shown, and the numbers the bar compares.</summary>
public sealed record TeamStatRow(string Label, string AwayText, string HomeText, double Away, double Home)
{
    public Color AwayColor { get; init; }
    public Color HomeColor { get; init; }
}

/// <summary>
/// What the live screen and the box score both show about a game's shape (roadmap 2, match day part 1): the period
/// splits table, the runs, and the flow chips. Built from <see cref="GameFlow"/>, so both screens agree.
/// </summary>
public static class GameFlowInfo
{
    /// <summary>Header row plus away and home rows: Q1 Q2 Q3 Q4 (OT…) and the total.</summary>
    public static List<SplitRow> Splits(GameResult r)
    {
        var (home, away) = GameFlow.Splits(r);
        // Games restored from a save have no timeline; fall back to the stored period scores.
        if (r.Flow.Count == 0 && r.Home.PointsByPeriod.Length > 0) { home = r.Home.PointsByPeriod; away = r.Away.PointsByPeriod; }
        int n = Math.Max(home.Length, away.Length);
        var head = Enumerable.Range(1, n).Select(p => GameFlow.PeriodName(r, p)).ToList();
        string Cell(int[] a, int i) => i < a.Length && (i < r.CurrentPeriod || r.Flow.Count == 0) ? a[i].ToString() : "–";
        return new()
        {
            new SplitRow("", head, "T", Header: true),
            new SplitRow(r.Away.Team.Abbreviation, Enumerable.Range(0, n).Select(i => Cell(away, i)).ToList(), r.Away.Points.ToString()),
            new SplitRow(r.Home.Team.Abbreviation, Enumerable.Range(0, n).Select(i => Cell(home, i)).ToList(), r.Home.Points.ToString()),
        };
    }

    /// <summary>Each run as a line, biggest first.</summary>
    public static List<string> RunLines(GameResult r) =>
        GameFlow.Runs(r).OrderByDescending(x => x.For - x.Against).ThenBy(x => x.StartElapsed).Select(x => GameFlow.Describe(r, x)).ToList();

    /// <summary>Lead changes, the biggest lead each way, and the biggest run.</summary>
    public static List<Chip> Chips(GameResult r)
    {
        var (h, a) = GameFlow.BiggestLeads(r);
        var chips = new List<Chip>
        {
            new("Lead changes", GameFlow.LeadChanges(r).ToString(), GameFlow.LeadChanges(r) >= 8 ? "A back-and-forth game" : "", "info"),
            new($"{r.Away.Team.Abbreviation} biggest lead", a.ToString(), "", "team"),
            new($"{r.Home.Team.Abbreviation} biggest lead", h.ToString(), "", "team"),
        };
        var best = GameFlow.Runs(r).OrderByDescending(x => x.For - x.Against).FirstOrDefault();
        chips.Add(best == null ? new("Biggest run", "None", $"No {GameFlow.RunMinPoints}+ point runs", "info")
                               : new("Biggest run", best.Score, best.Team, "warn"));
        return chips;
    }

    /// <summary>Side-by-side team stats (match day part 2): shooting, the glass, ball security, paint and bench points.</summary>
    public static List<TeamStatRow> TeamStats(GameResult r)
    {
        var a = r.Away; var h = r.Home;
        string Pct(int m, int att) => att == 0 ? "–" : $"{100.0 * m / att:0}%";
        double P(int m, int att) => att == 0 ? 0 : (double)m / att;
        int Paint(TeamBox b) => b.Lines.Sum(l => l.ZoneMakes[0]) * 2;
        int Bench(TeamBox b) => b.Lines.Where(l => !l.Started).Sum(l => l.Points);
        var (ac, hc) = Colours(r);
        return new List<TeamStatRow>
        {
            new("Field goals", $"{a.FGM}-{a.FGA} · {Pct(a.FGM, a.FGA)}", $"{h.FGM}-{h.FGA} · {Pct(h.FGM, h.FGA)}", P(a.FGM, a.FGA), P(h.FGM, h.FGA)),
            new("Three-pointers", $"{a.ThreePM}-{a.ThreePA} · {Pct(a.ThreePM, a.ThreePA)}", $"{h.ThreePM}-{h.ThreePA} · {Pct(h.ThreePM, h.ThreePA)}", P(a.ThreePM, a.ThreePA), P(h.ThreePM, h.ThreePA)),
            new("Free throws", $"{a.FTM}-{a.FTA}", $"{h.FTM}-{h.FTA}", a.FTM, h.FTM),
            new("Rebounds", $"{a.OReb + a.DReb} ({a.OReb} off)", $"{h.OReb + h.DReb} ({h.OReb} off)", a.OReb + a.DReb, h.OReb + h.DReb),
            new("Assists", a.Assists.ToString(), h.Assists.ToString(), a.Assists, h.Assists),
            // The bar for turnovers shows who protected the ball better: each side's share is the other's turnovers.
            new("Turnovers", a.Turnovers.ToString(), h.Turnovers.ToString(), h.Turnovers, a.Turnovers),
            new("Steals · blocks", $"{a.Steals} · {a.Blocks}", $"{h.Steals} · {h.Blocks}", a.Steals + a.Blocks, h.Steals + h.Blocks),
            new("Points at the rim", Paint(a).ToString(), Paint(h).ToString(), Paint(a), Paint(h)),
            new("Bench points", Bench(a).ToString(), Bench(h).ToString(), Bench(a), Bench(h)),
        }.Select(x => x with { AwayColor = ac, HomeColor = hc }).ToList();
    }

    /// <summary>"BOS 64%": the favourite's chance to win right now.</summary>
    public static string WinChance(GameResult r)
    {
        var wp = GameFlow.WinProbability(r);
        double home = wp.Count == 0 ? 50 : wp[^1].Percent;
        return home >= 50 ? $"{r.Home.Team.Abbreviation} {home:0}%" : $"{r.Away.Team.Abbreviation} {100 - home:0}%";
    }

    /// <summary>Team colours for the chart, nudged apart when the two clubs wear much the same colour.</summary>
    public static (Color Away, Color Home) Colours(GameResult r)
    {
        var home = ThemeService.AccentFor(r.Home.Team);
        var away = ThemeService.AccentFor(r.Away.Team);
        double d = Math.Abs(home.R - away.R) + Math.Abs(home.G - away.G) + Math.Abs(home.B - away.B);
        if (d < 120) away = Distance(home, Color.Parse("#4F8EF7")) > 150 ? Color.Parse("#4F8EF7") : Color.Parse("#E5E7EB");
        return (away, home);
    }

    private static double Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
}
