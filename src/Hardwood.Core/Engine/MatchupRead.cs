using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>
/// The one matchup model the Tactics page, the setup cards, the matchup board and the assistant all read (critic K2,
/// run 99). Before it, each panel had its own thresholds and its own idea of "good", so the assistant could pick a
/// scheme the board called a big disadvantage while every card said it played into their hands.
/// <para>
/// The value of a plan is the same sum the AI coach maximises (<see cref="CoachAi.BestSystem"/> and
/// <see cref="CoachAi.BestScheme"/>): half the roster's fit to the system and the scheme, plus 2.5 × the matchup edge,
/// less a small step for the plain baseline options. The assistant's pick is always the plan with the best value, so
/// nothing he suggests can score below an alternative.
/// </para>
/// </summary>
public sealed record MatchupRead(OffensiveSystem TheirSystem, DefensiveScheme TheirScheme)
{
    /// <summary>An edge at or past this (either way) is worth a word; at or past <see cref="Big"/> it's a big one.</summary>
    public const double Notable = 0.015;
    public const double Big = 0.03;
    /// <summary>The whole plan's edge (attack plus defence) at which a card says it beats, or plays into, their plan.</summary>
    public const double PlanNotable = 0.02;
    /// <summary>The value step for the baseline system or scheme (the AI coach's 0.25 on its ×10 scale).</summary>
    public const double BaselineStep = 0.025;

    /// <summary>What the assistant expects <paramref name="opp"/> to run against <paramref name="us"/>.</summary>
    public static MatchupRead Of(Team us, Team opp, DateOnly? on)
    {
        var (s, d) = CoachAi.TheirPlan(opp, us, on);
        return new MatchupRead(s, d);
    }

    /// <summary>Our system against their defence: positive is good for us.</summary>
    public double Attack(OffensiveSystem s) => Tactics.Matchup(s, TheirScheme);

    /// <summary>Our scheme against their offence: positive is good for us.</summary>
    public double Defence(DefensiveScheme d) => -Tactics.Matchup(TheirSystem, d);

    /// <summary>The whole plan's matchup edge: attack plus defence.</summary>
    public double Edge(OffensiveSystem s, DefensiveScheme d) => Attack(s) + Defence(d);

    /// <summary>How good a plan is for this roster against this opponent (unclamped; higher is better).</summary>
    public static double Value(double systemFit, double schemeFit, double edge, OffensiveSystem s, DefensiveScheme d) =>
        (systemFit + schemeFit) / 2 + edge * 2.5
        - (s == OffensiveSystem.Balanced ? BaselineStep : 0) - (d == DefensiveScheme.Man ? BaselineStep : 0);

    /// <summary>The board's words for one side's edge.</summary>
    public static string Words(double edge) =>
        edge >= Big ? "Big edge" : edge >= Notable ? "Edge" : edge <= -Big ? "Big disadvantage" : edge <= -Notable ? "Disadvantage" : "Even";

    /// <summary>The option list's short tag for one side's edge.</summary>
    public static string Tag(double edge) => edge >= Notable ? "good matchup" : edge <= -Notable ? "poor matchup" : "";

    /// <summary>
    /// Why the assistant picks a system whose matchup is poor: the roster's fit outweighs it, and nothing his players
    /// suit does better. Empty when the matchup isn't poor.
    /// </summary>
    public string WhySystem(OffensiveSystem pick)
    {
        if (Attack(pick) > -Notable) return "";
        var better = Enum.GetValues<OffensiveSystem>().Where(s => Attack(s) >= Notable).OrderByDescending(Attack).FirstOrDefault();
        return Attack(better) >= Notable
            ? $"{Systems.Name(pick)} runs into their {Systems.Name(TheirScheme).ToLower()}, but your players don't suit {Systems.Name(better).ToLower()}, which would beat it; the fit is worth more."
            : $"Nothing beats their {Systems.Name(TheirScheme).ToLower()}; {Systems.Name(pick).ToLower()} is the least-bad option for this roster.";
    }

    /// <summary>Why the assistant picks a scheme whose matchup is poor. Empty when it isn't.</summary>
    public string WhyScheme(DefensiveScheme pick)
    {
        if (Defence(pick) > -Notable) return "";
        var better = Enum.GetValues<DefensiveScheme>().Where(d => Defence(d) >= Notable).OrderByDescending(Defence).FirstOrDefault();
        return Defence(better) >= Notable
            ? $"{Systems.Name(pick)} suits their {Systems.Name(TheirSystem).ToLower()} more than you'd like, but your players don't suit {Systems.Name(better).ToLower()}, which would stop it; the fit is worth more."
            : $"Nothing stops their {Systems.Name(TheirSystem).ToLower()}; {Systems.Name(pick).ToLower()} is the least-bad option for this roster.";
    }

    /// <summary>The game-plan card's pitch: says "counters both" only when it does.</summary>
    public string PlanPitch(OffensiveSystem s, DefensiveScheme d)
    {
        string head = $"They should run {Systems.Name(TheirSystem).ToLower()} and {Systems.Name(TheirScheme).ToLower()}";
        double a = Attack(s), f = Defence(d);
        if (a >= 0 && f >= 0 && (a > 0 || f > 0)) return head + "; this counters both.";
        if (a >= Notable && f < 0) return head + $"; this beats their {Systems.Name(TheirScheme).ToLower()} and gives a little back on defense. The best your players can do.";
        if (f >= Notable && a < 0) return head + $"; this stops their {Systems.Name(TheirSystem).ToLower()} and gives a little back on offense. The best your players can do.";
        if (Edge(s, d) <= -PlanNotable)
            return head + ". The least-bad plan for your players. " + string.Join(" ", new[] { WhySystem(s), WhyScheme(d) }.Where(x => x.Length > 0));
        return head + "; this is the best your players can do against it.";
    }
}
