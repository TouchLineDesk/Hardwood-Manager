using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Staff part 2: staff contracts are yours to extend, the assistant coach can front the press, and you grow as a coach.</summary>
public class StaffPart2Tests
{
    private static GameSession Session(ulong seed, int strengthRank = 10, string? path = null)
    {
        var probe = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var id = probe.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(strengthRank).Id;
        return GameSession.NewGame(seed, id, path);
    }

    private static void ToOffseason(GameSession s, Action<GameSession>? weekly = null)
    {
        int last = 0;
        while (s.Phase != SessionPhase.Offseason)
        {
            s.AdvanceDay();
            // Not on the day the season ends: the coach's season totals have just been reset (run 20 hit this when the
            // engine change moved the last day onto a weekly step).
            if (weekly != null && s.Phase != SessionPhase.Offseason && s.Today.DayNumber - last >= 7) { last = s.Today.DayNumber; weekly(s); }
        }
    }

    [Fact]
    public void Staff_you_extend_stay_and_the_rest_leave_when_their_contracts_end()
    {
        var s = Session(101);
        var staff = s.HumanTeam.Staff.ToList();
        Assert.True(staff.Count >= 2);
        foreach (var st in staff) st.ContractYears = 1;
        var keep = staff[0];
        var go = staff[1];
        Assert.Contains(keep, s.StaffExpiring);

        var terms = s.StaffRenewal(keep);
        Assert.True(terms.Willing, terms.Text);
        Assert.StartsWith("Done", s.ExtendStaff(keep));
        Assert.Equal(1 + terms.Years, keep.ContractYears);
        Assert.Equal(terms.Wage, keep.Salary);
        Assert.DoesNotContain(keep, s.StaffExpiring);
        Assert.False(s.StaffRenewal(keep).Willing); // not in his final year any more

        // The assistant flags it from February.
        while (s.Today < new DateOnly(2027, 2, 2)) s.AdvanceDay();
        var advice = Hardwood.Core.Guide.Assistant.Advise(s, "Home", Hardwood.Core.Guide.GuideLevel.Auto, new Hardwood.Core.Guide.GuideProgress());
        Assert.Contains(advice.Tasks, t => t.Key.StartsWith("staffdeal-") && t.Page == "Staff & Training");

        ToOffseason(s);
        Assert.Contains(keep, s.HumanTeam.Staff);
        Assert.DoesNotContain(go, s.HumanTeam.Staff);
        Assert.Contains(s.Inbox, m => m.Title.EndsWith("ran out") && m.Body.Contains(go.Name));
    }

    [Fact]
    public void A_delegated_assistant_coach_answers_the_press_in_his_own_style()
    {
        var s = Session(102, 28);
        s.Delegated.Media = true;
        var asst = s.HumanTeam.Staff.First(x => x.Role == StaffRole.AssistantCoach);
        while (s.Today < new DateOnly(2027, 2, 1)) s.AdvanceDay();
        var mine = s.Media.Decisions.Where(d => d.TeamId == s.HumanTeam.Id).ToList();
        Assert.NotEmpty(mine);
        Assert.Empty(s.OpenDecisions);
        Assert.All(mine, d => Assert.StartsWith($"{asst.Name} answered", d.Outcome));
        foreach (var d in mine)
        {
            int spirit = d.Options.FindIndex(o => o.Hint.Contains("pirit up"));
            int expected = asst.Is("Mentor") && spirit >= 0 ? spirit : d.SafeOption;
            Assert.Equal(expected, d.Chosen);
        }
        Assert.Contains(mine.Last().Title, s.Delegated.LastMedia);
        Assert.Equal(0, s.Coach.SeasonAnswers); // he answered, not you
    }

    [Fact]
    public void A_season_of_fronting_the_press_and_talking_to_players_grows_the_coach()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-coach-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = Session(103, 20, path);
            int mot = s.Coach[CoachAttr.Motivation], man = s.Coach[CoachAttr.ManManagement];
            ToOffseason(s, x =>
            {
                foreach (var d in x.OpenDecisions.ToList()) x.Answer(d.Id, d.SafeOption);
                var p = x.HumanTeam.Roster.FirstOrDefault(p => Hardwood.Core.People.Talks.Unavailable(p, x.Today) == null);
                if (p != null) x.Talk(p, Hardwood.Core.People.TalkTopic.Praise);
            });
            Assert.True(s.Media.Answered >= 5, $"{s.Media.Answered} answered");
            Assert.Equal(mot + 1, s.Coach[CoachAttr.Motivation]);
            Assert.Equal(man + 1, s.Coach[CoachAttr.ManManagement]);
            Assert.Equal(0, s.Coach.SeasonTalks);
            Assert.Contains(s.Inbox, m => m.Title == "You've grown as a coach" && m.Body.Contains("Motivation"));
            s.SaveNow();
            var loaded = GameSession.Load(path);
            Assert.Equal(mot + 1, loaded.Coach[CoachAttr.Motivation]);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
