using Hardwood.Core.Engine;
using Hardwood.Core.Presentation;

namespace Hardwood.Desktop.Audio;

/// <summary>The sounds of a game (M6, run 153; design §F): the crowd's reactions, the referee's whistle and the buzzer.
/// The crowd beds are not cues: they loop all game and their mix is set by <see cref="CrowdMix"/>.</summary>
public enum Cue
{
    /// <summary>A home make (cheer-1 or cheer-3, alternating).</summary>
    Cheer,
    /// <summary>A big home moment: a three, a dunk, an and-one, a block, a steal, an away foul or a late lead change (cheer-2).</summary>
    BigCheer,
    /// <summary>Applause: a substitution or a period's end.</summary>
    Applause,
    /// <summary>A fan's whistle at an away free-throw trip (now and then).</summary>
    FanWhistle,
    /// <summary>The crowd is bummed: an away basket, or the home side losing the ball. The bed dips for a moment (no file
    /// yet: Danny's notes have no crowd "aww" to use, so it's the bed's dip, not a sound made in code).</summary>
    Groan,
    /// <summary>A foul called on the home side: the crowd boos the call (run 157). Until the Splice boos land in the intake,
    /// the crowd's own bed swells under the whistle (the mixer plays a <c>crowd-boo-*.ogg</c> as soon as one is embedded).</summary>
    Boo,
    /// <summary>The referee's pea whistle: a foul or a dead-ball turnover (code-made).</summary>
    Whistle,
    /// <summary>The period buzzer (code-made).</summary>
    Buzzer,
    /// <summary>The house erupts: a home dunk, an and-one or a late lead change (run 160; the pack's eruption, else
    /// the big cheer).</summary>
    Erupt,
    /// <summary>The student section's chant and stomp after the house erupts (pack only).</summary>
    Student,
    /// <summary>"Ohh": a home three that rims out in a close game (the pack's file; nothing without it).</summary>
    Oh,
    // The court (run 160; pack only, panned to where the play is; never made in code).
    /// <summary>One dribble while the ball is carried.</summary>
    Bounce,
    /// <summary>A sneaker squeak on a cut, a drive or a closeout.</summary>
    Squeak,
    /// <summary>The net on a made jumper, three or free throw.</summary>
    Swish,
    /// <summary>A finish at the rim off the glass (a made layup).</summary>
    Finish,
    /// <summary>A miss off the rim.</summary>
    Rim,
    /// <summary>A miss off the backboard.</summary>
    Backboard,
    /// <summary>A dunk: the backboard slam.</summary>
    Slam,
    // The band (run 160; pack only).
    /// <summary>The drumline's cadence at a dead ball: before the tip and at each period's end.</summary>
    Drumline,
    /// <summary>The brass section's stab after a big home play.</summary>
    Brass,
    /// <summary>The home band's fight song (fight songs A, run 161): before the tip, at halftime and on a home win. With no
    /// band (the pros) the drumline plays instead.</summary>
    FightSong,
    /// <summary>The song's last strain: after a home run of 8-0 or more, at the next dead ball.</summary>
    FightHit,
}

/// <summary>The home side's run so far, carried from one possession to the next (the band's hit, run 161).</summary>
public sealed class BandRun
{
    /// <summary>Home points since the visitors last scored.</summary>
    public int Points { get; set; }
    /// <summary>The band has already played this run's hit.</summary>
    public bool Played { get; set; }
}

/// <summary>One sound at a time on the possession's playback clock (seconds into the possession on screen).</summary>
public sealed record SoundCue(double T, Cue Kind, double Gain = 1.0, double Pan = 0)
{
    /// <summary>The cues that still play at 4× and 8×: the biggest reactions, the buzzer and the band at a period's end.</summary>
    public bool Big => Kind is Cue.BigCheer or Cue.Erupt or Cue.Buzzer or Cue.Drumline or Cue.FightSong;
}

