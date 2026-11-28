using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 235, `danny-broadcast-nov27` (b): the studio's debate ("versus") card and hot take, graded at the final.</summary>
public class Run235DebateTests
{
    private static bool NextGameDay(GameSession s)
    {
        int guard = 0;
        while (!s.HumanPlaysToday && guard++ < 30) s.AdvanceDay();
        return s.HumanPlaysToday;
    }

    /// <summary>Watch <paramref name="games"/> of your games with the show on, checking each night's debate and take.</summary>
    private static (int debates, int takes, int nights) Watch(GameSession s, int games, Action<StudioShow, GameSession>? each = null)
    {
        int debates = 0, takes = 0, nights = 0;
        for (int g = 0; g < games && NextGameDay(s); g++)
        {
            var (live, played) = s.BeginLiveDay();
            if (live == null) { s.AdvanceDay(); continue; }
            var show = s.BuildStudioShow(live.Result.Home.Team, live.Result.Away.Team);
            s.LockStudioPicks(show);
            each?.Invoke(show, s);
            if (show.Debate != null) debates++;
            if (show.HotTake != null) takes++;
            nights++;
            live.Finish();
            // The postgame show's verdict is read from the box before the game is recorded, and agrees with the grade after.
            var verdicts = s.StudioVerdicts(live.Result);
            Assert.Equal((show.Debate != null ? 1 : 0) + (show.HotTake != null ? 1 : 0), verdicts.Count);
            var beats = s.VerdictBeats(live.Result);
            if (verdicts.Count > 0) Assert.Equal("THE VERDICT", Assert.Single(beats).Kicker);
            s.FinishLiveDay(live, played);
            foreach (var v in verdicts)
            {
                Assert.Equal(v.Holds, v.Take.Held);
                Assert.Equal(v.Verdict, v.Take.Verdict);
                Assert.Matches(@"^\S+ (was right|called it|missed)$", v.Verdict);
            }
        }
        return (debates, takes, nights);
    }

    [Fact]
    public void Nobody_argues_against_his_own_pick_and_every_night_is_graded()
    {
        var s = GameSession.NewGame(31, 5, null);
        for (int i = 0; i < 10; i++) s.AdvanceDay();
        var (debates, takes, nights) = Watch(s, 14, (show, _) =>
        {
            Team Pick(StudioAnalyst a) => show.Picks.Single(p => p.Analyst == a).Pick;
            if (show.Debate is { } d)
            {
                Assert.NotEqual(d.Yes.Analyst, d.No.Analyst);
                Assert.Contains(show.Picks, p => p.Analyst == d.Yes.Analyst);
                Assert.Contains(show.Picks, p => p.Analyst == d.No.Analyst);
                Assert.EndsWith("?", d.Question);
                Assert.DoesNotContain("{", d.Yes.Take + d.No.Take + d.Question);
                if (d.Check == TakeCheck.Wins)
                {
                    Assert.Equal(d.TeamId, Pick(d.Yes.Analyst).Id);
                    Assert.NotEqual(d.TeamId, Pick(d.No.Analyst).Id);
                }
                if (d.Check == TakeCheck.WinsBy)
                {
                    Assert.True(show.Picks.Single(p => p.Analyst == d.Yes.Analyst).Margin >= d.Line);
                    Assert.True(show.Picks.Single(p => p.Analyst == d.No.Analyst).Margin < d.Line);
                }
                if (d.Check == TakeCheck.Scores) Assert.True(d.PlayerId > 0 && d.Line > 0);
            }
            if (show.HotTake is { } t)
            {
                Assert.Contains(show.Picks, p => p.Analyst == t.Analyst);
                if (t.Check is TakeCheck.Wins or TakeCheck.WinsBy) Assert.Equal(t.TeamId, Pick(t.Analyst).Id);
                Assert.DoesNotContain("{", t.Take);
            }
            // The show's reel carries the versus card (a beat a side) and the take.
            var reel = DeskReel.Pregame(show);
            Assert.Equal(show.Debate == null ? 0 : 2, reel.Count(b => b.Kicker == "THE DEBATE"));
            Assert.Equal(show.HotTake == null ? 0 : 1, reel.Count(b => b.Kicker == "HOT TAKE"));
        });
        Assert.True(nights >= 10, $"only {nights} nights");
        Assert.True(debates >= nights * 3 / 4, $"{debates} debates in {nights} nights");
        Assert.True(takes >= nights / 2, $"{takes} takes in {nights} nights");
        Assert.All(s.Media.Studio.Takes, t => Assert.NotNull(t.Held));
        // The trackers count: every graded take is on its speaker's record.
        int landed = 0, missed = 0;
        foreach (var key in s.Media.Studio.Takes.Where(t => t.Kind == StudioTakeRecord.Take).Select(t => t.Yes).Distinct())
        {
            var (w, l) = s.Media.Studio.TakeRecord(key, s.League.Season);
            landed += w; missed += l;
        }
        Assert.Equal(s.Media.Studio.Takes.Count(t => t.Kind == StudioTakeRecord.Take), landed + missed);
        var debatesOnRecord = s.Media.Studio.Takes.Where(t => t.Kind == StudioTakeRecord.Debate).ToList();
        foreach (var d in debatesOnRecord)
        {
            var (yr, yw) = s.Media.Studio.DebateRecord(d.Yes, s.League.Season);
            Assert.True(yr + yw >= 1);
        }
    }

