using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.ViewModels;

/// <summary>One rating on a staff card (run 66): a label, the 1–20 value as a bar, and whether it's the role's key.</summary>
public sealed record StaffBar(string Label, int Value, bool IsKey, Avalonia.Media.IBrush Brush, string Tip)
{
    public Avalonia.Media.FontWeight Weight => IsKey ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
}

public sealed partial class StaffRow : ObservableObject
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
    public string Contract => Staff.ContractYears > 1 ? $"{Staff.ContractYears} yr" : "final yr";
    /// <summary>His way of working (roadmap 12): one of two styles per role, each with an effect.</summary>
    public string Style => Staff.Style;
    public string StyleText => Staff.StyleText;

    // ---- Run 66 (review #13 rank 5, Danny's #464): the staff card. ----
    /// <summary>The card is lit when he's the one picked.</summary>
    [ObservableProperty] private bool _picked;
    /// <summary>On the market: is he better than the man you have in the role? "" on your own staff.</summary>
    public string Compare { get; init; } = "";
    public Avalonia.Media.IBrush CompareBrush { get; init; } = StaffCards.Neutral;
    public string CompareTip { get; init; } = "";
    public bool HasCompare => Compare.Length > 0;
    public Avalonia.Media.IBrush RoleBrush => StaffCards.RoleBrush(Staff.Role);
    public Avalonia.Media.IBrush KeyBrush => StaffCards.BandBrush(Key);
    public string KeyLabel => StaffCards.KeyName(Staff.Role);
    public string AgeLine => $"Age {Age} · {Style}";
    /// <summary>The five ratings as bars, the role's own first and in bold.</summary>
    public List<StaffBar> Bars => StaffCards.Bars(Staff);
    /// <summary>Your staff: years left, amber in his final year. The market: what he'd ask a season.</summary>
    public string ContractChip => Pay >= 0 ? $"Asks {Salary} a season" : Staff.ContractYears > 1 ? $"{Staff.ContractYears} yrs left · {Salary}" : $"Final year · {Salary}";
    public Avalonia.Media.IBrush ContractBrush => Pay < 0 && Staff.ContractYears <= 1 ? StandingRow.PlayInBrush : StaffCards.Neutral;
    public string ContractTip => Pay >= 0 ? "His salary a season if you hire him (staff pay isn't counted against the cap)"
        : Staff.ContractYears > 1 ? "Years left on his deal and his salary a season" : "His deal ends this summer: pick him and press Extend, or he leaves";
}

/// <summary>Run 66: the colours and bars shared by the staff cards.</summary>
public static class StaffCards
{
    public static readonly Avalonia.Media.IBrush Neutral = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2A3440"));
    private static readonly Avalonia.Media.IBrush Other = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#5A6573"));
    private static readonly Avalonia.Media.IBrush Mid = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2F6FB3"));
    private static readonly Dictionary<StaffRole, Avalonia.Media.IBrush> Roles = new()
    {
        [StaffRole.AssistantCoach] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7A4FB3")),
        [StaffRole.DevelopmentCoach] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2E8B57")),
        [StaffRole.StrengthCoach] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#B7642A")),
        [StaffRole.Physio] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#B23A5A")),
        [StaffRole.Scout] = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2F7F9F")),
    };

    public static Avalonia.Media.IBrush RoleBrush(StaffRole r) => Roles[r];

    /// <summary>15+ green, 8–14 blue, 7 and under red: the same bands the coach chips use.</summary>
    public static Avalonia.Media.IBrush BandBrush(int v) => v >= 15 ? StandingRow.PlayoffBrush : v <= 7 ? StandingRow.RelegationBrush : Mid;

    public static string KeyName(StaffRole r) => r switch
    {
        StaffRole.AssistantCoach => "Coaching",
        StaffRole.DevelopmentCoach => "Development",
        StaffRole.StrengthCoach => "Fitness",
        StaffRole.Physio => "Medical",
        _ => "Scouting",
    };

    private static readonly (StaffRole Role, string Label, string Tip)[] Order =
    {
        (StaffRole.AssistantCoach, "Coa", "Coaching: tactics and in-game work; also feeds development"),
        (StaffRole.DevelopmentCoach, "Dev", "Development: teaching young players, the main driver of growth"),
        (StaffRole.StrengthCoach, "Fit", "Fitness: lowers injury risk and slows physical decline"),
        (StaffRole.Physio, "Med", "Medical: shortens injuries"),
        (StaffRole.Scout, "Sco", "Scouting: how much of other players' ratings you can see"),
    };

