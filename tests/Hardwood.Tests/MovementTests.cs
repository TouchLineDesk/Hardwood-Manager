using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Stage 2 player movement: buyouts (#53), summer-only moves (#54), draft-and-stash (#55), import limits (#56).</summary>
public class MovementTests
{
    private static (League league, GameWorld world, Offseason off) SummerAtFreeAgency(ulong seed = 11)
    {
        var league = new WorldGenerator(new Rng(seed)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, seed, Array.Empty<string>(), new DateOnly(2026, 10, 1));
        var rng = new Rng(seed + 1);
        var outcome = new SeasonSimulator(rng).Run(league, ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), rng));
        var off = new Offseason(league, outcome, new Dictionary<int, PlayerSeasonStats>(), rng);
        off.Begin();
        world.BeginSummer(2027, new GameSimulator(), league.FreeAgents);
        WorldMarket.Attach(world, off, null);
        off.AiReSignAll();
        off.AdvanceToDraft();
        off.AutoDraftUntil(null);
        off.AdvanceToFreeAgency();
        return (league, world, off);
    }

    [Fact]
    public void Every_overseas_contract_has_a_buyout_clause()
    {
        var (_, world, _) = SummerAtFreeAgency();
        Assert.All(world.Players, p => Assert.True(p.Contract!.Buyout >= 250_000, p.Name));
        // Young players cost more to buy out than veterans on the same deal.
        var on = new DateOnly(2027, 7, 1);
        long salary = 1_000_000;
        var young = new Player { FirstName = "Y", LastName = "Y", BirthDate = on.AddYears(-20) };
        var vet = new Player { FirstName = "V", LastName = "V", BirthDate = on.AddYears(-29) };
        Assert.True(GameWorld.BuyoutFor(young, salary, 2, on) > GameWorld.BuyoutFor(vet, salary, 2, on));
    }

    [Fact]
    public void A_buyout_splits_between_team_allowance_and_player_and_he_joins_the_team()
    {
        var (league, world, off) = SummerAtFreeAgency();
        var fin = league.Finance;
        var team = league.Teams.OrderByDescending(off.Room).First();
        while (team.Roster.Count > 12) off.Waive(team, team.Roster.OrderBy(p => p.CurrentAbility).First());
        var target = world.Players.Where(p => p.DraftRightsTeamId == null).OrderByDescending(p => p.CurrentAbility).First();
        target.Contract!.Buyout = WorldMarket.Allowance(fin) + 200_000; // he pays a little himself
        target.Contract.Salary = 500_000;
        var terms = WorldMarket.Terms(world, off, team, target);
        Assert.True(terms.Possible, terms.Note);
        Assert.Equal(WorldMarket.Allowance(fin), terms.TeamPays);
        Assert.Equal(200_000, terms.PlayerPays);

        var club = WorldMarket.ClubOf(world, target)!;
        Assert.True(WorldMarket.BuyOut(world, off, team, target));
        Assert.Contains(target, team.Roster);
        Assert.DoesNotContain(target, club.Roster);
        Assert.Equal(team.Id, target.TeamId);

        // A buyout the player would mostly fund himself is refused.
        var other = world.Players.Where(p => p.DraftRightsTeamId == null).OrderByDescending(p => p.CurrentAbility).First();
        other.Contract!.Buyout = 50_000_000;
        Assert.False(WorldMarket.Terms(world, off, team, other).Possible);
    }

    [Fact]
    public void Moves_happen_only_in_free_agency()
    {
        var league = new WorldGenerator(new Rng(4)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, 4, Array.Empty<string>(), new DateOnly(2026, 10, 1));
        var rng = new Rng(5);
        var outcome = new SeasonSimulator(rng).Run(league, ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), rng));
        var off = new Offseason(league, outcome, new Dictionary<int, PlayerSeasonStats>(), rng);
        off.Begin();
        Assert.False(WorldMarket.BuyOut(world, off, league.Teams[0], world.Players.First())); // still re-signing
    }

    [Fact]
    public void A_stashed_pick_plays_abroad_and_his_team_keeps_the_rights()
    {
        var (league, world, off) = SummerAtFreeAgency();
        var d = off.Report.Draft.First(x => x.Round == 2 && league.FreeAgents.Contains(x.Player));
        Assert.Null(WorldMarket.CannotStash(off, d.Team, d.Player));
        Assert.True(WorldMarket.Stash(world, off, d.Team, d.Player, new Rng(1)));
        Assert.Equal(d.Team.Id, d.Player.DraftRightsTeamId);
        Assert.Contains(d.Player, world.Players);
        Assert.Contains(d.Player, WorldMarket.RightsHeld(world, league, d.Team));

        // Nobody else can sign him; his team can bring him over (the buyout applies).
        var other = league.Teams.First(t => t != d.Team);
        Assert.False(WorldMarket.Terms(world, off, other, d.Player).Possible);
        d.Player.Contract!.Buyout = 100_000;
        while (d.Team.Roster.Count >= league.Finance.RosterMax) off.Waive(d.Team, d.Team.Roster.OrderBy(p => p.CurrentAbility).First());
        Assert.True(WorldMarket.BuyOut(world, off, d.Team, d.Player));
        Assert.Null(d.Player.DraftRightsTeamId);
        Assert.Contains(d.Player, d.Team.Roster);
    }

    [Fact]
    public void Overseas_clubs_never_go_over_their_import_limit()
    {
        var (league, world, off) = SummerAtFreeAgency(23);
        WorldMarket.AiMoves(world, off, null);
        off.AiFreeAgency(null);
        var before = world.Competitions.SelectMany(c => c.Clubs.Select(t => (t, n: t.Roster.Count(p => !c.Profile.LocalNationalities.Contains(p.Nationality)))))
            .ToDictionary(x => x.t.Id, x => x.n);
        off.Complete(); // the end of summer: clubs sign from the pool

        foreach (var c in world.Competitions.Where(c => c.Profile.Kind == CompetitionKind.Domestic))
            foreach (var t in c.Clubs)
                Assert.True(t.Roster.Count(p => !c.Profile.LocalNationalities.Contains(p.Nationality)) <= Math.Max(c.Profile.ForeignLimit, before[t.Id]),
                    $"{t.FullName} has too many imports");
        Assert.All(world.Clubs, t => Assert.True(t.Roster.Count >= GameWorld.RosterSize, t.FullName));
        Assert.Equal(2027, world.SummerEnded);
    }

    [Fact]
    public void Players_move_both_ways_over_several_summers()
    {
        var league = new WorldGenerator(new Rng(9)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        var world = GameWorld.Generate(league, 9, Array.Empty<string>(), new DateOnly(2026, 10, 1));
        var career = new Career(new Rng(10));
        var sim = new GameSimulator();
        int signedAbroad = 0, stashed = 0, intl = 0, picks = 0;
        for (int i = 0; i < 2; i++)
        {
            world.FinishSeasons(sim);
            var played = career.PlaySeason(league, world: world, worldSim: sim);
            signedAbroad += world.SignedAbroad.Count;
            stashed += world.Stashes.Count;
            intl += played.Offseason.Draft.Count(d => WorldMarket.IsInternational(d.Player));
            picks += played.Offseason.Draft.Count;
        }
        Assert.True(signedAbroad > 0, "overseas clubs should sign from the shared pool");
        Assert.True(stashed > 0, "AI teams should stash some international picks");
        Assert.InRange(intl / (double)picks, 0.12, 0.45); // about a quarter (decision #55)
        Assert.All(league.Teams, t => Assert.InRange(t.Roster.Count, league.Finance.RosterMin, league.Finance.RosterMax));
    }
}
