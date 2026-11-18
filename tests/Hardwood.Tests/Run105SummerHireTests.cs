using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 105. Bug hunt H80: a coach hired in the summer (fired, re-hired by Providence City on May 4 2028) was told
/// "Expectations for 2027-28", the season just finished, and welcomed to "the Providence City".
/// </summary>
public class Run105SummerHireTests
{
    private readonly ITestOutputHelper _out;
    public Run105SummerHireTests(ITestOutputHelper o) => _out = o;

    private static string SeasonLabel(int y) => $"{y}-{(y + 1) % 100:00}";

    /// <summary>Fired in the summer (seed 9), applies, and holds an offer while the summer goes on.</summary>
    private static (GameSession s, int finished, int teamId) SummerOffer()
    {
        var probe = GameSession.NewGame(9, 1, null);
        var weakest = probe.League.Teams.OrderBy(t => probe.JobPrestige(t)).First().Id;
        var coach = new Coach { FirstName = "Danny", LastName = "Garrett", Background = CoachBackground.ExPro, Licence = CoachLicence.Pro, Reputation = 90 };
        var s = GameSession.NewGame(9, weakest, null, coach: coach);
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 700) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        int finished = s.League.Season;
        if (s.Employed) s.Fire();
        foreach (var t in s.League.Teams.Where(t => t != s.HumanTeam).OrderByDescending(t => s.JobPrestige(t)).Take(4)) s.CoachLeaves(t, "fired");
        foreach (var v in s.Job.Vacancies.Where(v => v.Status == "open").Take(4).ToList()) Assert.Null(s.Apply(v.TeamId));
        if (s.Offseason!.Phase == OffseasonPhase.Draft && !s.Offseason.DraftComplete) s.Offseason.AutoDraftUntil(null);
        s.AdvanceOffseason();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        var offer = s.Job.Vacancies.First(v => v.Status == "offered");
        Assert.True(s.ClubSeasonOver(s.FindTeam(offer.TeamId)!));
        return (s, finished, offer.TeamId);
    }

    [Fact]
    public void A_summer_hire_gets_the_coming_seasons_goal_and_it_is_posted_once()
    {
        var (s, finished, teamId) = SummerOffer();
        int guard = 0;
        int before = s.Inbox.Count;
        Assert.Null(s.AcceptOffer(teamId));
        var hired = s.Inbox.Take(s.Inbox.Count - before).ToList(); // newest first
        foreach (var m in hired) _out.WriteLine($"{m.Title}: {m.Body}");

        // The goal is for next season, and the board's first season of patience is next season too.
        Assert.Equal(finished + 1, s.Job.Current!.Season);
        Assert.Equal(finished + 1, s.Job.HiredSeason);
        Assert.Contains(hired, m => m.Title == $"Expectations for {SeasonLabel(finished + 1)}");
        Assert.DoesNotContain(hired, m => m.Title == $"Expectations for {SeasonLabel(finished)}");
        Assert.Contains(hired, m => m.Title == $"Welcome to {s.HumanTeam.TheFullName}");
        var goal = (s.Job.Current.Goal, s.Job.Current.TargetWins);

        // Into the new season: the goal stands, and isn't posted a second time.
        while (s.Phase == SessionPhase.Offseason && guard++ < 800)
        {
            if (s.Offseason!.Phase == OffseasonPhase.Draft && !s.Offseason.DraftComplete) s.Offseason.AutoDraftUntil(null);
            s.AdvanceOffseason();
        }
        Assert.Equal(SessionPhase.Preseason, s.Phase);
        Assert.Equal(finished + 1, s.League.Season);
        Assert.True(s.Employed);
        Assert.Equal(s.League.Season, s.Job.Current!.Season);
        Assert.Equal(goal, (s.Job.Current.Goal, s.Job.Current.TargetWins));
        Assert.Single(s.Inbox, m => m.Title == $"Expectations for {SeasonLabel(s.League.Season)}");
    }

    [Fact]
    public void A_summer_hire_who_resigns_before_a_game_has_no_record_at_the_club()
    {
        var (s, finished, teamId) = SummerOffer();
        Assert.Null(s.AcceptOffer(teamId));
        Assert.Null(s.Resign());
        var spell = s.Coach.History.Last();
        Assert.Equal(teamId, spell.TeamId);
        Assert.Equal((finished + 1, finished + 1, 0, 0), (spell.FromSeason, spell.ToSeason, spell.Wins, spell.Losses));
    }

    [Fact]
    public void An_in_season_hire_is_still_hired_for_the_current_season()
    {
        var s = GameSession.NewGame(9, 1, null);
        for (int i = 0; i < 30; i++) s.AdvanceDay();
        Assert.Equal(SessionPhase.RegularSeason, s.Phase);
        Assert.False(s.ClubSeasonOver(s.League.Teams[5]));
        Assert.Equal(s.League.Season, s.Job.HiredSeason);
    }

    [Theory]
    [InlineData("Providence", "City", "Providence City", "Providence City")]
    [InlineData("Charlotte", "FC", "Charlotte FC", "Charlotte FC")]
    [InlineData("Baltimore", "Basketball Club", "Baltimore Basketball Club", "Baltimore Basketball Club")]
    [InlineData("Boston", "Celtics", "the Boston Celtics", "The Boston Celtics")]
    [InlineData("Miami", "Heat", "the Miami Heat", "The Miami Heat")]
    public void Club_style_names_take_no_article(string city, string name, string inSentence, string atStart)
    {
        var t = new Team { City = city, Name = name, Abbreviation = "XXX", Division = "D" };
        Assert.Equal(inSentence, t.TheFullName);
        Assert.Equal(atStart, t.TheFullNameCap);
    }
    [Fact]
    public void H67_an_owner_below_his_reserve_says_he_funds_nothing_until_the_bank_recovers()
    {
        var s = GameSession.NewGame(4, 3, null);
        Assert.True(s.HasFacilities);
        var t = s.HumanTeam;
        t.Money.Balance = Franchise.Reserve(t) / 3;
        Assert.Equal(0, s.FacilityBudget);
        var f = s.MyFacilities;
        f.Proposals = Franchise.Candidates(t, s.MyLeagueRevenue, 18000, s.League.Season, 1_000_000_000).Take(1).ToList();
        Assert.NotEmpty(f.Proposals);
        var why = s.ApproveFacility(0)!;
        _out.WriteLine(why);
        Assert.Contains("funds nothing until the bank", why);
        Assert.Contains(Franchise.Money(t.Money.Balance), why);
        Assert.DoesNotContain("keeps in the bank", why);
    }
}
