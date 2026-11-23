using Hardwood.Core.College;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 171, CS7 part 2 (K21's 25-season drift, H173): a four- or five-star weighs the blue-chips a program already has
/// next season, so the blue bloods can't stack five-stars year after year. The class board counts each program's
/// blue-chips (returning players, signed classes and commitments) and keeps the count as players commit or flip.
/// </summary>
public class Run171StackingTests
{
    private readonly ITestOutputHelper _out;
    public Run171StackingTests(ITestOutputHelper o) => _out = o;

    private static Recruit Stars(int n) => new() { Stars = n, Rank = n == 5 ? 3 : n == 4 ? 60 : 300 };

    [Fact]
    public void The_cost_starts_past_a_few_blue_chips_and_weighs_five_stars_most()
    {
        Assert.Equal(0, CollegeWorld.StackCost(Stars(5), CollegeWorld.StackFree));
        Assert.Equal(CollegeWorld.FiveStarStack, CollegeWorld.StackCost(Stars(5), CollegeWorld.StackFree + 1));
        Assert.Equal(3 * CollegeWorld.FiveStarStack, CollegeWorld.StackCost(Stars(5), CollegeWorld.StackFree + 3));
        Assert.Equal(0, CollegeWorld.StackCost(Stars(4), CollegeWorld.StackFree + 1));
        Assert.Equal(CollegeWorld.FiveStarStack, CollegeWorld.StackCost(Stars(4), CollegeWorld.StackFree + 3));
        Assert.Equal(0, CollegeWorld.StackCost(Stars(3), 12));
    }

    [Fact]
    public void The_board_counts_each_programs_blue_chips_and_a_five_star_weighs_them()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var c = world.College!;
        int cls = c.SeniorClass;
        var board = c.BoardFor(cls);
        var summer = new DateOnly(cls, 7, 1);
        foreach (var t in c.Programs.OrderByDescending(t => c.Prestige[t.Id]).Take(10))
        {
            int expected = t.Roster.Count(x => x.Age(summer) < CollegeWorld.GraduateAge && !c.WalkOnIds.Contains(x.Id) && c.IsElite(x))
                         + c.Prospects.Count(p => c.Recruits.TryGetValue(p.Id, out var r) && r.CommittedTo == t.Id && r.ClassOf <= cls
                                                  && p.Age(summer) < CollegeWorld.GraduateAge && c.IsElite(p));
            Assert.Equal(expected, board.Elite.GetValueOrDefault(t.Id));
        }
        var top = c.Programs.OrderByDescending(t => c.Prestige[t.Id]).First();
        _out.WriteLine($"{top.FullName}: {board.Elite.GetValueOrDefault(top.Id)} blue-chips next season");

        // Seat and unseat are symmetric (a flip puts everything back).
        var five = c.ClassOf(cls).First(p => c.Recruits[p.Id].Stars == 5 && CollegeWorld.PriorityOf(p) != RecruitPriority.PlayingTime);
        var r5 = c.Recruits[five.Id];
        int open = board.Open[top.Id], elite = board.Elite.GetValueOrDefault(top.Id);
        board.Seat(top.Id, five.PrimaryPosition, true, 1);
        Assert.Equal(open - 1, board.Open[top.Id]);
        Assert.Equal(elite + 1, board.Elite[top.Id]);
        board.Seat(top.Id, five.PrimaryPosition, true, -1);
        Assert.Equal(open, board.Open[top.Id]);
        Assert.Equal(elite, board.Elite[top.Id]);

        // A five-star's view of the program falls by exactly the stated cost as its blue-chips pile up (no noise passed).
        board.Elite[top.Id] = CollegeWorld.StackFree;
        double free = c.Appeal(top, five, r5, board);
        board.Elite[top.Id] = CollegeWorld.StackFree + 4;
        double stacked = c.Appeal(top, five, r5, board);
        _out.WriteLine($"five-star No. {r5.Rank} on {top.FullName}: {free:F1} with {CollegeWorld.StackFree} blue-chips, {stacked:F1} with {CollegeWorld.StackFree + 4}");
        Assert.Equal(4 * CollegeWorld.FiveStarStack, free - stacked, 6);
        // One who cares most about playing time counts his minutes, and so the stacking, twice.
        var minutesKid = c.ClassOf(cls).FirstOrDefault(p => c.Recruits[p.Id].Stars == 5 && CollegeWorld.PriorityOf(p) == RecruitPriority.PlayingTime);
        if (minutesKid != null)
        {
            var rm = c.Recruits[minutesKid.Id];
            board.Elite[top.Id] = CollegeWorld.StackFree;
            double f2 = c.Appeal(top, minutesKid, rm, board);
            board.Elite[top.Id] = CollegeWorld.StackFree + 4;
            Assert.Equal(8 * CollegeWorld.FiveStarStack, f2 - c.Appeal(top, minutesKid, rm, board), 6);
        }
    }
}
