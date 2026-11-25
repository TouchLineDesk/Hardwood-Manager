using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// CS9, the coaching carousel (run 191): college jobs open and close each spring with reasons, big jobs hire coaches away
/// from smaller programs (whose jobs open in turn), the spring is one inbox story and a Board panel, and it survives a reload.
/// </summary>
public class Run191CarouselTests
{
    private readonly ITestOutputHelper _out;
    public Run191CarouselTests(ITestOutputHelper output) => _out = output;

    private static GameSession CollegeCareer(string school, string? path)
    {
        var (_, world0) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        int id = world0.College!.Programs.First(p => p.FullName.StartsWith(school)).Id;
        return GameSession.NewGame(9, id, path);
    }

    private static void ToSummer(GameSession s)
    {
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 700) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
    }

    [Fact]
    public void A_spring_carousel_has_reasons_chains_a_story_and_survives_a_reload()
    {
        string dir = Path.Combine(Path.GetTempPath(), "hw-run191-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "career.hwsave");
            var s = CollegeCareer("George Mason", path);
            var before = s.World.College!.Programs.ToDictionary(p => p.Id, p => p.CoachName);
            // A winning season, as the AD sees it: an overachiever gets looked at by bigger programs.
            s.Job.Current!.TargetWins = 0;
            s.Coach.Reputation = 55;
            ToSummer(s);
            var spring = s.LatestCarousel;
            foreach (var m in spring.Take(12)) _out.WriteLine(s.CarouselLine(m));
            _out.WriteLine($"{spring.Count} changes: {spring.Count(m => m.Why == "fired")} fired, {spring.Count(m => m.Why == "retired")} retired, "
                + $"{spring.Count(m => m.Why == "left")} left for bigger jobs, {spring.Count(m => m.OpenToYou)} open to you");

            // CS9's bar: 10+ college coaching changes a spring nationally, each with a reason.
            Assert.InRange(spring.Count, 10, 110);
            Assert.All(spring, m => Assert.Contains(m.Why, new[] { "fired", "retired", "left", "you" }));
            Assert.Contains(spring, m => m.Why == "fired");
            Assert.Equal(spring.Count, spring.Select(m => m.ProgramId).Distinct().Count());
            Assert.All(spring, m => Assert.NotEqual(m.Out, m.In));
            // Chains: every coach who left for a bigger job is the one that job hired, and he's gone from his old program.
            var left = spring.Where(m => m.Why == "left").ToList();
            Assert.NotEmpty(left);
            foreach (var m in left)
            {
                var hire = spring.Single(x => x.ProgramId == m.ToProgramId);
                Assert.Equal(m.ProgramId, hire.FromProgramId);
                Assert.Equal(m.Out, hire.In);
                Assert.Equal(m.Out, s.FindTeam(hire.ProgramId)!.CoachName);
                Assert.NotEqual(m.Out, s.FindTeam(m.ProgramId)!.CoachName);
            }
            // A coach is only hired away by a bigger program (the margin is checked on the spring's prestige; the summer moves
            // every program's a little since, so here it's only "bigger").
            foreach (var m in spring.Where(x => x.FromProgramId != null))
                Assert.True(s.CarouselSize(s.FindTeam(m.ProgramId)!) > s.CarouselSize(s.FindTeam(m.FromProgramId!.Value)!), s.CarouselLine(m));
            // Your own program isn't on it while you keep the job, and the rest kept their coaches.
            Assert.DoesNotContain(spring, m => m.ProgramId == s.HumanTeam.Id);
            var moved = spring.Select(m => m.ProgramId).ToHashSet();
            Assert.All(s.World.College!.Programs.Where(p => !moved.Contains(p.Id) && p != s.HumanTeam), p => Assert.Equal(before[p.Id], p.CoachName));

            // One story in the inbox.
            var story = Assert.Single(s.Inbox, m => m.Title.StartsWith("The coaching carousel"));
            _out.WriteLine(story.Title + "\n" + story.Body);
            Assert.Contains($"{spring.Count} programs", story.Title);
            Assert.Contains("let go", story.Body);

            // A winning coach is looked at: a bigger college job opens to him or comes to him.
            var mine = s.Job.Vacancies.Where(v => v.WhileEmployed).ToList();
            _out.WriteLine($"step-up openings {mine.Count}, offers {mine.Count(v => v.Status == "offered")}");
            Assert.NotEmpty(mine);

            // Saved and reloaded, the carousel is the same.
            s.SaveNow();
            var again = GameSession.Load(path);
            Assert.Equal(s.Carousel, again.Carousel);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void The_carousel_line_says_who_left_why_and_who_came()
    {
        var s = CollegeCareer("George Mason", null);
        var cw = s.World.College!;
        var big = cw.Programs.First(p => p.FullName.StartsWith("Kentucky"));
        var small = cw.Programs.First(p => p.FullName.StartsWith("Drake"));
        Assert.Equal($"{big.FullName}: Pat Moore let go after 14-16; hired Dan Hill from {small.FullName}",
            s.CarouselLine(new CarouselMove(2027, big.Id, big.FullName, "Pat Moore", "Dan Hill", "fired", "14-16", FromProgramId: small.Id)));
        Assert.Equal($"{small.FullName}: Dan Hill left for {big.FullName}; promoted Lee Ross",
            s.CarouselLine(new CarouselMove(2027, small.Id, small.FullName, "Dan Hill", "Lee Ross", "left", "27-6", ToProgramId: big.Id)));
        Assert.Equal($"{small.FullName}: Lee Ross retired; promoted Ann Cole",
            s.CarouselLine(new CarouselMove(2027, small.Id, small.FullName, "Lee Ross", "Ann Cole", "retired")));
    }
}
