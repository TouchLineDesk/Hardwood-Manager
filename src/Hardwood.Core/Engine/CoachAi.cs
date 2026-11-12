using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>
/// The computer's head coach (decisions #58-61): before each game it picks the system and scheme its available players
/// suit best (and that beat the opponent's likely approach), plays everyone in his best natural role, puts its best
/// perimeter defender on the other team's top scorer, and doubles that scorer when he dominates his team.
/// Also writes the scouting report the human sees about his next opponent.
/// </summary>
public static class CoachAi
{
    /// <summary>The players a coach plans around: healthy, best first, top nine.</summary>
    public static List<Player> Rotation(Team t, DateOnly? on) =>
        t.Roster.Where(p => on == null || p.IsAvailable(on.Value)).OrderByDescending(p => p.CurrentAbility).Take(9).ToList();

    public static OffensiveSystem BestSystem(IReadOnlyCollection<Player> rot, DefensiveScheme? against = null) =>
        Enum.GetValues<OffensiveSystem>()
            .OrderByDescending(s => (Systems.Fit(s, rot) - 0.5) * 5 + (against is { } d ? Tactics.Matchup(s, d) * 25 : 0) - (s == OffensiveSystem.Balanced ? 0.25 : 0))
            .First();

    public static DefensiveScheme BestScheme(IReadOnlyCollection<Player> rot, OffensiveSystem? against = null) =>
        Enum.GetValues<DefensiveScheme>()
            .OrderByDescending(d => (Systems.Fit(d, rot) - 0.5) * 5 - (against is { } s ? Tactics.Matchup(s, d) * 25 : 0) - (d == DefensiveScheme.Man ? 0.25 : 0))
            .First();

    /// <summary>The opponent's go-to scorer, and whether he dominates enough to double.</summary>
    public static (Player? star, bool worthDoubling) TheirStar(Team opp, DateOnly? on)
    {
        var rot = Rotation(opp, on).OrderByDescending(p => p.CurrentAbility).ToList();
        if (rot.Count < 2) return (rot.FirstOrDefault(), false);
        return (rot[0], rot[0].CurrentAbility >= 150 && rot[0].CurrentAbility - rot[1].CurrentAbility >= 22);
    }

    /// <summary>Best defender to put on a scorer.</summary>
    public static Player? Stopper(IReadOnlyCollection<Player> rot) =>
        rot.OrderByDescending(p => p.Attributes[Attr.PerimeterDefense] * 2 + p.Attributes[Attr.Agility] + p.Attributes[Attr.Steal]).FirstOrDefault();

    /// <summary>How many games running a team must use the same system (or scheme) before opponents read it from film.</summary>
    public const int FilmReadGames = 3;
    private const int RecentKept = 5;

    /// <summary>
    /// What another coach expects <paramref name="opp"/> to run (run 21, #267). Every team is scouted the same way, auto
    /// or manual: from film when it has run the same thing <see cref="FilmReadGames"/> games running, otherwise from
    /// its personnel (the system and scheme its players suit). Nobody sees the other bench's game plan before tip-off.
    /// </summary>
    public static (OffensiveSystem system, DefensiveScheme scheme) Expected(Team opp, DateOnly? on)
    {
        var t = opp.Tactic;
        var oppRot = Rotation(opp, on);
        var sys = Repeated(t.RecentSystems) ?? (oppRot.Count > 0 ? BestSystem(oppRot) : t.System);
        var sch = Repeated(t.RecentSchemes) ?? (oppRot.Count > 0 ? BestScheme(oppRot) : t.Scheme);
        return (sys, sch);
    }

    /// <summary>Whether the team has run one thing for the last <see cref="FilmReadGames"/> games (it's on film).</summary>
    public static bool OnFilm<T>(List<T> recent) where T : struct, Enum => Repeated(recent) != null;

    private static T? Repeated<T>(List<T> recent) where T : struct, Enum
    {
        if (recent.Count < FilmReadGames) return null;
        var last = recent[^1];
        for (int i = recent.Count - FilmReadGames; i < recent.Count; i++)
            if (!EqualityComparer<T>.Default.Equals(recent[i], last)) return null;
        return last;
    }

    /// <summary>The game plan an auto coach would use against <paramref name="opp"/>: counter what he expects them to run.</summary>
    public static (OffensiveSystem system, DefensiveScheme scheme) PlanFor(Team team, Team opp, DateOnly? on)
    {
        var rot = Rotation(team, on);
        if (rot.Count == 0) return (team.Tactic.System, team.Tactic.Scheme);
        var (theirSystem, theirScheme) = Expected(opp, on);
        return (BestSystem(rot, theirScheme), BestScheme(rot, theirSystem));
    }

