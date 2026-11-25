using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>
/// Slice 6 of the revised launch plan (#1164, run 196): one program's captains for one season. Names are kept with the ids,
/// so a captain who has since turned pro is still named in his program's past.
/// </summary>
public sealed class CaptainSeason
{
    /// <summary>The college season (the year it began).</summary>
    public int Year { get; init; }
    public int TeamId { get; init; }
    public List<int> PlayerIds { get; set; } = new();
    public Dictionary<int, string> Names { get; set; } = new();
    /// <summary>True once the coach has changed the assistant's choice.</summary>
    public bool Chosen { get; set; }
    /// <summary>The coach has been told (his own program's inbox message).</summary>
    public bool Announced { get; set; }
    /// <summary>Review (run 196): players who lost the captaincy this season, and the day it stops weighing on them (its own
    /// mood line, so it neither replaces nor is replaced by another lift).</summary>
    public Dictionary<int, DateOnly> Stripped { get; set; } = new();
    /// <summary>Review (run 196): the seniors honoured on Senior Night (their own mood line for the rest of the season).</summary>
    public List<int> Honoured { get; set; } = new();
}

/// <summary>
/// Run 196: captains. Every program names two captains a week before its first game, the players the room follows: leadership
/// first, then class year, teamwork and ability (no random draw). The coach of a program can change them on the Squad page.
/// <list type="bullet">
/// <item><b>The locker room:</b> in college, the captains' leadership replaces the old "strongest leader in the rotation" line in the
/// chemistry target: <c>(average leadership − 9) × 1.2</c>, from −6 to +8. A poor pick costs chemistry; an unhappy captain costs 3 more.</item>
/// <item><b>The captain himself:</b> "Proud to captain the team" (+4 to his target morale). A player who loses the captaincy
/// takes it badly for a month (−8).</item>
/// </list>
/// Moods are weekly for the coached program only (the AI programs' rooms aren't simulated), so the AI programs' captains are
/// names and honours; nothing else in the world moves.
/// </summary>
public static class Captaincy
{
    public const int Count = 2;
    public const double LeadershipPar = 9, LeadershipWeight = 1.2, ChemistryMin = -6, ChemistryMax = 8;
    public const double UnhappyCaptainCost = 3;
    public const double CaptainMorale = 4, LostCaptaincy = -8;
    public const int LostCaptaincyDays = 30;
    /// <summary>Captains are named this many days before the season's first game (the preseason message's day).</summary>
    public const int LeadDays = 7;

    /// <summary>How strongly the room would follow a player: leadership, then seniority, teamwork and ability.</summary>
    public static double Standing(Player p, int seasonYear) =>
        p.Attributes[Attr.Leadership] * 2.0
        + (CollegeWorld.ClassYearOf(p, seasonYear)?.Year ?? 1) * 3.0
        + p.Attributes[Attr.Teamwork] * 0.5
        + p.CurrentAbility / 20.0;

    /// <summary>The assistant's two captains for a roster, best first.</summary>
    public static List<Player> Suggest(Team t, int seasonYear) =>
        t.Roster.OrderByDescending(p => Standing(p, seasonYear)).ThenBy(p => p.Id).Take(Count).ToList();

    /// <summary>"Proud to captain the team" while he is one.</summary>
    public const string CaptainReason = "Proud to captain the team";
    public const string LostReason = "Lost the captaincy";
    public const string HonouredReason = "Honoured on Senior Night";
    public const double SeniorNightLift = 6;

    /// <summary>The college chemistry line for the captains (null with none on the roster).</summary>
    public static (string reason, double effect)? ChemistryLine(Team team)
    {
        var caps = team.Roster.Where(p => team.CaptainIds.Contains(p.Id)).OrderBy(p => p.Id).ToList();
        if (caps.Count == 0) return null;
        double lead = caps.Average(p => p.Attributes[Attr.Leadership]);
        double e = Math.Clamp((lead - LeadershipPar) * LeadershipWeight, ChemistryMin, ChemistryMax);
        string names = string.Join(" and ", caps.Select(p => p.Name));
        string who = caps.Count == 1 ? $"Captain {names}" : $"Captains {names}";
        return e >= 0 ? ($"{who} {(caps.Count == 1 ? "leads" : "lead")} the locker room", e)
                      : ($"{who} {(caps.Count == 1 ? "doesn't" : "don't")} lead the room (leadership {lead:0} of 20)", e);
    }
}

