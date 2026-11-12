using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>College sim slice C2 (decisions #160, #168): coach a college program. Your games use the full engine with
/// college rules, the athletic director sets March Madness goals, and the career rolls into the next season.</summary>
public class CollegeCareerTests
{
    private static (int id, string conference) ProgramFor(ulong seed, bool strong)
    {
        var league = new WorldGenerator(new Rng(seed)).Generate("North American Basketball League", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, seed, GameSession.DefaultFullSim, new DateOnly(2026, 10, 1));
        var cw = world.College!;
        var t = (strong ? cw.Programs.OrderByDescending(p => cw.Prestige[p.Id]) : cw.Programs.OrderBy(p => cw.Prestige[p.Id])).First();
        return (t.Id, CollegeWorld.Conferences.First(c => c.Key == cw.ConferenceOf[t.Id]).Name);
    }

    private static string TempSave(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), $"hw-college-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "career.hwsave");
    }

    [Fact]
    public void A_college_career_starts_with_an_athletic_director_and_plays_full_engine_games()
    {
        var (id, conference) = ProgramFor(5, strong: true);
        var s = GameSession.NewGame(5, id, null);
        Assert.True(s.InCollege);
        Assert.True(s.Abroad);
        Assert.False(s.InNabl);
        Assert.Equal(conference, s.HumanLeagueName);
        Assert.Equal("college", s.Job.Current!.League);
        Assert.Equal("athletic director", s.BossTitle);
        // A blue blood is asked for a deep tournament run, in college words.
        var ctx = s.ContextOf(s.HumanTeam);
        Assert.True(ctx.IsCollege);
        var goal = Board.Describe(s.Job.Current.Goal, ctx);
        Assert.Contains(goal, new[] { "Win the national championship", "Reach the Final Four", "Reach the Sweet 16", "Make March Madness" });
        Assert.Empty(s.Job.Current.Demands);
        Assert.Contains(s.Inbox, m => m.Title.StartsWith("Expectations") && m.Body.StartsWith("Athletic director"));
        Assert.InRange(s.HumanTeam.Roster.Count, 13, 15);
        Assert.Equal(Enum.GetValues<Hardwood.Core.Model.StaffRole>().Length, s.HumanTeam.Staff.Count);

        var first = s.NextMyGame!;
        Assert.Equal(11, first.Date.Month);
        Assert.Equal(0, s.MyGames().Count(g => g.Played));

        // The first game, live, on the full engine with college rules.
        while (!s.HumanPlaysToday) s.AdvanceDay();
        var (live, played) = s.BeginLiveDay();
        Assert.NotNull(live);
        s.FinishLiveDay(live, played);
        var done = s.MyGames().Single(g => g.Played);
        Assert.NotNull(done.Result);
        Assert.Contains(done.Result!.Home.Lines.Concat(done.Result.Away.Lines), l => l.Points > 0);
        Assert.True(done.Result.Home.Lines.Sum(l => l.Seconds) <= 5 * 2 * 20 * 60 + 5 * 5 * 60 * 3, "college games are two 20-minute halves");

        // Pro-only business is closed to a college coach.
        var pro = s.League.Teams[0].Roster[0];
        Assert.Contains("high-school recruiting", s.CannotTalk(pro));
        var abroad = s.World.Players.First(p => p.TeamId != null);
        Assert.Equal(BidAnswer.Rejected, s.Bid(abroad, 1_000_000).Answer);
        Assert.False(s.RecruitingOpen);

        // A month on: every game of ours is a full box score; the conference table and stats add up.
        for (int i = 0; i < 30; i++) s.AdvanceDay();
        var mine = s.MyGames().Where(g => g.Played).ToList();
        Assert.True(mine.Count >= 6, $"{mine.Count} games");
        Assert.All(mine, g => Assert.NotNull(g.Result));
        Assert.Equal(mine.Count, s.MyRecord.Games);
        Assert.Equal(mine.Count(g => g.Won), s.MyRecord.Wins);
        Assert.Equal(s.World.College!.InConference(s.World.College.ConferenceOf[id]).Count(), s.MyTable.Count);
        var top = s.MyStats.Values.OrderByDescending(x => x.Points).First();
        Assert.True(top.Games >= mine.Count - 2 && top.Points > 0);
        Assert.InRange(s.MyStats.Values.Max(x => x.Games), mine.Count - 1, mine.Count);
    }

    [Fact]
    public void A_college_season_ends_with_a_tournament_verdict_and_a_new_class()
    {
        var (id, _) = ProgramFor(7, strong: false);
        var path = TempSave(out var dir);
        try
        {
            var s = GameSession.NewGame(7, id, path);
            var roster = s.HumanTeam.Roster.ToList();
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            var cw = s.World.College!;
            Assert.NotNull(cw.Season.ChampionId);
            var games = s.MyGames();
            Assert.All(games, g => Assert.True(g.Played));
            Assert.InRange(games.Count, 24, 40);
            // No pro-league calendar news for a college coach (critic review #8).
            Assert.DoesNotContain(s.Inbox, m => m.Title is "All-Star Game" or "Trade deadline has passed" or "The January window has shut");
            // The AD judged the season in college terms (or let a first-year coach go on).
            var review = s.Inbox.Last(m => m.Category == "Board" && (m.Title == "Season review" || m.Title == "You have been sacked"));
            Assert.Matches("Result: (win the national championship|reach the final four|reach the sweet 16|make march madness|build the program)", review.Body);
            if (s.Employed)
            {
                // Seniors graduated, the signed class enrolled.
                var post = s.Inbox.Last(m => m.Title == "Graduation and enrollment");
                Assert.Contains("Your new class", post.Body);
                Assert.True(roster.Any(p => !s.HumanTeam.Roster.Contains(p)), "someone graduated");
                Assert.InRange(s.HumanTeam.Roster.Count, 13, 15);
                // College coaches have no contracts to settle.
                Assert.Empty(s.MyExpiring);
            }

            // Through the pros' summer into the next college season.
            while (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason();
            Assert.Equal(SessionPhase.Preseason, s.Phase);
            if (s.Employed)
            {
                // The pros' summer stays out of a college coach's inbox (review #8): no free agency, no pro coaching changes.
                Assert.DoesNotContain(s.Inbox, m => m.Title == "Free agency is open" || m.Title == "Overseas free agents" || (m.Title.EndsWith(" change coach") && m.Category == "League"));
                Assert.True(s.InCollege);
                Assert.Equal("college", s.Job.Current!.League);
                Assert.Equal(s.League.Season, s.Job.Current.Season);
                Assert.Equal(s.League.Season, cw.Season.Year);
                Assert.True(s.NextMyGame is { } next && next.Date.Month == 11);
            }

            // The career saves and loads as a college career.
            s.SaveNow();
            var staff = s.HumanTeam.Staff.Select(x => x.Name).OrderBy(x => x).ToList();
            var back = GameSession.Load(path);
            Assert.Equal(s.HumanTeam.Id, back.HumanTeam.Id);
            Assert.Equal(s.InCollege, back.InCollege);
            if (s.Employed) Assert.Equal(staff, back.HumanTeam.Staff.Select(x => x.Name).OrderBy(x => x).ToList());
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void The_athletic_director_judges_on_the_college_ladder()
    {
        var league = new WorldGenerator(new Rng(9)).Generate("North American Basketball League", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, 9, GameSession.DefaultFullSim, new DateOnly(2026, 10, 1));
        var cw = world.College!;
        var program = cw.Programs[0];
        var ctx = LeagueContext.College(cw, program);
        Assert.True(ctx.IsCollege);
        Assert.Equal(cw.Programs.Count, ctx.Teams.Count);
        Assert.Equal("Reach the Final Four", Board.Describe(SeasonGoal.Finals, ctx));
        Assert.Equal("Make March Madness", Board.Describe(SeasonGoal.Playoffs, ctx));
        Assert.Equal(3, Board.CollegeStep(SeasonGoal.Title) - Board.CollegeStep(SeasonGoal.Playoffs));
        // The favourite's goal is the title, the field's tail is asked to build; no pro demands.
        var best = cw.Programs.OrderBy(t => Board.ProjectedRank(ctx, t)).First();
        Assert.Equal(SeasonGoal.Title, Board.SetExpectations(LeagueContext.College(cw, best), best, 2026).Goal);
        var worst = cw.Programs.OrderBy(t => Board.ProjectedRank(ctx, t)).Last();
        worst.Owner.Ambition = 10;
        var low = Board.SetExpectations(LeagueContext.College(cw, worst), worst, 2026);
        Assert.Equal(SeasonGoal.Develop, low.Goal);
        Assert.Empty(low.Demands);

        // A 1-seed set "win it all" who goes out in the round of 32 at 24-8: judged three steps short, not four.
        JobState Job() => new() { Confidence = 80, HiredSeason = 2025, Current = new Expectations { Season = 2026, Goal = SeasonGoal.Title, TargetWins = 21 } };
        var college = Job(); var pro = Job();
        var (_, body, sacked) = Board.SeasonReview(college, new Coach(), ctx, program, SeasonGoal.Playoffs, 24, 8, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2027, 4, 7), 2026);
        Board.SeasonReview(pro, new Coach(), ctx with { IsCollege = false }, program, SeasonGoal.Playoffs, 24, 8, new Dictionary<int, PlayerSeasonStats>(), new DateOnly(2027, 4, 7), 2026);
        Assert.False(sacked);
        Assert.True(college.Confidence > pro.Confidence, $"{college.Confidence} vs {pro.Confidence}");
        Assert.Contains("Goal: win the national championship. Result: make march madness", body);
    }
}
