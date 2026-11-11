using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;

namespace Hardwood.Desktop.ViewModels;

/// <summary>A player on the transfer market: his club, what your scouts think he's worth, and his release clause.</summary>
public sealed class MarketRow
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

/// <summary>The transfer market on the Transactions page (decision #87): search, bid, agree terms; answer bids for your players.</summary>
public sealed partial class TransactionsViewModel
{
    public ObservableCollection<MarketRow> Market { get; } = new();
    [ObservableProperty] private MarketRow? _selectedMarket;
    public List<string> MarketLeagues { get; } = new() { "All leagues", "NABL", "Spanish League", "Turkish Super League", "Australian League", "China Pro League", "Other European clubs" };
    public List<string> Positions { get; } = new() { "All", "PG", "SG", "SF", "PF", "C" };
    [ObservableProperty] private string _marketLeague = "All leagues";
    [ObservableProperty] private string _marketPosition = "All";
    [ObservableProperty] private decimal? _minAbility = 110;
    [ObservableProperty] private decimal? _bidFee;
    [ObservableProperty] private string _dealText = "Select a player to see what his club wants.";
    /// <summary>The selected player's deal at a glance: likely fee, the club's stance, whether he'd come, the clause, your budget.</summary>
    public ObservableCollection<Chip> DealChips { get; } = new();
    [ObservableProperty] private string _dealReply = "";
    [ObservableProperty] private string _windowText = "";
    [ObservableProperty] private string _transferBudgetText = "";
    public ObservableCollection<IncomingRow> Incoming { get; } = new();
    public ObservableCollection<string> RecentTransfers { get; } = new();
    public bool HasIncoming => Incoming.Count > 0;

    partial void OnMarketLeagueChanged(string value) => FillMarket();
    partial void OnMarketPositionChanged(string value) => FillMarket();
    partial void OnMinAbilityChanged(decimal? value) => FillMarket();
    partial void OnSelectedMarketChanged(MarketRow? value) => ShowDeal(value);

    private void ShowDeal(MarketRow? row)
    {
        DealReply = "";
        DealChips.Clear();
        if (row == null) { DealText = "Select a player to see what his club wants."; return; }
        var s = Session;
        var p = row.Player;
        if (s.AgreedFees.TryGetValue(p.Id, out var fee))
        {
            DealText = $"Fee agreed with {row.ClubName}: ${fee / 1e6:F2}M. Agree personal terms with {p.Name} below.";
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
        DealText = row.WontCome is { } why ? why + " A bid would be wasted."
            : $"{row.ClubName}: {stance?.Why} Your scouts expect them to want {likely.Text}; a bid at the top of that range should be accepted."
              + (p.Contract is { Buyout: > 0 } cl ? $" His release clause (${cl.Buyout / 1e6:F2}M) can't be refused." : "");
        Talk.Show(null);
    }

    private void FillMarket()
    {
        var s = Session;
        Market.Clear();
        int min = (int)(MinAbility ?? 0);
        var rows = s.Market()
            .Where(x => x.player.CurrentAbility >= min)
            .Where(x => MarketPosition == "All" || x.player.PrimaryPosition.ToString() == MarketPosition)
            .Select(x => (x.player, x.club, league: s.ContextOf(x.club).Name is var n && s.League.Teams.Contains(x.club) ? "NABL" : s.World.Competitions.FirstOrDefault(c => c.Clubs.Contains(x.club))?.Profile.Name ?? n))
            .Where(x => MarketLeague == "All leagues" || x.league == MarketLeague)
            .OrderByDescending(x => x.player.CurrentAbility).Take(300);
        foreach (var (p, club, league) in rows)
            Market.Add(new MarketRow
            {
                Player = p, Club = club, League = league, Age = p.Age(s.Today), Knowledge = Scouting.Knowledge(s.HumanTeam, p),
                AskValue = TransferMarket.Ask(p, club, s.Today, s.ScaleOf(club), s.League.Finance), Agreed = s.AgreedFees.ContainsKey(p.Id),
                Likely = s.LikelyFee(p), StanceInfo = s.StanceOn(p), WontCome = s.WontCome(p),
            });
    }

    private void FillTransfers()
    {
        var s = Session;
        WindowText = s.WindowText;
        TransferBudgetText = $"Transfer budget ${s.HumanTeam.Money.TransferBudget / 1e6:F2}M · bank ${s.HumanTeam.Money.Balance / 1e6:F1}M";
        FillMarket();
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
        RecentTransfers.Clear();
        foreach (var t in s.TransferLog.AsEnumerable().Reverse().Take(40))
            RecentTransfers.Add($"{t.Date:MMM d yyyy} · {t.Player}: {t.From} → {t.To} (${t.Fee / 1e6:F2}M)");
    }

    [RelayCommand]
    private void PlaceBid()
    {
        if (SelectedMarket is not { } row || BidFee is not { } fee) return;
        var r = Session.Bid(row.Player, (long)Math.Round(fee * 1_000_000m / 10_000m) * 10_000);
        DealReply = r.Text;
        if (r.Answer == BidAnswer.Countered) BidFee = Math.Round((decimal)(r.Fee / 1e6), 2);
        if (r.Answer == BidAnswer.Accepted) Talk.Show(row.Player);
        Shell.AfterTick();
    }
}
