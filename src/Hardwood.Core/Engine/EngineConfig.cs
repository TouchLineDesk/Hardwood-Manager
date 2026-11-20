namespace Hardwood.Core.Engine;

/// <summary>
/// Every tunable number in the match engine lives here so calibration never touches sim logic.
/// Defaults are tuned so a league of average generated players reproduces modern NBA averages.
/// </summary>
public sealed class EngineConfig
{
    // Nov 11 (engine dial-in): fewer rim shots and fouled makes lowered FG% to .451; two-point zones up 0.04-0.05.
    // --- Make probability: logit = ZoneBase + SkillBeta*(skill-11) - ContestBeta*(contest-11) - FatigueBeta*(1-energy) + Home*home
    // Two-point zones down another 0.02-0.03 (Nov 10): the late-game catch-up push lifted FG% to .486-.488 (critic review #3).
    // Lowered 0.065 across the board (Nov 9): roles and fitting systems steer shots to players' strengths, and the
    // players taking most shots are usually the happiest (morale); together FG% +1.2 pts before this.
    public double[] ZoneBaseLogit { get; set; } =
    {
        0.265,  // Rim           ≈ 65% after shooter selection bias (review #4: the rebalanced systems drive more)
        -0.425, // ShortMid      ≈ 44%
        -0.545, // LongMid       ≈ 41%
        -0.535, // CornerThree   ≈ 39% (run 19, #255: +0.05 with the skill term at 0.75)
        -0.715, // AboveBreak3   ≈ 35%
    };
    public double SkillBeta { get; set; } = 0.060;
    /// <summary>
    /// Run 19 (#255): the skill term on threes counts this much of <see cref="SkillBeta"/>. The best volume shooters made
    /// .43-.45 on 10-12 attempts a game, and the season threes leader made 424-462 (NBA 240-402).
    /// </summary>
    /// Run 20 (#259, Danny's D15 B): loosened from 0.75 to 0.85, so the leader makes about 350-395 and Curry's 402 falls now and then.
    public double ThreeSkillShare { get; set; } = 0.85;
    /// <summary>
    /// Run 19 (#255): a player's three-point tendency counts in full up to this rate; above it, only
    /// <see cref="ThreeRateAboveKnee"/> of each extra point counts, so the most trigger-happy shooters don't take
    /// three-quarters of their shots from deep at star usage.
    /// </summary>
    public double ThreeRateKnee { get; set; } = 45;
    /// Run 20 (#259, D15 B): 0.4 -> 0.7.
    public double ThreeRateAboveKnee { get; set; } = 0.7;
    /// <summary>
    /// Run 19 (#255): after this many three-point attempts in a game, each further attempt makes another three
    /// <see cref="ThreeGameDecay"/> less likely (multiplicatively), as defences run him off the line. 0 = off.
    /// </summary>
    /// Run 20 (#259, D15 B): 8 -> 9.
    public int ThreeGameKnee { get; set; } = 9;
    public double ThreeGameDecay { get; set; } = 0.2;
    public double ContestBeta { get; set; } = 0.060;
    public double FatigueBeta { get; set; } = 0.55;
    public double HomeLogit { get; set; } = 0.07;
    /// <summary>
    /// Score effects: make-probability logit change per point of lead (minus when ahead, plus when behind; capped at 25).
    /// Real leads shrink (the leader relaxes and rests starters, the trailer pushes); without this, single games were
    /// ~45% more random than the NBA's ±12-13 points around expectation (balance audit, Nov 9).
    /// </summary>
    public double ScoreEffectPerPoint { get; set; } = 0.010;
    /// <summary>Per-game, per-player random form; scaled by (21 - Consistency).</summary>
    public double FormSd { get; set; } = 0.035;

    // --- Free throws
    public double FtBaseLogit { get; set; } = 1.12; // ≈ 78%
    public double FtSkillBeta { get; set; } = 0.09;

