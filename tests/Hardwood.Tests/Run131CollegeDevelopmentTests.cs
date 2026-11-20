using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 131, CP2 (`claude/college-pipeline-design.md`; Danny, Nov 19: "a real 'development' pipeline where i turn out top
/// future nba players"): college growth responds to the coach. Minutes, the program's coaching and staff, its standing and
/// the player's professionalism tilt each summer's step; before, every player was nudged to the same fixed path.
/// </summary>
public class Run131CollegeDevelopmentTests
{
    private readonly ITestOutputHelper _out;
    public Run131CollegeDevelopmentTests(ITestOutputHelper o) => _out = o;

    /// <summary>The freshman (18 on July 1, 2026) heading for the most at 22 below the franchise band, in a fresh seed-31 world.</summary>
    private static (Hardwood.Core.World.GameWorld World, CollegeWorld College, Team Program, Player Player) Freshman()
    {
        var (league, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var c = world.College!;
        var start = new DateOnly(2026, 7, 1);
        var p = c.Programs.SelectMany(t => t.Roster).Where(x => x.Age(start) == 18 && c.Destined.TryGetValue(x.Id, out var d) && d is >= 118 and <= 128)
            .OrderByDescending(x => c.Destined[x.Id]).ThenBy(x => x.Id).First();
        return (world, c, c.Program(p.TeamId!.Value)!, p);
    }

    /// <summary>Four college seasons (three summers of growth): the given minutes each season, then the summer.</summary>
    private static int Develop(CollegeWorld c, Team t, Player p, double mpg, ulong seed)
    {
        var rng = new Rng(seed);
        var dev = new DevelopmentEngine(new Rng(seed + 1));
        for (int year = 2027; year <= 2029; year++)
        {
            var on = new DateOnly(year, 7, 1);
            c.Grow(t, p, p.Age(on), on, mpg, 0.5, dev, rng);
        }
        return p.CurrentAbility;
    }

    private static void GoodStaff(Hardwood.Core.World.GameWorld w, Team t)
    {
        var gen = new StaffGenerator(new Rng(7));
        var league = new League { Name = "test" };
        foreach (var role in Enum.GetValues<StaffRole>()) { var st = gen.Generate(league, role, new DateOnly(2026, 7, 1), 14); st.TeamId = t.Id; t.Staff.Add(st); }
        t.Coach = new Coach(); t.Coach.Attributes[CoachAttr.Development] = 15;
    }

    [Fact]
    public void The_same_prospect_ends_six_to_ten_better_at_30_minutes_with_good_staff_than_buried_on_a_weak_program()
    {
        var diffs = new List<int>();
        foreach (ulong seed in new ulong[] { 1, 2, 3, 4, 5 })
        {
            var a = Freshman(); var b = Freshman();
            Assert.Equal(a.Player.Name, b.Player.Name);
            Assert.Equal(a.Player.CurrentAbility, b.Player.CurrentAbility);
            GoodStaff(a.World, a.Program); a.College.Prestige[a.Program.Id] = 85;
            b.Program.Coach = new Coach(); b.Program.Coach.Attributes[CoachAttr.Development] = 6; b.College.Prestige[b.Program.Id] = 25;
            int good = Develop(a.College, a.Program, a.Player, 30, seed);
            int weak = Develop(b.College, b.Program, b.Player, 5, seed);
            _out.WriteLine($"seed {seed}: {a.Player.Name}, heading for {b.College.Destined[b.Player.Id]}→ good {good} (q {a.College.ProgramQuality(a.Program, a.Player, new DateOnly(2029, 7, 1)):0.0}), weak {weak} (q {b.College.ProgramQuality(b.Program, b.Player, new DateOnly(2029, 7, 1)):0.0})");
            diffs.Add(good - weak);
        }
        _out.WriteLine($"average gap {diffs.Average():0.0}");
        Assert.InRange(diffs.Average(), 6, 10);
        Assert.All(diffs, d => Assert.InRange(d, 3, 13));
    }

    [Fact]
    public void Prestige_alone_doesnt_move_a_graduate_so_it_cant_compound()
    {
        var a = Freshman(); var b = Freshman();
        a.College.Prestige[a.Program.Id] = 99; b.College.Prestige[b.Program.Id] = 50;
        a.Program.Coach = null; b.Program.Coach = null; a.Program.Staff.Clear(); b.Program.Staff.Clear();
        int top = Develop(a.College, a.Program, a.Player, 24, 9), avg = Develop(b.College, b.Program, b.Player, 24, 9);
        _out.WriteLine($"blue blood {top}, average {avg}");
        Assert.Equal(avg, top);
    }

    [Fact]
    public void The_growth_factor_is_neutral_for_a_regular_on_an_average_program()
    {
        Assert.Equal(1.0, CollegeWorld.GrowthFactor(15, CollegeWorld.AverageQuality, 10), 6);
        Assert.Equal(1.0, CollegeWorld.GrowthFactor(21, CollegeWorld.AverageQuality, 10, 0.7), 6);
        Assert.True(CollegeWorld.GrowthFactor(32, 15, 15) > 1.1);
        Assert.True(CollegeWorld.GrowthFactor(3, 6, 5) < 0.9);
    }

    [Fact]
    public void Depth_minutes_follow_ability_when_no_box_scores_were_kept()
    {
        var (_, c, t, _) = Freshman();
        var on = new DateOnly(2027, 7, 1);
        var depth = t.Roster.Where(x => x.Age(on) > 18).OrderByDescending(x => x.CurrentAbility).ThenBy(x => x.Id).ToList();
        Assert.Equal(32, c.SeasonMpg(t, depth[0], on));
        Assert.True(c.SeasonMpg(t, depth[^1], on) <= 7);
    }

    [Fact]
    public void Your_staff_page_projection_for_a_college_player_counts_the_program_and_matches_the_summer()
    {
        var (w, c, t, p) = Freshman();
        var on = new DateOnly(2027, 7, 1);
        GoodStaff(w, t);
        var (change, fromProgram) = c.ExpectedGrowth(t, p, on);
        var (mpg, pivot) = c.Minutes(t, on);
        _out.WriteLine($"{p.Name}: +{change:0.0} this summer at {mpg[p.Id]:0} min (pivot {pivot:0.00}), program {fromProgram:+0.0}");
        Assert.True(change > 3);
        Assert.True(fromProgram > 1, $"good staff adds {fromProgram:0.0}");
        // What the summer actually does, over many seeds, lands on the projection (luck averages out).
        var realised = new List<int>();
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var (_, c2, t2, p2) = Freshman();
            GoodStaff(w, t2);
            int before = p2.CurrentAbility;
            var (m2, pv2) = c2.Minutes(t2, on);
            c2.Grow(t2, p2, p2.Age(on), on, m2[p2.Id], pv2, new DevelopmentEngine(new Rng(seed + 100)), new Rng(seed));
            realised.Add(p2.CurrentAbility - before);
        }
        _out.WriteLine($"realised average {realised.Average():0.0}");
        Assert.InRange(realised.Average(), change - 1.5, change + 1.5);
        // A senior turns pro: no projection.
        var senior = t.Roster.FirstOrDefault(x => x.Age(on) >= CollegeWorld.GraduateAge);
        if (senior != null) Assert.Equal((0.0, 0.0), c.ExpectedGrowth(t, senior, on));
    }

    [Fact]
    public void Across_a_whole_world_the_growth_factor_averages_about_one_so_the_class_doesnt_shrink_or_inflate()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var c = world.College!;
        var on = new DateOnly(2027, 7, 1);
        double w = 0, sum = 0;
        foreach (var t in c.Programs)
        {
            var (mpg, pivot) = c.Minutes(t, on);
            foreach (var p in t.Roster.Where(p => p.Age(on) is > 18 and < CollegeWorld.GraduateAge && c.Destined.ContainsKey(p.Id)))
            {
                double step = Math.Max(1, c.PathStep(p, p.Age(on)));
                w += step; sum += step * CollegeWorld.GrowthFactor(mpg[p.Id], c.ProgramQuality(t, p, on), p.Hidden.Professionalism, pivot);
            }
        }
        _out.WriteLine($"step-weighted mean factor {sum / w:0.000}");
        Assert.InRange(sum / w, 0.99, 1.02);
    }

    [Fact]
    public void A_whole_summer_runs_and_moves_paths_by_minutes()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var c = world.College!;
        var on = new DateOnly(2027, 7, 1);
        var before = c.Destined.ToDictionary(k => k.Key, k => k.Value);
        var starters = new List<int>(); var bench = new List<int>();
        foreach (var t in c.Programs)
        {
            var depth = t.Roster.Where(x => x.Age(on) is > 18 and < CollegeWorld.GraduateAge && c.Destined.ContainsKey(x.Id)).ToList();
            var (mpg, _) = c.Minutes(t, on);
            starters.AddRange(depth.Where(x => mpg[x.Id] >= 26).Select(x => x.Id));
            bench.AddRange(depth.Where(x => mpg[x.Id] <= 7).Select(x => x.Id));
        }
        var gen = new PlayerGenerator(new Rng(5), 5_000_000);
        c.Summer(on, new List<Player>(), gen, new DevelopmentEngine(new Rng(6)), new Rng(7));
        double Moved(List<int> ids) => ids.Where(c.Destined.ContainsKey).Average(id => c.Destined[id] - before[id]);
        _out.WriteLine($"starters' paths {Moved(starters):+0.00;-0.00}, deep bench {Moved(bench):+0.00;-0.00} ({starters.Count}/{bench.Count})");
        Assert.True(Moved(starters) > Moved(bench) + 0.5);
    }
}