/// <summary>
/// The reaction schedule for one possession (pure, testable): read from the choreography's <see cref="Choreography.Moments"/>,
/// which are the engine's own events at the time they land on screen. No event, no sound.
/// The crowd belongs to the home side, as the floor does (a neutral site's crowd follows the home side too).
/// Run 157 (Danny's play-test note, Nov 22): boos on a home foul, a cheer on an away foul, a big cheer on a home steal or
/// a dunk, a groan when the home side loses the ball, and a crowd beaten into silence: down <see cref="BlowoutDeficit"/>
/// or more, only the big home plays (and the boos) get a reaction.
/// </summary>
public static class Reactions
{
    /// <summary>Down this many or more, the home crowd has given up on the small things (Danny: "goes quiet when the home
    /// team starts to really get beat hard").</summary>
    public const int BlowoutDeficit = 15;

    public static List<SoundCue> Plan(Choreography c)
    {
        var cues = new List<SoundCue>();
        var input = c.Input;
        int home = input.StartHomeScore, away = input.StartAwayScore;
        // A line change (two or more walking on from the scorer's table at once) gets a ripple of applause; a single
        // sub doesn't, or the crowd would clap sixty times a game.
        if (c.Actions.Count(a => a.Tag == "sub") >= 2) cues.Add(new SoundCue(0.4, Cue.Applause, away - home >= BlowoutDeficit ? 0.25 : 0.45));
        var moments = c.Moments;
        for (int i = 0; i < moments.Count; i++)
        {
            var (t, e) = moments[i];
            int hBefore = home, aBefore = away;
            home = e.HomeScore; away = e.AwayScore;
            bool late = e.Period >= input.Periods;
            // Beaten badly as the play happens: the crowd only answers its side's big plays.
            bool beaten = aBefore - hBefore >= BlowoutDeficit;
            switch (e.Kind)
            {
                case PlayKind.Shot when e.Made:
                {
                    bool three = e.Zone is ShotZone.CornerThree or ShotZone.AboveBreakThree;
                    if (!e.Home) { cues.Add(new SoundCue(t, Cue.Groan, beaten ? 0.35 : three ? 1.0 : 0.7)); break; }
                    bool andOne = moments.Skip(i + 1).TakeWhile(m => m.E.Kind is PlayKind.Foul or PlayKind.FreeThrow)
                        .Count(m => m.E.Kind == PlayKind.FreeThrow && m.E.Actor == e.Actor) == 1;
                    bool leadChange = late && hBefore <= aBefore && home > away;
                    // Run 160: a dunk, an and-one or a late lead change brings the house down (the eruption), and the
                    // students join in unless it's a blowout; a three is the big cheer.
                    bool dunk = Choreography.IsDunk(e);
                    if (dunk || andOne || leadChange)
                    {
                        cues.Add(new SoundCue(t, Cue.Erupt, 1.0));
                        if (!beaten) cues.Add(new SoundCue(t + 0.8, Cue.Student, 0.6));
                    }
                    else if (three) cues.Add(new SoundCue(t, Cue.BigCheer, 1.0));
                    else if (!beaten) cues.Add(new SoundCue(t, Cue.Cheer, 1.0));
                    break;
                }
                case PlayKind.Shot when e.Blocked:
                    // The blocker is on the other side from the shooter (e.Home is the shooter's).
                    if (!e.Home) cues.Add(new SoundCue(t, Cue.BigCheer, 1.0));
                    else if (!beaten) cues.Add(new SoundCue(t, Cue.Groan, 0.5));
                    break;
                case PlayKind.Shot when e.Home && Math.Abs(hBefore - aBefore) <= 6 && e.Zone is ShotZone.CornerThree or ShotZone.AboveBreakThree:
                    // Run 160: in a close game, a home three that rims out gets the crowd's "ohh" (the pack's file; nothing
                    // without it). Review: not every missed three, or it's 15–20 a game.
                    cues.Add(new SoundCue(t, Cue.Oh, 0.6));
                    break;
                case PlayKind.Steal:
                    // e.Home is the stealer's side: a home steal brings them up; an away steal is the home side losing it.
                    if (e.Home) cues.Add(new SoundCue(t, Cue.BigCheer, 1.0));
                    else cues.Add(new SoundCue(t, Cue.Groan, beaten ? 0.35 : 0.8));
                    break;
                case PlayKind.Turnover:
                    // A dead-ball turnover: the whistle, then the home crowd's groan if it was theirs.
                    cues.Add(new SoundCue(t, Cue.Whistle, 1.0));
                    if (e.Home) cues.Add(new SoundCue(t + 0.2, Cue.Groan, beaten ? 0.35 : 0.7));
                    break;
                case PlayKind.Foul:
                    // e.Home is the fouler's side. The whistle, then the crowd's answer to the call.
                    cues.Add(new SoundCue(t, Cue.Whistle, 1.0));
                    if (e.Home) cues.Add(new SoundCue(t + 0.15, Cue.Boo, beaten ? 0.6 : 1.0));
                    else if (!beaten) cues.Add(new SoundCue(t + 0.15, Cue.BigCheer, 0.8));   // Danny: "cheer loudly … the away team fouls"
                    break;
                case PlayKind.FreeThrow:
                {
                    // A fan's whistle as an away shooter steps to the line, on every third possession (now and then, never random).
                    bool firstOfTrip = !(i > 0 && moments[i - 1].E.Kind == PlayKind.FreeThrow && moments[i - 1].E.Actor == e.Actor);
                    if (!e.Home && !beaten && firstOfTrip && input.Index % 3 == 0) cues.Add(new SoundCue(Math.Max(0, t - 1.6), Cue.FanWhistle, 0.7));
                    bool lastOfTrip = !(i + 1 < moments.Count && moments[i + 1].E.Kind == PlayKind.FreeThrow && moments[i + 1].E.Actor == e.Actor);
                    if (!lastOfTrip || !e.Made) break;
                    bool leadChange = late && hBefore <= aBefore && home > away;
                    if (e.Home) { if (leadChange) cues.Add(new SoundCue(t, Cue.BigCheer, 0.6)); else if (!beaten) cues.Add(new SoundCue(t, Cue.Cheer, 0.6)); }
                    else if (!beaten) cues.Add(new SoundCue(t, Cue.Groan, 0.4));
                    break;
                }
                case PlayKind.PeriodEnd:
                    cues.Add(new SoundCue(t, Cue.Buzzer, 1.0));
                    cues.Add(new SoundCue(t + 0.3, Cue.Applause, beaten ? 0.4 : 0.9));
                    break;
            }
        }
        return cues.OrderBy(q => q.T).ToList();
    }

