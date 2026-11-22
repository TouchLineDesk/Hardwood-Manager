using System.Text.RegularExpressions;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 155 (polish, college): the critic's K16, K18 and K20 and the hunter's H160, H163 and H176.
/// - K16: a college coach's digest carries the college world only (no NBA milestones, overseas champions or pro transfers).
/// - K18: the enrollment message gives a freshman's potential as the staff's range, never the exact number.
/// - K20: the inbox's conference place reads the same order as Home and Standings before conference play.
/// </summary>
public class Run155CollegePolishTests
{
    private readonly ITestOutputHelper _out;
    public Run155CollegePolishTests(ITestOutputHelper o) => _out = o;

    private static GameSession Career(string name, ulong seed = 2026)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith(name)).Id;
        return GameSession.NewGame(seed, id, null);
    }

    [Fact]
    public void A_college_coach_hears_only_college_news()
    {
        var s = Career("Boston University", 31);
        Assert.True(s.InCollege);
        int digest = s.WorldDigest.Count;
        // The pros' stories: a milestone, an overseas champion, the NBA champion. None reaches him.
        s.PostWorld("Milestone: Stephen Curry passes 25,000 points", "Stephen Curry passed Elvin Hayes on the all-time scoring list.");
        s.PostWorld("Kigali Patriots win the Basketball Africa League", "Kigali Patriots beat Petro de Luanda.");
        s.PostPro("Oklahoma City Thunder are champions", "Oklahoma City Thunder defeated New York Knicks in the Finals.");
        Assert.Equal(digest, s.WorldDigest.Count);
        // College stories and the draft (his graduates' night) still ride in the digest.
        s.PostWorld("5-star Joe Smith commits to Kentucky", "Joe Smith has chosen Kentucky.", college: true);
        s.PostPro("The draft lottery", "The best 30 college graduates go in a one-round draft.", "Draft");
        Assert.Equal(digest + 2, s.WorldDigest.Count);

        // Two months of a real season: every story in the digest came from the college world.
        int guard = 0;
        var pros = s.League.Teams.Select(t => t.FullName).Concat(s.World.Competitions.SelectMany(c => c.Clubs).Select(t => t.FullName)).ToHashSet();
        while (s.Today < new DateOnly(2027, 1, 10) && guard++ < 200) s.AdvanceDay();
        var around = s.Inbox.Where(m => m.Body.Contains("Around the world")).ToList();
        _out.WriteLine($"{around.Count} messages carry 'Around the world'");
        foreach (var m in around)
        {
            var section = m.Body[m.Body.IndexOf("Around the world", StringComparison.Ordinal)..];
            Assert.DoesNotContain(section.Split('\n'), l => l.StartsWith("• Milestone") || pros.Any(p => l.Contains(p)));
        }
    }

    [Fact]
    public void A_pro_coach_still_reads_the_world()
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var s = GameSession.NewGame(2026, league.Teams[0].Id, null);
        Assert.False(s.InCollege);
        int digest = s.WorldDigest.Count;
        s.PostWorld("Kigali Patriots win the Basketball Africa League", "Kigali Patriots beat Petro de Luanda.");
        Assert.Equal(digest + 1, s.WorldDigest.Count);
    }

    [Fact]
    public void The_inbox_place_matches_Standings_before_conference_play()
    {
        var s = Career("Duke");
        int guard = 0;
        while (s.Today < new DateOnly(2026, 12, 24) && guard++ < 200) s.AdvanceDay();
        Assert.True(s.MyRecord.Games > 0);
        var table = s.MyTable;
        Assert.True(table.All(r => r.Games == 0), "Dec 24 is before ACC play on this seed");
        // Home and Standings order by overall record until the first conference game (H81, H134).
        var shown = table.Select(r => s.CollegeRecord(r.Team)).OrderByDescending(r => r.WinPct).ThenByDescending(r => r.Wins).ThenByDescending(r => r.PointDiff).ToList();
        int expected = shown.FindIndex(r => r.Team == s.HumanTeam) + 1;
        _out.WriteLine($"Duke {s.MyRecord.Wins}-{s.MyRecord.Losses}: place {s.MyRank}, Standings {expected}");
        Assert.Equal(expected, s.MyRank);
    }

    [Fact]
    public void The_new_class_reads_as_ranges()
    {
        var s = Career("Duke", 7);
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
        while (s.Phase == SessionPhase.Offseason && !s.Inbox.Any(m => m.Title == "Graduation and enrollment") && guard++ < 1200) s.AdvanceOffseason();
        if (!s.Employed) return; // a first-year sacking ends the career; CollegeCareerTests covers that path
        var post = s.Inbox.First(m => m.Title == "Graduation and enrollment");
        _out.WriteLine(post.Body);
        var line = post.Body.Split("\n\n").First(l => l.StartsWith("Your new class") || l.StartsWith("No new players"));
        if (line.StartsWith("No new players")) return;
        // "Theodore Randall (SF, CA 69, PA 150-168)", never "(SF, 69/97)".
        Assert.DoesNotMatch(@"\(\w+, \d+/\d+\)", line);
        Assert.Matches(@"\(\w+, CA \d+, PA \d+-\d+\)", line);
    }
}
