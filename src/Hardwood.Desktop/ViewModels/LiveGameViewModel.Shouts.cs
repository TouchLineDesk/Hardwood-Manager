using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;

namespace Hardwood.Desktop.ViewModels;

/// <summary>One shout button: its words, its tooltip and whether it's on.</summary>
public sealed partial class ShoutRow : ObservableObject
{
    public required ShoutInfo Info { get; init; }
    public string Label => Info.Label;
    public string Tip => Info.Tip + " Tap again to cancel. For this game only.";
    public string Key => ((int)Info.Shout).ToString();
    /// <summary>The page's toggle (the button binds to it with its <see cref="Key"/>).</summary>
    public required System.Windows.Input.ICommand Toggle { get; init; }
    [ObservableProperty] private bool _isOn;
}

/// <summary>
/// M3 of the 2D match engine (run 150, `claude/match-engine-2d-design.md` C): the shout bar, changes on the fly and the
/// commentary naming them. Shouts move the levers the engine already reads (Danny's answer 4, #1027): no new effect.
/// They are for this game only: Continue puts every lever back.
/// </summary>
public sealed partial class LiveGameViewModel
{
    private readonly List<ActiveShout> _shouts = new();
    private readonly HashSet<Shout> _newShouts = new();
    private TacticSnapshot? _homeSnap, _awaySnap;

    private ObservableCollection<ShoutRow>? _shoutRows;
    public ObservableCollection<ShoutRow> ShoutRows => _shoutRows ??= new(Shouts.All.Select(i => new ShoutRow { Info = i, Toggle = ToggleShoutCommand }));
    /// <summary>The shout bar folds away to its header (FM's mentality picker style).</summary>
    [ObservableProperty] private bool _shoutsOpen = true;
    public string ShoutsHeader => _shouts.Count == 0 ? "Shouts" : $"Shouts · {_shouts.Count} on";
    /// <summary>The active shouts, as chips under the bar.</summary>
    public string ActiveShoutsLine => string.Join(" · ", _shouts.Select(a => Shouts.Info(a.Shout).Label));
    public bool HasActiveShouts => _shouts.Count > 0;
    public IReadOnlyList<ActiveShout> ActiveShouts => _shouts;
    /// <summary>The subs waiting for the next dead ball ("Smith, Jones in at the next dead ball"), or "".</summary>
    [ObservableProperty] private string _subChip = "";
    public bool HasSubChip => SubChip.Length > 0;
    partial void OnSubChipChanged(string value) => OnPropertyChanged(nameof(HasSubChip));

    [RelayCommand] private void ToggleShouts() => ShoutsOpen = !ShoutsOpen;

    /// <summary>Turn a shout on (cancelling its opposite) or off. It counts from the next possession the engine plays.</summary>
    [RelayCommand]
    public void ToggleShout(string key)
    {
        if (Complete || !int.TryParse(key, out int i) || i < 0 || i >= Shouts.All.Count) return;
        var s = (Shout)i;
        var info = Shouts.Info(s);
        var t = Us.Tactic;
        var before = TacticSnapshot.Of(t);
        bool on = Shouts.Toggle(t, _shouts, s);
        // Review (run 150): a shout that moved nothing (its lever already at the limit) isn't announced.
        bool moved = _shouts.FirstOrDefault(a => a.Shout == s) is { } act && (act.Moved != 0 || act.WasDoubling == false);
        if (on && moved) _newShouts.Add(s); else _newShouts.Remove(s);
        if (info.Opposite is { } opp) _newShouts.Remove(opp);
        foreach (var row in ShoutRows) row.IsOn = _shouts.Any(a => a.Shout == row.Info.Shout);
        Status = on ? $"Shout: {info.Label} ({LeverWords(before, TacticSnapshot.Of(t))}). It counts from the next possession."
                    : $"{info.Label} cancelled ({LeverWords(before, TacticSnapshot.Of(t))}).";
        Log.Insert(0, $"{_live.PeriodText} {_live.ClockText}  >> {Us.Name}: {(on ? info.Label : info.Label + " off")}");
        OnPropertyChanged(nameof(ShoutsHeader)); OnPropertyChanged(nameof(ActiveShoutsLine)); OnPropertyChanged(nameof(HasActiveShouts));
        // The drawer's sliders read the same levers.
        foreach (var n in new[] { nameof(Pace), nameof(ThreeEmphasis), nameof(CrashGlass), nameof(Aggression), nameof(StarUsage), nameof(DoubleStar) }) OnPropertyChanged(n);
    }

