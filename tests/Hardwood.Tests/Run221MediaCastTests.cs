using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>W1 (run 221, `claude/world-stories-plan.md`): the media cast. Three fictional columnists per career pick your table
/// before the season, are judged at the end of the regular season, and (pros) the stats writer's power rankings ride in every
/// fourth weekly results message. No random draw.</summary>
public class Run221MediaCastTests
{
    private readonly ITestOutputHelper _out;
    public Run221MediaCastTests(ITestOutputHelper output) => _out = output;

    private static Team T(int id, string city, string name, int ca, int age = 26)
    {
        var t = new Team { Id = id, City = city, Name = name, Abbreviation = name[..3].ToUpperInvariant(), Division = "X" };
        for (int i = 0; i < 9; i++)
            t.Roster.Add(new Player { Id = id * 100 + i, FirstName = "A", LastName = $"B{i}", BirthDate = new DateOnly(2026 - age, 1, 1), CurrentAbility = ca - i, TeamId = id });
        return t;
    }

    [Fact]
    public void The_picks_follow_the_rosters_and_never_change_for_the_same_career()
    {
        var table = Enumerable.Range(1, 12).Select(i => T(i, $"City{i}", $"Club{i}x", 100 + i * 4)).ToList();
        var ours = table[3];
        foreach (var w in MediaCast.CastFor(ours, college: false))
        {
            var a = MediaCast.Pick(w, table, ours, t => QuickSim.Strength(t), null, 31, 2026);
            var b = MediaCast.Pick(w, table, ours, t => QuickSim.Strength(t), null, 31, 2026);
            Assert.Equal(a, b);
            Assert.Equal(12, a.Distinct().Count());
            // The strongest roster is picked near the top and the weakest near the bottom, by every writer.
            Assert.True(a.IndexOf(12) <= 2, $"{w.Name}: the best roster picked {a.IndexOf(12) + 1}");
            Assert.True(a.IndexOf(1) >= 9, $"{w.Name}: the worst roster picked {a.IndexOf(1) + 1}");
            _out.WriteLine($"{w.Byline}: {string.Join(" ", a)}");
        }
        // The stats writer is closest to the rosters' order; a different season gives different hunches somewhere.
        var numbers = MediaCast.Pick(MediaCast.Numbers, table, ours, t => QuickSim.Strength(t), null, 31, 2026);
        var strength = table.OrderByDescending(t => QuickSim.Strength(t)).Select(t => t.Id).ToList();
        Assert.True(numbers.Zip(strength).Count(x => x.First == x.Second) >= 8);
        Assert.InRange(Enumerable.Range(0, 200).Select(i => MediaCast.Hunch(31, 2026, "beat", i)).Min(), -1, -0.5);
        Assert.InRange(Enumerable.Range(0, 200).Select(i => MediaCast.Hunch(31, 2026, "beat", i)).Max(), 0.5, 1);
    }

    [Fact]
    public void A_beat_writer_is_the_same_for_a_team_and_names_are_made_up()
    {
        var a = MediaCast.BeatWriter(T(1, "Boston", "Celtics", 120));
        var b = MediaCast.BeatWriter(T(9, "Boston", "Celtics", 90));
        Assert.Equal(a, b);
        Assert.StartsWith("the Boston ", a.Outlet);
        Assert.Equal(ColumnVoice.Beat, a.Voice);
        var c = MediaCast.CastFor(T(1, "Boston", "Celtics", 120), college: true);
        Assert.Equal(new[] { ColumnVoice.Beat, ColumnVoice.College, ColumnVoice.Numbers }, c.Select(x => x.Voice));
        Assert.Equal(3, c.Select(x => x.Key).Distinct().Count());
    }

    [Fact]
    public void The_verdict_reads_as_the_plan_asks()
    {
        var beat = MediaCast.BeatWriter(T(1, "Fairfax", "Patriots", 120));
        string line = MediaCast.VerdictLine(beat, 11, 1, "George Mason Patriots", "A-10");
        _out.WriteLine(line);
        Assert.StartsWith($"{beat.Name} ({beat.Outlet}) picked you 11th. You won the A-10.", line);
        Assert.Contains("column back", line);
        Assert.Contains("Better than I wrote", MediaCast.VerdictLine(beat, 12, 9, "X", "A-10"));
        Assert.StartsWith("Priya Venkataraman (The Possession Log) picked you 3rd. You finished 9th.", MediaCast.VerdictLine(MediaCast.Numbers, 3, 9, "X", "East"));
        Assert.StartsWith("Walt Brennecke (the Courtside Sentinel) picked you to win the East, and you did.", MediaCast.VerdictLine(MediaCast.OldSchool, 1, 1, "X", "East"));
        Assert.Contains("Told you so", MediaCast.VerdictLine(MediaCast.OldSchool, 5, 6, "X", "East"));
    }

