using Hardwood.Core.College;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Slice 9 of the revised launch plan (#1164, run 200): rival coaches as characters, with H202 (the carousel fired winning and
/// first-year coaches) and H203 (computer coaches shared names).
/// </summary>
public class Run200RivalCoachesTests
{
    private readonly ITestOutputHelper _out;
    public Run200RivalCoachesTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void An_athletic_director_gives_a_coach_time()
    {
        // First and second seasons are safe, whatever the record.
        Assert.Equal(0, GameSession.CarouselFireChance(1, 4, 26, false, 15, 1, 5));
        Assert.Equal(0, GameSession.CarouselFireChance(2, 4, 26, false, 15, 2, 5));
        // A winning season is safe: .650+, or .600+ with a bid (Memphis 26-4, Texas Tech 24-6, UCLA 22-10 with a bid).
        Assert.Equal(0, GameSession.CarouselFireChance(6, 26, 4, false, 2, 1, 5));
        Assert.Equal(0, GameSession.CarouselFireChance(6, 21, 10, false, 6, 1, 5));
        Assert.Equal(0, GameSession.CarouselFireChance(6, 22, 10, true, 6, 3, 5));
        // One poor season costs a little; three in a row cost a lot more.
        double one = GameSession.CarouselFireChance(5, 12, 19, false, 9, 1, 10);
        double three = GameSession.CarouselFireChance(5, 12, 19, false, 9, 3, 10);
        Assert.InRange(one, 0.05, 0.3);
        Assert.True(three > one * 2, $"{one} vs {three}");
        // A season that met the target: only a losing one carries the base chance.
        Assert.Equal(0, GameSession.CarouselFireChance(5, 17, 14, false, 0, 0, 10));
        Assert.InRange(GameSession.CarouselFireChance(5, 13, 17, false, 0, 0, 10), 0.005, 0.03);
    }

    [Fact]
    public void Names_never_repeat_even_when_the_lists_run_short()
    {
        var taken = new HashSet<string>();
        var rng = new Hardwood.Core.Rng(5);
        for (int i = 0; i < 3000; i++) Assert.True(taken.Add(Hardwood.Core.Jobs.Board.NewCoachName(rng, taken.Contains)));
    }

    [Fact]
    public void Coaches_have_a_style_a_past_and_a_voice()
    {
        var c = new BenchCoach { ProgramId = 1, Name = "Phil Hollis", Seasons = 8, Wins = 170, Losses = 90, CareerWins = 200, CareerLosses = 120, Bids = 5 };
        Assert.Equal(RivalCoaches.StyleOf("Phil Hollis"), c.Style);
        Assert.Equal("in his ninth season", c.SeasonText);
        Assert.InRange(RivalCoaches.Reputation(c, 70), 40, 90);
        Assert.StartsWith("\"", RivalCoaches.RivalryQuote(c, true, "Richmond", "the Capital Clash"));
        Assert.Equal("22nd", RivalCoaches.Ordinal(22));
    }

    /// <summary>The probe from bug hunt 26: a real world (seed 9, George Mason), three springs. No firing at .650+ or in a
    /// coach's first two seasons, a believable number of changes, every bench its own name, and the book survives a reload.</summary>
    [Fact]
    public void Three_springs_fire_only_after_poor_seasons_and_every_bench_keeps_its_own_name()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("George Mason"));
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run200-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = GameSession.NewGame(9, me.Id, path);
            s.Job.Current!.TargetWins = 0; // keep the job through three springs
            AssertUniqueBenches(s, "day one");
            var cw = s.World.College!;
            Assert.Equal(cw.Programs.Count - 1, cw.Benches.Count);
            _out.WriteLine("Day one: " + string.Join("; ", cw.RivalsOf(me.Id).Take(2).Select(r => cw.Program(r.RivalId)!.FullName + ": " + cw.CoachLine(r.RivalId))));

            var end = new DateOnly(2029, 6, 1);
            int springs = 0, highFired = 0, earlyFired = 0;
            var tenure = new Dictionary<int, int>();
            int lastYear = 0;
            while (s.Today < end && s.Employed)
            {
                // Just before the carousel, note each bench's seasons there.
                if (s.Phase != SessionPhase.Offseason && s.Carousel.FirstOrDefault()?.Year != s.Today.Year)
                    foreach (var b in cw.Benches) tenure[b.ProgramId] = b.Seasons;
                if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay();
                if (s.Carousel.Count > 0 && s.Carousel[0].Year != lastYear)
                {
                    lastYear = s.Carousel[0].Year;
                    springs++;
                    var spring = s.LatestCarousel;
                    var fired = spring.Where(m => m.Why == "fired").ToList();
                    foreach (var m in fired)
                    {
                        var parts = m.Record.Split('-').Select(int.Parse).ToArray();
                        double pct = parts[0] / (double)Math.Max(1, parts[0] + parts[1]);
                        if (pct >= 0.65) highFired++;
                        if (tenure.GetValueOrDefault(m.ProgramId, 99) + 1 <= 2) earlyFired++;
                    }
                    _out.WriteLine($"Spring {lastYear}: {spring.Count} changes, {fired.Count} let go (best record {fired.Select(m => m.Record).OrderByDescending(r => int.Parse(r.Split('-')[0])).FirstOrDefault()}), "
                        + $"{spring.Count(m => m.Why == "retired")} retired, {spring.Count(m => m.Why == "left")} hired away");
                    foreach (var m in fired.Take(4)) _out.WriteLine("   " + s.CarouselLine(m) + $" (seasons there: {tenure.GetValueOrDefault(m.ProgramId) + 1})");
                    Assert.InRange(spring.Count, 20, 75);
                    AssertUniqueBenches(s, $"spring {lastYear}");
                    // A new coach starts at 0 seasons there; a hired-away one brings his career.
                    foreach (var m in spring.Where(m => m.FromProgramId != null && !m.OpenToYou))
                        if (cw.BenchOf(m.ProgramId) is { } nb && nb.Name == m.In) Assert.True(nb.CareerWins > 0, m.In);
                }
            }
            Assert.True(springs >= 3, $"springs {springs}");
            Assert.Equal(0, highFired);
            Assert.Equal(0, earlyFired);
            Assert.Contains(cw.Benches, b => b.Seasons >= 3 && b.PoorRun >= 0);

            s.SaveNow();
            string before = System.Text.Json.JsonSerializer.Serialize(cw.Benches.OrderBy(b => b.ProgramId));
            var back = GameSession.Load(path);
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(back.World.College!.Benches.OrderBy(b => b.ProgramId)));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>An older save (no bench book) gets unique names and every coach's past on its first load.</summary>
    [Fact]
    public void An_older_save_gets_its_benches_on_load()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("George Mason"));
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run200b-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = GameSession.NewGame(9, me.Id, path);
            var cw = s.World.College!;
            // As before v0.188.0: two benches share a name, and there's no book.
            var a = cw.Programs.First(p => p != s.HumanTeam);
            var b = cw.Programs.Last(p => p != s.HumanTeam);
            b.CoachName = a.CoachName;
            cw.Benches.Clear();
            s.SaveNow();
            var back = GameSession.Load(path);
            AssertUniqueBenches(back, "old save");
            Assert.Equal(back.World.College!.Programs.Count - 1, back.World.College.Benches.Count);
            Assert.Equal(a.CoachName, back.FindTeam(a.Id)!.CoachName); // the first holder keeps it
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private void AssertUniqueBenches(GameSession s, string when)
    {
        var names = s.League.Teams.Concat(s.World.Clubs).Concat(s.World.College!.Programs)
            .Where(t => t != s.HumanTeam && t.CoachName is not "" and not "Interim coach").Select(t => t.CoachName).ToList();
        var dupes = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => $"{g.Key} x{g.Count()}").ToList();
        Assert.True(dupes.Count == 0, $"{when}: {string.Join(", ", dupes.Take(10))}");
    }
}
