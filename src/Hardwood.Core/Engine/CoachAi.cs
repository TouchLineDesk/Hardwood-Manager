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

    /// <summary>Set up an AI team's tactics for a game against <paramref name="opp"/>. Leaves human-run teams alone.</summary>
    public static void Prepare(Team team, Team opp, DateOnly? on)
    {
        var t = team.Tactic;
        if (!t.Auto) return;
        var rot = Rotation(team, on);
        if (rot.Count == 0) return;
        var oppRot = Rotation(opp, on);
        // Plan against what the other coach is most likely to run (his best fit), without chasing it in circles.
        var theirSystem = opp.Tactic.Auto ? BestSystem(oppRot) : opp.Tactic.System;
        var theirScheme = opp.Tactic.Auto ? BestScheme(oppRot) : opp.Tactic.Scheme;
        t.System = BestSystem(rot, theirScheme);
        t.Scheme = BestScheme(rot, theirSystem);
        t.Roles.Clear();
        var (star, dbl) = TheirStar(opp, on);
        t.StarStopperId = star != null ? Stopper(rot)?.Id : null;
        t.DoubleStar = dbl;
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
        var theirSystem = opp.Tactic.Auto ? BestSystem(oppRot) : opp.Tactic.System;
        var theirScheme = opp.Tactic.Auto ? BestScheme(oppRot) : opp.Tactic.Scheme;
        var (star, dbl) = TheirStar(opp, on);
        var ourScheme = BestScheme(ourRot, theirSystem);
        var ourSystem = BestSystem(ourRot, theirScheme);
        var stopper = Stopper(ourRot);
        var lines = new List<string>
        {
            $"{opp.FullName} usually run {Systems.Name(theirSystem).ToLower()} and play {Systems.Name(theirScheme).ToLower()} defense.",
        };
        if (star != null)
            lines.Add($"Their go-to scorer is {star.Name} ({star.PrimaryPosition}). " + (dbl ? "He dominates their offense: consider doubling him." : "He isn't dominant enough to double; put a stopper on him.")
                      + (stopper != null ? $" Best stopper: {stopper.Name}." : ""));
        lines.Add($"Assistant's suggestion: {Systems.Name(ourSystem).ToLower()} on offense, {Systems.Name(ourScheme).ToLower()} on defense.");
        double m = Tactics.Matchup(theirSystem, ourScheme);
        if (m < 0) lines.Add($"{Systems.Name(ourScheme)} is a good answer to {Systems.Name(theirSystem).ToLower()}.");
        return string.Join(" ", lines);
    }
}
