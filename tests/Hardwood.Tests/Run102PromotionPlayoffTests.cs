using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Danny (chat, Nov 17, 2026; run 102): the NBA's 4th-placed club in each division no longer goes down automatically. It plays
/// the paired tier-2 division's winner, best of three, alongside the NBA playoffs and the third-place tournament; the tier-2
/// club hosts Games 1 and 3. The winner plays in the NBA next season, the loser in tier 2. Tier 2's division winners skip
/// tier 2's playoffs: its eight runners-up play for the title, and the third-placed clubs keep their tournament. Lower
/// boundaries stay automatic.
/// </summary>
public class Run102PromotionPlayoffTests
{
    private static (League nabl, GameWorld world) NewWorld(ulong seed)
    {
        var nabl = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        return (nabl, GameWorld.Generate(nabl, seed, Array.Empty<string>(), new DateOnly(2026, 10, 1), legacyPyramid: false));
    }

    /// <summary>A finished NBA table: in each division the lowest id is last (as PyramidTests' summary).</summary>
    private static Standings TopTable(League nabl)
    {
        var st = new Standings(nabl);
        var order = nabl.Teams.OrderBy(t => t.Id).ToList();
        // Each club beats every club with a lower id once: the lowest id in a division has the fewest wins.
        for (int i = 0; i < order.Count; i++)
            for (int j = 0; j < i; j++)
                st.Record(order[i], order[j], 110, 100);
        return st;
    }

    private static List<Team> DivisionWinners(GameWorld world, WorldCompetition t2)
    {
        var table = t2.Season.Table().Select(r => r.TeamId).ToList();
        return t2.Clubs.GroupBy(t => (t.Conference, t.Division)).Select(g => g.OrderBy(t => table.IndexOf(t.Id)).First()).ToList();
    }

    [Fact]
    public void The_promotion_playoffs_pair_each_division_and_decide_who_goes_up()
    {
        var (nabl, world) = NewWorld(5);
        Assert.True(UsaPyramid.PlaysPromotion(world, nabl));
        var t2 = world[UsaPyramid.Tier2];
        var sim = new GameSimulator();
        // Tier 2's regular season ends in March; play to the NBA's last day.
        var nbaEnd = new DateOnly(2027, 4, 12);
        world.PlayThrough(nbaEnd, sim);
        Assert.NotEqual(CompetitionSeason.RegularSeason, t2.Season.Stage);
        var winners = DivisionWinners(world, t2);

        // Tier 2's title bracket: the eight runners-up only, starting at the conference semi-finals (1v4, 2v3).
        var bracket = t2.Season.Series.Where(x => !Postseason.IsThirdPlace(x.Round) && !UsaPyramid.IsPromotion(x.Round)).ToList();
        var firstRound = bracket.Where(x => x.RoundIndex == bracket.Min(y => y.RoundIndex)).ToList();
        Assert.Equal(1, firstRound[0].RoundIndex);
        Assert.Equal(4, firstRound.Count);
        Assert.DoesNotContain(bracket, x => winners.Any(w => w.Id == x.HighId || w.Id == x.LowId));
        // The third-placed clubs keep their tournament (eight, best of three).
        Assert.Equal(4, t2.Season.Series.Count(x => x.Round == Postseason.ThirdQuarters));

        var table = TopTable(nabl);
        var pairs = world.StartPromotionPlayoffs(table, nbaEnd);
        Assert.Equal(8, pairs.Count);
        // Starting twice does nothing.
        Assert.Empty(world.StartPromotionPlayoffs(table, nbaEnd));
        foreach (var (top, challenger) in pairs)
        {
            Assert.Equal((top.Conference, top.Division), (challenger.Conference, challenger.Division));
            Assert.Equal(nabl.Teams.Where(t => t.Conference == top.Conference && t.Division == top.Division).Min(t => t.Id), top.Id);
            Assert.Contains(challenger, winners);
        }
        var series = t2.Season.Series.Where(x => UsaPyramid.IsPromotion(x.Round)).ToList();
        Assert.Equal(8, series.Count);
        Assert.All(series, x => Assert.Equal(3, x.BestOf));
        // Game 1 is after the NBA's last day.
        Assert.All(t2.Season.Fixtures.Where(f => UsaPyramid.IsPromotion(f.Stage)), f => Assert.True(f.Date > nbaEnd));

        world.FinishSeasons(sim);
        Assert.All(series, x => Assert.True(x.Complete));
        Assert.True(t2.Season.IsComplete);
        // Tier 2's third-place tournament is still crowned (review, run 102), and its history row names the club.
        Assert.NotNull(t2.Season.ThirdPlaceId);
        Assert.NotNull(t2.History[^1].ThirdPlaceName);
        foreach (var x in series)
        {
            var games = t2.Season.Fixtures.Where(f => f.SeriesId == x.Id).OrderBy(f => f.Date).ToList();
            Assert.InRange(games.Count, 2, 3);
            // The tier-2 club hosts Games 1 and 3, the NBA club Game 2; never two games on one day.
            Assert.Equal(x.HighId, games[0].HomeId);
            Assert.Equal(x.LowId, games[1].HomeId);
            if (games.Count == 3) Assert.Equal(x.HighId, games[2].HomeId);
            Assert.Equal(games.Count, games.Select(f => f.Date).Distinct().Count());
        }
        // No top-league player's lines land in tier 2's stats.
        var topPlayers = nabl.Teams.SelectMany(t => t.Roster).Select(p => p.Id).ToHashSet();
        Assert.DoesNotContain(t2.Season.Stats.Keys, topPlayers.Contains);

        // The summer: the series decide who moves.
        nabl.History.Add(new SeasonSummary { Season = 2026, Records = table.All().Select(r => (r.Team.Id, r.Wins, r.Losses)).ToList() });
        var outcome = series.Select(x => (top: nabl.Teams.First(t => t.Id == x.LowId), challenger: t2.Clubs.First(t => t.Id == x.HighId), up: x.WinnerId == x.HighId)).ToList();
        var news = UsaPyramid.Promote(world, nabl, new StaffGenerator(new Rng(1)), new DateOnly(2027, 8, 15));
        foreach (var (top, challenger, up) in outcome)
        {
            if (up) { Assert.Contains(challenger, nabl.Teams); Assert.Contains(top, t2.Clubs); }
            else { Assert.Contains(top, nabl.Teams); Assert.Contains(challenger, t2.Clubs); }
        }
        Assert.Equal(32, nabl.Teams.Count);
        Assert.Equal(32, t2.Clubs.Count);
        Assert.All(nabl.Teams.GroupBy(t => (t.Conference, t.Division)), g => Assert.Equal(4, g.Count()));
        if (outcome.Any(o => !o.up)) Assert.Contains(news, n => n.Title.StartsWith("Staying up"));
        if (outcome.Any(o => o.up)) Assert.Contains(news, n => n.Body.Contains("promotion playoff"));
    }

