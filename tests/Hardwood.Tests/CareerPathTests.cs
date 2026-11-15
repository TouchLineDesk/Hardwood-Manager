using Hardwood.Core;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// The career path (Danny, chat, Nov 15, #651; run 61): resign; step-up openings and offers while in work; applying without
/// leaving; college and pro jobs both ways; and a career that climbs through a summer of offers.
/// </summary>
public class CareerPathTests
{
    private readonly ITestOutputHelper _out;
    public CareerPathTests(ITestOutputHelper output) => _out = output;

    private static string TempSave(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), $"hw-career-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "career.hwsave");
    }

    /// <summary>A coach at the NBA club with the lowest standing as a job, so most others are a step up.</summary>
    private static GameSession WeakNbaCareer(ulong seed, string? path, double reputation = 95)
    {
        var probe = GameSession.NewGame(seed, 1, null);
        var weakest = probe.League.Teams.OrderBy(t => probe.JobPrestige(t)).First().Id;
        var coach = new Coach { FirstName = "Danny", LastName = "Garrett", Background = CoachBackground.ExPro, Licence = CoachLicence.Pro, Reputation = reputation };
        return GameSession.NewGame(seed, weakest, path, coach: coach);
    }

    [Fact]
    public void Resigning_leaves_the_club_to_the_computer_and_it_saves()
    {
        var path = TempSave(out var dir);
        try
        {
            var s = WeakNbaCareer(2026, path, reputation: 40);
            var club = s.HumanTeam;
            for (int i = 0; i < 3; i++) s.AdvanceDay();
            Assert.Null(s.Resign());
            Assert.False(s.Employed);
            Assert.Null(s.ControlledTeamId);
            Assert.Null(club.Coach);
            Assert.True(club.Tactic.Auto);
            Assert.NotEqual("Danny Garrett", club.CoachName);
            Assert.Equal(38, s.Coach.Reputation, 3);
            var rec = Assert.Single(s.Coach.History);
            Assert.Equal("Resigned", rec.Ended);
            Assert.Contains(s.Inbox, m => m.Title == "You resigned" && m.Category == "Board");
            Assert.Equal("You don't have a job to resign from.", s.Resign());

            var loaded = GameSession.Load(path);
            Assert.False(loaded.Employed);
            Assert.Equal("Resigned", loaded.Coach.History.Single().Ended);
            for (int i = 0; i < 3; i++) loaded.AdvanceDay(); // the world goes on without you
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_step_up_opens_while_in_work_you_apply_without_leaving_and_move_when_you_accept()
    {
        var path = TempSave(out var dir);
        try
        {
            var s = WeakNbaCareer(2026, path);
            var club = s.HumanTeam;
            var weaker = s.League.Teams.Where(t => t != club).OrderBy(t => s.JobPrestige(t)).First();
            var bigger = s.League.Teams.OrderByDescending(t => s.JobPrestige(t)).First();
            Assert.False(s.IsStepUp(weaker) && s.JobPrestige(weaker) < s.JobPrestige(club) + GameSession.StepUpMargin);
            Assert.True(s.IsStepUp(bigger));

            // A club no bigger than yours just hires; a bigger one opens to you.
            string before = weaker.CoachName;
            s.CoachLeaves(weaker, "fired");
            Assert.DoesNotContain(s.Job.Vacancies, v => v.TeamId == weaker.Id);
            Assert.NotEqual(before, weaker.CoachName);
            s.CoachLeaves(bigger, "fired");
            var v = Assert.Single(s.Job.Vacancies, x => x.TeamId == bigger.Id);
            Assert.True(v.WhileEmployed);
            Assert.Equal("open", v.Status);
            Assert.Equal("Interim coach", bigger.CoachName);
            Assert.NotEmpty(v.NextCoach);

            Assert.Null(s.Apply(bigger.Id));
            Assert.True(s.Employed);
            Assert.Same(club, s.HumanTeam);
            Assert.Contains(s.Inbox, m => m.Title == $"Application sent: {bigger.FullName}" && m.Body.Contains("You stay with"));
            for (int i = 0; i < 10 && v.Status == "applied"; i++) s.AdvanceDay();
            Assert.Equal("offered", v.Status); // reputation 95: they want you
            Assert.Same(club, s.HumanTeam);
            s.SaveNow();

            var loaded = GameSession.Load(path);
            var lv = loaded.Job.Vacancies.Single(x => x.TeamId == bigger.Id);
            Assert.True(lv.WhileEmployed);
            Assert.Equal(v.NextCoach, lv.NextCoach);
            Assert.Null(loaded.AcceptOffer(bigger.Id));
            Assert.True(loaded.Employed);
            Assert.Equal(bigger.Id, loaded.HumanTeam.Id);
            Assert.Same(loaded.Coach, loaded.HumanTeam.Coach);
            var old = loaded.League.TeamById(club.Id);
            Assert.Null(old.Coach);
            Assert.True(old.Tactic.Auto);
            Assert.Equal($"Left for {bigger.FullName}", loaded.Coach.History.Single().Ended);
            Assert.Contains(loaded.Inbox, m => m.Title == $"Welcome to the {bigger.FullName}");
            for (int i = 0; i < 5; i++) loaded.AdvanceDay();
            var again = GameSession.Load(path);
            Assert.Equal(bigger.Id, again.HumanTeam.Id);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Opening_a_job_to_you_draws_the_same_random_numbers_as_the_computer_hiring()
    {
        // Danny's roadmap principle (#595): a career that never moves plays exactly as before.
        var a = WeakNbaCareer(77, null);
        var b = WeakNbaCareer(77, null);
        var bigger = a.League.Teams.OrderByDescending(t => a.JobPrestige(t)).First();
        var weaker = b.League.Teams.Where(t => t != b.HumanTeam).OrderBy(t => b.JobPrestige(t)).First();
        a.CoachLeaves(bigger, "fired");   // opens to you
        b.CoachLeaves(weaker, "fired");   // the computer hires
        Assert.Contains(a.Job.Vacancies, v => v.TeamId == bigger.Id);
        Assert.Equal(b.NextRngSeed, a.NextRngSeed);
    }

    [Fact]
    public void College_coaches_can_go_to_the_NBA_and_the_G_League_and_pro_coaches_to_college()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(5, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        var program = cw.Programs.OrderBy(p => cw.Prestige[p.Id]).Skip(100).First();
        var coach = new Coach { FirstName = "Danny", LastName = "Garrett", Reputation = 95 };
        var s = GameSession.NewGame(5, program.Id, null, coach: coach);
        Assert.True(s.InCollege);
        var nba = s.League.Teams.OrderByDescending(t => s.JobPrestige(t)).First();
        var gl = s.World.Competitions.First(c => c.Key == UsaPyramid.Tier2).Clubs[0];
        var tier3 = s.World.Competitions.First(c => c.Key == UsaPyramid.Tier3).Clubs[0];
        Assert.True(s.EligibleFor(nba));
        Assert.True(s.EligibleFor(gl));
        Assert.False(s.EligibleFor(tier3));
        Assert.True(s.IsStepUp(nba));

        s.CoachLeaves(nba, "fired");
        Assert.Contains(s.Job.Vacancies, v => v.TeamId == nba.Id && v.WhileEmployed);
        Assert.Null(s.Apply(nba.Id));
        for (int i = 0; i < 10 && s.Job.Vacancies.Single(v => v.TeamId == nba.Id).Status == "applied"; i++) s.AdvanceDay();
        Assert.Null(s.AcceptOffer(nba.Id));
        Assert.False(s.InCollege);
        Assert.True(s.InNabl);
        Assert.Same(s.Coach, nba.Coach);
        Assert.Null(program.Coach);
        Assert.Null(s.World.College!.ManagedProgramId);
        for (int i = 0; i < 5; i++) s.AdvanceDay();

        // And back: a pro coach can take a college program.
        var big = cw.Programs.OrderByDescending(p => cw.Prestige[p.Id]).First();
        Assert.True(s.EligibleFor(big));
        Assert.Null(s.Resign());
        s.Job.Vacancies.Add(new Vacancy { TeamId = big.Id, Opened = s.Today, Status = "offered", DecideOn = s.Today.AddDays(7) });
        Assert.Null(s.AcceptOffer(big.Id));
        Assert.True(s.InCollege);
        Assert.True(s.Delegated.Recruiting);
        Assert.Equal(big.Id, s.World.College!.ManagedProgramId);
        for (int i = 0; i < 5; i++) s.AdvanceDay();
    }

    /// <summary>
    /// "Tests through a bot career that climbs" (#651): a coach who beats his target at the weakest NBA club plays the season;
    /// in the summer the carousel (pro and college) opens bigger jobs to him, clubs come to him, he takes the biggest offer,
    /// and the summer and the next season go on at his new club.
    /// </summary>
    [Fact]
    public void A_career_that_beats_expectations_gets_offers_in_the_summer_and_climbs()
    {
        var s = WeakNbaCareer(31, null);
        var start = s.HumanTeam;
        int guard = 0;
        while (s.Phase != SessionPhase.Postseason && s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
        s.Job.Current!.TargetWins = 0; // whatever the record, he beat it
        var collegeCoaches = s.World.College!.Programs.ToDictionary(p => p.Id, p => p.CoachName);
        while (s.Phase != SessionPhase.Offseason && guard++ < 600) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        Assert.True(s.Employed, "a coach who beat his target isn't sacked");

        var mine = s.Job.Vacancies.Where(v => v.WhileEmployed).ToList();
        _out.WriteLine($"Summer: {mine.Count} step-up openings, {mine.Count(v => v.Status == "offered")} offers; college {mine.Count(v => s.IsCollegeProgram(s.FindTeam(v.TeamId)!))}");
        Assert.NotEmpty(mine);
        Assert.InRange(mine.Count, 1, GameSession.MaxOpenings);
        Assert.True(mine.Count(v => s.IsCollegeProgram(s.FindTeam(v.TeamId)!)) <= GameSession.MaxCollegeOpenings);
        // From the NBA, a step up is another NBA club or a college program, never a lower tier or a club abroad.
        Assert.All(mine, v => Assert.True(s.League.Teams.Any(t => t.Id == v.TeamId) || s.IsCollegeProgram(s.FindTeam(v.TeamId)!)));
        var offers = mine.Where(v => v.Status == "offered").ToList();
        Assert.InRange(offers.Count, 1, GameSession.MaxApproaches);
        Assert.All(offers, v => Assert.True(v.Approach));
        Assert.Contains(s.Inbox, m => m.Category == "Board" && (m.Title.StartsWith("Job offer: ") || m.Title.EndsWith(" job offers")));
        // The college carousel ran: some programs changed coach.
        int collegeChanges = s.World.College!.Programs.Count(p => p.CoachName != collegeCoaches[p.Id]);
        _out.WriteLine($"College programs that changed coach: {collegeChanges}");
        Assert.InRange(collegeChanges, 5, 80);

        var best = offers.Select(v => s.FindTeam(v.TeamId)!).OrderByDescending(t => s.JobPrestige(t)).First();
        Assert.Null(s.AcceptOffer(best.Id));
        Assert.Same(best, s.HumanTeam);
        Assert.Null(start.Coach);
        Assert.DoesNotContain(s.LiveVacancies, v => v.WhileEmployed); // the rest closed when he accepted
        _out.WriteLine($"Moved: {start.FullName} → {best.FullName} ({s.ContextOf(best).Name})");

        while (s.Phase == SessionPhase.Offseason && guard++ < 700)
        {
            if (s.Offseason!.Phase == OffseasonPhase.Draft && !s.Offseason.DraftComplete) s.Offseason.AutoDraftUntil(null);
            s.AdvanceOffseason();
        }
        Assert.Equal(SessionPhase.Preseason, s.Phase);
        Assert.Same(best, s.HumanTeam);
        Assert.NotNull(s.Job.Current);
        Assert.DoesNotContain(s.LiveVacancies, v => v.WhileEmployed);
        for (int i = 0; i < 20; i++) s.AdvanceDay();
        Assert.True(s.Employed);
    }
}
