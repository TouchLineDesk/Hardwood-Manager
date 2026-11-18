using Hardwood.Core;
using Hardwood.Core.Model;
using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 111, the contracts-economy pass, part 2 (K1, Danny's Nov 17 decision). A relegation release clause is now a saved
/// contract term that the very best players ask for; when the club goes down each player with one chooses, at the start of
/// the top league's free agency, so a star who leaves can sign in the top league that summer. Parachutes are EPL-style:
/// 55%, 45% and 20% of the media money of the league the club left (two years after a single season up), stopping on
/// promotion. Saves move to format 13; an older save gives the clause to the top league's 60 biggest deals.
/// </summary>
public class Run111ReleaseClauseTests
{
    private readonly ITestOutputHelper _out;
    public Run111ReleaseClauseTests(ITestOutputHelper o) => _out = o;

    private static readonly DateOnly On = new(2027, 7, 1);

    private static Player Make(int ca, int age, int ambition = 10, double morale = 60) =>
        new() { Id = 1, FirstName = "A", LastName = "B", CurrentAbility = ca, PotentialAbility = ca, BirthDate = On.AddYears(-age).AddDays(-30),
                Hidden = new HiddenTraits { Ambition = ambition }, Morale = morale };

    [Fact]
    public void Stars_ask_for_the_clause_and_ambitious_near_stars_too()
    {
        Assert.True(ContractMarket.AsksForReleaseClause(Make(150, 28)));
        Assert.True(ContractMarket.AsksForReleaseClause(Make(148, 28)));
        Assert.False(ContractMarket.AsksForReleaseClause(Make(147, 28)));
        Assert.True(ContractMarket.AsksForReleaseClause(Make(140, 26, ambition: 18)));
        Assert.False(ContractMarket.AsksForReleaseClause(Make(137, 26, ambition: 20)));
    }

    [Fact]
    public void The_player_chooses_by_his_level_age_and_mood_with_no_dice()
    {
        const double bar = 120;
        Assert.True(UsaPyramid.ReleaseChoice(Make(150, 27), bar, On).Leaves);
        Assert.True(UsaPyramid.ReleaseChoice(Make(111, 27), bar, On).Leaves);        // within 10 of the rotation bar
        Assert.False(UsaPyramid.ReleaseChoice(Make(109, 27), bar, On).Leaves);
        var old = UsaPyramid.ReleaseChoice(Make(150, 34), bar, On);
        Assert.False(old.Leaves);
        Assert.Contains("at 34", old.Why);
        Assert.True(UsaPyramid.ReleaseChoice(Make(100, 34, morale: 20), bar, On).Leaves);  // unhappy: he goes anyway
        // The same answer every time it's asked (the summer's end asks again).
        var p = Make(130, 29);
        Assert.Equal(UsaPyramid.ReleaseChoice(p, bar, On), UsaPyramid.ReleaseChoice(p, bar, On));
    }

    [Fact]
    public void A_new_real_world_gives_the_clause_to_the_biggest_deals()
    {
        var s = GameSession.NewGame(31, 5, null);
        var signed = s.League.Teams.SelectMany(t => t.Roster).Where(p => p.Contract != null).ToList();
        var with = signed.Where(p => p.Contract!.RelegationRelease).ToList();
        _out.WriteLine($"{with.Count} of {signed.Count} top-league deals carry a release clause; e.g. {string.Join(", ", with.OrderByDescending(p => p.Contract!.Salary).Take(5).Select(p => $"{p.Name} {Budgets.Money(p.Contract!.Salary)}"))}");
        Assert.InRange(with.Count, ContractMarket.ReleaseClauseTopDeals, 150);
        var top60 = signed.OrderByDescending(p => p.Contract!.Salary).ThenBy(p => p.Id).Take(60);
        Assert.All(top60, p => Assert.True(p.Contract!.RelegationRelease));
        Assert.All(signed.Where(p => p.CurrentAbility >= ContractMarket.ReleaseClauseCa), p => Assert.True(p.Contract!.RelegationRelease));
        // Below the top league nobody has one.
        Assert.DoesNotContain(s.World.Players, p => p.Contract is { RelegationRelease: true });
    }

    [Fact]
    public void Parachutes_pay_55_45_20_percent_of_the_media_money_left_behind()
    {
        long media = Budgets.MediaMoney(1.0);
        var t = new Team { Id = 1, City = "C", Name = "D", Abbreviation = "CD", Division = "X", ArenaCapacity = 18000 };
        t.Money.Ready = true; t.Money.Revenue = 245_000_000; t.Money.Balance = 10_000_000;
        Budgets.Rescale(t, 0.10, 6000, 1.0);
        var paid = new List<long> { t.Money.Parachute };
        _out.WriteLine(GameSession.ParachuteNote(t.Money));
        for (int y = 0; y < 3; y++) { Budgets.Summer(new[] { t }, 0.10, _ => 0.5, _ => false); paid.Add(t.Money.Parachute); }
        _out.WriteLine($"media money {Budgets.Money(media)}; parachute {string.Join(" → ", paid.Select(Budgets.Money))}");
        Assert.Equal(Round(media * 0.55), paid[0]);
        Assert.Equal(Round(media * 0.45), paid[1]);
        Assert.Equal(Round(media * 0.20), paid[2]);
        Assert.Equal(0, paid[3]);

        // One season up, then straight back down: two years.
        var yoyo = new Team { Id = 2, City = "E", Name = "F", Abbreviation = "EF", Division = "X", ArenaCapacity = 18000 };
        yoyo.Money.Ready = true; yoyo.Money.Revenue = 25_000_000;
        Budgets.Rescale(yoyo, 1.0, 18000, 0.10);                                   // promoted
        Budgets.Summer(new[] { yoyo }, 1.0, _ => 0.3, _ => false);                  // one season up
        Budgets.Rescale(yoyo, 0.10, 6000, 1.0);                                    // down again
        Assert.Equal(Budgets.ShortParachuteYears, yoyo.Money.ParachuteYears);
        Assert.Equal(Round(media * 0.55), yoyo.Money.Parachute);

        // Going straight back up stops it.
        Budgets.Rescale(t, 0.10, 6000, 1.0);
        Assert.True(t.Money.Parachute > 0);
        Budgets.Rescale(t, 1.0, 18000, 0.10);
        Assert.Equal(0, t.Money.Parachute);
        Assert.Equal(0, t.Money.ParachuteYears);

        // A second drop while it's still paid keeps the first (richer) parachute on its schedule.
        var twice = new Team { Id = 3, City = "G", Name = "H", Abbreviation = "GH", Division = "X", ArenaCapacity = 18000 };
        twice.Money.Ready = true; twice.Money.Revenue = 245_000_000;
        Budgets.Rescale(twice, 0.10, 6000, 1.0);
        Budgets.Summer(new[] { twice }, 0.10, _ => 0.3, _ => false);
        Budgets.Rescale(twice, 0.05, 4000, 0.10);
        Assert.Equal(Round(media * 0.45), twice.Money.Parachute);
        Assert.Contains("second drop", GameSession.ParachuteNote(twice.Money));
    }

    private static long Round(double v) => (long)Math.Round(v / 100_000) * 100_000;

    [Fact]
    public void At_free_agency_a_relegated_clubs_stars_choose_and_the_ones_who_leave_are_free_agents()
    {
        var s = GameSession.NewGame(31, 5, null);
        var nabl = s.League;
        // A finished season where the first club in each division is last.
        var season = new SeasonSummary { Season = nabl.Season };
        var divs = nabl.Teams.GroupBy(t => (t.Conference, t.Division)).ToList();
        var lastOf = divs.Select(g => g.OrderBy(t => t.Id).First()).ToHashSet();
        foreach (var t in nabl.Teams.OrderBy(t => lastOf.Contains(t) ? 1 : 0)) season.Records.Add((t.Id, lastOf.Contains(t) ? 20 : 45, lastOf.Contains(t) ? 62 : 37));
        nabl.History.Add(season);
        var club = lastOf.First();
        var star = club.Roster.OrderByDescending(p => p.CurrentAbility).First();
        star.Contract!.RelegationRelease = true; star.Morale = 60;
        // His deep bench, well under the top league's rotation bar, stays even with the clause.
        var veteran = club.Roster.OrderBy(p => p.CurrentAbility).First();
        veteran.Contract!.RelegationRelease = true; veteran.Morale = 60;
        var plain = club.Roster.First(p => p != star && p != veteran);
        plain.Contract!.RelegationRelease = false;

        var date = new DateOnly(nabl.Season + 1, 7, 1);
        var (news, mine) = UsaPyramid.UseReleaseClauses(s.World, nabl, date, club.Id);
        foreach (var n in news) _out.WriteLine($"{n.Title}: {n.Body}");
        foreach (var m in mine) _out.WriteLine($"{m.Player.Name} ({m.Player.CurrentAbility}, {m.Player.Age(date)}): {(m.Left ? "leaves" : "stays")}, {m.Why}");
        Assert.Contains(nabl.FreeAgents, p => p == star);
        Assert.Null(star.Contract);
        Assert.DoesNotContain(star, club.Roster);
        Assert.Contains(veteran, club.Roster);
        Assert.Contains(plain, club.Roster);
        Assert.Contains(mine, m => m.Player == star && m.Left);
        Assert.Contains(mine, m => m.Player == veteran && !m.Left);
        Assert.Contains(news, n => n.Title.StartsWith(club.FullName) && n.Body.Contains(star.Name));
        // Clubs that aren't going down keep their stars.
        var safe = nabl.Teams.First(t => !lastOf.Contains(t));
        Assert.All(safe.Roster, p => Assert.NotNull(p.Contract));
    }

    [Fact]
    public void The_clause_is_saved_and_an_old_save_gets_it_on_its_biggest_deals()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run111-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            File.Copy(Path.Combine(AppContext.BaseDirectory, "SaveFixtures", "real-v12.hwsave"), path);
            var s = GameSession.Load(path);
            var signed = s.League.Teams.SelectMany(t => t.Roster).Where(p => p.Contract != null).ToList();
            int with = signed.Count(p => p.Contract!.RelegationRelease);
            _out.WriteLine($"format 12 save upgraded: {with} of {signed.Count} deals carry the clause");
            Assert.InRange(with, ContractMarket.ReleaseClauseTopDeals, 150);
            var topDeal = signed.OrderByDescending(p => p.Contract!.Salary).First();
            Assert.True(topDeal.Contract!.RelegationRelease);
            var bench = signed.OrderBy(p => p.Contract!.Salary).First(p => !ContractMarket.AsksForReleaseClause(p));
            Assert.False(bench.Contract!.RelegationRelease);
            s.SaveNow();
            var again = GameSession.Load(path);
            var p2 = again.League.Teams.SelectMany(t => t.Roster).First(p => p.Id == topDeal.Id);
            Assert.True(p2.Contract!.RelegationRelease);
            Assert.False(again.League.Teams.SelectMany(t => t.Roster).First(p => p.Id == bench.Id).Contract!.RelegationRelease);
            Assert.Equal(with, again.League.Teams.SelectMany(t => t.Roster).Count(p => p.Contract is { RelegationRelease: true }));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, true);
        }
    }
}
