using Hardwood.Core.Engine;

namespace Hardwood.Core.Presentation;

/// <summary>What the home crowd does at a moment, as the commentary hears it (K25's leftover, run 237).</summary>
public enum CrowdRead
{
    /// <summary>The house comes down: a home dunk, an and-one or a late lead change (the sound's eruption).</summary>
    Erupt,
    /// <summary>A home foul in a tight finish, or now and then: the crowd boos the call (the sound's boo).</summary>
    Boo,
    /// <summary>A home three rims out in a close game (the sound's "ohh").</summary>
    Oh,
    /// <summary>The visitors take the lead late, or hit a three in a close finish: the building goes quiet (the sound's
    /// loudest groan).</summary>
    Silenced,
    /// <summary>The home side has just fallen 15 behind: the crowd gives up on the small things (the sound's blowout rule).</summary>
    Hushed,
}

/// <summary>
/// K25's leftover (critic reviews #22–#23; Danny's `danny-broadcast-nov27` (c)): the commentary hears the crowd. The
/// reads come from the same moments and the same rules as the crowd's sounds (<c>Hardwood.Desktop.Audio.Reactions</c>,
/// which uses <see cref="BlowoutDeficit"/>, <see cref="IsAndOne"/> and <see cref="LateLeadChange"/> from here), so the
/// words never say the building erupted when the sound groaned. Presentation only: no engine, RNG or save change, and
/// the wording is keyed on the possession's number, never the choreographer's random stream.
/// <para>At most one crowd line a possession. It is its own line a second after the play when the bar has room;
/// otherwise a short clause on the play's own line. Nothing is said after a period's horn.</para>
/// </summary>
public static class CrowdCalls
{
    /// <summary>Down this many or more, the home crowd only answers the big plays (Danny's run 157 note).</summary>
    public const int BlowoutDeficit = 15;
    /// <summary>A crowd line of its own waits this long after the play (the play's line stays up first).</summary>
    public const double Delay = 1.0;
    /// <summary>The room a crowd line of its own needs before the next line.</summary>
    public const double Room = 1.6;

    /// <summary>A made shot at <paramref name="i"/> that has exactly one free throw by the shooter after it (an and-one).</summary>
    public static bool IsAndOne(IReadOnlyList<(double T, PlayEvent E)> moments, int i)
    {
        var e = moments[i].E;
        return moments.Skip(i + 1).TakeWhile(m => m.E.Kind is PlayKind.Foul or PlayKind.FreeThrow)
            .Count(m => m.E.Kind == PlayKind.FreeThrow && m.E.Actor == e.Actor) == 1;
    }

    /// <summary>In the last regulation period (or overtime), the home side goes from level or behind to in front.</summary>
    public static bool LateLeadChange(PlayEvent e, int periods, int homeBefore, int awayBefore) =>
        e.Period >= periods && homeBefore <= awayBefore && e.HomeScore > e.AwayScore;

    /// <summary>The crowd's reads for one possession, in time order, from the choreography's moments.</summary>
    public static List<(double T, CrowdRead Read)> Reads(Choreography c)
    {
        var reads = new List<(double, CrowdRead)>();
        var input = c.Input;
        int home = input.StartHomeScore, away = input.StartAwayScore;
        var moments = c.Moments;
        int deepest = Math.Max(input.HomeDeepestDeficit, input.StartAwayScore - input.StartHomeScore);
        for (int i = 0; i < moments.Count; i++)
        {
            var (t, e) = moments[i];
            int hBefore = home, aBefore = away;
            home = e.HomeScore; away = e.AwayScore;
            bool late = e.Period >= input.Periods;
            bool three = e.Zone is ShotZone.CornerThree or ShotZone.AboveBreakThree;
            switch (e.Kind)
            {
                case PlayKind.Shot when e.Made && e.Home:
                    // Beaten badly, the words only now and then (a rare cheer isn't rare four times a quarter).
                    if ((Choreography.IsDunk(e) || IsAndOne(moments, i) || LateLeadChange(e, input.Periods, hBefore, aBefore))
                        && (aBefore - hBefore < BlowoutDeficit || input.Index % 3 == 0)) reads.Add((t, CrowdRead.Erupt));
                    break;
                case PlayKind.Shot when e.Made:
                    // The visitors: a late go-ahead basket, or a three in a one-possession finish.
                    if (late && (aBefore <= hBefore && away > home || three && Math.Abs(hBefore - aBefore) <= 3)) reads.Add((t, CrowdRead.Silenced));
                    else if (deepest < BlowoutDeficit && away - home >= BlowoutDeficit) reads.Add((t, CrowdRead.Hushed));
                    deepest = Math.Max(deepest, away - home);
                    break;
                case PlayKind.Shot when e.Home && !e.Blocked && three && Math.Abs(hBefore - aBefore) <= 6:
                    // The sound's "ohh" plays at every close miss from deep; the words only in a one-possession game, late
                    // (or now and then earlier), so it isn't said a dozen times a game.
                    if (late && Math.Abs(hBefore - aBefore) <= 3 || input.Index % 5 == 0) reads.Add((t, CrowdRead.Oh));
                    break;
                case PlayKind.Foul when e.Home:
                    // The sound boos every home foul; the words only now and then, and always in a tight finish.
                    if (late && Math.Abs(hBefore - aBefore) <= 8 || input.Index % 4 == 0) reads.Add((t, CrowdRead.Boo));
                    break;
            }
        }
        return reads;
    }