    // --- Possession flow
    public double TurnoverBase { get; set; } = 0.118;
    public double TurnoverSkillBeta { get; set; } = 0.0085; // per point of (ball security - defence pressure)
    public double LiveBallTurnoverShare { get; set; } = 0.58;
    public double NonShootingFoulRate { get; set; } = 0.065;
    public double[] ShootingFoulRateByZone { get; set; } = { 0.175, 0.065, 0.040, 0.016, 0.016 };
    /// <summary>A fouled shot goes in at this share of its normal chance (Nov 11: and-ones were 4-8 a team a game).</summary>
    public double AndOneRate { get; set; } = 0.45;
    public double OffensiveReboundBase { get; set; } = 0.205;
    /// <summary>Share of misses that become a team rebound or go out of bounds (no individual credit, possession flips).</summary>
    public double TeamReboundShare { get; set; } = 0.08;
    public double ReboundSkillBeta { get; set; } = 0.012;
    public double BlockRateRim { get; set; } = 0.26;
    public double BlockRateMid { get; set; } = 0.06;
    public double BlockSkillBeta { get; set; } = 0.012;
    /// <summary>H94 (run 109): how much likelier the man on the ball is than a helper to be the blocker (rim, mid-range).</summary>
    public double BlockOnBallRim { get; set; } = 1.5;
    public double BlockOnBallMid { get; set; } = 3.0;

    /// <summary>How strongly shots go to the players best at creating them (exponent on usage). Higher = stars take more shots.</summary>
    // Players as people (decision #64): a delighted player (morale 100) shoots this much better (logit) than a
    // content one (60), a miserable one (20) this much worse; same scale for team chemistry. Kept small: winning makes
    // players happy, so a bigger effect fed dynasties (balance audit, Nov 9: 3 champions in 10 years at 0.04/0.03).
    public double MoraleLogit { get; set; } = 0.02;
    public double ChemistryLogit { get; set; } = 0.015;
    /// <summary>Turnover probability change per 40 points of chemistry above/below 60.</summary>
    public double ChemistryTurnover { get; set; } = 0.002;

    // Tactics (decisions #58-61).
    /// <summary>Logit added to every shot per unit of system fit above 0.5 (offense), and subtracted per unit of scheme fit (defense).</summary>
    public double SystemFitLogit { get; set; } = 0.16;
    public double SchemeFitLogit { get; set; } = 0.16;
    /// <summary>Logit per role-suitability star above 3 for the shooter.</summary>
    public double RoleFitLogit { get; set; } = 0.015;
    /// <summary>Doubled star: his shot share is multiplied by this; he faces this much extra contest; his teammates this much less.</summary>
    public double DoubleStarUsage { get; set; } = 0.85; // H105 (run 113): 0.65 → 0.85, applied before the shot-share cap, so a capped star loses only a few shots; at 0.65 after the cap, Gilgeous-Alexander (doubled most nights) fell out of the top 10
    public double DoubleStarContest { get; set; } = 2.0;
    public double DoubleStarOpenLook { get; set; } = 1.2;
    /// <summary>Fourth-quarter catch-up: shot logit for a side trailing by 7+ (and 70% of it off a side leading by 7+).</summary>
    public double CatchUpLogit { get; set; } = 0.40;
    /// <summary>H105: a star's shot weight is 1 + (CA - floor) / scale above the floor, at most +cap. Absolute, not
    /// relative to teammates, so a thin team's best player gains nothing from being the best of a thin team.</summary>
    public double StarAbilityFloor { get; set; } = 155;
    public double StarAbilityScale { get; set; } = 20;
    /// <summary>The most the star term adds (x2 at CA 175+ with the neutral Star usage; the slider scales it): uncapped, ten seasons of development took leaders to 40+ ppg.</summary>
    public double StarAbilityCap { get; set; } = 1.0;
    /// <summary>H105: the soft cap on one player's share of the five's shots (see GameSimulator.SoftCapShare) and the part
    /// of the share above it that he keeps.</summary>
    public double MaxShotShare { get; set; } = 0.28;
    public double ShotShareOverCap { get; set; } = 0.25;
    /// <summary>H105: a star's cap is higher by this much per unit of his star term (so up to +StarAbilityCap times it).</summary>
    public double StarShareBonus { get; set; } = 0.05;
    public double UsageSharpness { get; set; } = 1.4; // H105 (run 112): 1.65 → 1.4 with the star term; earlier: 1.75 still gave 36-37 ppg leaders on some seeds (Nov 10, fixed PA); 2.0 gave 36-37 ppg leaders once the world kept its stars (critic review #2, Nov 10); // 1.6 left the best player near 24 ppg; 2.2 gave 37 ppg with the soft cap; audit Nov 9

    // --- Assists: share of made shots that are assisted, by zone
    public double[] AssistedShareByZone { get; set; } = { 0.57, 0.46, 0.49, 0.92, 0.80 };

    // --- Shot zone mix (league baseline, before player tendencies)
    // Run 19 (#255): rim 0.285 ×0.94 and mid-range ×1.06, so the threes the knee takes away don't all become rim shots.
    public double[] ZoneBaseWeight { get; set; } = { 0.2679, 0.2014, 0.1272, 0.10, 0.30 };