public sealed partial class CollegeWorld
{
    /// <summary>Every program's captains, season by season (saved with the college world; none before v0.184.0).</summary>
    public List<CaptainSeason> Captains { get; } = new();

    public CaptainSeason? CaptainsOf(int teamId, int? year = null) =>
        Captains.FirstOrDefault(c => c.TeamId == teamId && c.Year == (year ?? Season.Year));

    /// <summary>The day this season's captains are named (a week before the first game), or null before the schedule exists.</summary>
    public DateOnly? CaptainsDay => FirstGameDay?.AddDays(-Captaincy.LeadDays);

    /// <summary>Name every program's captains once the day has come (no upper bound, so a jump past it still names them).
    /// Returns true on the day they're named.</summary>
    public bool NameCaptainsThrough(DateOnly date)
    {
        // Review (run 196): not once the season's final is played (an old save loaded in the spring).
        if (CaptainsDay is not { } day || date < day || Season.ChampionId != null || Captains.Any(c => c.Year == Season.Year)) { RefreshCaptainFlags(); return false; }
        foreach (var t in Programs)
        {
            var pick = Captaincy.Suggest(t, Season.Year);
            if (pick.Count == 0) continue;
            Captains.Add(new CaptainSeason { Year = Season.Year, TeamId = t.Id, PlayerIds = pick.Select(p => p.Id).ToList(), Names = pick.ToDictionary(p => p.Id, p => p.Name) });
        }
        RefreshCaptainFlags();
        return true;
    }

    /// <summary>The season's record for a program, made if it has none.</summary>
    public CaptainSeason CaptainRecord(Team t)
    {
        var rec = CaptainsOf(t.Id);
        if (rec == null) { rec = new CaptainSeason { Year = Season.Year, TeamId = t.Id }; Captains.Add(rec); }
        return rec;
    }

    /// <summary>Set a program's captains for this season (the coach's choice). A captain no longer on the roster keeps his
    /// place in the record (review, run 196), so his honour isn't lost.</summary>
    public CaptainSeason SetCaptains(Team t, IReadOnlyList<Player> captains)
    {
        var rec = CaptainRecord(t);
        var onRoster = t.Roster.Select(p => p.Id).ToHashSet();
        rec.PlayerIds = rec.PlayerIds.Where(id => !onRoster.Contains(id)).Concat(captains.Select(p => p.Id)).Distinct().ToList();
        foreach (var p in captains) { rec.Names[p.Id] = p.Name; rec.Stripped.Remove(p.Id); }
        rec.Chosen = true;
        RefreshCaptainFlags();
        return rec;
    }

    /// <summary>Copy this season's captains onto each program (<see cref="Team.CaptainIds"/>, which the mood code reads).
    /// Outside a season with captains (the summer), nobody is captain.</summary>
    public void RefreshCaptainFlags()
    {
        var now = new Dictionary<int, CaptainSeason>();
        foreach (var c in Captains) if (c.Year == Season.Year) now[c.TeamId] = c;
        foreach (var t in Programs)
        {
            t.CaptainIds.Clear(); t.StrippedUntil.Clear(); t.HonouredIds.Clear();
            if (!now.TryGetValue(t.Id, out var c)) continue;
            foreach (var id in c.PlayerIds) t.CaptainIds.Add(id);
            foreach (var (id, until) in c.Stripped) t.StrippedUntil[id] = until;
            foreach (var id in c.Honoured) t.HonouredIds.Add(id);
        }
    }

    /// <summary>"Captain 2026-27, Clemson" lines for a player's honours, newest first.</summary>
    public IEnumerable<string> CaptainHonoursOf(int playerId) =>
        Captains.Where(c => c.PlayerIds.Contains(playerId)).OrderByDescending(c => c.Year)
            .Select(c => $"Captain {CollegeAwards.SeasonText(c.Year)}" + (Program(c.TeamId) is { } t ? $", {t.FullName}" : ""));
}
