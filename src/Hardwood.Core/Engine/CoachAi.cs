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

    /// <param name="college">Run 194: college benches choose from every scheme (<see cref="Tactics.Schemes"/>), fit against
    /// college rotations, and lean further to man-to-man (<see cref="CollegeManLean"/>): most real programs play man.</param>
    public static DefensiveScheme BestScheme(IReadOnlyCollection<Player> rot, OffensiveSystem? against = null, bool college = false) =>
        Tactics.Schemes(college)
            .OrderByDescending(d => (Systems.Fit(d, rot, college) - 0.5) * 5 - (against is { } s ? Tactics.Matchup(s, d) * 25 : 0)
                                    - (d == DefensiveScheme.Man ? 0.25 : 0) + (college ? CollegeLean(d) : 0))
            .First();

    /// <summary>Run 194: how far a college bench leans to man-to-man (in the same units as a fit step of 0.2 = 1.0). Tuned
    /// with `hardwood pressbox` so about two programs in three play man, one in six a 2-3 and a handful the 1-3-1 or the
    /// match-up zone.</summary>
    public const double CollegeManLean = 1.0;

    /// <summary>Run 194: how much a college bench favours each scheme beyond fit (man most, then the 2-3, the common zone;
    /// the 1-3-1 and the match-up are specialist defences).</summary>
    public static double CollegeLean(DefensiveScheme d) => d switch
    {
        DefensiveScheme.Man => CollegeManLean,
        DefensiveScheme.Zone23 => 0.1,
        DefensiveScheme.Drop or DefensiveScheme.SwitchEverything => -0.1,
        _ => -0.35,
    };

    /// <summary>Run 194: a college bench presses full court when its rotation suits it this well (about one program in eight).</summary>
    public const double PressFitToPress = 0.80;

    /// <summary>Run 194: whether a college bench with this rotation presses (never in the pros).</summary>
    public static bool Presses(IReadOnlyCollection<Player> rot, bool college) => college && rot.Count > 0 && Systems.PressFit(rot) >= PressFitToPress;

    /// <summary>The opponent's go-to scorer, and whether he dominates enough to double. Run 116: the go-to man is the
    /// player the engine's shot weights favour (<see cref="GameSimulator.GoToWeight"/>: role, star term, Star usage and
    /// system), the same man the defence keys on in the game, not simply the highest-rated player. He is worth doubling
    /// when he is a star (CA 150+) at least 22 points better than anyone else in the rotation.</summary>
    /// <param name="us">The team they play. Given, an AI opponent is judged in the system it will pick against us
    /// (<see cref="PlanFor"/>, the same choice its own <see cref="Prepare"/> makes, whichever bench is prepared first);
    /// a human-run team in the system it has set.</param>
    public static (Player? star, bool worthDoubling) TheirStar(Team opp, DateOnly? on, Team? us = null, bool college = false)
    {
        var rot = Rotation(opp, on);
        if (rot.Count == 0) return (null, false);
        var tac = opp.Tactic;
        OffensiveSystem system = us != null && (tac.Auto || tac.AssistantPlans) ? PlanFor(opp, us, on, college).system : tac.System;
        // Highest weight first; a tie goes to the better player.
        var star = rot.OrderByDescending(p => GameSimulator.GoToWeight(p, tac, system)).ThenByDescending(p => p.CurrentAbility).First();
        if (rot.Count < 2) return (star, false);
        int nextBest = rot.Where(p => p != star).Max(p => p.CurrentAbility);
        return (star, star.CurrentAbility >= 150 && star.CurrentAbility - nextBest >= 22);
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
    public static (OffensiveSystem system, DefensiveScheme scheme) Expected(Team opp, DateOnly? on, bool college = false)
    {
        var t = opp.Tactic;
        var oppRot = Rotation(opp, on);
        var sys = Repeated(t.RecentSystems) ?? (oppRot.Count > 0 ? BestSystem(oppRot) : t.System);
        var sch = Repeated(t.RecentSchemes) ?? (oppRot.Count > 0 ? BestScheme(oppRot, null, college) : t.Scheme);
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
    public static (OffensiveSystem system, DefensiveScheme scheme) PlanFor(Team team, Team opp, DateOnly? on, bool college = false)
    {
        var rot = Rotation(team, on);
        if (rot.Count == 0) return (team.Tactic.System, team.Tactic.Scheme);
        var (theirSystem, theirScheme) = Expected(opp, on, college);
        return (BestSystem(rot, theirScheme), BestScheme(rot, theirSystem, college));
    }

    /// <summary>
    /// What <paramref name="opp"/> will most likely run against <paramref name="us"/>, as our assistant scouts it: the
    /// same read every coach makes (<see cref="Expected"/>), so the human's report is no better or worse than the AI's.
    /// The real answer comes at tip-off: the live game and the halftime adjustment see what they actually run.
    /// </summary>
    public static (OffensiveSystem system, DefensiveScheme scheme) TheirPlan(Team opp, Team us, DateOnly? on, bool college = false) => Expected(opp, on, college);

    /// <summary>
    /// The counter to what the other team is actually running (the halftime read, #268): the system and scheme our
    /// players suit best against their real system and scheme.
    /// </summary>
    public static (OffensiveSystem system, DefensiveScheme scheme) Counter(IReadOnlyCollection<Player> rot, OffensiveSystem theirSystem, DefensiveScheme theirScheme, bool college = false) =>
        (BestSystem(rot, theirScheme), BestScheme(rot, theirSystem, college));

    /// <summary>Set up an AI team's tactics for a game against <paramref name="opp"/>. Leaves human-run teams alone.</summary>
    /// <param name="college">Run 194: college rules (the zones and the press are open to the bench).</param>
    public static void Prepare(Team team, Team opp, DateOnly? on, bool college = false)
    {
        var t = team.Tactic;
        if (!t.Auto && !t.AssistantPlans) return;
        var rot = Rotation(team, on);
        if (rot.Count == 0) return;
        (t.System, t.Scheme) = PlanFor(team, opp, on, college);
        // The press is the auto coach's own call; a manual coach whose assistant plans keeps his own press setting.
        if (t.Auto) t.FullCourtPress = Presses(rot, college);
        if (t.Auto) t.Roles.Clear();
        var (star, dbl) = TheirStar(opp, on, team, college);
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
    public static void AssistantSetup(Team team, bool college = false)
    {
        var rot = Rotation(team, null);
        var t = team.Tactic;
        t.System = BestSystem(rot);
        t.Scheme = BestScheme(rot, null, college);
        t.FullCourtPress = Presses(rot, college);
        t.Roles.Clear();
        t.StarStopperId = null;
        t.DoubleStar = false;
        t.Auto = false;
    }

    /// <summary>A short scouting report on the next opponent, with what the assistant coach would do about it.</summary>
    public static string ScoutingReport(Team us, Team opp, DateOnly? on, bool college = false) => string.Join(" ", ScoutingLines(us, opp, on, college));

    /// <summary>H101 (run 112): the report's first sentence, for Home's next-game card. Splitting the joined report on
    /// ". " cut "St. Louis FC" to "St."; the headline is the first line as built.</summary>
    public static string ScoutingHeadline(Team us, Team opp, DateOnly? on, bool college = false) => ScoutingLines(us, opp, on, college).FirstOrDefault() ?? "";

    static List<string> ScoutingLines(Team us, Team opp, DateOnly? on, bool college)
    {
        var oppRot = Rotation(opp, on);
        var ourRot = Rotation(us, on);
        if (oppRot.Count == 0) return new();
        var (theirSystem, theirScheme) = TheirPlan(opp, us, on, college);
        var (star, dbl) = TheirStar(opp, on, us, college);
        var ourScheme = BestScheme(ourRot, theirSystem, college);
        var ourSystem = BestSystem(ourRot, theirScheme);
        var stopper = Stopper(ourRot);
        var lines = new List<string>
        {
            $"{opp.FullName} will likely run {Systems.Name(theirSystem).ToLower()} and play {Systems.Name(theirScheme).ToLower()} defense against us.",
        };
        if (star != null)
            lines.Add($"Their go-to scorer is {star.Name} ({star.PrimaryPosition}). " + (dbl ? "He dominates their offense: consider doubling him." : "He isn't dominant enough to double; put a stopper on him.")
                      + (stopper != null ? $" Best stopper: {stopper.Name}." : ""));
        // Run 194: a pressing college opponent is named in the report (the press is chosen from the roster, so it's scoutable).
        if (college && (opp.Tactic.Auto ? Presses(oppRot, true) : opp.Tactic.FullCourtPress))
            lines.Add("Expect them to press full court after every basket: take care of the ball, and a broken press means layups.");
        lines.Add($"Assistant's suggestion: {Systems.Name(ourSystem).ToLower()} on offense, {Systems.Name(ourScheme).ToLower()} on defense"
                  + (college && Presses(ourRot, true) ? ", and press them full court." : "."));
        // K2: the same matchup model as the Tactics page; a poor side says why it's still his pick.
        var read = new MatchupRead(theirSystem, theirScheme, college);
        if (read.Defence(ourScheme) >= MatchupRead.Notable) lines.Add($"{Systems.Name(ourScheme)} is a good answer to {Systems.Name(theirSystem).ToLower()}.");
        if (read.WhySystem(ourSystem) is { Length: > 0 } ws) lines.Add(ws);
        if (read.WhyScheme(ourScheme) is { Length: > 0 } wd) lines.Add(wd);
        // Being predictable has a cost (#267): after three games of the same thing, the other bench has it on film.
        if (!us.Tactic.Auto && (OnFilm(us.Tactic.RecentSystems) || OnFilm(us.Tactic.RecentSchemes)))
            lines.Add($"They've seen us run {Systems.Name(us.Tactic.RecentSystems[^1]).ToLower()} and {Systems.Name(us.Tactic.RecentSchemes[^1]).ToLower()} defense {FilmReadGames} games running and will plan for it; a change would catch them out.");
        return lines;
    }
}
