using Hardwood.Core.College;

namespace Hardwood.Core.Season;

/// <summary>One road event on the Recruiting tab (run 197).</summary>
public sealed record RoadRow(RoadEvent Event, string Title, string When, IReadOnlyList<string> Options, string? Choice, string? Result,
                             bool Open, bool ByStaff, string Help);

/// <summary>Recruiting on the road (run 197, slice 7 of the revised launch plan): see <see cref="CollegeWorld"/>'s CollegeRoad.cs.</summary>
public sealed partial class GameSession
{
    /// <summary>The recruiting class's four road events, in date order (empty outside the trail).</summary>
    public List<RoadRow> RoadCalendar()
    {
        if (!TrailOpen) return new();
        var c = TrailWorld!;
        int cls = c.RecruitingClass(Today);
        return Enum.GetValues<RoadEvent>().Select(e =>
        {
            var entry = c.RoadEntry(HumanTeam.Id, cls, e);
            bool open = entry?.Result == null && Today <= CollegeWorld.RoadDay(e, cls);
            string? result = entry?.Result ?? (open ? null : "Not run: it came before your job here (or before this save kept a road calendar).");
            return new RoadRow(e, CollegeWorld.RoadTitle(e), CollegeWorld.RoadWhen(e, cls), CollegeWorld.RoadOptions(e),
                entry?.Choice, result, open, entry?.ByStaff ?? false, CollegeWorld.RoadHelp(e));
        }).ToList();
    }

    /// <summary>The coach's pick for a road event still to come.</summary>
    public string ChooseRoad(RoadEvent e, string choice) =>
        TrailOpen ? TrailWorld!.ChooseRoad(HumanTeam, e, choice, Today) : "Only a college coach goes on the recruiting road.";

    /// <summary>
    /// Each day: the road event due today runs on the coach's pick (or his staff's), and the day after signing day the
    /// next class's calendar is posted. Messages post once (the result is saved), so a reload can't repeat one.
    /// </summary>
    private void RecruitingRoad(CollegeWorld c)
    {
        if (!TrailOpen) return;
        c = TrailWorld!; // marks the coached program
        int cls = c.RecruitingClass(Today);
        if (Today == CollegeWorld.SigningDay(cls - 1).AddDays(1) && c.RoadEntry(HumanTeam.Id, cls, RoadEvent.SpringEvaluation) == null)
        {
            var lines = Enum.GetValues<RoadEvent>().Select(e => $"{CollegeWorld.RoadTitle(e)} ({CollegeWorld.RoadWhen(e, cls)}): {string.Join(" or ", CollegeWorld.RoadOptions(e).Take(2))}{(CollegeWorld.RoadOptions(e).Count > 2 ? ", or another region" : "")}.");
            Post($"On the road: the Class of {cls}",
                $"The recruiting calendar for the Class of {cls} is set. Pick where your staff go on the Recruiting tab; anything you leave open, your staff decide on the day.\n"
                + string.Join("\n", lines), "Roster");
        }
        RunRoad(c, Today, Today);
    }

    /// <summary>
    /// Runs the road events dated from <paramref name="from"/> to <paramref name="to"/>. Daily that is today; the college
    /// summer holds the date still (about May 8) and then jumps to the new season, so the jump runs the June camp and the
    /// July evaluation period it skipped, on the coach's picks made over the summer.
    /// </summary>
    private void RunRoad(CollegeWorld c, DateOnly from, DateOnly to)
    {
        foreach (int cls in new[] { c.RecruitingClass(from), c.RecruitingClass(to) }.Distinct())
            foreach (var e in Enum.GetValues<RoadEvent>())
            {
                var day = CollegeWorld.RoadDay(e, cls);
                if (day < from || day > to) continue;
                if (c.RunRoad(HumanTeam, e, cls, day) is { } msg) Post(msg.Title, msg.Body, "Roster");
            }
    }

    /// <summary>The summer's jump to the new season (see <see cref="RunRoad(CollegeWorld, DateOnly, DateOnly)"/>).</summary>
    private void RoadOverSummer(DateOnly summer, DateOnly newYear)
    {
        if (!TrailOpen || newYear <= summer) return;
        // From the summer's own first day: the day the season ended rolls into the summer without its calendar running.
        RunRoad(TrailWorld!, summer, newYear.AddDays(-1));
    }
}
