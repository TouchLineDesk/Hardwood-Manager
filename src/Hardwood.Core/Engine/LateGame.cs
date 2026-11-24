namespace Hardwood.Core.Engine;

/// <summary>
/// K28 (run 177, critic review #22): late-game management. Before this, the only intentional foul came in the last 40
/// seconds with the trailing side down one to six, so a college team down 7 with 17 seconds left ran a clear-out, and a
/// team down 9 with 20 seconds left in March let the clock die. College coaches foul much earlier and much further back,
/// and they send a shaky free-throw side to the one-and-one before the last two minutes are out.
/// <para>The pros and FIBA keep the old window (the NBA rows and characterization pins don't move). Rule-based: the
/// decision draws no random numbers, so a game that never reaches a late window plays exactly as before.</para>
/// </summary>
public static class LateGame
{
    /// <summary>The old window, every league: the last 40 seconds, down one to six.</summary>
    public const int ShortWindowSeconds = 40;
    public const int ShortMin = 1, ShortMax = 6;

    /// <summary>College: the last minute, down four to ten, the trailing side fouls straight away to stop the clock.</summary>
    public const int FoulWindowSeconds = 60;
    public const int FoulMin = 4, FoulMax = 10;

    /// <summary>College: the last two minutes, down seven to twelve, the trailing side fouls when its next foul gives a
    /// one-and-one (a miss on the front end is a free possession).</summary>
    public const int BonusWindowSeconds = 120;
    public const int BonusMin = 7, BonusMax = 12;

    /// <summary>College: in the last minute a side down four to ten with the ball shoots early (about half the usual trip),
    /// and looks for a three when down seven or more, or down four or more in the last 30 seconds.</summary>
    public const int HurryWindowSeconds = 60;
    public const double HurryShare = 0.45;
    public const double HurryFloorSeconds = 3;

    /// <summary>Why the side without the ball fouls on purpose.</summary>
    public enum Foul { None, StopClock, OneAndOne }

    /// <summary>
    /// Whether the side without the ball fouls on purpose at the start of this possession.
    /// </summary>
    /// <param name="college">College rules (the one-and-one; <see cref="Model.Rules.OneAndOne"/>).</param>
    /// <param name="finalPeriod">The last period of regulation, or overtime.</param>
    /// <param name="clock">Seconds left in the period.</param>
    /// <param name="lead">The lead of the side with the ball (positive: the fouling side trails).</param>
    /// <param name="oneAndOneNext">The fouling side's next common foul gives a one-and-one.</param>
    public static Foul ShouldFoul(bool college, bool finalPeriod, double clock, int lead, bool oneAndOneNext)
    {
        if (!finalPeriod || clock <= 2) return Foul.None;
        if (clock <= ShortWindowSeconds && lead is >= ShortMin and <= ShortMax) return Foul.StopClock;
        if (!college) return Foul.None;
        if (clock <= FoulWindowSeconds && lead is >= FoulMin and <= FoulMax) return Foul.StopClock;
        if (clock <= BonusWindowSeconds && lead is >= BonusMin and <= BonusMax && oneAndOneNext) return Foul.OneAndOne;
        return Foul.None;
    }

    /// <summary>College only: a side with the ball, down four to ten in the last minute, hurries. Returns the trip's
    /// length (unchanged when it doesn't apply) and whether it must look for a three.</summary>
    public static (double Seconds, bool NeedThree) Hurry(bool college, bool finalPeriod, double clock, int diff, double seconds)
    {
        if (!college || !finalPeriod || clock > HurryWindowSeconds || diff is > -FoulMin or < -FoulMax) return (seconds, false);
        double s = Math.Max(HurryFloorSeconds, seconds * HurryShare);
        bool three = diff <= -BonusMin || clock <= 30;
        return (Math.Min(s, seconds), three);
    }
}

/// <summary>An intentional foul (K28; kept on <see cref="GameResult.LateFouls"/>; not saved).</summary>
/// <param name="FoulingHome">Whether the fouling (trailing) side is the home side.</param>
/// <param name="Margin">How far the fouling side trailed when it fouled.</param>
public sealed record LateFoulCall(int Period, double Clock, bool FoulingHome, int Margin, LateGame.Foul Kind);