    [Fact]
    public void A_bracket_seeded_before_the_playoffs_existed_gets_none()
    {
        // A save from before run 102, made between tier 2's last regular-season day and the NBA's: its bracket already has the
        // division winners in a first round, so no club is booked twice on one day (review, run 102).
        var (nabl, world) = NewWorld(5);
        var t2 = world[UsaPyramid.Tier2];
        world.PlayThrough(new DateOnly(2027, 4, 12), new GameSimulator());
        t2.Season.Series.Add(new SeriesState { Id = t2.Season.Series.Count + 1, RoundIndex = 0, Round = "First round", BestOf = 5, HighId = t2.Clubs[0].Id, LowId = t2.Clubs[1].Id });
        Assert.Empty(world.StartPromotionPlayoffs(TopTable(nabl), new DateOnly(2027, 4, 12)));
    }

    [Fact]
    public void Without_a_promotion_playoff_the_old_automatic_swap_stands()
    {
        // A world whose season played without the session starting the series (an older save's season): last down, winners up.
        var (nabl, world) = NewWorld(7);
        world.FinishSeasons(new GameSimulator());
        var t2 = world[UsaPyramid.Tier2];
        Assert.DoesNotContain(t2.Season.Series, x => UsaPyramid.IsPromotion(x.Round));
        var table = TopTable(nabl);
        nabl.History.Add(new SeasonSummary { Season = 2026, Records = table.All().Select(r => (r.Team.Id, r.Wins, r.Losses)).ToList() });
        var down = nabl.Teams.GroupBy(t => (t.Conference, t.Division)).Select(g => g.OrderBy(t => t.Id).First()).ToList();
        var up = DivisionWinners(world, t2);
        UsaPyramid.Promote(world, nabl, new StaffGenerator(new Rng(1)), new DateOnly(2027, 8, 15));
        Assert.All(down, t => Assert.Contains(t, t2.Clubs));
        Assert.All(up, t => Assert.Contains(t, nabl.Teams));
    }