    private static int Value(Staff s, StaffRole r) => r switch
    {
        StaffRole.AssistantCoach => s.Coaching,
        StaffRole.DevelopmentCoach => s.Development,
        StaffRole.StrengthCoach => s.Fitness,
        StaffRole.Physio => s.Medical,
        _ => s.Scouting,
    };

    /// <summary>The five ratings, the role's key one first (in its band colour) and the rest in grey.</summary>
    public static List<StaffBar> Bars(Staff s) =>
        Order.OrderBy(o => o.Role == s.Role ? 0 : 1)
             .Select(o => { int v = Value(s, o.Role); bool key = o.Role == s.Role; return new StaffBar(o.Label, v, key, key ? BandBrush(v) : Other, $"{o.Tip}. {v} of 20" + (key ? " (his role's key rating)" : "")); })
             .ToList();
}

/// <summary>Run 66: a role with nobody in it, as a card that sends you to the market for one.</summary>
public sealed record VacantRole(StaffRole Role, string Text)
{
    public string Name => Staff.RoleName(Role);
    public Avalonia.Media.IBrush RoleBrush => StaffCards.RoleBrush(Role);
}

public sealed partial class FocusRow : ObservableObject
{
    public required Player Player { get; init; }
    public StaffViewModel Owner { get; init; } = null!;
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public string Ability => $"{Player.CurrentAbility} / {Player.PotentialAbility}";
    /// <summary>Where this summer's development is heading at his minutes, and your staff's share of it (roadmap 12).</summary>
    public string Projected { get; init; } = "";
    public string FromStaff { get; init; } = "";
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
    // Run 66 (review #13 rank 5): cards first, the tables one click away; roles as chips with counts.
    public string[] Views { get; } = { "Cards", "Full table" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowCards), nameof(ShowTable))] private string _view = "Cards";
    public bool ShowCards => View == "Cards";
    public bool ShowTable => !ShowCards;
    /// <summary>How many market candidates get a card (the best by their role's key rating); the rest are in the table.</summary>
    public const int CardCount = 12;
    public ObservableCollection<StaffRow> MarketCards { get; } = new();
    public ObservableCollection<VacantRole> Vacant { get; } = new();
    [ObservableProperty] private List<FilterChip> _roleChips = new();
    [ObservableProperty] private string _marketCardsNote = "";
    [ObservableProperty] private StaffRow? _selectedOurs;
    [ObservableProperty] private StaffRow? _selectedMarket;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _effects = "";
    /// <summary>Roadmap 1: what your staff do for you, as stat chips (the same numbers the Effects line used to list).</summary>
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _chips = new();
    public Avalonia.Media.Color Accent => ThemeService.AccentFor(Session.HumanTeam);
    [ObservableProperty] private double _intensity;
    // Interviews and delegation (roadmap 12).
    [ObservableProperty] private string _interview = "";
    [ObservableProperty] private bool _canHire;
    [ObservableProperty] private bool _delegateTraining;
    [ObservableProperty] private bool _delegateRecruiting;
    /// <summary>A pro club in a real career has no senior recruiting to delegate (D48 B): the draft replaces it.</summary>
    public bool ShowDelegateRecruiting => Session.InCollege || Session.SeniorRecruiting;
    public string DelegateRecruitingText => Session.TrailOpen ? "Assistant recruits high-schoolers with the hours you leave unused (offers included)" : "Head scout courts college seniors (spends the recruiting points; offers stay with you)";
    [ObservableProperty] private bool _delegateTactics;
    [ObservableProperty] private string _delegationNote = "";
    [ObservableProperty] private bool _delegateMedia;
    partial void OnDelegateMediaChanged(bool value) { if (_loading) return; Session.Delegated.Media = value; Session.SaveNow(); FillDelegationNote(); }
    // Staff part 2: extending your staff, and you as a coach.
    [ObservableProperty] private string _renewal = "";
    [ObservableProperty] private bool _canExtend;
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _coachChips = new();
    [ObservableProperty] private string _coachNote = "";
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _developmentChips = new();
    public bool TrainingManual => !DelegateTraining;
    partial void OnSelectedMarketChanged(StaffRow? value)
    {
        CanHire = false;
        // Run 66: picking a card says what's next (the bug finder saw a pick change nothing but the card's border).
        Interview = value == null ? "" : $"{value.Name}, {value.Role.ToLowerInvariant()}: press Interview to hear whether he'd come and on what terms.";
    }
    partial void OnSelectedMarketChanged(StaffRow? oldValue, StaffRow? newValue) { if (oldValue != null) oldValue.Picked = false; if (newValue != null) newValue.Picked = true; }
    partial void OnSelectedOursChanged(StaffRow? oldValue, StaffRow? newValue) { if (oldValue != null) oldValue.Picked = false; if (newValue != null) newValue.Picked = true; }
    partial void OnDelegateTrainingChanged(bool value)
    {
        OnPropertyChanged(nameof(TrainingManual));
        if (_loading) return;
        Session.Delegated.Training = value;
        if (value) { Session.DelegateTrainingNow(); Refresh(); }
        Session.SaveNow();
    }
    partial void OnDelegateRecruitingChanged(bool value) { if (_loading) return; Session.Delegated.Recruiting = value; Session.SaveNow(); FillDelegationNote(); }
    partial void OnDelegateTacticsChanged(bool value) { if (_loading || !Session.Employed) return; Session.HumanTeam.Tactic.Auto = value; Session.SaveNow(); FillDelegationNote(); }
    [ObservableProperty] private string _intensityText = "";
    private bool _loading;

    public StaffViewModel(MainViewModel shell) : base(shell) { }

    partial void OnRoleFilterChanged(string value) => FillMarket();
    partial void OnIntensityChanged(double value)
    {
        if (_loading || !Session.Employed) return;   // H52 (run 84)
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
        var keepOurs = SelectedOurs?.Staff;
        Ours.Clear(); Vacant.Clear();
        foreach (var role in Enum.GetValues<StaffRole>())
        {
            var st = team.Staff.FirstOrDefault(x => x.Role == role);
            if (st != null) Ours.Add(new StaffRow { Staff = st, Age = st.Age(s.Today) });
            else Vacant.Add(new VacantRole(role, VacancyText(role)));
        }
        if (keepOurs != null && Ours.FirstOrDefault(r => r.Staff == keepOurs) is { } again) { again.Picked = true; SelectedOurs = again; }
        else if (SelectedOurs != null) SelectedOurs = null;   // he was released or left: nothing picked, no stale renewal line
        FillMarket();
        Players.Clear();
        double staffTotal = 0; (Player p, double c)? riser = null;
        foreach (var p in team.Roster.OrderByDescending(p => p.PotentialAbility - p.CurrentAbility).ThenByDescending(p => p.CurrentAbility))
        {
            var (change, fromStaff) = s.Projection(p);
            staffTotal += fromStaff;
            if (riser == null || change > riser.Value.c) riser = (p, change);
            Players.Add(new FocusRow
            {
                Player = p, Owner = this, Age = p.Age(s.Today), Focus = team.Training.Focus.TryGetValue(p.Id, out var f) ? f.ToString() : "Balanced",
                Projected = Math.Abs(change) < 0.5 ? "steady" : $"{change:+0;-0} CA", FromStaff = Math.Abs(fromStaff) < 0.05 ? "-" : $"{fromStaff:+0.0;-0.0}",
            });
        }
        int growing = Players.Count(r => r.Projected.StartsWith("+"));
        DevelopmentChips = new()
        {
            s.InCollege
                // CP2 (run 131): in college the program's standing counts too, and growth is against an average program.
                ? new("Your program adds", $"{Math.Round(staffTotal, 1) + 0.0:+0.0;-0.0} CA", "across the roster this summer: your staff, facility and Development rating, against an average program", staffTotal >= 1 ? "good" : staffTotal < 0 ? "bad" : "")
                : new("Your staff add", $"{Math.Round(staffTotal, 1) + 0.0:+0.0;-0.0} CA", "across the squad this summer, against average training", staffTotal >= 1 ? "good" : staffTotal < 0 ? "bad" : ""),
            new("Biggest riser", riser is { } r && r.c >= 0.5 ? $"{r.c:+0} CA" : "None", riser is { } rr ? rr.p.Name : "", riser is { } r2 && r2.c >= 3 ? "good" : ""),
            new("Growing", growing.ToString(), "players heading up at their minutes", "info"),
        };
        DelegateTraining = s.Delegated.Training; DelegateRecruiting = s.Delegated.Recruiting; DelegateTactics = team.Tactic.Auto; DelegateMedia = s.Delegated.Media;
        var c = s.Coach;
        CoachChips = Enum.GetValues<Hardwood.Core.Jobs.CoachAttr>()
            .Select(a => new Hardwood.Desktop.Controls.Chip(Hardwood.Core.Jobs.Coach.Label(a), c[a].ToString(), Hardwood.Core.Jobs.Coach.Describe(a), c[a] >= 14 ? "good" : c[a] <= 7 ? "warn" : "")).ToList();
        CoachNote = $"This season so far: {c.SeasonAnswers} press and locker-room moments answered yourself, {c.SeasonTalks} one-on-one talks. "
                    + "A season's work grows your ratings: fronting the press builds Motivation, talks build Man management, a winning season Tactics, planning your own training for young players Development (up to two points a season).";
        var ending = s.StaffExpiring;
        if (Renewal.Length == 0 && ending.Count > 0)
            Renewal = $"{string.Join(", ", ending.Select(x => x.Name))}: contract{(ending.Count == 1 ? "" : "s")} end this summer. Select one in Your staff and press Extend, or he leaves when the season ends.";
        FillDelegationNote();
        Intensity = team.Training.Intensity;
        IntensityText = Describe(team.Training.Intensity);
        UpdateEffects();
        _loading = false;
    }

    private void FillMarket()
    {
        var keep = SelectedMarket?.Staff;
        Market.Clear();
        var team = Session.HumanTeam;
        foreach (var st in Session.League.FreeStaff.Where(st => RoleFilter == "All roles" || Staff.RoleName(st.Role) == RoleFilter).OrderByDescending(st => st.KeyAttribute))
        {
            var (cmp, brush, tip) = CompareWith(team.Staff.FirstOrDefault(x => x.Role == st.Role), st);
            Market.Add(new StaffRow { Staff = st, Age = st.Age(Session.Today), Pay = Session.StaffPayFor(st), Compare = cmp, CompareBrush = brush, CompareTip = tip });
        }
        MarketCards.Clear();
        foreach (var r in Market.Take(CardCount)) MarketCards.Add(r);
        if (keep != null && Market.FirstOrDefault(r => r.Staff == keep) is { } again)
        {
            if (!MarketCards.Contains(again)) MarketCards.Insert(0, again);
            again.Picked = true;
            if (SelectedMarket != again) SelectedMarket = again;
        }
        else if (SelectedMarket != null) SelectedMarket = null;
        var free = Session.League.FreeStaff;
        RoleChips = RoleFilters.Select(f => new FilterChip
        {
            Name = f, Selected = f == RoleFilter,
            Record = (f == "All roles" ? free.Count : free.Count(x => Staff.RoleName(x.Role) == f)).ToString("N0"),
            Tip = f == "All roles" ? "Every candidate on the staff market" : $"Only {f.ToLowerInvariant()} candidates",
        }).ToList();
        MarketCardsNote = Market.Count == 0 ? (RoleFilter == "All roles" ? "Nobody is on the staff market right now. Candidates appear as clubs change their staff." : $"No {RoleFilter.ToLowerInvariant()} is on the market right now. Pick another role.")
            : Market.Count <= CardCount ? $"All {Market.Count} candidates, best first by their role's key rating. Pick one, then Interview."
            : $"The best {CardCount} of {Market.Count:N0}, by their role's key rating. Pick one, then Interview; Full table lists them all.";
    }

    /// <summary>Run 66: a role chip on the staff market.</summary>
    public void SetRole(string role)
    {
        if (!RoleFilters.Contains(role)) return;
        RoleFilter = role;
    }

    /// <summary>Run 66: a vacant role's card sends you to the market for that role, in cards.</summary>
    public void FindFor(StaffRole role)
    {
        View = "Cards";
        RoleFilter = Staff.RoleName(role);
    }

    /// <summary>Run 66: how a candidate compares with the man you have in his role.</summary>
    public static (string Text, Avalonia.Media.IBrush Brush, string Tip) CompareWith(Staff? yours, Staff him)
    {
        string key = StaffCards.KeyName(him.Role).ToLowerInvariant();
        if (yours == null) return ("Fills a vacancy", StandingRow.PlayoffBrush, $"You have no {Staff.RoleName(him.Role).ToLowerInvariant()}: an empty role hurts");
        int d = him.KeyAttribute - yours.KeyAttribute;
        if (d == 0) return ($"Level with {yours.LastName}", StaffCards.Neutral, $"Same {key} as {yours.Name} ({yours.KeyAttribute})");
        return ($"{d:+0;-0} on {yours.LastName}", d > 0 ? StandingRow.PlayoffBrush : StandingRow.RelegationBrush,
                $"{key} {him.KeyAttribute} against {yours.Name}'s {yours.KeyAttribute}");
    }

    private static string VacancyText(StaffRole r) => r switch
    {
        StaffRole.AssistantCoach => "Nobody helps with tactics or the training ground.",
        StaffRole.DevelopmentCoach => "Young players grow slower without him.",
        StaffRole.StrengthCoach => "Injury risk is higher without him.",
        StaffRole.Physio => "Injuries last longer without him.",
        _ => "You see less of other players' ratings.",
    };

    /// <summary>Run 66: pressing a card picks him (your staff or the market).</summary>
    public void PickOurs(StaffRow row) => SelectedOurs = row;
    public void PickMarket(StaffRow row) => SelectedMarket = row;

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
        if (_loading || !Session.Employed) return;   // H52 (run 84)
        var focus = Session.HumanTeam.Training.Focus;
        if (row.Focus == "Balanced") focus.Remove(row.Player.Id);
        else focus[row.Player.Id] = Enum.Parse<AttrGroup>(row.Focus);
        Session.SaveNow();
    }

    private void FillDelegationNote()
    {
        var d = Session.Delegated;
        var lines = new List<string>();
        if (d.Training && d.LastTraining.Length > 0) lines.Add(d.LastTraining);
        if (d.Recruiting) lines.Add(d.LastRecruiting.Length > 0 ? d.LastRecruiting : Session.TrailOpen ? "Your assistant spends the recruiting hours you leave unused each week (College > Recruiting)." : "Your head scout starts courting seniors from November (College > Recruiting).");
        if (Session.HumanTeam.Tactic.Auto) lines.Add("Your assistant picks the system, scheme and sliders for each opponent.");
        if (d.Media) lines.Add(d.LastMedia.Length > 0 ? d.LastMedia : "Your assistant coach fronts the press and handles the locker room, in his own style (a Mentor protects team spirit).");
        DelegationNote = lines.Count == 0 ? "You're running everything yourself." : string.Join("\n", lines);
    }

    /// <summary>Interview the selected candidate: will he come, and for what (roadmap 12).</summary>
    [RelayCommand]
    public void InterviewCandidate()
    {
        if (SelectedMarket == null) return;
        var a = Session.Interview(SelectedMarket.Staff);
        Interview = a.Text; CanHire = a.Willing;
    }

    [RelayCommand]
    public void Hire()
    {
        if (SelectedMarket == null) return;
        var st = SelectedMarket.Staff;
        var replaced = Session.HumanTeam.Staff.FirstOrDefault(x => x.Role == st.Role);
        Status = Session.HireOnTerms(st) + (replaced != null && Session.HumanTeam.Staff.Contains(st) ? $" {replaced.Name} released." : "");
        Interview = ""; CanHire = false;
        Shell.AfterTick();
    }

    partial void OnSelectedOursChanged(StaffRow? value)
    {
        CanExtend = false;
        if (value == null) { Renewal = ""; return; }
        var a = Session.StaffRenewal(value.Staff);
        CanExtend = a.Willing;
        if (_loading) return;   // Refresh re-picking the same man keeps the page's line as it was
        Renewal = a.Text; CanExtend = a.Willing;
    }

    /// <summary>Extend the selected staff member on the terms he asked for (staff part 2).</summary>
    [RelayCommand]
    public void Extend()
    {
        if (SelectedOurs == null) return;
        Status = Session.ExtendStaff(SelectedOurs.Staff);
        Renewal = ""; CanExtend = false;
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
