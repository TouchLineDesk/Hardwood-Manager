using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Danny's draft (D40, #410): one round, the best graduates, an equal-odds lottery, real careers only.</summary>
public class CollegeDraftTests
{
    private static readonly DateOnly On = new(2026, 10, 1);

    private static (League league, GameWorld world, SeasonPlayed played, int season) OneSeason(ulong seed, bool real)
    {
        var league = new WorldGenerator(new Rng(seed)).Generate(real ? RealWorldData.LeagueName : "NABL", 2026, On);
        var world = GameWorld.Generate(league, seed, Array.Empty<string>(), On);
        if (real) RealWorldData.Apply(league, world, seed);
        Hardwood.Core.Jobs.Board.EnsureOwnersAndCoaches(league, seed);
        Hardwood.Core.Jobs.Board.EnsureOwnersAndCoaches(world.Clubs, seed);
        var sim = new GameSimulator();
        int season = league.Season;
        world.FinishSeasons(sim);
        var played = new Career(new Rng(seed ^ 0xA0D17)).PlaySeason(league, world: world, worldSim: sim);
        return (league, world, played, season);
    }

    [Fact]
    public void A_real_career_drafts_the_best_graduates_into_the_eligible_clubs()
    {
        var (league, world, played, season) = OneSeason(2026, real: true);
        var draft = played.Offseason.Draft;
        var before = played.Outcome.Standings.All().Select(r => r.Team).ToHashSet();
        var extra = played.Outcome.ThirdPlaceWinner;
        Assert.NotNull(extra);
        Assert.Equal(33, draft.Count);
        // One round, one pick each; the third-place winner's extra pick is the last.
        Assert.All(draft, d => Assert.Equal(1, d.Round));
        Assert.Equal(Enumerable.Range(1, draft.Count), draft.Select(d => d.Pick));
        Assert.Equal(extra, draft[^1].Team);
        var clubs = draft.Take(32).Select(d => d.Team).ToList();
        Assert.Equal(32, clubs.Distinct().Count());
        // The picks are next season's NBA: the top three in each division stay up and the tier-2 division winners came up,
        // so every club with a pick is in the league now, and none of the relegated clubs has one.
        Assert.All(clubs, t => Assert.Contains(t, league.Teams));
        Assert.Equal(24, clubs.Count(before.Contains));
        Assert.Equal(8, clubs.Count(t => !before.Contains(t)));
        // Every pick is one of this year's graduates, signed for three years and on his club's roster.
        Assert.Equal(draft.Count, draft.Select(d => d.Player.Id).Distinct().Count());
        Assert.All(draft, d =>
        {
            Assert.Equal(season + 1, d.Player.RookieSeason);
            Assert.NotNull(d.Player.Contract);
            Assert.Equal(d.Player.Draft!.Pick, d.Pick);
        });
        // The best of the class went in the draft: nobody left in free agency was rated above the best drafted player's
        // floor by the measure the pool uses (the free-agent pool may have signed some since; only check those still there).
        var drafted = draft.Select(d => d.Player).ToHashSet();
        double floor = drafted.Min(p => ContractMarket.Value(p, new DateOnly(season + 1, 7, 1)));
        var undrafted = league.FreeAgents.Where(p => p.RookieSeason == season + 1 && !drafted.Contains(p)).ToList();
        Assert.All(undrafted, p => Assert.True(ContractMarket.Value(p, new DateOnly(season + 1, 7, 1)) <= floor + 1e-9, p.Name));
    }

    [Fact]
    public void The_made_up_world_has_no_draft()
    {
        var (_, _, played, _) = OneSeason(77, real: false);
        Assert.Empty(played.Offseason.Draft);
    }

    [Fact]
    public void The_lottery_gives_every_club_the_same_odds()
    {
        var clubs = Enumerable.Range(1, 32).Select(i => new Team { Id = i, City = "C" + i, Name = "N" + i, Abbreviation = "A" + i, Conference = Conference.East, Division = "D" }).ToList();
        var firsts = new int[33];
        var rng = new Rng(5);
        const int n = 32_000;
        for (int k = 0; k < n; k++)
        {
            var order = CollegeDraft.Lottery(clubs, clubs[0], rng);
            Assert.Equal(33, order.Count);
            Assert.Equal(clubs[0], order[^1]);
            firsts[order[0].Id]++;
        }
        // About 1,000 first picks each (1 in 32); allow five standard deviations.
        Assert.All(Enumerable.Range(1, 32), i => Assert.InRange(firsts[i], 1000 - 155, 1000 + 155));
    }
}

/// <summary>The draft from your side (D40): the lottery message, your pick on the clock, and a save in the middle of it.</summary>
public class CollegeDraftSessionTests
{
    [Fact]
    public void You_pick_on_the_clock_and_the_draft_survives_a_save()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-draft-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            // The strongest real club: almost always top three in its division, so it has a pick.
            var probe = GameSession.NewGame(31, 1, null, realWorld: true);
            int id = probe.League.Teams.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).First().Id;
            var s = GameSession.NewGame(31, id, path, realWorld: true);
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            var o = s.Offseason!;
            Assert.True(o.CollegeDraftOn);
            Assert.Contains(s.Inbox, m => m.Title == "The draft lottery");
            Assert.InRange(o.PickOwners.Count, 32, 33);
            Assert.Equal(o.PickOwners.Count, o.Prospects.Count);
            bool ours = o.PickOwners.Contains(s.HumanTeam);

            s.AdvanceOffseason(); // contracts done: the computer picks until you're on the clock
            Assert.True(ours, "the strongest club should have a pick");
            Assert.Equal(OffseasonPhase.Draft, s.Offseason!.Phase);
            Assert.Equal(s.HumanTeam, s.Offseason.OnTheClock);
            int next = s.Offseason.NextPickIndex;
            int left = s.Offseason.Prospects.Count;

            // Save and load mid-draft: the order, the pool and the picks made so far come back.
            s.SaveNow();
            var back = GameSession.Load(path);
            Assert.Equal(OffseasonPhase.Draft, back.Offseason!.Phase);
            Assert.Equal(next, back.Offseason.NextPickIndex);
            Assert.Equal(left, back.Offseason.Prospects.Count);
            Assert.Equal(back.HumanTeam.Id, back.Offseason.OnTheClock!.Id);
            Assert.Equal(next, back.Offseason.Report.Draft.Count(d => d.Player != null));

            var choice = back.Offseason.Prospects.OrderByDescending(p => p.PotentialAbility).First();
            back.MakePick(choice);
            Assert.Contains(choice, back.HumanTeam.Roster);
            Assert.Equal(3, choice.Contract!.Years);
            Assert.Equal(OffseasonPhase.FreeAgency, back.Offseason.Phase);
            Assert.Contains(back.Inbox, m => m.Title == "Your draft");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
