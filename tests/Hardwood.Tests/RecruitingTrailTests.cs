using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Generation;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>College slice C3, the recruiting trail (decisions #173-#178): coach hours, interest, offers and commitments.</summary>
public class RecruitingTrailTests
{
    private static int MidProgram(ulong seed)
    {
        var league = new WorldGenerator(new Rng(seed)).Generate("North American Basketball League", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, seed, GameSession.DefaultFullSim, new DateOnly(2026, 10, 1));
        var cw = world.College!;
        return cw.Programs.OrderByDescending(p => cw.Prestige[p.Id]).ElementAt(50).Id;
    }

    [Fact]
    public void Hours_buy_interest_and_an_offer_is_needed_to_commit()
    {
        var s = GameSession.NewGame(9, MidProgram(9), null);
        Assert.True(s.TrailOpen);
        Assert.True(s.Delegated.Recruiting); // a college coach starts with his assistant using spare hours (#176)
        var c = s.TrailWorld!;
        var me = s.HumanTeam;
        Assert.Equal(CollegeWorld.WeeklyHours, s.TrailHoursLeft);

        var fit = c.Fits(me, c.SeniorClass, want: 5);
        Assert.NotEmpty(fit);
        var (p, r) = fit[0];
        Assert.Contains("Scouted", s.ScoutProspect(p));
        Assert.True(r.Scouted && r.OnBoard && r.CourtedBy == me.Id);
        Assert.Contains("called", s.CallProspect(p));
        Assert.Contains("already called", s.CallProspect(p)); // once a week
        s.HomeVisitProspect(p);
        s.OfficialVisitProspect(p); // seniors: official visits open on September 1, before the career starts
        Assert.True(r.HomeVisit && r.OfficialVisit);
        Assert.Equal(CollegeWorld.WeeklyHours - 1 - 1 - 4 - 8, s.TrailHoursLeft);
        Assert.InRange(r.Interest, 20, 36); // 5 + 12 + 18, less for a 4- or 5-star

        // No offer yet: he can't be leaning to (or commit to) you, however much he likes you.
        r.Interest = 100;
        Assert.NotEqual(me, c.LeaningTo(p, r));
        Assert.Contains("can now commit", s.OfferProspect(p));
        Assert.Equal(me, c.LeaningTo(p, r)); // 45 points of appeal from 100 interest: a mid-major beats everyone for a fit
        Assert.Contains("you have", s.OfficialVisitProspect(fit[1].p) + s.HomeVisitProspect(fit[1].p) + s.HomeVisitProspect(fit[2].p) + s.OfferProspect(fit[3].p) + s.HomeVisitProspect(fit[4].p));

        // Play on: a prospect who prefers you with an offer commits during the season or on signing day.
        var signing = CollegeWorld.SigningDay(r.ClassOf);
        while (s.Today <= signing && r.CommittedTo == null) s.AdvanceDay();
        Assert.Equal(me.Id, r.CommittedTo);
        Assert.Contains(s.Inbox, m => m.Title == $"{p.Name} commits to you");
    }

    [Fact]
    public void The_assistant_recruits_a_class_and_the_offer_limit_holds()
    {
        var s = GameSession.NewGame(11, MidProgram(11), null);
        var c = s.TrailWorld!;
        var me = s.HumanTeam;
        int cls = c.SeniorClass;
        var signing = CollegeWorld.SigningDay(cls);
        while (s.Today <= signing) s.AdvanceDay();
        var ours = c.ClassOf(cls).Select(p => c.Recruits[p.Id]).Where(r => r.CommittedTo == me.Id).ToList();
        // Every scholarship that opens is filled by signing day, and every recruit was offered (or committed before you arrived).
        Assert.Equal(0, c.OpenScholarships(me, cls));
        Assert.All(ours, r => Assert.True(r.Offered || r.CommittedOn < s.Job.HiredOn.AddDays(1)));
        Assert.True(c.OffersOut(me, cls) <= c.OfferLimit(me, cls) + 3);
        Assert.False(string.IsNullOrEmpty(s.Delegated.LastRecruiting));
        // The national picture holds: 20-30 five-stars a class (the C1 measure), all signed somewhere.
        Assert.InRange(c.ClassOf(cls).Count(p => c.Recruits[p.Id].Stars == 5), 20, 30);
    }

    [Fact]
    public void Courting_survives_a_save_and_older_saves_load_without_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-trail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var s = GameSession.NewGame(13, MidProgram(13), path);
            var c = s.TrailWorld!;
            var (p, r) = c.Fits(s.HumanTeam, c.SeniorClass, want: 1)[0];
            s.ScoutProspect(p); s.OfferProspect(p);
            int left = s.TrailHoursLeft;
            s.SaveNow();
            var back = GameSession.Load(path);
            var r2 = back.TrailWorld!.Recruits[p.Id];
            Assert.True(r2.Scouted && r2.Offered);
            Assert.Equal(s.HumanTeam.Id, r2.CourtedBy);
            Assert.Equal(r.Interest, r2.Interest, 3);
            Assert.Equal(left, back.TrailHoursLeft);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        // A v0.43 recruit (no trail fields) reads with nothing courted.
        var old = System.Text.Json.JsonSerializer.Deserialize<Recruit>("""{"HighSchool":"Lincoln","State":"OH","ClassOf":2027,"Stars":4,"Rank":40,"Projection":120,"Bias":2}""")!;
        Assert.Null(old.CourtedBy);
        Assert.False(old.Offered);
        Assert.Equal(0, old.Interest);
    }
}
