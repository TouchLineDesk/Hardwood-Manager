using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.People;

namespace Hardwood.Core.Season;

/// <summary>What a debate or hot take is graded on at the final (run 235).</summary>
public static class TakeCheck
{
    /// <summary>Team <c>TeamId</c> wins.</summary>
    public const string Wins = "wins";
    /// <summary>Team <c>TeamId</c> wins by <c>Line</c> or more.</summary>
    public const string WinsBy = "wins-by";
    /// <summary>Player <c>PlayerId</c> scores <c>Line</c> or more.</summary>
    public const string Scores = "scores";
}

/// <summary>One side of a debate: who says it and his take in one line.</summary>
public sealed record DebateSide(StudioAnalyst Analyst, string Take, bool Yes);

/// <summary>
/// The debate card (run 235, `danny-broadcast-nov27` (b); `claude/studio-show-design.md`'s "versus" card): a question, two
/// panelists on opposite sides, graded at the final ("Reggie was right"). <see cref="Yes"/> argues the claim
/// (<see cref="Check"/>, <see cref="TeamId"/>/<see cref="PlayerId"/>, <see cref="Line"/>), <see cref="No"/> against it.
/// </summary>
public sealed record StudioDebate(string Question, DebateSide Yes, DebateSide No, string Check, int TeamId, int PlayerId, int Line);

/// <summary>The hot take card (run 235): one panelist, one bold, checkable line, and his season's take tracker.</summary>
public sealed record StudioHotTake(StudioAnalyst Analyst, string Take, string Check, int TeamId, int PlayerId, int Line, int Landed, int Missed)
{
    public string Tracker => Landed + Missed == 0 ? "First hot take of the season" : $"Take tracker: {Landed} of {Landed + Missed} landed this season";
}

/// <summary>
/// Run 235: the studio's debates and hot takes. Built from the panel's own tracked picks, so nobody argues against his own
/// pick (the lesson of run 234's review). Pure: no random draw (the speaker comes from a hash of the game's key), and the
/// verdict is read from the box score.
/// </summary>
public static class StudioDebates
{
    public const int BlowoutLine = 15, StarNightLine = 30;

    /// <summary>A panelist's first name, as the verdict chip says it ("Marcus 'Showtime' Lyle" → "Marcus").</summary>
    public static string FirstName(string name) => name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? name;

    /// <summary>The night's debate. If the panel's picks split, it's "who wins?" with the guest (or Vern) on one side when
    /// he's on either; if they all agree, it's whether the best player on the floor beats his scoring average (hype says yes,
    /// the others no); without a star yet, whether the favourite wins by double digits. Null when there is nothing to argue.</summary>
    public static StudioDebate? Build(StudioShow show, Team fav, (Player p, double ppg)? star)
    {
        var picks = show.Picks;
        if (picks.Count < 2) return null;
        // 1. A split desk: who wins?
        var onHome = picks.Where(p => p.Pick == show.Home).ToList();
        var onAway = picks.Where(p => p.Pick == show.Away).ToList();
        if (onHome.Count > 0 && onAway.Count > 0)
        {
            // The minority side's best voice (the guest first) against the majority's most confident pick.
            var (few, many) = onHome.Count <= onAway.Count ? (onHome, onAway) : (onAway, onHome);
            var a = few.OrderByDescending(p => p.Analyst.IsGuest || p.Analyst == StudioShow.Coach).ThenByDescending(p => p.Margin).First();
            var b = many.OrderByDescending(p => p.Analyst.IsGuest || p.Analyst == StudioShow.Coach).ThenByDescending(p => p.Margin).First();
            // "Yes" is the favourite winning, so the chip reads the same way every night.
            var (yes, no) = a.Pick == fav ? (a, b) : (b, a);
            var dog = fav == show.Home ? show.Away : show.Home;
            return new StudioDebate($"{Ask(fav)} win tonight?",
                new DebateSide(yes.Analyst, $"Yes. {Short(yes.Why)} {fav.Abbreviation} by {yes.Margin}.", true),
                new DebateSide(no.Analyst, $"No. {Short(no.Why)} {dog.Abbreviation} by {no.Margin}.", false),
                TakeCheck.Wins, fav.Id, 0, 0);
        }
        // 2. One side for everyone: does the star beat his average?
        var hype = picks.Select(p => p.Analyst).FirstOrDefault(a => a.Voice == GuestPanel.Hype) ?? StudioShow.Guard;
        if (star is var (sp, ppg) && ppg >= 8)
        {
            int line = (int)Math.Floor(ppg) + 1;
            var doubter = picks.Select(p => p.Analyst).Where(a => a != hype && (a.IsGuest || a == StudioShow.Coach)).FirstOrDefault()
                          ?? (picks.Any(p => p.Analyst == StudioShow.BigMan) ? StudioShow.BigMan : picks.Select(p => p.Analyst).First(a => a != hype));
            if (doubter == hype) return null;
            return new StudioDebate($"Does {sp.Name} beat his {ppg:F1} a night?",
                new DebateSide(hype, $"Yes. {line} or more. Stars show up on nights like this.", true),
                new DebateSide(doubter, doubter.Voice == GuestPanel.NumbersVoice || doubter == StudioShow.Numbers
                    ? "No. An average is an average. Bet the under."
                    : "No. They'll load up on him and make somebody else beat them.", false),
                TakeCheck.Scores, 0, sp.Id, line);
        }
        // 3. No star numbers yet (opening night): does the favourite win by double digits?
        var big = picks.Where(p => p.Pick == fav).OrderByDescending(p => p.Margin).FirstOrDefault();
        if (big == null) return null;   // review (PR #304): a rebuilt show whose locked picks are all on the new underdog
        var small = picks.Where(p => p.Pick == fav && p != big).OrderBy(p => p.Margin).FirstOrDefault();
        if (small == null || big.Margin < 10 || small.Margin >= 10) return null;
        return new StudioDebate($"{Ask(fav)} win by 10 or more?",
            new DebateSide(big.Analyst, $"Yes. I've got them by {big.Margin}. This one gets away early.", true),
            new DebateSide(small.Analyst, $"No. {small.Margin} at most. Nobody blows anybody out on night one.", false),
            TakeCheck.WinsBy, fav.Id, 0, 10);
    }

