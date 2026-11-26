using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 208, a polish run:
/// - K38: college media timeouts (the first dead ball under 16, 12, 8 and 4 minutes of each half) and no bench timeouts in the
///   last two minutes of a 15-point game; the NBA and FIBA call theirs as before;
/// - H210: the Final Four page's Most Outstanding Player line names the Awards card's man and counts the same games;
/// - K37: a college player's repeated complaint is one inbox thread a season.
/// </summary>
public class Run208PolishTests
{
    private readonly ITestOutputHelper _out;
    public Run208PolishTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void K38_college_games_have_media_timeouts_and_no_garbage_time_timeouts()
    {
        var (league, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        var sim = new GameSimulator(); var rng = new Rng(208);
        var games = cw.Season.Games.Where(CollegeWorld.IsRegularSeason).Take(80).ToList();
        int media = 0, garbage = 0;
        foreach (var g in games)
        {
            var r = sim.Simulate(cw.Program(g.HomeId)!, cw.Program(g.AwayId)!, cw.GameRules(g), rng.NextULong(), keepLog: true);
            media += r.MediaTimeouts.Count;
            Assert.All(r.MediaTimeouts, m => Assert.InRange(m.Period, 1, 2));
            foreach (var half in new[] { 1, 2 })
            {
                var marks = r.MediaTimeouts.Where(m => m.Period == half).Select(m => m.Mark).ToList();
                Assert.Equal(marks.Distinct().Count(), marks.Count);
                Assert.True(marks.SequenceEqual(marks.OrderByDescending(x => x)), "marks in order");
                Assert.All(r.MediaTimeouts.Where(m => m.Period == half), m => Assert.True(m.Clock < m.Mark));
            }
            garbage += r.Timeouts.Count(t => t.Period >= 2 && t.Clock <= 120 && Math.Abs(t.HomeScore - t.AwayScore) >= TimeoutRules.GarbageMargin
                                              && t.Reason != TimeoutReason.Coach);
            Assert.Equal(r.MediaTimeouts.Count, r.PlayLog.Count(l => l.Contains("Media timeout (under ")));
        }
        double perGame = media / (double)games.Count;
        _out.WriteLine($"{games.Count} college games: {perGame:0.00} media timeouts a game, {garbage} garbage-time timeouts");
        Assert.InRange(perGame, 6.0, 8.0);
        Assert.Equal(0, garbage);

        // The pros and FIBA have none.
        var nba = league.Teams.Take(2).ToList();
        var pro = sim.Simulate(nba[0], nba[1], Rules.Nba, 5);
        Assert.Empty(pro.MediaTimeouts);
        Assert.Empty(sim.Simulate(nba[0], nba[1], Rules.Fiba, 5).MediaTimeouts);
        Assert.False(TimeoutRules.Nba.HasMediaTimeouts);
        Assert.False(TimeoutRules.Fiba.HasMediaTimeouts);
        Assert.True(TimeoutRules.College.HasMediaTimeouts);
    }

    [Fact]
    public void H210_the_MOP_line_counts_the_games_he_led()
    {
        GameStar Star(int id, int pts) => new(id, $"P{id}", pts, 4, 2);
        Assert.Equal("21 and 17 points in the Final Four", CollegeAwards.OutstandingPoints(new[] { Star(1, 21), Star(1, 17) }));
        Assert.Equal("17 points in the title game", CollegeAwards.OutstandingPoints(new[] { Star(1, 17) }));

        var s = new CollegeSeason { Year = 2026, ChampionId = 10 };
        s.Games.Add(new CollegeGame { Event = CollegeWorld.Madness, Round = 5, HomeId = 10, AwayId = 11, HomePts = 70, AwayPts = 60, HomeStar = Star(7, 25) });
        s.Games.Add(new CollegeGame { Event = CollegeWorld.Madness, Round = 6, HomeId = 12, AwayId = 10, HomePts = 61, AwayPts = 66, AwayStar = Star(8, 19) });
        var o = CollegeAwards.OutstandingOf(s);
        Assert.NotNull(o);
        // The title game's leading scorer, even though a team-mate scored more in the semi-final (the Awards card's rule).
        Assert.Equal(8, o!.Value.Star.PlayerId);
        Assert.Equal("19 points in the title game", CollegeAwards.OutstandingPoints(o.Value.Led));
        Assert.Null(CollegeAwards.OutstandingOf(new CollegeSeason { Year = 2026 }));
    }

    [Fact]
    public void K37_a_repeated_college_complaint_is_one_thread()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run208-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
            var me = world.College!.Programs.First(t => t.FullName.StartsWith("George Mason"));
            var s = GameSession.NewGame(9, me.Id, Path.Combine(dir, "career.hwsave"));
            var p = s.HumanTeam.Roster[0];
            var (t1, b1) = s.OneComplaintThread(p, $"{p.Name} wants a word", "He is unhappy and wants to talk.");
            Assert.Equal($"{p.Name} wants a word", t1);   // the first time: as before
            s.Post(t1, b1, "Squad");
            p.UnhappyWeeks = 6;
            var (t2, b2) = s.OneComplaintThread(p, $"{p.Name} wants a word", "He is unhappy and wants to talk.");
            Assert.Equal($"{p.Name} still wants a word", t2);
            Assert.Contains("unhappy for 6 weeks", b2);
            Assert.Contains("first raised on", b2);
            Assert.DoesNotContain(s.Inbox, m => m.Title.StartsWith(p.Name) && m.Title.EndsWith("wants a word"));
            s.Post(t2, b2, "Squad");
            // A new spell (he settled: no unhappy weeks) after the old message's week starts a new thread.
            p.UnhappyWeeks = 0;
            s.Inbox[0] = new InboxMessage { Id = s.Inbox[0].Id, Date = s.Today.AddDays(-30), Title = s.Inbox[0].Title, Body = s.Inbox[0].Body, Category = "Squad" };
            Assert.Equal($"{p.Name} wants a word", s.OneComplaintThread(p, $"{p.Name} wants a word", "x").title);
            s.Inbox[0] = new InboxMessage { Id = s.Inbox[0].Id, Date = s.Today, Title = s.Inbox[0].Title, Body = s.Inbox[0].Body, Category = "Squad" };
            p.UnhappyWeeks = 7;
            var (_, b3) = s.OneComplaintThread(p, $"{p.Name} wants a word", "He is unhappy and wants to talk.");
            // The first date carries through every update.
            Assert.Equal(System.Text.RegularExpressions.Regex.Match(b2, @"first raised on \w+ \d+").Value,
                         System.Text.RegularExpressions.Regex.Match(b3, @"first raised on \w+ \d+").Value);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
