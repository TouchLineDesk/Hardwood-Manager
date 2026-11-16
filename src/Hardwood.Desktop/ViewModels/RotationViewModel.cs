using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.ViewModels;

public sealed partial class RotationRow : ObservableObject
{
    public required Player Player { get; init; }
    public RotationViewModel Owner { get; init; } = null!;
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition + (Player.SecondaryPosition is { } s ? "/" + s : "");
    public double Ovr => Math.Round(Player.Overall, 1);
    public string Health { get; init; } = "";
    public bool Healthy { get; init; }
    [ObservableProperty] private bool _isStarter;
    [ObservableProperty] private double _minutes;
    public string MinutesText => Minutes.ToString("0");

    // Run 73 (review #13 rank 10): the depth chart's cards, chips and minute bars.
    public Team? Team { get; init; }
    public string ShortName => Player.FirstName.Length > 0 ? $"{Player.FirstName[0]}. {Player.LastName}" : Player.LastName;
    /// <summary>The line under a starter's name on the court: rating and minutes, or why he can't play.</summary>
    public string CardLine => Healthy ? $"{Ovr:0.0} OVR · {Minutes:0} min" : $"{Ovr:0.0} OVR · {Health}";
    /// <summary>A bench chip: who, where, and how much he plays.</summary>
    public string ChipLine => $"{ShortName} · {Pos} · {Ovr:0.0} · " + (!Healthy ? Health : Minutes > 0 ? $"{Minutes:0} min" : "DNP");
    public string ChipTip => !Healthy ? $"{Name} is out: {Player.Injury?.Name}. He can't start until he's back."
        : "Pick him to start: at the spot you picked on the court, or at his own position. He takes the starter's minutes.";
    public Avalonia.Media.IBrush BarBrush => !Healthy ? Bad : IsStarter ? new Avalonia.Media.SolidColorBrush(Hardwood.Desktop.ThemeService.Accent) : Bench;
    private static readonly Avalonia.Media.IBrush Bad = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F85149"));
    private static readonly Avalonia.Media.IBrush Bench = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#6E7C8F"));
    public string Role => IsStarter ? "Starter" : Minutes > 0 ? "Bench" : "Out of the rotation";

    [RelayCommand] private void More() => Minutes = Math.Min(40, Minutes + 2);
    [RelayCommand] private void Less() => Minutes = Math.Max(0, Minutes - 2);

    partial void OnMinutesChanged(double value)
    {
        OnPropertyChanged(nameof(MinutesText)); OnPropertyChanged(nameof(CardLine)); OnPropertyChanged(nameof(ChipLine)); OnPropertyChanged(nameof(Role));
        Owner.Recalc();
    }
    partial void OnIsStarterChanged(bool value) { OnPropertyChanged(nameof(BarBrush)); OnPropertyChanged(nameof(Role)); }
}

public sealed partial class StarterSlot : ObservableObject
{
    public required Position Position { get; init; }
    public RotationViewModel Owner { get; init; } = null!;
    public string Label => Position.ToString();
    public ObservableCollection<Player> Options { get; } = new();
    [ObservableProperty] private Player? _selected;
    partial void OnSelectedChanged(Player? value) => Owner.StartersChanged();

    // Run 73: the slot as a card on the half court.
    [ObservableProperty] private RotationRow? _row;
    [ObservableProperty] private bool _picked;
    public double Left { get; init; }
    public double Top { get; init; }
    public bool Filled => Row != null;
    public bool Empty => Row == null;
    public string Tip => Row == null ? $"No {Label}: pick a bench player to start here."
        : $"{Row.Name} starts at {Label}. Click, then a bench player to swap him out, or another starter to swap spots.";
    partial void OnRowChanged(RotationRow? value) { OnPropertyChanged(nameof(Filled)); OnPropertyChanged(nameof(Empty)); OnPropertyChanged(nameof(Tip)); }
}

public sealed partial class RotationViewModel : PageViewModel
{
    public override string Title => "Rotation";
    public ObservableCollection<RotationRow> Rows { get; } = new();
    public ObservableCollection<StarterSlot> Starters { get; } = new();
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private bool _totalOk;
    [ObservableProperty] private string _status = "";

