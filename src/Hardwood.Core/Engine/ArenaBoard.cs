using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>One line of the jumbotron's team comparison: the stat, each side's value, and who leads it
/// (-1 away, +1 home, 0 level).</summary>
public sealed record BoardRow(string Label, string Away, string Home, int Lead);

/// <summary>One player in an "On the floor" panel: his position and surname, his line so far and his energy (0.3–1.0,
/// the engine's own value).</summary>
public sealed record FloorRow(Player Player, string Pos, string Name, int Pts, int Reb, int Ast, int Pf, string Fg, double Energy)
{
    /// <summary>Energy as a whole percentage, as the bar is drawn.</summary>
    public int EnergyPercent => (int)Math.Round(100 * Energy);
    /// <summary>"fresh" (85%+), "ok" (70–84%: the engine starts looking to sub him below 70) or "tired".</summary>
    public string EnergyBand => EnergyPercent >= 85 ? "fresh" : EnergyPercent >= 70 ? "ok" : "tired";
    /// <summary>Four or more fouls in a 40-minute game (five fouls out), three or more in the first half.</summary>
    public bool FoulTrouble { get; init; }
}

/// <summary>The momentum chip: home points minus away points over the last <see cref="ArenaBoard.MomentumWindow"/>
/// seconds played, and the line it shows.</summary>
public sealed record MomentumRead(int Swing, string Label, bool? HomeHas);

/// <summary>
/// B2b (run 141, broadcast-presentation-design.md): what the arena's jumbotron, the two "On the floor" panels and the
/// momentum chip show during a college live game. <b>Presentation only:</b> everything is read from the box score, the
/// score flow and the engine's energy; nothing is written back and no random number is drawn.
/// </summary>
public static class ArenaBoard
{
    /// <summary>The momentum chip looks at the last four minutes played.</summary>
    public const double MomentumWindow = 240;

    /// <summary>The jumbotron's comparison, from the two team boxes: FG, 3PT, FT, rebounds, assists, steals, blocks, turnovers.</summary>
    public static IReadOnlyList<BoardRow> Compare(GameResult r)
    {
        var a = r.Away; var h = r.Home;
        static int Cmp(double away, double home) => away > home ? -1 : home > away ? 1 : 0;
        static double P(int m, int att) => att == 0 ? 0 : (double)m / att;
        static string Pct(int m, int att) => att == 0 ? "–" : $"{Math.Round(100.0 * m / att):0}%";
        return new List<BoardRow>
        {
            new("FG", $"{a.FGM}-{a.FGA}", $"{h.FGM}-{h.FGA}", Cmp(P(a.FGM, a.FGA), P(h.FGM, h.FGA))),
            new("FG%", Pct(a.FGM, a.FGA), Pct(h.FGM, h.FGA), Cmp(P(a.FGM, a.FGA), P(h.FGM, h.FGA))),
            new("3PT", $"{a.ThreePM}-{a.ThreePA}", $"{h.ThreePM}-{h.ThreePA}", Cmp(a.ThreePM, h.ThreePM)),
            new("FT", $"{a.FTM}-{a.FTA}", $"{h.FTM}-{h.FTA}", Cmp(a.FTM, h.FTM)),
            new("REB", Reb(a).ToString(), Reb(h).ToString(), Cmp(Reb(a), Reb(h))),
            new("AST", a.Assists.ToString(), h.Assists.ToString(), Cmp(a.Assists, h.Assists)),
            new("STL", a.Steals.ToString(), h.Steals.ToString(), Cmp(a.Steals, h.Steals)),
            new("BLK", a.Blocks.ToString(), h.Blocks.ToString(), Cmp(a.Blocks, h.Blocks)),
            // Fewer turnovers leads.
            new("TO", a.Turnovers.ToString(), h.Turnovers.ToString(), Cmp(h.Turnovers, a.Turnovers)),
        };
    }

    private static int Reb(TeamBox b) => b.Lines.Sum(l => l.Rebounds);

    /// <summary>The five on the floor, point guard to centre, each with his line so far and his energy.</summary>
    public static IReadOnlyList<FloorRow> OnFloor(TeamBox box, IReadOnlyList<Player> five, Func<Player, double> energy, int periods = 2)
    {
        int trouble = periods == 2 ? 4 : 5;
        return five.Where(p => p != null).OrderBy(p => p.PrimaryPosition).Select(p =>
        {
            var l = box.LineFor(p);
            return new FloorRow(p, p.PrimaryPosition.ToString(), Surname(p), l.Points, l.Rebounds, l.Assists, l.Fouls, $"{l.FGM}-{l.FGA}",
                Math.Clamp(energy(p), 0, 1)) { FoulTrouble = l.Fouls >= trouble };
        }).ToList();
    }

    /// <summary>The surname as a broadcast shows it ("Gilgeous-Alexander", "Hughes"), capped so a panel never runs over.</summary>
    public static string Surname(Player p)
    {
        var n = string.IsNullOrWhiteSpace(p.LastName) ? p.Name : p.LastName;
        return n.Length <= 12 ? n : n[..11] + ".";
    }

    /// <summary>
    /// The momentum chip: the points each side scored over the last four minutes played. "MIA +8", "EVEN" at level
    /// (and before the first basket).
    /// </summary>
    public static MomentumRead Momentum(GameResult r)
    {
        var flow = r.Flow;
        if (flow.Count == 0) return new MomentumRead(0, "EVEN", null);
        double cutoff = r.Elapsed - MomentumWindow;
        int h0 = 0, a0 = 0;
        foreach (var p in flow)
        {
            if (p.Elapsed > cutoff) break;
            h0 = p.Home; a0 = p.Away;
        }
        var now = flow[^1];
        int swing = (now.Home - h0) - (now.Away - a0);
        if (swing == 0) return new MomentumRead(0, "EVEN", null);
        var team = swing > 0 ? r.Home.Team : r.Away.Team;
        return new MomentumRead(swing, $"{team.Abbreviation} +{Math.Abs(swing)}", swing > 0);
    }

    /// <summary>The share of the momentum bar the home side fills (0.5 at level; a 12-point swing or more fills it).</summary>
    public static double HomeShare(MomentumRead m) => Math.Clamp(0.5 + m.Swing / 24.0, 0, 1);
}
