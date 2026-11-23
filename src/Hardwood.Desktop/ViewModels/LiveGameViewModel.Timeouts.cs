using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Engine;

namespace Hardwood.Desktop.ViewModels;

/// <summary>
/// Timeouts (#1064, run 168): the coach's button, the timeouts left on the score bug, and the commentary line when either
/// bench calls one. The engine calls the usual ones for both benches (to stop a run, draw up a late play or rest a tired
/// five); the button calls one of yours at the next dead ball.
/// </summary>
public sealed partial class LiveGameViewModel
{
    private bool UsHome => _live.Result.Home.Team == Us;
    private int OurTimeouts => _live.TimeoutsLeft(UsHome);

    /// <summary>The button's words: "Timeout · 3 left", "Timeout at the next dead ball", "No timeouts left".</summary>
    public string TimeoutButtonText => _live.TimeoutPending(Us) ? "Timeout at the next dead ball"
        : OurTimeouts == 0 ? "No timeouts left" : $"Call timeout · {OurTimeouts} left";
    public bool CanCallTimeout => !Complete && OurTimeouts > 0 && !_live.TimeoutPending(Us);
    public string TimeoutTip => "Stop the game at the next dead ball: your five get a rest and you draw up the next play. " +
        "Your assistant also calls them for you to stop a run, draw up a late play or rest a tired five.";

    [RelayCommand]
    private void CallTimeout()
    {
        if (!CanCallTimeout || !_live.RequestTimeout(Us)) return;
        Status = "Timeout called: it comes at the next dead ball.";
        RaiseTimeoutProps();
    }

    private void RaiseTimeoutProps()
    {
        OnPropertyChanged(nameof(TimeoutButtonText)); OnPropertyChanged(nameof(CanCallTimeout));
        OnPropertyChanged(nameof(TimeoutsLine));
    }

    /// <summary>The commentary for timeouts the engine took in the step just played ("TIMEOUT, DUKE: to stop a 9-0 run · 3 left").</summary>
    private List<string> TimeoutNotes(int before)
    {
        var notes = new List<string>();
        var all = _live.Result.Timeouts;
        for (int i = before; i < all.Count; i++)
        {
            var t = all[i];
            var line = TimeoutLine(Short(t.Home ? HomeTeam : AwayTeam), t);
            notes.Add(line);
            Log.Insert(0, $"{_live.PeriodText} {_live.ClockText}  >> {line}");
        }
        if (notes.Count > 0) RaiseTimeoutProps();
        return notes;
    }

    public static string TimeoutLine(string team, TimeoutCall t) => $"Timeout, {team}: {t.Why} · {t.LeftText}";
}
