using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

/// <summary>
/// Contract talks with one player (decision #89): his ask, your offer (wage in $M and years), his reply.
/// Shared by the Offseason page (renewals and free agents) and the in-season free-agent list.
/// </summary>
public sealed partial class TalkViewModel : ViewModelBase
{
    private readonly MainViewModel _shell;
    private readonly Action _after;
    private GameSession Session => _shell.Session!;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlayerTeam))] private Player? _player;
    /// <summary>His club, for the card's crest and colours (none for a free agent).</summary>
    public Team? PlayerTeam => Player?.TeamId is { } id ? Session.FindTeam(id) : null;
    [ObservableProperty] private string _title = "Contract talks";
    [ObservableProperty] private string _askText = "Select a player to talk to.";
    [ObservableProperty] private decimal? _wage;
    [ObservableProperty] private decimal? _years = 3;
    [ObservableProperty] private string _reply = "";
    [ObservableProperty] private bool _canTalk;
    [ObservableProperty] private bool _isRenewal;
    [ObservableProperty] private bool _signed;
    /// <summary>Release is pressable only for a player of yours in renewal talks who is still yours (bug hunt H1).</summary>
    [ObservableProperty] private bool _canRelease;
    [ObservableProperty] private string _releaseTip = "Pick one of your players whose contract is ending first";
    /// <summary>Wage step for the spinner: $50K in the NABL, $10K for smaller leagues.</summary>
    public decimal WageStep => Session.MySalaryScale >= 0.5 ? 0.05m : 0.01m;

    public TalkViewModel(MainViewModel shell, Action after)
    {
        _shell = shell;
        _after = after;
    }

    /// <summary>Open talks with a player (or clear the panel).</summary>
    public void Show(Player? p)
    {
        Player = p;
        Reply = ""; Signed = false; _offers = 0; _lastReply = ""; ConfirmingRelease = false;
        if (p == null)
        {
            // H1: the renewal flag used to survive a cleared panel, leaving a Release button that did nothing.
            CanTalk = false; IsRenewal = false; CanRelease = false;
            AskText = "Select a player to talk to."; Title = "Contract talks"; return;
        }
        var s = Session;
        IsRenewal = s.MyExpiring.Contains(p);
        CanRelease = IsRenewal;
        ReleaseTip = IsRenewal ? $"Let {p.Name} go now instead of offering a new deal" : "Pick one of your players whose contract is ending first";
        Title = IsRenewal ? $"New deal for {p.Name}" : $"Sign {p.Name}";
        var why = s.CannotTalk(p);
        CanTalk = why == null;
        var d = s.PeekDemand(p);
        _lead = why;
        _withContext = why == null;
        if (why == null) _lead = $"His agent asks ${d.Wage / 1e6:F2}M a year for {d.Years} year{(d.Years == 1 ? "" : "s")}.";
        RefreshBudget();
        Wage = Math.Round((decimal)(d.Wage / 1e6), 2);
        Years = d.Years;
    }

    /// <summary>The panel's first sentence (his ask, why you can't talk, or the deal he signed) and whether the budget line follows.</summary>
    private string? _lead;
    private bool _withContext;

    /// <summary>
    /// H84 (run 103): the budget line is worked out again whenever the page refreshes, so after a signing (or anything else
    /// that moves the wages) it reads the same "to spend" as the page's header instead of the figure from when talks opened.
    /// The reply to your last offer stays as it is.
    /// </summary>
    public void RefreshBudget()
    {
        if (Player is not { } p) return;
        AskText = (_lead ?? "") + (_withContext ? Context(p) : "");
    }

    /// <summary>Money as the Offseason header writes it: whole millions from $10M up, one decimal below.</summary>
    public static string M(long v) => Budgets.Money(v);

    /// <summary>
    /// H84 (run 103): the budget sentence. It starts from the header's own figure (the budget less the wages committed),
    /// then says what's left for this deal once the owner's reserves are kept back, so the two never show different numbers.
    /// </summary>
    public static string BudgetLine(long budget, long committed, long seniors, long fill, bool signed = false)
    {
        long spend = budget - committed;
        if (spend < 0) return $" You are {M(-spend)} over your wage budget.";
        var parts = new List<string>();
        if (seniors > 0) parts.Add($"{M(seniors)} for your committed college seniors");
        if (fill > 0) parts.Add($"{M(fill)} for the minimum deals the roster still needs");
        if (parts.Count == 0) return $" You have {M(spend)} of wage budget to spend.";
        long room = spend - seniors - fill;
        var kept = string.Join(" and ", parts);
        var deal = signed ? "another deal" : "this deal";   // review (run 103): once he has signed, the room is for the next one
        return room >= 0
            ? $" You have {M(spend)} of wage budget to spend, {M(room)} of it for {deal} after keeping {kept}."
            : $" You have {M(spend)} of wage budget to spend, but keeping {kept} leaves nothing for {deal} ({M(-room)} short).";
    }

    private string Context(Player p)
    {
        var s = Session;
        // Over budget reads as such, not as "$-1.1M to spend" (review #14, the bug finder).
        return BudgetLine(s.HumanTeam.Money.WageBudget, s.MyCommittedWages, s.MyReservedWages, s.MyRosterFillReserve, Signed)
            + (p.Morale < 45 && IsRenewal && !Signed ? " He isn't happy here, which pushes his price up." : "")
            // H18 (run 100): say plainly what happens if you don't act, by name.
            + (IsRenewal && !Signed ? $" {p.Name} leaves as a free agent if he's unsigned {(s.InNabl ? "when contract decisions end" : "at the end of the summer")}." : "");
    }

    private int _offers;
    private string _lastReply = "";

    /// <summary>Bug list H7: a second offer that drew the same answer looked like nothing happened. Each reply now
    /// carries the offer's number once you've made more than one, so every press shows something new.</summary>
    public static string NumberedReply(int offer, string text, string last) =>
        offer > 1 ? $"Offer {offer}{(text == last ? ", same answer" : "")}: {text}" : text;

    [RelayCommand]
    private void Offer()
    {
        if (Player is not { } p) return;
        ConfirmingRelease = false;
        // H9 (run 67): an empty (or zero) Wage or Years box made Make offer do nothing and say nothing.
        if (Wage is not { } w || w <= 0 || Years is not { } y || y < 1)
        {
            var d = Session.PeekDemand(p);
            Reply = $"Enter a wage and years first. His agent asks ${d.Wage / 1e6:F2}M a year for {d.Years} year{(d.Years == 1 ? "" : "s")}, so the boxes are back at his ask: press Make offer to offer that, or change it.";
            if (Wage is not > 0) Wage = Math.Round((decimal)(d.Wage / 1e6), 2);
            if (Years is not >= 1) Years = d.Years;
            return;
        }
        var r = Session.Offer(p, (long)Math.Round(w * 1_000_000m / 10_000m) * 10_000, (int)y);
        _offers++;
        Reply = NumberedReply(_offers, r.Text, _lastReply);
        _lastReply = r.Text;
        Signed = r.Accepted;
        if (r.Accepted || r.WalkedAway) CanTalk = false;
        if (r.Accepted)
        {
            CanRelease = false; ReleaseTip = $"{p.Name} has signed his new deal";
            // H84 (run 103): the panel says he's signed and on what, instead of still reading "Sign …" with his old ask.
            long pay = (long)Math.Round(w * 1_000_000m / 10_000m) * 10_000; int yrs = (int)y;   // the offer he accepted
            Title = $"{p.Name} has signed";
            _lead = $"Signed · ${pay / 1e6:F2}M × {yrs} year{(yrs == 1 ? "" : "s")}.";
            _withContext = true;
        }
        else if (!r.Accepted) { Wage = Math.Round((decimal)(r.Ask.Wage / 1e6), 2); }
        _shell.AfterTick();
        _after();
        RefreshBudget();
    }

    // H24 (bug hunt, Nov 16): Release asks first.
    [ObservableProperty] private bool _confirmingRelease;

    [RelayCommand]
    private void Release()
    {
        if (Player is not { } p || !CanRelease) return;
        Reply = $"Release {p.Name}? He becomes a free agent and you lose the right to re-sign him. You can't undo it.";
        ConfirmingRelease = true;
    }

    [RelayCommand]
    private void CancelRelease()
    {
        if (!ConfirmingRelease) return;
        ConfirmingRelease = false;
        Reply = Player is { } p ? $"{p.Name} stays in talks." : "";
    }

    [RelayCommand]
    private void ConfirmRelease()
    {
        ConfirmingRelease = false;
        if (Player is not { } p || !CanRelease) return;
        Session.ReleaseExpiring(p);
        Reply = $"{p.Name} has been released.";
        CanTalk = false;
        CanRelease = false;
        ReleaseTip = $"{p.Name} has already been released";
        _shell.AfterTick();
        _after();
    }
}
