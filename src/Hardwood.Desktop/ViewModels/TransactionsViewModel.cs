using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed partial class TradePiece : ObservableObject
{
    public required Player Player { get; init; }
    public TransactionsViewModel Owner { get; init; } = null!;
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public double Knowledge { get; init; } = 1.0;
    public string CA => Scouting.Ability(Player, Knowledge).ToString();
    public string PA => Scouting.Potential(Player, Knowledge).ToString();
    public string Salary => Player.Contract is { } c ? $"{Hardwood.Core.Season.Budgets.Money(c.Salary)} × {Math.Max(c.Years, 1)}" : "-";
    public string Value { get; init; } = "";
    [ObservableProperty] private bool _included;
    partial void OnIncludedChanged(bool value) => Owner.Evaluate();
}

public sealed partial class PickPiece : ObservableObject
{
    public required DraftPickAsset Pick { get; init; }
    public TransactionsViewModel Owner { get; init; } = null!;
    public string Description { get; init; } = "";
    public string Projected { get; init; } = "";
    public string Value { get; init; } = "";
    [ObservableProperty] private bool _included;
    partial void OnIncludedChanged(bool value) => Owner.Evaluate();
}

public sealed partial class TransactionsViewModel : PageViewModel
{
    public ObservableCollection<PickPiece> OurPicks { get; } = new();
    public ObservableCollection<PickPiece> TheirPicks { get; } = new();
    public override string Title => "Transactions";

    // Free agents
    public ObservableCollection<PlayerRow> FreeAgents { get; } = new();
    [ObservableProperty] private PlayerRow? _selectedFreeAgent;
    public Player? SelectedFreeAgentPlayer => SelectedFreeAgent?.Player;
    public double FreeAgentKnowledge => SelectedFreeAgent?.Knowledge ?? 1.0;
    partial void OnSelectedFreeAgentChanged(PlayerRow? value)
    {
        OnPropertyChanged(nameof(SelectedFreeAgentPlayer)); OnPropertyChanged(nameof(FreeAgentKnowledge));
        foreach (var c in FreeAgentCards) c.Picked = value != null && c.Player == value.Player;
        if (_rebuilding) return;
        if (value != null) _talkingTo = value.Player;
        Talk.Show(value?.Player);
    }
    /// <summary>The free agent in talks. Kept apart from <see cref="SelectedFreeAgent"/>, which the table clears when
    /// the list is rebuilt (an offer refreshes the page twice: code review, run 67).</summary>
    private Player? _talkingTo;
    /// <summary>True while Refresh rebuilds the lists: the table drops and regains the same man, and his talks
    /// (with the answer to your last offer) must stay on screen.</summary>
    private bool _rebuilding;

