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

    partial void OnMinutesChanged(double value) { OnPropertyChanged(nameof(MinutesText)); Owner.Recalc(); }
}

public sealed partial class StarterSlot : ObservableObject
{
    public required Position Position { get; init; }
    public RotationViewModel Owner { get; init; } = null!;
    public string Label => Position.ToString();
    public ObservableCollection<Player> Options { get; } = new();
    [ObservableProperty] private Player? _selected;
    partial void OnSelectedChanged(Player? value) => Owner.StartersChanged();
}

public sealed partial class RotationViewModel : PageViewModel
{
    public override string Title => "Rotation & Tactics";
    public ObservableCollection<RotationRow> Rows { get; } = new();
    public ObservableCollection<StarterSlot> Starters { get; } = new();
    [ObservableProperty] private string _totalText = "";
    [ObservableProperty] private bool _totalOk;
    [ObservableProperty] private string _status = "";

    // Tactics
    [ObservableProperty] private double _pace;
    [ObservableProperty] private double _threeEmphasis;
    [ObservableProperty] private double _crashGlass;
    [ObservableProperty] private double _aggression;
    [ObservableProperty] private double _starUsage;
    public string PaceText => $"{Pace:0} possessions / 48";
    public string ThreeText => Describe(ThreeEmphasis, "Inside-out", "Balanced", "Let it fly");
    public string CrashText => Describe(CrashGlass, "Get back", "Balanced", "Crash the glass");
    public string AggressionText => Describe(Aggression, "Contain", "Balanced", "Gamble");
    public string StarText => Describe(StarUsage, "Share the ball", "Balanced", "Feed the stars");

    private bool _loading;

    public RotationViewModel(MainViewModel shell) : base(shell) { }

    partial void OnPaceChanged(double v) { OnPropertyChanged(nameof(PaceText)); Apply(); }
    partial void OnThreeEmphasisChanged(double v) { OnPropertyChanged(nameof(ThreeText)); Apply(); }
    partial void OnCrashGlassChanged(double v) { OnPropertyChanged(nameof(CrashText)); Apply(); }
    partial void OnAggressionChanged(double v) { OnPropertyChanged(nameof(AggressionText)); Apply(); }
    partial void OnStarUsageChanged(double v) { OnPropertyChanged(nameof(StarText)); Apply(); }

    private static string Describe(double v, string low, string mid, string high) => v < 35 ? low : v > 65 ? high : mid;

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
                Player = p, Owner = this, Healthy = healthy,
                Health = healthy ? "" : $"Out {p.Injury!.DaysOut(Session.Today)}d",
                IsStarter = chart.Starters.Contains(p.Id),
                Minutes = chart.MinutesTargets.GetValueOrDefault(p.Id),
            });
        }

        Starters.Clear();
        var positions = Enum.GetValues<Position>();
        for (int i = 0; i < 5; i++)
        {
            var slot = new StarterSlot { Position = positions[i], Owner = this };
            foreach (var p in team.Roster.OrderByDescending(p => p.Overall)) slot.Options.Add(p);
            slot.Selected = i < chart.Starters.Count ? team.Roster.FirstOrDefault(p => p.Id == chart.Starters[i]) : null;
            Starters.Add(slot);
        }

        var t = team.Tactic;
        Pace = t.Pace; ThreeEmphasis = t.ThreePointEmphasis; CrashGlass = t.CrashGlass; Aggression = t.DefensiveAggression; StarUsage = t.StarUsage;
        _loading = false;
        Recalc();
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

    public void StartersChanged()
    {
        if (_loading) return;
        var chart = Session.HumanTeam.DepthChart!;
        var ids = Starters.Select(s => s.Selected?.Id).ToList();
        if (ids.Any(id => id == null) || ids.Distinct().Count() < 5) { Status = "Pick five different starters."; return; }
        chart.Starters = ids.Select(id => id!.Value).ToList();
        foreach (var row in Rows) row.IsStarter = chart.Starters.Contains(row.Player.Id);
        Status = "";
        Recalc();
    }

    public void Recalc()
    {
        if (_loading) return;
        var chart = Session.HumanTeam.DepthChart!;
        chart.MinutesTargets = Rows.Where(r => r.Minutes > 0).ToDictionary(r => r.Player.Id, r => r.Minutes);
        double total = Rows.Sum(r => r.Minutes);
        TotalOk = Math.Abs(total - 240) <= 3;
        TotalText = $"{total:0} of 240 minutes" + (TotalOk ? "" : total > 240 ? " (over; the engine scales everyone down)" : " (under; the engine scales everyone up)");
        var starterNoMinutes = Rows.Where(r => r.IsStarter && r.Minutes < 20).Select(r => r.Player.LastName).ToList();
        if (starterNoMinutes.Count > 0) Status = $"Starters with under 20 minutes: {string.Join(", ", starterNoMinutes)}";
        else if (Rows.Count(r => r.Minutes > 0) < 8) Status = "Give at least eight players minutes.";
        else Status = "";
        Session.SaveNow();
    }

    private void Apply()
    {
        if (_loading) return;
        var t = Session.HumanTeam.Tactic;
        t.Pace = Pace; t.ThreePointEmphasis = (int)ThreeEmphasis; t.CrashGlass = (int)CrashGlass; t.DefensiveAggression = (int)Aggression; t.StarUsage = (int)StarUsage;
    }

    [RelayCommand]
    public void AutoRotation()
    {
        Session.HumanTeam.DepthChart = Suggest(Session.HumanTeam, Session.Today);
        Refresh();
    }

    [RelayCommand]
    public void ResetTactics()
    {
        Session.HumanTeam.Tactic = new Tactic();
        Refresh();
    }
}