    /// <summary>The court's sounds and the band for one possession (run 160; Danny's note: "zero basketball court noises …
    /// where is the big college band"). Pure: read from the choreography's ball, actions and moments, panned to where the
    /// play is. Nothing here touches the engine or its random stream. The players' shouts wait for a file (none in the
    /// pack), and the fight songs come in their own runs.</summary>
    /// <param name="band">K44 (run 216): only a college game has a band. The pros and national teams get the court's
    /// sounds with no fight song, drumline or brass (a pro PA or a FIBA horn waits for files).</param>
    public static List<SoundCue> Court(Choreography c, bool band = true)
    {
        var cues = new List<SoundCue>();
        var input = c.Input;
        static double Pan(double x) => Math.Clamp((x - 47) / 47.0, -1, 1) * 0.8;
        // Before the tip: the home band's fight song (run 161; the drumline when there's no band).
        if (band && input.Index == 0) cues.Add(new SoundCue(0.1, Cue.FightSong, 0.8));
        // Dribbles while the ball is carried: one every 0.42 s, at the carrier (at most 12 a possession).
        int bounces = 0;
        foreach (var b in c.Ball.Where(b => b.Mode == BallMode.Held && b.Holder >= 0 && !b.FreeThrow && b.T1 - b.T0 >= 0.5))
            for (double t = b.T0 + 0.25; t < b.T1 - 0.1 && bounces < 12; t += 0.42, bounces++)
                cues.Add(new SoundCue(t, Cue.Bounce, 0.8, Pan(c.PositionOf(b.Holder, t).X)));
        // Squeaks on cuts, drives and closeouts (at most 4).
        foreach (var a in c.Actions.Where(a => a.Tag is "cut" or "drive" or "attack-paint" or "hedge" or "switch" && a.Slot >= 0).OrderBy(a => a.T).Take(4))
            cues.Add(new SoundCue(a.T + 0.1, Cue.Squeak, 0.7, Pan(c.PositionOf(a.Slot, a.T + 0.1).X)));
        // The rim, the glass and the net where the shot lands.
        double RimX(double t) => c.Ball.LastOrDefault(b => b.Mode == BallMode.Shot && Math.Abs(b.T1 - t) < 0.05)?.Dest.X ?? 47;
        int home = input.StartHomeScore, away = input.StartAwayScore;
        for (int i = 0; i < c.Moments.Count; i++)
        {
            var (t, e) = c.Moments[i];
            int hBefore = home, aBefore = away;
            home = e.HomeScore; away = e.AwayScore;
            switch (e.Kind)
            {
                case PlayKind.Shot when !e.Blocked:
                {
                    double pan = Pan(RimX(t));
                    if (e.Made)
                    {
                        bool dunk = Choreography.IsDunk(e);
                        cues.Add(new SoundCue(t, dunk ? Cue.Slam : e.Zone == ShotZone.Rim ? Cue.Finish : Cue.Swish, 1.0, pan));
                        bool leadChange = e.Period >= input.Periods && hBefore <= aBefore && home > away;
                        // The brass after a home dunk or a late lead change (not in a blowout).
                        if (band && e.Home && (dunk || leadChange) && aBefore - hBefore < BlowoutDeficit) cues.Add(new SoundCue(t + 0.5, Cue.Brass, 0.7));
                    }
                    else cues.Add(new SoundCue(t, (input.Index + i) % 3 == 0 && e.Zone != ShotZone.Rim ? Cue.Backboard : Cue.Rim, 0.9, pan));
                    break;
                }
                case PlayKind.FreeThrow:
                    cues.Add(new SoundCue(t, e.Made ? Cue.Swish : Cue.Rim, 0.7, Pan(RimX(t))));
                    break;
                case PlayKind.PeriodEnd when band:
                {
                    // The band strikes up at the break (after the buzzer and the applause): the fight song at halftime and
                    // when the home side has won (run 161), else the drumline.
                    bool half = input.Periods >= 2 && e.Period == input.Periods / 2;
                    bool over = e.Period >= input.Periods && e.HomeScore != e.AwayScore;
                    bool song = half || over && e.HomeScore > e.AwayScore;
                    if (!over || song) cues.Add(new SoundCue(t + (over ? 1.5 : 1.2), song ? Cue.FightSong : Cue.Drumline, 0.8));
                    break;
                }
            }
        }
        return cues.OrderBy(q => q.T).ToList();
    }