    // --- Clock and fatigue
    public double SecondsPerPossessionSd { get; set; } = 5.0;
    public double TransitionSeconds { get; set; } = 7.0;
    public double EnergyDrainPerPossession { get; set; } = 0.0045;
    public double EnergyRecoverPerPossession { get; set; } = 0.0055;
    public double SubOutEnergy { get; set; } = 0.70;
    public double SubInMinEnergy { get; set; } = 0.85;
    public int RotationSize { get; set; } = 10;
    /// <summary>Target minutes per rotation slot (starters first, best to worst), summing to 240.</summary>
    public double[] TargetMinutesBySlot { get; set; } = { 36, 34, 32, 30, 27, 24, 20, 16, 12, 9 };
    public double RegulationMinutes { get; set; } = 48;
    /// <summary>How far ahead of his minutes pace a player may run before he is pulled.</summary>
    public double MinutesSlackSeconds { get; set; } = 120;
    /// <summary>Fourth-quarter margin (at 0:00, growing 1.5/min earlier) at which the deep bench comes in.</summary>
    public int BlowoutMargin { get; set; } = 18;
    /// <summary>How long a hand-picked live lineup is protected from automatic substitutions (game seconds).</summary>
    public double ManualLineupHoldSeconds { get; set; } = 240;

    // --- Injuries: per player-game probability at 30 minutes for an average player (≈ 7 injuries per team per season)
    public double InjuryBaseRate { get; set; } = 0.011;

    // --- Overtime
    public int OvertimeMinutes { get; set; } = 5;

    public static EngineConfig Default => new();

    /// <summary>
    /// A copy in which every player-against-player edge counts <paramref name="k"/> times as much: shooting skill against
    /// the contest, ball security against pressure, rebounding. Each is a difference (or centred on both sides alike), so
    /// even teams score about the same and only the gaps grow. Used for college games (C4 step 0, #185).
    /// </summary>
    public EngineConfig ForTalentScale(double k)
    {
        var c = (EngineConfig)MemberwiseClone();
        c.SkillBeta *= k; c.ContestBeta *= k; c.TurnoverSkillBeta *= k; c.ReboundSkillBeta *= k;
        return c;
    }

    /// <summary>The settings a game under these rules plays with: talent scaled, and no home edge on a neutral court.</summary>
    public EngineConfig For(Model.Rules rules)
    {
        if (rules.TalentScale == 1.0 && !rules.NeutralSite && rules.HomeScale == 1.0 && rules.ShotLogitShift == 0 && rules.FoulScale == 1.0
            && rules.OffRebShift == 0 && rules.TurnoverShift == 0 && rules.ThreeRateScale == 1.0 && rules.ThreeLogitShift == 0 && rules.CommonFoulScale == 1.0) return this;
        var c = rules.TalentScale == 1.0 ? (EngineConfig)MemberwiseClone() : ForTalentScale(rules.TalentScale);
        if (rules.NeutralSite) c.HomeLogit = 0;
        else c.HomeLogit *= rules.HomeScale; // C5 part 2: a college arena's home court
        // C5 (run 96): a conference's style. The arrays are copied, never changed in place (the clone shares them).
        if (rules.ShotLogitShift != 0) c.ZoneBaseLogit = ZoneBaseLogit.Select(x => x + rules.ShotLogitShift).ToArray();
        // K11 (run 134): the college profile. Threes are zones 3 and 4 (corner, above the break).
        if (rules.ThreeLogitShift != 0) c.ZoneBaseLogit = c.ZoneBaseLogit.Select((x, z) => z >= 3 ? x + rules.ThreeLogitShift : x).ToArray();
        if (rules.ThreeRateScale != 1.0) c.ZoneBaseWeight = ZoneBaseWeight.Select((x, z) => z >= 3 ? x * rules.ThreeRateScale : x).ToArray();
        c.OffensiveReboundBase = OffensiveReboundBase + rules.OffRebShift;
        c.TurnoverBase = TurnoverBase + rules.TurnoverShift;
        c.NonShootingFoulRate = NonShootingFoulRate * rules.CommonFoulScale; // college: more reach-ins and holds before the bonus
        if (rules.FoulScale != 1.0)
        {
            c.NonShootingFoulRate = NonShootingFoulRate * rules.CommonFoulScale * rules.FoulScale;
            c.ShootingFoulRateByZone = ShootingFoulRateByZone.Select(x => x * rules.FoulScale).ToArray();
        }
        return c;
    }
}
