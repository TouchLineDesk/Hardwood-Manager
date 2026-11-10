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

    [ObservableProperty] private Player? _player;
    [ObservableProperty] private string _title = "Contract talks";
    [ObservableProperty] private string _askText = "Select a player to talk to.";
    [ObservableProperty] private decimal? _wage;
    [ObservableProperty] private decimal? _years = 3;
    [ObservableProperty] private string _reply = "";
    [ObservableProperty] private bool _canTalk;
    [ObservableProperty] private bool _isRenewal;
    [ObservableProperty] private bool _signed;
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
        Reply = ""; Signed = false;
        if (p == null) { CanTalk = false; AskText = "Select a player to talk to."; Title = "Contract talks"; return; }
        var s = Session;
        IsRenewal = s.MyExpiring.Contains(p);
        Title = IsRenewal ? $"New deal for {p.Name}" : $"Sign {p.Name}";
        var why = s.CannotTalk(p);
        CanTalk = why == null;
        var d = s.PeekDemand(p);
        AskText = why ?? $"His agent asks ${d.Wage / 1e6:F2}M a year for {d.Years} year{(d.Years == 1 ? "" : "s")}." + Context(p);
        Wage = Math.Round((decimal)(d.Wage / 1e6), 2);
        Years = d.Years;
    }

    private string Context(Player p)
    {
        var s = Session;
        var t = s.HumanTeam;
        long room = t.Money.WageBudget - s.MyCommittedWages;
        return $" You have ${room / 1e6:F1}M of wage budget to spend." + (p.Morale < 45 && IsRenewal ? " He isn't happy here, which pushes his price up." : "");
    }

    [RelayCommand]
    private void Offer()
    {
        if (Player is not { } p || Wage is not { } w || Years is not { } y) return;
        var r = Session.Offer(p, (long)Math.Round(w * 1_000_000m / 10_000m) * 10_000, (int)y);
        Reply = r.Text;
        Signed = r.Accepted;
        if (r.Accepted || r.WalkedAway) CanTalk = false;
        else if (!r.Accepted) { Wage = Math.Round((decimal)(r.Ask.Wage / 1e6), 2); }
        _shell.AfterTick();
        _after();
    }

    [RelayCommand]
    private void Release()
    {
        if (Player is not { } p || !IsRenewal) return;
        Session.ReleaseExpiring(p);
        Reply = $"{p.Name} has been released.";
        CanTalk = false;
        _shell.AfterTick();
        _after();
    }
}