    private static int BenchClub(ulong seed)
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026);
        return probe.Teams.OrderBy(t => QuickSim.Strength(t)).ElementAt(14).Id;
    }

    /// <summary>A pro career (seed 2026) through its regular season: the picks on day one, power rankings about monthly, the
    /// verdict when the playoffs start, and the picks kept through a save and reload.</summary>
    [Fact]
    public void A_pro_season_picks_ranks_and_judges()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run221-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = GameSession.NewGame(2026, BenchClub(2026), path);
            s.Job.Current!.TargetWins = 0;
            s.AdvanceDay();
            var picks = s.MyColumnPicks;
            Assert.Equal(3, picks.Count);
            Assert.All(picks, p => Assert.Equal(s.MyTable.Count, p.Order.Count));
            var msg = Assert.Single(s.Inbox, m => m.Title.StartsWith("Preseason picks:"));
            _out.WriteLine($"{msg.Title}\n{msg.Body}\n");
            Assert.Contains(MediaCast.Numbers.Name, msg.Body);
            Assert.Contains(MediaCast.OldSchool.Name, msg.Body);
            Assert.Contains(MediaCast.BeatWriter(s.HumanTeam).Name, msg.Title);

            while (s.Phase != SessionPhase.Postseason && s.Employed) s.AdvanceDay();
            Assert.True(s.Employed);
            s.AdvanceDay();
            var ranked = s.Inbox.Where(m => GameSession.IsResultsTitle(m.Title) && m.Body.Contains("POWER RANKINGS")).ToList();
            _out.WriteLine($"{ranked.Count} power rankings; last: {ranked.FirstOrDefault()?.Body.Split("POWER RANKINGS")[1]}");
            Assert.InRange(ranked.Count, 4, 7);
            Assert.All(ranked, m => Assert.Single(System.Text.RegularExpressions.Regex.Matches(m.Body, "POWER RANKINGS")));
            var verdict = Assert.Single(s.Inbox, m => m.Title.Contains("the columnists look back"));
            _out.WriteLine($"{verdict.Title}\n{verdict.Body}");
            Assert.All(s.MyColumnPicks, p => Assert.NotNull(p.Judged));
            int finish = s.MyTable.FindIndex(r => r.Team == s.HumanTeam) + 1;
            Assert.All(s.MyColumnPicks, p => Assert.Equal(finish, p.FinishOf(s.HumanTeam.Id)));
            Assert.Contains("biggest miss", verdict.Body);

            s.SaveNow();
            string before = System.Text.Json.JsonSerializer.Serialize(s.Media.Columns);
            var back = GameSession.Load(path);
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(back.Media.Columns));
            back.AdvanceDay();
            Assert.Single(back.Inbox, m => m.Title.Contains("the columnists look back")); // judged once
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>A college career (seed 9, George Mason): the picks ride in the preseason message (no message of their own) and
    /// are judged at Selection Sunday against the conference table.</summary>
    [Fact]
    public void A_college_season_picks_in_the_preseason_message_and_judges_at_selection()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("George Mason"));
        var s = GameSession.NewGame(9, me.Id, savePath: null);
        s.Job.Current!.TargetWins = 0;
        var cw = s.World.College!;
        while (!cw.Season.Selected && s.Employed && s.Phase != SessionPhase.Offseason) s.AdvanceDay();
        s.AdvanceDay();
        var pre = Assert.Single(s.Inbox, m => m.Title.StartsWith("Preseason:"));
        _out.WriteLine($"{pre.Title}\n{pre.Body}\n");
        Assert.Contains("The columnists' picks:", pre.Body);
        Assert.Contains(MediaCast.CollegeWriter.Name, pre.Body);
        Assert.DoesNotContain(s.Inbox, m => m.Title.StartsWith("Preseason picks:"));
        Assert.DoesNotContain(s.Inbox, m => m.Body.Contains("POWER RANKINGS"));
        var picks = s.MyColumnPicks;
        Assert.Equal(3, picks.Count);
        Assert.All(picks, p => Assert.Equal(s.MyTable.Count, p.Order.Count));
        var verdict = Assert.Single(s.Inbox, m => m.Title.Contains("the columnists look back"));
        _out.WriteLine($"{verdict.Title}\n{verdict.Body}");
        Assert.Contains($"Around the {s.ColumnTable}", verdict.Body);
    }
}
