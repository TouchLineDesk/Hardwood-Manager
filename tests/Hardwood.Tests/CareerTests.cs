using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

public class CareerTests
{
    private static League NewLeague(ulong seed = 11) =>
        new WorldGenerator(new Rng(seed)).Generate("Test League", 2026, new DateOnly(2026, 10, 1));

    [Fact]
    public void Save_and_load_round_trip_preserves_the_world()
    {
        var league = NewLeague();
        var path = Path.Combine(Path.GetTempPath(), $"hw-{Guid.NewGuid():N}.save");
        try
        {
            var career = new Career(new Rng(3), new SaveFile(path));
            career.PlaySeason(league);

            var loaded = new SaveFile(path).Load();
            Assert.Equal(league.Season, loaded.Season);
            Assert.Equal(league.Teams.Count, loaded.Teams.Count);
            Assert.Equal(league.AllActivePlayers.Count(), loaded.AllActivePlayers.Count());
            Assert.Equal(league.FreeAgents.Count, loaded.FreeAgents.Count);
            Assert.Equal(league.Retired.Count, loaded.Retired.Count);
            Assert.Single(loaded.History);
            Assert.Equal(league.History[0].ChampionTeamId, loaded.History[0].ChampionTeamId);
            Assert.Equal(30, loaded.History[0].Records.Count);

            foreach (var team in league.Teams)
            {
                var other = loaded.TeamById(team.Id);
                Assert.Equal(team.Roster.Select(p => p.Id).OrderBy(x => x), other.Roster.Select(p => p.Id).OrderBy(x => x));
                foreach (var p in team.Roster)
                {
                    var q = other.Roster.First(x => x.Id == p.Id);
                    Assert.Equal(p.CurrentAbility, q.CurrentAbility);
                    Assert.Equal(p.Contract!.Salary, q.Contract!.Salary);
                    foreach (var a in AttrInfo.All) Assert.Equal(p.Attributes[a], q.Attributes[a]);
                }
            }

            var stats = new SaveFile(path).LoadStats(2026);
            Assert.True(stats.Count > 300);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_short_roster_near_the_cap_ends_at_most_two_minimum_deals_over()
    {
        // The known bug: a team near the cap that needs 3+ minimum signings to reach the roster minimum
        // used to end the offseason three or more minimum deals over the hard cap.
        var league = NewLeague();
        var rng = new Rng(11);
        var schedule = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), rng);
        var outcome = new SeasonSimulator(rng).Run(league, schedule);
        var stats = new Dictionary<int, PlayerSeasonStats>();
        var off = new Offseason(league, outcome, stats, rng);
        off.Begin();
        off.AdvanceToDraft();
        off.AdvanceToFreeAgency();

        var fin = league.Finance;
        var team = league.Teams[4];
        while (team.Roster.Count > fin.RosterMin - 4) off.Waive(team, team.Roster.OrderBy(p => p.Contract!.Salary).First());
        // Spread salary so payroll sits just under the apron with only 9 players.
        long each = (fin.Apron - fin.MinSalary / 2) / team.Roster.Count;
        foreach (var p in team.Roster) p.Contract!.Salary = each;

        off.Complete();
        Assert.True(team.Roster.Count >= fin.RosterMin, $"roster {team.Roster.Count}");
        Assert.True(ContractMarket.Payroll(team) <= fin.Apron + fin.MinSalary * 2,
            $"payroll {ContractMarket.Payroll(team):N0} vs apron {fin.Apron:N0} + 2 x {fin.MinSalary:N0}");
        foreach (var t in league.Teams)
            Assert.True(ContractMarket.Payroll(t) <= fin.Apron + fin.MinSalary * 2, t.FullName);
    }

    [Fact]
    public void Offseason_keeps_rosters_legal()
    {
        var league = NewLeague();
        var career = new Career(new Rng(5));
        for (int i = 0; i < 3; i++) career.PlaySeason(league);

        foreach (var t in league.Teams)
        {
            Assert.InRange(t.Roster.Count, league.Finance.RosterMin, league.Finance.RosterMax);
            Assert.All(t.Roster, p => Assert.NotNull(p.Contract));
            Assert.All(t.Roster, p => Assert.Equal(t.Id, p.TeamId));
            // Nobody passes the apron (roster-minimum signings may nudge over it by two minimum deals at most).
            Assert.True(ContractMarket.Payroll(t) <= league.Finance.Apron + league.Finance.MinSalary * 2);
        }
        var ids = league.AllActivePlayers.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(2029, league.Season);
        Assert.Equal(3, league.History.Count);
    }

    [Fact]
    public void Young_players_grow_and_old_players_decline()
    {
        var rng = new Rng(9);
        var gen = new PlayerGenerator(rng);
        var dev = new DevelopmentEngine(rng);
        var date = new DateOnly(2027, 7, 1);

        int youngGain = 0, oldLoss = 0;
        for (int i = 0; i < 200; i++)
        {
            var young = gen.Generate(Position.SG, 90, 20, new DateOnly(2026, 10, 1));
            young.PotentialAbility = 150;
            var r1 = dev.Develop(young, date, mpg: 25);
            youngGain += r1.NewCa - r1.OldCa;

            var old = gen.Generate(Position.PF, 130, 34, new DateOnly(2026, 10, 1));
            var r2 = dev.Develop(old, date, mpg: 25);
            oldLoss += r2.OldCa - r2.NewCa;
        }
        Assert.True(youngGain / 200.0 > 8, $"young gain {youngGain / 200.0}");
        Assert.True(oldLoss / 200.0 > 3, $"old loss {oldLoss / 200.0}");
    }

    [Fact]
    public void Attributes_stay_anchored_to_ability_after_many_seasons()
    {
        var rng = new Rng(21);
        var gen = new PlayerGenerator(rng);
        var dev = new DevelopmentEngine(rng);
        var p = gen.Generate(Position.PG, 95, 19, new DateOnly(2026, 10, 1));
        p.PotentialAbility = 160;
        for (int year = 1; year <= 15; year++)
            dev.Develop(p, new DateOnly(2026 + year, 7, 1), mpg: 28);

        double mean = AttrInfo.All.Average(a => (double)p.Attributes[a]);
        double expected = PlayerGenerator.MeanAttributeFor(p.CurrentAbility);
        Assert.InRange(mean, expected - 0.6, expected + 0.6);
    }
}
