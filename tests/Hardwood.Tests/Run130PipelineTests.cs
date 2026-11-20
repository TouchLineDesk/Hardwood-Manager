using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 130, CP1 (`claude/college-pipeline-design.md`; Danny, Nov 19: "a real 'development' pipeline where i turn out top
/// future nba players"): every class has real potential at the top. One or two franchise prospects (heading for 132-145 at
/// 22, potential 165-190) and 8-15 five-star-level prospects (118-135, potential 148-170), drawn per class so the band
/// doesn't shrink as the world gains programs. Before, the best graduate's potential was about 140-150.
/// </summary>
public class Run130PipelineTests
{
    private readonly ITestOutputHelper _out;
    public Run130PipelineTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void The_top_band_has_one_or_two_franchise_prospects_and_eight_to_fifteen_five_stars()
    {
        var rng = new Rng(130);
        int pairs = 0;
        for (int i = 0; i < 400; i++)
        {
            var band = CollegeWorld.TopBand(rng);
            // The franchise prospects come first (a five-star can overlap their range: 132-135 heading, 165-170 potential).
            // (so a pair is counted when the second also looks like one; a five-star does about 6% of the time).
            int franchise = band[1].Destined >= 132 && band[1].Potential >= 165 && band[1].Potential >= band[1].Destined + 25 ? 2 : 1;
            Assert.InRange(band.Count, 9, 17);
            Assert.InRange(band[0].Destined, 132, 145); Assert.InRange(band[0].Potential, 165, 190);
            Assert.All(band, b => { Assert.InRange(b.Destined, 118, 145); Assert.InRange(b.Potential, 148, 190); Assert.True(b.Potential >= b.Destined + 12); });
            Assert.InRange(band.Skip(2).Count(b => b.Potential > 170), 0, 0);
            if (franchise == 2) pairs++;
        }
        Assert.InRange(pairs / 400.0, 0.3, 0.65);
    }

    [Fact]
    public void Every_class_in_a_new_real_world_has_the_top_band_and_the_No_1_recruit_is_special()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var c = world.College!;
        var start = new DateOnly(2026, 7, 1);
        // College classes (ages 18-21) and the four high-school classes.
        for (int age = 18; age <= 21; age++)
        {
            var cls = c.Programs.SelectMany(t => t.Roster).Where(p => p.Age(start) == age).ToList();
            _out.WriteLine($"age {age}: PA 165+ {cls.Count(p => p.PotentialAbility >= 165)}, PA 148+ {cls.Count(p => p.PotentialAbility >= 148)}, best PA {cls.Max(p => p.PotentialAbility)}");
            Assert.InRange(cls.Count(p => p.PotentialAbility >= 165), 1, 9);
            Assert.True(cls.Count(p => p.PotentialAbility >= 148) >= 9);
        }
        for (int classOf = 2027; classOf <= 2030; classOf++)
        {
            var cls = c.ClassOf(classOf).ToList();
            Assert.InRange(cls.Count(p => p.PotentialAbility >= 165), 1, 9);
            var no1 = cls.First(p => c.RecruitOf(p)!.Rank == 1);
            _out.WriteLine($"class of {classOf}: No. 1 {no1.Name} destined {c.Destined[no1.Id]} PA {no1.PotentialAbility}; five-stars PA 148+ {cls.Count(p => c.RecruitOf(p)!.Stars == 5 && p.PotentialAbility >= 148)}");
            // The No. 1 recruit is from the band, and most five-stars are.
            Assert.True(c.Destined[no1.Id] >= 118, $"No. 1 of {classOf} is heading for {c.Destined[no1.Id]}");
            Assert.True(cls.Count(p => c.RecruitOf(p)!.Stars == 5 && p.PotentialAbility >= 148) >= 6);
        }
    }

    [Fact]
    public void A_franchise_prospect_graduates_well_short_of_his_potential_so_the_pros_decide_it()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var c = world.College!;
        var start = new DateOnly(2026, 7, 1);
        var seniors = c.Programs.SelectMany(t => t.Roster).Where(p => p.Age(start) == 21).OrderByDescending(p => p.PotentialAbility).Take(2).ToList();
        foreach (var p in seniors)
        {
            _out.WriteLine($"{p.Name}: CA {p.CurrentAbility}, PA {p.PotentialAbility}, destined {c.Destined[p.Id]}");
            Assert.True(p.PotentialAbility - c.Destined[p.Id] >= 12);
            Assert.True(p.CurrentAbility < 150);
        }
    }

    [Fact]
    public void The_services_call_a_recruit_projected_at_132_or_more_a_franchise_prospect()
    {
        Assert.Equal("franchise prospect", CollegeWorld.ProspectTag(new Recruit { Projection = 138 }));
        Assert.Equal("", CollegeWorld.ProspectTag(new Recruit { Projection = 131 }));
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var c = world.College!;
        // Every class has someone the services call that, but only a few.
        for (int classOf = 2027; classOf <= 2030; classOf++)
            Assert.InRange(c.ClassOf(classOf).Count(p => CollegeWorld.ProspectTag(c.RecruitOf(p)!) != ""), 1, 8);
    }
}
