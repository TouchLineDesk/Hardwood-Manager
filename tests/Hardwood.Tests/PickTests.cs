using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

public class PickTests
{
    private static League NewLeague(ulong seed = 31) =>
        Legacy.Rules(new WorldGenerator(new Rng(seed)).Generate("L", 2026, new DateOnly(2026, 10, 1)));

    [Fact]
    public void World_starts_with_seven_years_of_picks_per_team()
    {
        var league = NewLeague();
        Assert.Equal(30 * 2 * League.PickYearsAhead, league.Picks.Count);
        Assert.All(league.Teams, t => Assert.Equal(14, league.PicksOwnedBy(t.Id).Count()));
    }

    [Fact]
    public void Traded_pick_is_used_by_its_new_owner_and_protection_rolls_over()
    {
        var league = NewLeague();
        var seller = league.Teams[0]; var buyer = league.Teams[1];
        var pick = league.Picks.First(p => p.Year == 2027 && p.Round == 1 && p.OriginalTeamId == seller.Id);
        pick.OwnerTeamId = buyer.Id;
        pick.ProtectedTop = 30; // always protected: the buyer must receive 2028's pick instead

        var career = new Career(new Rng(4));
        var played = career.PlaySeason(league);
        var report = played.Offseason;

        // Seller kept its 2027 first; the buyer now owns seller's 2028 first.
        Assert.Single(report.ProtectionsTriggered);
        var rolled = league.Picks.First(p => p.Year == 2028 && p.Round == 1 && p.OriginalTeamId == seller.Id);
        Assert.Equal(buyer.Id, rolled.OwnerTeamId);
        Assert.Contains(report.Draft, d => d.Round == 1 && d.Team == seller && d.OriginalTeam == seller);
        // Picks for the new horizon were topped up.
        Assert.Equal(30 * 2 * League.PickYearsAhead, league.Picks.Count);
    }

    [Fact]
    public void Unprotected_traded_pick_goes_to_buyer()
    {
        var league = NewLeague();
        var seller = league.Teams[2]; var buyer = league.Teams[3];
        var pick = league.Picks.First(p => p.Year == 2027 && p.Round == 1 && p.OriginalTeamId == seller.Id);
        pick.OwnerTeamId = buyer.Id;

        var played = new Career(new Rng(6)).PlaySeason(league);
        Assert.Contains(played.Offseason.Draft, d => d.Round == 1 && d.Team == buyer && d.OriginalTeam == seller);
        Assert.Equal(2, played.Offseason.Draft.Count(d => d.Round == 1 && d.Team == buyer));
        Assert.Equal(0, played.Offseason.Draft.Count(d => d.Round == 1 && d.Team == seller));
    }

    [Fact]
    public void Trade_engine_values_picks_and_executes_pick_swaps()
    {
        var league = NewLeague();
        var engine = new TradeEngine(league, new DateOnly(2026, 12, 1));
        var a = league.Teams[4]; var b = league.Teams[5];
        var aFirst = league.PicksOwnedBy(a.Id).First(p => p.Round == 1 && p.Year == 2027);
        var bSecond = league.PicksOwnedBy(b.Id).First(p => p.Round == 2 && p.Year == 2027);

        Assert.True(engine.PickValueTo(b, aFirst) > engine.PickValueTo(b, bSecond));
        var near = league.PicksOwnedBy(a.Id).First(p => p.Round == 1 && p.Year == 2027);
        var far = league.PicksOwnedBy(a.Id).First(p => p.Round == 1 && p.Year == 2032);
        Assert.True(engine.PickValueTo(b, near) > engine.PickValueTo(b, far));

        // A first for a second: B happily accepts.
        var t = new TradeProposal { From = a, To = b };
        t.FromPicks.Add(aFirst);
        t.ToPicks.Add(bSecond);
        var eval = engine.Evaluate(t);
        Assert.True(eval.Legal, eval.LegalityNote);
        Assert.True(eval.Accepted);
        engine.Execute(t);
        Assert.Equal(b.Id, aFirst.OwnerTeamId);
        Assert.Equal(a.Id, bSecond.OwnerTeamId);

        // Can't trade a pick you don't own.
        var bad = new TradeProposal { From = a, To = b };
        bad.FromPicks.Add(aFirst);
        Assert.False(engine.Evaluate(bad).Legal);
    }

    [Fact]
    public void Mid_offseason_save_and_reload_resumes_the_summer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-off-{Guid.NewGuid():N}.save");
        try
        {
            var s = GameSession.NewGame(12, humanTeamId: 7, path);
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);

