using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>
/// How offensive systems and defensive schemes change a possession (decisions #58-59). Every number is a small tilt
/// around the Balanced / Man-to-man baseline so league-wide stats stay where the balance audit wants them; what
/// matters is fit (do your players suit the system?) and the matchup (does it beat their scheme?).
/// </summary>
public static class Tactics
{
    /// <summary>Run 194 (#1170): the schemes a league's benches can pick. The pros keep the four they always had (the NBA's
    /// rows stay unchanged); college adds the 1-3-1 and the match-up zone, and the full-court press (<see cref="Tactic.FullCourtPress"/>).</summary>
    public static IReadOnlyList<DefensiveScheme> Schemes(bool college) => college ? AllSchemes : ProSchemes;
    private static readonly DefensiveScheme[] ProSchemes = { DefensiveScheme.Man, DefensiveScheme.SwitchEverything, DefensiveScheme.Zone23, DefensiveScheme.Drop };
    private static readonly DefensiveScheme[] AllSchemes = Enum.GetValues<DefensiveScheme>();

    /// <summary>Run 194: how much a college scheme's fit counts, against the pros' <see cref="EngineConfig.SchemeFitLogit"/>. A
    /// college fit tracks talent (the best programs suit every scheme best), so at full weight it made the favourites'
    /// defence better still: `madness` on seed 2026 went to 5 v 12 79% and 1-seed titles 58% (from 69% and 46%).</summary>
    public const double CollegeSchemeFitScale = 0.2;

    /// <summary>A zone of any kind (the 2-3, the 1-3-1, the match-up).</summary>
    public static bool IsZone(DefensiveScheme s) => s is DefensiveScheme.Zone23 or DefensiveScheme.Zone131 or DefensiveScheme.MatchUpZone;

    // Run 194: the full-court press. Measured with `hardwood collegebox` and `hardwood pressbox` (seed 31, 2026): a pressing
    // side forces about 3 more turnovers a game (TOV% 17.5 → about 21, near VCU's and Arkansas's real 21-23), the game
    // gets about 3 possessions quicker, and a broken press gives up layups; the pressers tire a third faster and reach more.
    /// <summary>Possessions per 48 added when the defence presses.</summary>
    public const double PressPace = 3;
    /// <summary>Turnover chance added on a pressed possession at an average press fit (0.5); ± <see cref="PressFitTurnover"/> × (fit − 0.5).</summary>
    public const double PressTurnover = 0.022, PressFitTurnover = 0.02;
    /// <summary>Share of pressed turnovers that are steals (live balls) instead of the usual <see cref="EngineConfig.LiveBallTurnoverShare"/>.</summary>
    public const double PressLiveBallShare = 0.70;
    /// <summary>A broken press: rim shots weigh more and are contested less on the possession's first shot.</summary>
    public const double PressBrokenRimWeight = 1.3, PressBrokenRimContest = 1.4;
    /// <summary>Extra reach-in fouls on a pressed possession, and how much faster the pressers tire.</summary>
    public const double PressFoulRate = 0.012, PressEnergyDrain = 1.35;
    /// <summary>Offensive-rebound chance added against a 1-3-1 (nobody boxes out the weak side) and a match-up zone.</summary>
    public static double ZoneOffRebound(DefensiveScheme s) => s switch { DefensiveScheme.Zone131 => 0.03, DefensiveScheme.MatchUpZone => 0.01, _ => 0 };

    /// <summary>Change to possessions per 48 minutes.</summary>
    public static double PaceShift(OffensiveSystem s) => s switch
    {
        OffensiveSystem.PaceAndSpace => 4,
        OffensiveSystem.PostCentric => -3,
        OffensiveSystem.Isolation => -1.5,
        _ => 0,
    };

    /// <summary>Multiplier on how sharply shots concentrate on the best scorers.</summary>
    public static double SharpnessFactor(OffensiveSystem s) => s switch
    {
        OffensiveSystem.Isolation => 1.15,
        OffensiveSystem.Motion => 0.88,
        _ => 1.0,
    };

    /// <summary>Per-player shot-share multiplier from the system (who the plays are run for).</summary>
    public static double SystemUsage(OffensiveSystem s, Player p) => s switch
    {
        OffensiveSystem.PickAndRoll => 0.85 + p.Attributes[Attr.PickAndRollHandler] / 55.0 + (p.PrimaryPosition >= Position.PF ? p.Attributes[Attr.Dunk] / 80.0 : 0),
        OffensiveSystem.PostCentric => 0.8 + p.Attributes[Attr.PostMoves] / 40.0,
        OffensiveSystem.PaceAndSpace => 0.8 + p.Attributes[Attr.ThreePoint] / 50.0,
        _ => 1.0,
    };

