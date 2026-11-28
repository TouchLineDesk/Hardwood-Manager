using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 236, a polish run: hunt 32's new Mediums. H236 (round robins alternate home and away), H237 (a player's ask never passes
/// the maximum for his years in the game), H238 (a second relegation after the rebuild season ends the job for a board low
/// on faith). H242's card order is checked in <see cref="Run236FreeAgentCardsTests"/>.
/// </summary>
public class Run236HuntFixesTests
{
    private static readonly Dictionary<int, PlayerSeasonStats> None = new();

    [Theory]
    [InlineData(6, 2)] [InlineData(10, 2)] [InlineData(12, 2)] [InlineData(16, 2)] [InlineData(18, 2)]
    [InlineData(20, 2)] [InlineData(26, 2)] [InlineData(31, 2)] [InlineData(32, 2)] [InlineData(12, 4)] [InlineData(9, 3)] [InlineData(18, 1)]
    public void H236_every_club_alternates_home_and_away_with_at_most_three_in_a_row(int clubs, int robins)
    {
        var ids = Enumerable.Range(100, clubs).ToList();
        var rounds = GameWorld.Rounds(ids, robins);
        foreach (int id in ids)
        {
            var venues = rounds.Select(r => r.Where(g => g.h == id || g.a == id).Select(g => g.h == id).Cast<bool?>().FirstOrDefault())
                .Where(v => v != null).Select(v => v!.Value).ToList();
            Assert.Equal((clubs - 1) * robins, venues.Count);
            int run = 1, longest = 1;
            for (int i = 1; i < venues.Count; i++) { run = venues[i] == venues[i - 1] ? run + 1 : 1; longest = Math.Max(longest, run); }
            Assert.True(longest <= 3, $"{clubs} clubs, {robins} passes: club {id} has {longest} in a row at one end");
            int home = venues.Count(v => v);
            Assert.InRange(home, venues.Count / 2 - (robins % 2 == 1 ? 1 : 0), (venues.Count + 1) / 2 + (robins % 2 == 1 ? 1 : 0));
        }
        // Over two passes every pairing is played once at each end.
        if (robins == 2)
            foreach (var a in ids) foreach (var b in ids.Where(b => b > a))
            {
                var games = rounds.SelectMany(r => r).Where(g => (g.h == a && g.a == b) || (g.h == b && g.a == a)).ToList();
                Assert.Equal(2, games.Count);
                Assert.NotEqual(games[0].h, games[1].h);
            }
    }

    [Fact]
    public void H236_every_league_in_a_new_world_alternates_home_and_away_in_date_order()
    {
        var league = new WorldGenerator(new Rng(2026)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, 2026, Array.Empty<string>(), new DateOnly(2026, 10, 1));
        int checkedLeagues = 0;
        foreach (var comp in world.Playing.Where(c => c.Profile.Kind == CompetitionKind.Domestic && c.Season.Fixtures.Count > 0))
        {
            checkedLeagues++;
            foreach (var club in comp.Clubs)
            {
                var venues = comp.Season.Fixtures.Where(f => f.HomeId == club.Id || f.AwayId == club.Id)
                    .OrderBy(f => f.Date).ThenBy(f => f.Index).Select(f => f.HomeId == club.Id).ToList();
                int run = 1, longest = venues.Count > 0 ? 1 : 0;
                for (int i = 1; i < venues.Count; i++) { run = venues[i] == venues[i - 1] ? run + 1 : 1; longest = Math.Max(longest, run); }
                Assert.True(longest <= 3, $"{comp.Profile.Short}: {club.Name} has {longest} in a row at one end");
            }
        }
        Assert.True(checkedLeagues >= 10, $"{checkedLeagues} leagues");
    }

    private static Player Star(int ca, DateOnly born, int? draftYear) => new()
    {
        Id = 1, FirstName = "Test", LastName = "Star", BirthDate = born, CurrentAbility = ca, PotentialAbility = ca,
        Draft = draftYear is { } y ? new DraftRecord(y, 1, 4) : null,
        Hidden = new HiddenTraits { Ambition = 20, Loyalty = 1 }, Morale = 20,
    };

