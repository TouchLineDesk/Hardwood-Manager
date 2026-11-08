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
    /// <summary>The year the current season starts (2026 for 2026-27).</summary>
    public int Season { get; set; }
    public List<Team> Teams { get; } = new();
    public Rules Rules { get; init; } = Rules.Nba;
    public Finance Finance { get; init; } = Finance.HardCapDefault;
    /// <summary>Players without a team (free agents, undrafted), still in the world.</summary>
    public List<Player> FreeAgents { get; } = new();
    /// <summary>Retired players, kept for history.</summary>
    public List<Player> Retired { get; } = new();
    public List<SeasonSummary> History { get; } = new();
    public int NextPlayerId { get; set; } = 1;

    public Team TeamById(int id) => Teams.First(t => t.Id == id);
    public IEnumerable<Player> AllActivePlayers => Teams.SelectMany(t => t.Roster);
}

/// <summary>League money rules. The prototype uses a hard cap; later models (soft cap, tax, aprons) plug in here.</summary>
public sealed record Finance(long HardCap, long MinSalary, int RosterMax, int RosterMin, long RookieScaleFirstPick)
{
    public static readonly Finance HardCapDefault = new(
        HardCap: 165_000_000,
        MinSalary: 1_200_000,
        RosterMax: 15,
        RosterMin: 13,
        RookieScaleFirstPick: 11_000_000);
}

/// <summary>One finished season, kept in league history.</summary>
public sealed class SeasonSummary
{
    public int Season { get; init; }
    public int ChampionTeamId { get; init; }
    public int RunnerUpTeamId { get; init; }
    public List<(int teamId, int wins, int losses)> Records { get; init; } = new();
    public int? MvpPlayerId { get; init; }
    public int? ScoringLeaderPlayerId { get; init; }
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