    private bool _loading;

    // Run 73 (review #13 rank 10): the depth chart. The five on a half court, the bench as chips, minutes as bars;
    // the table one switch away (the same words as Roster, Schedule and the market).
    public string[] Views { get; } = { "Cards", "Full table" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowCards), nameof(ShowTable))] private string _view = "Cards";
    public bool ShowCards => View == "Cards";
    public bool ShowTable => !ShowCards;
    public ObservableCollection<RotationRow> Bench { get; } = new();
    /// <summary>Everyone, starters first in court order, then the bench by minutes: the minute bars.</summary>
    public ObservableCollection<RotationRow> MinuteRows { get; } = new();
    [ObservableProperty] private Avalonia.Media.Color _accent = Avalonia.Media.Color.Parse("#F0A33A");
    [ObservableProperty] private string _hint = "";
    [ObservableProperty] private string _shareText = "";
    public StarterSlot? PickedSlot => Starters.FirstOrDefault(s => s.Picked);

    /// <summary>The court the cards sit on (50 × 47 ft, drawn to fill it exactly) and a card's size.</summary>
    public const double CourtWidth = 470, CourtHeight = CourtWidth * 47 / 50, CardWidth = 168, CardHeight = 100;
    /// <summary>Where each position stands, in feet from the left sideline and the baseline: the point guard up top,
    /// the wings out wide, the bigs on the blocks.</summary>
    private static readonly (double x, double y)[] Spots = { (25, 37), (43, 24), (7, 24), (36, 7.5), (14, 7.5) };
    public static (double left, double top) SpotFor(int slot)
    {
        var (x, y) = Spots[slot];
        double left = x / 50 * CourtWidth - CardWidth / 2, top = CourtHeight - y / 47 * CourtHeight - CardHeight / 2;
        return (Math.Clamp(left, 0, CourtWidth - CardWidth), Math.Clamp(top, 0, CourtHeight - CardHeight));
    }

    public RotationViewModel(MainViewModel shell) : base(shell) { }



    public override void Refresh()
    {
        _loading = true;
        var team = Session.HumanTeam;
        var chart = team.DepthChart ?? Suggest(team);
        team.DepthChart = chart;

        Rows.Clear();
        foreach (var p in team.Roster.OrderByDescending(p => chart.MinutesTargets.GetValueOrDefault(p.Id)).ThenByDescending(p => p.Overall))
        {
            bool healthy = p.IsAvailable(Session.Today);
            Rows.Add(new RotationRow
            {
                Player = p, Owner = this, Healthy = healthy, Team = team,
                Health = healthy ? "" : $"Out {p.Injury!.DaysOut(Session.Today)}d",
                IsStarter = chart.Starters.Contains(p.Id),
                Minutes = chart.MinutesTargets.GetValueOrDefault(p.Id),
            });
        }

        Starters.Clear();
        var positions = Enum.GetValues<Position>();
        for (int i = 0; i < 5; i++)
        {
            var (left, top) = SpotFor(i);
            var slot = new StarterSlot { Position = positions[i], Owner = this, Left = left, Top = top };
            foreach (var p in team.Roster.OrderByDescending(p => p.Overall)) slot.Options.Add(p);
            slot.Selected = i < chart.Starters.Count ? team.Roster.FirstOrDefault(p => p.Id == chart.Starters[i]) : null;
            Starters.Add(slot);
        }

        _loading = false;
        Accent = ThemeService.AccentFor(team);
        Recalc();
        RebuildDepth();
    }

    /// <summary>Puts each starter on his spot, the rest on the bench, and orders the minute bars.</summary>
    private void RebuildDepth()
    {
        foreach (var slot in Starters) slot.Row = slot.Selected == null ? null : Rows.FirstOrDefault(r => r.Player == slot.Selected);
        Bench.Clear();
        foreach (var r in Rows.Where(r => !r.IsStarter).OrderByDescending(r => r.Healthy).ThenByDescending(r => r.Minutes).ThenByDescending(r => r.Ovr)) Bench.Add(r);
        MinuteRows.Clear();
        foreach (var slot in Starters) if (slot.Row != null) MinuteRows.Add(slot.Row);
        foreach (var r in Bench) MinuteRows.Add(r);
        Hint = PickedSlot is { } p
            ? p.Row == null ? $"Pick a bench player to start at {p.Label}." : $"{p.Row.ShortName} picked at {p.Label}: now a bench player to start in his place, or another starter to swap spots."
            : "Click a starter on the court, then a bench player to swap them (he takes the starter's minutes). A bench player on his own goes in at his position.";
    }

    /// <summary>Click a starter's card: pick it; click it again to let go; click another starter to swap their spots.</summary>
    public void PickSlot(StarterSlot slot)
    {
        var picked = PickedSlot;
        if (picked == slot) { slot.Picked = false; RebuildDepth(); return; }
        if (picked != null && picked.Selected != null && slot.Selected != null)
        {
            var a = picked.Selected; var b = slot.Selected;
            _loading = true;
            picked.Selected = b; slot.Selected = a;
            picked.Picked = false;
            _loading = false;
            if (!StartersChanged())
            {
                // Code review (run 73): the five weren't valid (a duplicate pick in the table): undo, say why.
                _loading = true; picked.Selected = a; slot.Selected = b; _loading = false;
                RebuildDepth();
                Status = "Pick five different starters first (Full table), then swap.";
                return;
            }
            string warn = Status;
            Status = $"{a.LastName} and {b.LastName} swap spots: {a.LastName} at {slot.Label}, {b.LastName} at {picked.Label}." + (warn.Length > 0 ? " " + warn : "");
            return;
        }
        foreach (var s in Starters) s.Picked = s == slot;
        RebuildDepth();
    }

    /// <summary>Click a bench chip: he starts at the picked spot (or his own position) and takes that starter's minutes.</summary>
    public void SwapIn(RotationRow bench)
    {
        if (bench.IsStarter) return;
        if (!bench.Healthy) { Status = $"{bench.Name} is out ({bench.Health}): he can't start until he's back."; return; }
        var slot = PickedSlot
                   ?? Starters.FirstOrDefault(s => s.Selected == null)
                   ?? Starters.FirstOrDefault(s => s.Position == bench.Player.PrimaryPosition)
                   ?? Starters.FirstOrDefault(s => s.Position == bench.Player.SecondaryPosition);
        if (slot == null) { Status = $"Pick a spot on the court first, then {bench.ShortName}."; return; }
        var outRow = slot.Row;
        double minIn = bench.Minutes, minOut = outRow?.Minutes ?? 0;
        _loading = true;
        slot.Selected = bench.Player;
        slot.Picked = false;
        if (outRow != null) { outRow.Minutes = minIn; bench.Minutes = minOut; }
        _loading = false;
        if (!StartersChanged())
        {
            // Code review (run 73): the five weren't valid (a duplicate pick in the table): undo the swap and its minutes.
            _loading = true;
            slot.Selected = outRow?.Player;
            if (outRow != null) { outRow.Minutes = minOut; bench.Minutes = minIn; }
            _loading = false;
            RebuildDepth();
            Status = "Pick five different starters first (Full table), then swap.";
            return;
        }
        string warn = Status;
        Status = (outRow == null
            ? $"{bench.Name} starts at {slot.Label}."
            : $"{bench.Name} starts at {slot.Label} in place of {outRow.Name}; they swap minutes ({minOut:0} and {minIn:0}).") + (warn.Length > 0 ? " " + warn : "");
    }

    /// <summary>A sensible default rotation: best healthy player per position starts; minutes by quality.</summary>
    public static DepthChart Suggest(Team team, DateOnly? on = null)
    {
        var chart = new DepthChart();
        var pool = team.Roster.Where(p => on == null || p.IsAvailable(on.Value)).OrderByDescending(p => p.Overall).ToList();
        if (pool.Count < 8) pool = team.Roster.OrderByDescending(p => p.Overall).ToList();
        var starters = new List<Player>();
        foreach (var pos in Enum.GetValues<Position>())
        {
            var best = pool.FirstOrDefault(p => !starters.Contains(p) && (p.PrimaryPosition == pos || p.SecondaryPosition == pos));
            if (best != null) starters.Add(best);
        }
        foreach (var p in pool) { if (starters.Count >= 5) break; if (!starters.Contains(p)) starters.Add(p); }
        // Order starters by position.
        var ordered = new List<Player>();
        foreach (var pos in Enum.GetValues<Position>())
        {
            var pick = starters.FirstOrDefault(p => !ordered.Contains(p) && p.PrimaryPosition == pos) ?? starters.FirstOrDefault(p => !ordered.Contains(p) && p.SecondaryPosition == pos);
            if (pick != null) ordered.Add(pick);
        }
        foreach (var p in starters) if (!ordered.Contains(p)) ordered.Add(p);
        chart.Starters = ordered.Select(p => p.Id).ToList();

        double[] starterMins = { 36, 34, 32, 30, 28 };
        double[] benchMins = { 24, 20, 16, 12, 8 };
        var s = starters.OrderByDescending(p => p.Overall).ToList();
        for (int i = 0; i < s.Count; i++) chart.MinutesTargets[s[i].Id] = starterMins[i];
        var bench = pool.Where(p => !starters.Contains(p)).Take(5).ToList();
        for (int i = 0; i < bench.Count; i++) chart.MinutesTargets[bench[i].Id] = benchMins[i];
        return chart;
    }

    /// <summary>True when the five picks are valid and now the chart's starters.</summary>
    public bool StartersChanged()
    {
        if (_loading) return false;
        var chart = Session.HumanTeam.DepthChart!;
        var ids = Starters.Select(s => s.Selected?.Id).ToList();
        if (ids.Any(id => id == null) || ids.Distinct().Count() < 5)
        {
            Status = "Pick five different starters.";
            // Code review (run 73): the cards follow the picks even when they're not valid yet, so they show what the table shows.
            RebuildDepth();
            return false;
        }
        chart.Starters = ids.Select(id => id!.Value).ToList();
        foreach (var row in Rows) row.IsStarter = chart.Starters.Contains(row.Player.Id);
        Status = "";
        Recalc();
        RebuildDepth();
        return true;
    }

    public void Recalc()
    {
        if (_loading) return;
        var chart = Session.HumanTeam.DepthChart!;
        chart.MinutesTargets = Rows.Where(r => r.Minutes > 0).ToDictionary(r => r.Player.Id, r => r.Minutes);
        double total = Rows.Sum(r => r.Minutes);
        TotalOk = Math.Abs(total - 240) <= 3;
        double starters = Rows.Where(r => r.IsStarter).Sum(r => r.Minutes);
        ShareText = total > 0 ? $"Starters {starters:0} min ({starters / total * 100:0}%) · bench {total - starters:0} min · {Rows.Count(r => r.Minutes > 0)} players in the rotation" : "";
        TotalText = $"{total:0} of 240 minutes" + (TotalOk ? "" : total > 240 ? " (over; the engine scales everyone down)" : " (under; the engine scales everyone up)");
        var starterNoMinutes = Rows.Where(r => r.IsStarter && r.Minutes < 20).Select(r => r.Player.LastName).ToList();
        if (starterNoMinutes.Count > 0) Status = $"Starters with under 20 minutes: {string.Join(", ", starterNoMinutes)}";
        else if (Rows.Count(r => r.Minutes > 0) < 8) Status = "Give at least eight players minutes.";
        else Status = "";
        Session.SaveNow();
    }

    [RelayCommand]
    public void AutoRotation()
    {
        var team = Session.HumanTeam;
        string before = ChartKey(team.DepthChart);
        team.DepthChart = Suggest(team, Session.Today);
        bool same = ChartKey(team.DepthChart) == before;
        Refresh();
        // Bug list H2: say what happened, and say so when the suggestion was already in place.
        if (Status.Length == 0)
            Status = same ? "Already the assistant's rotation: the same starters and minutes. Nothing changed."
                : "The assistant's rotation is in: his starters and minutes.";
    }

    private static string ChartKey(DepthChart? c) => c == null ? "" :
        string.Join(",", c.Starters) + "|" + string.Join(",", c.MinutesTargets.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{Math.Round(kv.Value)}"));

}
