using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Avalonia.Media;
using Hardwood.Desktop.Controls;

namespace Hardwood.Desktop.ViewModels;

/// <summary>A player on the transfer market: his club, what your scouts think he's worth, and his release clause.</summary>
public sealed partial class MarketRow : ObservableObject
{
    public required Player Player { get; init; }
    public required Team Club { get; init; }
    public string Name => Player.Name;
    public string ClubName => Club.FullName;
    public string League { get; init; } = "";
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public double Knowledge { get; init; } = 1;
    public string CA => Scouting.Ability(Player, Knowledge).ToString();
    public string PA => Scouting.Potential(Player, Knowledge).ToString();
    public string Contract => Player.Contract is { } c ? $"${c.Salary / 1e6:F2}M × {Math.Max(1, c.Years)}" : "-";
    public long AskValue { get; init; }
    /// <summary>What your scouts expect the fee to be (roadmap 2b): a range, not the club's exact price.</summary>
    public FeeRange Likely { get; init; }
    public string Ask => Likely.Short;
    public StanceInfo? StanceInfo { get; init; }
    public string Stance => StanceInfo?.Label ?? "";
    /// <summary>Null when he'd talk to you; otherwise why he won't.</summary>
    public string? WontCome { get; init; }
    public string Interest => WontCome == null ? "Yes" : WontCome.Contains("weaker league") ? "Weaker league" : "Aims higher";
    public string Clause => Player.Contract is { Buyout: > 0 } c ? $"${c.Buyout / 1e6:F2}M" : "-";
    public bool Agreed { get; init; }
    public string Status => Agreed ? "Fee agreed" : "";

    // Run 65 (review #13 rank 4): the market as player cards, with the club's stance and his answer as coloured chips.
    /// <summary>The card picked (its border lights up), as the selected row is in the table.</summary>
    [ObservableProperty] private bool _picked;
    /// <summary>Selling stance: red when they won't sell, green when they want him gone, blue when they'd listen.</summary>
    public IBrush StanceBrush => StanceInfo?.Stance switch
    {
        TransferStance.WontSell => StandingRow.RelegationBrush,
        TransferStance.WantsHimGone => StandingRow.PlayoffBrush,
        _ => ListenBrush,
    };
    public string StanceTip => StanceInfo?.Why ?? "";
    public bool HasStance => StanceInfo != null;
    /// <summary>Would he come: green yes, red with the reason's short form.</summary>
    public string InterestChip => WontCome == null ? "Would come" : Interest;
    public IBrush InterestBrush => WontCome == null ? StandingRow.PlayoffBrush : StandingRow.RelegationBrush;
    public string InterestTip => WontCome ?? "He'd talk terms if the clubs agree a fee.";
    public bool HasClause => Player.Contract is { Buyout: > 0 };
    public string ClauseChip => HasClause ? $"Clause {Clause}" : "";
    /// <summary>"PG · age 24 · PA 160": the line under his name on a card.</summary>
    public string CardLine => $"{Pos} · age {Age} · PA {PA}";
    public string ContractLine => Player.Contract != null ? Contract : "";
    public string FeeLine => $"Likely fee {Ask}";
    public Team? Team => Club;
    private static readonly IBrush ListenBrush = new SolidColorBrush(Color.Parse("#2F6FB5"));
}

/// <summary>A bid for one of your players.</summary>
public sealed class IncomingRow
{
    public required IncomingBid Bid { get; init; }
    public string Player { get; init; } = "";
    public string From { get; init; } = "";
    public string Fee => $"${Bid.Fee / 1e6:F2}M";
    public string Value { get; init; } = "";
    public string Expires => Bid.Expires.ToString("MMM d");
    public required IRelayCommand Accept { get; init; }
    public required IRelayCommand Reject { get; init; }
}

