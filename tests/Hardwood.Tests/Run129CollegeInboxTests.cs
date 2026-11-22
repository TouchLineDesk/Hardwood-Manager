using Hardwood.Core.Guide;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 129, CS4 (college plan): the college inbox and assistant.
/// - Pro league news (champions, the third-place tournament, the awards, the Hall of Fame, the draft lottery, transfers)
///   rides in the world digest for a college coach instead of arriving as messages of their own.
/// - A Cinderella story joins today's March Madness message; a top recruit's national headline about your own program
///   doesn't repeat the trail's "commits to you".
/// - The assistant's standing reminders (the AD's patience, staff contracts) show on at most two days a phase.
/// - K10's small words: "an assistant coach", and the digest keeps "The No. 3 player ..." whole.
/// </summary>
public class Run129CollegeInboxTests
{
    private readonly ITestOutputHelper _out;
    public Run129CollegeInboxTests(ITestOutputHelper o) => _out = o;

    private static GameSession Career(string name, ulong seed = 2026)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith(name)).Id;
        return GameSession.NewGame(seed, id, null);
    }

    [Fact]
    public void Pro_news_goes_to_the_digest_for_a_college_coach()
    {
        var s = Career("Clemson");
        Assert.True(s.InCollege);
        int before = s.Inbox.Count, digest = s.WorldDigest.Count;
        s.PostPro("Oklahoma City Thunder are champions", "Oklahoma City Thunder defeated New York Knicks in the Finals.");
        s.PostPro("The draft lottery", "The best 30 college graduates go in a one-round draft.", "Draft");
        Assert.Equal(before, s.Inbox.Count);
        // K16 (run 155): the pros' champions don't reach a college coach at all; the draft (his graduates') still does.
        Assert.Equal(digest + 1, s.WorldDigest.Count);

        // March Madness: the round's message carries the Cinderella story.
        s.PostWorld("March Madness: the Sweet 16 is set", "East: 1 Duke, 4 Purdue.");
        s.PostWorld("Cinderella: 11-seed Drake Bulldogs reach the Sweet 16", "Drake Bulldogs, an 11-seed from the MVC, are into the Sweet 16.");
        Assert.Equal(before + 1, s.Inbox.Count);
        Assert.Contains("Cinderella: 11-seed Drake", s.Inbox[0].Body);

        // Your own top recruit: the trail says "commits to you"; the national headline doesn't repeat it in the inbox.
        if (s.TrailOpen)
        {
            s.PostWorld($"5-star Joe Smith commits to {s.HumanTeam.FullName}", $"Joe Smith has chosen {s.HumanTeam.FullName}.");
            Assert.Equal(before + 1, s.Inbox.Count);
        }
    }

    [Fact]
    public void Pro_news_still_arrives_for_a_pro_coach()
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var s = GameSession.NewGame(2026, league.Teams[0].Id, null);
        Assert.False(s.InCollege);
        int before = s.Inbox.Count;
        s.PostPro("Oklahoma City Thunder are champions", "Oklahoma City Thunder defeated New York Knicks in the Finals.");
        Assert.Equal(before + 1, s.Inbox.Count);
        Assert.Equal("League", s.Inbox[0].Category);
    }

    [Fact]
    public void Standing_reminders_show_on_two_days_a_phase()
    {
        var s = Career("Clemson");
        var progress = new GuideProgress();
        s.Job.Confidence = 30;
        int shown = 0;
        for (int d = 0; d < 6; d++)
        {
            var adv = Assistant.Advise(s, "Home", GuideLevel.Experienced, progress);
            if (adv.Tasks.Any(t => t.Title == "The AD is losing patience")) shown++;
            // Asking twice on the same day doesn't use up a day.
            Assistant.Advise(s, "Board", GuideLevel.Experienced, progress);
            s.AdvanceDay();
            s.Job.Confidence = 30;
        }
        Assert.Equal(Assistant.MaxReminderDays, shown);
        // Falling below 20 is news: it comes back.
        s.Job.Confidence = 15;
        Assert.Contains(Assistant.Advise(s, "Home", GuideLevel.Experienced, progress).Tasks, t => t.Title == "The AD is losing patience");
    }

    [Fact]
    public void Targets_who_chose_elsewhere_arrive_together()
    {
        var s = Career("Clemson");
        s.WeekRecruiting.Add("Joe Smith (PG, Texas), whom you were recruiting, has committed to Duke Blue Devils. Interest in you was 41.");
        s.WeekRecruiting.Add("Sam Jones (C, Ohio), whom you were recruiting, has committed to Ohio State Buckeyes. Interest in you was 22.");
        for (int d = 0; d < 30 && s.WeekRecruiting.Count > 0; d++) s.AdvanceDay();
        Assert.Empty(s.WeekRecruiting);
        // They arrive together: in the next digest ("Recruiting: 2 targets chose other schools") or, once the season is
        // a week away (H159, run 156: it opens November 3), in the first results message.
        var m = Assert.Single(s.Inbox, m => m.Body.Contains("Recruiting (targets who chose other schools)"));
        if (m.Title.StartsWith("Recruiting:")) Assert.StartsWith("Recruiting: 2 targets chose other schools", m.Title);
        else Assert.True(GameSession.IsResultsTitle(m.Title), m.Title);
        Assert.Contains("Duke Blue Devils", m.Body);
        Assert.Contains("Ohio State Buckeyes", m.Body);
        _out.WriteLine($"{m.Date}: {m.Title}");
    }

    [Fact]
    public void Small_words()
    {
        Assert.Equal("an assistant coach", Assistant.Article("assistant coach"));
        Assert.Equal("a physio", Assistant.Article("physio"));
        Assert.Equal("The No. 3 player in the Class of 2028 (Texas) has chosen Duke Blue Devils.",
            GameSession.FirstSentence("The No. 3 player in the Class of 2028 (Texas) has chosen Duke Blue Devils. He had offers from 20 schools."));
        Assert.Equal("He signs with St. John's Red Storm.", GameSession.FirstSentence("He signs with St. John's Red Storm. Then more."));
        Assert.Equal("A plain sentence.", GameSession.FirstSentence("A plain sentence. Another."));
    }
}
