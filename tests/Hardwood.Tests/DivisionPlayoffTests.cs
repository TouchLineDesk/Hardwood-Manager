using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Danny's division format, part 2 (D33/D36, run 30): the top two in each division make a 16-team playoff seeded by record
/// within the conference; the eight third-placed teams play the third-place tournament for a trophy and prize money; the
/// play-in is retired. The same in The Championship and National League One.
/// </summary>
public class DivisionPlayoffTests
{
    private static (League nabl, GameWorld world) NewWorld(ulong seed = 3)
    {
        var nabl = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        return (nabl, GameWorld.Generate(nabl, seed, Array.Empty<string>(), new DateOnly(2026, 10, 1)));
    }

    private static List<Team> PlaceInDivision(Standings st, int place) =>
        new[] { Conference.East, Conference.West }
            .SelectMany(c => st.Conference(c).Select(r => r.Team).GroupBy(t => t.Division).Select(g => g.ElementAt(place - 1))).ToList();

    [Fact]
    public void The_top_league_plays_division_playoffs_and_a_third_place_tournament()
    {
        var (nabl, _) = NewWorld();
        Assert.True(Postseason.UsesDivisions(nabl));
        var rng = new Rng(11);
        long[] before = nabl.Teams.Select(t => t.Money.Balance).ToArray();
        var outcome = new SeasonSimulator(rng).Run(nabl, ScheduleBuilder.Build(nabl, new DateOnly(2026, 10, 21), rng));
        var st = outcome.Standings;

        Assert.DoesNotContain(outcome.Playoffs, p => p.Round.Contains("Play-In"));
        // The 16 in the first round are exactly the top two in each division.
        var firstRound = outcome.Playoffs.Where(p => p.Round.EndsWith("First Round")).SelectMany(p => new[] { p.Series.Winner, p.Series.Loser }).ToHashSet();
        var topTwo = PlaceInDivision(st, 1).Concat(PlaceInDivision(st, 2)).ToHashSet();
        Assert.Equal(16, firstRound.Count);
        Assert.True(topTwo.SetEquals(firstRound));
        // Seeded by record within the conference: the 1 seed is the conference's best division-top-two record.
        foreach (var conf in new[] { Conference.East, Conference.West })
        {
            var seeds = Postseason.DivisionSeeds(st, conf);
            Assert.Equal(8, seeds.Count);
            Assert.Equal(seeds.OrderByDescending(t => st[t].WinPct).ThenByDescending(t => st[t].PointDiff).ToList(), seeds);
        }
        // Four title rounds (15 series) and the third-place tournament (7 series, best of three) among the third-placed teams.
        Assert.Equal(15, outcome.Playoffs.Count(p => !Postseason.IsThirdPlace(p.Round)));
        var third = outcome.Playoffs.Where(p => Postseason.IsThirdPlace(p.Round)).ToList();
        Assert.Equal(7, third.Count);
        var thirds = PlaceInDivision(st, 3).ToHashSet();
        Assert.All(third, p => { Assert.Contains(p.Series.Winner, thirds); Assert.Contains(p.Series.Loser, thirds); Assert.Equal(2, p.Series.WinnerGames); });
        Assert.NotNull(outcome.ThirdPlaceWinner);
        Assert.Contains(outcome.ThirdPlaceWinner!, thirds);
        // The Finals is still the last title series, and the champion one of the top two in a division.
        Assert.Equal("Finals", outcome.Playoffs.Last(p => !Postseason.IsThirdPlace(p.Round)).Round);
        Assert.Contains(outcome.Champion!, topTwo);
        // Prize money: the winner gets the full prize (on top of any other movement, there is none during the playoffs).
        int wi = nabl.Teams.IndexOf(outcome.ThirdPlaceWinner!);
        Assert.Equal(before[wi] + Postseason.ThirdPlacePrizeFor(true), outcome.ThirdPlaceWinner!.Money.Balance);
        // The board counts the third-place tournament where the play-in was.
        Assert.Equal(SeasonGoal.PlayIn, Board.RoundReached(Postseason.ThirdQuarters));
        Assert.Equal("Finish in your division's top three", Board.Describe(SeasonGoal.PlayIn, LeagueContext.Nabl(nabl)));
    }