/// <summary>One of your bids on the My bids list (D54): who, how much, where it stands and how the club took it.</summary>
public sealed class MyBidRow
{
    public required MyBid Bid { get; init; }
    public string Player { get; init; } = "";
    public string Club => Bid.Club;
    public string Offer => $"${Bid.Fee / 1e6:F2}M";
    /// <summary>Which bid this was and the day you made it.</summary>
    public string OfferNote => (Bid.Bids > 1 ? $"bid {Bid.Bids} · " : "") + Bid.Answered.ToString("MMM d");
    public string Wages => Bid.Status == MyBidStatus.Signed ? $"${Bid.Wage / 1e6:F2}M × {Math.Max(1, Bid.Years)}" : "-";
    public string Status => Bid.Status switch
    {
        MyBidStatus.FeeAgreed => "Accepted: talk terms",
        MyBidStatus.Signed => "Signed",
        MyBidStatus.Countered => $"Countered: ${Bid.Counter / 1e6:F2}M",
        MyBidStatus.Rejected => "Rejected",
        MyBidStatus.TermsFailed => "Fell through: his terms",
        _ => "Fell through",
    };
    /// <summary>The status chip's colour: green when it's done or agreed, amber for a counter, red for a no.</summary>
    public Avalonia.Media.IBrush StatusBrush => Bid.Status switch
    {
        MyBidStatus.FeeAgreed or MyBidStatus.Signed => StandingRow.PlayoffBrush,
        MyBidStatus.Countered => StandingRow.PlayInBrush,
        MyBidStatus.Rejected => StandingRow.RelegationBrush,
        _ => Gray,
    };
    private static readonly Avalonia.Media.IBrush Gray = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6B7280"));
    public string Reaction => Bid.Reaction;
    /// <summary>When they answered and what's next, or why it fell through.</summary>
    public string When { get; init; } = "";
    /// <summary>The button shows while the window is open and the deal is still alive.</summary>
    public bool CanAct { get; init; }
    public string ActText => Bid.Status == MyBidStatus.FeeAgreed ? "Terms" : "Bid again";
    public required IRelayCommand Act { get; init; }
}

/// <summary>The transfer market on the Transactions page (decision #87): search, bid, agree terms; answer bids for your players.</summary>
public sealed partial class TransactionsViewModel
{
    public ObservableCollection<MarketRow> Market { get; } = new();
    [ObservableProperty] private MarketRow? _selectedMarket;
    /// <summary>The league filter: the top league, then every league abroad by its current name (a name pack can rename them).</summary>
    public List<string> MarketLeagues { get; } = new[] { "All leagues", Hardwood.Core.Names.Brand.Top }
        .Concat(Hardwood.Core.World.Competitions.All.Where(c => c.Kind != Hardwood.Core.World.CompetitionKind.Continental).Select(c => c.Name)).Distinct().ToList();
    public List<string> Positions { get; } = new() { "All", "PG", "SG", "SF", "PF", "C" };
    [ObservableProperty] private string _marketLeague = "All leagues";
    [ObservableProperty] private string _marketPosition = "All";
    [ObservableProperty] private decimal? _minAbility = 110;
    [ObservableProperty] private decimal? _bidFee;
    [ObservableProperty] private string _dealText = "Select a player to see what his club wants.";
    /// <summary>H68 (run 100): the bid panel says who the bid is for: "Make a bid · Jayson Tatum, Boston".</summary>
    [ObservableProperty] private string _bidTitle = "Make a bid";
    /// <summary>The fee box and Bid work only with a player picked and the window open (H68: a shut window showed a live fee).</summary>
    [ObservableProperty] private bool _canBid;
    [ObservableProperty] private string _bidTip = "Pick a player in the market first";
    /// <summary>The selected player's deal at a glance: likely fee, the club's stance, whether he'd come, the clause, your budget.</summary>
    public ObservableCollection<Chip> DealChips { get; } = new();
    [ObservableProperty] private string _dealReply = "";
    [ObservableProperty] private string _windowText = "";
    [ObservableProperty] private string _transferBudgetText = "";
    public ObservableCollection<IncomingRow> Incoming { get; } = new();
    /// <summary>Your bids this window (D54).</summary>
    public ObservableCollection<MyBidRow> MyBids { get; } = new();
    [ObservableProperty] private string _myBidsText = "";
    public bool HasMyBids => MyBids.Count > 0;
    /// <summary>The selected tab (0 market, 1 my bids, 2 bids for your players, 3 free agents...).</summary>
    [ObservableProperty] private int _tab;
    public ObservableCollection<string> RecentTransfers { get; } = new();
    public bool HasIncoming => Incoming.Count > 0;

    partial void OnMarketLeagueChanged(string value) => FillMarket();
    partial void OnMarketPositionChanged(string value) => FillMarket();
    partial void OnMinAbilityChanged(decimal? value) => FillMarket();
    partial void OnSelectedMarketChanged(MarketRow? oldValue, MarketRow? newValue)
    {
        if (oldValue != null) oldValue.Picked = false;
        if (newValue != null) newValue.Picked = true;
        ShowDeal(newValue);
    }

