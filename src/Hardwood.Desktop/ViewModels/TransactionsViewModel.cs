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
    public int CA => Player.CurrentAbility;
    public int PA => Player.PotentialAbility;
    public string Salary => Player.Contract is { } c ? $"${c.Salary / 1e6:F1}M × {Math.Max(c.Years, 1)}" : "-";
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
    [ObservableProperty] private string _faStatus = "";
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
        foreach (var t in Session.League.Teams.Where(t => t != Session.HumanTeam).OrderBy(t => t.City)) Partners.Add(t);
        _partner = Partners.FirstOrDefault();
    }

    partial void OnPartnerChanged(Team? value) => RebuildTrade();

    public override void Refresh()
    {
        var s = Session; var fin = s.League.Finance;
        FreeAgents.Clear();
        foreach (var p in s.League.FreeAgents.OrderByDescending(p => p.CurrentAbility)) FreeAgents.Add(PlayerRow.From(p, s, "FA"));
        long payroll = ContractMarket.Payroll(s.HumanTeam);
        CapText = $"Roster {s.HumanTeam.Roster.Count}/{fin.RosterMax} · payroll ${payroll / 1e6:F1}M · room ${(fin.HardCap - payroll) / 1e6:F1}M";
        DeadlineText = s.Phase == SessionPhase.Offseason ? "Offseason: trades are open."
            : s.TradeDeadlinePassed ? "The trade deadline has passed; trades reopen after the Finals."
            : $"Trade deadline: {s.TradeDeadline:MMM d}.";
        RebuildTrade();
    }

    public string AskFor(Player p) => $"${ContractMarket.MarketSalary(p.CurrentAbility, p.Age(Session.Today), Session.League.Finance) / 1e6:F1}M";

    [RelayCommand]
    public void Sign()
    {
        var row = SelectedFreeAgent;
        if (row == null) return;
        var s = Session; var fin = s.League.Finance; var team = s.HumanTeam;
        if (team.Roster.Count >= fin.RosterMax) { FaStatus = "Roster is full. Waive someone first."; return; }
        long ask = ContractMarket.MarketSalary(row.Player.CurrentAbility, row.Player.Age(s.Today), fin);
        long room = fin.HardCap - ContractMarket.Payroll(team);
        bool minDeal = team.Roster.Count < fin.RosterMin && ask <= fin.MinSalary * 2;
        if (ask > room && !minDeal) { FaStatus = $"{row.Player.Name} asks ${ask / 1e6:F1}M; you have ${room / 1e6:F1}M of room."; return; }
        var p = row.Player;
        p.Contract = new Contract { Years = p.Age(s.Today) <= 26 ? 3 : 2, Salary = ask > room ? fin.MinSalary : ask, StartSeason = s.League.Season };
        p.TeamId = team.Id;
        team.Roster.Add(p);
        s.League.FreeAgents.Remove(p);
        team.DepthChart = null;
        s.Post($"Signed {p.Name}", $"{p.Name} ({p.PrimaryPosition}, age {p.Age(s.Today)}) joins on a {p.Contract.Years}-year deal at ${p.Contract.Salary / 1e6:F1}M per season.", "Transactions");
        s.SaveNow();
        FaStatus = $"Signed {p.Name}.";
        Shell.AfterTick();
    }

    [RelayCommand]
    public void Waive(TradePiece? piece)
    {
        if (piece == null) return;
        var s = Session; var team = s.HumanTeam;
        if (team.Roster.Count <= s.League.Finance.RosterMin) { FaStatus = $"You need at least {s.League.Finance.RosterMin} players."; return; }
        var p = piece.Player;
        team.Roster.Remove(p);
        p.TeamId = null; p.Contract = null;
        s.League.FreeAgents.Add(p);
        team.DepthChart = null;
        s.Post($"Waived {p.Name}", $"{p.Name} has been released and is a free agent.", "Transactions");
        s.SaveNow();
        FaStatus = $"Waived {p.Name}.";
        Shell.AfterTick();
    }

    private void RebuildTrade()
    {
        var s = Session;
        Ours.Clear(); Theirs.Clear();
        var engine = s.Trades;
        if (Partner == null) return;
        foreach (var p in s.HumanTeam.Roster.OrderByDescending(p => p.CurrentAbility))
            Ours.Add(new TradePiece { Player = p, Owner = this, Age = p.Age(s.Today), Value = engine.ValueTo(Partner, p).ToString("0.0") });
        foreach (var p in Partner.Roster.OrderByDescending(p => p.CurrentAbility))
            Theirs.Add(new TradePiece { Player = p, Owner = this, Age = p.Age(s.Today), Value = engine.ValueTo(Partner, p).ToString("0.0") });
        OurPicks.Clear(); TheirPicks.Clear();
        string Abbr(int id) => s.League.TeamById(id).Abbreviation;
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
                 : Session.Trades.SuggestPickSweetener(t) is { } pk ? $" Adding your {pk.Describe(id => Session.League.TeamById(id).Abbreviation)} pick would get it done." : "")
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
