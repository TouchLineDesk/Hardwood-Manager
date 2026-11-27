using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 226, H61's next slice (#932): "prices follow ability everywhere" and "some fading stars retire". Run 110's floor only
/// covered a league's stars (40 CA above its level), so a CA-140 American was neither a top-league star nor anything below
/// and asked a tier-5 club its scale: LeBron James at Worcester for $0.43M, Joel Embiid at Juventus Utena for $1.86M.
/// Now, while the top league's free agency is open, a player good enough for it waits for its offers; an old one nobody
/// there signs retires at the end of the summer.
/// </summary>
public class Run226FadingStarsTests
{
    private readonly ITestOutputHelper _out;
    public Run226FadingStarsTests(ITestOutputHelper o) => _out = o;

    private static readonly DateOnly On = new(2027, 7, 1);

    private static Player Make(int id, int ca, int age, string nat = "USA") =>
        new() { Id = id, FirstName = "A", LastName = $"B{id}", CurrentAbility = ca, PotentialAbility = ca, Nationality = nat, BirthDate = On.AddYears(-age).AddDays(-30) };

    [Fact]
    public void A_top_league_free_agent_asks_most_of_his_top_league_market()
    {
        long market = ContractMarket.MarketSalary(140, 34, Finance.HardCapDefault);
        Assert.Equal((long)Math.Round(market * 0.75 / 10_000) * 10_000, ContractMarket.TopLeagueFloor(Make(1, 140, 34), On, 105));
        Assert.True(ContractMarket.TopLeagueFloor(Make(1, 140, 34), On, 105) > 10_000_000);
        Assert.Equal(0, ContractMarket.TopLeagueFloor(Make(2, 114, 28), On, 105));   // not quite a top-league player
        Assert.Equal(0, ContractMarket.TopLeagueFloor(Make(3, 140, 28), On, 0));      // no top league known
        // Run 110's star rule prices a CA-140 player by tier 4 (he isn't 40 above the top league's or the G League's level):
        // under $1M, the gap this slice closes.
        Assert.True(ContractMarket.AskingFloor(Make(4, 140, 30), On, new (double, double)[] { (105, 1.0), (101, 0.10), (86, 0.03) }) < 1_000_000);
    }

    [Fact]
    public void In_a_tier_5_career_a_top_league_free_agent_waits_for_its_offers_until_its_free_agency_ends()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var teamId = world[UsaPyramid.Tier5].Clubs.OrderBy(t => QuickSim.Strength(t)).ElementAt(16).Id;
        var s = GameSession.NewGame(31, teamId, null);
        double top = s.World.Options(s.League.Teams[0].Roster[0])[0].Level;
        var player = s.League.Teams.SelectMany(t => t.Roster)
            .Where(p => p.Nationality == "USA" && p.CurrentAbility >= top + 10 && p.CurrentAbility < top + 35)
            .OrderByDescending(p => p.CurrentAbility).First();
        long usual = s.MyAskingFloor(player);
        Assert.Equal(s.World.AskingFloor(player, s.Today), usual);
        Assert.True(usual < 2_000_000, $"in season he is priced by the small leagues: {Budgets.Money(usual)}");

        // The top league's free agency is open: the world's summer has started and not yet ended.
        int y = s.World.SummerEnded + 1;
        s.World.SummerStarted = y;
        Assert.True(s.World.TopMarketOpen);
        long waiting = s.MyAskingFloor(player);
        var ask = s.PeekDemand(player);
        _out.WriteLine($"top level {top:0.0}: {player.Name} CA {player.CurrentAbility}, {player.Age(s.Today)}: floor {Budgets.Money(waiting)}, ask {Budgets.Money(ask.Wage)}; {s.AskingFloorNote(player)}");
        Assert.True(waiting > Math.Max(5_000_000, usual * 3), "a top-league player isn't a tier-5 wage while its free agency is open");
        Assert.True(ask.Wage >= waiting);
        Assert.Contains("waiting for its offers", s.AskingFloorNote(player));

        // Your own players renew on the usual floor, as the computer's clubs' do.
        var mine = s.HumanTeam.Roster.OrderByDescending(p => p.CurrentAbility).First();
        Assert.Equal(s.World.AskingFloor(mine, s.Today), s.MyAskingFloor(mine));

        // Free agency over and no top-league club signed him: he takes what's there (#932 "unless no better offer comes").
        s.World.SummerEnded = y;
        Assert.False(s.World.TopMarketOpen);
        Assert.Equal(usual, s.MyAskingFloor(player));
    }

    [Fact]
    public void An_old_top_league_free_agent_nobody_signed_retires_at_the_end_of_the_summer()
    {
        var (league, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        double top = league.Teams.SelectMany(t => t.Roster).Average(p => p.CurrentAbility);
        int ca = (int)Math.Ceiling(top + 15);
        var old = Make(900_001, ca, 37);
        var young = Make(900_002, ca, 29);
        var fading = Make(900_003, (int)Math.Floor(top - 5), 36);   // not top-league ability: he may play on wherever he likes
        var pool = new List<Player> { old, young, fading };
        world.EndSummer(2027, new GameSimulator(), pool);
        _out.WriteLine($"top level {top:0.0}; news: {string.Join(" | ", world.News.Where(n => n.Title.Contains("retire")).Select(n => n.Title + ": " + n.Body))}");
        Assert.Contains(old, league.Retired);
        Assert.DoesNotContain(old, pool);
        Assert.DoesNotContain(young, league.Retired);
        Assert.DoesNotContain(fading, league.Retired);
        Assert.Contains(world.News, n => n.Title.Contains("retire") && n.Body.Contains(old.Name));
    }
}
