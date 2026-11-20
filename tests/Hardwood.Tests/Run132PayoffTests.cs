using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 132, CP3 (`claude/college-pipeline-design.md`): show the payoff of developing players.
/// - A college player's or recruit's potential is a scouting range: it holds his potential, never reads below his ability,
///   narrows with knowledge (and time on your roster) and never becomes exact. Pros stay exact (#119).
/// - Every graduate's program is kept (the alumni record), saved with the career, and read by the alumni tracker.
/// - Draft night tells a college coach where his graduates went, even after a load, and ends with the program's pro record.
/// - The program's pro record is a small, explained recruiting pitch, for the coached program only.
/// </summary>
public class Run132PayoffTests
{
    private readonly ITestOutputHelper _out;
    public Run132PayoffTests(ITestOutputHelper o) => _out = o;

    private static Player P(int id, int ca, int pa) => new() { Id = id, FirstName = "A", LastName = "B", CurrentAbility = ca, PotentialAbility = pa };

    [Fact]
    public void Potential_ranges_hold_the_truth_and_narrow_with_knowledge()
    {
        for (int id = 1; id <= 400; id++)
        {
            int ca = 40 + id % 90, pa = Math.Min(200, ca + id % 70);
            var p = P(id, ca, pa);
            int last = int.MaxValue;
            foreach (var k in new[] { 0.3, 0.5, 0.55, 0.65, 0.75, 0.85, 0.95, 1.0 })
            {
                var r = Scouting.CollegePotential(p, k);
                Assert.InRange(pa, r.Low, r.High);
                Assert.True(r.Low >= ca, $"id {id}: {r} below CA {ca}");
                Assert.False(r.Exact, $"id {id} at {k}: exact");
                Assert.True(r.High <= 200);
                Assert.True(r.High - r.Low <= last, $"id {id}: widened at {k}");
                last = r.High - r.Low;
                Assert.Equal(r, Scouting.CollegePotential(p, k)); // stable from screen to screen
            }
        }
        // About 20 wide for a recruit you know little about, 6 for your own senior.
        var x = P(7, 90, 160);
        Assert.InRange(Scouting.CollegePotential(x, 0.5).High - Scouting.CollegePotential(x, 0.5).Low, 18, 22);
        Assert.InRange(Scouting.CollegePotential(x, 0.85).High - Scouting.CollegePotential(x, 0.85).Low, 4, 7);
    }

    [Fact]
    public void The_pitch_is_small_and_counts_most_with_top_recruits()
    {
        Assert.Equal(0, CollegeWorld.PitchPoints(0, 0));
        Assert.Equal(3, CollegeWorld.PitchPoints(3, 0));
        Assert.Equal(5, CollegeWorld.PitchPoints(3, 1));
        Assert.Equal(CollegeWorld.MaxProPitch, CollegeWorld.PitchPoints(12, 5));
        var c = new CollegeWorld { ProPitch = 4 };
        Assert.Equal(4, c.PitchFor(new Recruit { Stars = 5 }));
        Assert.Equal(4, c.PitchFor(new Recruit { Stars = 4 }));
        Assert.Equal(2, c.PitchFor(new Recruit { Stars = 3 }));
    }

    [Fact]
    public void Graduates_are_followed_into_the_pros_and_through_a_save()
    {
        var (_, world0) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var cw0 = world0.College!;
        int id = cw0.Programs.OrderByDescending(p => cw0.Prestige[p.Id]).First().Id;
        var dir = Path.Combine(Path.GetTempPath(), $"hw-cp3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = GameSession.NewGame(2026, id, path);
            var c = s.World.College!;
            // Ranges where the design says, exact elsewhere.
            var mine = s.HumanTeam.Roster.First();
            Assert.NotNull(s.CollegePotentialOf(mine));
            Assert.Null(s.CollegePotentialOf(s.League.Teams[0].Roster[0]));
            var hs = c.Prospects.First();
            Assert.NotNull(s.CollegePotentialOf(hs));
            // A fresh career has no alumni and no pitch.
            Assert.Empty(c.Alumni);
            Assert.Equal(0, c.ProPitch);
            Assert.Equal("No pros yet", s.ProRecordOf(s.HumanTeam).Summary("NBA"));

            var seniors = c.FinalYear(s.HumanTeam).Select(p => p.Id).ToHashSet();
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            Assert.True(s.Employed, "the top program kept its coach");
            int summer = s.Offseason!.Season + 1;
            // Every program's graduates are on the record, with the summer they turned pro.
            Assert.True(c.Alumni.Count > 300, $"alumni {c.Alumni.Count}");
            var ours = c.Alumni.Where(kv => kv.Value.ProgramId == s.HumanTeam.Id).ToList();
            Assert.Equal(seniors.OrderBy(x => x), ours.Select(kv => kv.Key).OrderBy(x => x));
            Assert.All(ours, kv => Assert.Equal(summer, kv.Value.Year));

            // Save mid-summer and load: the draft message still comes (the graduates come from the record).
            s.SaveNow();
            var back = GameSession.Load(path);
            Assert.Equal(c.Alumni.Count, back.World.College!.Alumni.Count);
            while (back.Phase == SessionPhase.Offseason && back.Offseason!.Report.Draft.Count == 0 && guard++ < 800) back.AdvanceOffseason();
            var draft = back.Inbox.LastOrDefault(m => m.Category == "Draft" && m.Title.StartsWith("Draft:"));
            Assert.NotNull(draft);
            _out.WriteLine($"{draft!.Title}\n{draft.Body}");
            Assert.Contains("Your program in the pros:", draft.Body);

            // The tracker lists them, the pros among them on a club.
            var rec = back.ProRecordOf(back.HumanTeam);
            Assert.Equal(seniors.Count, rec.Lines.Count);
            Assert.All(rec.Lines, l => Assert.Equal(summer, l.Class));
            var drafted = back.Offseason!.Report.Draft.Where(d => seniors.Contains(d.Player.Id)).ToList();
            foreach (var d in drafted) Assert.Contains(rec.Lines, l => l.Player == d.Player && l.Draft != null);
            // Into the new season: the pitch reads where they signed this summer.
            while (back.Phase == SessionPhase.Offseason && guard++ < 1200) back.AdvanceOffseason();
            rec = back.ProRecordOf(back.HumanTeam);
            _out.WriteLine($"{rec.Summary("NBA")} · pitch {back.World.College!.ProPitch} ({back.World.College.ProPitchText})");
            Assert.Equal(CollegeWorld.PitchPoints(rec.TopLeague, rec.Lines.Count(l => l.TopLeague && l.AllStars > 0)), back.World.College.ProPitch);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void The_pitch_counts_only_for_the_coached_program()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var c = world.College!;
        var t = c.Programs[5];
        c.Recruiting = true;
        var p = c.Prospects.First(x => c.RecruitOf(x) is { Stars: >= 4 });
        var r = c.RecruitOf(p)!;
        var board = c.BoardFor(r.ClassOf);
        c.ManagedProgramId = t.Id;
        c.ProPitch = 0;
        double before = c.Appeal(t, p, r, board);
        c.ProPitch = 5;
        Assert.Equal(before + 5, c.Appeal(t, p, r, board), 6);
        var other = c.Programs[6];
        double o1 = c.Appeal(other, p, r, board);
        c.ProPitch = 0;
        Assert.Equal(o1, c.Appeal(other, p, r, board), 6);
    }
}
