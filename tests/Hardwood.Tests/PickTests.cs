using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

public class PickTests
{
    private static League NewLeague(ulong seed = 31) =>
        new WorldGenerator(new Rng(seed)).Generate("L", 2026, new DateOnly(2026, 10, 1));

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
    public void Mid_offseason_save_and_reload_resumes_the_draft()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-off-{Guid.NewGuid():N}.save");
        try
        {
            var s = GameSession.NewGame(12, humanTeamId: 7, path);
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);

            // Reload during re-signing.
            var r1 = GameSession.Load(path);
            Assert.Equal(SessionPhase.Offseason, r1.Phase);
            Assert.Equal(OffseasonPhase.ReSigning, r1.Offseason!.Phase);
            Assert.Equal(s.Offseason!.Expiring[7].Count, r1.Offseason.Expiring[7].Count);
            Assert.Equal(s.Offseason.Prospects.Count, r1.Offseason.Prospects.Count);
            Assert.Equal(s.Offseason.DraftOrder.Select(t => t.Id), r1.Offseason.DraftOrder.Select(t => t.Id));

            foreach (var p in r1.Offseason.Expiring[7].ToList()) r1.Offseason.Release(r1.HumanTeam, p);
            r1.AdvanceOffseason(); // to draft, AI picks until we're up
            Assert.Equal(OffseasonPhase.Draft, r1.Offseason.Phase);
            int picksBefore = r1.Offseason.Report.Draft.Count;
            Assert.Equal(r1.HumanTeam, r1.Offseason.OnTheClock);

            // Reload during the draft: we are still on the clock with the same board.
            var r2 = GameSession.Load(path);
            Assert.Equal(OffseasonPhase.Draft, r2.Offseason!.Phase);
            Assert.Equal(picksBefore, r2.Offseason.Report.Draft.Count);
            Assert.Equal(r1.Offseason.NextPickIndex, r2.Offseason.NextPickIndex);
            Assert.Equal(r2.HumanTeam, r2.Offseason.OnTheClock);
            Assert.Equal(r1.Offseason.Prospects.Count, r2.Offseason.Prospects.Count);

            r2.MakePick(r2.Offseason.Prospects.First());
            while (r2.Offseason.Phase == OffseasonPhase.Draft && !r2.Offseason.DraftComplete) r2.MakePick(r2.Offseason.Prospects.First());
            Assert.Equal(OffseasonPhase.FreeAgency, r2.Offseason.Phase);

            // Reload in free agency, finish, and the new season starts.
            var r3 = GameSession.Load(path);
            Assert.Equal(OffseasonPhase.FreeAgency, r3.Offseason!.Phase);
            r3.AdvanceOffseason();
            Assert.Equal(SessionPhase.Preseason, r3.Phase);
            Assert.Equal(2027, r3.League.Season);
            Assert.InRange(r3.HumanTeam.Roster.Count, 13, 15);
            Assert.Equal(30 * 2 * League.PickYearsAhead, r3.League.Picks.Count);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
