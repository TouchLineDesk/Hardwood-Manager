using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>The classic-era roster model (Danny, Nov 11, decisions #159-#167): high-school prospects in a recruiting
/// pool, 13 scholarships and 2 walk-ons, commitments and signing day, enrolment at 18, everyone pro at 23.</summary>
public class CollegeRecruitingTests
{
    private static (League nabl, GameWorld world) NewWorld(ulong seed = 9)
    {
        var nabl = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        return (nabl, GameWorld.Generate(nabl, seed, Array.Empty<string>(), new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Four_high_school_classes_are_ranked_with_real_star_counts()
    {
        var (_, world) = NewWorld();
        var c = world.College!;
        for (int cls = 2027; cls <= 2030; cls++)
        {
            var recruits = c.ClassOf(cls).Select(p => c.Recruits[p.Id]).ToList();
            Assert.Equal(CollegeWorld.ClassTracked, recruits.Count);
            Assert.Equal(CollegeWorld.FiveStars, recruits.Count(r => r.Stars == 5));
            Assert.Equal(CollegeWorld.FourStars, recruits.Count(r => r.Stars == 4));
            Assert.Equal(CollegeWorld.ThreeStars, recruits.Count(r => r.Stars == 3));
            Assert.Equal(Enumerable.Range(1, CollegeWorld.ClassTracked), recruits.Select(r => r.Rank).OrderBy(x => x));
            Assert.All(recruits, r => { Assert.False(string.IsNullOrEmpty(r.HighSchool)); Assert.Equal(2, r.State.Length); });
            // Stars are an opinion, but a good one: five-stars are headed far higher than two-stars.
            double Dest(int stars) => c.ClassOf(cls).Where(p => c.Recruits[p.Id].Stars == stars).Average(p => c.Destined[p.Id]);
            Assert.True(Dest(5) > Dest(4) + 5 && Dest(4) > Dest(3) && Dest(3) > Dest(2) + 10, $"{Dest(5):F0} {Dest(4):F0} {Dest(3):F0} {Dest(2):F0}");
        }
        // Every program: 13 scholarships and 2 walk-ons.
        Assert.All(c.Programs, t => Assert.Equal(CollegeWorld.WalkOns, t.Roster.Count(p => c.WalkOnIds.Contains(p.Id))));
        // The senior class has started committing (September to October 1), the best first.
        Assert.InRange(c.ClassOf(2027).Count(p => c.Recruits[p.Id].CommittedTo != null), 5, 400);
    }

    [Fact]
    public void Seniors_commit_through_the_season_and_sign_by_signing_day_without_overfilling_anyone()
    {
        var (_, world) = NewWorld();
        var c = world.College!;
        world.FinishSeasons(new GameSimulator());
        c.RecruitingThrough(new DateOnly(2027, 4, 20), new Rng(1));
        var cls = c.ClassOf(2027).Select(p => c.Recruits[p.Id]).ToList();
        // Every five- and four-star has a school; no program has more than its open scholarships.
        Assert.All(cls.Where(r => r.Stars >= 4), r => Assert.NotNull(r.CommittedTo));
        var board = c.BoardFor(2027);
        Assert.All(c.Programs, t => Assert.True(board.Open[t.Id] >= 0, $"{t.FullName} over by {-board.Open[t.Id]}"));
        int scholarships = c.Programs.Sum(t => CollegeWorld.Scholarships) - c.Programs.Sum(t => t.Roster.Count(p => p.Age(new DateOnly(2027, 7, 1)) < 23 && !c.WalkOnIds.Contains(p.Id)));
        Assert.Equal(scholarships, cls.Count(r => r.CommittedTo != null));
        // Commitments were spread through the season, not all on signing day.
        Assert.True(cls.Count(r => r.CommittedOn < new DateOnly(2027, 1, 1)) > 50, $"{cls.Count(r => r.CommittedOn < new DateOnly(2027, 1, 1))} of {cls.Count(r => r.CommittedTo != null)}; {string.Join(",", cls.Where(r => r.CommittedOn != null).GroupBy(r => r.CommittedOn!.Value.Month).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"))}");
        // The big programs land the big classes.
        var ranking = c.ClassRanking(2027);
        double Prestige(IEnumerable<Team> ts) => ts.Average(t => c.Prestige[t.Id]);
        Assert.True(Prestige(ranking.Take(20).Select(x => x.Program)) > Prestige(ranking.Skip(100).Select(x => x.Program)) + 10, $"{Prestige(ranking.Take(20).Select(x => x.Program)):F1} vs {Prestige(ranking.Skip(100).Select(x => x.Program)):F1}");
        // Home matters: more players stay in their region than chance alone would send there.
        double home = cls.Where(r => r.CommittedTo != null).Average(r => CollegeWorld.RegionOf(CollegeWorld.HomeState(c.Program(r.CommittedTo!.Value)!)) == CollegeWorld.RegionOf(r.State) ? 1.0 : 0.0);
        Assert.True(home > 0.3, $"{home:P0} stayed in their region");
        Assert.Contains(world.News, n => n.Title.StartsWith("Signing day"));
    }

    [Fact]
    public void The_class_enrolls_at_18_the_unsigned_leave_and_a_new_class_of_14_year_olds_arrives()
    {
        var (_, world) = NewWorld();
        var c = world.College!;
        world.FinishSeasons(new GameSimulator());
        var signed = c.ClassOf(2027).Where(p => c.Recruits[p.Id].CommittedTo != null).ToDictionary(p => p, p => c.Recruits[p.Id].CommittedTo!.Value);
        var all = c.ClassOf(2027).ToList();
        var pool = new List<Player>();
        world.BeginSummer(2027, new GameSimulator(), pool);
        Assert.NotEmpty(signed);
        Assert.All(signed, kv => Assert.Equal(kv.Value, kv.Key.TeamId));
        Assert.All(c.Programs, t =>
        {
            Assert.Equal(CollegeWorld.Squad, t.Roster.Count);
            Assert.Equal(CollegeWorld.WalkOns, t.Roster.Count(p => c.WalkOnIds.Contains(p.Id)));
        });
        // The unsigned are gone: not in college, not in the pros' pool.
        var unsigned = all.Where(p => p.TeamId == null).ToList();
        Assert.NotEmpty(unsigned);
        Assert.All(unsigned, p => { Assert.DoesNotContain(p, c.Players); Assert.DoesNotContain(p, pool); Assert.False(c.Recruits.ContainsKey(p.Id)); });
        Assert.Empty(c.ClassOf(2027));
        Assert.Equal(CollegeWorld.ClassTracked, c.ClassOf(2031).Count());
        Assert.All(c.ClassOf(2031), p => Assert.Equal(14, p.Age(new DateOnly(2027, 7, 1))));
        // Graduates are still 23 and still feed the pros.
        Assert.All(pool.Where(p => p.RookieSeason == 2027), p => Assert.Equal(23, p.Age(new DateOnly(2027, 7, 1))));
        Assert.Contains(pool, p => p.RookieSeason == 2027 && p.CurrentAbility >= 100);
    }

    [Fact]
    public void Prospects_and_their_recruiting_survive_save_and_load()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-recruit-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = GameSession.NewGame(2026, 5, path);
            while (s.Today < new DateOnly(2026, 11, 20)) s.AdvanceDay();
            s.SaveNow();
            var c = s.World.College!;
            var l = GameSession.Load(path).World.College!;
            Assert.True(l.Recruiting);
            Assert.Equal(c.Prospects.Count, l.Prospects.Count);
            Assert.Equal(c.WalkOnIds.OrderBy(x => x), l.WalkOnIds.OrderBy(x => x));
            Assert.Equal(c.RecruitingDay, l.RecruitingDay);
            var top = c.ClassOf(2027).OrderBy(p => c.Recruits[p.Id].Rank).First();
            var (a, b) = (c.Recruits[top.Id], l.Recruits[top.Id]);
            Assert.Equal((a.HighSchool, a.State, a.ClassOf, a.Stars, a.Rank, a.CommittedTo, a.CommittedOn), (b.HighSchool, b.State, b.ClassOf, b.Stars, b.Rank, b.CommittedTo, b.CommittedOn));
            Assert.Null(l.Prospects.First(p => p.Id == top.Id).TeamId);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Every_program_has_a_real_home_state()
    {
        var (_, world) = NewWorld();
        Assert.All(world.College!.Programs, t => Assert.False(string.IsNullOrEmpty(CollegeWorld.RegionOf(CollegeWorld.HomeState(t))), t.City));
    }
}