    [Fact]
    public void The_next_show_says_who_won_the_last_debate()
    {
        var s = GameSession.NewGame(31, 5, null);
        for (int i = 0; i < 20; i++) s.AdvanceDay();
        int said = 0;
        Watch(s, 8, (show, sess) =>
        {
            var ld = sess.StudioLastDebate();
            var ln = sess.StudioLastNight();
            if (ld == null || ln.Count == 0 || ln[0].Key != ld.Key) return;
            var lines = show.Segments.SelectMany(x => x.Lines).Select(l => l.Text).ToList();
            Assert.Contains(lines, l => l.StartsWith("And the last debate") && l.Contains(ld.Verdict) && l.Contains(ld.Question));
            said++;
        });
        Assert.True(said >= 3, $"the last debate was named on {said} shows");
        var last = s.StudioLastDebate();
        Assert.NotNull(last);
        Assert.NotEqual("", last!.Happened);
    }

    [Fact]
    public void Claims_are_judged_from_the_box()
    {
        var s = GameSession.NewGame(31, 5, null);
        for (int i = 0; i < 12; i++) s.AdvanceDay();
        Assert.True(NextGameDay(s));
        var (live, _) = s.BeginLiveDay();
        live!.Finish();
        var r = live.Result;
        var win = r.Winner; var lose = r.Home == win ? r.Away : r.Home;
        int by = win.Points - lose.Points;
        Assert.True(StudioDebates.Holds(TakeCheck.Wins, win.Team.Id, 0, 0, r));
        Assert.False(StudioDebates.Holds(TakeCheck.Wins, lose.Team.Id, 0, 0, r));
        Assert.True(StudioDebates.Holds(TakeCheck.WinsBy, win.Team.Id, 0, by, r));
        Assert.False(StudioDebates.Holds(TakeCheck.WinsBy, win.Team.Id, 0, by + 1, r));
        var top = r.Home.Lines.OrderByDescending(l => l.Points).First();
        Assert.True(StudioDebates.Holds(TakeCheck.Scores, 0, top.Player.Id, top.Points, r));
        Assert.False(StudioDebates.Holds(TakeCheck.Scores, 0, top.Player.Id, top.Points + 1, r));
        var rec = new StudioTakeRecord { Kind = StudioTakeRecord.Debate, YesName = "Marcus 'Showtime' Lyle", NoName = "Reggie Banfield" };
        Assert.Equal("Marcus was right", StudioDebates.Verdict(rec, true));
        Assert.Equal("Reggie was right", StudioDebates.Verdict(rec, false));
        var take = new StudioTakeRecord { Kind = StudioTakeRecord.Take, YesName = "Tasha Kincaid" };
        Assert.Equal("Tasha called it", StudioDebates.Verdict(take, true));
        Assert.Equal("Tasha missed", StudioDebates.Verdict(take, false));
    }

    [Fact]
    public void Debates_and_takes_survive_a_save_and_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run235-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = GameSession.NewGame(31, 5, path);
            for (int i = 0; i < 12; i++) s.AdvanceDay();
            Watch(s, 3);
            Assert.NotEmpty(s.Media.Studio.Takes);
            s.SaveNow();
            var loaded = GameSession.Load(path);
            Assert.Equal(s.Media.Studio.Takes.Select(t => (t.Key, t.Kind, t.Yes, t.No, t.Question, t.Held, t.Verdict, t.Happened, t.Line)),
                         loaded.Media.Studio.Takes.Select(t => (t.Key, t.Kind, t.Yes, t.No, t.Question, t.Held, t.Verdict, t.Happened, t.Line)));
        }
        finally { foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f); }
    }
}
