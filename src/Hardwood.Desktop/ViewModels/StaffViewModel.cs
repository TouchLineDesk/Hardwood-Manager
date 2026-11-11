using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.ViewModels;

public sealed class StaffRow
{
    public required Staff Staff { get; init; }
    public string Role => Staff.RoleName(Staff.Role);
    public string Name => Staff.Name;
    public int Age { get; init; }
    public int Key => Staff.KeyAttribute;
    public int Coaching => Staff.Coaching;
    public int Development => Staff.Development;
    public int Fitness => Staff.Fitness;
    public int Medical => Staff.Medical;
    public int Scouting => Staff.Scouting;
    /// <summary>What he'd cost you (market staff abroad are paid on your league's scale when hired).</summary>
    public long Pay { get; init; } = -1;
    public string Salary => (Pay >= 0 ? Pay : Staff.Salary) is var v && v >= 1_000_000 ? $"${v / 1e6:F2}M" : $"${v / 1e3:F0}K";
    public string Contract => Staff.ContractYears > 0 ? $"{Staff.ContractYears} yr" : "expiring";
}

public sealed partial class FocusRow : ObservableObject
{
    public required Player Player { get; init; }
    public StaffViewModel Owner { get; init; } = null!;
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public string Ability => $"{Player.CurrentAbility} / {Player.PotentialAbility}";
    [ObservableProperty] private string _focus = "Balanced";
    partial void OnFocusChanged(string value) => Owner.FocusChanged(this);
}

public sealed partial class StaffViewModel : PageViewModel
{
    public override string Title => "Staff & Training";
    public ObservableCollection<StaffRow> Ours { get; } = new();
    public ObservableCollection<StaffRow> Market { get; } = new();
    public ObservableCollection<FocusRow> Players { get; } = new();
    public string[] RoleFilters { get; } = { "All roles", "Assistant coach", "Development coach", "Strength & conditioning", "Physio", "Head scout" };
    public string[] FocusOptions { get; } = { "Balanced", "Shooting", "Playmaking", "Defense", "Mental", "Physical" };
    [ObservableProperty] private string _roleFilter = "All roles";
    [ObservableProperty] private StaffRow? _selectedOurs;
    [ObservableProperty] private StaffRow? _selectedMarket;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _effects = "";
    /// <summary>Roadmap 1: what your staff do for you, as stat chips (the same numbers the Effects line used to list).</summary>
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _chips = new();
    public Avalonia.Media.Color Accent => ThemeService.AccentFor(Session.HumanTeam);
    [ObservableProperty] private double _intensity;
    [ObservableProperty] private string _intensityText = "";
    private bool _loading;

    public StaffViewModel(MainViewModel shell) : base(shell) { }

    partial void OnRoleFilterChanged(string value) => FillMarket();
    partial void OnIntensityChanged(double value)
    {
        if (_loading) return;
        Session.HumanTeam.Training.Intensity = (int)value;
        IntensityText = Describe((int)value);
        UpdateEffects();
        Session.SaveNow();
    }

    private static string Describe(int i) => i < 30 ? "Light: fewer injuries, slower growth" : i > 70 ? "Hard: faster growth, more injuries" : "Normal";

    public override void Refresh()
    {
        _loading = true;
        var s = Session; var team = s.HumanTeam;
        Ours.Clear();
        foreach (var role in Enum.GetValues<StaffRole>())
        {
            var st = team.Staff.FirstOrDefault(x => x.Role == role);
            if (st != null) Ours.Add(new StaffRow { Staff = st, Age = st.Age(s.Today) });
        }
        FillMarket();
        Players.Clear();
        foreach (var p in team.Roster.OrderByDescending(p => p.PotentialAbility - p.CurrentAbility).ThenByDescending(p => p.CurrentAbility))
            Players.Add(new FocusRow { Player = p, Owner = this, Age = p.Age(s.Today), Focus = team.Training.Focus.TryGetValue(p.Id, out var f) ? f.ToString() : "Balanced" });
        Intensity = team.Training.Intensity;
        IntensityText = Describe(team.Training.Intensity);
        UpdateEffects();
        _loading = false;
    }

    private void FillMarket()
    {
        Market.Clear();
        foreach (var st in Session.League.FreeStaff.Where(st => RoleFilter == "All roles" || Staff.RoleName(st.Role) == RoleFilter).OrderByDescending(st => st.KeyAttribute))
            Market.Add(new StaffRow { Staff = st, Age = st.Age(Session.Today), Pay = Session.StaffPayFor(st) });
    }

    private void UpdateEffects()
    {
        var t = Session.HumanTeam;
        var missing = Enum.GetValues<StaffRole>().Where(r => t.Staff.All(s => s.Role != r)).Select(Staff.RoleName).ToList();
        Effects = $"Training quality {StaffEffects.TrainingQuality(t):0.0}/20 · injury risk ×{StaffEffects.InjuryRiskFactor(t):0.00} · recovery time ×{StaffEffects.RecoveryFactor(t):0.00} · scouting knowledge {StaffEffects.ScoutingKnowledge(t) * 100:F0}% · staff payroll ${t.Staff.Sum(s => s.Salary) / 1e6:F2}M (not counted against the cap)"
                  + (missing.Count > 0 ? $"\nVacant: {string.Join(", ", missing)} — empty roles hurt." : "");
        double tq = StaffEffects.TrainingQuality(t), inj = StaffEffects.InjuryRiskFactor(t), rec = StaffEffects.RecoveryFactor(t), sc = StaffEffects.ScoutingKnowledge(t);
        Chips = new List<Hardwood.Desktop.Controls.Chip>
        {
            new("Training", $"{tq:0.0}", "quality out of 20", tq >= 14 ? "good" : tq < 9 ? "warn" : ""),
            new("Injury risk", $"×{inj:0.00}", inj <= 1 ? "below normal" : "above normal", inj <= 0.95 ? "good" : inj > 1.05 ? "bad" : ""),
            new("Recovery", $"×{rec:0.00}", rec <= 1 ? "faster than normal" : "slower than normal", rec <= 0.95 ? "good" : rec > 1.05 ? "bad" : ""),
            new("Scouting", $"{sc * 100:F0}%", "of other players' ratings known", sc >= 0.8 ? "good" : sc < 0.5 ? "warn" : ""),
            new("Staff payroll", PlayerViewModel.Money(t.Staff.Sum(s => s.Salary)), "not counted against the cap", "team"),
            new("Vacant roles", missing.Count.ToString(), missing.Count == 0 ? "every role filled" : string.Join(", ", missing), missing.Count > 0 ? "bad" : "good"),
        };
    }

    public void FocusChanged(FocusRow row)
    {
        if (_loading) return;
        var focus = Session.HumanTeam.Training.Focus;
        if (row.Focus == "Balanced") focus.Remove(row.Player.Id);
        else focus[row.Player.Id] = Enum.Parse<AttrGroup>(row.Focus);
        Session.SaveNow();
    }

    [RelayCommand]
    public void Hire()
    {
        if (SelectedMarket == null) return;
        var st = SelectedMarket.Staff;
        var replaced = Session.HumanTeam.Staff.FirstOrDefault(x => x.Role == st.Role);
        Session.HireStaff(st);
        Status = $"Hired {st.Name} as {Staff.RoleName(st.Role).ToLower()}." + (replaced != null ? $" {replaced.Name} released." : "");
        Shell.AfterTick();
    }

    [RelayCommand]
    public void Fire()
    {
        if (SelectedOurs == null) return;
        Session.FireStaff(SelectedOurs.Staff);
        Status = $"Released {SelectedOurs.Name}.";
        Shell.AfterTick();
    }
}
