using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 97. Bug hunt H75: out of work in the summer, applications went unanswered until October (the daily market runs
/// only on game days). H76: a college player "requests a trade" and is told to sell him, which a program can't do.
/// </summary>
public class Run97SummerJobTests
{
    private readonly ITestOutputHelper _out;
    public Run97SummerJobTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Out_of_work_in_the_summer_applications_are_answered_at_each_step_and_offers_last_into_the_season()
    {
        var probe = GameSession.NewGame(9, 1, null);
        var weakest = probe.League.Teams.OrderBy(t => probe.JobPrestige(t)).First().Id;
        var coach = new Coach { FirstName = "Danny", LastName = "Garrett", Background = CoachBackground.ExPro, Licence = CoachLicence.Pro, Reputation = 90 };
        var s = GameSession.NewGame(9, weakest, null, coach: coach);
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 700) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        if (s.Employed) s.Fire();
        Assert.False(s.Employed);
        // Make sure there's something to apply for: a few clubs change coach.
        foreach (var t in s.League.Teams.Where(t => t != s.HumanTeam).OrderByDescending(t => s.JobPrestige(t)).Take(4)) s.CoachLeaves(t, "fired");
        var open = s.Job.Vacancies.Where(v => v.Status == "open").ToList();
        Assert.NotEmpty(open);
        var picks = open.Take(4).Select(v => v.TeamId).ToList();
        foreach (var id in picks) Assert.Null(s.Apply(id));
        Assert.Contains(s.Inbox, m => m.Title.StartsWith("Application sent") && m.Body.Contains("next step of the summer"));

        var step = s.Offseason!.Phase;
        if (s.Offseason.Phase == OffseasonPhase.Draft && !s.Offseason.DraftComplete) s.Offseason.AutoDraftUntil(null);
        s.AdvanceOffseason();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        var mine = s.Job.Vacancies.Where(v => picks.Contains(v.TeamId)).ToList();
        _out.WriteLine($"After {step}: " + string.Join(", ", mine.Select(v => $"{s.FindTeam(v.TeamId)!.Abbreviation} {v.Status}")));
        Assert.DoesNotContain(mine, v => v.Status == "applied");           // H75: answered at the step, not in October
        Assert.Contains(mine, v => v.Status == "offered");                 // reputation 90: someone wants him
        Assert.Contains(s.Inbox, m => m.Title.StartsWith("Job offer: ") && m.Body.Contains("before the new season starts"));

        // Leave the offers standing through the summer: they last a week into the new season.
        while (s.Phase == SessionPhase.Offseason && guard++ < 800)
        {
            if (s.Offseason!.Phase == OffseasonPhase.Draft && !s.Offseason.DraftComplete) s.Offseason.AutoDraftUntil(null);
            s.AdvanceOffseason();
        }
        Assert.Equal(SessionPhase.Preseason, s.Phase);
        var offer = s.Job.Vacancies.First(v => v.Status == "offered");
        Assert.Equal(s.Today.AddDays(7), offer.DecideOn);
        s.AdvanceDay();
        Assert.Equal("offered", offer.Status);                             // not lapsed on the first day
        Assert.Null(s.AcceptOffer(offer.TeamId));
        Assert.True(s.Employed);
        for (int i = 0; i < 5; i++) s.AdvanceDay();
        Assert.True(s.Employed);
    }

    [Fact]
    public void A_college_player_who_wants_out_is_thinking_of_transferring_and_can_only_be_talked_round()
    {
        var p = new Player { FirstName = "John", LastName = "Mercer", TradeRequested = true };
        Assert.Equal(new[] { TalkTopic.ConvinceToStay, TalkTopic.AskWhatsWrong }, Talks.Available(p, college: true));
        Assert.Contains(TalkTopic.PromiseTrade, Talks.Available(p));       // the pros are unchanged
        var T = new Team { City = "Clemson", Name = "Tigers", Abbreviation = "CLEM", Division = "ACC" };
        var e = new PeopleEvent(p, T, PeopleEventKind.TradeRequest, "pro text");
        var text = GameSession.CollegeWording(e);
        Assert.Contains("thinking of transferring", text);
        Assert.DoesNotContain("Transactions", text);
        Assert.DoesNotContain("trade", text);
        var settled = GameSession.CollegeWording(new PeopleEvent(p, T, PeopleEventKind.Settled, "John Mercer has withdrawn his trade request and will see out the season."));
        Assert.DoesNotContain("trade", settled);
        Assert.Equal("ok", GameSession.CollegeWording(new PeopleEvent(p, T, PeopleEventKind.WantsTalk, "ok")));
    }
}
