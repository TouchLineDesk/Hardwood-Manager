using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class TeamPlayerRow
{
    public required Player Player { get; init; }
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public string CA { get; init; } = "";
    public string PA { get; init; } = "";
    public string Contract { get; init; } = "";
    public string Nat => Player.Nationality;
    public string Status { get; init; } = "";
    public int G { get; init; }
    public string Mpg { get; init; } = "";
    public string Ppg { get; init; } = "";
    public string Rpg { get; init; } = "";
    public string Apg { get; init; } = "";
    public string Spg { get; init; } = "";
    public string Bpg { get; init; } = "";
    public string Fg { get; init; } = "";
    public string Tp { get; init; } = "";
}

public sealed class TeamGameRow
{
    public string Date { get; init; } = "";
    public string Competition { get; init; } = "";
    public string Where { get; init; } = "";
    public required Team Opponent { get; init; }
    public string Result { get; init; } = "";
    public GameResult? Box { get; init; }
    public bool HasBox => Box != null;
    public IRelayCommand? Open { get; init; }
}

/// <summary>Any team's page (NABL, abroad, college): roster as your scouts see it, season stats, games and the club.</summary>
public sealed partial class TeamViewModel : PageViewModel
{
    public Team Team { get; }
    public override string Title => Team.FullName;
    [ObservableProperty] private string _league = "";
    [ObservableProperty] private string _record = "";
    [ObservableProperty] private string _club = "";
    [ObservableProperty] private string _money = "";
    /// <summary>Roadmap 1: the club at a glance as stat chips (record, coach, owner, arena, money).</summary>
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _chips = new();
    public Avalonia.Media.Color Accent => ThemeService.AccentFor(Team);
    public ObservableCollection<TeamPlayerRow> Players { get; } = new();
    public ObservableCollection<TeamGameRow> Games { get; } = new();
    [ObservableProperty] private TeamPlayerRow? _selectedPlayer;
    public bool IsYours => Team == Session.HumanTeam;

    public TeamViewModel(MainViewModel shell, Team team) : base(shell) => Team = team;

    [RelayCommand] private void Back() => Shell.GoBack();
    [RelayCommand] private void OpenPlayer() { if (SelectedPlayer is { } r) Shell.ShowPlayer(r.Player); }

