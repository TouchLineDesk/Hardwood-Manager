using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 180, K27 (critic review #22: "the program's arc is invisible in a new career"). Run 173's five-season memory started
/// empty and divided every mark by five, and the mark read the overall record, so Virginia Tech went 6-13, 7-12 and 5-14 in
/// the ACC with prestige 69 → 69 → 69. Now the newest season counts most (weights 40/25/15/12/8, adding to 1), a power
/// program's mark reads its conference record (60%) beside the overall one (40%), and the Season review says the move.
/// </summary>
public class Run180PrestigeArcTests
{
    private readonly ITestOutputHelper _out;
    public Run180PrestigeArcTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void The_mark_reads_the_conference_record_and_the_newest_season_counts_most()
    {
        Assert.Equal(1.0, CollegeWorld.FormWeights.Sum(), 9);
        Assert.Equal(CollegeWorld.PrestigeMemory, CollegeWorld.FormWeights.Length);
        // Virginia Tech's first season on hunt 24: 16-14 overall, 6-13 in the ACC.
        double vt = CollegeWorld.SeasonMark(16, 14, 6, 13, 0, champion: false);
        Assert.Equal((16 / 30.0 - 0.5) * 4 + (6 / 19.0 - 0.5) * 6, vt, 9);
        Assert.True(vt < -0.9, $"a 6-13 ACC season is a bad one: {vt:0.00}");
        Assert.True(CollegeWorld.SeasonMark(16, 14, 0, 0, 0, false) > 0); // the overall record alone called it a winning one
        // Fewer than 8 conference games (an independent): the overall record only, run 173's formula.
        Assert.Equal(CollegeWorld.SeasonMark(20, 10, 0, false), CollegeWorld.SeasonMark(20, 10, 3, 2, 0, false), 9);
        Assert.Equal((20 / 30.0 - 0.5) * 10, CollegeWorld.SeasonMark(20, 10, 0, false), 9);
        // The weights, newest first: one season moves prestige 40% of its mark the first summer, and its whole mark over five.
        Assert.Equal(-2.0 * 0.40, CollegeWorld.FormOfMarks(new[] { -2.0 }), 9);
        Assert.Equal(1.0 * 0.40 + 3.0 * 0.25, CollegeWorld.FormOfMarks(new[] { 3.0, 1.0 }), 9);
        Assert.Equal(2.0, CollegeWorld.FormOfMarks(Enumerable.Repeat(2.0, CollegeWorld.PrestigeMemory).ToList()), 9);
        // An over-long list (never stored) reads only the newest five.
        Assert.Equal(2.0, CollegeWorld.FormOfMarks(new[] { 99.0, 2, 2, 2, 2, 2 }), 9);
        Assert.Equal("−1.4", CollegeWorld.Signed(-1.44));
        Assert.Equal("+0.6", CollegeWorld.Signed(0.55));
        Assert.Equal("0.0", CollegeWorld.Signed(0.04));
    }

    [Fact]
    public void A_season_review_says_the_move_the_summer_then_makes()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        int id = world.College!.Programs.First(p => p.FullName.StartsWith("Virginia Tech")).Id;
        var s = GameSession.NewGame(31, id, null);
        var c = s.World.College!;
        Dictionary<int, (double Before, double Mark, double Form, double After)>? predicted = null;
        int guard = 0;
        while (guard++ < 420)
        {
            if (c.Season.ChampionId != null && predicted == null)
                predicted = c.Programs.ToDictionary(t => t.Id, t => c.PrestigeMove(t.Id));
            s.AdvanceDay();
            if (c.Form.Count > 0) break;
        }
        Assert.NotNull(predicted);
        // The summer applied exactly what was predicted, for every program.
        foreach (var t in c.Programs)
        {
            Assert.Equal(predicted![t.Id].After, c.Prestige[t.Id], 9);
            Assert.Equal(predicted[t.Id].Mark, Assert.Single(c.Form[t.Id]), 9);
        }
        var me = predicted![id];
        var review = s.Inbox.Where(m => m.Title is "Season review" or "You have been sacked").OrderBy(m => m.Date).Last();
        _out.WriteLine(review.Body);
        if (review.Title == "Season review")
        {
            var line = Assert.Single(review.Body.Split('\n'), l => l.StartsWith("Program prestige: "));
            Assert.Contains($"{me.Before:0.0} → {me.After:0.0}", line);
            Assert.Contains($"Five-season form {CollegeWorld.Signed(me.Form)}", line);
            Assert.Contains("in the Atlantic Coast Conference", line);
        }

        // The arc is visible: a power program's bad league season moves prestige, and a good one lifts it.
        var power = c.Programs.Where(t => CollegeWorld.Conferences.First(x => x.Key == c.ConferenceOf[t.Id]).Power).ToList();
        var bad = power.Where(t => c.ConferenceRecord(t.Id) is var r && r.wins + r.losses >= 16 && r.wins <= r.losses * 0.6).ToList();
        var good = power.Where(t => c.ConferenceRecord(t.Id) is var r && r.wins + r.losses >= 16 && r.wins >= r.losses * 2).ToList();
        double Move(Team t) => c.Prestige[t.Id] - predicted[t.Id].Before;
        // What run 173's rule would have done: the overall record's mark over five.
        double OldMove(Team t)
        {
            var (w, l) = c.Record(t.Id);
            int runs = c.Season.Games.Count(g => g.Event == CollegeWorld.Madness && g.Round >= 1 && g.WinnerId == t.Id);
            double before = predicted[t.Id].Before, change = CollegeWorld.SeasonMark(w, l, runs, c.Season.ChampionId == t.Id) / 5;
            if (change > 0) change *= Math.Clamp((100 - before) / 30.0, 0, 1);
            var conf = CollegeWorld.Conferences.First(x => x.Key == c.ConferenceOf[t.Id]);
            return Math.Clamp(before + change + (c.Baseline(t, conf) - before) * 0.08, 10, 99) - before;
        }
        _out.WriteLine($"Virginia Tech: {c.Record(id)} ({c.ConferenceRecord(id)} ACC), mark {me.Mark:0.00}, prestige {me.Before:0.0} → {me.After:0.0} (run 173's rule: {me.Before + OldMove(c.Program(id)!):0.0})");
        _out.WriteLine($"Power programs with a bad league season: {bad.Count}, mean move {bad.Average(Move):0.00} (run 173: {bad.Average(OldMove):0.00})");
        _out.WriteLine($"Power programs with a good league season: {good.Count}, mean move {good.Average(Move):0.00} (run 173: {good.Average(OldMove):0.00})");
        _out.WriteLine($"Every program: mean |move| {c.Programs.Average(t => Math.Abs(Move(t))):0.00} (run 173: {c.Programs.Average(t => Math.Abs(OldMove(t))):0.00})");
        Assert.NotEmpty(bad); Assert.NotEmpty(good);
        Assert.True(bad.Average(Move) < bad.Average(OldMove) - 0.2, "a bad league season costs more than before");
        // Run 222: the floor was -0.5 on this one draw; the EuroLeague's and EuroCup's bigger fields draw more games on the
        // shared stream, and the same 27-program sample now reads -0.47 (still 0.42 below run 173's rule, checked above).
        Assert.True(bad.Average(Move) <= -0.45, $"a bad league season costs about half a point the first summer: {bad.Average(Move):0.00}");
        Assert.True(good.Average(Move) > 0, $"a good one lifts it: {good.Average(Move):0.00}");
    }
}
