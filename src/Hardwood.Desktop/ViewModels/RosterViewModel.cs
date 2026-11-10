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
    public string Salary => Player.Contract is { } c ? $"${c.Salary / 1e6:F1}M" : "-";
    public long SalaryValue => Player.Contract?.Salary ?? 0;
    public string Years => Player.Contract is { } c ? (c.Years <= 0 ? "Expiring" : $"{c.Years} yr") : "-";
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
    /// <summary>Set by the hosting page so a card click opens the player.</summary>
    public IRelayCommand? Open { get; set; }

    public static PlayerRow From(Player p, GameSession s, string? team = null, bool prospect = false)
    {
        s.Stats.TryGetValue(p.Id, out var st);
        string status = p.IsAvailable(s.Today) ? "" : $"{p.Injury!.Name} ({p.Injury.DaysOut(s.Today)}d)";
        return new PlayerRow
        {
            Player = p, Age = p.Age(s.Today), Status = status, Knowledge = Scouting.Knowledge(s.HumanTeam, p, prospect),
            Mpg = Math.Round(st?.Mpg ?? 0, 1), Ppg = Math.Round(st?.Ppg ?? 0, 1), Rpg = Math.Round(st?.Rpg ?? 0, 1), Apg = Math.Round(st?.Apg ?? 0, 1),
            FgPct = st is { FGA: > 0 } ? $"{st.FgPct * 100:F1}" : "",
            Team = team ?? (p.TeamId is { } id ? s.League.TeamById(id).Abbreviation : "FA"),
            TeamObj = p.TeamId is { } tid ? s.League.TeamById(tid) : null,
            CardFooter = st is { Games: > 0 } ? $"{st.Ppg:F1} pts · {st.Rpg:F1} reb · {st.Apg:F1} ast · {st.Mpg:F0} min"
                       : p.Contract is { } c ? $"${c.Salary / 1e6:F1}M · {(c.Years <= 0 ? "expiring" : c.Years + " yr")}" : $"Age {p.Age(s.Today)}",
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
    [ObservableProperty] private bool _cardView = true;
    public bool TableView => !CardView;
    partial void OnCardViewChanged(bool value) => OnPropertyChanged(nameof(TableView));

    public RosterViewModel(MainViewModel shell) : base(shell)
    {
        foreach (var t in Session.League.Teams.OrderBy(t => t.City)) Teams.Add(t);
        _team = Session.HumanTeam;
    }

    partial void OnTeamChanged(Team? value) => Refresh();

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
        Summary = $"{team.Roster.Count} players · " + fin.Describe(payroll)
                  + (team == Session.HumanTeam ? "" : $" · {Session.Trades.Direction(team)} · scouting knowledge {StaffEffects.ScoutingKnowledge(Session.HumanTeam) * 100:F0}% (ranges show what your scouts believe)");
    }
}