    /// <summary>
    /// What <paramref name="opp"/> will most likely run against <paramref name="us"/>, as our assistant scouts it: the
    /// same read every coach makes (<see cref="Expected"/>), so the human's report is no better or worse than the AI's.
    /// The real answer comes at tip-off: the live game and the halftime adjustment see what they actually run.
    /// </summary>
    public static (OffensiveSystem system, DefensiveScheme scheme) TheirPlan(Team opp, Team us, DateOnly? on) => Expected(opp, on);

    /// <summary>
    /// The counter to what the other team is actually running (the halftime read, #268): the system and scheme our
    /// players suit best against their real system and scheme.
    /// </summary>
    public static (OffensiveSystem system, DefensiveScheme scheme) Counter(IReadOnlyCollection<Player> rot, OffensiveSystem theirSystem, DefensiveScheme theirScheme) =>
        (BestSystem(rot, theirScheme), BestScheme(rot, theirSystem));

    /// <summary>Set up an AI team's tactics for a game against <paramref name="opp"/>. Leaves human-run teams alone.</summary>
    public static void Prepare(Team team, Team opp, DateOnly? on)
    {
        var t = team.Tactic;
        if (!t.Auto && !t.AssistantPlans) return;
        var rot = Rotation(team, on);
        if (rot.Count == 0) return;
        (t.System, t.Scheme) = PlanFor(team, opp, on);
        if (t.Auto) t.Roles.Clear();
        var (star, dbl) = TheirStar(opp, on);
        t.StarStopperId = star != null ? Stopper(rot)?.Id : null;
        t.DoubleStar = dbl;
    }

    /// <summary>Put what both teams ran at tip-off on film. Call after both <see cref="Prepare"/> calls.</summary>
    public static void Remember(Team team)
    {
        var t = team.Tactic;
        t.RecentSystems.Add(t.System);
        t.RecentSchemes.Add(t.Scheme);
        if (t.RecentSystems.Count > RecentKept) t.RecentSystems.RemoveRange(0, t.RecentSystems.Count - RecentKept);
        if (t.RecentSchemes.Count > RecentKept) t.RecentSchemes.RemoveRange(0, t.RecentSchemes.Count - RecentKept);
    }

    /// <summary>
    /// The assistant sets up a human-run team: the system and scheme that suit the roster, everyone in his best role,
    /// no special matchups. The human takes it from there (Auto stays off so nothing changes behind his back).
    /// </summary>
    public static void AssistantSetup(Team team)
    {
        var rot = Rotation(team, null);
        var t = team.Tactic;
        t.System = BestSystem(rot);
        t.Scheme = BestScheme(rot);
        t.Roles.Clear();
        t.StarStopperId = null;
        t.DoubleStar = false;
        t.Auto = false;
    }

    /// <summary>A short scouting report on the next opponent, with what the assistant coach would do about it.</summary>
    public static string ScoutingReport(Team us, Team opp, DateOnly? on)
    {
        var oppRot = Rotation(opp, on);
        var ourRot = Rotation(us, on);
        if (oppRot.Count == 0) return "";
        var (theirSystem, theirScheme) = TheirPlan(opp, us, on);
        var (star, dbl) = TheirStar(opp, on);
        var ourScheme = BestScheme(ourRot, theirSystem);
        var ourSystem = BestSystem(ourRot, theirScheme);
        var stopper = Stopper(ourRot);
        var lines = new List<string>
        {
            $"{opp.FullName} will likely run {Systems.Name(theirSystem).ToLower()} and play {Systems.Name(theirScheme).ToLower()} defense against us.",
        };
        if (star != null)
            lines.Add($"Their go-to scorer is {star.Name} ({star.PrimaryPosition}). " + (dbl ? "He dominates their offense: consider doubling him." : "He isn't dominant enough to double; put a stopper on him.")
                      + (stopper != null ? $" Best stopper: {stopper.Name}." : ""));
        lines.Add($"Assistant's suggestion: {Systems.Name(ourSystem).ToLower()} on offense, {Systems.Name(ourScheme).ToLower()} on defense.");
        double m = Tactics.Matchup(theirSystem, ourScheme);
        if (m < 0) lines.Add($"{Systems.Name(ourScheme)} is a good answer to {Systems.Name(theirSystem).ToLower()}.");
        // Being predictable has a cost (#267): after three games of the same thing, the other bench has it on film.
        if (!us.Tactic.Auto && (OnFilm(us.Tactic.RecentSystems) || OnFilm(us.Tactic.RecentSchemes)))
            lines.Add($"They've seen us run {Systems.Name(us.Tactic.RecentSystems[^1]).ToLower()} and {Systems.Name(us.Tactic.RecentSchemes[^1]).ToLower()} defense {FilmReadGames} games running and will plan for it; a change would catch them out.");
        return string.Join(" ", lines);
    }
}