    /// <summary>The order a possession's reads are said in (one a possession).</summary>
    private static int Rank(CrowdRead r) => r switch { CrowdRead.Erupt => 0, CrowdRead.Silenced => 1, CrowdRead.Hushed => 2, CrowdRead.Oh => 3, _ => 4 };

    /// <summary>The crowd's line, as its own line on the bar.</summary>
    /// <param name="beaten">The home side was already down <see cref="BlowoutDeficit"/> or more.</param>
    public static string Line(CrowdRead read, int index, string homeName, bool beaten, bool late) => read switch
    {
        CrowdRead.Erupt when beaten => PossessionChoreographer.Vary(index, "A rare cheer from the home crowd tonight", "Something for the home fans to cheer, at last"),
        CrowdRead.Erupt => PossessionChoreographer.Vary(index, "The building erupts!", "Listen to this place!", $"The {homeName} crowd is on its feet!", "The roof just about comes off!"),
        CrowdRead.Boo => PossessionChoreographer.Vary(index, "The crowd lets the officials hear it", "Boos rain down on that call", $"The {homeName} fans don't like that one"),
        CrowdRead.Oh => PossessionChoreographer.Vary(index, "Ohh! The crowd thought that one was down", "So close: the building groans"),
        CrowdRead.Silenced => PossessionChoreographer.Vary(index, "That quiets the building", "You could hear a pin drop in here", "That one silences the crowd"),
        _ => late ? PossessionChoreographer.Vary(index, "The home fans start heading for the exits", "The crowd has seen enough")
                  : PossessionChoreographer.Vary(index, "The crowd has gone quiet", "Not much for the home fans to shout about"),
    };

    /// <summary>The crowd's clause, added to the play's own line when the bar has no room for a line of its own.</summary>
    public static string Clause(CrowdRead read, int index, bool beaten) => read switch
    {
        CrowdRead.Erupt when beaten => PossessionChoreographer.Vary(index, "A rare cheer from the home crowd", "Something for the home fans"),
        CrowdRead.Erupt => PossessionChoreographer.Vary(index, "The building erupts!", "Listen to this place!", "The crowd is on its feet!"),
        CrowdRead.Boo => PossessionChoreographer.Vary(index, "Boos from the stands", "The crowd doesn't like it"),
        CrowdRead.Oh => PossessionChoreographer.Vary(index, "The crowd groans", "Ohh from the crowd"),
        CrowdRead.Silenced => PossessionChoreographer.Vary(index, "The building goes quiet", "That silences the crowd"),
        _ => "The crowd has gone quiet",
    };

    /// <summary>Adds the possession's crowd line (if any) to the choreography's commentary. Called by
    /// <see cref="PossessionChoreographer.Build"/> once the moments are placed, before the lines are sorted.</summary>
    public static void AddLines(Choreography c)
    {
        var reads = Reads(c);
        if (reads.Count == 0) return;
        var input = c.Input;
        // Nothing after a period's horn, and no crowd line between the period's last play and the horn.
        double horn = c.Moments.Where(m => m.E.Kind == PlayKind.PeriodEnd).Select(m => m.T).DefaultIfEmpty(double.MaxValue).Min();
        var (t, read) = reads.Where(r => r.T < horn).OrderBy(r => Rank(r.Read)).ThenBy(r => r.T).FirstOrDefault((-1, CrowdRead.Erupt));
        if (t < 0) return;
        // The score before the play, for the blowout rule.
        int home = input.StartHomeScore, away = input.StartAwayScore;
        foreach (var (mt, e) in c.Moments) { if (mt >= t) break; home = e.HomeScore; away = e.AwayScore; }
        bool beaten = away - home >= BlowoutDeficit;
        bool late = c.Moments.FirstOrDefault(m => m.T >= t).E?.Period >= input.Periods;
        double at = t + Delay;
        bool room = horn == double.MaxValue && at + 0.5 <= c.Length && !c.Lines.Any(l => l.T > t + 1e-6 && l.T < at + Room);
        if (room)
        {
            c.Lines.Add(new CommentaryLine(at, Line(read, input.Index, input.HomeName, beaten, late), read == CrowdRead.Erupt && !beaten));
            return;
        }
        int k = c.Lines.FindLastIndex(l => Math.Abs(l.T - t) < 1e-6);
        if (k < 0 || c.Lines[k].Text.EndsWith("...")) return;
        var line = c.Lines[k];
        string text = line.Text.TrimEnd();
        string sep = text.EndsWith('!') || text.EndsWith('.') ? " " : ". ";
        c.Lines[k] = line with { Text = text + sep + Clause(read, input.Index, beaten) };
    }
}
