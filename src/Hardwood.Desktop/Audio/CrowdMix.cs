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
    public static BedLevels Target(double crowd, int homeSwing, bool lateAndClose, bool paused, bool quiet)
    {
        crowd = Math.Clamp(crowd, 0, 1);
        double excitement = Math.Clamp(Math.Max(0, homeSwing - 3) / 9.0, 0, 1) * 0.6 + (lateAndClose ? 0.6 : 0);
        excitement = Math.Clamp(excitement, 0, 1);
        double level = (0.30 + 0.45 * crowd) * (1 + 0.30 * excitement);
        double highShare = Math.Clamp(Math.Clamp((crowd - 0.45) / 0.55, 0, 1) * 0.7 + 0.3 * excitement, 0, 1);
        if (paused) level *= 0.4;
        else if (quiet) level *= 0.55;
        level = Math.Min(level, 1.0);
        return new BedLevels(level * (1 - highShare), level * highShare);
    }

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
}
