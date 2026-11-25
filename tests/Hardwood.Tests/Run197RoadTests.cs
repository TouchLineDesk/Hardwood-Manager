using System.Text.Json;
using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Slice 7 of the revised launch plan (run 197): recruiting on the road.</summary>
public class Run197RoadTests
{
    private readonly ITestOutputHelper _out;
    public Run197RoadTests(ITestOutputHelper output) => _out = output;

    private static CollegeWorld NewWorld(ulong seed)
    {
        var gen = new PlayerGenerator(new Rng(seed ^ 0x51), 2_000_000);
        return CollegeWorld.Generate(gen, new Rng(seed), new DateOnly(2026, 10, 1));
    }

    [Fact]
    public void The_calendar_has_four_dated_events_in_order_each_with_its_choices()
    {
        var days = Enum.GetValues<RoadEvent>().Select(e => CollegeWorld.RoadDay(e, 2028)).ToList();
        Assert.Equal(days.OrderBy(d => d), days);
        Assert.All(days, d => Assert.Equal(2027, d.Year));
        Assert.True(days[0] > CollegeWorld.SigningDay(2027));
        Assert.Equal(DayOfWeek.Friday, days[3].DayOfWeek);
        Assert.Equal(GameSession.MidnightMadnessDay(2027), days[3]);
        Assert.Equal(CollegeWorld.RoadRegions, CollegeWorld.RoadOptions(RoadEvent.SpringEvaluation));
        Assert.Equal(new[] { CollegeWorld.EliteCamp, CollegeWorld.TeamCamp }, CollegeWorld.RoadOptions(RoadEvent.Camp));
        Assert.Equal("Apr 24–Apr 26", CollegeWorld.RoadWhen(RoadEvent.SpringEvaluation, 2028));
    }

    [Fact]
    public void Each_event_acts_only_on_the_coached_programs_courting_and_runs_once()
    {
        var c = NewWorld(41);
        var t = c.Programs.OrderByDescending(p => c.Prestige[p.Id]).Skip(60).First();
        int cls = c.RecruitingClass(new DateOnly(2027, 4, 26));
        var others = string.Join(",", c.Recruits.Values.Select(r => $"{r.CommittedTo}|{r.CourtedBy == null}"));

        // A region choice; a bad choice is refused; the day passes and it locks.
        var before = new DateOnly(2027, 4, 20);
        Assert.Contains("isn't a choice", c.ChooseRoad(t, RoadEvent.SpringEvaluation, "Mars", before));
        Assert.Contains("West", c.ChooseRoad(t, RoadEvent.SpringEvaluation, "West", before));
        var msg = c.RunRoad(t, RoadEvent.SpringEvaluation, cls, new DateOnly(2027, 4, 26))!.Value;
        _out.WriteLine(msg.Title + "\n" + msg.Body);
        Assert.StartsWith("You chose the West", msg.Body);
        var seen = c.Recruits.Values.Where(r => r.CourtedBy == t.Id && r.Scouted).ToList();
        Assert.InRange(seen.Count, 1, CollegeWorld.EvaluationLooks);
        Assert.All(seen, r => Assert.Equal("West", CollegeWorld.RegionOf(r.State)));
        Assert.All(seen, r => Assert.Equal(cls, r.ClassOf));
        Assert.Null(c.RunRoad(t, RoadEvent.SpringEvaluation, cls, new DateOnly(2027, 4, 26))); // once
        Assert.Contains("is over", c.ChooseRoad(t, RoadEvent.SpringEvaluation, "Midwest", new DateOnly(2027, 4, 27)));

        // Nobody committed, and no other program's courting moved.
        Assert.Equal(others, string.Join(",", c.Recruits.Values.Select(r => $"{r.CommittedTo}|{r.CourtedBy == null || r.CourtedBy == t.Id}")));

        // The elite camp brings the board's best; left unpicked, the staff choose (4+ on the board: the elite camp).
        var board = c.ClassOf(cls).Select(p => (p, r: c.Recruits[p.Id])).Where(x => x.r.CommittedTo == null).OrderBy(x => x.r.Rank).Skip(100).Take(6).ToList();
        foreach (var (p, r) in board) { CollegeWorld.Courting(r, t); r.OnBoard = true; }
        var interest = board.Select(x => x.r.Interest).ToList();
        var camp = c.RunRoad(t, RoadEvent.Camp, cls, CollegeWorld.RoadDay(RoadEvent.Camp, cls))!.Value;
        _out.WriteLine(camp.Title + "\n" + camp.Body);
        Assert.StartsWith("Your staff chose elite camp", camp.Body);
        Assert.True(c.RoadEntry(t.Id, cls, RoadEvent.Camp)!.ByStaff);
        Assert.All(board.Select((x, i) => (x, i)), q => Assert.True(q.x.r.Interest > interest[q.i] && q.x.r.Scouted));

        // The visit weekend: offered seniors take their official visit, free of hours.
        foreach (var (p, r) in board.Take(3)) r.Offered = true;
        int hours = c.HoursLeft(CollegeWorld.RoadDay(RoadEvent.VisitWeekend, cls));
        c.ChooseRoad(t, RoadEvent.VisitWeekend, CollegeWorld.BringLikely, new DateOnly(2027, 9, 1));
        var visit = c.RunRoad(t, RoadEvent.VisitWeekend, cls, CollegeWorld.RoadDay(RoadEvent.VisitWeekend, cls))!.Value;
        _out.WriteLine(visit.Title + "\n" + visit.Body);
        Assert.All(board.Take(3), x => Assert.True(x.r.OfficialVisit));
        Assert.False(board[3].r.OfficialVisit);
        Assert.Equal(hours, c.HoursLeft(CollegeWorld.RoadDay(RoadEvent.VisitWeekend, cls)));
    }

