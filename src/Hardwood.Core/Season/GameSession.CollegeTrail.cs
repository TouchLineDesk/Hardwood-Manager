using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>The recruiting trail for a college coach (slice C3, decisions #173-#178): see <see cref="CollegeWorld"/>'s CollegeTrail.cs.</summary>
public sealed partial class GameSession
{
    /// <summary>You coach a program in the recruiting model, so you recruit high-schoolers yourself.</summary>
    public bool TrailOpen => Employed && InCollege && World.College is { Recruiting: true };

    public int TrailHoursLeft => TrailWorld is { } c ? c.HoursLeft(Today) : 0;

    /// <summary>The college world, with your program marked as the coached one (the mark isn't saved; it decides who can take whom).</summary>
    public CollegeWorld? TrailWorld
    {
        get
        {
            if (World.College is { } c) c.ManagedProgramId = Employed && InCollege ? HumanTeam.Id : null;
            return World.College;
        }
    }

    private string Trail(Player p, Func<CollegeWorld, Recruit, string> act)
    {
        if (!TrailOpen) return InCollege ? "This career's college world predates high-school recruiting." : "Only a college coach recruits high-schoolers.";
        var c = TrailWorld!;
        if (c.RecruitOf(p) is not { } r) return $"{p.Name} isn't a high-school prospect.";
        var reply = act(c, r);
        return reply;
    }

    public string BoardProspect(Player p) => Trail(p, (c, r) => c.ToggleBoard(HumanTeam, p, r, Today));
    public string ScoutProspect(Player p) => Trail(p, (c, r) => c.Scout(HumanTeam, p, r, Today));
    public string CallProspect(Player p) => Trail(p, (c, r) => c.Call(HumanTeam, p, r, Today));
    public string HomeVisitProspect(Player p) => Trail(p, (c, r) => c.VisitHome(HumanTeam, p, r, Today));
    public string OfficialVisitProspect(Player p) => Trail(p, (c, r) => c.OfficialVisit(HumanTeam, p, r, Today));
    public string OfferProspect(Player p) => Trail(p, (c, r) => c.Offer(HumanTeam, p, r, Today));
    /// <summary>K15a (run 146): what your calls and visits sell him (null: no pitch).</summary>
    public string PitchProspect(Player p, RecruitPriority? pitch) => Trail(p, (c, r) => c.SetPitch(HumanTeam, p, r, pitch, Today));

    /// <summary>K15b (run 147): promise him a starting role as a freshman.</summary>
    public string PromiseProspect(Player p) => Trail(p, (c, r) => c.Promise(HumanTeam, p, r, Today));

    /// <summary>
    /// K15b (run 147): from mid-January of a freshman's first season, the starting-role promise his recruiters made is
    /// judged on his own numbers: at least half his games started, or <see cref="CollegeWorld.PromiseMpg"/> minutes a game.
    /// Kept, he trusts you; broken, he feels lied to and the trail hears (<see cref="CollegeWorld.BrokenWordUntil"/>). A
    /// freshman who has barely played (an injury) doesn't hold it against you. Pledges to a program you no longer coach lapse.
    /// </summary>
    internal void WeeklyPromises()
    {
        if (!InCollege || !Employed || World.College is not { Recruiting: true } c) return;
        var due = new DateOnly(c.Season.Year + 1, 1, 15);
        if (Today < due) return;
        foreach (var pl in c.Pledges.Where(x => x.Status == "pending" && x.Season <= c.Season.Year).ToList())
        {
            var p = HumanTeam.Roster.FirstOrDefault(x => x.Id == pl.PlayerId);
            if (pl.ProgramId != HumanTeam.Id || p == null || pl.Season < c.Season.Year) { pl.Status = "lapsed"; continue; }
            MyStats.TryGetValue(p.Id, out var st);
            int g = st?.Games ?? 0;
            if (g < 6)
            {
                if (Today < due.AddDays(31)) continue;
                pl.Status = "lapsed";
                Post($"Promise on hold: {p.Name}", $"{p.Name} has played only {g} game{(g == 1 ? "" : "s")} this season, so he doesn't hold your promise of a starting role against you.", "Squad");
                continue;
            }
            int starts = st!.Starts;
            // Review: a save from before v0.135.0 has no college starts for the games before it; judge on minutes alone then.
            bool startsKnown = HumanTeam.Roster.Any(x => MyStats.TryGetValue(x.Id, out var y) && y.Starts > 0);
            if ((startsKnown && starts * 2 >= g) || st.Mpg >= CollegeWorld.PromiseMpg)
            {
                pl.Status = "kept";
                p.Morale = Math.Min(100, p.Morale + 8);
                People.PeopleEngine.Boost(p, 10, Today.AddDays(56), "Trusts you after a kept promise", Today);
                Post($"Promise kept: {p.Name}", $"You promised {p.Name} a starting role as a freshman, and he has it: {starts} starts in {g} games, {st.Mpg:0} minutes a game. He trusts you.", "Squad");
            }
            else
            {
                pl.Status = "broken";
                p.Morale = Math.Max(0, p.Morale - 20); p.DistrustUntil = Today.AddDays(90);
                c.BrokenWordUntil = Today.AddDays(365);
                Post($"Promise broken: {p.Name}", $"You promised {p.Name} a starting role as a freshman. By mid-January he has {starts} starts in {g} games and {st.Mpg:0} minutes a game, and he feels lied to. "
                    + $"The recruiting trail has heard too: until {c.BrokenWordUntil:MMMM d, yyyy} your appeal is {CollegeWorld.BrokenWordPenalty:0} lower with every prospect, and your promises won't sway anyone.", "Squad");
            }
        }
    }

    /// <summary>Monday: fade your effort, let the assistant spend last week's spare hours if he recruits for you, refill the hours.</summary>
    internal void WeeklyTrail()
    {
        if (!TrailOpen) return;
        if (TrailWorld!.NewTrailWeek(HumanTeam, Today, Delegated.Recruiting) is { } did) Delegated.LastRecruiting = did;
    }

    /// <summary>CS1 (run 125): a new college coach starts with the class's computer-made commitments back in the pool.</summary>
    internal void HandBackOnHire()
    {
        if (!TrailOpen) return;
        TrailWorld!.HandBackCommitments(HumanTeam, Today);
        PostCoachNews();
    }

    /// <summary>Commitments the coached program cares about become messages (to you, or a target going elsewhere).</summary>
    private void PostCoachNews()
    {
        if (World.College is not { } c || c.CoachNews.Count == 0) return;
        // Recruiting news needs the trail; Selection Sunday (C4 part 1) comes to any college coach.
        // CS4 (run 129): targets who chose another school wait for Monday: they ride in the weekly results message, or
        // arrive together as one "Recruiting" message when there are no results that week (one message each was 15-20
        // messages per three seasons).
        foreach (var (title, body) in c.CoachNews)
            if (title.StartsWith("Selection Sunday")) { if (Employed && InCollege) Post(title, body, "League"); }
            else if (!TrailOpen) { }
            else if (title.Contains(" picks ")) WeekRecruiting.Add(body);
            else Post(title, body, "Roster");
        c.CoachNews.Clear();
    }
}
