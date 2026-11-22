using Hardwood.Core.Engine;
using Hardwood.Core.Presentation;

namespace Hardwood.Desktop.Audio;

/// <summary>The sounds of a game (M6, run 153; design §F): the crowd's reactions, the referee's whistle and the buzzer.
/// The crowd beds are not cues: they loop all game and their mix is set by <see cref="CrowdMix"/>.</summary>
public enum Cue
{
    /// <summary>A home make (cheer-1 or cheer-3, alternating).</summary>
    Cheer,
    /// <summary>A big home moment: a three, an and-one, a block or a late lead change (cheer-2).</summary>
    BigCheer,
    /// <summary>Applause: a substitution or a period's end.</summary>
    Applause,
    /// <summary>A fan's whistle at an away free-throw trip (now and then).</summary>
    FanWhistle,
    /// <summary>An away basket or steal: the bed dips for a moment (no file).</summary>
    Groan,
    /// <summary>The referee's pea whistle: a foul or a dead-ball turnover (code-made).</summary>
    Whistle,
    /// <summary>The period buzzer (code-made).</summary>
    Buzzer,
}

/// <summary>One sound at a time on the possession's playback clock (seconds into the possession on screen).</summary>
public sealed record SoundCue(double T, Cue Kind, double Gain = 1.0)
{
    /// <summary>The cues that still play at 4× and 8×: the biggest reactions and the buzzer.</summary>
    public bool Big => Kind is Cue.BigCheer or Cue.Buzzer;
}

/// <summary>
/// The reaction schedule for one possession (pure, testable): read from the choreography's <see cref="Choreography.Moments"/>,
/// which are the engine's own events at the time they land on screen. No event, no sound.
/// The crowd belongs to the home side, as the floor does (a neutral site's crowd follows the home side too).
/// </summary>
public static class Reactions
{
    public static List<SoundCue> Plan(Choreography c)
    {
        var cues = new List<SoundCue>();
        var input = c.Input;
        int home = input.StartHomeScore, away = input.StartAwayScore;
        // A line change (two or more walking on from the scorer's table at once) gets a ripple of applause; a single
        // sub doesn't, or the crowd would clap sixty times a game.
        if (c.Actions.Count(a => a.Tag == "sub") >= 2) cues.Add(new SoundCue(0.4, Cue.Applause, 0.45));
        var moments = c.Moments;
        for (int i = 0; i < moments.Count; i++)
        {
            var (t, e) = moments[i];
            int hBefore = home, aBefore = away;
            home = e.HomeScore; away = e.AwayScore;
            bool late = e.Period >= input.Periods;
            switch (e.Kind)
            {
                case PlayKind.Shot when e.Made:
                {
                    bool three = e.Zone is ShotZone.CornerThree or ShotZone.AboveBreakThree;
                    if (!e.Home) { cues.Add(new SoundCue(t, Cue.Groan, three ? 1.0 : 0.7)); break; }
                    bool andOne = moments.Skip(i + 1).TakeWhile(m => m.E.Kind is PlayKind.Foul or PlayKind.FreeThrow)
                        .Count(m => m.E.Kind == PlayKind.FreeThrow && m.E.Actor == e.Actor) == 1;
                    bool leadChange = late && hBefore <= aBefore && home > away;
                    cues.Add(new SoundCue(t, three || andOne || leadChange ? Cue.BigCheer : Cue.Cheer, 1.0));
                    break;
                }
                case PlayKind.Shot when e.Blocked:
                    // The blocker is on the other side from the shooter (e.Home is the shooter's).
                    cues.Add(e.Home ? new SoundCue(t, Cue.Groan, 0.5) : new SoundCue(t, Cue.BigCheer, 1.0));
                    break;
                case PlayKind.Steal:
                    // e.Home is the stealer's side.
                    cues.Add(e.Home ? new SoundCue(t, Cue.Cheer, 0.8) : new SoundCue(t, Cue.Groan, 0.5));
                    break;
                case PlayKind.Turnover:
                case PlayKind.Foul:
                    cues.Add(new SoundCue(t, Cue.Whistle, 1.0));
                    break;
                case PlayKind.FreeThrow:
                {
                    // A fan's whistle as an away shooter steps to the line, on every third possession (now and then, never random).
                    bool firstOfTrip = !(i > 0 && moments[i - 1].E.Kind == PlayKind.FreeThrow && moments[i - 1].E.Actor == e.Actor);
                    if (!e.Home && firstOfTrip && input.Index % 3 == 0) cues.Add(new SoundCue(Math.Max(0, t - 1.6), Cue.FanWhistle, 0.7));
                    bool lastOfTrip = !(i + 1 < moments.Count && moments[i + 1].E.Kind == PlayKind.FreeThrow && moments[i + 1].E.Actor == e.Actor);
                    if (!lastOfTrip || !e.Made) break;
                    if (e.Home) cues.Add(new SoundCue(t, late && hBefore <= aBefore && home > away ? Cue.BigCheer : Cue.Cheer, 0.6));
                    else cues.Add(new SoundCue(t, Cue.Groan, 0.4));
                    break;
                }
                case PlayKind.PeriodEnd:
                    cues.Add(new SoundCue(t, Cue.Buzzer, 1.0));
                    cues.Add(new SoundCue(t + 0.3, Cue.Applause, 0.9));
                    break;
            }
        }
        return cues.OrderBy(q => q.T).ToList();
    }

    /// <summary>The speed rules (design §F): at 1× and 2× everything plays; at 4× and 8× only the biggest reactions and the
    /// buzzer; Key moments plays everything.</summary>
    public static bool Plays(SoundCue cue, double rate, bool keyMoments) => keyMoments || rate <= 2 || cue.Big;
}
