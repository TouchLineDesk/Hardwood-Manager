namespace Hardwood.Core.Model;

/// <summary>
/// Money buys things, part 1 (item M, Danny's #237 and D17 A, #273): a franchise's facilities. Each is rated 1-5 and
/// works against the clubs it competes with (its <see cref="Facility.Edge"/>), so a league where everyone builds doesn't
/// inflate: it's an arms race, as in the NBA.
/// </summary>
public enum FacilityKind
{
    /// <summary>Practice facility: courts, weight room, film rooms. Training quality.</summary>
    Practice,
    /// <summary>Sports science and medical centre: load management, recovery, imaging. Injury risk and recovery time.</summary>
    Medical,
    /// <summary>Analytics and scouting department: tracking data, video, a scouting network. Scouting knowledge.</summary>
    Analytics,
    /// <summary>G League affiliate and academy: reps and coaching for players 22 and under. Young players' growth.</summary>
    Academy,
    /// <summary>Arena renovation and premium seating: suites, courtside clubs, the fan experience. Revenue and fan growth.</summary>
    Arena,
    /// <summary>A new arena: a multi-season build. More seats, and the arena at the top level for decades.</summary>
    NewArena,
}

/// <summary>One facility: its level (1-5), seasons since it was built or renewed, and its edge on the league's average.</summary>
public sealed class Facility
{
    public int Level { get; set; } = 3;
    /// <summary>Seasons since it was last built or renewed. Past its lifespan it dates and loses a level.</summary>
    public int Age { get; set; }
    /// <summary>Level minus the league's average level (set each summer); effects use this, so they're zero-sum.</summary>
    public double Edge { get; set; }
}

/// <summary>A facility project: being built (paid in instalments, one a summer), or proposed to you.</summary>
public sealed class FacilityProject
{
    public FacilityKind Kind { get; set; }
    /// <summary>The level it brings the facility to (5 for a new arena).</summary>
    public int ToLevel { get; set; }
    /// <summary>The whole price.</summary>
    public long Cost { get; set; }
    /// <summary>Summers until it opens (and instalments still to pay, including the first).</summary>
    public int SeasonsLeft { get; set; }
    /// <summary>Instalments in all (1 for an upgrade, 3 for a new arena).</summary>
    public int Instalments { get; set; } = 1;
    /// <summary>A new arena's capacity.</summary>
    public int Capacity { get; set; }
    /// <summary>The season it was approved.</summary>
    public int Season { get; set; }
    /// <summary>A renewal of a dated facility: it keeps its level and starts its lifespan again.</summary>
    public bool Renewal { get; set; }
    public long Instalment => Cost / Math.Max(1, Instalments);
}

/// <summary>A club's facilities, projects and (for your club) the front office's proposals.</summary>
public sealed class ClubFacilities
{
    public Facility Practice { get; set; } = new();
    public Facility Medical { get; set; } = new();
    public Facility Analytics { get; set; } = new();
    public Facility Academy { get; set; } = new();
    public Facility Arena { get; set; } = new();
    /// <summary>Projects under way.</summary>
    public List<FacilityProject> Building { get; set; } = new();
    /// <summary>Your club only: projects the front office proposes and the owner will fund, waiting for your answer.</summary>
    public List<FacilityProject> Proposals { get; set; } = new();
    /// <summary>Your club, when you haven't delegated it: the summer makes proposals instead of building.</summary>
    public bool YouDecide { get; set; }
    /// <summary>Set once levels have been seeded.</summary>
    public bool Ready { get; set; }
    /// <summary>Last summer: running costs paid, money put into projects, and what happened (for the Board page and news).</summary>
    public long LastUpkeep { get; set; }
    public long LastInvested { get; set; }
    public List<string> LastNews { get; set; } = new();
    /// <summary>Every dollar put into facilities since the career began.</summary>
    public long TotalInvested { get; set; }
    public int ArenasBuilt { get; set; }
    /// <summary>Paid out to the owner when the club held more than a season's revenue with nothing to build.</summary>
    public long LastDistribution { get; set; }
    public long TotalDistributed { get; set; }
    /// <summary>Seasons since the arena opened (a new one is due after about 30).</summary>
    public int ArenaYears { get; set; }

    public Facility Get(FacilityKind k) => k switch
    {
        FacilityKind.Practice => Practice,
        FacilityKind.Medical => Medical,
        FacilityKind.Analytics => Analytics,
        FacilityKind.Academy => Academy,
        _ => Arena,
    };

    public static readonly FacilityKind[] Kinds = { FacilityKind.Practice, FacilityKind.Medical, FacilityKind.Analytics, FacilityKind.Academy, FacilityKind.Arena };

    public bool IsBuilding(FacilityKind k) => Building.Any(b => b.Kind == k || (k == FacilityKind.Arena && b.Kind == FacilityKind.NewArena) || (k == FacilityKind.NewArena && b.Kind == FacilityKind.Arena));
}
