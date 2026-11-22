namespace Hardwood.Desktop.Audio;

/// <summary>The two crowd beds' levels (0–1 each, before the master volume).</summary>
public readonly record struct BedLevels(double Low, double High)
{
    public double Total => Low + High;
}

/// <summary>
/// The crowd bed's mix (M6, run 153; design §F), pure. Attendance sets the house: a thin one leans to the low bed, a
/// sell-out to the high bed. A home run or a close game late lifts it. The mixer eases toward the target with
/// <see cref="Ease"/>, so a swell takes 1–2 seconds and never jumps.
/// </summary>
public static class CrowdMix
{
    /// <summary>The easing's time constant: 90% of the way in about 1.2 s.</summary>
    public const double Tau = 0.52;
    /// <summary>How far the bed dips on a groan (an away basket), and for how long.</summary>
    public const double GroanDepth = 0.45, GroanTime = 1.3;

    /// <param name="crowd">The house, 0–1 (<see cref="Hardwood.Core.College.ArenaDress.Crowd"/>).</param>
    /// <param name="homeSwing">The momentum swing in points over the last stretch, home positive (ArenaBoard.Momentum).</param>
    /// <param name="lateAndClose">The last period or overtime, under two minutes, within six.</param>
    /// <param name="paused">The game is paused or at a break: the crowd murmurs.</param>
    /// <param name="quiet">4× or 8× (not Key moments): the bed plays quieter.</param>
    /// <param name="homeMargin">The score on screen, home minus away (run 157).</param>
    /// <param name="worstDeficit">The most the home side has trailed by so far this game (run 157).</param>
    public static BedLevels Target(double crowd, int homeSwing, bool lateAndClose, bool paused, bool quiet,
                                   int homeMargin = 0, int worstDeficit = 0)
    {
        crowd = Math.Clamp(crowd, 0, 1);
        double excitement = Math.Clamp(Math.Max(0, homeSwing - 3) / 9.0, 0, 1) * 0.6 + (lateAndClose ? 0.6 : 0);
        // Run 157: a comeback brings them back to life: down 10 or more earlier, the home side has cut 8 off it and is on a run.
        if (Comeback(homeMargin, worstDeficit, homeSwing)) excitement += 0.5;
        double beaten = Beaten(homeMargin);
        // Beaten badly, a home run alone doesn't lift them (a real comeback does, above).
        if (beaten < 1 && !Comeback(homeMargin, worstDeficit, homeSwing)) excitement *= beaten;
        excitement = Math.Clamp(excitement, 0, 1);
        double level = (0.30 + 0.45 * crowd) * (1 + 0.30 * excitement) * beaten;
        double highShare = Math.Clamp(Math.Clamp((crowd - 0.45) / 0.55, 0, 1) * 0.7 * beaten + 0.3 * excitement, 0, 1);
        if (paused) level *= 0.4;
        else if (quiet) level *= 0.55;
        level = Math.Min(level, 1.0);
        return new BedLevels(level * (1 - highShare), level * highShare);
    }

    /// <summary>The bed's multiplier as the home side falls behind (run 157; Danny: "goes quiet when the home team starts to
    /// really get beat hard"): whole within 12, 0.65 at 15 down, 0.45 from 20 down. Smooth in between, so it sinks as the
    /// deficit grows rather than dropping at a line.</summary>
    public static double Beaten(int homeMargin)
    {
        int d = -homeMargin;
        if (d <= 12) return 1;
        if (d <= 15) return 1 - (d - 12) / 3.0 * 0.35;
        if (d <= 20) return 0.65 - (d - 15) / 5.0 * 0.20;
        return 0.45;
    }

    /// <summary>A comeback (run 157): the home side trailed by 10 or more, has cut at least 8 off its worst deficit, and the
    /// last stretch is going its way.</summary>
    public static bool Comeback(int homeMargin, int worstDeficit, int homeSwing) =>
        worstDeficit >= 10 && worstDeficit - Math.Max(0, -homeMargin) >= 8 && homeSwing > 0;

    /// <summary>One easing step of <paramref name="dt"/> seconds from <paramref name="now"/> toward <paramref name="target"/>.</summary>
    public static BedLevels Ease(BedLevels now, BedLevels target, double dt)
    {
        double k = 1 - Math.Exp(-Math.Max(0, dt) / Tau);
        return new BedLevels(now.Low + (target.Low - now.Low) * k, now.High + (target.High - now.High) * k);
    }

    /// <summary>The bed's multiplier <paramref name="age"/> seconds into a groan of strength <paramref name="depth"/> (0–1):
    /// down over 0.2 s, back up by <see cref="GroanTime"/>.</summary>
    public static double Groan(double age, double depth)
    {
        if (age < 0 || age >= GroanTime) return 1;
        double shape = age < 0.2 ? age / 0.2 : 1 - (age - 0.2) / (GroanTime - 0.2);
        return 1 - GroanDepth * Math.Clamp(depth, 0, 1) * shape;
    }

    /// <summary>How long a boo's swell lasts when no boo file is embedded yet (run 157).</summary>
    public const double SwellTime = 1.6;

    /// <summary>The low bed's multiplier <paramref name="age"/> seconds into a boo's swell of strength <paramref name="depth"/>
    /// (0–1): up over 0.25 s to 1 + 0.8 × depth, back down by <see cref="SwellTime"/>. It's the crowd's own bed, louder, not
    /// a sound made in code.</summary>
    public static double Swell(double age, double depth)
    {
        if (age < 0 || age >= SwellTime) return 1;
        double shape = age < 0.25 ? age / 0.25 : 1 - (age - 0.25) / (SwellTime - 0.25);
        return 1 + 0.8 * Math.Clamp(depth, 0, 1) * shape;
    }
}
