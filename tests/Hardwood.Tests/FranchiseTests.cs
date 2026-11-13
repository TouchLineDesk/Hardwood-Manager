using Hardwood.Core;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Money buys things, part 1 (item M, #281): facilities with real effects, costs, build times and an owner's appetite;
/// your club gets proposals to approve or delegate (D10).
/// </summary>
public class FranchiseTests
{
    private const long Lr = Budgets.NablRevenue;

    private static List<Team> League(int n = 30)
    {
        var teams = new List<Team>();
        for (int i = 1; i <= n; i++)
        {
            var t = new Team { Id = i, City = $"City{i}", Name = "Club", Abbreviation = $"C{i:00}", Division = "D", ArenaCapacity = 18000 };
            t.Money.Revenue = Lr; t.Money.FanBase = 50; t.Money.Ready = true;
            teams.Add(t);
        }
        foreach (var t in teams) Franchise.Seed(t, 18000);
        Franchise.SetEdges(teams);
        return teams;
    }

    [Fact]
    public void Seeding_is_deterministic_and_edges_are_zero_sum()
    {
        var a = League(); var b = League();
        for (int i = 0; i < a.Count; i++)
            foreach (var k in ClubFacilities.Kinds) Assert.Equal(a[i].Money.Facilities.Get(k).Level, b[i].Money.Facilities.Get(k).Level);
        foreach (var k in ClubFacilities.Kinds)
        {
            Assert.InRange(a.Sum(t => t.Money.Facilities.Get(k).Edge), -1e-9, 1e-9);
            Assert.All(a, t => Assert.InRange(t.Money.Facilities.Get(k).Level, 1, 5));
        }
    }

    [Fact]
    public void A_rich_club_invests_above_its_reserve_and_a_poor_one_doesnt()
    {
        var teams = League();
        var rich = teams[0]; var poor = teams[1];
        rich.Money.Balance = 400_000_000; poor.Money.Balance = 20_000_000;
        rich.Owner.Ambition = 16;
        Franchise.Summer(rich, Lr, 18000, 2027);
        Franchise.Summer(poor, Lr, 18000, 2027);
        Assert.NotEmpty(rich.Money.Facilities.Building);
        Assert.True(rich.Money.Facilities.LastInvested > 0);
        Assert.True(rich.Money.Balance >= Franchise.Reserve(rich) - 1, "the owner never spends into his reserve");
        Assert.Empty(poor.Money.Facilities.Building);
        Assert.Equal(20_000_000 - poor.Money.Facilities.LastUpkeep, poor.Money.Balance);
        Assert.Empty(rich.Money.Facilities.Proposals); // an AI club builds; it doesn't propose
    }

    [Fact]
    public void An_upgrade_opens_next_summer_and_a_dated_facility_drops_a_level()
    {
        var t = League()[0];
        var f = t.Money.Facilities;
        f.Practice.Level = 3; f.Practice.Age = 0;
        Franchise.Start(t, new FacilityProject { Kind = FacilityKind.Practice, ToLevel = 4, Cost = Franchise.CostTo(FacilityKind.Practice, 4, Lr), SeasonsLeft = 1 });
        Assert.Equal(3, f.Practice.Level);
        t.Money.Balance = 0;
        Franchise.Summer(t, Lr, 18000, 2027);
        Assert.Equal(4, f.Practice.Level);
        Assert.Contains(f.LastNews, n => n.Contains("upgrade opened"));

        f.Medical.Level = 4; f.Medical.Age = Franchise.Info(FacilityKind.Medical).Lifespan;
        Franchise.Summer(t, Lr, 18000, 2028);
        Assert.Equal(3, f.Medical.Level);
        Assert.Contains(f.LastNews, n => n.Contains("dated"));
    }

    [Fact]
    public void A_new_arena_takes_three_summers_of_payments_and_adds_seats()
    {
        var t = League()[0];
        var f = t.Money.Facilities;
        var p = new FacilityProject { Kind = FacilityKind.NewArena, ToLevel = 5, Cost = Franchise.CostTo(FacilityKind.NewArena, 5, Lr), SeasonsLeft = 3, Instalments = 3, Capacity = 19_500 };
        t.Money.Balance = 2_000_000_000;
        long before = t.Money.Balance;
        Franchise.Start(t, p);
        Assert.Equal(before - p.Instalment, t.Money.Balance);
        f.YouDecide = true; // no other projects in the way
        Franchise.Summer(t, Lr, 18000, 2027);
        Franchise.Summer(t, Lr, 18000, 2028);
        Assert.Equal(18000, t.ArenaCapacity);
        Assert.Equal(3 * p.Instalment, f.TotalInvested);
        Franchise.Summer(t, Lr, 18000, 2029);
        Assert.Equal(19_500, t.ArenaCapacity);
        Assert.Equal(5, f.Arena.Level);
        Assert.Equal(1, f.ArenasBuilt);
        Assert.Equal(3 * p.Instalment, f.TotalInvested);
    }

    [Fact]
    public void Your_club_gets_proposals_the_owner_will_fund()
    {
        var t = League()[0];
        t.Money.Balance = 300_000_000;
        t.Money.Facilities.YouDecide = true;
        Franchise.Summer(t, Lr, 18000, 2027);
        var f = t.Money.Facilities;
        Assert.Empty(f.Building);
        Assert.InRange(f.Proposals.Count, 1, 3);
        Assert.All(f.Proposals, p => Assert.True(p.Instalment <= Franchise.Spendable(t)));
    }

    [Fact]
    public void Your_owner_takes_a_distribution_but_keeps_the_proposals_fundable()
    {
        // Review #11: only computer clubs paid their owners, so a managed club that didn't build sat on $606M in tier 2.
        var t = League()[0];
        t.Money.Balance = 2_000_000_000;
        t.Money.Facilities.YouDecide = true;
        Franchise.Summer(t, Lr, 18000, 2027);
        var f = t.Money.Facilities;
        Assert.True(f.LastDistribution > 500_000_000, $"{f.LastDistribution:N0}");
        Assert.True(t.Money.Balance <= Math.Max(t.Money.Revenue, Franchise.Reserve(t) + f.Proposals.Sum(p => p.Instalment)) + 1_000_000, $"{t.Money.Balance:N0}");
        Assert.InRange(f.Proposals.Count, 1, 3);
        Assert.All(f.Proposals, p => Assert.True(p.Instalment <= Franchise.Spendable(t)));
        Assert.Contains(f.LastNews, n => n.StartsWith("The owner took "));
    }

    [Fact]
    public void Facilities_ahead_of_the_league_help_and_behind_it_hurt()
    {
        var t = League()[0];
        var f = t.Money.Facilities;
        foreach (var k in ClubFacilities.Kinds) f.Get(k).Edge = 0;
        double q0 = StaffEffects.TrainingQuality(t), i0 = StaffEffects.InjuryRiskFactor(t), r0 = StaffEffects.RecoveryFactor(t), s0 = StaffEffects.ScoutingKnowledge(t);
        long rev0 = Budgets.Revenue(t, Lr, 18000, 0.5);
        foreach (var k in ClubFacilities.Kinds) f.Get(k).Edge = 1.5;
        Assert.True(StaffEffects.TrainingQuality(t) > q0);
        Assert.True(StaffEffects.InjuryRiskFactor(t) < i0);
        Assert.True(StaffEffects.RecoveryFactor(t) < r0);
        Assert.True(StaffEffects.ScoutingKnowledge(t) > s0);
        Assert.True(Budgets.Revenue(t, Lr, 18000, 0.5) > rev0);
        foreach (var k in ClubFacilities.Kinds) f.Get(k).Edge = -1.5;
        Assert.True(StaffEffects.TrainingQuality(t) < q0);
        Assert.True(StaffEffects.InjuryRiskFactor(t) > i0);
    }

    [Fact]
    public void The_academy_is_gone_and_a_save_that_had_one_drops_its_projects()
    {
        // Danny, #291: four facilities, no G League affiliate & academy.
        Assert.Equal(new[] { FacilityKind.Practice, FacilityKind.Medical, FacilityKind.Analytics, FacilityKind.Arena }, ClubFacilities.Kinds);
        Assert.DoesNotContain(Franchise.All, i => i.Kind == FacilityKind.Academy);
        Assert.Equal(FacilityKind.NewArena, Franchise.Info(FacilityKind.NewArena).Kind);
        Assert.Equal(FacilityKind.Arena, Franchise.Info(FacilityKind.Arena).Kind);
        var opts = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        var json = "{\"Revenue\":245000000,\"Balance\":900000000,\"Ready\":true,\"Facilities\":{\"Ready\":true,\"Academy\":{\"Level\":5}," +
                   "\"Building\":[{\"Kind\":3,\"ToLevel\":4,\"Cost\":9,\"SeasonsLeft\":1}],\"Proposals\":[{\"Kind\":3,\"ToLevel\":4,\"Cost\":9,\"SeasonsLeft\":1}]}}";
        var t = League()[0];
        t.Money = System.Text.Json.JsonSerializer.Deserialize<ClubFinance>(json, opts)!;
        Franchise.Summer(t, Lr, 18000, 2027);
        Assert.DoesNotContain(t.Money.Facilities.Building, p => p.Kind == FacilityKind.Academy);
        Assert.DoesNotContain(t.Money.Facilities.Proposals, p => p.Kind == FacilityKind.Academy);
        // A young player's training no longer depends on the academy's level.
        var young = new Player { FirstName = "Young", LastName = "Prospect", BirthDate = new DateOnly(2008, 1, 1) };
        t.Money.Facilities.Academy.Edge = 3; t.Money.Facilities.Practice.Edge = 0;
        Assert.Equal(0, StaffEffects.FacilityTraining(t, young, new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void An_old_save_without_facilities_loads_the_defaults()
    {
        var opts = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        var m = System.Text.Json.JsonSerializer.Deserialize<ClubFinance>("{\"Revenue\":245000000,\"Balance\":5,\"Ready\":true}", opts)!;
        Assert.NotNull(m.Facilities);
        Assert.False(m.Facilities.Ready);
        Assert.Equal(3, m.Facilities.Practice.Level);
        var back = System.Text.Json.JsonSerializer.Deserialize<ClubFinance>(System.Text.Json.JsonSerializer.Serialize(new ClubFinance { Facilities = { Ready = true, ArenaYears = 7, Building = { new FacilityProject { Kind = FacilityKind.NewArena, Cost = 9 } } } }, opts), opts)!;
        Assert.Equal(7, back.Facilities.ArenaYears);
        Assert.Single(back.Facilities.Building);
    }

    [Fact]
    public void A_season_brings_your_club_proposals_and_approval_pays_for_one()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-fac-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var s = GameSession.NewGame(4, 3, path);
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            Assert.Contains(s.Inbox, m => m.Title == "Facilities: the summer meeting");
            var f = s.MyFacilities;
            Assert.True(f.YouDecide);
            Assert.True(f.Ready);
            // Nobody else waits for you, and the computer's owners spent.
            Assert.All(s.League.Teams.Where(t => t != s.HumanTeam), t => Assert.Empty(t.Money.Facilities.Proposals));
            Assert.Contains(s.League.Teams, t => t.Money.Facilities.LastInvested > 0);

            s.HumanTeam.Money.Balance = 600_000_000;
            f.Proposals = Franchise.Candidates(s.HumanTeam, s.MyLeagueRevenue, 18000, 2027, s.FacilityBudget).Take(3).ToList();
            Assert.NotEmpty(f.Proposals);
            var first = f.Proposals[0];
            long before = s.HumanTeam.Money.Balance;
            Assert.Null(s.ApproveFacility(0));
            Assert.Equal(before - first.Instalment, s.HumanTeam.Money.Balance);
            Assert.Contains(f.Building, b => b.Kind == first.Kind);

            s.DelegateFacilities(true);
            Assert.False(f.YouDecide);
            s.SaveNow();
            var back = GameSession.Load(path);
            Assert.Contains(back.MyFacilities.Building, b => b.Kind == first.Kind);
            Assert.True(back.Delegated.Facilities);
            Assert.Equal(f.Practice.Level, back.MyFacilities.Practice.Level);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