    // Zone mixes: Rim, ShortMid, LongMid, CornerThree, AboveBreakThree.
    private static readonly double[] One = { 1, 1, 1, 1, 1 };
    public static double[] SystemZoneMix(OffensiveSystem s) => s switch
    {
        OffensiveSystem.PickAndRoll => new[] { 1.4, 1.0, 0.72, 1.05, 1.0 },   // run 48: was 1.35 / 0.8 at the rim and long twos; review #4: rolls and kick-outs, fewer long twos
        OffensiveSystem.Motion => new[] { 1.0, 0.95, 0.85, 1.3, 1.05 },
        OffensiveSystem.PostCentric => new[] { 1.3, 1.3, 0.75, 0.85, 0.8 }, // run 48: long twos 0.85 -> 0.75 (the weakest on real rosters); run 19 (#255): fewer long twos, a few more kick-outs once threes were tuned
        OffensiveSystem.PaceAndSpace => new[] { 1.03, 0.85, 0.78, 1.12, 1.08 }, // run 48 (real rosters): was 1.05, 0.85, 0.75, 1.18, 1.15, the best by 4+; review #4: a near-free 6-7 win-% edge before that
        OffensiveSystem.Balanced => new[] { 1.06, 0.98, 0.78, 1.08, 1.03 }, // run 48 (real rosters): fewer long twos, was 1.03, 1.0, 0.92, 1.04, 1.0 (8+ behind)
        OffensiveSystem.Isolation => new[] { 1.18, 1.1, 0.82, 0.9, 1.0 },  // run 48: long twos 0.95 -> 0.82; review #4: drives over long twos (it was the weakest again)
        _ => One,
    };

    /// <summary>What a defensive scheme gives up and takes away (applied to the offense's shot mix).</summary>
    public static double[] SchemeZoneMix(DefensiveScheme s) => s switch
    {
        DefensiveScheme.Zone23 => new[] { 0.88, 0.95, 1.0, 1.12, 1.1 },
        DefensiveScheme.Drop => new[] { 0.95, 1.1, 1.25, 1.0, 1.0 },
        DefensiveScheme.Zone131 => new[] { 0.92, 1.0, 1.04, 1.25, 1.02 },  // run 194: the corners are the 1-3-1's hole
        DefensiveScheme.MatchUpZone => new[] { 0.94, 0.98, 1.02, 1.05, 1.05 },
        _ => One,
    };

    /// <summary>Contest adjustment by scheme and shot zone (attribute-scale units, like the rest of the contest).</summary>
    public static double SchemeContest(DefensiveScheme s, ShotZone z) => s switch
    {
        DefensiveScheme.Zone23 => z == ShotZone.Rim ? 0.6 : z.IsThree() ? -0.9 : 0,
        DefensiveScheme.Drop => z == ShotZone.Rim ? 0.5 : z == ShotZone.LongMid ? -2.0 : z == ShotZone.ShortMid ? -0.8 : z == ShotZone.AboveBreakThree ? -0.6 : 0,
        DefensiveScheme.Zone131 => z == ShotZone.Rim ? 0.3 : z == ShotZone.CornerThree ? -1.4 : z == ShotZone.AboveBreakThree ? 0.4 : 0,
        DefensiveScheme.MatchUpZone => z == ShotZone.Rim ? 0.35 : z.IsThree() ? -0.4 : 0,
        _ => 0,
    };

    public static double AssistFactor(OffensiveSystem s) => s switch
    {
        OffensiveSystem.Motion => 1.12,
        OffensiveSystem.Isolation => 0.88,
        OffensiveSystem.PostCentric => 0.95,
        _ => 1.0,
    };

    public static double TurnoverShift(Tactic off, Tactic def) =>
        (off.System switch { OffensiveSystem.Motion => 0.006, OffensiveSystem.Isolation => -0.012, OffensiveSystem.PostCentric => 0.003, OffensiveSystem.PaceAndSpace => 0.004, _ => 0 })
        + (def.Scheme switch { DefensiveScheme.Zone23 => 0.002, DefensiveScheme.SwitchEverything => 0.004, DefensiveScheme.Zone131 => 0.010, DefensiveScheme.MatchUpZone => 0.002, _ => 0 })
        + (def.DoubleStar ? 0.004 : 0);

