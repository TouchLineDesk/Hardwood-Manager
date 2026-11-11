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

    public string BoardProspect(Player p) => Trail(p, (c, r) => c.ToggleBoard(HumanTeam, p, r));
    public string ScoutProspect(Player p) => Trail(p, (c, r) => c.Scout(HumanTeam, p, r, Today));
    public string CallProspect(Player p) => Trail(p, (c, r) => c.Call(HumanTeam, p, r, Today));
    public string HomeVisitProspect(Player p) => Trail(p, (c, r) => c.VisitHome(HumanTeam, p, r, Today));
    public string OfficialVisitProspect(Player p) => Trail(p, (c, r) => c.OfficialVisit(HumanTeam, p, r, Today));
    public string OfferProspect(Player p) => Trail(p, (c, r) => c.Offer(HumanTeam, p, r, Today));

    /// <summary>Monday: fade your effort, let the assistant spend last week's spare hours if he recruits for you, refill the hours.</summary>
    internal void WeeklyTrail()
    {
        if (!TrailOpen) return;
        if (TrailWorld!.NewTrailWeek(HumanTeam, Today, Delegated.Recruiting) is { } did) Delegated.LastRecruiting = did;
    }

    /// <summary>Commitments the coached program cares about become messages (to you, or a target going elsewhere).</summary>
    private void PostCoachNews()
    {
        if (World.College is not { } c || c.CoachNews.Count == 0) return;
        if (TrailOpen)
            foreach (var (title, body) in c.CoachNews) Post(title, body, "Roster");
        c.CoachNews.Clear();
    }
}
