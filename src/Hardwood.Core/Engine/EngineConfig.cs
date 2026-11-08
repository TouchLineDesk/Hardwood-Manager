namespace Hardwood.Core.Engine;

/// <summary>
/// Every tunable number in the match engine lives here so calibration never touches sim logic.
/// Defaults are tuned so a league of average generated players reproduces modern NBA averages.
/// </summary>
public sealed class EngineConfig
{
    // --- Make probability: logit = ZoneBase + SkillBeta*(skill-11) - ContestBeta*(contest-11) - FatigueBeta*(1-energy) + Home*home
    public double[] ZoneBaseLogit { get; set; } =
    {
        0.33,   // Rim           ≈ 65% after shooter selection bias
        -0.38,  // ShortMid      ≈ 44%
        -0.50,  // LongMid       ≈ 41%
        -0.52,  // CornerThree   ≈ 39%
        -0.70,  // AboveBreak3   ≈ 35%
    };
    public double SkillBeta { get; set; } = 0.060;
    public double ContestBeta { get; set; } = 0.060;
    public double FatigueBeta { get; set; } = 0.55;
    public double HomeLogit { get; set; } = 0.07;
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
    public double[] ShootingFoulRateByZone { get; set; } = { 0.215, 0.080, 0.050, 0.020, 0.020 };
    public double AndOneRate { get; set; } = 0.18;
    public double OffensiveReboundBase { get; set; } = 0.205;
    /// <summary>Share of misses that become a team rebound or go out of bounds (no individual credit, possession flips).</summary>
    public double TeamReboundShare { get; set; } = 0.08;
    public double ReboundSkillBeta { get; set; } = 0.012;
    public double BlockRateRim { get; set; } = 0.30;
    public double BlockRateMid { get; set; } = 0.07;
    public double BlockSkillBeta { get; set; } = 0.012;

    // --- Assists: share of made shots that are assisted, by zone
    public double[] AssistedShareByZone { get; set; } = { 0.52, 0.42, 0.45, 0.88, 0.74 };

    // --- Shot zone mix (league baseline, before player tendencies)
    public double[] ZoneBaseWeight { get; set; } = { 0.32, 0.17, 0.12, 0.10, 0.29 };

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
}
