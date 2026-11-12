using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.ViewModels;

/// <summary>An offensive system or defensive scheme as a pickable card, with how well the roster fits it.</summary>
public sealed class SystemOption
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required double Fit { get; init; }
    public OffensiveSystem? System { get; init; }
    public DefensiveScheme? Scheme { get; init; }
    /// <summary>How it fares against what the next opponent is likely to run (critic review: show the matchup before you choose).</summary>
    public double Matchup { get; init; }
    public bool Recommended { get; init; }
    /// <summary>Why it fits or doesn't (roadmap 4): each ingredient against an average rotation, naming the player who supplies it.</summary>
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
    public bool HasReasons => Reasons.Count > 0;
    public string FitText => (System is OffensiveSystem.Balanced || Scheme is DefensiveScheme.Man ? "baseline" : $"fit {TacticsViewModel.StarText(Fit * 5)}")
        + (Matchup >= 0.02 ? " · good matchup" : Matchup <= -0.02 ? " · poor matchup" : "") + (Recommended ? " · assistant's pick" : "");
    public override string ToString() => Name;
}

public sealed class RoleChoice
{
    public required Role Role { get; init; }
    public required double Stars { get; init; }
    public string Label => $"{Roles.Info(Role).Name}  {TacticsViewModel.StarText(Stars)}";
    public override string ToString() => Label;
}

public sealed partial class RoleRow : ObservableObject
{
    public required Player Player { get; init; }
    public required TacticsViewModel Owner { get; init; }
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition + (Player.SecondaryPosition is { } s ? "/" + s : "");
    public double Minutes { get; init; }
    public string MinutesText => Minutes > 0 ? $"{Minutes:0}" : "-";
    public List<RoleChoice> Choices { get; init; } = new();
    public string Best { get; init; } = "";
    [ObservableProperty] private RoleChoice? _selected;
    public string Summary => Selected != null ? Roles.Info(Selected.Role).Summary : "";
    partial void OnSelectedChanged(RoleChoice? value) { OnPropertyChanged(nameof(Summary)); Owner.RoleChanged(this); }
}

public sealed class StopperOption
{
    public Player? Player { get; init; }
    public string Label => Player == null ? "Normal matchups" : $"{Player.Name} ({Player.PrimaryPosition}, perimeter D {Player.Attributes[Attr.PerimeterDefense]})";
    public override string ToString() => Label;
}

/// <summary>
/// The Tactics page (decisions #58-61): offensive system and defensive scheme with roster fit, the fine-tuning sliders,
/// player roles with suitability stars, who guards their star and whether to double him, saved presets with a late-game
/// switch, and the assistant's scouting report on the next opponent.
/// </summary>
public sealed partial class TacticsViewModel : PageViewModel
{
    public override string Title => "Tactics";
    public TacticsViewModel(MainViewModel shell) : base(shell) { }

    public static string StarText(double stars)
    {
        stars = Math.Clamp(Math.Round(stars * 2) / 2, 0, 5);
        int full = (int)stars; bool half = stars - full >= 0.5;
        return new string('★', full) + (half ? "½" : "") + new string('☆', 5 - full - (half ? 1 : 0));
    }

    private bool _loading;

    public ObservableCollection<SystemOption> Systems { get; } = new();
    public ObservableCollection<SystemOption> Schemes { get; } = new();
    [ObservableProperty] private SystemOption? _selectedSystem;
    [ObservableProperty] private SystemOption? _selectedScheme;
    partial void OnSelectedSystemChanged(SystemOption? value) { if (!_loading && value?.System is { } s) { Tac.System = s; Changed(); } }
    partial void OnSelectedSchemeChanged(SystemOption? value) { if (!_loading && value?.Scheme is { } d) { Tac.Scheme = d; Changed(); } }

