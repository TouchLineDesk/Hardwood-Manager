using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>What happened on one play of a logged game (the 2D tracker, D67 B, run 80).</summary>
public enum PlayKind
{
    /// <summary>A field-goal attempt. <see cref="PlayEvent.Other"/> is the assister of a make, or the blocker of a blocked miss.</summary>
    Shot,
    /// <summary>One free throw.</summary>
    FreeThrow,
    /// <summary>A turnover with no steal.</summary>
    Turnover,
    /// <summary>A steal: the actor is the defender, <see cref="PlayEvent.Other"/> the player who lost the ball.</summary>
    Steal,
    OffensiveRebound,
    DefensiveRebound,
    /// <summary>A foul: the actor is the fouler, <see cref="PlayEvent.Other"/> the player fouled (null on a loose-ball foul).</summary>
    Foul,
    /// <summary>The end of a period (no actor).</summary>
    PeriodEnd,
}

/// <summary>
/// One play of a game that keeps its log, as data rather than commentary, so a viewer can draw it (the 2D tracker,
/// D67 B, run 80). It is recorded beside the play-by-play text from the same moments the engine already resolves; it
/// reads nothing from the random stream, so recording it can never change a result (#595: no second simulator).
/// <para><see cref="Home"/> says whether the actor plays for the home side. The scores are after the play.</para>
/// </summary>
public sealed record PlayEvent(int Period, double Clock, PlayKind Kind, bool Home, Player? Actor, Player? Other,
                               ShotZone Zone, bool Made, int HomeScore, int AwayScore)
{
    /// <summary>The side with the ball when the play happened: the actor's side for a shot, free throw, turnover or
    /// offensive rebound; the other side for a steal, defensive rebound or foul (the actor defended).</summary>
    public bool HomeOffense => Kind is PlayKind.Steal or PlayKind.DefensiveRebound or PlayKind.Foul ? !Home : Home;

    /// <summary>A missed shot that was blocked.</summary>
    public bool Blocked => Kind == PlayKind.Shot && !Made && Other != null;
}
