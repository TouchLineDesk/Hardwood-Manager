using Hardwood.Core.Engine;

namespace Hardwood.Core.Season;

/// <summary>
/// Per-team-per-game league averages with real-world targets. This is the calibration harness:
/// every engine change is judged against these numbers.
/// </summary>
public sealed class LeagueAverages
{
    public int Games { get; private set; }
    public double Points, FGM, FGA, ThreePM, ThreePA, FTM, FTA, OReb, DReb, Assists, Steals, Blocks, Turnovers, Fouls, Possessions;
    public double[] ZoneAttempts = new double[5];
    public double[] ZoneMakes = new double[5];
    public double Overtimes;
    public double HomeWinPct;

    public static LeagueAverages From(IEnumerable<GameResult> games)
    {
        var a = new LeagueAverages();
        int teamGames = 0, homeWins = 0;
        foreach (var g in games)
        {
            a.Games++;
            if (g.Winner == g.Home) homeWins++;
            a.Overtimes += g.Overtimes;
            foreach (var box in new[] { g.Home, g.Away })
            {
                teamGames++;
                a.Points += box.Points; a.FGM += box.FGM; a.FGA += box.FGA;
                a.ThreePM += box.ThreePM; a.ThreePA += box.ThreePA; a.FTM += box.FTM; a.FTA += box.FTA;
                a.OReb += box.OReb; a.DReb += box.DReb; a.Assists += box.Assists; a.Steals += box.Steals;
                a.Blocks += box.Blocks; a.Turnovers += box.Turnovers; a.Fouls += box.Fouls;
                a.Possessions += box.Possessions;
                foreach (var l in box.Lines)
                    for (int z = 0; z < 5; z++) { a.ZoneAttempts[z] += l.ZoneAttempts[z]; a.ZoneMakes[z] += l.ZoneMakes[z]; }
            }
        }
        if (teamGames == 0) return a;
        a.Points /= teamGames; a.FGM /= teamGames; a.FGA /= teamGames; a.ThreePM /= teamGames; a.ThreePA /= teamGames;
        a.FTM /= teamGames; a.FTA /= teamGames; a.OReb /= teamGames; a.DReb /= teamGames; a.Assists /= teamGames;
        a.Steals /= teamGames; a.Blocks /= teamGames; a.Turnovers /= teamGames; a.Fouls /= teamGames; a.Possessions /= teamGames;
        a.HomeWinPct = (double)homeWins / a.Games;
        return a;
    }

    public string Report()
    {
        // Targets: modern NBA (approximate 2022–2025 league averages).
        var rows = new (string stat, double value, double target)[]
        {
            ("Points", Points, 114.0),
            ("Possessions", Possessions, 99.0),
            ("FG%", FGM / FGA * 100, 47.5),
            ("FGA", FGA, 88.5),
            ("3PA", ThreePA, 35.0),
            ("3P%", ThreePM / ThreePA * 100, 36.5),
            ("FTA", FTA, 22.0),
            ("FT%", FTM / FTA * 100, 78.0),
            ("Off reb", OReb, 10.5),
            ("Def reb", DReb, 33.0),
            ("Assists", Assists, 26.5),
            ("Steals", Steals, 7.5),
            ("Blocks", Blocks, 5.0),
            ("Turnovers", Turnovers, 13.5),
            ("Fouls", Fouls, 19.0),
            ("Rim FG%", ZoneMakes[0] / ZoneAttempts[0] * 100, 65.0),
            ("Short mid FG%", ZoneMakes[1] / ZoneAttempts[1] * 100, 44.0),
            ("Long mid FG%", ZoneMakes[2] / ZoneAttempts[2] * 100, 41.0),
            ("Corner 3 FG%", ZoneMakes[3] / ZoneAttempts[3] * 100, 39.0),
            ("Above-break 3 FG%", ZoneMakes[4] / ZoneAttempts[4] * 100, 35.0),
            ("Rim share of FGA %", ZoneAttempts[0] / ZoneAttempts.Sum() * 100, 32.0),
            ("Home win %", HomeWinPct * 100, 55.0),
            ("OT games %", Overtimes / Games * 100, 6.0),
        };
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{"Stat (per team-game)",-22} {"Sim",8} {"Target",8} {"Diff",8}");
        foreach (var (stat, value, target) in rows)
            sb.AppendLine($"{stat,-22} {value,8:F1} {target,8:F1} {value - target,8:+0.0;-0.0;0.0}");
        return sb.ToString();
    }
}
