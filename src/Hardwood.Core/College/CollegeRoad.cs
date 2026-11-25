using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>Slice 7 of the revised launch plan (run 197, #1173): the four dates on a college staff's recruiting road each year.</summary>
public enum RoadEvent { SpringEvaluation, Camp, JulyEvaluation, VisitWeekend }

/// <summary>A program's choice for one road event of one recruiting class, and what came of it (saved).</summary>
public sealed class RoadChoice
{
    public int ProgramId { get; set; }
    public int ClassOf { get; set; }
    public RoadEvent Event { get; set; }
    /// <summary>The coach's pick (a region, a camp or a guest list); null until he picks.</summary>
    public string? Choice { get; set; }
    /// <summary>What happened, once the event is over (null before).</summary>
    public string? Result { get; set; }
    /// <summary>The staff picked because the coach didn't.</summary>
    public bool ByStaff { get; set; }
}

/// <summary>
/// Recruiting on the road (run 197, `danny-college-complete-nov22` item 6): the spring evaluation period (late April), the
/// summer camp (mid-June), the July evaluation period and the official-visit weekend at Midnight Madness, as calendar
/// events with a choice each for the coached program. They act on the coached program's courting only (scouting,
/// interest, official visits): no random draw, no computer school touched, so `madness`, `collegebox` and the audits are
/// unchanged. An event the coach leaves unpicked runs on his staff's default. Saved as <see cref="Road"/>.
/// </summary>
public sealed partial class CollegeWorld
{
    /// <summary>Every program's road choices and results (only the coached program's are ever written).</summary>
    public List<RoadChoice> Road { get; } = new();

    public static readonly string[] RoadRegions = { "Northeast", "Southeast", "Midwest", "Southwest", "West" };
    public const string EliteCamp = "Elite camp", TeamCamp = "Team camp";
    public const string BringStars = "The stars", BringLikely = "The likely yeses";

    /// <summary>An evaluation period: the staff see this many of a region's best uncommitted seniors.</summary>
    public const int EvaluationLooks = 40;
    /// <summary>An evaluation period: interest for a prospect on your board who saw your staff courtside (×<see cref="ContactScale"/>).</summary>
    public const double CourtsideInterest = 3;
    /// <summary>A sleeper: your staff's projection beats the services' by at least this much.</summary>
    public const int SleeperEdge = 8;
    public const int EliteCampInvites = 8, TeamCampLooks = 15;
    public const double EliteCampInterest = 6, TeamCampInterest = 4;
    public const int VisitWeekendGuests = 4;
    /// <summary>An official visit's interest (as <see cref="OfficialVisit"/>).</summary>
    public const double VisitInterest = 18;

    public static string RoadTitle(RoadEvent e) => e switch
    {
        RoadEvent.SpringEvaluation => "Spring evaluation period",
        RoadEvent.Camp => "Summer camp",
        RoadEvent.JulyEvaluation => "July evaluation period",
        _ => "Official visit weekend",
    };

    /// <summary>The first day of the event (for its "when").</summary>
    public static DateOnly RoadStart(RoadEvent e, int classOf) => e switch
    {
        RoadEvent.SpringEvaluation => new(classOf - 1, 4, 24),
        RoadEvent.Camp => new(classOf - 1, 6, 15),
        RoadEvent.JulyEvaluation => new(classOf - 1, 7, 10),
        _ => VisitFriday(classOf - 1),
    };

    /// <summary>The day the event happens (its choice locks then).</summary>
    public static DateOnly RoadDay(RoadEvent e, int classOf) => e switch
    {
        RoadEvent.SpringEvaluation => new(classOf - 1, 4, 26),
        RoadEvent.Camp => new(classOf - 1, 6, 17),
        RoadEvent.JulyEvaluation => new(classOf - 1, 7, 26),
        _ => VisitFriday(classOf - 1),
    };

    /// <summary>
    /// The visit weekend: Midnight Madness, unless that falls before the college season's first day (Oct 14; the summer
    /// jumps straight there, review of run 197), then the Friday a week later, so it always runs on a played day after
    /// September's commitments.
    /// </summary>
    public static DateOnly VisitFriday(int seasonYear)
    {
        var d = MadnessFriday(seasonYear);
        return d < new DateOnly(seasonYear, 10, 14) ? d.AddDays(7) : d;
    }

    /// <summary>The first Friday on or after October 10 (as the session's Midnight Madness).</summary>
    public static DateOnly MadnessFriday(int seasonYear)
    {
        var d = new DateOnly(seasonYear, 10, 10);
        while (d.DayOfWeek != DayOfWeek.Friday) d = d.AddDays(1);
        return d;
    }

    public static string RoadWhen(RoadEvent e, int classOf)
    {
        var a = RoadStart(e, classOf); var b = RoadDay(e, classOf);
        return a == b ? $"{a:ddd, MMM d}" : $"{a:MMM d}–{b:MMM d}";
    }

    public static IReadOnlyList<string> RoadOptions(RoadEvent e) => e switch
    {
        RoadEvent.Camp => new[] { EliteCamp, TeamCamp },
        RoadEvent.VisitWeekend => new[] { BringStars, BringLikely },
        _ => RoadRegions,
    };

    /// <summary>What a choice does, in a line (the panel's tip).</summary>
    public static string RoadHelp(RoadEvent e) => e switch
    {
        RoadEvent.Camp => $"{EliteCamp}: the {EliteCampInvites} best seniors on your board come to campus (scouted, interest +{EliteCampInterest:0}). "
                        + $"{TeamCamp}: your staff see {TeamCampLooks} of the best seniors from your state and region (scouted); those from your state join your board with interest +{TeamCampInterest:0}.",
        RoadEvent.VisitWeekend => $"Up to {VisitWeekendGuests} seniors with your offer take their official visit around Midnight Madness, free of coach hours; a loud crowd helps. "
                        + $"{BringStars}: the highest-ranked. {BringLikely}: the ones closest to saying yes.",
        _ => $"Your staff watch the {EvaluationLooks} best uncommitted prospects of the class you recruit (rising seniors) in one region: each is scouted, your board's prospects there see you courtside (interest +{CourtsideInterest:0}), and sleepers the services missed join your board.",
    };

    public RoadChoice? RoadEntry(int programId, int classOf, RoadEvent e) =>
        Road.FirstOrDefault(x => x.ProgramId == programId && x.ClassOf == classOf && x.Event == e);

    /// <summary>The coach picks for an event that hasn't happened yet.</summary>
    public string ChooseRoad(Team t, RoadEvent e, string choice, DateOnly today)
    {
        int cls = RecruitingClass(today);
        if (!RoadOptions(e).Contains(choice)) return $"\"{choice}\" isn't a choice for the {RoadTitle(e).ToLowerInvariant()}.";
        var entry = RoadEntry(t.Id, cls, e);
        if (entry?.Result != null || today > RoadDay(e, cls)) return $"The {RoadTitle(e).ToLowerInvariant()} for the Class of {cls} is over.";
        if (entry == null) Road.Add(entry = new RoadChoice { ProgramId = t.Id, ClassOf = cls, Event = e });
        entry.Choice = choice; entry.ByStaff = false;
        return $"{RoadTitle(e)} ({RoadWhen(e, cls)}): {choice}.";
    }

    /// <summary>The staff's pick when the coach hasn't made one.</summary>
    public string RoadDefault(Team t, RoadEvent e, int cls) => e switch
    {
        RoadEvent.Camp => BoardSeniors(t, cls).Count() >= 4 ? EliteCamp : TeamCamp,
        RoadEvent.VisitWeekend => BringStars,
        RoadEvent.SpringEvaluation => RegionOf(HomeState(t)) is { Length: > 0 } home ? home : RoadRegions[0],
        // July: wherever the most of the class's best 150 are still unscouted (ties: the home region first).
        _ => RoadRegions.OrderByDescending(reg => ClassOf(cls).Count(p => Recruits[p.Id] is var r && r.Rank <= 150 && r.CommittedTo == null
                                                                  && RegionOf(r.State) == reg && !(r.CourtedBy == t.Id && r.Scouted)))
                        .ThenByDescending(reg => reg == RegionOf(HomeState(t))).First(),
    };

    private IEnumerable<(Player p, Recruit r)> BoardSeniors(Team t, int cls) =>
        ClassOf(cls).Select(p => (p, r: Recruits[p.Id])).Where(x => x.r.CourtedBy == t.Id && x.r.OnBoard && x.r.CommittedTo == null)
                    .OrderBy(x => x.r.Rank);

    /// <summary>
    /// Runs one road event for the coached program on its day: applies the choice (or the staff's default) and returns the
    /// inbox message. Null if it already ran for this class.
    /// </summary>
    public (string Title, string Body)? RunRoad(Team t, RoadEvent e, int cls, DateOnly today)
    {
        var entry = RoadEntry(t.Id, cls, e);
        if (entry?.Result != null) return null;
        if (entry == null) Road.Add(entry = new RoadChoice { ProgramId = t.Id, ClassOf = cls, Event = e });
        if (entry.Choice == null || !RoadOptions(e).Contains(entry.Choice)) { entry.Choice = RoadDefault(t, e, cls); entry.ByStaff = true; }
        string choice = entry.Choice;
        string body = e switch
        {
            RoadEvent.Camp => choice == EliteCamp ? RunEliteCamp(t, cls) : RunTeamCamp(t, cls),
            RoadEvent.VisitWeekend => RunVisitWeekend(t, cls, choice),
            _ => RunEvaluation(t, cls, choice, today),
        };
        entry.Result = body;
        string who = entry.ByStaff ? "Your staff chose" : "You chose";
        string pick = e == RoadEvent.VisitWeekend ? choice.ToLowerInvariant() : $"the {(e == RoadEvent.Camp ? choice.ToLowerInvariant() : choice)}";
        return ($"{RoadTitle(e)}: {Short(e, choice)}", $"{who} {pick}. {body}");
    }

    private static string Short(RoadEvent e, string choice) => e switch
    {
        RoadEvent.Camp => choice == EliteCamp ? "the elite camp" : "the team camp",
        RoadEvent.VisitWeekend => "recruits on campus",
        _ => $"your staff in the {choice}",
    };

    private string RunEvaluation(Team t, int cls, string region, DateOnly today)
    {
        var seen = ClassOf(cls).Select(p => (p, r: Recruits[p.Id]))
            .Where(x => x.r.CommittedTo == null && RegionOf(x.r.State) == region && !Signed(x.r, today))
            .OrderBy(x => x.r.Rank).Take(EvaluationLooks).ToList();
        if (seen.Count == 0) return $"No uncommitted senior in the Class of {cls} was left to watch in the {region}.";
        int scouted = 0, courtside = 0;
        var sleepers = new List<string>();
        foreach (var (p, r) in seen)
        {
            bool mine = r.CourtedBy == t.Id;
            bool known = mine && (r.Scouted || r.Interest > 0); // review: one he already knew (or took off his board) isn't a new sleeper
            Courting(r, t);
            if (!r.Scouted) { r.Scouted = true; scouted++; }
            if (mine && r.OnBoard) { Gain(r, CourtsideInterest * ContactScale(r)); courtside++; }
            if (r.Stars <= 3 && StaffProjection(p, r) - r.Projection >= SleeperEdge && sleepers.Count < 3 && !r.OnBoard && !known)
            {
                r.OnBoard = true;
                sleepers.Add($"{StarText(r.Stars)} {p.PrimaryPosition} {p.Name} (No. {r.Rank}, {StateName(r.State)}): your staff project {StaffProjection(p, r)}, the services {r.Projection}");
            }
        }
        var best = seen[0];
        string text = $"They watched {seen.Count} of the {region}'s best uncommitted seniors, led by {StarText(best.r.Stars)} {best.p.Name} (No. {best.r.Rank}); "
            + $"{scouted} {(scouted == 1 ? "is" : "are")} newly scouted.";
        if (courtside > 0) text += $" {courtside} on your board saw your staff courtside (interest +{CourtsideInterest:0} each, less for the stars).";
        text += sleepers.Count > 0
            ? $" Sleepers the services missed, now on your board: {string.Join("; ", sleepers)}."
            : " No sleeper stood out.";
        return text;
    }

    private string RunEliteCamp(Team t, int cls)
    {
        var guests = BoardSeniors(t, cls).Take(EliteCampInvites).ToList();
        if (guests.Count == 0) return $"Nobody came: no uncommitted senior from the Class of {cls} was on your board to invite. Put prospects on your board before next June.";
        var lines = new List<string>();
        foreach (var (p, r) in guests)
        {
            double before = r.Interest;
            r.Scouted = true;
            Gain(r, EliteCampInterest * ContactScale(r));
            lines.Add($"{p.Name} ({StarText(r.Stars)}, {before:0} → {r.Interest:0})");
        }
        return $"{guests.Count} of your board's best seniors spent three days on campus: {string.Join(", ", lines)}.";
    }

    private string RunTeamCamp(Team t, int cls)
    {
        string home = HomeState(t), region = RegionOf(home);
        // A home-state camp first: up to half the places go to your own state's best, the rest to the region's.
        var pool = ClassOf(cls).Select(p => (p, r: Recruits[p.Id]))
            .Where(x => x.r.CommittedTo == null && (x.r.State == home || (region.Length > 0 && RegionOf(x.r.State) == region)))
            .OrderBy(x => x.r.Rank).ToList();
        var seen = pool.Where(x => x.r.State == home).Take(TeamCampLooks / 2).ToList();
        seen = seen.Concat(pool.Where(x => !seen.Contains(x)).Take(TeamCampLooks - seen.Count)).OrderBy(x => x.r.Rank).ToList();
        if (seen.Count == 0) return $"No uncommitted senior from the Class of {cls} came from your region.";
        int local = 0;
        foreach (var (p, r) in seen)
        {
            Courting(r, t);
            r.Scouted = true;
            if (r.State == home) { r.OnBoard = true; Gain(r, TeamCampInterest * ContactScale(r)); local++; }
        }
        return $"Your staff ran the camp for {seen.Count} of the best uncommitted seniors from {StateName(home)} and the {region}, all now scouted"
            + (local > 0 ? $"; the {local} from {StateName(home)} are on your board (interest +{TeamCampInterest:0})." : "; none was from your own state.");
    }

    private string RunVisitWeekend(Team t, int cls, string choice)
    {
        var offered = ClassOf(cls).Select(p => (p, r: Recruits[p.Id]))
            .Where(x => x.r.CourtedBy == t.Id && x.r.Offered && !x.r.OfficialVisit && x.r.CommittedTo != t.Id
                        && (x.r.CommittedTo == null || x.r.FlipOffer)).ToList();
        if (offered.Count == 0) return "Nobody came: no senior with your offer had an official visit left. Offer scholarships before the visit weekend to fill it.";
        var board = BoardFor(cls);
        var guests = (choice == BringStars
                ? offered.OrderBy(x => x.r.Rank)
                : offered.OrderBy(x => StandingOf(t, x.p, x.r, board)).ThenByDescending(x => x.r.Interest).ThenBy(x => x.r.Rank))
            .Take(VisitWeekendGuests).ToList();
        double pts = HomeCourtPoints(t.Id);
        double crowd = pts >= 4.2 ? 1.25 : pts >= 3.6 ? 1.1 : 1.0;
        var lines = new List<string>();
        foreach (var (p, r) in guests)
        {
            double before = r.Interest;
            r.OfficialVisit = true;
            Gain(r, VisitInterest * ContactScale(r) * crowd, p);
            lines.Add($"{p.Name} ({StarText(r.Stars)}, {before:0} → {r.Interest:0})");
        }
        string house = crowd >= 1.25 ? "A packed house sold the weekend" : crowd > 1 ? "A big crowd helped" : "The crowd was quiet, so the visits counted for no more than usual";
        return $"{guests.Count} took their official visit: {string.Join(", ", lines)}. {house}. No coach hours spent.";
    }
}