    /// <summary>What a shout moved, in the drawer's words ("pace 99 → 105").</summary>
    public static string LeverWords(TacticSnapshot a, TacticSnapshot b)
    {
        var w = new List<string>();
        if (a.Pace != b.Pace) w.Add($"pace {a.Pace:0} → {b.Pace:0}");
        if (a.Threes != b.Threes) w.Add($"three-point emphasis {a.Threes} → {b.Threes}");
        if (a.Glass != b.Glass) w.Add($"offensive glass {a.Glass} → {b.Glass}");
        if (a.Aggression != b.Aggression) w.Add($"defensive aggression {a.Aggression} → {b.Aggression}");
        if (a.Star != b.Star) w.Add($"star usage {a.Star} → {b.Star}");
        if (a.Double != b.Double) w.Add(b.Double ? "doubling their best scorer" : "no double");
        return w.Count == 0 ? "already at the limit" : string.Join(", ", w);
    }

    private void InitShouts()
    {
        _homeSnap = TacticSnapshot.Of(HomeTeam.Tactic);
        _awaySnap = TacticSnapshot.Of(AwayTeam.Tactic);
    }

    /// <summary>What both benches changed since the last possession, as commentary lines (said as the next one starts).</summary>
    private List<string> TakeChangeNotes()
    {
        var notes = new List<string>();
        if (_homeSnap == null || _awaySnap == null) InitShouts();
        foreach (var home in new[] { false, true })
        {
            var team = home ? HomeTeam : AwayTeam;
            var snap = home ? _homeSnap! : _awaySnap!;
            var now = TacticSnapshot.Of(team.Tactic);
            var said = now.ChangesFrom(snap, team == Us ? _newShouts : Array.Empty<Shout>());
            if (TacticSnapshot.Line(Short(team), said) is { } line)
            {
                notes.Add(line);
                Log.Insert(0, $"{_live.PeriodText} {_live.ClockText}  >> {line}");
            }
            if (home) _homeSnap = now; else _awaySnap = now;
        }
        _newShouts.Clear();
        return notes;
    }

    /// <summary>The subs chip: who comes in at the next dead ball.</summary>
    private void UpdateSubChip()
    {
        if (Complete || !_live.LineupPending(Us)) { SubChip = ""; return; }
        var floor = (_live.Result.Home.Team == Us ? _live.HomeOnCourt : _live.AwayOnCourt).ToHashSet();
        var ins = Lineup.Select(s => s.Selected).OfType<Player>().Where(p => !floor.Contains(p)).Select(p => p.LastName).ToList();
        SubChip = ins.Count == 0 ? "Lineup change at the next dead ball" : $"{string.Join(", ", ins)} {(ins.Count == 1 ? "checks" : "check")} in at the next dead ball";
    }

    /// <summary>At the final the shouts come off: your saved tactics are as they were before the game's shouts.</summary>
    public void ClearShouts()
    {
        Shouts.ClearAll(Us.Tactic, _shouts);
        _newShouts.Clear();
        foreach (var row in ShoutRows) row.IsOn = false;
        OnPropertyChanged(nameof(ShoutsHeader)); OnPropertyChanged(nameof(ActiveShoutsLine)); OnPropertyChanged(nameof(HasActiveShouts));
    }
}