    // Fine-tuning sliders (moved from the Rotation page).
    [ObservableProperty] private double _pace;
    [ObservableProperty] private double _threeEmphasis;
    [ObservableProperty] private double _crashGlass;
    [ObservableProperty] private double _aggression;
    [ObservableProperty] private double _starUsage;
    public string PaceText => Hardwood.Core.Engine.Tactics.PaceShift(Tac.System) is var shift && shift != 0
        ? $"{Pace:0} possessions / 48 ({shift:+0.#;-0.#} from the system)" : $"{Pace:0} possessions / 48";
    public string ThreeText => Describe(ThreeEmphasis, "Inside-out", "Balanced", "Let it fly");
    public string CrashText => Describe(CrashGlass, "Get back", "Balanced", "Crash the glass");
    public string AggressionText => Describe(Aggression, "Contain", "Balanced", "Gamble");
    public string StarText2 => Describe(StarUsage, "Share the ball", "Balanced", "Feed the stars");
    partial void OnPaceChanged(double value) { OnPropertyChanged(nameof(PaceText)); ApplySliders(); }
    partial void OnThreeEmphasisChanged(double value) { OnPropertyChanged(nameof(ThreeText)); ApplySliders(); }
    partial void OnCrashGlassChanged(double value) { OnPropertyChanged(nameof(CrashText)); ApplySliders(); }
    partial void OnAggressionChanged(double value) { OnPropertyChanged(nameof(AggressionText)); ApplySliders(); }
    partial void OnStarUsageChanged(double value) { OnPropertyChanged(nameof(StarText2)); ApplySliders(); }
    private static string Describe(double v, string low, string mid, string high) => v < 35 ? low : v > 65 ? high : mid;

    public ObservableCollection<RoleRow> RoleRows { get; } = new();

    public ObservableCollection<StopperOption> Stoppers { get; } = new();
    [ObservableProperty] private StopperOption? _selectedStopper;
    [ObservableProperty] private bool _doubleStar;
    partial void OnSelectedStopperChanged(StopperOption? value) { if (!_loading) { Tac.StarStopperId = value?.Player?.Id; Changed(); } }
    partial void OnDoubleStarChanged(bool value) { if (!_loading) { Tac.DoubleStar = value; Changed(); } }
    [ObservableProperty] private bool _halftimeAdjust;
    partial void OnHalftimeAdjustChanged(bool value) { if (!_loading) { Tac.HalftimeAdjust = value; Changed(); } }
    [ObservableProperty] private bool _assistantPlans;
    partial void OnAssistantPlansChanged(bool value) { if (!_loading) { Tac.AssistantPlans = value; Changed(); } }

    public ObservableCollection<string> Presets { get; } = new();
    public ObservableCollection<string> LateGameOptions { get; } = new();
    [ObservableProperty] private string? _selectedPreset;
    [ObservableProperty] private string _newPresetName = "";
    [ObservableProperty] private string? _lateGame;
    partial void OnLateGameChanged(string? value) { if (!_loading) { Tac.LateGamePreset = value is null or NoPreset ? null : value; Changed(); } }
    private const string NoPreset = "Keep current tactics";

    [ObservableProperty] private string _opponentHeadline = "";
    [ObservableProperty] private string _scoutingReport = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _fitSummary = "";

    private Tactic Tac => Session.HumanTeam.Tactic;

