using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>
/// How offensive systems and defensive schemes change a possession (decisions #58-59). Every number is a small tilt
/// around the Balanced / Man-to-man baseline so league-wide stats stay where the balance audit wants them; what
/// matters is fit (do your players suit the system?) and the matchup (does it beat their scheme?).
/// </summary>
public static class Tactics
{
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
        _ => One,
    };

    /// <summary>Contest adjustment by scheme and shot zone (attribute-scale units, like the rest of the contest).</summary>
    public static double SchemeContest(DefensiveScheme s, ShotZone z) => s switch
    {
        DefensiveScheme.Zone23 => z == ShotZone.Rim ? 0.6 : z.IsThree() ? -0.9 : 0,
        DefensiveScheme.Drop => z == ShotZone.Rim ? 0.5 : z == ShotZone.LongMid ? -2.0 : z == ShotZone.ShortMid ? -0.8 : z == ShotZone.AboveBreakThree ? -0.6 : 0,
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
        + (def.Scheme switch { DefensiveScheme.Zone23 => 0.002, DefensiveScheme.SwitchEverything => 0.004, _ => 0 })
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
        _ => 0,
    };

    /// <summary>Shot logit from system fit, scheme fit and the matchup, for the ten players on the floor.</summary>
    public static double FitLogit(Tactic off, Tactic def, IReadOnlyCollection<Player> offense, IReadOnlyCollection<Player> defense, EngineConfig cfg)
    {
        double l = Matchup(off.System, def.Scheme);
        if (off.System != OffensiveSystem.Balanced) l += cfg.SystemFitLogit * (Systems.Fit(off.System, offense) - 0.5);
        if (def.Scheme != DefensiveScheme.Man) l -= cfg.SchemeFitLogit * (Systems.Fit(def.Scheme, defense) - 0.5);
        return l;
    }
}
