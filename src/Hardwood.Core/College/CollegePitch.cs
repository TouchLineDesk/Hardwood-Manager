using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>
/// K15a (run 146, critic review #18 item 7): the recruiting pitch. The coached program chooses what its calls and visits
/// sell a prospect: early playing time, a big-name program, staying close to home or a winning team (the four things a
/// prospect can care most about, <see cref="RecruitPriority"/>). A pitch that matches what he cares about lands better
/// (each call and visit gains <see cref="PitchMatch"/>× the interest, and the message adds <see cref="PitchMatchAppeal"/>
/// appeal once he has heard it); a pitch that misses lands worse (<see cref="PitchMiss"/>×, <see cref="PitchMissAppeal"/>).
/// No pitch is the old, neutral courting. Scouting him tells you what he cares about; without it a pitch is a guess.
/// Only the coached program pitches, so the computer schools (and March Madness) are unchanged.
/// </summary>
public sealed partial class CollegeWorld
{
    public const double PitchMatch = 1.4, PitchMiss = 0.6, PitchMatchAppeal = 3, PitchMissAppeal = -2;

    /// <summary>The four pitches, in the order the Recruiting tab lists them.</summary>
    public static readonly RecruitPriority[] Pitches = { RecruitPriority.PlayingTime, RecruitPriority.Prestige, RecruitPriority.Home, RecruitPriority.Winning };

    /// <summary>Whether a pitch is what he cares about most (null when there's no pitch).</summary>
    public static bool? PitchMatches(Player p, Recruit r) => r.Pitch is { } k ? k == PriorityOf(p) : null;

    /// <summary>How much of a call's or a visit's interest lands under the current pitch.</summary>
    public static double PitchFactor(Player p, Recruit r) => PitchMatches(p, r) switch { true => PitchMatch, false => PitchMiss, _ => 1 };

    /// <summary>He has heard the pitch once you've called or visited him; it weighs in your appeal only once your staff
    /// has scouted him (run 146 review: otherwise cycling the free pitch on an unscouted prospect would reveal what he cares about).</summary>
    public static bool HeardPitch(Recruit r) => r.Pitch != null && r.Scouted && (r.Calls > 0 || r.HomeVisit || r.OfficialVisit);

    /// <summary>The pitch's own weight in the coached program's appeal (0 for anyone else, or before he has heard it).</summary>
    public static double PitchAppeal(Player p, Recruit r, Team t) =>
        r.CourtedBy != t.Id || !HeardPitch(r) ? 0 : PitchMatches(p, r) == true ? PitchMatchAppeal : PitchMissAppeal;

    /// <summary>The pitch as the Recruiting tab names it.</summary>
    public static string PitchName(RecruitPriority? k) => k switch
    {
        RecruitPriority.PlayingTime => "Early playing time",
        RecruitPriority.Prestige => "A big-name program",
        RecruitPriority.Home => "Close to home",
        RecruitPriority.Winning => "A winning team",
        _ => "No pitch",
    };

    /// <summary>What he cares about, in two words for a card ("playing time").</summary>
    public static string PriorityShort(RecruitPriority k) => k switch
    {
        RecruitPriority.PlayingTime => "playing time",
        RecruitPriority.Prestige => "a big name",
        RecruitPriority.Home => "home",
        _ => "winning",
    };

    /// <summary>Choose what your calls and visits sell him (free; it can be changed at any time).</summary>
    public string SetPitch(Team t, Player p, Recruit r, RecruitPriority? pitch, DateOnly today)
    {
        if (Closed(p, r, today) is { } c) return c;
        Courting(r, t);
        if (r.Pitch == pitch) return pitch == null ? $"You have no pitch for {p.Name}." : $"You're already selling {p.Name} on {PriorityText(pitch.Value)}.";
        r.Pitch = pitch; r.OnBoard = true;
        if (pitch == null) return $"No pitch for {p.Name}: your calls and visits sell nothing in particular.";
        string what = $"Your calls and visits now sell {p.Name} on {PriorityText(pitch.Value)}.";
        if (!r.Scouted) return what + " Scout him to learn whether that is what he cares about.";
        return what + (pitch == PriorityOf(p)
            ? $" That is what he cares about most: each call and visit lands {(PitchMatch - 1) * 100:0}% better."
            : $" He cares most about {PriorityText(PriorityOf(p))}: each call and visit lands {(1 - PitchMiss) * 100:0}% worse.");
    }

