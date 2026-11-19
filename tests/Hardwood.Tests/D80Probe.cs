using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 128 (D80 A) probe: roster sizes, classes and graduates over two summers of the real world. Opt-in.</summary>
public class D80Probe
{
    private readonly ITestOutputHelper _out;
    public D80Probe(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("HW_D80_PROBE") == null) return;
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var c = world.College!;
        void Report(string when, DateOnly summer)
        {
            var sizes = c.Programs.Select(t => t.Roster.Count).ToList();
            var ages = c.Programs.SelectMany(t => t.Roster).GroupBy(p => p.Age(summer)).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
            var top = c.Programs.SelectMany(t => t.Roster).OrderByDescending(p => p.CurrentAbility).Take(10).Select(p => p.CurrentAbility);
            var avg = c.Programs.Select(t => t.Roster.OrderByDescending(p => p.CurrentAbility).Take(8).Average(p => p.CurrentAbility)).OrderByDescending(x => x).ToList();
            _out.WriteLine($"{when}: rosters min {sizes.Min()} avg {sizes.Average():F2} max {sizes.Max()}; under 15: {sizes.Count(s => s < 15)}; ages {string.Join(" ", ages)}");
            _out.WriteLine($"   top 10 CA {string.Join(",", top)}; top-8 avg: best {avg[0]:F1}, 25th {avg[24]:F1}, 100th {avg[99]:F1}, median {avg[avg.Count / 2]:F1}");
        }
        Report("start 2026", new DateOnly(2026, 7, 1));
        for (int y = 2027; y <= 2029; y++)
        {
            world.FinishSeasons(new GameSimulator());
            var pool = new List<Player>();
            world.BeginSummer(y, new GameSimulator(), pool);
            var grads = pool.Where(p => p.RookieSeason == y).OrderByDescending(p => p.CurrentAbility).ToList();
            _out.WriteLine($"summer {y}: {grads.Count} graduates, ages {string.Join(" ", grads.GroupBy(p => p.Age(new DateOnly(y, 7, 1))).Select(g => $"{g.Key}:{g.Count()}"))}; top {string.Join(",", grads.Take(12).Select(p => p.CurrentAbility))}; 100+: {grads.Count(p => p.CurrentAbility >= 100)}, 90+: {grads.Count(p => p.CurrentAbility >= 90)}");
            Report($"after summer {y}", new DateOnly(y, 7, 1));
            world.EndSummer(y, new GameSimulator(), pool);
        }
    }
}
