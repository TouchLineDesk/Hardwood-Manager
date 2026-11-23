using Hardwood.Core;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// D81, the revenue levers (run 169): ticket, food and parking prices and fan appreciation days for a pro club. Standard
/// prices earn exactly the old revenue (so the computer's clubs are unchanged); the levers trade money now for fans.
/// </summary>
public class Run169BusinessTests
{
    private const long Lr = Budgets.NablRevenue;

    private static Team Club(double fans = 50, int id = 1)
    {
        var t = new Team { Id = id, City = "City", Name = "Club", Abbreviation = $"CL{id}", Division = "D", ArenaCapacity = 18000 };
        t.Money.Revenue = Lr; t.Money.FanBase = fans; t.Money.Ready = true;
        return t;
    }

    [Fact]
    public void Standard_prices_change_nothing()
    {
        var t = Club();
        Assert.True(t.Money.Business.Now.IsStandard);
        Assert.Equal(0, Business.Change(t.Money));
        Assert.Equal(t.Money.Revenue, t.Money.Income);
        Assert.Equal(0, Business.FanDelta(new BusinessPlan()));
        var md = Business.Plan(Lr, 50, new BusinessPlan());
        Assert.Equal(0, md.Change);
        Assert.InRange(md.Total, Lr * 0.29, Lr * 0.31);
    }

    [Theory]
    [InlineData(90, true)]
    [InlineData(30, false)]
    public void Premium_tickets_pay_only_with_a_big_support(double fans, bool pays)
    {
        var md = Business.Plan(Lr, fans, new BusinessPlan { Tickets = 2 });
        Assert.Equal(pays, md.Change > 0);
        Assert.True(md.Fill < Business.BaseFill(fans), "higher prices always empty some seats");
    }

    [Fact]
    public void Cheap_tickets_fill_the_arena_and_sell_more_food_but_earn_less()
    {
        var std = Business.Plan(Lr, 50, new BusinessPlan());
        var cheap = Business.Plan(Lr, 50, new BusinessPlan { Tickets = -1 });
        Assert.Equal(1.0, cheap.Fill, 6);
        Assert.True(cheap.Food > std.Food);
        Assert.True(cheap.Change < 0);
        Assert.True(Business.FanDelta(new BusinessPlan { Tickets = -1 }) > 0);
    }

    [Fact]
    public void Free_parking_and_fan_days_cost_money_and_win_fans_with_diminishing_returns()
    {
        var md = Business.Plan(Lr, 50, new BusinessPlan { Parking = -1, FanDays = 2 });
        Assert.Equal(0, md.Parking);
        Assert.Equal((long)Math.Round(Lr * Business.FanDayCost * 2 / 10_000) * 10_000, md.FanDayCost);
        double one = Business.FanDelta(new BusinessPlan { FanDays = 1 }), two = Business.FanDelta(new BusinessPlan { FanDays = 2 }), four = Business.FanDelta(new BusinessPlan { FanDays = 4 });
        Assert.True(one > 0 && two > one && four > two);
        Assert.True(two - one < one, "each extra day adds less");
    }

    [Fact]
    public void The_summer_closes_the_books_on_this_seasons_plan_moves_the_fans_and_brings_in_next_seasons()
    {
        var a = Club(); var b = Club(id: 2);
        b.Money.Business.Now = new BusinessPlan { Tickets = 2, Food = 1 };
        b.Money.Business.Next = new BusinessPlan { Tickets = -1, FanDays = 3 };
        long income = b.Money.Income;
        Assert.True(income != b.Money.Revenue);
        Budgets.Summer(new List<Team> { a, b }, 1, _ => 0.5, _ => false);
        Assert.Equal(a.Money.FanBase + Business.FanDelta(new BusinessPlan { Tickets = 2, Food = 1 }), b.Money.FanBase, 6);
        Assert.Equal(-1, b.Money.Business.Now.Tickets);
        Assert.Equal(3, b.Money.Business.Now.FanDays);
        Assert.True(b.Money.Business.BooksClosed);
        Assert.Equal(b.Money.Revenue + Business.Change(b.Money), b.Money.Income); // next season's income is on the new plan
    }

    [Fact]
    public void Your_prices_apply_at_once_before_the_season_and_wait_for_next_season_once_it_starts()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-biz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var s = GameSession.NewGame(4, 3, path);
            Assert.True(s.HasBusiness);
            Assert.Equal(SessionPhase.Preseason, s.Phase);
            Assert.True(s.BusinessChangesNow);
            string line = s.SetBusiness(new BusinessPlan { Tickets = 1, FanDays = 1 });
            Assert.StartsWith("Set for this season", line);
            Assert.Equal(1, s.MyPlan.Tickets);
            Assert.NotEqual(s.HumanTeam.Money.Revenue, s.HumanTeam.Money.Income);

            int guard = 0;
            while (s.Phase != SessionPhase.RegularSeason && guard++ < 200) s.AdvanceDay();
            Assert.False(s.BusinessChangesNow);
            line = s.SetBusiness(new BusinessPlan { Tickets = -1, Parking = -1 });
            Assert.StartsWith("Season tickets are already sold", line);
            Assert.Equal(1, s.MyPlan.Tickets);
            Assert.Equal(-1, s.MyNextPlan.Tickets);

            s.SaveNow();
            var back = GameSession.Load(path);
            Assert.Equal(1, back.MyPlan.Tickets);
            Assert.Equal(1, back.MyPlan.FanDays);
            Assert.Equal(-1, back.MyNextPlan.Parking);

            // Review (run 169): the club you leave goes back to standard prices for the computer.
            var club = back.HumanTeam;
            Assert.Null(back.Resign());
            Assert.True(club.Money.Business.Next.IsStandard);
            Assert.Equal(1, club.Money.Business.Now.Tickets); // this season's tickets are sold
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
