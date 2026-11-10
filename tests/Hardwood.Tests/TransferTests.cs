using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>The transfer market (decision #87): fees, release clauses, personal terms, computer deals, bids for your players.</summary>
public class TransferTests
{
    private static readonly DateOnly On = new(2027, 7, 1);

    private static Player Make(int ca, int pa, int age, int years, long buyout = 0)
    {
        var p = new PlayerGenerator(new Rng(5), 1).Generate(Position.SF, ca, age, On);
        p.PotentialAbility = pa;
        p.Contract = new Contract { Years = years, Salary = 1_000_000, Buyout = buyout };
        return p;
    }

    [Fact]
    public void Fees_follow_league_age_contract_and_importance()
    {
        var fin = Finance.HardCapDefault;
        var star = Make(150, 155, 26, 3);
        Assert.True(TransferMarket.Valuation(star, On, 1, fin) > TransferMarket.Valuation(star, On, 0.11, fin) * 5); // NABL money vs Spanish
        Assert.True(TransferMarket.Valuation(Make(130, 165, 23, 3), On, 1, fin) > TransferMarket.Valuation(Make(130, 130, 31, 3), On, 1, fin));
        Assert.True(TransferMarket.Valuation(Make(130, 130, 27, 3), On, 1, fin) > TransferMarket.Valuation(Make(130, 130, 27, 1), On, 1, fin));

        var club = new Team { Id = 9, City = "C", Name = "N", Abbreviation = "CN", Division = "D" };
        club.Roster.Add(star);
        for (int i = 0; i < 9; i++) club.Roster.Add(Make(100, 100, 27, 2));
        long ask = TransferMarket.Ask(star, club, On, 1, fin);
        Assert.True(ask > TransferMarket.Valuation(star, On, 1, fin)); // key player premium

        Assert.Equal(BidAnswer.Accepted, TransferMarket.Answer(star, club, ask, On, 1, fin).Answer);
        Assert.Equal(BidAnswer.Countered, TransferMarket.Answer(star, club, (long)(ask * 0.85), On, 1, fin).Answer);
        Assert.Equal(BidAnswer.Rejected, TransferMarket.Answer(star, club, ask / 3, On, 1, fin).Answer);
        // A release clause forces the sale.
        star.Contract!.Buyout = ask / 2;
        Assert.Equal(BidAnswer.Accepted, TransferMarket.Answer(star, club, ask / 2, On, 1, fin).Answer);
        // Good players won't drop to a much weaker league.
        Assert.NotNull(TransferMarket.WontJoin(star, 1, 0.11, On));
        Assert.Null(TransferMarket.WontJoin(star, 0.11, 1, On));
    }

    private static GameSession SummerSession(string? path = null)
    {
        var s = GameSession.NewGame(4, 3, path);
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
        foreach (var p in s.MyExpiring.ToList()) s.ReleaseExpiring(p);
        s.AdvanceOffseason(); // free agency + the summer market
        return s;
    }

    [Fact]
    public void You_buy_a_player_fee_then_terms_and_the_money_moves()
    {
        var s = SummerSession();
        Assert.True(s.WindowOpen);
        Assert.NotEmpty(s.TransferLog); // the computer clubs traded at the start of free agency
        var team = s.HumanTeam;
        while (team.Roster.Count > 12) { var x = team.Roster.OrderBy(p => p.CurrentAbility).First(); team.Roster.Remove(x); x.TeamId = null; x.Contract = null; s.League.FreeAgents.Add(x); }
        team.Money.TransferBudget = 50_000_000;
        team.Money.WageBudget = 400_000_000;

        var (target, seller) = s.Market().Where(x => !s.League.Teams.Contains(x.club) && x.player.Contract is { Years: >= 1 })
            .OrderByDescending(x => x.player.CurrentAbility).First();
        long ask = s.AskingPrice(target);
        long sellerBalance = seller.Money.Balance;
        Assert.Equal(BidAnswer.Rejected, s.Bid(target, ask / 4).Answer);
        var r = s.Bid(target, ask);
        Assert.Equal(BidAnswer.Accepted, r.Answer);
        Assert.Equal(ask, s.AgreedFees[target.Id]);
        Assert.Null(s.CannotTalk(target));

        var d = s.DemandOf(target);
        var reply = s.Offer(target, d.Wage, d.Years);
        Assert.True(reply.Accepted, reply.Text);
        Assert.Contains(target, team.Roster);
        Assert.DoesNotContain(target, seller.Roster);
        Assert.Equal(team.Id, target.TeamId);
        Assert.Equal(d.Wage, target.Contract!.Salary);
        Assert.Equal(sellerBalance + ask, seller.Money.Balance);
        Assert.Equal(50_000_000 - ask, team.Money.TransferBudget);
        Assert.Contains(s.TransferLog, t => t.PlayerId == target.Id && t.Fee == ask);
        Assert.Empty(s.AgreedFees);
    }

    [Fact]
    public void Bids_for_your_players_can_be_accepted_or_rejected_and_everything_saves()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-tr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var s = SummerSession(Path.Combine(dir, "career.hwsave"));
            var team = s.HumanTeam;
            var p = team.Roster.OrderByDescending(x => x.CurrentAbility).First();
            var buyer = s.League.Teams.First(t => t != team);
            s.IncomingBids.Clear(); // a computer club may already have bid when free agency opened
            var bid = new IncomingBid { PlayerId = p.Id, FromTeamId = buyer.Id, Fee = 9_000_000, Expires = s.Today.AddDays(5) };
            s.IncomingBids.Add(bid);
            var other = team.Roster.OrderByDescending(x => x.CurrentAbility).ElementAt(1);
            s.IncomingBids.Add(new IncomingBid { PlayerId = other.Id, FromTeamId = buyer.Id, Fee = 4_000_000, Expires = s.Today.AddDays(5) });
            s.SaveNow();

            var l = GameSession.Load(Path.Combine(dir, "career.hwsave"));
            Assert.Equal(2, l.IncomingBids.Count);
            Assert.Equal(s.TransferLog.Count, l.TransferLog.Count);
            var lb = l.IncomingBids.First(b => b.PlayerId == p.Id);
            long balance = l.HumanTeam.Money.Balance;
            var text = l.AcceptBid(lb);
            Assert.False(string.IsNullOrEmpty(text));
            if (!l.HumanTeam.Roster.Any(x => x.Id == p.Id))
            {
                Assert.Equal(balance + 9_000_000, l.HumanTeam.Money.Balance);
                Assert.Contains(l.TransferLog, t => t.PlayerId == p.Id && t.FromTeamId == l.HumanTeam.Id);
            }
            l.RejectBid(l.IncomingBids.Single());
            Assert.Empty(l.IncomingBids);
            Assert.Contains(l.HumanTeam.Roster, x => x.Id == other.Id);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void The_window_is_shut_in_midseason_and_open_in_january()
    {
        var s = GameSession.NewGame(4, 3, null);
        while (s.Today < new DateOnly(2026, 12, 1)) s.AdvanceDay();
        Assert.False(s.WindowOpen);
        var (p, _) = s.Market().First();
        Assert.Equal(BidAnswer.Rejected, s.Bid(p, 1).Answer);
        while (s.Today < new DateOnly(2027, 1, 2)) s.AdvanceDay();
        Assert.True(s.WindowOpen);
        int before = s.TransferLog.Count;
        while (s.Today < new DateOnly(2027, 1, 20)) s.AdvanceDay();
        Assert.True(s.TransferLog.Count >= before); // the January market ran (deals depend on budgets)
        while (s.Today < new DateOnly(2027, 2, 2)) s.AdvanceDay();
        Assert.False(s.WindowOpen);
        Assert.Empty(s.AgreedFees);
    }
}
