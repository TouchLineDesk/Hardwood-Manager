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
        Reply = ""; Signed = false; _offers = 0; _lastReply = "";
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
        AskText = why ?? $"His agent asks ${d.Wage / 1e6:F2}M a year for {d.Years} year{(d.Years == 1 ? "" : "s")}." + Context(p);
        Wage = Math.Round((decimal)(d.Wage / 1e6), 2);
        Years = d.Years;
    }

    private string Context(Player p)
    {
        var s = Session;
        long room = s.MyWageRoom;
        // Over budget reads as such, not as "$-1.1M to spend" (review #14, the bug finder).
        return (room >= 0 ? $" You have ${room / 1e6:F1}M of wage budget to spend" : $" You are ${-room / 1e6:F1}M over your wage budget") + Kept(s.MyReservedWages, s.MyRosterFillReserve) + (p.Morale < 45 && IsRenewal ? " He isn't happy here, which pushes his price up." : "");
    }

    private static string Kept(long seniors, long fill)
    {
        var parts = new List<string>();
        if (seniors > 0) parts.Add($"${seniors / 1e6:F1}M kept for your committed college seniors");
        if (fill > 0) parts.Add($"${fill / 1e6:F1}M kept for the minimum deals the roster still needs");
        return parts.Count == 0 ? "." : $" (after {string.Join(" and ", parts)}).";
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
        if (r.Accepted) { CanRelease = false; ReleaseTip = $"{p.Name} has signed his new deal"; }
        else if (!r.Accepted) { Wage = Math.Round((decimal)(r.Ask.Wage / 1e6), 2); }
        _shell.AfterTick();
        _after();
    }

    [RelayCommand]
    private void Release()
    {
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