    /// <summary>
    /// Rock-paper-scissors: how an offensive system fares against a defensive scheme (shot logit). Switching kills
    /// pick-and-roll but gives up post mismatches; a zone stops the post and isolation but is shot over by motion and
    /// spacing; drop coverage invites pull-ups.
    /// </summary>
    public static double Matchup(OffensiveSystem o, DefensiveScheme d) => (o, d) switch
    {
        (OffensiveSystem.PickAndRoll, DefensiveScheme.SwitchEverything) => -0.025, // run 48: -0.05 left it 10 points behind on real rosters
        (OffensiveSystem.PickAndRoll, DefensiveScheme.Zone23) => 0.03,
        (OffensiveSystem.PickAndRoll, DefensiveScheme.Drop) => 0.04,
        (OffensiveSystem.Motion, DefensiveScheme.SwitchEverything) => 0.04, // run 48: was 0.03
        (OffensiveSystem.Motion, DefensiveScheme.Zone23) => 0.05, // run 48: counters pay a little more (the halftime read), was 0.04
        (OffensiveSystem.PostCentric, DefensiveScheme.SwitchEverything) => 0.06, // run 48: was 0.05
        (OffensiveSystem.PostCentric, DefensiveScheme.Zone23) => -0.035, // run 48: was -0.05
        (OffensiveSystem.PostCentric, DefensiveScheme.Drop) => -0.02, // run 48: was -0.03
        (OffensiveSystem.PaceAndSpace, DefensiveScheme.Zone23) => 0.05, // run 48: was 0.04
        (OffensiveSystem.PaceAndSpace, DefensiveScheme.Drop) => -0.02,
        (OffensiveSystem.Isolation, DefensiveScheme.SwitchEverything) => 0.04, // run 48: was 0.03
        (OffensiveSystem.Isolation, DefensiveScheme.Zone23) => -0.03, // run 48: was -0.04
        (OffensiveSystem.Isolation, DefensiveScheme.Drop) => 0.02,
        // Run 194: the college zones. The 1-3-1 traps the ball handler and walls off isolation, but ball movement and
        // shooters find its corners; the match-up zone is in between, a smaller version of the 2-3's trade.
        (OffensiveSystem.PickAndRoll, DefensiveScheme.Zone131) => -0.01,
        (OffensiveSystem.Motion, DefensiveScheme.Zone131) => 0.04,
        (OffensiveSystem.PostCentric, DefensiveScheme.Zone131) => 0.01,
        (OffensiveSystem.PaceAndSpace, DefensiveScheme.Zone131) => 0.045,
        (OffensiveSystem.Isolation, DefensiveScheme.Zone131) => -0.035,
        (OffensiveSystem.PickAndRoll, DefensiveScheme.MatchUpZone) => 0.01,
        (OffensiveSystem.Motion, DefensiveScheme.MatchUpZone) => 0.02,
        (OffensiveSystem.PostCentric, DefensiveScheme.MatchUpZone) => -0.025,
        (OffensiveSystem.PaceAndSpace, DefensiveScheme.MatchUpZone) => 0.02,
        (OffensiveSystem.Isolation, DefensiveScheme.MatchUpZone) => -0.02,
        _ => 0,
    };

    /// <summary>Shot logit from system fit, scheme fit and the matchup, for the ten players on the floor.</summary>
    /// <param name="college">Run 194: college rules, where scheme fits are measured against college rotations
    /// (<see cref="Systems.Fit(DefensiveScheme, IReadOnlyCollection{Player}, bool)"/>).</param>
    public static double FitLogit(Tactic off, Tactic def, IReadOnlyCollection<Player> offense, IReadOnlyCollection<Player> defense, EngineConfig cfg, bool college = false)
    {
        double l = Matchup(off.System, def.Scheme);
        if (off.System != OffensiveSystem.Balanced) l += cfg.SystemFitLogit * (Systems.Fit(off.System, offense) - 0.5);
        if (def.Scheme != DefensiveScheme.Man) l -= cfg.SchemeFitLogit * (college ? CollegeSchemeFitScale : 1) * (Systems.Fit(def.Scheme, defense, college) - 0.5);
        return l;
    }
}