    [Fact]
    public void H237_a_fourth_year_player_asks_at_most_a_quarter_of_the_cap_and_a_veteran_at_most_35_percent()
    {
        var fin = Finance.HardCapDefault;
        var summer = new DateOnly(2028, 7, 1);
        var league = new WorldGenerator(new Rng(5)).Generate("NABL", 2027, new DateOnly(2027, 10, 1));
        var weak = league.Teams.OrderBy(ContractTalks.Level).First();

        var young = Star(200, new DateOnly(2005, 1, 1), 2024);
        Assert.Equal(4, ContractMarket.ServiceSeasons(young, summer));
        var ask = ContractTalks.Demand(young, weak, summer, 1, fin, renewal: true);
        Assert.True(ask.Wage <= fin.SalaryCap * ContractMarket.MaxShareYoung + 10_000, $"asks {ask.Wage:N0}");
        Assert.True(ask.Wage >= fin.SalaryCap * ContractMarket.MaxShareYoung - 10_000, $"asks {ask.Wage:N0}: the max, not less");

        // No draft record: the years are counted from age 19.
        var undrafted = Star(200, new DateOnly(2005, 1, 1), null);
        Assert.Equal(4, ContractMarket.ServiceSeasons(undrafted, summer));

        var vet = Star(200, new DateOnly(1994, 1, 1), 2013);
        Assert.Equal(15, ContractMarket.ServiceSeasons(vet, summer));
        Assert.Equal((long)Math.Round(fin.SalaryCap * 0.35 / 10_000) * 10_000, ContractMarket.MaxSalary(vet, summer, fin));
        Assert.True(ContractTalks.Demand(vet, weak, summer, 1, fin, renewal: true).Wage <= ContractMarket.MaxSalary(vet, summer, fin));

        // Abroad the maximum scales with the league's wages.
        var scaled = ContractTalks.Demand(young, weak, summer, 0.3, fin, renewal: true);
        Assert.True(scaled.Wage <= fin.SalaryCap * ContractMarket.MaxShareYoung * 0.3 + 10_000, $"asks {scaled.Wage:N0}");
    }

    [Fact]
    public void H238_going_down_again_in_the_rebuild_season_is_a_sacking_below_30_however_patient_the_owner()
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        GameWorld.Generate(league, 3, Array.Empty<string>(), new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        var ctx = new LeagueContext("NBA G League", league.Teams, 62, 16, false, null, -10, 2);
        var team = league.Teams.First();
        team.Owner.Patience = 20;   // the most patient owner: his usual sack line is 15
        JobState Job(double conf) => new() { Current = new Expectations { Season = 2027, Goal = SeasonGoal.Playoffs, TargetWins = 31 }, Confidence = conf,
            HiredSeason = 2026, ProtectedSeason = 2027, ProtectedReason = "rebuild", RebuildUsed = true };

        var (title, body, sacked) = Board.SeasonReview(Job(50), new Coach(), ctx, team, SeasonGoal.Develop, 27, 35, None, new DateOnly(2028, 5, 8), 2027, relegated: true);
        Assert.True(sacked, body);
        Assert.Equal("You have been sacked", title);
        Assert.Contains("gone down again", body);
        Assert.Contains("You have been sacked", body);

        // A board with real faith keeps him (K7).
        var (_, body2, kept) = Board.SeasonReview(Job(95), new Coach(), ctx, team, SeasonGoal.Develop, 27, 35, None, new DateOnly(2028, 5, 8), 2027, relegated: true);
        Assert.False(kept, body2);

        // A first relegation (no rebuild season yet) is the owner's patience as before.
        var first = new JobState { Current = new Expectations { Season = 2027, Goal = SeasonGoal.Playoffs, TargetWins = 31 }, Confidence = 50, HiredSeason = 2026 };
        var (_, body3, sacked3) = Board.SeasonReview(first, new Coach(), ctx, team, SeasonGoal.Develop, 27, 35, None, new DateOnly(2028, 5, 8), 2027, relegated: true);
        Assert.False(sacked3, body3);
    }
}

