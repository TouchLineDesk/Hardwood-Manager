using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

/// <summary>A computer club's bid for one of your players, waiting for your answer.</summary>
public sealed class IncomingBid
{
    public int PlayerId { get; set; }
    public int FromTeamId { get; set; }
    public long Fee { get; set; }
    public DateOnly Expires { get; set; }
}

/// <summary>Where one of your bids stands (Danny, #447: "i cant tell who ive given bids to and for how much").</summary>
public enum MyBidStatus { FeeAgreed, Signed, Countered, Rejected, FellThrough, TermsFailed }

/// <summary>
/// One player you've bid for this window: your latest offer, the club's answer and reaction, and what happened next
/// (the "My bids" list on the Transactions page, D54). One row per player per window; a new bid updates it.
/// </summary>
public sealed class MyBid
{
    public int PlayerId { get; set; }
    public string Player { get; set; } = "";
    public int ClubId { get; set; }
    public string Club { get; set; } = "";
    /// <summary>The window it was made in, e.g. "Summer 2026" or "January 2027".</summary>
    public string Window { get; set; } = "";
    public long Fee { get; set; }
    /// <summary>How many bids you've made for him this window.</summary>
    public int Bids { get; set; } = 1;
    public MyBidStatus Status { get; set; }
    /// <summary>The club's price when they countered (or what they'd want after a rejection, never shown).</summary>
    public long Counter { get; set; }
    /// <summary>The club's reaction in plain words ("Close", "Well short").</summary>
    public string Reaction { get; set; } = "";
    /// <summary>The day the club answered (clubs answer the day you bid).</summary>
    public DateOnly Answered { get; set; }
    /// <summary>His wage and years once he signs.</summary>
    public long Wage { get; set; }
    public int Years { get; set; }
    /// <summary>Why it fell through, when it did.</summary>
    public string Note { get; set; } = "";
    public bool Open => Status is MyBidStatus.FeeAgreed or MyBidStatus.Countered;
}

