using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// The My bids list (Danny, #447 / D54): every player you've bid for this window, your offer, the club's answer and
/// reaction, and what happened after (signed, or fell through), and it all saves.
/// </summary>
public class MyBidsTests
{
    private static GameSession SummerSession(string? path = null)
    {
        var s = GameSession.NewGame(4, 3, path);
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
        foreach (var p in s.MyExpiring.ToList()) s.ReleaseExpiring(p);
        s.AdvanceOffseason();
        var team = s.HumanTeam;
        while (team.Roster.Count > 12) { var x = team.Roster.OrderBy(p => p.CurrentAbility).First(); team.Roster.Remove(x); x.TeamId = null; x.Contract = null; s.League.FreeAgents.Add(x); }
        team.Money.TransferBudget = 80_000_000;
        team.Money.WageBudget = 400_000_000;
        return s;
    }

    [Fact]
    public void Every_bid_has_a_row_with_its_status_reaction_and_outcome()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-mybids-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "career.hwsave");
            var s = SummerSession(path);
            var targets = s.Market().Where(x => !s.League.Teams.Contains(x.club) && x.player.Contract is { Years: >= 1 } && s.WontCome(x.player) == null && s.AskingPrice(x.player) <= 20_000_000)
                .OrderByDescending(x => x.player.CurrentAbility).Take(3).ToList();
            Assert.True(targets.Count == 3, $"{s.Market().Count()} on the market, {s.Market().Count(x => s.WontCome(x.player) == null)} would come");
            var (a, _) = targets[0]; var (b, _) = targets[1]; var (c, clubC) = targets[2];

            // A lowball is rejected, a near miss is countered with their price, the full ask is accepted.
            long askA = s.AskingPrice(a);
            Assert.Equal(BidAnswer.Rejected, s.Bid(a, askA / 4).Answer);
            long askB = s.AskingPrice(b);
            var rb = s.Bid(b, (long)(askB * 0.95) / 10_000 * 10_000);
            long askC = s.AskingPrice(c);
            Assert.Equal(BidAnswer.Accepted, s.Bid(c, askC).Answer);

            var shown = s.BidsToShow();
            Assert.Equal(3, shown.Count);
            Assert.Equal(c.Id, shown[0].PlayerId); // newest first
            var rowA = shown.Single(x => x.PlayerId == a.Id);
            Assert.Equal(MyBidStatus.Rejected, rowA.Status);
            if (a.Contract!.Buyout == 0) Assert.Equal("Nowhere near: they're insulted", rowA.Reaction);
            var rowB = shown.Single(x => x.PlayerId == b.Id);
            if (rb.Answer == BidAnswer.Countered)
            {
                Assert.Equal(MyBidStatus.Countered, rowB.Status);
                Assert.Equal(rb.Fee, rowB.Counter);
                Assert.StartsWith("Close", rowB.Reaction);
            }
            var rowC = shown.Single(x => x.PlayerId == c.Id);
            Assert.Equal(MyBidStatus.FeeAgreed, rowC.Status);
            Assert.Equal(clubC.FullName, rowC.Club);
            Assert.Equal(askC, rowC.Fee);
            Assert.Equal(s.Today, rowC.Answered);

            // A second bid for the same player updates his row rather than adding one.
            Assert.Equal(BidAnswer.Accepted, s.Bid(a, askA).Answer);
            Assert.Equal(3, s.BidsToShow().Count);
            rowA = s.BidsToShow().Single(x => x.PlayerId == a.Id);
            Assert.Equal(2, rowA.Bids);
            Assert.Equal(MyBidStatus.FeeAgreed, rowA.Status);
            Assert.Equal(s.BidsToShow()[0], rowA);

            // The rows survive a save and load.
            s.SaveNow();
            var l = GameSession.Load(path);
            Assert.Equal(s.BidsToShow().Select(x => (x.PlayerId, x.Status, x.Fee, x.Reaction)), l.BidsToShow().Select(x => (x.PlayerId, x.Status, x.Fee, x.Reaction)));

            // Agreeing his terms turns the row to Signed, with his wage.
            var pc = l.Market().First(x => x.player.Id == c.Id).player;
            var d = l.DemandOf(pc);
            Assert.True(l.Offer(pc, d.Wage, d.Years).Accepted);
            var signed = l.BidsToShow().Single(x => x.PlayerId == c.Id);
            Assert.Equal(MyBidStatus.Signed, signed.Status);
            Assert.Equal(d.Wage, signed.Wage);

            // When the window shuts, a fee agreed without terms falls through, and the rows stay to read.
            int guard = 0;
            while (l.WindowOpen && guard++ < 400) { if (l.Phase == SessionPhase.Offseason) l.AdvanceOffseason(); else l.AdvanceDay(); }
            Assert.False(l.WindowOpen);
            var after = l.BidsToShow();
            Assert.Equal(3, after.Count);
            Assert.Equal(MyBidStatus.FellThrough, after.Single(x => x.PlayerId == a.Id).Status);
            Assert.Equal(MyBidStatus.Signed, after.Single(x => x.PlayerId == c.Id).Status);
            Assert.DoesNotContain(after, x => x.Open);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_bid_refused_before_it_reaches_the_club_leaves_no_row()
    {
        var s = GameSession.NewGame(4, 3, null);
        while (s.Today < new DateOnly(2026, 12, 1)) s.AdvanceDay();
        var (p, _) = s.Market().First();
        Assert.Equal(BidAnswer.Rejected, s.Bid(p, 1).Answer); // the window is shut
        Assert.Empty(s.MyBids);
        Assert.Empty(s.BidsToShow());
    }

    [Fact]
    public void Older_saves_without_the_list_load_with_none()
    {
        // The transfers record from before v0.71.0 has three fields; the fourth defaults to null.
        var json = "{\"Log\":[],\"Incoming\":[],\"Agreed\":{}}";
        var t = System.Text.Json.JsonSerializer.Deserialize<TransferProbe>(json);
        Assert.NotNull(t);
        Assert.Null(t!.Mine);
    }

    private sealed record TransferProbe(List<object>? Log, List<object>? Incoming, Dictionary<int, long>? Agreed, List<MyBid>? Mine = null);
}
