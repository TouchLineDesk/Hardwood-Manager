using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 128, D80 A (Danny, by email Nov 19): four college years. Players enroll at 18 and turn pro at 22, so a program has
/// four classes (Fr/So/Jr/Sr) and the seniors play their final season. A real world's 365 programs need about 1,370
/// players a class, so a class is that big (the top keeps the old calibration) and any place still open in the summer
/// goes to a late signing. A career from before it moves over in one summer: two classes graduate, every roster refills.
/// </summary>
public class Run128FourYearTests
{
    private readonly ITestOutputHelper _out;
    public Run128FourYearTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void A_new_real_world_has_four_full_classes_and_graduates_at_22_into_full_rosters()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var c = world.College!;
        var start = new DateOnly(2026, 7, 1);
        Assert.Equal(4, CollegeWorld.CollegeYears);
        Assert.All(c.Programs, t =>
        {
            Assert.Equal(CollegeWorld.Squad, t.Roster.Count);
            Assert.Equal(CollegeWorld.WalkOns, t.Roster.Count(p => c.WalkOnIds.Contains(p.Id)));
            Assert.All(t.Roster, p => Assert.InRange(p.Age(start), 18, 21));
            Assert.Equal(4, t.Roster.Select(p => p.Age(start)).Distinct().Count());
        });
        Assert.True(c.ClassSize >= c.Programs.Count * CollegeWorld.Squad / CollegeWorld.CollegeYears, $"class of {c.ClassSize}");
        Assert.Equal(c.ClassSize, c.ClassOf(2027).Count());

        world.FinishSeasons(new GameSimulator());
        var pool = new List<Player>();
        world.BeginSummer(2027, new GameSimulator(), pool);
        var grads = pool.Where(p => p.RookieSeason == 2027).ToList();
        var summer = new DateOnly(2027, 7, 1);
        Assert.All(grads, p => Assert.Equal(22, p.Age(summer)));
        Assert.All(c.Programs, t =>
        {
            Assert.Equal(CollegeWorld.Squad, t.Roster.Count);
            Assert.All(t.Roster, p => Assert.InRange(p.Age(summer), 18, 21));
        });
        // Four years don't add stars: the class's top end is as tuned (a handful of 100+ graduates a year).
        Assert.InRange(grads.Count(p => p.CurrentAbility >= 100), 2, 20);
        _out.WriteLine($"{grads.Count} graduates; 100+ {grads.Count(p => p.CurrentAbility >= 100)}, 90+ {grads.Count(p => p.CurrentAbility >= 90)}");
    }

    [Fact]
    public void Class_years_are_four_and_an_older_save_reads_both_final_classes_as_seniors()
    {
        var p = new Player { FirstName = "A", LastName = "B", BirthDate = new DateOnly(2008, 3, 1) };
        // The 2026-27 season: next summer he is 19 (freshman); four seasons later 22 (senior, final).
        Assert.Equal("Fr", CollegeWorld.ClassYearOf(p, 2026)!.Short);
        Assert.Equal("So", CollegeWorld.ClassYearOf(p, 2027)!.Short);
        Assert.Equal("Jr", CollegeWorld.ClassYearOf(p, 2028)!.Short);
        var sr = CollegeWorld.ClassYearOf(p, 2029)!;
        Assert.Equal(("Sr", "Senior", true), (sr.Short, sr.Long, sr.Final));
        // A career saved under five-year college: someone 23 next summer is a senior in his final season too.
        var old = CollegeWorld.ClassYearOf(p, 2030)!;
        Assert.Equal(("Sr", true), (old.Short, old.Final));
        Assert.Null(CollegeWorld.ClassYearOf(p, 2025)); // still in high school
    }

    [Fact]
    public void A_save_from_before_four_year_college_moves_over_in_one_summer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run128-{Guid.NewGuid():N}.hwsave");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "SaveFixtures", "real-v13.hwsave"), path);
        try
        {
            var s = GameSession.Load(path);
            var c = s.World.College!;
            var summer = new DateOnly(2027, 7, 1);
            // Five age groups on the rosters; the two oldest both graduate next summer and both read "Sr, final season".
            var oldest = c.Programs.SelectMany(t => t.Roster).Where(x => x.Age(summer) >= 22).ToList();
            Assert.Contains(oldest, x => x.Age(summer) == 23);
            Assert.Contains(oldest, x => x.Age(summer) == 22);
            Assert.All(oldest, x => Assert.Equal(("Sr", true), (CollegeWorld.ClassYearOf(x, 2026)!.Short, CollegeWorld.ClassYearOf(x, 2026)!.Final)));
            int before = c.ClassOf(2027).Count();

            s.World.FinishSeasons(new GameSimulator());
            var pool = new List<Player>();
            s.World.BeginSummer(2027, new GameSimulator(), pool);
            var grads = pool.Where(x => x.RookieSeason == 2027).ToList();
            Assert.Equal(oldest.Count, grads.Count);
            Assert.All(c.Programs, t =>
            {
                Assert.Equal(CollegeWorld.Squad, t.Roster.Count);
                Assert.Equal(CollegeWorld.WalkOns, t.Roster.Count(x => c.WalkOnIds.Contains(x.Id)));
                Assert.All(t.Roster, x => Assert.InRange(x.Age(summer), 18, 21));
            });
            // The classes still in high school were made for five-year college; the one made this summer is full size.
            Assert.Equal(c.ClassSize, c.ClassOf(2031).Count());
            _out.WriteLine($"old class of 2027: {before}; graduates {grads.Count} (22: {grads.Count(x => x.Age(summer) == 22)}, 23: {grads.Count(x => x.Age(summer) == 23)})");
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void A_graduate_at_22_signs_a_rookie_scale_first_deal()
    {
        // Review of PR #192: graduates now turn pro inside the rookie-scale age, as NBA rookies do; the established pros' prices
        // (22-year-olds already in the league) are unchanged, so the pinned bench seasons are too.
        var fin = Hardwood.Core.Model.Finance.HardCapDefault;
        Assert.True(ContractMarket.MarketSalary(110, CollegeWorld.GraduateAge, fin) < ContractMarket.MarketSalary(110, 23, fin));
        Assert.Equal(ContractMarket.MarketSalary(110, 21, fin), ContractMarket.MarketSalary(110, 22, fin));
    }
}
