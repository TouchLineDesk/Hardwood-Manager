using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

/// <summary>One row of a roster table. Shared by roster, free agents, trade builder and draft.</summary>
public sealed class PlayerRow
{
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition + (Player.SecondaryPosition is { } s ? "/" + s : "");
    public int Age { get; init; }
    public string Height => FormatHeight(Player.HeightCm);
    public static string FormatHeight(int cm)
    {
        int inches = (int)Math.Round(cm / 2.54);
        return $"{inches / 12}'{inches % 12}\"";
    }
    public int HeightCm => Player.HeightCm;
    public double Knowledge { get; init; } = 1.0;
    public string Ovr => Knowledge >= 0.999 ? Player.Overall.ToString("0.0") : $"{Scouting.Ability(Player, Knowledge).Low / 200.0 * 14 + 4:0.0}-{Scouting.Ability(Player, Knowledge).High / 200.0 * 14 + 4:0.0}";
    public string CA => Scouting.Ability(Player, Knowledge).ToString();
    public string PA => Scouting.Potential(Player, Knowledge).ToString();
    public int CaSort => Player.CurrentAbility;
    public string Sho => Scouting.GroupAverage(Player, AttrGroup.Shooting, Knowledge);
    public string Ply => Scouting.GroupAverage(Player, AttrGroup.Playmaking, Knowledge);
    public string Def => Scouting.GroupAverage(Player, AttrGroup.Defense, Knowledge);
    public string Men => Scouting.GroupAverage(Player, AttrGroup.Mental, Knowledge);
    public string Phy => Scouting.GroupAverage(Player, AttrGroup.Physical, Knowledge);
    public string Salary => Player.Contract is { } c ? $"{Hardwood.Core.Season.Budgets.Money(c.Salary)}" : "-";
    public long SalaryValue => Player.Contract?.Salary ?? 0;
    /// <summary>H30: set when the club's contracts haven't counted down yet this summer (in season, Years 1 is the final year).</summary>
    public bool BeforeCountdown { get; init; }
    public string Years => YearsText(Player.Contract, BeforeCountdown);
    public static string YearsText(Contract? c, bool beforeCountdown) =>
        c == null ? "-" : c.Years <= 0 ? "Expiring" : c.Years == 1 && beforeCountdown ? "Final year" : $"{c.Years} yr";
    public string Status { get; init; } = "";
    public string Mood => Player.TeamId == null ? "" : Hardwood.Core.People.PeopleEngine.Mood(Player.Morale) + (Player.TradeRequested ? " (wants out)" : "");
    public double Mpg { get; init; }
    public double Ppg { get; init; }
    public double Rpg { get; init; }
    public double Apg { get; init; }
    public string FgPct { get; init; } = "";
    public string Team { get; init; } = "";
    public Hardwood.Core.Model.Team? TeamObj { get; init; }
    public string TierName => Tier.ForCa(Player.CurrentAbility).Name;
    public Avalonia.Media.IBrush TierBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Tier.ForCa(Player.CurrentAbility).Light));
    public string MoodIcon => Player.Morale >= 60 ? "Smile" : Player.Morale >= 45 ? "Meh" : "Frown";
    public Avalonia.Media.IBrush MoodBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Player.Morale >= 60 ? "#3FB950" : Player.Morale >= 45 ? "#D29922" : "#F85149"));
    public string CardFooter { get; init; } = "";
    /// <summary>CS3 (run 127): a college player's class year ("Fr" … "5th"); empty for the pros.</summary>
    public string ClassYear { get; init; } = "";
    /// <summary>Sorts the class column youngest first.</summary>
    public int ClassOrder { get; init; }
    /// <summary>Set by the hosting page so a card click opens the player.</summary>
    public IRelayCommand? Open { get; set; }

    public static PlayerRow From(Player p, GameSession s, string? team = null, bool prospect = false)
    {
        (p.TeamId == s.HumanTeam.Id ? s.MyStats : s.Stats).TryGetValue(p.Id, out var st);
        string status = p.IsAvailable(s.Today) ? "" : $"{p.Injury!.Name} ({p.Injury.DaysOut(s.Today)}d)";
        var club = p.TeamId is { } cid ? s.FindTeam(cid) : null;
        bool before = club != null && !s.ContractsCounted(club);
        return new PlayerRow
        {
            ClassYear = s.ClassYearOf(p)?.Short ?? "", ClassOrder = s.ClassYearOf(p)?.Year ?? 0,
            Player = p, Age = p.Age(s.Today), BeforeCountdown = before, Status = status, Knowledge = Scouting.Knowledge(s.HumanTeam, p, prospect),
            Mpg = Math.Round(st?.Mpg ?? 0, 1), Ppg = Math.Round(st?.Ppg ?? 0, 1), Rpg = Math.Round(st?.Rpg ?? 0, 1), Apg = Math.Round(st?.Apg ?? 0, 1),
            FgPct = st is { FGA: > 0 } ? $"{st.FgPct * 100:F1}" : "",
            Team = team ?? (p.TeamId is { } id ? s.FindTeam(id)!.Abbreviation : "FA"),
            TeamObj = p.TeamId is { } tid ? s.FindTeam(tid)! : null,
            CardFooter = st is { Games: > 0 } ? $"{st.Ppg:F1} pts · {st.Rpg:F1} reb · {st.Apg:F1} ast · {st.Mpg:F0} min"
                       : p.Contract is { } c ? $"{Hardwood.Core.Season.Budgets.Money(c.Salary)} · {YearsText(c, before).ToLowerInvariant()}"
                       // CS3 (run 127): a college player's card foot names his class, not his age again.
                       : s.ClassYearOf(p) is { } cy ? (cy.Final ? $"{cy.Long} · final season" : cy.Long) : $"Age {p.Age(s.Today)}",
        };
    }
}