    [Fact]
    public void A_division_postseason_restores_mid_way_and_finishes_the_same_way()
    {
        var (nabl, _) = NewWorld(4);
        var rng = new Rng(5);
        var sim = new GameSimulator();
        var standings = new Standings(nabl);
        foreach (var g in ScheduleBuilder.Build(nabl, new DateOnly(2026, 10, 21), rng))
            standings.Record(sim.Simulate(g.Home, g.Away, nabl.Rules, rng.NextULong(), date: g.Date));
        var post = new Postseason(nabl, standings);
        Assert.True(post.DivisionFormat);
        Assert.Equal("First Round", post.Stage);
        Assert.Equal(12, post.Series.Count);   // 8 first-round series + 4 third-place quarter-finals
        var day = new DateOnly(2027, 4, 18);
        while (post.Stage != "Conference Finals") { post.PlayDay(sim, rng, day); day = day.AddDays(1); }
        Assert.Equal(2, post.Series.Count(s => s.Round == Postseason.ThirdSemis));
        Assert.Single(post.Series, s => s.Round == Postseason.ThirdFinal);

        var rows = post.Series.Select(s => (s.Round, s.Higher.Id, s.Lower.Id, s.HigherWins, s.LowerWins, s.WinsNeeded)).ToList();
        var restored = new Postseason(nabl, standings);
        restored.Restore(post.Stage, rows);
        Assert.Equal(post.Series.Count, restored.Series.Count);
        while (!restored.Complete) { restored.PlayDay(sim, rng, day); day = day.AddDays(1); }
        Assert.NotNull(restored.Champion);
        Assert.NotNull(restored.ThirdPlaceWinner);
        Assert.Equal("Finals", restored.Series.Last().Round);
    }

    [Fact]
    public void A_thirty_team_league_keeps_the_play_in()
    {
        var nabl = new WorldGenerator(new Rng(6)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Assert.Equal(30, nabl.Teams.Count);
        Assert.False(Postseason.UsesDivisions(nabl));
        var post = new Postseason(nabl, new Standings(nabl));
        Assert.False(post.DivisionFormat);
        Assert.Equal("Play-In", post.Stage);
        Assert.Equal(4, post.Series.Count);
    }

    [Fact]
    public void The_national_tiers_play_division_playoffs_and_a_third_place_tournament()
    {
        var (_, world) = NewWorld(7);
        world.FinishSeasons(new GameSimulator());
        foreach (var key in new[] { UsaPyramid.Tier2, UsaPyramid.Tier3 })
        {
            var c = world[key];
            var s = c.Season;
            Assert.True(s.IsComplete);
            var table = s.Table();
            var divs = UsaPyramid.Divisions(key, c.Clubs);
            List<int> Place(int k) => divs.Select(d => d.Teams.OrderBy(t => table.FindIndex(x => x.TeamId == t.Id)).ElementAt(k - 1).Id).ToList();
            var topTwo = Place(1).Concat(Place(2)).ToHashSet();
            var first = s.Series.Where(x => x.Round == "First round").ToList();
            Assert.Equal(8, first.Count);
            Assert.True(topTwo.SetEquals(first.SelectMany(x => new[] { x.HighId, x.LowId })));
            Assert.Equal(15, s.Series.Count(x => !Postseason.IsThirdPlace(x.Round)));
            Assert.Equal(7, s.Series.Count(x => Postseason.IsThirdPlace(x.Round)));
            Assert.Contains(s.ChampionId!.Value, topTwo);
            Assert.NotNull(s.ThirdPlaceId);
            Assert.Contains(s.ThirdPlaceId!.Value, Place(3));
            Assert.Equal(world.TeamById(s.ThirdPlaceId!.Value)!.FullName, c.History.Last().ThirdPlaceName);
            // The board's view of a third-placed club, and of the champion.
            Assert.Equal(SeasonGoal.PlayIn, Board.Achieved(Place(3).First(id => id != s.ThirdPlaceId), s, c.Profile));
            Assert.Equal(SeasonGoal.Title, Board.Achieved(s.ChampionId!.Value, s, c.Profile));
            // Every playoff game is after the regular season.
            var last = s.Fixtures.Where(f => f.Stage == CompetitionSeason.RegularSeason).Max(f => f.Date);
            Assert.All(s.Fixtures.Where(f => f.Stage != CompetitionSeason.RegularSeason), f => Assert.True(f.Date > last));
            Assert.True(s.Fixtures.Max(f => f.Date) < new DateOnly(2027, 6, 30), $"{key} ends {s.Fixtures.Max(f => f.Date)}");
        }
    }

    [Fact]
    public void A_season_record_without_the_third_place_field_still_loads()
    {
        var rec = System.Text.Json.JsonSerializer.Deserialize<List<ChampionRecord>>("[{\"Season\":2026,\"ChampionId\":1,\"RunnerUpId\":2,\"ChampionName\":\"A\",\"RunnerUpName\":\"B\"}]");
        Assert.Null(rec![0].ThirdPlaceName);
        var s = System.Text.Json.JsonSerializer.Deserialize<CompetitionSeason>("{\"Year\":2026}");
        Assert.Null(s!.ThirdPlaceId);
        var a = System.Text.Json.JsonSerializer.Deserialize<SeasonAwards>("{\"Season\":2026}");
        Assert.Null(a!.ThirdPlaceWinnerId);
    }
}