    private static string PitchedText(Player p, Recruit r) => r.Pitch is { } k ? $", selling him on {PriorityText(k)}" : "";

    /// <summary>After a scouted contact, a word on how the pitch landed.</summary>
    private static string PitchHint(Player p, Recruit r) => !r.Scouted || r.Pitch == null ? ""
        : PitchMatches(p, r) == true ? " The pitch landed: it's what he cares about." : $" The pitch fell flat: he cares most about {PriorityText(PriorityOf(p))}.";

    /// <summary>
    /// A program's appeal to a prospect in named parts, adding up to <see cref="Appeal"/> without the luck: prestige,
    /// home, playing time and winning (the one he cares about most with its extra weight), and for the coached program
    /// its courting (interest against what the rivals put in, and its reach), its pros and its pitch.
    /// </summary>
    public List<(string Factor, double Value)> AppealBreakdown(Team t, Player p, Recruit r, ClassBoard board, Dictionary<int, int>? ranks = null, double? scale = null)
    {
        var (prestige, near, minutes, winning) = AppealParts(t, p, r, board);
        var k = PriorityOf(p);
        var parts = new List<(string, double)>
        {
            ("prestige", prestige + (k == RecruitPriority.Prestige ? (prestige - 50) * 0.5 : 0)),
            ("home", near * (k == RecruitPriority.Home ? 1.5 : 1)),
            ("playing time", minutes * (k == RecruitPriority.PlayingTime ? 2 : 1)),
            ("winning", winning * (k == RecruitPriority.Winning ? 2 : 1)),
        };
        if (t.Id == ManagedProgramId)
        {
            parts.Add(("courting", CourtingEdge(r, t.Id, InterestIn(r, t)) + ReachCredit(t, r, ranks, scale)));
            parts.Add(("pros", PitchFor(r)));
            parts.Add(("pitch", PitchAppeal(p, r, t)));
        }
        return parts;
    }

    /// <summary>
    /// Why the coached program trails (or leads) for him, factor by factor against the best rival who can take him:
    /// "Syracuse leads you by 12: prestige +18, winning +4. You lead on home (+8)." Empty once he has committed or when
    /// no rival is recruiting him.
    /// </summary>
    public string WhyTrails(Team me, Player p, Recruit r, ClassBoard? board = null)
    {
        if (r.CommittedTo != null) return "";
        board ??= BoardFor(r.ClassOf);
        var ranks = PrestigeRanks();
        var rival = Considering(p, r, board).Select(x => x.Program).FirstOrDefault(x => x != me);
        if (rival == null) return "";
        var mine = AppealBreakdown(me, p, r, board, ranks);
        var theirs = AppealBreakdown(rival, p, r, board, ranks);
        double gap = theirs.Sum(x => x.Value) - mine.Sum(x => x.Value);
        // Rivals court every prospect in their reach as a matter of course (the baseline); yours is the difference.
        var diffs = mine.Select((m, i) => (m.Factor, Lead: (i < theirs.Count ? theirs[i].Value : 0) - m.Value))
                        .Where(x => Math.Abs(x.Lead) >= 0.5).ToList();
        var theyLead = diffs.Where(x => x.Lead > 0).OrderByDescending(x => x.Lead).Take(3).ToList();
        var youLead = diffs.Where(x => x.Lead < 0).OrderBy(x => x.Lead).Take(3).ToList();
        string Fmt(IEnumerable<(string Factor, double Lead)> xs) => string.Join(", ", xs.Select(x => $"{x.Factor} {Math.Abs(x.Lead):+0;+0}"));
        string care = $" He cares most about {PriorityText(PriorityOf(p))}.";
        string known = r.CourtedBy == me.Id && r.Scouted ? care : "";
        if (gap > 0)
            return $"{rival.City} leads you by {gap:0}" + (theyLead.Count > 0 ? $": {Fmt(theyLead)}." : ".")
                   + (youLead.Count > 0 ? $" You lead on {Fmt(youLead)}." : "") + known;
        return $"You lead {rival.City} by {-gap:0}" + (youLead.Count > 0 ? $": {Fmt(youLead)}." : ".")
               + (theyLead.Count > 0 ? $" They lead on {Fmt(theyLead)}." : "") + known;
    }
}
