namespace Hardwood.Core.Model;

public enum Conference { East, West }

public sealed class Team
{
    public int Id { get; init; }
    /// <summary>City, nickname and abbreviation: settable so a name pack can rename the club (Names.NamePack).</summary>
    public required string City { get; set; }
    public required string Name { get; set; }
    public string FullName => $"{City} {Name}";
    public required string Abbreviation { get; set; }
    public Conference Conference { get; set; }
    public required string Division { get; set; }
    public int ArenaCapacity { get; set; } = 18000;
    /// <summary>Brand colours as #RRGGBB and a logo style index (0 shield, 1 circle, 2 hexagon, 3 diamond, 4 banner).</summary>
    public string PrimaryColor { get; set; } = "#1D3FA3";
    public string SecondaryColor { get; set; } = "#F0A33A";
    public int LogoStyle { get; set; }

    public List<Player> Roster { get; } = new();

    /// <summary>Team tactics used by the engine.</summary>
    public Tactic Tactic { get; set; } = new();

    /// <summary>Human-set rotation. When null or empty the engine picks starters and minutes itself.</summary>
    public DepthChart? DepthChart { get; set; }

    /// <summary>Player ids whose numbers hang in the rafters.</summary>
    public List<int> RetiredNumbers { get; } = new();

    /// <summary>The human coach while he runs this team (null for computer-run teams: neutral coaching).</summary>
    public Jobs.Coach? Coach { get; set; }
    /// <summary>The club's owner: how patient and how ambitious (decision #71).</summary>
    public Jobs.Owner Owner { get; set; } = new();
    /// <summary>Revenue, budgets, balance and fan base (decision #88).</summary>
    public ClubFinance Money { get; set; } = new();
    /// <summary>Name of the computer head coach (for news, vacancies and the coaching carousel).</summary>
    public string CoachName { get; set; } = "";

    /// <summary>Team chemistry 0-100 (60 = normal): how well this group plays together. See People.PeopleEngine.</summary>
    public double Chemistry { get; set; } = 60;
    /// <summary>Weeks each current player has been on the team (continuity feeds chemistry).</summary>
    public Dictionary<int, int> WeeksTogether { get; set; } = new();

    public List<Staff> Staff { get; } = new();
    public TrainingPlan Training { get; set; } = new();

    /// <summary>Players fit to play on a given day.</summary>
    public IEnumerable<Player> Available(DateOnly on) => Roster.Where(p => p.IsAvailable(on));

    public override string ToString() => FullName;
}

/// <summary>A coach's rotation sheet: who starts and how many minutes each player should get.</summary>
public sealed class DepthChart
{
    /// <summary>Player ids of the five starters, in position order PG..C.</summary>
    public List<int> Starters { get; set; } = new();
    /// <summary>Target minutes per player id. Players missing from the dictionary get 0 (deep bench).</summary>
    public Dictionary<int, double> MinutesTargets { get; set; } = new();
}

/// <summary>
/// A team's tactics: an offensive system and a defensive scheme (decisions #58-59), fine-tuned by the sliders; player
/// roles (#60); who guards the other team's best scorer and whether to double him (#59); saved presets and a late-game
/// switch (#61). Players without an assigned role play their best natural one.
/// </summary>
public sealed class Tactic
{
    /// <summary>True for teams run by the computer: the coach picks system, scheme and matchups before each game.</summary>
    public bool Auto { get; set; } = true;
    public OffensiveSystem System { get; set; } = OffensiveSystem.Balanced;
    public DefensiveScheme Scheme { get; set; } = DefensiveScheme.Man;
    /// <summary>Role per player id. Missing = the player's best natural role (<see cref="Roles.Best"/>).</summary>
    public Dictionary<int, Role> Roles { get; set; } = new();
    /// <summary>Player id who guards the other team's best scorer when both are on the floor (null = normal matchups).</summary>
    public int? StarStopperId { get; set; }
    /// <summary>Send a second defender at the other team's best scorer: he shoots less and worse, his teammates get open looks.</summary>
    public bool DoubleStar { get; set; }
    public List<TacticPreset> Presets { get; set; } = new();
    /// <summary>Preset used in the last five minutes of the fourth quarter and overtime when the score is within six (null = keep the current tactics).</summary>
    public string? LateGamePreset { get; set; }