    [Fact]
    public void Your_NBA_club_plays_its_promotion_playoff_and_the_board_reads_the_result()
    {
        var s = GameSession.NewGame(2026, humanTeamId: 1, savePath: null);
        Assert.True(s.InNabl);
        // A club of 1s finishes last in its division.
        foreach (var p in s.HumanTeam.Roster) { foreach (var a in Enum.GetValues<Attr>()) p.Attributes[a] = 1; p.CurrentAbility = 20; }
        // ...and its owner keeps the coach all season (a protected season, as after a relegation).
        s.Job.ProtectedSeason = s.League.Season; s.Job.ProtectedReason = "rebuild";
        while (s.Phase != SessionPhase.Postseason) s.AdvanceDay();
        var t2 = s.World[UsaPyramid.Tier2];
        var mine = t2.Season.Series.Single(x => UsaPyramid.IsPromotion(x.Round) && x.LowId == s.HumanTeam.Id);
        Assert.Equal(8, t2.Season.Series.Count(x => UsaPyramid.IsPromotion(x.Round)));
        Assert.Contains(s.Inbox, m => m.Title == "Regular season complete" && m.Body.Contains("promotion playoff"));
        Assert.False(s.GoingDown());   // not decided yet

        int games = 0; bool watched = false;
        for (int day = 0; day < 20 && !mine.Complete; day++)
        {
            if (s.HumanPlaysToday)
            {
                games++;
                if (!watched)
                {
                    // Watch the first game live, as the playoffs' games are watched.
                    var (live, played) = s.BeginLiveDay();
                    Assert.NotNull(live);
                    s.FinishLiveDay(live, played);
                    watched = true;
                    continue;
                }
            }
            s.AdvanceDay();
        }
        Assert.True(mine.Complete);
        Assert.Equal(mine.HighWins + mine.LowWins, games);
        var promo = s.MyGames().Where(g => UsaPyramid.IsPromotion(g.Stage)).ToList();
        Assert.Equal(games, promo.Count);
        Assert.All(promo, g => Assert.True(g.Played));
        Assert.Equal(mine.LoserId == s.HumanTeam.Id, s.GoingDown());
        Assert.Contains(s.Inbox, m => m.Title.StartsWith("Promotion playoff"));
    }

    [Fact]
    public void Your_tier_two_division_winner_waits_for_its_playoff_then_plays_it()
    {
        int clubId = GameSession.NewGame(2026, humanTeamId: 1, savePath: null).World[UsaPyramid.Tier2].Clubs[0].Id;
        var s = GameSession.NewGame(2026, humanTeamId: clubId, savePath: null);
        Assert.Equal(UsaPyramid.Tier2, s.HumanLeague!.Key);
        // A club of 20s wins its division.
        foreach (var p in s.HumanTeam.Roster) { foreach (var a in Enum.GetValues<Attr>()) p.Attributes[a] = 20; p.CurrentAbility = 190; }
        bool waited = false;
        while (s.Phase != SessionPhase.Postseason)
        {
            if (s.AwaitingPromotionPlayoff) { waited = true; Assert.False(s.HumanSeasonOver); }
            s.AdvanceDay();
        }
        Assert.True(waited);
        var t2 = s.World[UsaPyramid.Tier2];
        // Not in tier 2's own title bracket.
        Assert.DoesNotContain(t2.Season.Series, x => !UsaPyramid.IsPromotion(x.Round) && (x.HighId == clubId || x.LowId == clubId));
        var mine = t2.Season.Series.Single(x => UsaPyramid.IsPromotion(x.Round) && x.HighId == clubId);
        Assert.Contains(s.Inbox, m => m.Title == "Promotion playoff");
        Assert.False(s.HumanSeasonOver);
        for (int day = 0; day < 20 && !mine.Complete; day++) s.AdvanceDay();
        Assert.True(mine.Complete);
        var promo = s.MyGames().Where(g => UsaPyramid.IsPromotion(g.Stage)).ToList();
        Assert.Equal(mine.HighWins + mine.LowWins, promo.Count);
        Assert.True(promo[0].Home);
        Assert.All(promo, g => Assert.NotNull(g.Result));   // coached: the full engine's box score
    }
}