    public override void Refresh()
    {
        _loading = true;
        var team = Session.HumanTeam;
        var t = team.Tactic;
        var rot = CoachAi.Rotation(team, Session.Today);

        // What the next opponent will probably run, to show each option's matchup.
        var nextOpp = Session.NextMyGame?.Opponent;
        var theirPlan = nextOpp == null ? ((OffensiveSystem, DefensiveScheme)?)null : CoachAi.TheirPlan(nextOpp, team, Session.NextMyGame?.Date ?? Session.Today);
        DefensiveScheme? theirScheme = theirPlan?.Item2;
        OffensiveSystem? theirSystem = theirPlan?.Item1;
        var bestSystem = CoachAi.BestSystem(rot, theirScheme);
        var bestScheme = CoachAi.BestScheme(rot, theirSystem);
        Systems.Clear();
        foreach (var s in Enum.GetValues<OffensiveSystem>())
            Systems.Add(new SystemOption { Name = Hardwood.Core.Model.Systems.Name(s), Description = Hardwood.Core.Model.Systems.Describe(s), Fit = Hardwood.Core.Model.Systems.Fit(s, rot), System = s,
                Reasons = Hardwood.Core.Model.Systems.Reasons(s, rot).Select(r => (r.Effect >= 0.025 ? "+ " : r.Effect <= -0.025 ? "− " : "· ") + r.Text).ToList(),
                Matchup = theirScheme is { } ts ? Hardwood.Core.Engine.Tactics.Matchup(s, ts) : 0, Recommended = s == bestSystem });
        Schemes.Clear();
        foreach (var d in Enum.GetValues<DefensiveScheme>())
            Schemes.Add(new SystemOption { Name = Hardwood.Core.Model.Systems.Name(d), Description = Hardwood.Core.Model.Systems.Describe(d), Fit = Hardwood.Core.Model.Systems.Fit(d, rot), Scheme = d,
                Reasons = Hardwood.Core.Model.Systems.Reasons(d, rot).Select(r => (r.Effect >= 0.025 ? "+ " : r.Effect <= -0.025 ? "− " : "· ") + r.Text).ToList(),
                Matchup = theirSystem is { } tsys ? -Hardwood.Core.Engine.Tactics.Matchup(tsys, d) : 0, Recommended = d == bestScheme });
        SelectedSystem = Systems.First(o => o.System == t.System);
        SelectedScheme = Schemes.First(o => o.Scheme == t.Scheme);

        Pace = t.Pace; ThreeEmphasis = t.ThreePointEmphasis; CrashGlass = t.CrashGlass; Aggression = t.DefensiveAggression; StarUsage = t.StarUsage;

        RoleRows.Clear();
        var chart = team.DepthChart;
        foreach (var p in team.Roster.OrderByDescending(p => chart?.MinutesTargets.GetValueOrDefault(p.Id) ?? 0).ThenByDescending(p => p.CurrentAbility))
        {
            var choices = Roles.All.Select(i => new RoleChoice { Role = i.Role, Stars = Roles.Stars(p, i.Role) })
                .OrderByDescending(c => Roles.Natural(p.PrimaryPosition, c.Role)).ThenByDescending(c => c.Stars).ToList();
            var current = t.RoleOf(p);
            var best = Roles.Best(p);
            var row = new RoleRow
            {
                Player = p, Owner = this, Choices = choices, Minutes = chart?.MinutesTargets.GetValueOrDefault(p.Id) ?? 0,
                Best = $"{Roles.Info(best).Name} {StarText(Roles.Stars(p, best))}",
            };
            row.Selected = choices.First(c => c.Role == current);
            RoleRows.Add(row);
        }

        Stoppers.Clear();
        Stoppers.Add(new StopperOption());
        foreach (var p in team.Roster.OrderByDescending(p => p.Attributes[Attr.PerimeterDefense])) Stoppers.Add(new StopperOption { Player = p });
        SelectedStopper = Stoppers.FirstOrDefault(s => s.Player?.Id == t.StarStopperId) ?? Stoppers[0];
        DoubleStar = t.DoubleStar;
        HalftimeAdjust = t.HalftimeAdjust;
        AssistantPlans = t.AssistantPlans;

        Presets.Clear();
        foreach (var pr in t.Presets) Presets.Add(pr.Name);
        LateGameOptions.Clear();
        LateGameOptions.Add(NoPreset);
        foreach (var pr in t.Presets) LateGameOptions.Add(pr.Name);
        LateGame = t.LateGamePreset is { } lg && t.Presets.Any(p => p.Name == lg) ? lg : NoPreset;

        var next = Session.NextMyGame;
        if (next != null)
        {
            var opp = next.Opponent;
            OpponentHeadline = $"Next: {(next.Home ? "vs" : "at")} {opp.FullName}, {next.Date:MMM d}" + (next.Competition != Session.League.Name ? $" ({next.Competition})" : "");
            ScoutingReport = CoachAi.ScoutingReport(team, opp, next.Date);
        }
        else { OpponentHeadline = "No game scheduled"; ScoutingReport = ""; }

        _loading = false;
        UpdateFitSummary();
    }