    /// <summary>
    /// The systems and schemes this team ran in its last few games, newest last (run 21, #267). Other coaches scout it:
    /// a team that has run the same thing three games running is expected to run it again.
    /// </summary>
    public List<OffensiveSystem> RecentSystems { get; set; } = new();
    /// <summary>
    /// Manual coaches only (run 21, #268): at halftime of a game you don't watch live, the assistant reads what the other
    /// bench is actually running and switches to the counter for the second half. Your saved tactics don't change.
    /// </summary>
    public bool HalftimeAdjust { get; set; } = true;
    /// <summary>
    /// Manual coaches only (#268): before every game the assistant sets the system, scheme and star matchup from his
    /// scouting report, as an auto coach would. Your roles, sliders and presets stay yours.
    /// </summary>
    public bool AssistantPlans { get; set; }
    public List<DefensiveScheme> RecentSchemes { get; set; } = new();

    public Role RoleOf(Player p) => Roles.TryGetValue(p.Id, out var r) ? r : Hardwood.Core.Model.Roles.Best(p);

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
    public Finance Finance { get; set; } = Finance.HardCapDefault;
    /// <summary>Players without a team (free agents, undrafted), still in the world.</summary>
    public List<Player> FreeAgents { get; } = new();
    /// <summary>College exists in this world (decision #98): its graduates replace the generated graduate class.</summary>
    public bool CollegeActive { get; set; }
    /// <summary>Retired players, kept for history.</summary>
    public List<Player> Retired { get; } = new();
    public List<SeasonSummary> History { get; } = new();
    public int NextPlayerId { get; set; } = 1;
    /// <summary>Every future draft pick in the world (both rounds, several years ahead).</summary>
    public List<DraftPickAsset> Picks { get; } = new();
    public Dictionary<int, Season.CareerTotals> Careers { get; } = new();
    /// <summary>Staff without a team, available to hire.</summary>
    public List<Staff> FreeStaff { get; } = new();
    public int NextStaffId { get; set; } = 1;
    public List<Season.HallOfFamer> HallOfFame { get; } = new();
    /// <summary>Single-game records and career milestones (roadmap 4, the living history). Empty for saves made before it.</summary>
    public Season.RecordBook Records { get; set; } = new();
    /// <summary>What the manager has handed to his staff (roadmap 12). Saved as JSON in meta; older saves delegate nothing.</summary>
    public Season.Delegation Delegation { get; set; } = new();
    /// <summary>Press conferences, locker-room moments and the team spirit they leave (people part 2). Saved as JSON in meta.</summary>
    public People.MediaDesk Media { get; set; } = new();
    public int NextPickId { get; set; } = 1;
    public const int PickYearsAhead = 7;

    public IEnumerable<DraftPickAsset> PicksOwnedBy(int teamId) => Picks.Where(p => p.OwnerTeamId == teamId).OrderBy(p => p.Year).ThenBy(p => p.Round);

    /// <summary>Make sure each team has own picks for every year through Season + PickYearsAhead.</summary>
    public void EnsurePicks()
    {
        for (int year = Season + 1; year <= Season + PickYearsAhead; year++)
        foreach (var t in Teams)
        for (int round = 1; round <= 2; round++)
            if (!Picks.Any(p => p.Year == year && p.Round == round && p.OriginalTeamId == t.Id))
                Picks.Add(new DraftPickAsset { Id = NextPickId++, Year = year, Round = round, OriginalTeamId = t.Id, OwnerTeamId = t.Id });
    }

    public Team TeamById(int id) => Teams.First(t => t.Id == id);
    public IEnumerable<Player> AllActivePlayers => Teams.SelectMany(t => t.Roster);
}

