using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

public enum ShotZone { Rim, ShortMid, LongMid, CornerThree, AboveBreakThree }

public static class ShotZones
{
    public static bool IsThree(this ShotZone z) => z is ShotZone.CornerThree or ShotZone.AboveBreakThree;
}

public sealed class PlayerLine
{
    public required Player Player { get; init; }
    public bool Started { get; set; }
    public double Seconds { get; set; }
    public int Points { get; set; }
    public int FGM { get; set; }
    public int FGA { get; set; }
    public int ThreePM { get; set; }
    public int ThreePA { get; set; }
    public int FTM { get; set; }
    public int FTA { get; set; }
    public int OReb { get; set; }
    public int DReb { get; set; }
    public int Assists { get; set; }
    public int Steals { get; set; }
    public int Blocks { get; set; }
    public int Turnovers { get; set; }
    public int Fouls { get; set; }
    public int PlusMinus { get; set; }
    public int[] ZoneAttempts { get; } = new int[5];
    public int[] ZoneMakes { get; } = new int[5];

    public int Rebounds => OReb + DReb;
    public double Minutes => Seconds / 60.0;
    public bool FouledOut { get; set; }

    public override string ToString() =>
        $"{Player.Name,-24} {Minutes,5:F1} {Points,3} {FGM,2}-{FGA,-2} {ThreePM,2}-{ThreePA,-2} {FTM,2}-{FTA,-2} {Rebounds,3} {Assists,3} {Steals,2} {Blocks,2} {Turnovers,2} {Fouls,2} {PlusMinus,4:+#;-#;0}";
}

public sealed class TeamBox
{
    public required Team Team { get; init; }
    public List<PlayerLine> Lines { get; } = new();
    public int[] PointsByPeriod { get; set; } = Array.Empty<int>();
    public int Possessions { get; set; }
    public int TeamFoulsThisPeriod { get; set; }
    public int Timeouts { get; set; } = 7;

    /// <summary>Set when a result is restored from a save without its box score.</summary>
    public int? FixedPoints { get; set; }
    public int Points => FixedPoints ?? Lines.Sum(l => l.Points);
    public int FGM => Lines.Sum(l => l.FGM);
    public int FGA => Lines.Sum(l => l.FGA);
    public int ThreePM => Lines.Sum(l => l.ThreePM);
    public int ThreePA => Lines.Sum(l => l.ThreePA);
    public int FTM => Lines.Sum(l => l.FTM);
    public int FTA => Lines.Sum(l => l.FTA);
    public int OReb => Lines.Sum(l => l.OReb);
    public int DReb => Lines.Sum(l => l.DReb);
    public int Assists => Lines.Sum(l => l.Assists);
    public int Steals => Lines.Sum(l => l.Steals);
    public int Blocks => Lines.Sum(l => l.Blocks);
    public int Turnovers => Lines.Sum(l => l.Turnovers);
    public int Fouls => Lines.Sum(l => l.Fouls);

    public PlayerLine LineFor(Player p) => Lines.First(l => l.Player == p);
}

public sealed class GameResult
{
    public required TeamBox Home { get; init; }
    public required TeamBox Away { get; init; }
    public int Overtimes { get; set; }
    public ulong Seed { get; init; }
    public DateOnly? Date { get; init; }
    public List<string> PlayLog { get; } = new();
    /// <summary>The score after every possession that changed it (games with a play log only; see <see cref="GameFlow"/>).</summary>
    public List<FlowPoint> Flow { get; } = new();
    /// <summary>Seconds played so far and the period being played (kept up to date while a live game runs).</summary>
    public double Elapsed { get; set; }
    public int CurrentPeriod { get; set; } = 1;
    public int Periods { get; set; } = 4;
    public int PeriodSeconds { get; set; } = 720;
    public int OvertimeSeconds { get; set; } = 300;
    /// <summary>The home side's expected winning margin at tip-off, and the spread around it (for win probability).</summary>
    public double PregameEdge { get; set; }
    public double MarginSpread { get; set; } = 13;
    public List<(Player player, Injury injury)> Injuries { get; } = new();

    public TeamBox Winner => Home.Points > Away.Points ? Home : Away;
    public TeamBox Loser => Home.Points > Away.Points ? Away : Home;

    public string Summary() => $"{Away.Team.Abbreviation} {Away.Points} @ {Home.Team.Abbreviation} {Home.Points}" + (Overtimes > 0 ? $" ({Overtimes}OT)" : "");
}