/// <summary>
/// The transfer market for your club and the computer's (decision #87). Windows: the whole summer and January.
/// You bid for a player under contract; once the fee is agreed you talk personal terms with him (the same contract
/// talks as free agents) and the deal completes when he signs. Computer clubs buy and sell among themselves and bid
/// for your players; you accept or turn those bids down.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Completed transfers this session's career (latest last; the most recent 150 are kept).</summary>
    public List<TransferRecord> TransferLog { get; } = new();
    /// <summary>Bids for your players waiting for an answer.</summary>
    public List<IncomingBid> IncomingBids { get; } = new();
    /// <summary>Players whose fee you've agreed (player id → fee), waiting on personal terms until the window shuts.</summary>
    public Dictionary<int, long> AgreedFees { get; } = new();
    /// <summary>Your bids this window and the last (D54), newest first.</summary>
    public List<MyBid> MyBids { get; } = new();

    /// <summary>The window's name for the My bids list.</summary>
    public string WindowName => Phase == SessionPhase.Offseason ? $"Summer {Today.Year}" : $"January {Today.Year}";

    /// <summary>How long a settled bid stays on My bids after the club's answer (Danny, D54 C: "a week").</summary>
    public const int SettledBidDays = 7;

    /// <summary>The bids to show: this window's while it's open, otherwise the last window's. A settled bid (signed, turned
    /// down, fallen through) drops off a week after the answer (D54 C); a live one (fee agreed, countered) stays.</summary>
    public List<MyBid> BidsToShow() => WindowBids().Where(b => b.Open || Today <= b.Answered.AddDays(SettledBidDays)).ToList();

    /// <summary>Every bid in the window the list is about, including those that have dropped off.</summary>
    public List<MyBid> WindowBids()
    {
        if (MyBids.Count == 0) return new();
        string w = WindowOpen ? WindowName : MyBids[0].Window;
        return MyBids.Where(b => b.Window == w).ToList();
    }

    /// <summary>The player a bid was for, wherever he is now (his old club, your roster, or elsewhere).</summary>
    public Player? BidPlayer(MyBid b) =>
        FindTeam(b.ClubId)?.Roster.FirstOrDefault(x => x.Id == b.PlayerId) ?? HumanTeam.Roster.FirstOrDefault(x => x.Id == b.PlayerId)
        ?? Market().Select(x => x.player).FirstOrDefault(x => x.Id == b.PlayerId);

    /// <summary>The club's reaction to a bid, in plain words, from the same price their answer used.</summary>
    public static string Reaction(BidReply reply, long fee, StanceInfo stance)
    {
        double r = reply.Fee <= 0 ? 1 : (double)fee / reply.Fee;
        return reply.Answer switch
        {
            BidAnswer.Accepted when reply.Text.Contains("release clause") => "Release clause: can't refuse",
            BidAnswer.Accepted => "They'd take it",
            BidAnswer.Countered => r >= 0.92 ? "Close: they want a little more" : "Short, but they'll talk",
            _ when stance.Stance == TransferStance.WontSell && r >= 0.75 => "No sale without a premium",
            _ => r < 0.5 ? "Nowhere near: they're insulted" : "Well short",
        };
    }

    private void RecordBid(Player p, Team seller, long fee, BidReply reply)
    {
        var stance = TransferMarket.Stance(p, seller);
        string window = WindowName;
        var row = MyBids.FirstOrDefault(b => b.PlayerId == p.Id && b.Window == window);
        if (row == null) { row = new MyBid { PlayerId = p.Id, Window = window, Bids = 0 }; MyBids.Insert(0, row); }
        else { MyBids.Remove(row); MyBids.Insert(0, row); }
        row.Player = p.Name; row.ClubId = seller.Id; row.Club = seller.FullName; row.Fee = fee; row.Bids++;
        row.Status = reply.Answer switch { BidAnswer.Accepted => MyBidStatus.FeeAgreed, BidAnswer.Countered => MyBidStatus.Countered, _ => MyBidStatus.Rejected };
        row.Counter = reply.Answer == BidAnswer.Countered ? reply.Fee : 0;
        row.Reaction = Reaction(reply, fee, stance);
        row.Answered = Today;
        row.Note = "";
        // Keep this window and the one before.
        var keep = MyBids.Select(b => b.Window).Distinct().Take(2).ToHashSet();
        MyBids.RemoveAll(b => !keep.Contains(b.Window));
    }

    /// <summary>The window shut: fees agreed without terms fall through (the rows stay to read until the next window).</summary>
    private void BidsWindowShut()
    {
        foreach (var b in MyBids.Where(b => b.Open))
        {
            if (b.Status == MyBidStatus.FeeAgreed) { b.Status = MyBidStatus.FellThrough; b.Note = "The window shut before he agreed terms."; }
            else { b.Status = MyBidStatus.FellThrough; b.Note = "The window shut before you bid again."; }
        }
    }

    /// <summary>A club's wage level: 1 in the NABL, its league's scale abroad.</summary>
    public double ScaleOf(Team t) =>
        League.Teams.Contains(t) ? 1 : World.Competitions.FirstOrDefault(c => c.Clubs.Contains(t))?.Profile.SalaryScale ?? 0.12;

    /// <summary>The summer (the whole offseason) and January.</summary>
    public bool WindowOpen => Phase == SessionPhase.Offseason || Today.Month == 1;
    public string WindowText => Phase == SessionPhase.Offseason ? "The summer transfer window is open."
        : Today.Month == 1 ? $"The January window is open until Jan 31."
        : $"The transfer window is shut. It reopens on Jan 1{(Today.Month >= 2 && Today.Month <= 6 ? " next season (and all summer)" : "")}.";

    /// <summary>Every player under contract at another club (the transfer list).</summary>
    public IEnumerable<(Player player, Team club)> Market() =>
        League.Teams.Concat(World.Competitions.Where(c => c.Profile.Kind != CompetitionKind.Continental).SelectMany(c => c.Clubs))
            .Where(t => t != HumanTeam).SelectMany(t => t.Roster.Select(p => (p, t)));

    /// <summary>The fee a club would want for its player (what your scouts expect).</summary>
    public long AskingPrice(Player p) =>
        FindTeam(p.TeamId ?? 0) is { } club ? TransferMarket.Ask(p, club, Today, ScaleOf(club), League.Finance) : 0;

    /// <summary>The selling club's stance on one of its players (roadmap 2b).</summary>
    public StanceInfo? StanceOn(Player p) => FindTeam(p.TeamId ?? 0) is { } club && club != HumanTeam ? TransferMarket.Stance(p, club) : null;

    /// <summary>The fee range your scouts expect, narrower with a better head scout (roadmap 2b).</summary>
    public FeeRange LikelyFee(Player p) => TransferMarket.LikelyFee(p, AskingPrice(p), Scouting.Knowledge(HumanTeam, p));

    /// <summary>
    /// Why he wouldn't come to your club, before you spend a bid on him: a good player won't drop to a weaker league,
    /// and one far above your league aims higher. Null when he'd talk to you.
    /// </summary>
    public string? WontCome(Player p) =>
        FindTeam(p.TeamId ?? 0) is { } seller && seller != HumanTeam
            ? TransferMarket.WontJoin(p, ScaleOf(seller), MySalaryScale, Today) ?? AimsHigher(p) : null;

    /// <summary>Bid for a player under contract. On acceptance, talk to him about personal terms.</summary>
    public BidReply Bid(Player p, long fee)
    {
        if (!Employed) return new(BidAnswer.Rejected, 0, "You aren't coaching a club right now.");
        if (InCollege) return new(BidAnswer.Rejected, 0, "A college program doesn't sign professionals: your roster comes from high-school recruiting.");
        if (!WindowOpen) return new(BidAnswer.Rejected, 0, WindowText);
        if (FindTeam(p.TeamId ?? 0) is not { } seller || seller == HumanTeam) return new(BidAnswer.Rejected, 0, "He isn't at another club.");
        if (HumanTeam.Money.Embargo)
            return new(BidAnswer.Rejected, 0, "The club is under a transfer embargo: the debt is more than half a season's revenue. Free agents only, within the wage budget, until it recovers.");
        if (fee > HumanTeam.Money.TransferBudget)
            return new(BidAnswer.Rejected, 0, $"The owner won't fund it: your transfer budget is ${HumanTeam.Money.TransferBudget / 1e6:F2}M.");
        if (WontCome(p) is { } why) return new(BidAnswer.Rejected, 0, why);
        var reply = TransferMarket.Answer(p, seller, fee, Today, ScaleOf(seller), League.Finance);
        RecordBid(p, seller, fee, reply);
        if (reply.Answer == BidAnswer.Accepted)
        {
            AgreedFees[p.Id] = fee;
            _talks.Remove(p.Id);
            reply = reply with { Text = reply.Text + $" Now agree personal terms with {p.Name}." };
            Post($"Fee agreed: {p.Name}", reply.Text + " His row is on the Transactions page, under My bids.", "Transactions");
        }
        SaveNow();
        return reply;
    }

    /// <summary>Accept a bid for one of your players: he talks to them and, if he agrees, leaves.</summary>
    public string AcceptBid(IncomingBid bid)
    {
        IncomingBids.Remove(bid);
        var p = HumanTeam.Roster.FirstOrDefault(x => x.Id == bid.PlayerId);
        var buyer = FindTeam(bid.FromTeamId);
        if (p == null || buyer == null) { SaveNow(); return "That bid is no longer on the table."; }
        if (_rng.Chance(0.15))
        {
            Post($"{p.Name} stays", $"{p.Name} couldn't agree terms with {buyer.FullName}. He stays with you.", "Transactions");
            SaveNow();
            return $"{p.Name} couldn't agree terms with {buyer.FullName} and stays.";
        }
        var d = ContractTalks.Demand(p, buyer, Today, ScaleOf(buyer), League.Finance, renewal: false);
        CompleteTransfer(p, HumanTeam, buyer, bid.Fee, d.Wage, d.Years);
        SaveNow();
        return $"{p.Name} joins {buyer.FullName} for ${bid.Fee / 1e6:F2}M.";
    }

    public void RejectBid(IncomingBid bid)
    {
        IncomingBids.Remove(bid);
        if (FindTeam(bid.FromTeamId) is { } t && HumanTeam.Roster.FirstOrDefault(x => x.Id == bid.PlayerId) is { } p)
            Post($"Bid rejected: {p.Name}", $"You turned down {t.FullName}'s ${bid.Fee / 1e6:F2}M for {p.Name}.", "Transactions");
        SaveNow();
    }

    /// <summary>Move a player between clubs: fee paid, new contract, rosters updated, news posted.</summary>
    private void CompleteTransfer(Player p, Team from, Team to, long fee, long wage, int years)
    {
        from.Roster.Remove(p);
        p.Contract = League.Teams.Contains(to) ? new Contract { Years = years, Salary = wage, StartSeason = ContractStart }
                                                 : GameWorld.ContractAt(p, wage, years, Today, ContractStart);
        p.TeamId = to.Id;
        p.DraftRightsTeamId = null;
        to.Roster.Add(p);
        from.DepthChart = null; to.DepthChart = null;
        People.PeopleEngine.Joined(p);
        to.Money.TransferBudget = Math.Max(0, to.Money.TransferBudget - fee);
        to.Money.Balance -= fee;
        from.Money.Balance += fee;
        from.Money.TransferBudget += fee / 2;
        AgreedFees.Remove(p.Id);
        IncomingBids.RemoveAll(b => b.PlayerId == p.Id);
        foreach (var b in MyBids.Where(b => b.PlayerId == p.Id && b.Status is not (MyBidStatus.Signed or MyBidStatus.FellThrough or MyBidStatus.TermsFailed)))
        {
            if (to == HumanTeam) { b.Status = MyBidStatus.Signed; b.Wage = wage; b.Years = years; b.Fee = fee; b.Note = ""; }
            else { b.Status = MyBidStatus.FellThrough; b.Note = $"He joined {to.FullName}."; }
        }
        _talks.Remove(p.Id);
        TransferLog.Add(new TransferRecord(Today, p.Id, p.Name, from.Id, from.FullName, to.Id, to.FullName, fee));
        if (TransferLog.Count > 150) TransferLog.RemoveAt(0);
        bool ours = from == HumanTeam || to == HumanTeam;
        bool big = fee >= (League.Teams.Contains(to) ? 15_000_000 : 1_500_000);
        if (ours || big || HumanLeague?.Clubs.Contains(to) == true || HumanLeague?.Clubs.Contains(from) == true)
            Post($"Transfer: {p.Name} to {to.Abbreviation}", $"{p.Name} ({p.PrimaryPosition}, {p.Age(Today)}) moves from {from.FullName} to {to.FullName} for ${fee / 1e6:F2}M, "
                + $"on ${wage / 1e6:F2}M a year for {years} year{(years == 1 ? "" : "s")}.", ours ? "Roster" : InCollege ? "League" : "Transactions");
    }

    /// <summary>
    /// Computer clubs buy from clubs in the same or a smaller league: a player who'd be in their top five, they can
    /// afford the fee and his wage, and he's willing. Limited to a handful of deals a window so the market moves
    /// without churning every roster.
    /// </summary>
    private void AiTransferRound(int maxDeals)
    {
        var clubs = League.Teams.Concat(World.Competitions.Where(c => c.Profile.Kind != CompetitionKind.Continental).SelectMany(c => c.Clubs))
            .Where(t => t.Id != ControlledTeamId && t.Money.Ready).ToList();
        var pool = clubs.SelectMany(t => t.Roster.Where(p => p.Contract is { Years: >= 1 } && p.Age(Today) <= 31).Select(p => (p, t))).ToList();
        int deals = 0;
        foreach (var buyer in clubs.OrderBy(_ => _rng.NextDouble()))
        {
            if (deals >= maxDeals) break;
            bool nabl = League.Teams.Contains(buyer);
            int max = nabl ? League.Finance.RosterMax : GameWorld.RosterSize + 2;
            if (buyer.Roster.Count >= max || buyer.Money.TransferBudget < 100_000 || buyer.Roster.Count < 5) continue;
            double scale = ScaleOf(buyer);
            var sorted = buyer.Roster.Select(p => p.CurrentAbility).OrderByDescending(x => x).ToList();
            int fifth = sorted[Math.Min(4, sorted.Count - 1)], best = sorted[0];
            var pick = pool.Where(x => x.t != buyer && x.t.Roster.Contains(x.p) && ScaleOf(x.t) <= scale + 1e-9
                                       && x.p.CurrentAbility >= fifth + 4 && x.p.CurrentAbility <= best + 6
                                       && x.t.Roster.Count > (League.Teams.Contains(x.t) ? League.Finance.RosterMin : GameWorld.RosterSize - 2))
                .OrderByDescending(x => x.p.CurrentAbility + _rng.NextDouble() * 8).Take(6)
                .Select(x => (x.p, x.t, fee: TransferMarket.Ask(x.p, x.t, Today, ScaleOf(x.t), League.Finance),
                              wage: ContractTalks.Demand(x.p, buyer, Today, scale, League.Finance, false)))
                .FirstOrDefault(x => x.fee <= buyer.Money.TransferBudget && x.wage.Wage <= Budgets.Room(buyer)
                                     && TransferMarket.WontJoin(x.p, ScaleOf(x.t), scale, Today) == null);
            if (pick.p == null) continue;
            if (pick.t == HumanTeam) continue; // bids for your players go through IncomingBids
            CompleteTransfer(pick.p, pick.t, buyer, pick.fee, pick.wage.Wage, pick.wage.Years);
            deals++;
        }
    }

    /// <summary>
    /// Each day of a window: bids for your players now and then; the computer's January deals; expired bids lapse.
    /// When a window shuts, agreed fees without personal terms fall through.
    /// </summary>
    private void DailyTransfers()
    {
        IncomingBids.RemoveAll(b => b.Expires < Today);
        if (!WindowOpen)
        {
            if (AgreedFees.Count > 0 || IncomingBids.Count > 0)
            {
                if (AgreedFees.Count > 0) Post("Transfer window shut", "Deals without agreed personal terms have fallen through.", "Transactions");
                AgreedFees.Clear(); IncomingBids.Clear();
            }
            if (MyBids.Any(b => b.Open)) BidsWindowShut();
            return;
        }
        if (Today.Month == 1 && Today.Day == 15) AiTransferRound(6);
        if (Employed && !InCollege && _rng.Chance(0.06)) MaybeBidForYourPlayer();
    }

    /// <summary>A computer club from the same or a bigger league bids for one of your better players.</summary>
    private void MaybeBidForYourPlayer()
    {
        var mine = HumanTeam.Roster.Where(p => p.Contract is { Years: >= 1 } && !IncomingBids.Any(b => b.PlayerId == p.Id))
            .OrderByDescending(p => ContractMarket.Value(p, Today)).Take(8).ToList();
        if (mine.Count == 0) return;
        var p = mine[_rng.Next(mine.Count)];
        double myScale = MySalaryScale;
        long value = TransferMarket.Valuation(p, Today, myScale, League.Finance);
        var bidders = League.Teams.Concat(World.Competitions.Where(c => c.Profile.Kind != CompetitionKind.Continental).SelectMany(c => c.Clubs))
            .Where(t => t != HumanTeam && t.Money.Ready && ScaleOf(t) >= myScale - 1e-9 && t.Money.TransferBudget >= value * 0.8)
            .Where(t => t.Roster.Select(x => x.CurrentAbility).OrderByDescending(x => x).Skip(4).FirstOrDefault() <= p.CurrentAbility)
            .ToList();
        if (bidders.Count == 0) return;
        var buyer = bidders[_rng.Next(bidders.Count)];
        long fee = Math.Min(buyer.Money.TransferBudget, (long)(value * (0.85 + _rng.NextDouble() * 0.4)) / 10_000 * 10_000);
        var bid = new IncomingBid { PlayerId = p.Id, FromTeamId = buyer.Id, Fee = fee, Expires = Today.AddDays(5) };
        IncomingBids.Add(bid);
        Post($"Bid for {p.Name}", $"{buyer.FullName} ({ContextOf(buyer).Name}) have bid ${fee / 1e6:F2}M for {p.Name}. "
            + $"Your scouts value him at about ${value / 1e6:F2}M. Accept or reject on the Transactions page by {bid.Expires:MMM d}.", "Transactions");
    }
}
