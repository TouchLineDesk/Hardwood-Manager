using Hardwood.Core;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// K4 (critic review #15): `realcheck` threw "Cannot balance home games" because the real 30 sit in D33's divisions of 3 and 4,
/// a shape the builder's fixed patterns didn't cover. Every shape now gets a full schedule with balanced home games.
/// </summary>
public class Run98ScheduleShapeTests
{
    private static League Shape(int[] east, int[] west)
    {
        var league = new League { Name = "Shape" };
        int id = 1;
        foreach (var (conf, sizes) in new[] { (Conference.East, east), (Conference.West, west) })
            for (int d = 0; d < sizes.Length; d++)
                for (int i = 0; i < sizes[d]; i++, id++)
                    league.Teams.Add(new Team { Id = id, City = $"C{id}", Name = $"N{id}", Abbreviation = $"T{id}", Conference = conf, Division = $"{conf}{d}" });
        return league;
    }

    private static void AssertSound(League league, List<ScheduledGame> schedule)
    {
        foreach (var t in league.Teams)
        {
            var mine = schedule.Where(g => g.Home == t || g.Away == t).ToList();
            Assert.InRange(mine.Count, 81, 82);
            int homes = mine.Count(g => g.Home == t);
            Assert.InRange(homes, mine.Count / 2, (mine.Count + 1) / 2);
            Assert.Equal(mine.Count, mine.Select(g => g.Date).Distinct().Count()); // never twice on one day
        }
        Assert.DoesNotContain(schedule, g => g.Home == g.Away);
        // At most one team plays 81 (an odd shortfall).
        Assert.True(league.Teams.Count(t => schedule.Count(g => g.Home == t || g.Away == t) == 81) <= 1);
    }

    public static IEnumerable<object[]> Shapes() => new[]
    {
        new object[] { new[] { 4, 4, 4, 4 }, new[] { 3, 4, 4, 3 } },   // the real 30 in D33's divisions (realcheck)
        new object[] { new[] { 5, 5, 5 }, new[] { 5, 5, 5 } },         // the NBA's real layout
        new object[] { new[] { 4, 4, 4, 4 }, new[] { 4, 4, 4, 4 } },   // D33's 32
        new object[] { new[] { 5, 5, 5 }, new[] { 6, 5, 4 } },         // uneven after a move
        new object[] { new[] { 4, 4, 4, 3 }, new[] { 4, 4, 4, 4 } },   // an odd total: 31 teams
        new object[] { new[] { 4, 4, 4 }, new[] { 4, 4, 4 } },         // three equal divisions of four
        new object[] { new[] { 6, 5, 5 }, new[] { 4, 4, 4, 4 } },
        new object[] { new[] { 3, 3, 3, 3, 3 }, new[] { 4, 4, 4, 4 } },
        // (Divisions of 8 or more play over 82 from division games alone; the game's leagues never have them.)
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Every_division_shape_gets_a_full_balanced_schedule(int[] east, int[] west)
    {
        for (ulong seed = 1; seed <= 3; seed++)
        {
            var league = Shape(east, west);
            AssertSound(league, ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), new Rng(seed)));
        }
    }

    [Fact]
    public void The_real_check_league_builds_a_schedule_on_both_audit_seeds()
    {
        foreach (ulong seed in new ulong[] { 2026, 77 })
        {
            var league = RealCheck.Build(seed);
            Assert.Equal(30, league.Teams.Count);
            AssertSound(league, ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), new Rng(seed)));
        }
    }
}
