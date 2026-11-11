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
            r.HomeVisit = false; r.OfficialVisit = false; r.Offered = false;
        }
        return r;
    }

    /// <summary>Your interest with him, or 0 if you haven't courted him (or the courting was another job's).</summary>
    public static double InterestIn(Recruit r, Team t) => r.CourtedBy == t.Id ? r.Interest : 0;

    /// <summary>The best prospects hear from everyone: each contact moves a 5-star 60% as much, a 4-star 80%.</summary>
    public static double ContactScale(Recruit r) => r.Stars >= 5 ? 0.6 : r.Stars == 4 ? 0.8 : 1.0;

    /// <summary>Offers still open to uncommitted prospects in a class.</summary>
    public int OffersOut(Team t, int classOf) =>
        Recruits.Values.Count(r => r.ClassOf == classOf && r.CourtedBy == t.Id && r.Offered && r.CommittedTo == null);

    /// <summary>A program can have this many offers out in a class: its open scholarships plus 3 (coaches over-offer; not everyone says yes).</summary>
    public int OfferLimit(Team t, int classOf) => OpenScholarships(t, classOf) is var open and > 0 ? open + 3 : 0;

    /// <summary>Why he can't be courted right now, or null.</summary>
    private string? Closed(Player p, Recruit r, DateOnly today)
    {
        if (r.CommittedTo is { } to) return $"{p.Name} has committed to {Program(to)?.FullName ?? "another school"}.";
        if (today >= SigningDay(r.ClassOf)) return $"Signing day has passed for the Class of {r.ClassOf}.";
        return null;
    }

    private string? Spend(int hours, DateOnly today)
    {
        int left = HoursLeft(today);
        if (left < hours) return $"That takes {hours} coach hour{(hours == 1 ? "" : "s")}; you have {left} left this week.";
        TrailHours -= hours;
        return null;
    }

    private string Gain(Recruit r, double amount)
    {
        double before = r.Interest;
        r.Interest = Math.Min(100, r.Interest + amount);
        return $"Interest {before:0} → {r.Interest:0}.";
    }

    /// <summary>Put him on (or take him off) your board. Free.</summary>
    public string ToggleBoard(Team t, Player p, Recruit r)
    {
        Courting(r, t);
        r.OnBoard = !r.OnBoard;
        return r.OnBoard ? $"{p.Name} is on your board." : $"{p.Name} is off your board.";
    }

    /// <summary>Scout him (1 hour): your staff's own read of his ceiling, and what he cares about most.</summary>
    public string Scout(Team t, Player p, Recruit r, DateOnly today)
    {
        Courting(r, t);
        if (r.Scouted) return $"You've already scouted {p.Name}.";
        if (Spend(ScoutHours, today) is { } no) return no;
        r.Scouted = true; r.OnBoard = true;
        return $"Scouted {p.Name}: your staff project him at {StaffProjection(p, r)} by 23. He cares most about {PriorityText(PriorityOf(p))}.";
    }

    /// <summary>Your staff's projection once scouted: closer to the truth than the services' (a third of their blind spot).</summary>
    public int StaffProjection(Player p, Recruit r) => Destined.GetValueOrDefault(p.Id, p.PotentialAbility) + r.Bias / 3;

    /// <summary>A phone call (1 hour, once a week): a little goodwill, less each time.</summary>
    public string Call(Team t, Player p, Recruit r, DateOnly today)
    {
        Courting(r, t);
        if (Closed(p, r, today) is { } c) return c;
        if (r.CalledWeek == MondayOf(today)) return $"You've already called {p.Name} this week.";
        if (Spend(CallHours, today) is { } no) return no;
        r.CalledWeek = MondayOf(today); r.OnBoard = true;
        double gain = 5 / (1 + r.Calls * 0.25) * ContactScale(r);
        r.Calls++;
        return $"You called {p.Name}. " + Gain(r, gain);
    }

    /// <summary>A home visit (4 hours, once): sitting down with the family.</summary>
    public string VisitHome(Team t, Player p, Recruit r, DateOnly today)
    {
        Courting(r, t);
        if (Closed(p, r, today) is { } c) return c;
        if (r.HomeVisit) return $"You've already been to {p.Name}'s home.";
        if (Spend(HomeVisitHours, today) is { } no) return no;
        r.HomeVisit = true; r.OnBoard = true;
        return $"You visited {p.Name} and his family in {StateName(r.State)}. " + Gain(r, 12 * ContactScale(r));
    }

    /// <summary>An official visit (8 hours, once, seniors only from September 1): a weekend on campus.</summary>
    public string OfficialVisit(Team t, Player p, Recruit r, DateOnly today)
    {
        Courting(r, t);
        if (Closed(p, r, today) is { } c) return c;
        if (r.OfficialVisit) return $"{p.Name} has already taken his official visit to you.";
        if (today < CommitmentsOpen(r.ClassOf)) return $"Official visits open for the Class of {r.ClassOf} on {CommitmentsOpen(r.ClassOf):MMM d, yyyy}.";
        if (Spend(OfficialVisitHours, today) is { } no) return no;
        r.OfficialVisit = true; r.OnBoard = true;
        return $"{p.Name} spent a weekend on campus at {t.FullName}. " + Gain(r, 18 * ContactScale(r));
    }

    /// <summary>A scholarship offer (2 hours): needed before he can commit to you, and a boost on its own.</summary>
    public string Offer(Team t, Player p, Recruit r, DateOnly today)
    {
        Courting(r, t);
        if (Closed(p, r, today) is { } c) return c;
        if (r.Offered) return $"{p.Name} already has your offer.";
        int limit = OfferLimit(t, r.ClassOf), outNow = OffersOut(t, r.ClassOf);
        if (limit == 0)
            return $"You have no scholarships left in the Class of {r.ClassOf}: every place is taken by returning players and commitments.";
        if (outNow >= limit)
            return $"You have {outNow} offers out to the Class of {r.ClassOf} and {OpenScholarships(t, r.ClassOf)} scholarships open: you can have at most {limit} out.";
        if (Spend(OfferHours, today) is { } no) return no;
        r.Offered = true; r.OnBoard = true;
        return $"You offered {p.Name} a scholarship. " + Gain(r, 8 * ContactScale(r)) + " He can now commit to you.";
    }

    /// <summary>The schools he's considering, best first, as he sees them today (no luck): only those that can take him
    /// (a scholarship left, and for the coached program an offer).</summary>
    public List<(Team Program, double Appeal)> Considering(Player p, Recruit r, ClassBoard? board = null)
    {
        board ??= BoardFor(r.ClassOf);
        return Programs.Where(t => board.Open[t.Id] > 0 && CanTake(t, r))
                       .Select(t => (t, Appeal(t, p, r, board)))
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
        Team? best = null; double top = double.MinValue;
        foreach (var t in Programs)
        {
            if (board.Open[t.Id] <= 0 || !CanTake(t, r)) continue;
            double a = Appeal(t, p, r, board);
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
    /// them on official visits, then home visits and calls; spare hours go on calls to the best juniors who'd come.
    /// <paramref name="week"/> is the Monday whose hours he spends.
    /// </summary>
    public string? AutoRecruit(Team t, DateOnly week)
    {
        var day = week.AddDays(6); // spend them as of Sunday of that week
        int senior = Season.Year + 1;
        var names = new List<string>();
        foreach (int cls in new[] { senior, senior + 1 })
        {
            if (day >= SigningDay(cls)) continue;
            var board = BoardFor(cls);
            int open = Math.Max(0, board.Open[t.Id]);
            if (cls == senior && open == 0) continue;
            // Who he could realistically land: his appeal with a solid effort (60 interest) at least matches the best rival's.
            var targets = Fits(t, cls, board, cls == senior ? open * 2 + 2 : 4);
            foreach (var (p, r) in targets)
            {
                if (TrailHours <= 0) break;
                bool any = false;
                foreach (var act in new Func<string>[]
                {
                    () => cls == senior ? Offer(t, p, r, day) : "",
                    () => cls == senior && day >= CommitmentsOpen(cls) ? OfficialVisit(t, p, r, day) : "",
                    () => VisitHome(t, p, r, day),
                    () => Call(t, p, r, day),
                })
                {
                    int before = TrailHours;
                    act();
                    any |= TrailHours < before;
                }
                if (any) names.Add(p.Name);
            }
        }
        return names.Count == 0 ? null : $"{week:MMM d}: your assistant courted {string.Join(", ", names.Take(6))}{(names.Count > 6 ? $" and {names.Count - 6} more" : "")}.";
    }

    /// <summary>
    /// Uncommitted prospects in a class a program could realistically land, best first: with a solid effort (60 interest,
    /// or more if it already has it) its appeal comes within 4 of the best rival's. Assumes it is the coached program.
    /// </summary>
    public List<(Player p, Recruit r)> Fits(Team t, int cls, ClassBoard? board = null, int want = 40)
    {
        board ??= BoardFor(cls);
        var targets = new List<(Player p, Recruit r)>();
        if (board.Open[t.Id] <= 0) return targets; // nothing to offer: no one is a realistic target
        var rivals = Programs.Where(o => o != t && board.Open[o.Id] > 0).ToList();
        foreach (var p in ClassOf(cls).OrderBy(p => Recruits[p.Id].Rank))
        {
            var r = Recruits[p.Id];
            if (r.CommittedTo != null) continue;
            double mine = InterestIn(r, t);
            double ours = Appeal(t, p, r, board) - CourtingEdge(r, t.Id, mine) + CourtingEdge(r, t.Id, Math.Max(60, mine));
            double rival = rivals.Count == 0 ? double.MinValue : rivals.Max(o => Appeal(o, p, r, board));
            if (ours - rival > -4) targets.Add((p, r));
            if (targets.Count >= want) break;
        }
        return targets;
    }

    /// <summary>Tell the coached program about a commitment it cares about: to it, or a prospect it was courting going elsewhere.</summary>
    private void TellCoach(Player p, Recruit r, Team to, bool signingDay = false)
    {
        if (ManagedProgramId is not { } me) return;
        string who = $"{StarText(r.Stars)} {p.PrimaryPosition} {p.Name} (No. {r.Rank}, {r.HighSchool}, {StateName(r.State)})";
        if (to.Id == me)
            CoachNews.Add(($"{p.Name} commits to you", $"{who} {(signingDay ? "signed with you on signing day" : "has committed to you")}. He enrolls in the summer of {r.ClassOf}."));
        else if (r.CourtedBy == me && (r.Offered || r.OnBoard))
            CoachNews.Add(($"{p.Name} picks {to.FullName}", $"{who}, whom you were recruiting{(r.Offered ? " with a scholarship offer" : "")}, {(signingDay ? "signed with" : "has committed to")} {to.FullName}. Interest in you was {r.Interest:0}."));
    }
}