            // Reload during re-signing: same expiring players, same graduate class waiting.
            var r1 = GameSession.Load(path);
            Assert.Equal(SessionPhase.Offseason, r1.Phase);
            Assert.Equal(OffseasonPhase.ReSigning, r1.Offseason!.Phase);
            Assert.Equal(s.Offseason!.Expiring[7].Count, r1.Offseason.Expiring[7].Count);
            Assert.Equal(s.Offseason.Prospects.Count, r1.Offseason.Prospects.Count);
            Assert.Equal(s.HumanTeam.Money.WageBudget, r1.HumanTeam.Money.WageBudget);

            foreach (var p in r1.MyExpiring.ToList()) r1.ReleaseExpiring(p);
            r1.AdvanceOffseason(); // straight to free agency
            Assert.Equal(OffseasonPhase.FreeAgency, r1.Offseason.Phase);

            // Reload in free agency, finish, and the new season starts.
            var r3 = GameSession.Load(path);
            Assert.Equal(OffseasonPhase.FreeAgency, r3.Offseason!.Phase);
            r3.AdvanceOffseason();
            Assert.Equal(SessionPhase.Preseason, r3.Phase);
            Assert.Equal(2027, r3.League.Season);
            Assert.InRange(r3.HumanTeam.Roster.Count, 13, 15);
            Assert.Empty(r3.League.Picks); // no draft picks on the open market
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

public class AwardsTests
{
    [Fact]
    public void Awards_are_computed_and_persist_across_a_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-aw-{Guid.NewGuid():N}.save");
        try
        {
            var s = GameSession.NewGame(21, humanTeamId: 3, path);
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            Assert.NotNull(s.Midseason);
            Assert.Equal(12, s.Midseason!.AllStarsEast.Count);
            Assert.Equal(12, s.Midseason.AllStarsWest.Count);
            Assert.False(string.IsNullOrEmpty(s.Midseason.AllStarGame));

            var summary = s.League.History.Single();
            var a = summary.Awards!;
            Assert.NotNull(a.Mvp);
            Assert.True(a.MvpVoting[0].Share > a.MvpVoting[^1].Share);
            Assert.True(Math.Abs(a.MvpVoting.Sum(v => v.Share) - 1.0) < 0.01);
            Assert.NotNull(a.Dpoy);
            Assert.Equal(3, a.AllLeague.Count);
            Assert.All(a.AllLeague, t => Assert.Equal(5, t.Count));
            Assert.Equal(15, a.AllLeague.SelectMany(t => t).Distinct().Count());
            Assert.NotNull(a.CoachOfYearTeamId);
            Assert.NotNull(summary.FinalsMvpPlayerId);
            Assert.True(s.League.Careers.Count > 400);
            Assert.Contains(s.Inbox, m => m.Title.StartsWith("MVP:"));
            Assert.Contains(s.Inbox, m => m.Title == "All-Star Game");

            var loaded = GameSession.Load(path);
            var la = loaded.League.History.Single().Awards!;
            Assert.Equal(a.Mvp, la.Mvp);
            Assert.Equal(a.MvpVoting.Count, la.MvpVoting.Count);
            Assert.Equal(a.AllLeague[0], la.AllLeague[0]);
            Assert.Equal(s.League.Careers.Count, loaded.League.Careers.Count);
            var anyCareer = s.League.Careers.Values.First(c => c.Games > 0);
            Assert.Equal(anyCareer.Points, loaded.League.Careers[anyCareer.PlayerId].Points);
            Assert.Equal(anyCareer.GamesByTeam.Count, loaded.League.Careers[anyCareer.PlayerId].GamesByTeam.Count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Hall_of_fame_fills_over_a_long_headless_run()
    {
        var league = Legacy.Rules(new WorldGenerator(new Rng(77)).Generate("L", 2026, new DateOnly(2026, 10, 1)));
        var career = new Career(new Rng(9));
        for (int i = 0; i < 12; i++) career.PlaySeason(league);
        Assert.True(league.HallOfFame.Count >= 1, $"HOF count {league.HallOfFame.Count}");
        Assert.True(league.HallOfFame.Count <= 40, $"HOF count {league.HallOfFame.Count}");
        Assert.All(league.History, h => Assert.NotNull(h.Awards));
        // Rookie of the Year exists from season 2 on (season 1 has no drafted rookies).
        Assert.All(league.History.Skip(1), h => Assert.NotNull(h.Awards!.Roy));
    }
}
