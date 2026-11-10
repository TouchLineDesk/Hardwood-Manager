using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Your coach, owners and job security (decisions #70-74).</summary>
public class JobTests
{
    private static League NewLeague(ulong seed = 3) => new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));

    [Fact]
    public void Coach_budget_grows_with_background_and_licence()
    {
        Assert.Equal(70, Coach.Budget(CoachBackground.None, CoachLicence.C));
        Assert.Equal(91, Coach.Budget(CoachBackground.ExAllStar, CoachLicence.Pro));
        Assert.True(Coach.StartingReputation(CoachBackground.ExAllStar, CoachLicence.Pro) > Coach.StartingReputation(CoachBackground.None, CoachLicence.C));
        var c = new Coach();
        Assert.Equal(0, c.ShotLogit);           // all 10s: neutral
        c.Attributes[CoachAttr.Offence] = 20;
        Assert.True(c.ShotLogit > 0);
    }

    [Fact]
    public void Expectations_follow_the_squad_and_payroll_and_the_owner_adds_demands()
    {
        var league = NewLeague();
        Board.EnsureOwnersAndCoaches(league, 3);
        Assert.All(league.Teams, t => { Assert.False(string.IsNullOrEmpty(t.Owner.Name)); Assert.False(string.IsNullOrEmpty(t.CoachName)); });
        var best = league.Teams.First(t => Board.ProjectedRank(league, t) == 1);
        var worst = league.Teams.First(t => Board.ProjectedRank(league, t) == 30);
        worst.Owner.Ambition = 10;
        Assert.Equal(SeasonGoal.Title, Board.SetExpectations(league, best, 2026).Goal);
        Assert.Equal(SeasonGoal.Develop, Board.SetExpectations(league, worst, 2026).Goal);
        Assert.True(Board.SetExpectations(league, best, 2026).TargetWins > Board.SetExpectations(league, worst, 2026).TargetWins + 20);
        worst.Owner.Ambition = 5;
        Assert.Contains(Demand.StayUnderTax, Board.SetExpectations(league, worst, 2026).Demands);
        worst.Owner.Ambition = 18;
        var e = Board.SetExpectations(league, worst, 2026);
        Assert.Contains(Demand.MakePlayoffs, e.Demands);
        Assert.Equal(SeasonGoal.Playoffs, e.Goal);
    }

    [Fact]
    public void Losing_badly_for_an_impatient_owner_gets_you_sacked_mid_season_but_not_in_the_honeymoon()
    {
        var league = NewLeague();
        Board.EnsureOwnersAndCoaches(league, 3);
        var team = league.Teams.First(t => Board.ProjectedRank(league, t) <= 3);
        team.Owner.Patience = 3;
        var job = new JobState { Current = Board.SetExpectations(league, team, 2026), HiredOn = new DateOnly(2026, 10, 1), HiredSeason = 2025, Confidence = 65 };
        var stats = new Dictionary<int, PlayerSeasonStats>();
        bool sacked = false; var day = new DateOnly(2026, 10, 25);
        // Honeymoon: a terrible start in the first 60 days doesn't cost the job.
        for (int w = 0; w < 6 && !sacked; w++) sacked = Board.Weekly(job, league, team, 2, 30, stats, day.AddDays(7 * w)).sacked;
        Assert.False(sacked);
        for (int w = 6; w < 20 && !sacked; w++) sacked = Board.Weekly(job, league, team, 4, 36, stats, day.AddDays(7 * w)).sacked;
        Assert.True(sacked);
        // A winning coach of a patient owner is safe.
        team.Owner.Patience = 18;
        var good = new JobState { Current = Board.SetExpectations(league, team, 2026), HiredOn = new DateOnly(2026, 10, 1), Confidence = 65 };
        for (int w = 0; w < 20; w++) Assert.False(Board.Weekly(good, league, team, 36, 4, stats, day.AddDays(7 * w)).sacked);
        Assert.True(good.Confidence > 65);
    }

    [Fact]
    public void The_season_review_judges_the_result_against_the_goal()
    {
        var league = NewLeague();
        Board.EnsureOwnersAndCoaches(league, 3);
        var team = league.Teams.First(t => Board.ProjectedRank(league, t) <= 3);
        var coach = new Coach { Reputation = 40 };
        var job = new JobState { Current = Board.SetExpectations(league, team, 2026), Confidence = 60 };
        var stats = new Dictionary<int, PlayerSeasonStats>();
        var (_, _, sacked) = Board.SeasonReview(job, coach, league, team, SeasonGoal.Title, 62, 20, stats, new DateOnly(2027, 6, 20));
        Assert.False(sacked);
        Assert.True(job.Confidence > 60 && coach.Reputation > 40);
        team.Owner.Patience = 4;
        // First season: protected, even when it goes badly.
        var first = new JobState { Current = Board.SetExpectations(league, team, 2026), Confidence = 40, HiredSeason = 2026 };
        Assert.False(Board.SeasonReview(first, coach, league, team, SeasonGoal.Develop, 25, 57, stats, new DateOnly(2027, 6, 20)).sacked);
        var bad = new JobState { Current = Board.SetExpectations(league, team, 2026), Confidence = 40, HiredSeason = 2025 };
        Assert.True(Board.SeasonReview(bad, coach, league, team, SeasonGoal.Develop, 25, 57, stats, new DateOnly(2027, 6, 20)).sacked);
    }

    [Fact]
    public void Sacked_coaches_apply_for_jobs_and_take_over_and_it_all_saves()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-job-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var coach = new Coach { FirstName = "Danny", LastName = "Garrett", Background = CoachBackground.ExPro, Licence = CoachLicence.Pro, Reputation = 95 };
            var s = GameSession.NewGame(2026, 3, path, coach: coach);
            Assert.Same(coach, s.HumanTeam.Coach);
            Assert.Equal("Danny Garrett", s.HumanTeam.CoachName);
            Assert.NotNull(s.Job.Current);
            Assert.Contains(s.Inbox, m => m.Category == "Board" && m.Title.StartsWith("Expectations"));
            var oldTeam = s.HumanTeam;

            s.Fire();
            Assert.False(s.Employed);
            Assert.Null(s.ControlledTeamId);
            Assert.True(oldTeam.Tactic.Auto);
            Assert.Null(oldTeam.Coach);
            Assert.False(s.HumanPlaysToday);
            Assert.Single(coach.History);
            for (int i = 0; i < 10; i++) s.AdvanceDay(); // the world goes on without you

            var target = s.League.Teams.First(t => t != oldTeam);
            s.CoachLeaves(target, "fired");
            Assert.Contains(s.Job.Vacancies, v => v.TeamId == target.Id && v.Status == "open");
            Assert.Null(s.Apply(target.Id));
            for (int i = 0; i < 10 && s.Job.Vacancies.First(v => v.TeamId == target.Id).Status == "applied"; i++) s.AdvanceDay();
            Assert.Equal("offered", s.Job.Vacancies.First(v => v.TeamId == target.Id).Status); // reputation 95: they want you
            s.SaveNow();

            var loaded = GameSession.Load(path);
            Assert.False(loaded.Employed);
            Assert.Equal("Danny Garrett", loaded.Coach.Name);
            Assert.Equal(oldTeam.Owner.Name, loaded.League.TeamById(oldTeam.Id).Owner.Name);
            Assert.Null(loaded.AcceptOffer(target.Id));
            Assert.True(loaded.Employed);
            Assert.Equal(target.Id, loaded.HumanTeam.Id);
            Assert.Same(loaded.Coach, loaded.HumanTeam.Coach);
            Assert.False(loaded.HumanTeam.Tactic.Auto);
            loaded.SaveNow();
            var again = GameSession.Load(path);
            Assert.True(again.Employed);
            Assert.Equal(target.Id, again.HumanTeam.Id);
            for (int i = 0; i < 5; i++) again.AdvanceDay();
        }
        finally { Directory.Delete(dir, true); }
    }
}
