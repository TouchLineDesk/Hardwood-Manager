using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>
/// Run 164 (critic review #21 item 5; Danny's college order, step 2): the recruiting trail's first week. Three parts, all
/// read-only (no RNG, no save fields):
/// <list type="bullet">
/// <item>a three-step guide on the Recruiting tab (scout, offer, visit) that ticks itself off as you do each one;</item>
/// <item>"the word" on what a prospect cares about before your staff scouts him: the recruiting services' read, right
/// about <see cref="WordAccuracy"/> of the time, fixed per prospect (a hash of his id, never the game's RNG); scouting
/// confirms or corrects it;</item>
/// <item>the reason he would say no to you today, in one line on his card (<see cref="SaysNo"/>).</item>
/// </list>
/// </summary>
public sealed partial class CollegeWorld
{
    /// <summary>How often the recruiting services' word on what a prospect cares about is right.</summary>
    public const double WordAccuracy = 0.7;

    /// <summary>
    /// What the word is on what he cares about most: <see cref="PriorityOf"/> about 7 times in 10, otherwise one of the other
    /// three. A separate hash of his id from <see cref="PriorityOf"/>'s, so it is the same every day and in every save.
    /// </summary>
    public static RecruitPriority WordOf(Player p)
    {
        uint h = (uint)p.Id;
        h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
        var k = PriorityOf(p);
        if (h % 100 < (uint)Math.Round(WordAccuracy * 100)) return k;
        return (RecruitPriority)(((int)k + 1 + (int)(h / 100 % 3)) % 4);
    }

    /// <summary>One step of the first-week guide.</summary>
    public sealed record GuideStep(string Title, string How, bool Done);

    /// <summary>
    /// The first-week guide for a class (scout, offer, visit), each done once you have done it to any prospect in the
    /// class. Empty when the class can't be recruited today or has no scholarship open.
    /// </summary>
    public List<GuideStep> FirstWeekGuide(Team me, int classOf, DateOnly today)
    {
        if (!Recruitable(new Recruit { ClassOf = classOf }, today) || OpenScholarships(me, classOf) <= 0) return new();
        var ours = Recruits.Values.Where(r => r.ClassOf == classOf && r.CourtedBy == me.Id).ToList();
        var opens = CommitmentsOpen(classOf);
        bool seniorsVisit = today >= opens;
        return new()
        {
            new("Scout a best fit", $"Scout ({ScoutHours} h): your staff's projection, and what he really cares about. Then set your pitch to it.",
                ours.Any(r => r.Scouted)),
            new("Offer him a scholarship", $"Offer ({OfferHours} h): no offer, no commitment. You can have your open scholarships + 3 out.",
                ours.Any(r => r.Offered || r.CommittedTo == me.Id)),
            new("Visit him", $"A home visit ({HomeVisitHours} h) is worth more than two calls" + (seniorsVisit ? $"; an official visit ({OfficialVisitHours} h) is the biggest push." : $". Official visits open on {opens:MMM d}.")
                + " Call each target once a week (1 h).",
                ours.Any(r => r.HomeVisit || r.OfficialVisit)),
        };
    }

    /// <summary>
    /// Why he would say no to the coached program today, in a few words for his card; "" when he leans your way with an
    /// offer made, or has committed or signed. Examples: "Says no: Kentucky leads by 12 (prestige)", "No offer yet: he can't
    /// commit to you" (you'd top his list on appeal, but haven't offered), "Says no: no scholarship left in his class".
    /// </summary>
    public string SaysNo(Team me, Player p, Recruit r, ClassBoard board, Team? lean, Dictionary<int, int>? ranks = null)
    {
        if (r.CommittedTo != null) return "";
        if (OpenScholarships(me, r.ClassOf) <= 0) return "Says no: no scholarship left in his class";
        bool offered = r.CourtedBy == me.Id && r.Offered;
        // The coached program can only be his lean once it has offered (CanTake), so a lean elsewhere may just be the
        // missing offer: name the rival only when he'd still pick the rival on appeal (run 164 review).
        if (lean != null && lean != me)
        {
            ranks ??= PrestigeRanks();
            var mine = AppealBreakdown(me, p, r, board, ranks);
            var theirs = AppealBreakdown(lean, p, r, board, ranks);
            double gap = theirs.Sum(x => x.Value) - mine.Sum(x => x.Value);
            if (gap > 0)
            {
                var top = mine.Select((m, i) => (m.Factor, Lead: (i < theirs.Count ? theirs[i].Value : 0) - m.Value))
                              .Where(x => x.Lead >= 0.5).OrderByDescending(x => x.Lead).Take(2).Select(x => x.Factor).ToList();
                string why = top.Count > 0 ? $" ({string.Join(", ", top)})" : "";
                return $"Says no: {lean.City} leads by {Math.Max(1, gap):0}{why}";
            }
        }
        return offered ? "" : "No offer yet: he can't commit to you";
    }
}