    public override void Refresh()
    {
        var s = Session;
        var t = Team;
        var home = LeagueBook.HomeOf(s, t);
        var books = LeagueBook.Of(s, t).ToList();
        League = home == null ? "" : $"{home.Name} · {home.Season} · {home.Status}";
        if (home != null)
        {
            var table = home.Table();
            var mine = table.FirstOrDefault(r => r.Team == t);
            var group = mine == null ? new List<BookTableRow>() : table.Where(r => r.Group == mine.Group).ToList();
            int rank = mine == null ? 0 : group.IndexOf(mine) + 1;
            Record = mine == null ? "" : $"{mine.W}-{mine.L} · {Ordinal(rank)} in {(mine.Group.Length > 0 ? mine.Group : home.Name)}" + (mine.Zone.Length > 0 && !mine.Zone.Contains("conf") ? $" · {mine.Zone}" : "");
        }
        var coach = t.Coach?.Name ?? (t.CoachName.Length > 0 ? t.CoachName : "-");
        Club = $"Coach: {coach} · Owner: {(t.Owner.Name.Length > 0 ? t.Owner.Name + ", " : "")}{t.Owner.Style.ToLowerInvariant()} · Arena: {t.ArenaCapacity:N0}";
        Money = t.Money.Ready ? $"Revenue ${t.Money.Revenue / 1e6:F1}M · Wage budget ${t.Money.WageBudget / 1e6:F1}M · Payroll ${t.Roster.Sum(p => p.Contract?.Salary ?? 0) / 1e6:F1}M" : "";
        var chips = new List<Hardwood.Desktop.Controls.Chip>();
        if (home != null)
        {
            var mine = home.Table().FirstOrDefault(r => r.Team == t);
            if (mine != null)
            {
                int gp = mine.W + mine.L;
                chips.Add(new("Record", $"{mine.W}-{mine.L}", Record.Contains(" · ") ? Record[(Record.IndexOf(" · ") + 3)..] : "",
                    gp == 0 ? "team" : mine.W * 2 > gp ? "good" : mine.W * 2 < gp ? "bad" : "team"));
            }
        }
        chips.Add(new("Coach", coach, t.Coach is { } hc ? $"reputation {hc.Reputation:0}" : ""));
        chips.Add(new("Owner", t.Owner.Name.Length > 0 ? t.Owner.Name : "-", t.Owner.Style.ToLowerInvariant()));
        chips.Add(new("Arena", t.ArenaCapacity.ToString("N0"), "seats"));
        if (t.Money.Ready)
        {
            long payroll = t.Roster.Sum(p => p.Contract?.Salary ?? 0);
            chips.Add(new("Revenue", PlayerViewModel.Money(t.Money.Revenue), "a season"));
            chips.Add(new("Payroll", PlayerViewModel.Money(payroll), $"budget {PlayerViewModel.Money(t.Money.WageBudget)}",
                payroll > t.Money.WageBudget ? "bad" : payroll > t.Money.WageBudget * 0.95 ? "warn" : "good"));
        }
        Chips = chips;

        // Roster: what your scouts know (everything about your own players).
        var stats = (home?.Stats() ?? new()).Where(x => x.TeamId == t.Id).ToDictionary(x => x.PlayerId);
        var today = s.Today;
        Players.Clear();
        foreach (var p in t.Roster.OrderByDescending(p => stats.TryGetValue(p.Id, out var st) ? st.Min : 0).ThenByDescending(p => p.CurrentAbility))
        {
            double k = Scouting.Knowledge(s.HumanTeam, p, prospect: s.World.College?.Programs.Contains(t) == true);
            stats.TryGetValue(p.Id, out var st);
            Players.Add(new TeamPlayerRow
            {
                Player = p, Age = p.Age(today), CA = Scouting.Ability(p, k).ToString(), PA = Scouting.Potential(p, k).ToString(),
                Contract = p.Contract is { } c ? $"${c.Salary / 1e6:F2}M × {c.Years}y" : "-",
                Status = p.IsAvailable(today) ? "" : $"Out {p.Injury!.DaysOut(today)}d", // the injury itself is on his page (too long for this column at 1280 px)
                G = st?.G ?? 0, Mpg = F(st?.Mpg), Ppg = F(st?.Ppg), Rpg = F(st?.Rpg), Apg = F(st?.Apg), Spg = F(st?.Spg), Bpg = F(st?.Bpg),
                Fg = st is { Fga: > 0 } ? st.FgPct.ToString(".000") : "-", Tp = st is { Tpa: > 0 } ? st.TpPct.ToString(".000") : "-",
            });
        }

        // Games in every competition this season.
        Games.Clear();
        foreach (var (book, g) in books.SelectMany(b => b.Games().Where(g => g.Involves(t)).Select(g => (b, g))).OrderBy(x => x.g.Date))
        {
            bool atHome = g.Home == t;
            var opp = atHome ? g.Away : g.Home;
            string result = "";
            if (g.Played)
            {
                int us = atHome ? g.HomePts!.Value : g.AwayPts!.Value, them = atHome ? g.AwayPts!.Value : g.HomePts!.Value;
                result = $"{(us > them ? "W" : "L")} {us}-{them}";
            }
            var box = g.Box;
            Games.Add(new TeamGameRow
            {
                Date = g.Date == DateOnly.MinValue ? "" : g.Date.ToString("ddd MMM d"), Competition = g.Stage == "Regular season" || g.Stage == CompetitionSeasonStage ? book.Name : $"{book.Name} · {g.Stage}",
                Where = atHome ? "vs" : "at", Opponent = opp, Result = result, Box = box,
                Open = box == null ? null : new RelayCommand(() => Shell.ShowBoxScore(box)),
            });
        }
    }

    private const string CompetitionSeasonStage = Hardwood.Core.World.CompetitionSeason.RegularSeason;
    private static string F(double? v) => v is { } x ? x.ToString("0.0") : "-";
    private static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}
