namespace Hardwood.Core.Model;

public enum Conference { East, West }

public sealed class Team
{
    public int Id { get; init; }
    public required string City { get; init; }
    public required string Name { get; init; }
    public string FullName => $"{City} {Name}";
    public required string Abbreviation { get; init; }
    public Conference Conference { get; init; }
    public required string Division { get; init; }
    public int ArenaCapacity { get; init; } = 18000;

    public List<Player> Roster { get; } = new();

    /// <summary>Team tactics used by the engine.</summary>
    public Tactic Tactic { get; set; } = new();

    public override string ToString() => FullName;
}

/// <summary>Minimal tactic set for the prototype; the full system/role model comes in the vertical slice.</summary>
public sealed class Tactic
{
    /// <summary>Target pace, possessions per 48 minutes.</summary>
    public double Pace { get; set; } = 99;
    /// <summary>0 = never shoot threes, 100 = three-point heavy. Shifts the shot mix.</summary>
    public int ThreePointEmphasis { get; set; } = 50;
    /// <summary>0 = send everyone back, 100 = crash the offensive glass hard.</summary>
    public int CrashGlass { get; set; } = 50;
    /// <summary>0 = conservative contain, 100 = gamble for steals.</summary>
    public int DefensiveAggression { get; set; } = 50;
    /// <summary>0 = spread touches, 100 = run everything through the stars.</summary>
    public int StarUsage { get; set; } = 50;
}

public sealed class League
{
    public required string Name { get; init; }
    public int Season { get; init; }
    public List<Team> Teams { get; } = new();
    public Rules Rules { get; init; } = Rules.Nba;

    public Team TeamById(int id) => Teams.First(t => t.Id == id);
}

/// <summary>Rules set, selectable per competition.</summary>
public sealed record Rules(
    string Name,
    int Periods,
    int MinutesPerPeriod,
    int ShotClockSeconds,
    int FoulsToFoulOut,
    int TeamFoulBonusThreshold,
    double ThreePointDistanceM)
{
    public int GameMinutes => Periods * MinutesPerPeriod;

    public static readonly Rules Nba = new("NBA-style", 4, 12, 24, 6, 5, 7.24);
    public static readonly Rules Fiba = new("FIBA", 4, 10, 24, 5, 5, 6.75);
}