    /// <summary>
    /// The band's hit (run 161, fight songs plan): after a home run of 8-0 or more, the song's last strain at the next dead
    /// ball (a foul, a dead-ball turnover or a period's end), once a run. <paramref name="run"/> carries the run from one
    /// possession to the next. Not in a blowout loss (the home side still down 15 or more).
    /// </summary>
    public static List<SoundCue> Band(Choreography c, BandRun run)
    {
        var cues = new List<SoundCue>();
        int home = c.Input.StartHomeScore, away = c.Input.StartAwayScore;
        foreach (var (t, e) in c.Moments)
        {
            if (e.AwayScore > away) { run.Points = 0; run.Played = false; }
            else if (e.HomeScore > home) run.Points += e.HomeScore - home;
            home = e.HomeScore; away = e.AwayScore;
            bool dead = e.Kind is PlayKind.Foul or PlayKind.Turnover or PlayKind.PeriodEnd;
            bool over = e.Kind == PlayKind.PeriodEnd && e.Period >= c.Input.Periods && home != away;
            if (dead && !over && run.Points >= 8 && !run.Played && away - home < BlowoutDeficit)
            {
                cues.Add(new SoundCue(t + 0.6, Cue.FightHit, 0.8));
                run.Played = true;
            }
        }
        return cues;
    }

    /// <summary>The speed rules (design §F): at 1× and 2× everything plays; at 4× and 8× only the biggest reactions and the
    /// buzzer; Key moments plays everything.</summary>
    public static bool Plays(SoundCue cue, double rate, bool keyMoments) => keyMoments || rate <= 2 || cue.Big;
}
