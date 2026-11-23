using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>
/// Timeouts (#1064, run 168; Danny, Nov 22: "yes, add them"): how many each side gets under a rule set, when they come
/// and go, and how long one lasts. Worked out from the rules' shape (two halves = college, 10-minute quarters = FIBA,
/// otherwise the NBA), so a league restored from a save, which keeps only the basic columns, still gets the right ones.
/// </summary>
/// <param name="AtTip">Timeouts each side has at the start of the game.</param>
/// <param name="CarryToSecondHalf">The most a side keeps into the second half (FIBA: none; college: 3).</param>
/// <param name="SecondHalfAdd">Timeouts added at the start of the second half (FIBA: 3).</param>
/// <param name="FinalPeriodCap">The most a side may have in the last period of regulation (the NBA's 4 in the 4th).</param>
/// <param name="PerOvertime">Timeouts given for each overtime.</param>
/// <param name="CarryIntoOvertime">Whether unused ones carry into overtime (college) or the overtime allowance replaces them.</param>
/// <param name="Seconds">How long a timeout is, for the rest it gives the players on the floor.</param>
/// <param name="Reserve">How many the bench holds back for the late game (the last 4 minutes of regulation, and overtime).</param>
public sealed record TimeoutRules(int AtTip, int CarryToSecondHalf, int SecondHalfAdd, int FinalPeriodCap, int PerOvertime,
                                  bool CarryIntoOvertime, double Seconds, int Reserve)
{
    /// <summary>The NBA: 7 a game, at most 4 in the fourth quarter, 2 for each overtime.</summary>
    public static readonly TimeoutRules Nba = new(7, 7, 0, 4, 2, false, 75, 2);
    /// <summary>NCAA men: 4 a game, at most 3 carried into the second half, 1 more for each overtime (unused ones carry).</summary>
    public static readonly TimeoutRules College = new(4, 3, 0, 99, 1, true, 60, 1);
    /// <summary>FIBA: 2 in the first half, 3 in the second (unused first-half ones are lost), 1 for each overtime.</summary>
    public static readonly TimeoutRules Fiba = new(2, 0, 3, 99, 1, false, 60, 1);

    public static TimeoutRules For(Rules rules) =>
        rules.Periods == 2 ? College : rules.MinutesPerPeriod == 10 ? Fiba : Nba;

    /// <summary>A side's timeouts as a new period opens, from what it had left.</summary>
    public int AtPeriodStart(int period, int periods, int left)
    {
        if (period == 1) return AtTip;
        if (period > periods) return CarryIntoOvertime ? left + PerOvertime : PerOvertime;
        if (period == periods / 2 + 1) left = Math.Min(left, CarryToSecondHalf) + SecondHalfAdd;
        if (period == periods) left = Math.Min(left, FinalPeriodCap);
        return left;
    }

    /// <summary>The unanswered points that make a bench stop the game (the classic "8-0 run" timeout).</summary>
    public const int RunToStop = 8;
    /// <summary>The after-timeout set: the shot logit the calling side's next possession gets (a drawn-up play, a little better
    /// than an ordinary half-court trip). Small on purpose: the timeout's main effect is the rest and stopping the run.</summary>
    public const double AfterTimeoutLogit = 0.10;
    /// <summary>A breather is called when the five on the floor average less than the sub-out line plus this.</summary>
    public const double BreatherMargin = 0.06;
}

/// <summary>Why a bench called a timeout.</summary>
public enum TimeoutReason
{
    /// <summary>The other side went on a run (8 or more unanswered points).</summary>
    StopRun,
    /// <summary>The last possession of a close game: set up the final play.</summary>
    LastPlay,
    /// <summary>The last two minutes, behind by one to six with the ball: draw up a play.</summary>
    LatePlay,
    /// <summary>The five on the floor are worn down: a breather.</summary>
    Breather,
    /// <summary>The coach watching live pressed the button.</summary>
    Coach,
}

/// <summary>A timeout called in a game (kept on <see cref="GameResult.Timeouts"/>; not saved).</summary>
/// <param name="Run">For <see cref="TimeoutReason.StopRun"/>, the run's points (the other side's unanswered points).</param>
public sealed record TimeoutCall(int Period, double Clock, bool Home, TimeoutReason Reason, int Run, int Left, int HomeScore, int AwayScore)
{
    /// <summary>The words after the team's name: "stopping a 9-0 run", "to set up the last play", "called from the bench".</summary>
    public string Why => Reason switch
    {
        TimeoutReason.StopRun => $"to stop a {Run}-0 run",
        TimeoutReason.LastPlay => "to set up the last play",
        TimeoutReason.LatePlay => "to draw up a play",
        TimeoutReason.Breather => "to give his five a breather",
        _ => "called by the coach",
    };

    public string LeftText => Left == 0 ? "none left" : Left == 1 ? "1 left" : $"{Left} left";
}
