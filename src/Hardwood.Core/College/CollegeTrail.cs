using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>What a high-school prospect weighs most when he picks a college (C3, decision #174).</summary>
public enum RecruitPriority { PlayingTime, Prestige, Home, Winning }

/// <summary>
/// The recruiting trail, part 1 (college slice C3, decisions #173-#178): the coached program courts high-school prospects
/// with a weekly budget of coach hours (scouting, calls, a home visit, an official visit, a scholarship offer). Courting
/// raises the prospect's <see cref="Recruit.Interest"/>, which adds to the program's appeal against every computer school;
/// he can only commit to the coached program once it has offered. The computer schools compete through
/// <see cref="CollegeWorld.Appeal"/> as before, and the board shows who he's leaning toward.
/// </summary>
public sealed partial class CollegeWorld
{
    /// <summary>Coach hours a week for recruiting (the head coach and his assistants on the road).</summary>
    public const int WeeklyHours = 24;
    public const int ScoutHours = 1, CallHours = 1, HomeVisitHours = 4, OfficialVisitHours = 8, OfferHours = 2;

    /// <summary>Hours left this week (saved; refilled each Monday by <see cref="NewTrailWeek"/>).</summary>
    public int TrailHours { get; set; }
    /// <summary>The Monday the hours were last refilled (null: never, so the first access fills them).</summary>
    public DateOnly? TrailWeek { get; set; }

    /// <summary>Messages for the coached program (its commitments and losses); the session posts and clears them.</summary>
    public List<(string Title, string Body)> CoachNews { get; } = new();

    public static DateOnly MondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>Hours left this week for a program's coach (refilled lazily the first time in a new week).</summary>
    public int HoursLeft(DateOnly today)
    {
        var monday = MondayOf(today);
        if (TrailWeek != monday) { TrailWeek = monday; TrailHours = WeeklyHours; }
        return TrailHours;
    }

    /// <summary>The prospect's record of a program's courting, reset when another program (a new job) starts on him.</summary>
    public static Recruit Courting(Recruit r, Team t)
    {
        if (r.CourtedBy != t.Id)
        {
            r.CourtedBy = t.Id; r.Interest = 0; r.OnBoard = false; r.Scouted = false; r.Calls = 0; r.CalledWeek = null;
            r.HomeVisit = false; r.OfficialVisit = false; r.Offered = false; r.Pitch = null; r.PitchChosen = false; r.Promised = false; r.FlipOffer = false;
        }
        return r;
    }

    /// <summary>Your interest with him, or 0 if you haven't courted him (or the courting was another job's).</summary>
    public static double InterestIn(Recruit r, Team t) => r.CourtedBy == t.Id ? r.Interest : 0;

    /// <summary>The best prospects hear from everyone: each contact moves a 5-star 60% as much, a 4-star 80%.</summary>
    public static double ContactScale(Recruit r) => r.Stars >= 5 ? 0.6 : r.Stars == 4 ? 0.8 : 1.0;

    /// <summary>Offers still open to uncommitted prospects in a class.</summary>
    public int OffersOut(Team t, int classOf) =>
        Recruits.Values.Count(r => r.ClassOf == classOf && r.CourtedBy == t.Id && r.Offered
            && (r.CommittedTo == null
                // K15b review: an offer made to a verbal commit elsewhere holds a place until he signs (no unlimited flip offers).
                || (r.FlipOffer && r.CommittedTo != t.Id && !Signed(r, RecruitingDay ?? DateOnly.MinValue))));

    /// <summary>A program can have this many offers out in a class: its open scholarships plus 3 (coaches over-offer; not everyone says yes).</summary>
    public int OfferLimit(Team t, int classOf) => OpenScholarships(t, classOf) is var open and > 0 ? open + 3 : 0;

    /// <summary>
    /// Danny, #320 (D34 A): college coaches recruit high-school seniors only, the coached program and the computer schools
    /// alike (the computer schools only ever took commitments from seniors). The class being recruited is this year's
    /// seniors until their signing day, then next year's (rising seniors can be courted from the spring; they commit from
    /// September 1). Juniors and younger stay in the world but can't be courted yet.
    /// </summary>
    public int RecruitingClass(DateOnly today) => today >= SigningDay(SeniorClass) ? SeniorClass + 1 : SeniorClass;

    public bool Recruitable(Recruit r, DateOnly today) => r.ClassOf == RecruitingClass(today);

    /// <summary>Why he can't be recruited yet (too young), or null.</summary>
    public string? NotASenior(Player p, Recruit r, DateOnly today) =>
        r.ClassOf <= RecruitingClass(today) ? null
        : $"You recruit high-school seniors only: {p.Name} is in the Class of {r.ClassOf}, so you can recruit him from {SigningDay(r.ClassOf - 1):MMMM d, yyyy}.";

    /// <summary>Why he can't be courted right now, or null.</summary>
    private string? Closed(Player p, Recruit r, DateOnly today)
    {
        // K15b (run 147): a commitment binds once he signs (the November early period, or signing day); before that it is
        // verbal, and he can still be courted (and flipped).
        if (r.CommittedTo is { } to && Signed(r, today)) return $"{p.Name} has signed with {Program(to)?.FullName ?? "another school"}.";
        if (today >= SigningDay(r.ClassOf)) return $"Signing day has passed for the Class of {r.ClassOf}.";
        return NotASenior(p, r, today);
    }

    private string? Spend(int hours, DateOnly today)
    {
        int left = HoursLeft(today);
        if (left < hours) return $"That takes {hours} coach hour{(hours == 1 ? "" : "s")}; you have {left} left this week.";
        TrailHours -= hours;
        return null;
    }

    private string Gain(Recruit r, double amount, Player? pitched = null)
    {
        double before = r.Interest;
        if (pitched != null) amount *= PitchFactor(pitched, r);
        r.Interest = Math.Min(100, r.Interest + amount);
        return $"Interest {before:0} → {r.Interest:0}.";
    }

    /// <summary>Put him on (or take him off) your board. Free.</summary>
    public string ToggleBoard(Team t, Player p, Recruit r, DateOnly today)
    {
        if (NotASenior(p, r, today) is { } junior && !(r.CourtedBy == t.Id && r.OnBoard)) return junior; // taking an old pick off stays possible
        Courting(r, t);
        r.OnBoard = !r.OnBoard;
        return r.OnBoard ? $"{p.Name} is on your board." : $"{p.Name} is off your board.";
    }

    /// <summary>Scout him (1 hour): your staff's own read of his ceiling, and what he cares about most.</summary>
    public string Scout(Team t, Player p, Recruit r, DateOnly today)
    {
        if (NotASenior(p, r, today) is { } junior) return junior;
        Courting(r, t);
        if (r.Scouted) return $"You've already scouted {p.Name}.";
        if (Spend(ScoutHours, today) is { } no) return no;
        r.Scouted = true; r.OnBoard = true;
        return $"Scouted {p.Name}: your staff project him at {StaffProjection(p, r)} by 22. He cares most about {PriorityText(PriorityOf(p))}.";
    }

    /// <summary>Your staff's projection once scouted: closer to the truth than the services' (a third of their blind spot).</summary>
    public int StaffProjection(Player p, Recruit r) => Destined.GetValueOrDefault(p.Id, p.PotentialAbility) + r.Bias / 3;

    /// <summary>A phone call (1 hour, once a week): a little goodwill, less each time.</summary>
    public string Call(Team t, Player p, Recruit r, DateOnly today)
    {
        if (Closed(p, r, today) is { } c) return c;
        Courting(r, t);
        if (r.CalledWeek == MondayOf(today)) return $"You've already called {p.Name} this week.";
        if (Spend(CallHours, today) is { } no) return no;
        r.CalledWeek = MondayOf(today); r.OnBoard = true;
        double gain = 5 / (1 + r.Calls * 0.25) * ContactScale(r);
        r.Calls++;
        return $"You called {p.Name}{PitchedText(p, r)}. " + Gain(r, gain, p) + PitchHint(p, r);
    }

    /// <summary>A home visit (4 hours, once): sitting down with the family.</summary>
    public string VisitHome(Team t, Player p, Recruit r, DateOnly today)
    {
        if (Closed(p, r, today) is { } c) return c;
        Courting(r, t);
        if (r.HomeVisit) return $"You've already been to {p.Name}'s home.";
        if (Spend(HomeVisitHours, today) is { } no) return no;
        r.HomeVisit = true; r.OnBoard = true;
        return $"You visited {p.Name} and his family in {StateName(r.State)}{PitchedText(p, r)}. " + Gain(r, 12 * ContactScale(r), p) + PitchHint(p, r);
    }

    /// <summary>An official visit (8 hours, once, seniors only from September 1): a weekend on campus.</summary>
    public string OfficialVisit(Team t, Player p, Recruit r, DateOnly today)
    {
        if (Closed(p, r, today) is { } c) return c;
        Courting(r, t);
        if (r.OfficialVisit) return $"{p.Name} has already taken his official visit to you.";
        if (today < CommitmentsOpen(r.ClassOf)) return $"Official visits open for the Class of {r.ClassOf} on {CommitmentsOpen(r.ClassOf):MMM d, yyyy}.";
        if (Spend(OfficialVisitHours, today) is { } no) return no;
        r.OfficialVisit = true; r.OnBoard = true;
        return $"{p.Name} spent a weekend on campus at {t.FullName}{PitchedText(p, r)}. " + Gain(r, 18 * ContactScale(r), p) + PitchHint(p, r);
    }

    /// <summary>A scholarship offer (2 hours): needed before he can commit to you, and a boost on its own.</summary>
    public string Offer(Team t, Player p, Recruit r, DateOnly today)
    {
        if (Closed(p, r, today) is { } c) return c;
        Courting(r, t);
        if (r.Offered) return $"{p.Name} already has your offer.";
        int limit = OfferLimit(t, r.ClassOf), outNow = OffersOut(t, r.ClassOf);
        if (limit == 0)
            return $"You have no scholarships left in the Class of {r.ClassOf}: every place is taken by returning players and commitments.";
        if (outNow >= limit)
            return $"You have {outNow} offers out to the Class of {r.ClassOf} and {OpenScholarships(t, r.ClassOf)} scholarships open: you can have at most {limit} out.";
        if (Spend(OfferHours, today) is { } no) return no;
        r.Offered = true; r.OnBoard = true;
        r.FlipOffer = r.CommittedTo is { } elsewhere && elsewhere != t.Id;
        return $"You offered {p.Name} a scholarship. " + Gain(r, 8 * ContactScale(r))
            + (r.CommittedTo is { } other && other != t.Id
                ? $" He is committed to {Program(other)?.FullName}, but only verbally: lead them clearly and he may flip to you before he signs."
                : " He can now commit to you.");
    }

    /// <summary>The schools he's considering, best first, as he sees them today (no luck): only those that can take him
    /// (a scholarship left, and for the coached program an offer).</summary>
    public List<(Team Program, double Appeal)> Considering(Player p, Recruit r, ClassBoard? board = null)
    {
        board ??= BoardFor(r.ClassOf);
        var ranks = PrestigeRanks();
        return Programs.Where(t => CouldTake(t, r, board, ranks))
                       .Select(t => (t, Appeal(t, p, r, board, null, ranks)))
                       .OrderByDescending(x => x.Item2).ToList();
    }

    /// <summary>Where the coached program would stand among the schools he's considering (1 = top), were it able to take him.</summary>
    public int StandingOf(Team t, Player p, Recruit r, ClassBoard? board = null)
    {
        board ??= BoardFor(r.ClassOf);
        double mine = Appeal(t, p, r, board);
        return 1 + Considering(p, r, board).Count(x => x.Program != t && x.Appeal > mine);
    }

    /// <summary>"Leaning: X" (the school he'd pick today among those who can take him).</summary>
    public Team? LeaningTo(Player p, Recruit r, ClassBoard? board = null)
    {
        if (r.CommittedTo is { } to) return Program(to);
        board ??= BoardFor(r.ClassOf);
        var ranks = PrestigeRanks();
        Team? best = null; double top = double.MinValue;
        foreach (var t in Programs)
        {
            if (!CouldTake(t, r, board, ranks)) continue;
            double a = Appeal(t, p, r, board, null, ranks);
            if (a > top) { top = a; best = t; }
        }
        return best;
    }

    /// <summary>
    /// Each Monday: your effort fades a little (1% a week), and if your assistant recruits for you (delegation), he spends
    /// the hours you left unused last week before they refill. Returns what he did, for the Staff page.
    /// </summary>
    public string? NewTrailWeek(Team t, DateOnly today, bool delegated)
    {
        string? did = null;
        if (TrailWeek is { } last && last < MondayOf(today))
        {
            foreach (var r in Recruits.Values)
                if (r.CourtedBy == t.Id && r.CommittedTo == null) r.Interest *= 0.99;
            if (delegated && TrailHours > 0) did = AutoRecruit(t, last);
        }
        HoursLeft(today);
        return did;
    }

    /// <summary>
    /// The assistant's recruiting (delegation, and the critic bot): offer the seniors he can land who fill a need, bring
    /// them on official visits, then home visits and calls. Seniors only (#320): spare hours are left unspent.
    /// <paramref name="week"/> is the Monday whose hours he spends.
    /// </summary>
    public string? AutoRecruit(Team t, DateOnly week)
    {
        var day = week.AddDays(6); // spend them as of Sunday of that week
        int senior = RecruitingClass(day);
        var names = new List<string>(); var courted = new List<string>();
        foreach (int cls in new[] { senior })
        {
            if (day >= SigningDay(cls)) continue;
            var board = BoardFor(cls);
            int open = Math.Max(0, board.Open[t.Id]);
            if (cls == senior && open == 0) continue;
            // H152 (run 136): in March and April a real staff stops waiting on lost causes. An offer to a prospect who has
            // leaned elsewhere by a clear margin is withdrawn, so the offer goes to someone who would say yes; and the
            // targets are those who already lean his way (or nearly), not the best names he can only hope for.
            bool spring = day.Month is >= 3 and <= 4;
            var withdrew = spring ? WithdrawLostCauses(t, cls, board) : new List<string>();
            if (withdrew.Count > 0) names.Add($"withdrew {(withdrew.Count == 1 ? "an offer" : $"{withdrew.Count} offers")} that won't be accepted ({string.Join("; ", withdrew)})");
            // Who he could realistically land: his appeal with a solid effort (60 interest) at least matches the best rival's.
            var targets = Fits(t, cls, board, cls == senior ? open * 2 + 2 : 4, spring ? SpringEdge : -4);
            foreach (var (p, r) in targets)
            {
                if (TrailHours <= 0) break;
                bool any = false;
                foreach (var act in new Func<string>[]
                {
                    () => cls == senior ? Offer(t, p, r, day) : "",
                    // K15a (run 146): he scouts each target (1 hour) and sells him what he cares about.
                    () => r.CourtedBy == t.Id && !r.Scouted ? Scout(t, p, r, day) : "",
                    () => { if (r.CourtedBy == t.Id && r.Scouted && r.Pitch == null && !r.PitchChosen) r.Pitch = PriorityOf(p); return ""; },
                    () => cls == senior && day >= CommitmentsOpen(cls) ? OfficialVisit(t, p, r, day) : "",
                    () => VisitHome(t, p, r, day),
                    () => Call(t, p, r, day),
                })
                {
                    int before = TrailHours;
                    act();
                    any |= TrailHours < before;
                }
                if (any) courted.Add(p.Name);
            }
        }
        if (courted.Count == 0 && names.Count == 0) return null;
        string text = $"{week:MMM d}: your assistant ";
        if (courted.Count > 0) text += $"courted {string.Join(", ", courted.Take(6))}{(courted.Count > 6 ? $" and {courted.Count - 6} more" : "")}";
        if (names.Count > 0) text += (courted.Count > 0 ? "; he " : "") + names[0];
        return text + ".";
    }

    /// <summary>How far ahead of the best rival (appeal points, with a solid effort) a spring target must be: he leans our way or nearly.</summary>
    public const double SpringEdge = -1;

    /// <summary>An offered prospect this far behind the best rival who can take him, in March or April, is a lost cause.</summary>
    public const double LostCauseGap = 8;

    /// <summary>
    /// H152 (run 136): the assistant's spring clean-up. Before, his offers went to the best prospects he could hope for in
    /// the autumn and stayed there: by March every offer the limit allowed sat with prospects leaning to Kentucky or UConn,
    /// so he could offer no one else, and on signing day they went elsewhere (Miami, seed 2026: 4 places open, 7 offers out
    /// all spring, 2 signed; hunt 19: Notre Dame 1 of 19, Providence 1 of 27). Now, from March 1, an offer to a prospect
    /// whose best rival is <see cref="LostCauseGap"/> or more ahead of you is withdrawn (his interest stays). Returns
    /// "Name, leaning Kentucky" for each, for the Staff page.
    /// </summary>
    public List<string> WithdrawLostCauses(Team t, int cls, ClassBoard board)
    {
        var said = new List<string>();
        var ranks = PrestigeRanks();
        foreach (var p in ClassOf(cls).OrderByDescending(p => Recruits[p.Id].Rank))
        {
            var r = Recruits[p.Id];
            if (r.CommittedTo != null || r.CourtedBy != t.Id || !r.Offered) continue;
            // With the same solid effort Fits assumes (60 interest, or more if he has it), so a withdrawn prospect is never a target again.
            double interest = InterestIn(r, t);
            double mine = Appeal(t, p, r, board, null, ranks) - CourtingEdge(r, t.Id, interest) + CourtingEdge(r, t.Id, Math.Max(60, interest));
            Team? best = null; double top = double.MinValue;
            foreach (var o in Programs)
            {
                if (o == t || !CouldTake(o, r, board, ranks)) continue;
                double a = Appeal(o, p, r, board, null, ranks);
                if (a > top) { top = a; best = o; }
            }
            if (best == null || top - mine < LostCauseGap) continue;
            r.Offered = false;
            said.Add($"{p.Name}, leaning {best.FullName}");
        }
        return said;
    }

    /// <summary>
    /// Uncommitted prospects in a class a program could realistically land, best first: with a solid effort (60 interest,
    /// or more if it already has it) its appeal comes within 4 of the best rival's. Assumes it is the coached program.
    /// </summary>
    public List<(Player p, Recruit r)> Fits(Team t, int cls, ClassBoard? board = null, int want = 40, double minEdge = -4)
    {
        board ??= BoardFor(cls);
        var targets = new List<(Player p, Recruit r)>();
        if (board.Open[t.Id] <= 0) return targets; // nothing to offer: no one is a realistic target
        var ranks = PrestigeRanks();
        double scale = ReachScale;
        var rivals = Programs.Where(o => o != t && board.Open[o.Id] > 0).ToList();
        foreach (var p in ClassOf(cls).OrderBy(p => Recruits[p.Id].Rank))
        {
            var r = Recruits[p.Id];
            if (r.CommittedTo != null) continue;
            double mine = InterestIn(r, t);
            double ours = Appeal(t, p, r, board, null, ranks) - CourtingEdge(r, t.Id, mine) + CourtingEdge(r, t.Id, Math.Max(60, mine));
            // Only the schools recruiting him are rivals (#184: a blue blood isn't chasing the No. 400 prospect in the autumn).
            var chasing = rivals.Where(o => InReach(o, r, ranks, scale)).ToList();
            double rival = chasing.Count == 0 ? double.MinValue : chasing.Max(o => Appeal(o, p, r, board));
            if (ours - rival > minEdge) targets.Add((p, r));
            if (targets.Count >= want) break;
        }
        return targets;
    }

    /// <summary>Interest a handed-back commitment keeps with you: he had picked your program, so he still leans your way.</summary>
    public const double HandedBackInterest = 40;

    /// <summary>
    /// CS1 (run 125): when you take over a program (a new career or a new college job), the commitments the computer
    /// made in its name for the class you're recruiting are handed back to the pool, so your first season has scholarships
    /// to chase. Before, Clemson's Recruiting tab read "0 scholarships open" from the first day until April 15. Each
    /// prospect keeps his liking for the program (interest <see cref="HandedBackInterest"/>, on your board, no offer yet):
    /// offer him and he'll usually come back. Commitments you won yourself (offered by you) are kept. Returns how many.
    /// </summary>
    public int HandBackCommitments(Team t, DateOnly today)
    {
        int cls = RecruitingClass(today);
        if (today >= SigningDay(cls)) return 0;
        var back = new List<(Player p, Recruit r)>();
        foreach (var p in ClassOf(cls))
        {
            var r = Recruits[p.Id];
            if (r.CommittedTo != t.Id || (r.CourtedBy == t.Id && r.Offered)) continue;
            r.CommittedTo = null; r.CommittedOn = null;
            Courting(r, t);
            r.Interest = Math.Max(r.Interest, HandedBackInterest);
            r.OnBoard = true;
            back.Add((p, r));
        }
        if (back.Count > 0)
        {
            var names = back.OrderBy(x => x.r.Rank).Select(x => $"{StarText(x.r.Stars)} {x.p.Name} (No. {x.r.Rank})").ToList();
            CoachNews.Add(($"{back.Count} recruit{(back.Count == 1 ? " has" : "s have")} reopened {(back.Count == 1 ? "his" : "their")} recruitment",
                $"With a new head coach, {string.Join(", ", names)} {(back.Count == 1 ? "is" : "are")} no longer committed to {t.FullName}. "
                + $"{(back.Count == 1 ? "He still likes" : "They still like")} the program and {(back.Count == 1 ? "is" : "are")} on your board: offer a scholarship on the Recruiting tab to win {(back.Count == 1 ? "him" : "them")} back before other schools do. "
                + $"You have {OpenScholarships(t, cls)} scholarships open in the Class of {cls}; signing day is {SigningDay(cls):MMMM d}."));
        }
        return back.Count;
    }

    /// <summary>Tell the coached program about a commitment it cares about: to it, or a prospect it was courting going elsewhere.</summary>
    private void TellCoach(Player p, Recruit r, Team to, bool signingDay = false, Team? flippedFrom = null)
    {
        if (ManagedProgramId is not { } me) return;
        string who = $"{StarText(r.Stars)} {p.PrimaryPosition} {p.Name} (No. {r.Rank}, {r.HighSchool}, {StateName(r.State)})";
        // K15b (run 147): a flip, to you or away from you.
        if (flippedFrom?.Id == me)
            CoachNews.Add(($"{p.Name} decommits", $"{who} has backed out of his commitment to you and committed to {to.FullName} instead. "
                + $"Interest in you was {r.Interest:0}. A verbal commitment holds only until he signs; keep calling the ones you have."));
        else if (flippedFrom != null && to.Id == me)
            CoachNews.Add(($"{p.Name} flips to you", $"{who} has backed out of his commitment to {flippedFrom.FullName} and committed to you. He enrolls in the summer of {r.ClassOf}."));
        else if (to.Id == me)
            CoachNews.Add(($"{p.Name} commits to you", $"{who} {(signingDay ? "signed with you on signing day" : "has committed to you")}. He enrolls in the summer of {r.ClassOf}."));
        // Only a prospect you'd offered gets a message when he goes elsewhere (critic review #8: a college coach's inbox had
        // about 100 of these in three seasons, most for prospects the assistant had only put on the board).
        else if (r.CourtedBy == me && r.Offered)
            CoachNews.Add(($"{p.Name} picks {to.FullName}", $"{who}, whom you were recruiting{(r.Offered ? " with a scholarship offer" : "")}, {(signingDay ? "signed with" : "has committed to")} {to.FullName}. Interest in you was {r.Interest:0}."));
    }
}