public sealed partial class RosterViewModel : PageViewModel
{
    public override string Title => "Roster";
    public ObservableCollection<PlayerRow> Rows { get; } = new();
    public ObservableCollection<Team> Teams { get; } = new();
    [ObservableProperty] private Team? _team;
    [ObservableProperty] private PlayerRow? _selected;
    [ObservableProperty] private string _summary = "";
    /// <summary>Roadmap 1: the squad at a glance as stat chips (players, wages, injuries, expiring deals, best player, mood).</summary>
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _chips = new();
    public Avalonia.Media.Color Accent => ThemeService.AccentFor(Team ?? Session.HumanTeam);
    [ObservableProperty] private bool _cardView = true;
    public bool TableView => !CardView;
    /// <summary>CS3: the class-year column shows for a college program.</summary>
    public bool ShowClassYear => Session.World.College is { } cw && cw.Programs.Contains(Team ?? Session.HumanTeam);
    partial void OnCardViewChanged(bool value) => OnPropertyChanged(nameof(TableView));

    public RosterViewModel(MainViewModel shell) : base(shell)
    {
        // Bug hunt H18: a college coach's program isn't in the pro league, so the Team box was blank. His own league comes
        // first (the conference in college, his tier in the pyramid), then the top league's clubs as before.
        var mine = (Session.InCollege && Session.World.College is { } col && Session.MyConference is { } conf
            ? col.InConference(conf.Key)
            : Session.InNabl ? Session.League.Teams : Session.HumanLeague?.Clubs ?? Enumerable.Empty<Team>()).Append(Session.HumanTeam).Distinct().OrderBy(t => t.City).ToList();
        foreach (var t in mine) Teams.Add(t);
        foreach (var t in Session.League.Teams.Where(t => !mine.Contains(t)).OrderBy(t => t.City)) Teams.Add(t);
        _team = Session.HumanTeam;
    }

    partial void OnTeamChanged(Team? value) { OnPropertyChanged(nameof(ShowClassYear)); Refresh(); }