/// <summary>
/// League money rules. Since Nov 9 2026 (decision #52) the NABL uses an NBA-style soft cap: <see cref="HardCap"/> is the
/// salary cap (the name is kept for save compatibility). Signing another team's free agent needs cap room, the
/// mid-level exception, or a minimum deal; a team may re-sign its own players over the cap (Bird rights); nobody may
/// pass the apron. The tax line, apron and exception are derived from the cap with the NBA's 2025-26 ratios.
/// </summary>
public sealed record Finance(long HardCap, long MinSalary, int RosterMax, int RosterMin, long RookieScaleFirstPick)
{
    /// <summary>
    /// The open market (decision #80, from Nov 9 2026): no cap, no draft, no Bird rights. Every club spends within the
    /// wage budget its owner sets from its revenue. The cap fields below are kept only for old code paths and saves.
    /// </summary>
    public bool OpenMarket { get; init; } = true;
    /// <summary>Soft cap with Bird rights (true) or a strict hard cap (false).</summary>
    public bool SoftCap { get; init; } = true;
    /// <summary>The salary cap: room under it is what lets a team sign other teams' free agents.</summary>
    public long SalaryCap => HardCap;
    /// <summary>Luxury tax line (NBA: 1.215 × cap). AI teams stay under it unless contending.</summary>
    public long TaxLine => Round(HardCap * 1.215);
    /// <summary>Absolute payroll ceiling (NBA second apron: 1.344 × cap). With a hard cap, the cap itself.</summary>
    public long Apron => SoftCap ? Round(HardCap * 1.344) : HardCap;
    /// <summary>Mid-level exception: once per offseason, a team over the cap may sign one free agent up to this (NBA: 0.091 × cap).</summary>
    public long MidLevel => Round(HardCap * 0.091);
    /// <summary>Tax owed per dollar over the tax line (shown to the player; AI teams avoid it).</summary>
    public double TaxRate => 1.5;
    public long TaxBill(long payroll) => payroll > TaxLine ? (long)((payroll - TaxLine) * TaxRate) : 0;

    /// <summary>One line for the UI: payroll against the cap, tax line and apron.</summary>
    public string Describe(long payroll)
    {
        static string M(long v) => $"${v / 1e6:F1}M";
        if (OpenMarket) return $"wages {M(payroll)}";
        if (!SoftCap) return $"payroll {M(payroll)} · hard cap {M(HardCap)} · room {M(HardCap - payroll)}";
        string vsCap = payroll <= SalaryCap ? $"cap room {M(SalaryCap - payroll)}" : $"{M(payroll - SalaryCap)} over the cap";
        string tax = payroll > TaxLine ? $" · luxury tax {M(TaxBill(payroll))}" : "";
        return $"payroll {M(payroll)} · {vsCap} · cap {M(SalaryCap)} · tax line {M(TaxLine)} · apron {M(Apron)}{tax}";
    }
    private static long Round(double v) => (long)Math.Round(v / 100_000) * 100_000;

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
    public int? FinalsMvpPlayerId { get; set; }
    public Season.SeasonAwards? Awards { get; set; }
}

/// <summary>Rules set, selectable per competition.</summary>
public sealed record Rules(
    string Name,
    int Periods,
    int MinutesPerPeriod,
    int ShotClockSeconds,
    int FoulsToFoulOut,
    int TeamFoulBonusThreshold,
    double ThreePointDistanceM,
    double PaceScale = 1.0,
    double TalentScale = 1.0,
    bool NeutralSite = false)
{
    public int GameMinutes => Periods * MinutesPerPeriod;

    public static readonly Rules Nba = new("NBA-style", 4, 12, 24, 6, 5, 7.24);
    public static readonly Rules Fiba = new("FIBA", 4, 10, 24, 5, 5, 6.75);
    /// <summary>
    /// College: two 20-minute halves, a 30-second clock, five fouls (decision #95). C4 step 0 (#185): the 30-second clock
    /// slows the game to about 68 possessions (pace scale), and talent gaps count for more than in the pros, so the
    /// favourite wins more often (talent scale; see <see cref="EngineConfig.ForTalentScale"/>).
    /// </summary>
    public static readonly Rules College = new("College", 2, 20, 30, 5, 7, 6.75, PaceScale: 0.9, TalentScale: 1.9);
    /// <summary>College events (invitationals, showcases, conference tournaments, March Madness) are on neutral courts: no home edge.</summary>
    public static readonly Rules CollegeNeutral = College with { NeutralSite = true };
}