    /// <summary>The night's hot take: the boldest call the desk's own picks allow, and never the debate's claim again.
    /// Whoever picked the favourite by 9+ calls a 15-point win; else the hype voice calls a 30-point night for the star
    /// (unless the debate is already about him); else someone who picked the underdog says they win by 10. The speaker is chosen by a hash of the game, never a random draw.</summary>
    public static StudioHotTake? HotTake(StudioShow show, StudioDesk desk, int season, Team fav, (Player p, double ppg)? star)
    {
        var picks = show.Picks;
        if (picks.Count == 0) return null;
        var dog = fav == show.Home ? show.Away : show.Home;
        ulong h = MediaCast.Hash($"take:{show.Key}");
        StudioPickView? Choose(IEnumerable<StudioPickView> from)
        {
            var list = from.OrderBy(p => p.Analyst.Key, StringComparer.Ordinal).ToList();
            return list.Count == 0 ? null : list[(int)(h % (ulong)list.Count)];
        }
        StudioHotTake Make(StudioAnalyst a, string take, string check, int team, int player, int line)
        {
            var (w, l) = desk.TakeRecord(a.Key, season);
            return new(a, take, check, team, player, line, w, l);
        }
        // Not the debate again: the take is a different, bolder claim, said by someone outside the debate where possible.
        var debaters = new HashSet<string>(show.Debate is { } d ? new[] { d.Yes.Analyst.Key, d.No.Analyst.Key } : Array.Empty<string>());
        StudioPickView? Prefer(IEnumerable<StudioPickView> from) => Choose(from.Where(p => !debaters.Contains(p.Analyst.Key))) ?? Choose(from);
        if (Prefer(picks.Where(p => p.Pick == fav && p.Margin >= 9)) is { } rout)
            return Make(rout.Analyst, $"{fav.Name} win this by {BlowoutLine} or more. It's over by the fourth.", TakeCheck.WinsBy, fav.Id, 0, BlowoutLine);
        if (star is var (sp, ppg) && ppg >= 18 && show.Debate?.Check != TakeCheck.Scores)
        {
            var hype = picks.Select(p => p.Analyst).FirstOrDefault(a => a.Voice == GuestPanel.Hype) ?? picks.Select(p => p.Analyst).FirstOrDefault(a => a == StudioShow.Guard);
            if (hype != null)
                return Make(hype, $"{sp.Name} drops {StarNightLine} tonight. Write it down.", TakeCheck.Scores, 0, sp.Id, StarNightLine);
        }
        if (Prefer(picks.Where(p => p.Pick == dog)) is { } upset)
            return Make(upset.Analyst, $"{dog.Name} don't just win this. They win it by 10.", TakeCheck.WinsBy, dog.Id, 0, 10);
        return null;
    }

    /// <summary>Is the claim true in this final?</summary>
    public static bool Holds(string check, int teamId, int playerId, int line, GameResult r)
    {
        int Margin(int id) => r.Home.Team.Id == id ? r.Home.Points - r.Away.Points : r.Away.Team.Id == id ? r.Away.Points - r.Home.Points : int.MinValue;
        return check switch
        {
            TakeCheck.Wins => Margin(teamId) > 0,
            TakeCheck.WinsBy => Margin(teamId) >= line,
            TakeCheck.Scores => r.Home.Lines.Concat(r.Away.Lines).Where(l => l.Player.Id == playerId).Sum(l => l.Points) >= line,
            _ => false,
        };
    }