    private void UpdateFitSummary()
    {
        var rot = CoachAi.Rotation(Session.HumanTeam, Session.Today);
        double sf = Hardwood.Core.Model.Systems.Fit(Tac.System, rot), df = Hardwood.Core.Model.Systems.Fit(Tac.Scheme, rot);
        var misfits = RoleRows.Where(r => r.Minutes > 0 && r.Selected != null && r.Selected.Stars < 2.5).Select(r => r.Player.LastName).ToList();
        FitSummary = $"Your rotation fits {Hardwood.Core.Model.Systems.Name(Tac.System).ToLower()} {StarText(sf * 5)} and {Hardwood.Core.Model.Systems.Name(Tac.Scheme).ToLower()} {StarText(df * 5)}."
                     + (misfits.Count > 0 ? $" Poor role fits among players who get minutes: {string.Join(", ", misfits)}." : "");
        OnPropertyChanged(nameof(PaceText));
    }

    public void RoleChanged(RoleRow row)
    {
        if (_loading || row.Selected == null) return;
        if (row.Selected.Role == Roles.Best(row.Player)) Tac.Roles.Remove(row.Player.Id);
        else Tac.Roles[row.Player.Id] = row.Selected.Role;
        Changed();
    }

    private void ApplySliders()
    {
        if (_loading) return;
        var t = Tac;
        t.Pace = Pace; t.ThreePointEmphasis = (int)ThreeEmphasis; t.CrashGlass = (int)CrashGlass; t.DefensiveAggression = (int)Aggression; t.StarUsage = (int)StarUsage;
        Changed();
    }

    private void Changed()
    {
        UpdateFitSummary();
        Session.SaveNow();
    }

    [RelayCommand]
    public void AssistantSetup()
    {
        var presets = Tac.Presets; var late = Tac.LateGamePreset;
        CoachAi.AssistantSetup(Session.HumanTeam);
        Tac.Presets = presets; Tac.LateGamePreset = late;
        Session.SaveNow();
        Refresh();
        Status = "Your assistant picked the system and scheme that suit the roster and put everyone in his best role.";
    }

    [RelayCommand]
    public void UseSuggestion()
    {
        var next = Session.NextMyGame;
        if (next == null) return;
        var team = Session.HumanTeam;
        var opp = next.Opponent;
        var rot = CoachAi.Rotation(team, next.Date);
        var (theirSystem, theirScheme) = CoachAi.TheirPlan(opp, team, next.Date);
        Tac.System = CoachAi.BestSystem(rot, theirScheme);
        Tac.Scheme = CoachAi.BestScheme(rot, theirSystem);
        var (star, dbl) = CoachAi.TheirStar(opp, next.Date);
        Tac.StarStopperId = star != null ? CoachAi.Stopper(rot)?.Id : null;
        Tac.DoubleStar = dbl;
        Session.SaveNow();
        Refresh();
        Status = $"Game plan set for {opp.Name}.";
    }

    [RelayCommand]
    public void SavePreset()
    {
        var name = NewPresetName.Trim();
        if (name.Length == 0) { Status = "Name the preset first."; return; }
        Tac.Presets.RemoveAll(p => p.Name == name);
        Tac.Presets.Add(TacticPreset.From(Tac, name));
        NewPresetName = "";
        Session.SaveNow();
        Refresh();
        Status = $"Saved \"{name}\".";
    }

    [RelayCommand]
    public void LoadPreset()
    {
        var pr = Tac.Presets.FirstOrDefault(p => p.Name == SelectedPreset);
        if (pr == null) { Status = "Pick a preset to load."; return; }
        pr.ApplyTo(Tac);
        Session.SaveNow();
        Refresh();
        Status = $"Loaded \"{pr.Name}\".";
    }

    [RelayCommand]
    public void DeletePreset()
    {
        if (SelectedPreset == null) return;
        Tac.Presets.RemoveAll(p => p.Name == SelectedPreset);
        if (Tac.LateGamePreset == SelectedPreset) Tac.LateGamePreset = null;
        Session.SaveNow();
        Refresh();
    }

    [RelayCommand]
    public void ResetSliders()
    {
        var d = new Tactic();
        Tac.Pace = d.Pace; Tac.ThreePointEmphasis = d.ThreePointEmphasis; Tac.CrashGlass = d.CrashGlass; Tac.DefensiveAggression = d.DefensiveAggression; Tac.StarUsage = d.StarUsage;
        Session.SaveNow();
        Refresh();
    }
}
