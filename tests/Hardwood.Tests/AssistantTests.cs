using Hardwood.Core.Guide;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>The in-game assistant (decisions #115-118): situation-based tasks, page tips, strategy, and a level that grows.</summary>
public class AssistantTests
{
    [Fact]
    public void A_new_coach_gets_first_steps_and_a_tip_for_every_page()
    {
        var s = GameSession.NewGame(2026, 5, null);
        var progress = new GuideProgress();
        var advice = Assistant.Advise(s, "Home", GuideLevel.Auto, progress);
        Assert.Equal(GuideStage.Beginner, advice.Stage);
        Assert.Contains(advice.Tasks, t => t.Key == "visit-Rotation" && t.Page == "Rotation");
        Assert.False(string.IsNullOrWhiteSpace(advice.PageTip));
        foreach (var page in new[] { "Home", "Inbox", "Roster", "Rotation", "Tactics", "Squad", "Board", "Staff & Training", "Schedule & Results",
                     "Standings", "Leagues", "College", "League Leaders", "Transactions", "Offseason", "Awards", "History", "Settings" })
            Assert.False(string.IsNullOrWhiteSpace(Assistant.PageTip(page, GuideStage.Beginner).Tip), page);

        // Seen pages drop off the checklist; dismissed tasks stay hidden.
        progress.PagesSeen.Add("Rotation");
        progress.Dismissed.Add("visit-Tactics");
        advice = Assistant.Advise(s, "Home", GuideLevel.Auto, progress);
        Assert.DoesNotContain(advice.Tasks, t => t.Key is "visit-Rotation" or "visit-Tactics");
        Assert.True(advice.Tasks.SequenceEqual(advice.Tasks.OrderBy(t => t.Priority)));
    }

    [Fact]
    public void The_level_grows_with_days_and_seasons()
    {
        var p = new GuideProgress();
        Assert.Equal(GuideStage.Beginner, Assistant.StageFor(GuideLevel.Auto, p));
        p.DaysPlayed = Assistant.BeginnerDays;
        Assert.Equal(GuideStage.Experienced, Assistant.StageFor(GuideLevel.Auto, p));
        p.SeasonsPlayed = 1;
        Assert.Equal(GuideStage.Strategy, Assistant.StageFor(GuideLevel.Auto, p));
        Assert.Equal(GuideStage.Beginner, Assistant.StageFor(GuideLevel.Beginner, p)); // a fixed level wins
    }

    [Fact]
    public void Problems_become_urgent_tasks_that_point_to_the_right_page()
    {
        var s = GameSession.NewGame(2026, 5, null);
        var team = s.HumanTeam;
        var star = team.Roster.OrderByDescending(p => p.CurrentAbility).First();
        star.Morale = 20;
        team.Staff.RemoveAll(st => st.Role == StaffRole.Physio);
        s.Job.Confidence = 30;
        team.DepthChart = new DepthChart { Starters = team.Roster.Take(5).Select(p => p.Id).ToList() };
        var hurt = team.Roster.First(p => team.DepthChart.Starters.Contains(p.Id));
        hurt.Injury = new Injury { Name = "Sprained ankle", Occurred = s.Today, ReturnDate = s.Today.AddDays(10) };

        var tasks = Assistant.Advise(s, "Home", GuideLevel.Experienced, new GuideProgress()).Tasks;
        Assert.Contains(tasks, t => t.Page == "Squad" && t.Title.Contains(star.Name) && t.Priority < 30);
        Assert.Contains(tasks, t => t.Page == "Staff & Training" && t.Title.Contains("physio"));
        Assert.Contains(tasks, t => t.Page == "Board" && t.Priority < 30);
        Assert.Contains(tasks, t => t.Page == "Rotation" && t.Title.Contains(hurt.Name));
        Assert.DoesNotContain(tasks, t => t.Key.StartsWith("visit-")); // no checklist past the beginner stage
    }

    [Fact]
    public void Strategy_points_at_young_players_without_minutes()
    {
        var s = GameSession.NewGame(2026, 5, null);
        while (s.MyRecord.Wins + s.MyRecord.Losses < 12) s.AdvanceDay();
        var tips = Assistant.Strategy(s, GuideStage.Strategy);
        Assert.NotEmpty(tips);
        Assert.Contains(tips, t => t.Contains("on pace for"));
    }
}