    // Run 67 (review #13 rank 6, bug hunt H9): free agents as cards with ask chips; the full table one click away.
    public string[] FaViews { get; } = { "Cards", "Full table" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowFaCards), nameof(ShowFaTable))] private string _faView = "Cards";
    public bool ShowFaCards => FaView == "Cards";
    public bool ShowFaTable => !ShowFaCards;
    public ObservableCollection<FreeAgentRow> FreeAgentCards { get; } = new();
    [ObservableProperty] private string _faCardsNote = "";

    /// <summary>A free agent's card picks him for talks (the same as picking his row in the table).</summary>
    public void PickFreeAgentCard(FreeAgentRow card)
    {
        if (FreeAgents.FirstOrDefault(r => r.Player == card.Player) is { } row) SelectedFreeAgent = row;
    }
    /// <summary>Contract talks with the selected free agent (decision #89).</summary>
    public TalkViewModel Talk { get; }
    /// <summary>Trades only exist under the old NABL rules; the open market has transfers instead.</summary>
    public bool TradesOpen => !Session.League.Finance.OpenMarket && Session.InNabl;
    [ObservableProperty] private string _faStatus = "";
    /// <summary>H39 (run 81): the Waive tab's own status line (it showed the Free agents hint).</summary>
    [ObservableProperty] private string _waiveStatus = "";
    /// <summary>H43/H44 (run 81): out of work or at a college program, the page shows why instead of its tabs.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Open))] private bool _closed;
    public bool Open => !Closed;
    [ObservableProperty] private string _closedText = "";
    [ObservableProperty] private string _capText = "";

    // Trade builder
    public ObservableCollection<Team> Partners { get; } = new();
    [ObservableProperty] private Team? _partner;
    public ObservableCollection<TradePiece> Ours { get; } = new();
    public ObservableCollection<TradePiece> Theirs { get; } = new();
    [ObservableProperty] private string _partnerDirection = "";
    [ObservableProperty] private string _verdict = "";
    [ObservableProperty] private string _verdictDetail = "";
    [ObservableProperty] private bool _canPropose;
    [ObservableProperty] private string _deadlineText = "";

    public TransactionsViewModel(MainViewModel shell) : base(shell)
    {
        Talk = new TalkViewModel(shell, () => Refresh());
        foreach (var t in Session.League.Teams.Where(t => t != Session.HumanTeam).OrderBy(t => t.City)) Partners.Add(t);
        _partner = Partners.FirstOrDefault();
    }

    partial void OnPartnerChanged(Team? value) => RebuildTrade();

    public override void Refresh()
    {
        PendingWaive = null;   // H24: a question left open doesn't outlive the page as it was (code review)
        var s = Session; var fin = s.League.Finance;
        // H43/H44 (run 81): no club of yours to sign for, waive from or trade: say so and show nothing to press.
        if (s.RosterMovesClosed is { } closed)
        {
            Closed = true;
            WaiveStatus = "";   // code review: an old "Waived …" line doesn't follow you to the next job
            ClosedText = closed + (s.Employed ? "" : " Open jobs are on the Board page.");
            FreeAgents.Clear(); FreeAgentCards.Clear(); Ours.Clear(); Theirs.Clear(); OurPicks.Clear(); TheirPicks.Clear();
            CapText = DeadlineText = FaStatus = ""; CanPropose = false;
            return;
        }
        if (Closed)
        {
            // Back in work (perhaps at another club): the partner list is rebuilt for the new club (code review).
            Partners.Clear();
            foreach (var t in s.League.Teams.Where(t => t != s.HumanTeam).OrderBy(t => t.City)) Partners.Add(t);
            _partner = Partners.FirstOrDefault(); OnPropertyChanged(nameof(Partner));
        }
        Closed = false; ClosedText = "";
        var keep = _talkingTo;
        _rebuilding = true;
        FreeAgents.Clear(); FreeAgentCards.Clear();
        foreach (var p in s.League.FreeAgents.OrderByDescending(p => p.CurrentAbility)) FreeAgents.Add(PlayerRow.From(p, s, "FA"));
        long room = s.MyWageRoom;
        // H242 (run 236): the cards lead with players who'd come and fit the room (from the best 250 by ability).
        foreach (var r in SummerCards.WithinReach(FreeAgents.Take(250).Select(r => SummerCards.FreeAgent(s, r.Player, s.Today, false, room)).ToList()))
            FreeAgentCards.Add(r);
        // H18 (run 100): the man you're talking to from further down goes after the best, not ahead of them.
        bool extra = keep != null && !FreeAgentCards.Any(c => c.Player == keep) && s.League.FreeAgents.Contains(keep);
        if (extra) FreeAgentCards.Add(SummerCards.FreeAgent(s, keep!, s.Today, false, room));
        // H103 (run 112): say when players reach the pool, not just that it is empty.
        FaCardsNote = (FreeAgents.Count == 0 ? FreeAgentsEmptyNote
            : FreeAgents.Count <= SummerCards.FreeAgentCount ? $"All {FreeAgents.Count} free agent{(FreeAgents.Count == 1 ? "" : "s")}, best first, with what each asks of your club. Pick a card to talk to him."
            : SummerCards.CardsNote(FreeAgentCards, FreeAgents.Count, room) + " Pick a card to talk to him; the full table lists them all by ability.")
            + (extra ? $" {keep!.Name}, whom you're talking to, is last." : "");
        // Keep the man you were talking to (the rows are rebuilt), so an offer's answer stays on screen.
        var again = keep == null ? null : FreeAgents.FirstOrDefault(r => r.Player == keep);
        if (again != null) SelectedFreeAgent = again;
        _rebuilding = false;
        // Nobody picked yet: the best free agent. The man you were talking to has signed: his answer stays up.
        if (again == null && keep == null) SelectedFreeAgent = FreeAgents.FirstOrDefault();
        else if (Talk.Player != keep) Talk.Show(keep);
        Talk.RefreshBudget();   // H84 (run 103): the panel's budget is the page's, after a signing too
        long payroll = ContractMarket.Payroll(s.HumanTeam);
        CapText = $"Roster {s.HumanTeam.Roster.Count}/{s.MyRosterMax} · " + fin.Describe(payroll);
        DeadlineText = s.Phase == SessionPhase.Offseason ? "Offseason: trades are open."
            : s.TradeDeadlinePassed ? "The trade deadline has passed; trades reopen after the Finals."
            : $"Trade deadline: {s.TradeDeadline:MMM d}.";
        RebuildTrade();
        FillTransfers();
        if (!TradesOpen)
        {
            DeadlineText = s.WindowText + " Free agents can be signed at any time through contract talks.";
            CapText = $"Roster {s.HumanTeam.Roster.Count}/{s.MyRosterMax} · {s.MyBudgetLine}";
            CanPropose = false;
            FaStatus = "Select a free agent to see his ask and make an offer.";
        }
    }

    public string AskFor(Player p) => $"${ContractMarket.MarketSalary(p.CurrentAbility, p.Age(Session.Today), Session.League.Finance) / 1e6:F1}M";

    [RelayCommand]
    public void Sign()
    {
        var row = SelectedFreeAgent;
        if (row == null) return;
        var s = Session; var fin = s.League.Finance; var team = s.HumanTeam;
        if (s.RosterMovesClosed is { } closed) { FaStatus = closed; return; }
        if (s.Abroad) { FaStatus = "Clubs abroad sign players in the summer (your sporting director handles it for now)."; return; }
        if (team.Roster.Count >= fin.RosterMax) { FaStatus = "Roster is full. Waive someone first."; return; }
        long ask = ContractMarket.MarketSalary(row.Player.CurrentAbility, row.Player.Age(s.Today), fin);
        long payroll = ContractMarket.Payroll(team);
        long room = fin.SalaryCap - payroll;
        // In season: cap room, or a minimum deal (players asking up to twice the minimum take one; under the soft cap
        // anyone may add one up to the apron, under a hard cap only a team below the roster minimum).
        bool minDeal = ask <= fin.MinSalary * 2 && (team.Roster.Count < fin.RosterMin || (fin.SoftCap && payroll + fin.MinSalary <= fin.Apron));
        if (ask > room && !minDeal) { FaStatus = $"{row.Player.Name} asks ${ask / 1e6:F1}M; you have ${Math.Max(0, room) / 1e6:F1}M of cap room and he won't take a minimum deal."; return; }
        var p = row.Player;
        p.Contract = new Contract { Years = p.Age(s.Today) <= 26 ? 3 : 2, Salary = ask > room ? fin.MinSalary : ask, StartSeason = s.League.Season };
        p.TeamId = team.Id;
        team.Roster.Add(p);
        s.League.FreeAgents.Remove(p);
        team.DepthChart = null;
        s.Post($"Signed {p.Name}", $"{p.Name} ({p.PrimaryPosition}, age {p.Age(s.Today)}) joins on a {p.Contract.Years}-year deal at {Hardwood.Core.Season.Budgets.Money(p.Contract.Salary)} per season.", "Transactions");
        s.SaveNow();
        FaStatus = $"Signed {p.Name}.";
        Shell.AfterTick();
    }

    // H24 (bug hunt, Nov 16): Waive asks first. One press used to release the player and save at once, and the list is
    // sorted best first, so a misclick on the top row cut your star for good.
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ConfirmingWaive), nameof(WaiveQuestion))] private TradePiece? _pendingWaive;
    public bool ConfirmingWaive => PendingWaive != null;
    public string WaiveQuestion => PendingWaive is { } w
        ? $"Waive {w.Name}? He becomes a free agent at once, his deal comes off your books and any club can sign him. You can't undo it."
        : "";

    /// <summary>Waive beside a player: asks first (<see cref="ConfirmWaive"/> does it).</summary>
    [RelayCommand]
    public void Waive(TradePiece? piece)
    {
        if (piece == null) return;
        var s = Session;
        if (s.RosterMovesClosed is { } closed) { PendingWaive = null; WaiveStatus = closed; return; }
        if (s.HumanTeam.Roster.Count <= s.MyRosterMin) { PendingWaive = null; WaiveStatus = $"You need at least {s.MyRosterMin} players."; return; }
        WaiveStatus = "";
        PendingWaive = piece;
    }

    [RelayCommand]
    public void CancelWaive()
    {
        if (PendingWaive is { } w) WaiveStatus = $"{w.Name} stays.";
        PendingWaive = null;
    }

    [RelayCommand]
    public void ConfirmWaive()
    {
        if (PendingWaive is not { } piece) return;
        PendingWaive = null;
        var s = Session;
        if (!s.HumanTeam.Roster.Contains(piece.Player) && s.RosterMovesClosed == null) return;
        // H44 (run 81): the rules live in GameSession.Waive (closed out of work and at a college program).
        if (s.Waive(piece.Player) is { } why) { WaiveStatus = why; return; }
        WaiveStatus = $"Waived {piece.Player.Name}.";
        Shell.AfterTick();
    }

    /// <summary>H103 (run 112): the empty pool says when players arrive.</summary>
    public const string FreeAgentsEmptyNote = "No free agents yet. Players reach the pool when clubs release them during the season, and when contracts end in the summer; they'll be listed here.";

    private void RebuildTrade()
    {
        var s = Session;
        Ours.Clear(); Theirs.Clear();
        var engine = s.Trades;
        if (Partner == null) return;
        foreach (var p in s.HumanTeam.Roster.OrderByDescending(p => p.CurrentAbility))
            Ours.Add(new TradePiece { Player = p, Owner = this, Age = p.Age(s.Today), Value = engine.ValueTo(Partner, p).ToString("0.0") });
        double know = Scouting.Knowledge(s.HumanTeam, Partner.Roster.FirstOrDefault() ?? s.HumanTeam.Roster[0]);
        foreach (var p in Partner.Roster.OrderByDescending(p => p.CurrentAbility))
            Theirs.Add(new TradePiece { Player = p, Owner = this, Age = p.Age(s.Today), Knowledge = know, Value = engine.ValueTo(Partner, p).ToString("0.0") });
        OurPicks.Clear(); TheirPicks.Clear();
        string Abbr(int id) => s.FindTeam(id)!.Abbreviation;
        foreach (var pk in s.League.PicksOwnedBy(s.HumanTeam.Id))
            OurPicks.Add(new PickPiece { Pick = pk, Owner = this, Description = pk.Describe(Abbr), Projected = $"~#{engine.ProjectedSlot(pk):0}", Value = engine.PickValueTo(Partner, pk).ToString("0.0") });
        foreach (var pk in s.League.PicksOwnedBy(Partner.Id))
            TheirPicks.Add(new PickPiece { Pick = pk, Owner = this, Description = pk.Describe(Abbr), Projected = $"~#{engine.ProjectedSlot(pk):0}", Value = engine.PickValueTo(Partner, pk).ToString("0.0") });
        PartnerDirection = $"{Partner.FullName}: {engine.Direction(Partner)} · payroll ${ContractMarket.Payroll(Partner) / 1e6:F1}M · {Partner.Roster.Count} players";
        Evaluate();
    }

    private TradeProposal? Build()
    {
        if (Partner == null) return null;
        var t = new TradeProposal { From = Session.HumanTeam, To = Partner };
        t.FromPlayers.AddRange(Ours.Where(x => x.Included).Select(x => x.Player));
        t.ToPlayers.AddRange(Theirs.Where(x => x.Included).Select(x => x.Player));
        t.FromPicks.AddRange(OurPicks.Where(x => x.Included).Select(x => x.Pick));
        t.ToPicks.AddRange(TheirPicks.Where(x => x.Included).Select(x => x.Pick));
        return t;
    }

    public void Evaluate()
    {
        var t = Build();
        if (t == null || t.IsEmpty) { Verdict = "Select players or picks to trade."; VerdictDetail = ""; CanPropose = false; return; }
        var eval = Session.Trades.Evaluate(t);
        Verdict = eval.Verdict;
        VerdictDetail = eval.Legal
            ? $"They value what they give at {eval.ValueGivenByTo:0.0} and what they get at {eval.ValueReceivedByTo:0.0} ({eval.GainForTo * 100:+0;-0}%)."
              + (eval.Accepted ? "" : Session.Trades.SuggestSweetener(t) is { } sw ? $" Adding {sw.Name} would get it done."
                 : Session.Trades.SuggestPickSweetener(t) is { } pk ? $" Adding your {pk.Describe(id => Session.FindTeam(id)!.Abbreviation)} pick would get it done." : "")
            : eval.LegalityNote;
        CanPropose = eval.Accepted && !(Session.TradeDeadlinePassed && Session.Phase is SessionPhase.RegularSeason or SessionPhase.Postseason);
    }

    [RelayCommand]
    public void Propose()
    {
        var t = Build();
        if (t == null) return;
        var eval = Session.ProposeTrade(t);
        Verdict = eval.Accepted ? "Trade completed." : eval.Verdict;
        if (eval.Accepted) { Shell.AfterTick(); RebuildTrade(); }
    }

    [RelayCommand]
    public void ClearTrade()
    {
        foreach (var x in Ours) x.Included = false;
        foreach (var x in Theirs) x.Included = false;
        foreach (var x in OurPicks) x.Included = false;
        foreach (var x in TheirPicks) x.Included = false;
    }
}