    public List<Hardwood.Desktop.Controls.Chip> BuildChips(Team team, long payroll)
    {
        var s = Session;
        var roster = team.Roster;
        var chips = new List<Hardwood.Desktop.Controls.Chip>
        {
            new("Players", roster.Count.ToString(), roster.Count == 0 ? "" : $"average age {roster.Average(p => p.Age(s.Today)):0.0}", "team"),
        };
        // H29: your club below its league's minimum says so (the same number the assistant and the sporting director use).
        if (team == s.HumanTeam && s.Employed && !s.InCollege && roster.Count < s.MyRosterMin)
            chips[0] = new("Players", roster.Count.ToString(), $"short: the league needs {s.MyRosterMin}-{s.MyRosterMax}", "bad");
        var college = s.World.College is { } cw && cw.Programs.Contains(team) ? cw : null;
        if (college != null)
        {
            // College (C2): scholarships, not wages (13 plus up to 2 walk-ons, #162).
            int walkOns = roster.Count(p => college.WalkOnIds.Contains(p.Id));
            chips.Add(new("Scholarships", $"{roster.Count - walkOns}/13", walkOns == 0 ? "no walk-ons" : walkOns == 1 ? "plus 1 walk-on" : $"plus {walkOns} walk-ons"));
        }
        else if (team.Money.Ready)
        {
            long budget = team.Money.WageBudget;
            chips.Add(new("Wages", Hardwood.Core.Season.Budgets.Money(payroll), $"of a {Hardwood.Core.Season.Budgets.Money(budget)} budget",
                payroll > budget ? "bad" : payroll > budget * 0.95 ? "warn" : "good"));
        }
        else chips.Add(new("Wages", Hardwood.Core.Season.Budgets.Money(payroll), "a season"));
        if (college != null)
        {
            var summer = new DateOnly(college.Season.Year + 1, 7, 1);
            int grads = roster.Count(p => p.Age(summer) >= Hardwood.Core.College.CollegeWorld.GraduateAge);
            chips.Add(new("Final season", grads.ToString(), grads == 1 ? "turns pro next summer" : "turn pro next summer", grads > 0 ? "info" : ""));
        }
        var injured = roster.Where(p => p.Injury is { } i && i.IsOut(s.Today)).ToList();
        chips.Add(injured.Count == 0
            ? new("Injured", "0", "everyone fit", "good")
            : new("Injured", injured.Count.ToString(), string.Join(", ", injured.Take(2).Select(p => p.Name.Split(' ').Last())) + (injured.Count > 2 ? $" +{injured.Count - 2}" : ""), "bad"));
        // H30: in season a deal in its final year has Years 1 (the countdown comes at the start of the summer).
        int expiring = roster.Count(p => s.EndsThisSummer(p, team));
        if (college == null) chips.Add(new("Expiring", expiring.ToString(), expiring == 0 ? "no deals end this summer" : expiring == 1 ? "deal ends this summer" : "deals end this summer", expiring > 0 ? "warn" : ""));
        if (roster.Count > 0)
        {
            var best = roster.OrderByDescending(p => p.CurrentAbility).First();
            var k = team == s.HumanTeam ? 1.0 : StaffEffects.ScoutingKnowledge(s.HumanTeam);
            chips.Add(new("Best player", best.Name.Split(' ').Last(), $"{best.PrimaryPosition} · ability {Scouting.Ability(best, k)}", "info"));
            int unhappy = roster.Count(p => p.TradeRequested || p.Morale < 50); // "Unhappy" or worse in the Mood column
            chips.Add(new("Unhappy", unhappy.ToString(), unhappy == 0 ? "nobody is unsettled" : "unhappy or want out",
                roster.Any(p => p.TradeRequested || p.Morale < 35) ? "bad" : unhappy > 0 ? "warn" : "good"));
        }
        return chips;
    }

    [RelayCommand]
    public void Open(PlayerRow? row)
    {
        if (row != null) Shell.ShowPlayer(row.Player);
    }

    public override void Refresh()
    {
        var team = Team ?? Session.HumanTeam;
        Rows.Clear();
        foreach (var p in team.Roster.OrderByDescending(p => p.CurrentAbility))
        {
            var row = PlayerRow.From(p, Session, team.Abbreviation);
            row.Open = new RelayCommand(() => Shell.ShowPlayer(row.Player));
            Rows.Add(row);
        }
        var fin = Session.League.Finance;
        long payroll = ContractMarket.Payroll(team);
        Summary = team == Session.HumanTeam ? ""
            : $"{(Session.Standings.Has(team) ? Session.Trades.Direction(team) + " · " : "")}scouting knowledge {StaffEffects.ScoutingKnowledge(Session.HumanTeam) * 100:F0}% (ranges show what your scouts believe)";
        Chips = BuildChips(team, payroll);
        OnPropertyChanged(nameof(Accent));
    }
}
