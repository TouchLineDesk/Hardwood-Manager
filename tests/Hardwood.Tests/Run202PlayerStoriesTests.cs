using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Slice 11 of the revised launch plan (#1164, run 202): player stories over four years (the freshman phenom, the
/// hometown hero, the walk-on, the senior's breakout, the overlooked recruit), told once from real game data, with every college
/// player's origin kept from his recruitment.</summary>
public class Run202PlayerStoriesTests
{
    private readonly ITestOutputHelper _out;
    public Run202PlayerStoriesTests(ITestOutputHelper output) => _out = output;

    private static Player P(int id = 1) => new() { Id = id, FirstName = "Jalen", LastName = "Storey", BirthDate = new DateOnly(2007, 1, 1) };
    private static Team T() => new() { Id = 5, City = "Nowhere", Name = "Owls", Abbreviation = "NOW", Division = "College" };
    private static StatLine Line(int g, int pts, int gs = 0, double min = 0) => new() { G = g, Pts = pts, Gs = gs, Min = min, Reb = g * 4, Ast = g * 2 };
    private static readonly ClassYear Fr = new(1, "Fr", "Freshman", false), Jr = new(3, "Jr", "Junior", false), Sr = new(4, "Sr", "Senior", true);
    private static readonly PlayerOrigin Far = new() { State = "ZZ", Stars = 3, Rank = 212, ClassOf = 2026 };

    private static List<string> Keys(StatLine st, ClassYear cy, PlayerOrigin? o = null, bool walkOn = false, bool leads = false, int priorG = 0, int priorPts = 0) =>
        PlayerStories.InSeason(P(), T(), st, cy, o ?? Far, walkOn, leads, priorG, priorPts).Select(x => x.Key).ToList();

    [Fact]
    public void Stories_follow_the_numbers()
    {
        // Not before ten games.
        Assert.Empty(Keys(Line(9, 200), Fr));
        // The freshman phenom: 14+ a game, or the team's leading scorer at 10+.
        Assert.Equal(new[] { "phenom" }, Keys(Line(12, 170), Fr));
        Assert.Empty(Keys(Line(12, 130), Fr));
        Assert.Equal(new[] { "phenom" }, Keys(Line(12, 130), Fr, leads: true));
        Assert.Empty(Keys(Line(12, 170), Jr)); // not a freshman
        // The walk-on who earned his minutes: 15+ minutes a game, or 5 starts.
        Assert.Equal(new[] { "walkon" }, Keys(Line(12, 40, min: 190), Jr, walkOn: true));
        Assert.Equal(new[] { "walkon" }, Keys(Line(12, 40, gs: 5, min: 100), Jr, walkOn: true));
        Assert.Empty(Keys(Line(12, 40, min: 100), Jr, walkOn: true));
        // The senior's breakout: 12+ a game and 6+ over his average before, over 20+ games.
        Assert.Equal(new[] { "breakout" }, Keys(Line(12, 180), Sr, priorG: 60, priorPts: 360));
        Assert.Empty(Keys(Line(12, 180), Sr, priorG: 60, priorPts: 600)); // he was already good
        Assert.Empty(Keys(Line(12, 180), Sr, priorG: 15, priorPts: 30));  // too few games before
        Assert.Empty(Keys(Line(12, 180), Jr, priorG: 60, priorPts: 360)); // not a senior
        var body = PlayerStories.InSeason(P(), T(), Line(12, 180), Sr, Far, false, false, 60, 360).Single().Body;
        Assert.Contains("6.0 points over his first 60 college games", body);
        Assert.Contains("15.0 points", body);
    }

    [Fact]
    public void The_overlooked_story_needs_a_three_star_or_a_walk_on_with_an_honour()
    {
        var honours = new List<string> { "All-America second team" };
        Assert.NotNull(PlayerStories.Overlooked(P(), T(), Far, false, honours));
        Assert.Null(PlayerStories.Overlooked(P(), T(), new PlayerOrigin { State = "OH", Stars = 4, Rank = 50, ClassOf = 2026 }, false, honours));
        Assert.NotNull(PlayerStories.Overlooked(P(), T(), new PlayerOrigin { State = "OH", Stars = 4, Rank = 50, ClassOf = 2026 }, true, honours));
        Assert.Null(PlayerStories.Overlooked(P(), T(), Far, false, new List<string>()));
        Assert.Equal("From Ohio · a four-star recruit, No. 50 in the class of 2026",
            PlayerStories.OriginLine(new PlayerOrigin { State = "OH", Stars = 4, Rank = 50, ClassOf = 2026 }, false));
        Assert.Equal("From Ohio · a four-star recruit", PlayerStories.OriginLine(new PlayerOrigin { State = "OH", Stars = 4, Rank = 50, Seeded = true }, false));
    }

    [Fact]
    public void The_hometown_hero_stays_home()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        var t = world.College!.Programs.First(x => CollegeWorld.HomeState(x) == "VA");
        var home = new PlayerOrigin { State = "VA", Stars = 3, Rank = 300, ClassOf = 2025 };
        Assert.Contains("hometown", PlayerStories.InSeason(P(), t, Line(12, 60, gs: 10), Jr, home, false, false, 0, 0).Select(x => x.Key));
        Assert.Contains("hometown", PlayerStories.InSeason(P(), t, Line(12, 150), Jr, home, false, false, 0, 0).Select(x => x.Key));
        Assert.DoesNotContain("hometown", PlayerStories.InSeason(P(), t, Line(12, 60, gs: 3), Jr, home, false, false, 0, 0).Select(x => x.Key));
        Assert.DoesNotContain("hometown", PlayerStories.InSeason(P(), t, Line(12, 150), Jr, Far, false, false, 0, 0).Select(x => x.Key));
    }

    /// <summary>A real world (seed 9, George Mason) through one season and into the next: every rostered player has an origin
    /// (seeded on day one, kept from recruiting for the new class), stories are told once each, and they survive a reload.</summary>
    [Fact]
    public void A_season_tells_stories_once_and_keeps_origins()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("George Mason"));
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run202-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = GameSession.NewGame(9, me.Id, path);
            s.Job.Current!.TargetWins = 0;
            var cw = s.World.College!;
            var rostered = cw.Programs.SelectMany(t => t.Roster).ToList();
            Assert.All(rostered, p => Assert.NotNull(cw.OriginOf(p.Id)));
            Assert.All(rostered, p => Assert.True(cw.OriginOf(p.Id)!.Seeded));
            // About the right shape: roughly 25 five-stars per class year.
            int fives = rostered.Count(p => cw.OriginOf(p.Id)!.Stars == 5);
            _out.WriteLine($"Day one: {rostered.Count} players, {fives} five-stars, {rostered.Count(p => CollegeWorld.HomeState(cw.Program(p.TeamId!.Value)!) == cw.OriginOf(p.Id)!.State)} from home");
            Assert.InRange(fives, 80, 120);

            var end = new DateOnly(2027, 11, 20);
            while (s.Today < end && s.Employed)
            {
                if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay();
            }
            Assert.True(s.Employed);
            foreach (var st in cw.Stories.Told) _out.WriteLine($"{st.Date} {st.Key}: {st.Title}\n    {st.Text}");
            Assert.Equal(cw.Stories.Told.Count, cw.Stories.Told.Select(x => (x.PlayerId, x.Key)).Distinct().Count());
            Assert.All(cw.Stories.Told, st => Assert.Equal(1, s.Inbox.Count(m => m.Title == st.Title)));
            Assert.All(cw.Stories.Told, st => Assert.True(st.Posted));
            Assert.NotEmpty(cw.Stories.Told);
            // The new class came through recruiting: its origins are the recruits' own.
            var now = cw.Programs.SelectMany(t => t.Roster).ToList();
            Assert.All(now, p => Assert.NotNull(cw.OriginOf(p.Id)));
            Assert.Equal(now.Count, cw.Stories.Origins.Count); // graduates' origins are dropped
            var freshmen = now.Where(p => cw.ClassYearOf(p, s.Today)?.Year == 1).ToList();
            Assert.True(freshmen.Count(p => !cw.OriginOf(p.Id)!.Seeded) > freshmen.Count * 0.8, "most freshmen were recruited");
            Assert.Contains(freshmen, p => cw.OriginOf(p.Id)!.Rank > 0 && !cw.OriginOf(p.Id)!.Seeded && cw.OriginOf(p.Id)!.ClassOf == 2027);

            s.SaveNow();
            string before = System.Text.Json.JsonSerializer.Serialize(cw.Stories);
            var back = GameSession.Load(path);
            Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(back.World.College!.Stories));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
