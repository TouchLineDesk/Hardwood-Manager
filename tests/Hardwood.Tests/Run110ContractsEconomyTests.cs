using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 110, the contracts-economy pass, part 1 (H61). Wages below the top league are its market times the league's scale,
/// so a star's ask fell with the league: Jarrett Allen (CA 145) for $1.05M in tier 4, Giannis re-signing in the G League for
/// $2.35M. A player far above a league's level now has an asking floor (a share of his top-league market): you pay it or he
/// doesn't come, computer clubs below the top league don't sign or renew him for their scale, and he goes to the pool, where
/// the top league's free agency comes first.
/// </summary>
public class Run110ContractsEconomyTests
{
    private readonly ITestOutputHelper _out;
    public Run110ContractsEconomyTests(ITestOutputHelper o) => _out = o;

    private static readonly DateOnly On = new(2027, 7, 1);

    private static Player Make(int ca, int age) =>
        new() { Id = 1, FirstName = "A", LastName = "B", CurrentAbility = ca, PotentialAbility = ca, BirthDate = On.AddYears(-age).AddDays(-30) };

    // The top league (level 105, full wages), the G League (101, 10%) and tier 4 (86, 3%), as an American sees them.
    private static readonly (double, double)[] Usa = { (105, 1.0), (101, 0.10), (86, 0.03) };

    [Fact]
    public void A_player_asks_three_quarters_of_what_the_best_league_that_would_have_him_pays()
    {
        var star = Make(145, 31);
        long top = ContractMarket.MarketSalary(145, 31, Finance.HardCapDefault);
        long floor = ContractMarket.AskingFloor(star, On, Usa);
        _out.WriteLine($"CA 145 at 31: top-league market {Budgets.Money(top)}, floor {Budgets.Money(floor)}");
        Assert.Equal((long)Math.Round(top * 0.75 / 10_000) * 10_000, floor);     // a top-league star (145 ≥ 105 + 40)
        Assert.True(floor >= 15_000_000, "Jarrett Allen's level isn't a $1M player anywhere");

        var g = Make(142, 27);   // a G League star, not a top-league one
        long gTop = ContractMarket.MarketSalary(142, 27, Finance.HardCapDefault);
        Assert.Equal((long)Math.Round(gTop * 0.10 * 0.75 / 10_000) * 10_000, ContractMarket.AskingFloor(g, On, Usa));
        Assert.True(ContractMarket.AskingFloor(g, On, Usa) < (long)(gTop * 0.10), "never priced out of the league that sets his floor");
        Assert.Equal(0, ContractMarket.AskingFloor(Make(125, 27), On, Usa));    // within every league's own range: its scale stands
        Assert.Equal(0, ContractMarket.AskingFloor(star, On, Array.Empty<(double, double)>()));
    }

    [Fact]
    public void The_demand_never_falls_below_the_floor()
    {
        var star = Make(145, 31);
        var club = new Team { Id = 9, City = "C", Name = "D", Abbreviation = "CD", Division = "X" };
        long floor = ContractMarket.AskingFloor(star, On, Usa);
        var plain = ContractTalks.Demand(star, club, On, 0.03, Finance.HardCapDefault, renewal: false);
        var floored = ContractTalks.Demand(star, club, On, 0.03, Finance.HardCapDefault, renewal: false, floor: floor);
        _out.WriteLine($"ask in tier 4: {Budgets.Money(plain.Wage)} without the floor, {Budgets.Money(floored.Wage)} with it");
        Assert.True(plain.Wage < 2_000_000);
        Assert.True(floored.Wage >= floor);
    }

    [Fact]
    public void In_a_tier_5_career_a_real_star_asks_his_floor_and_the_talks_screen_says_why()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var teamId = world[UsaPyramid.Tier5].Clubs.OrderBy(t => QuickSim.Strength(t)).ElementAt(16).Id;
        var s = GameSession.NewGame(31, teamId, null);
        Assert.False(s.InNabl);
        var star = s.League.Teams.SelectMany(t => t.Roster).Where(p => p.Age(s.Today) >= 31 && p.Nationality == "USA").OrderByDescending(p => p.CurrentAbility).First();
        var ask = s.PeekDemand(star);
        long floor = s.MyAskingFloor(star);
        _out.WriteLine($"{s.HumanTeam.FullName} ({s.HumanLeagueName}, level {s.MyLevel}): {star.Name} CA {star.CurrentAbility} asks {Budgets.Money(ask.Wage)}, floor {Budgets.Money(floor)}; budget {Budgets.Money(s.HumanTeam.Money.WageBudget)}");
        _out.WriteLine(s.AskingFloorNote(star));
        Assert.True(floor > 5_000_000);
        Assert.True(ask.Wage >= floor);
        Assert.Contains("could earn far more in a bigger league", s.AskingFloorNote(star));
        // An ordinary tier-5 player is unaffected.
        var local = s.HumanTeam.Roster.OrderBy(p => p.CurrentAbility).First();
        Assert.Equal(0, s.MyAskingFloor(local));
        Assert.Null(s.AskingFloorNote(local));
    }

    [Fact]
    public void Clubs_below_the_top_league_neither_sign_nor_renew_a_star_for_their_scale()
    {
        var (league, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var t5 = world[UsaPyramid.Tier5];
        var star = league.Teams.SelectMany(t => t.Roster).Where(p => p.Nationality == "USA").OrderByDescending(p => p.CurrentAbility).First();
        var local = t5.Clubs[0].Roster.OrderBy(p => p.CurrentAbility).First();
        Assert.True(world.PricedOut(star, t5, On));
        Assert.False(world.PricedOut(local, t5, On));

        // One summer for the whole world: nobody below the top league renews, or signs from the pool, on a deal under his
        // floor. (A club's own new generated players are priced by its league, as before: they are its level's stars.)
        var before = world.Competitions.SelectMany(c => c.Clubs.SelectMany(t => t.Roster.Select(p => (p.Id, t.Id)))).ToHashSet();
        var leagueBefore = world.Competitions.Where(c => c.Profile.Kind == CompetitionKind.Domestic).SelectMany(c => c.Clubs.Select(t => (t.Id, c.Key))).ToDictionary(x => x.Id, x => x.Key);
        world.Rollover(2027, new GameSimulator());
        var date = new DateOnly(2027, 8, 15);
        var fromPool = world.SignedAbroad.Select(x => x.player.Id).ToHashSet();
        var under = world.Competitions.Where(c => c.Profile.Kind == CompetitionKind.Domestic && world.Levels.ContainsKey(c.Key))
            // Clubs that went up or down this summer signed at their old level (relegation's own clauses are K1, next run).
            .SelectMany(c => c.Clubs.Where(t => leagueBefore.GetValueOrDefault(t.Id) == c.Key).SelectMany(t => t.Roster.Where(p => p.Contract is { } k && k.StartSeason == 2027
                && (fromPool.Contains(p.Id) || before.Contains((p.Id, t.Id)))
                && k.Salary < world.AskingFloor(p, date)).Select(p => $"{p.Name} CA {p.CurrentAbility} at {t.FullName} {Budgets.Money(p.Contract!.Salary)}")))
            .ToList();
        _out.WriteLine($"signed from the pool: {fromPool.Count}; under the floor: {under.Count}");
        foreach (var x in under.Take(10)) _out.WriteLine(x);
        Assert.Empty(under);
    }
}