    [Fact]
    public void A_college_career_gets_the_calendar_picks_survive_a_reload_and_the_summer_events_run_at_the_new_season()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run197-{Guid.NewGuid():N}.hwsave");
        try
        {
            var (_, world0) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
            int id = world0.College!.Programs.First(p => p.FullName.StartsWith("George Mason")).Id;
            var s = GameSession.NewGame(9, id, path);
            var c = s.World.College!;

            // Day one: the Class of 2027's spring and summer were before the job; the visit weekend is still to come.
            var cal = s.RoadCalendar();
            _out.WriteLine(s.Today + ": " + string.Join(" | ", cal.Select(r => $"{r.Title} {r.When} open={r.Open}")));
            Assert.Equal(4, cal.Count);
            Assert.False(cal[0].Open);
            Assert.True(cal[3].Open);
            Assert.Contains("Midnight Madness", s.ChooseRoad(RoadEvent.VisitWeekend, CollegeWorld.BringStars));

            int guard = 0;
            var weekend = CollegeWorld.RoadDay(RoadEvent.VisitWeekend, 2027);
            while (s.Today <= weekend && guard++ < 60) s.AdvanceDay();
            var visit = s.Inbox.Single(m => m.Title.StartsWith("Midnight Madness visit weekend"));
            _out.WriteLine(visit.Title + "\n" + visit.Body);
            Assert.StartsWith("You chose the stars", visit.Body);

            // The day after signing day: next class's calendar. Pick the spring evaluation, then save and reload.
            while (s.Today <= new DateOnly(2027, 4, 16) && guard++ < 400) s.AdvanceDay();
            Assert.Single(s.Inbox, m => m.Title == "On the road: the Class of 2028");
            s.ChooseRoad(RoadEvent.SpringEvaluation, "Southeast");
            s.ChooseRoad(RoadEvent.Camp, CollegeWorld.TeamCamp);
            s.SaveNow();
            var back = GameSession.Load(path);
            Assert.Equal(JsonSerializer.Serialize(c.Road), JsonSerializer.Serialize(back.World.College!.Road));
            s = back; c = s.World.College!;

            while (s.Today <= new DateOnly(2027, 4, 26) && s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            var spring = s.Inbox.Single(m => m.Title.StartsWith("Spring evaluation period"));
            _out.WriteLine(spring.Title + "\n" + spring.Body);
            Assert.StartsWith("You chose the Southeast", spring.Body);

            // The summer: the date stands still, then jumps to the new season; the camp and July run on the way.
            while (s.Phase != SessionPhase.Offseason && guard++ < 500) s.AdvanceDay();
            Assert.True(s.RoadCalendar().Single(r => r.Event == RoadEvent.JulyEvaluation).Open);
            while (s.Phase == SessionPhase.Offseason && guard++ < 1100) s.AdvanceOffseason();
            _out.WriteLine($"{s.Today} {s.Phase} employed {s.Employed}");
            if (!s.Employed || !s.TrailOpen) return; // sacked over the summer: nothing more to check
            var campMsg = s.Inbox.Single(m => m.Title.StartsWith("Summer camp"));
            var july = s.Inbox.Single(m => m.Title.StartsWith("July evaluation period"));
            _out.WriteLine(campMsg.Title + "\n" + campMsg.Body + "\n" + july.Title + "\n" + july.Body);
            Assert.StartsWith("You chose team camp", campMsg.Body);
            Assert.StartsWith("Your staff chose", july.Body);
            Assert.All(s.RoadCalendar().Take(3), r => Assert.False(r.Open));
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