    // Run 65 (review #13 rank 4): the market opens on player cards, the best first; the table is one click away.
    public string[] MarketViews { get; } = { "Cards", "Full table" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowMarketCards), nameof(ShowMarketTable))] private string _marketView = "Cards";
    public bool ShowMarketCards => MarketView == "Cards";
    public bool ShowMarketTable => !ShowMarketCards;
    /// <summary>How many cards the card view shows; the full table lists everyone the filters let through.</summary>
    public const int CardCount = 24;
    /// <summary>The most the table lists (the best by ability).</summary>
    public const int MarketMax = 300;
    public ObservableCollection<MarketRow> MarketCards { get; } = new();
    [ObservableProperty] private string _marketCardsNote = "";
    /// <summary>The position filter as chips, each with how many players it holds.</summary>
    public ObservableCollection<FilterChip> PositionChips { get; } = new();

    [RelayCommand]
    public void SetMarketPosition(string? pos) { if (pos != null && Positions.Contains(pos)) MarketPosition = pos; }

    [RelayCommand]
    public void PickMarket(MarketRow? row) { if (row != null) SelectedMarket = row; }

    private void ShowDeal(MarketRow? row)
    {
        DealReply = "";
        DealChips.Clear();
        var s = Session;
        UpdateBidState(row);
        if (row == null) { DealText = "Select a player to see what his club wants."; return; }
        var p = row.Player;
        if (s.AgreedFees.ContainsKey(p.Id))
        {
            DealText = DealTextFor(row);
            Talk.Show(p);
            return;
        }
        long budget = s.HumanTeam.Money.TransferBudget;
        var likely = row.Likely;
        var stance = row.StanceInfo;
        BidFee = Math.Round((decimal)(likely.Mid / 1e6), 2);
        DealChips.Add(new Chip("Likely fee", likely.Short, "your scouts' estimate", likely.Low > budget ? "bad" : likely.High > budget ? "warn" : "info"));
        if (stance != null)
            DealChips.Add(new Chip("Club stance", stance.Label, "", stance.Stance switch { TransferStance.WontSell => "bad", TransferStance.WantsHimGone => "good", _ => "info" }));
        DealChips.Add(new Chip("Will he come?", row.WontCome == null ? "Yes" : "No", row.WontCome == null ? "he'll talk terms" : row.Interest.ToLowerInvariant(), row.WontCome == null ? "good" : "bad"));
        if (p.Contract is { Buyout: > 0 } c) DealChips.Add(new Chip("Release clause", $"${c.Buyout / 1e6:F2}M", "can't be refused", "info"));
        DealChips.Add(new Chip("Your budget", $"${budget / 1e6:F2}M", "transfer budget", budget >= likely.High ? "good" : budget >= likely.Low ? "warn" : "bad"));
        DealText = DealTextFor(row);
        Talk.Show(null);
    }

    /// <summary>The deal's text for today: rebuilt on every refresh too, so a window that opens or shuts while he's
    /// picked doesn't leave "the window is shut" beside a live Bid (code review, PR #162).</summary>
    private string DealTextFor(MarketRow row)
    {
        var s = Session;
        var p = row.Player;
        if (s.AgreedFees.TryGetValue(p.Id, out var fee))
            return $"Fee agreed with {row.ClubName}: ${fee / 1e6:F2}M. Agree personal terms with {p.Name} below.";
        return (s.WindowOpen ? "" : $"{s.WindowText} You can scout {p.Name} now and bid once it opens. ")
            + (row.WontCome is { } why ? why + " A bid would be wasted."
            : $"{row.ClubName}: {row.StanceInfo?.Why} Your scouts expect them to want {row.Likely.Text}; a bid at the top of that range should be accepted."
              + (p.Contract is { Buyout: > 0 } cl ? $" His release clause (${cl.Buyout / 1e6:F2}M) can't be refused." : ""));
    }

    /// <summary>The panel's title and whether Bid works, for the picked player on today's date.</summary>
    private void UpdateBidState(MarketRow? row)
    {
        var s = Session;
        BidTitle = BidTitleFor(row);
        CanBid = row != null && s.WindowOpen && !s.AgreedFees.ContainsKey(row.Player.Id);
        BidTip = row == null ? "Pick a player in the market first"
            : !s.WindowOpen ? s.WindowText
            : s.AgreedFees.ContainsKey(row.Player.Id) ? $"The fee is agreed: talk terms with {row.Player.Name}"
            : $"Bid for {row.Player.Name}";
    }

    /// <summary>"Make a bid · Jayson Tatum, Boston Celtics" (H68), or plain "Make a bid" with nobody picked.</summary>
    public static string BidTitleFor(MarketRow? row) => row == null ? "Make a bid" : $"Make a bid · {row.Name}, {row.ClubName}";

    private void FillMarket()
    {
        var s = Session;
        Market.Clear();
        int min = (int)(MinAbility ?? 0);
        var pool = s.Market()
            .Where(x => x.player.CurrentAbility >= min)
            .Select(x => (x.player, x.club, league: s.ContextOf(x.club).Name is var n && s.League.Teams.Contains(x.club) ? Hardwood.Core.Names.Brand.Top : s.World.Competitions.FirstOrDefault(c => c.Clubs.Contains(x.club))?.Profile.Name ?? n))
            .Where(x => MarketLeague == "All leagues" || x.league == MarketLeague)
            .ToList();
        PositionChips.Clear();
        foreach (var pos in Positions)
        {
            int n = pos == "All" ? pool.Count : pool.Count(x => x.player.PrimaryPosition.ToString() == pos);
            PositionChips.Add(new FilterChip
            {
                Name = pos, Record = n.ToString(), Selected = pos == MarketPosition,
                Tip = pos == "All" ? $"Every position: {n} players over your filters" : $"{n} {pos}{(n == 1 ? "" : "s")} over your filters",
            });
        }
        var fits = pool.Where(x => MarketPosition == "All" || x.player.PrimaryPosition.ToString() == MarketPosition).ToList();
        var rows = fits.OrderByDescending(x => x.player.CurrentAbility).Take(MarketMax);
        foreach (var (p, club, league) in rows)
            Market.Add(new MarketRow
            {
                Player = p, Club = club, League = league, Age = p.Age(s.Today), Knowledge = Scouting.Knowledge(s.HumanTeam, p),
                AskValue = TransferMarket.Ask(p, club, s.Today, s.ScaleOf(club), s.League.Finance), Agreed = s.AgreedFees.ContainsKey(p.Id),
                Likely = s.LikelyFee(p), StanceInfo = s.StanceOn(p), WontCome = s.WontCome(p),
            });
        MarketCards.Clear();
        foreach (var r in Market.Take(CardCount)) MarketCards.Add(r);
        MarketCardsNote = Market.Count == 0 ? "Nobody on the market fits these filters. Lower Min CA, or pick another league or position."
            : Market.Count <= CardCount ? $"All {Market.Count} player{(Market.Count == 1 ? "" : "s")} who fit, best first. Pick a card to see the deal."
            : $"The best {CardCount} of {fits.Count:N0} by ability. Pick a card to see the deal; the full table lists "
              + (fits.Count > MarketMax ? $"the best {MarketMax}." : "them all.");
    }

    private void FillTransfers()
    {
        var s = Session;
        WindowText = s.WindowText;
        // Run 55's finder: "$-0.0M" for a $40,000 overdraft, and a transfer budget a college program never has.
        static string M(long v) => v < 0 ? "-" + PlayerViewModel.Money(-v) : PlayerViewModel.Money(v);
        var bank = M(s.HumanTeam.Money.Balance);
        TransferBudgetText = s.InCollege ? $"College programs don't pay transfer fees · bank {bank}"
            : $"Transfer budget {M(s.HumanTeam.Money.TransferBudget)} · bank {bank}";
        FillMarket();
        UpdateBidState(SelectedMarket);
        if (SelectedMarket is { } picked) DealText = DealTextFor(picked);
        Incoming.Clear();
        foreach (var b in s.IncomingBids.ToList())
        {
            var p = s.HumanTeam.Roster.FirstOrDefault(x => x.Id == b.PlayerId);
            var from = s.FindTeam(b.FromTeamId);
            if (p == null || from == null) continue;
            var bid = b;
            Incoming.Add(new IncomingRow
            {
                Bid = b, Player = $"{p.Name} ({p.PrimaryPosition}, {p.CurrentAbility})", From = from.FullName,
                Value = $"${TransferMarket.Valuation(p, s.Today, s.MySalaryScale, s.League.Finance) / 1e6:F2}M",
                Accept = new RelayCommand(() => { DealReply = Session.AcceptBid(bid); Shell.AfterTick(); Refresh(); }),
                Reject = new RelayCommand(() => { Session.RejectBid(bid); DealReply = "Bid rejected."; Shell.AfterTick(); Refresh(); }),
            });
        }
        OnPropertyChanged(nameof(HasIncoming));
        FillMyBids();
        RecentTransfers.Clear();
        foreach (var t in s.TransferLog.AsEnumerable().Reverse().Take(40))
            RecentTransfers.Add($"{t.Date:MMM d yyyy} · {t.Player}: {t.From} → {t.To} (${t.Fee / 1e6:F2}M)");
    }

    private void FillMyBids()
    {
        var s = Session;
        MyBids.Clear();
        var shown = s.BidsToShow();
        foreach (var b in shown)
        {
            var p = s.BidPlayer(b);
            var bid = b;
            string next = !s.WindowOpen ? "" : b.Status switch
            {
                MyBidStatus.FeeAgreed => s.Phase == SessionPhase.Offseason ? " · terms by the end of the summer" : " · terms by Jan 31",
                MyBidStatus.Countered => " · bid again to meet it",
                _ => "",
            };
            MyBids.Add(new MyBidRow
            {
                Bid = b, Player = p != null ? $"{b.Player} ({p.PrimaryPosition}, {Scouting.Ability(p, Scouting.Knowledge(s.HumanTeam, p))})" : b.Player,
                When = b.Note is { Length: > 0 } note ? note : $"Answered the same day{next}",
                Act = new RelayCommand(() => OpenDeal(bid)), CanAct = b.Open && s.WindowOpen,
            });
        }
        OnPropertyChanged(nameof(HasMyBids));
        int dropped = s.WindowBids().Count - shown.Count;
        MyBidsText = shown.Count == 0 && dropped > 0
            ? $"Your {dropped} settled bid{(dropped == 1 ? "" : "s")} this window dropped off a week after the answer."
            : shown.Count == 0
            ? (s.WindowOpen ? "You haven't bid for anyone this window. Bid on the Transfer market tab; every bid shows up here." : s.WindowText + " Your bids from the next window will show up here.")
            : $"{shown[0].Window} window: {shown.Count} player{(shown.Count == 1 ? "" : "s")} bid for. Clubs answer the day you bid."
              + (s.WindowOpen ? "" : " The window has shut.")
              + (dropped > 0 ? $" {dropped} settled bid{(dropped == 1 ? "" : "s")} dropped off a week after the answer." : " Settled bids drop off a week after the answer.");
    }

    /// <summary>Back to the market with that player selected, to talk terms or bid again.</summary>
    private void OpenDeal(MyBid bid)
    {
        var s = Session;
        if (s.BidPlayer(bid) is not { } p || s.FindTeam(p.TeamId ?? 0) is not { } club || club == s.HumanTeam) return;
        var row = Market.FirstOrDefault(r => r.Player == p);
        if (row == null)
        {
            row = new MarketRow
            {
                Player = p, Club = club, League = s.ContextOf(club).Name, Age = p.Age(s.Today), Knowledge = Scouting.Knowledge(s.HumanTeam, p),
                AskValue = TransferMarket.Ask(p, club, s.Today, s.ScaleOf(club), s.League.Finance), Agreed = s.AgreedFees.ContainsKey(p.Id),
                Likely = s.LikelyFee(p), StanceInfo = s.StanceOn(p), WontCome = s.WontCome(p),
            };
            Market.Insert(0, row);
        }
        Tab = 0;
        // Run 65: the market opens on cards; a player from below the best 24 (or filtered out) gets his card at the front.
        if (!MarketCards.Contains(row)) MarketCards.Insert(0, row);
        SelectedMarket = row;
        if (bid.Status == MyBidStatus.Countered) BidFee = Math.Round((decimal)(bid.Counter / 1e6), 2);
    }

    [RelayCommand]
    private void PlaceBid()
    {
        if (SelectedMarket is not { } row || BidFee is not { } fee || !Session.WindowOpen) return;
        var r = Session.Bid(row.Player, (long)Math.Round(fee * 1_000_000m / 10_000m) * 10_000);
        DealReply = r.Text;
        if (r.Answer == BidAnswer.Countered) BidFee = Math.Round((decimal)(r.Fee / 1e6), 2);
        if (r.Answer == BidAnswer.Accepted) Talk.Show(row.Player);
        Shell.AfterTick();
        FillMyBids();
    }
}