    /// <summary>The verdict chip: "Reggie was right" (the side that called it), or for a hot take "Marcus called it" / "Marcus
    /// missed".</summary>
    public static string Verdict(StudioTakeRecord t, bool holds) => t.Kind == StudioTakeRecord.Debate
        ? $"{FirstName(holds ? t.YesName : t.NoName)} was right"
        : holds ? $"{FirstName(t.YesName)} called it" : $"{FirstName(t.YesName)} missed";

    /// <summary>What actually happened, for the verdict's line ("Haliburton had 27", "IND won by 12").</summary>
    public static string WhatHappened(StudioTakeRecord t, GameResult r)
    {
        if (t.Check == TakeCheck.Scores)
        {
            var line = r.Home.Lines.Concat(r.Away.Lines).FirstOrDefault(l => l.Player.Id == t.PlayerId);
            return line == null ? "he didn't play" : $"{line.Player.Name} had {line.Points}";
        }
        var win = r.Winner; var lose = r.Home == win ? r.Away : r.Home;
        return $"{win.Team.Abbreviation} won by {win.Points - lose.Points}";
    }

    /// <summary>"Do the Raptors" for a plural club name, "Does Real Madrid" otherwise.</summary>
    public static string Ask(Team t) => t.Name.EndsWith('s') ? $"Do the {t.Name}" : $"Does {t.FullName}";

    private static string Short(string why)
    {
        // The pick's own reason, without its "XYZ by N" tail (the card adds the margin).
        int cut = why.LastIndexOf(". ", StringComparison.Ordinal);
        var s = cut > 0 ? why[..(cut + 1)] : why;
        int colon = s.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0 && s.Length - colon < 18) s = s[..colon] + ".";
        return s.TrimEnd().EndsWith('.') ? s.TrimEnd() : s.TrimEnd() + ".";
    }
}

/// <summary>A debate or hot take on the record for one game (run 235), graded at the final. Older saves load with none.</summary>
public sealed class StudioTakeRecord
{
    public const string Debate = "debate", Take = "take";
    public string Key { get; set; } = "";
    public DateOnly Date { get; set; }
    public int Season { get; set; }
    public string Kind { get; set; } = Debate;
    public string Question { get; set; } = "";
    /// <summary>The side arguing the claim (a hot take's speaker).</summary>
    public string Yes { get; set; } = "";
    public string YesName { get; set; } = "";
    /// <summary>The side against it (empty for a hot take).</summary>
    public string No { get; set; } = "";
    public string NoName { get; set; } = "";
    public string Check { get; set; } = "";
    public int TeamId { get; set; }
    public int PlayerId { get; set; }
    public int Line { get; set; }
    /// <summary>Did the claim hold? Null until the game is played.</summary>
    public bool? Held { get; set; }
    public string Verdict { get; set; } = "";
    public string Happened { get; set; } = "";
}

public sealed partial class GameSession
{
    /// <summary>The verdicts for a final, read from the box without changing anything (the postgame show airs before the
    /// game is recorded on Continue): each locked debate and hot take for this game with its chip and what happened.</summary>
    public List<(StudioTakeRecord Take, bool Holds, string Verdict, string Happened)> StudioVerdicts(GameResult r)
    {
        if (r.Date is not { } d) return new();
        string key = StudioShow.GameKey(d, r.Home.Team, r.Away.Team);
        return Media.Studio.Takes.Where(t => t.Key == key)
            .OrderBy(t => t.Kind == StudioTakeRecord.Debate ? 0 : 1)
            .Select(t => { bool ok = StudioDebates.Holds(t.Check, t.TeamId, t.PlayerId, t.Line, r); return (t, ok, StudioDebates.Verdict(t, ok), StudioDebates.WhatHappened(t, r)); })
            .ToList();
    }

    /// <summary>The postgame show's closing beat: the night's debate and hot take, judged ("Reggie was right").</summary>
    public List<DeskBeat> VerdictBeats(GameResult r)
    {
        var v = StudioVerdicts(r);
        if (v.Count == 0) return new();
        var rows = new List<DeskRow>();
        foreach (var (t, ok, verdict, happened) in v)
        {
            rows.Add(new("", $"{(t.Kind == StudioTakeRecord.Debate ? "The debate" : "Hot take")}: {t.Question}", "", 0, true));
            rows.Add(new("", $"{verdict}: {happened}", ""));
        }
        var parts = v.Select(x => $"{x.Verdict}: {x.Happened}.").ToList();
        return new() { new DeskBeat("THE VERDICT", "Who called it", rows, new StudioLine(StudioShow.HostDesk, string.Join(" ", parts))) };
    }

}