/// <summary>H242 (run 236): the free-agent cards lead with players who'd come and fit the wage room.</summary>
public class Run236FreeAgentCardsTests
{
    private static Hardwood.Desktop.ViewModels.FreeAgentRow Row(int id, int ca, long ask, int reach) => new()
    {
        Player = new Player { Id = id, FirstName = "P", LastName = id.ToString(), CurrentAbility = ca, PotentialAbility = ca, BirthDate = new DateOnly(2000, 1, 1) },
        Ask = new ContractDemand(ask, 2), Reach = reach,
    };

    [Fact]
    public void H242_cards_put_affordable_players_first_then_over_budget_then_those_who_wont_talk_each_best_first()
    {
        // 30 stars who aim higher, then 20 who'd talk but cost too much, then 10 who fit, in ability order (as the table is).
        var rows = Enumerable.Range(0, 30).Select(i => Row(i, 200 - i, 20_000_000, 2))
            .Concat(Enumerable.Range(30, 20).Select(i => Row(i, 200 - i, 5_000_000, 1)))
            .Concat(Enumerable.Range(50, 10).Select(i => Row(i, 200 - i, 500_000, 0))).ToList();
        var cards = Hardwood.Desktop.ViewModels.SummerCards.WithinReach(rows).ToList();
        Assert.Equal(Hardwood.Desktop.ViewModels.SummerCards.FreeAgentCount, cards.Count);
        Assert.Equal(Enumerable.Range(50, 10), cards.Take(10).Select(c => c.Player.Id));
        Assert.Equal(Enumerable.Range(30, 14), cards.Skip(10).Select(c => c.Player.Id));
        var note = Hardwood.Desktop.ViewModels.SummerCards.CardsNote(cards, 1_925, 800_000);
        Assert.StartsWith("10 free agents whose asks fit your wage room ($800K) come first", note);

        // When the best all fit, the cards are simply the best.
        var fit = Enumerable.Range(0, 40).Select(i => Row(i, 200 - i, 1_000_000, 0)).ToList();
        var best = Hardwood.Desktop.ViewModels.SummerCards.WithinReach(fit).ToList();
        Assert.Equal(Enumerable.Range(0, 24), best.Select(c => c.Player.Id));
        Assert.StartsWith("24 free agents whose asks fit your wage room", Hardwood.Desktop.ViewModels.SummerCards.CardsNote(best, 40, 50_000_000));

        // Rows are read only until 24 that fit are seen (the review of #305).
        int built = 0;
        IEnumerable<Hardwood.Desktop.ViewModels.FreeAgentRow> Lazy() { for (int i = 0; i < 1000; i++) { built++; yield return Row(i, 200 - i / 10, 1_000_000, i % 2); } }
        var lazy = Hardwood.Desktop.ViewModels.SummerCards.WithinReach(Lazy()).ToList();
        Assert.Equal(47, built);   // the 24th that fits is row 46
        Assert.All(lazy, c => Assert.Equal(0, c.Reach));

        var wontTalk = Enumerable.Range(0, 24).Select(i => Row(i, 200 - i, 9_000_000, 2)).ToList();
        Assert.StartsWith("None of the best 24 of 300 free agents will talk to you now", Hardwood.Desktop.ViewModels.SummerCards.CardsNote(wontTalk, 300, 5_000_000));

        var none = Enumerable.Range(0, 5).Select(i => Row(i, 200 - i, 9_000_000, 1)).ToList();
        Assert.StartsWith("Nobody of 5 free agents asks within your wage room", Hardwood.Desktop.ViewModels.SummerCards.CardsNote(none, 5, 0));
    }
}
