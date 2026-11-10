using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class WorldTableRow
{
    public int Rank { get; init; }
    public required Team Team { get; init; }
    public string Name => Team.FullName;
    public string Group { get; init; } = "";
    public int W { get; init; }
    public int L { get; init; }
    public string Pct { get; init; } = "";
    public string Diff { get; init; } = "";
    public string Zone { get; init; } = "";
    public bool Mine { get; init; }
}

public sealed class LeagueGameRow
{
    public string Date { get; init; } = "";
    public required Team Home { get; init; }
    public required Team Away { get; init; }
    public string Score { get; init; } = "";
    public string Stage { get; init; } = "";
    public GameResult? Box { get; init; }
    public bool HasBox => Box != null;
    public IRelayCommand? Open { get; init; }
}

public sealed class WorldLeaderRow
{
    public int Rank { get; init; }
    public Player? Player { get; init; }
    public string Name { get; init; } = "";
    public Team? Team { get; init; }
    public string Club => Team?.Abbreviation ?? "";
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

public sealed record RuleRow(string Label, string Value);
public sealed record CompetitionChoice(string Key, string Name);

/// <summary>
/// Every league in the save (the NABL, the world, the USA pyramid, college and the club cups): table, schedule and
/// results, player stats by category, rules and format, and champions. Team and player names open their pages.
/// </summary>
public sealed partial class WorldViewModel : PageViewModel
{
    public override string Title => "Leagues";
    public List<string> Regions { get; } = new() { "USA", "Europe", "Club cups", "Americas", "Asia-Pacific", "Africa" };
    [ObservableProperty] private string _region = "USA";
    public ObservableCollection<CompetitionChoice> Competitions { get; } = new();
    [ObservableProperty] private CompetitionChoice? _selected;
    [ObservableProperty] private string _heading = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _series = "";
    [ObservableProperty] private string _champions = "";
    public ObservableCollection<WorldTableRow> Table { get; } = new();
    public ObservableCollection<LeagueGameRow> Games { get; } = new();
    public ObservableCollection<WorldLeaderRow> Leaders { get; } = new();
    public ObservableCollection<RuleRow> Rules { get; } = new();

    public List<string> GameFilters { get; } = new() { "Results", "Coming up", "All" };
    [ObservableProperty] private string _gameFilter = "Results";
    public ObservableCollection<string> TeamFilters { get; } = new();
    [ObservableProperty] private string _teamFilter = "All teams";
    public List<string> Categories { get; } = new() { "Points", "Rebounds", "Assists", "Steals", "Blocks", "FG%", "3P%", "Minutes" };
    [ObservableProperty] private string _category = "Points";
    [ObservableProperty] private int _tab;

    private LeagueBook? _book;
    private bool _filling;

    public WorldViewModel(MainViewModel shell) : base(shell)
    {
        var mine = LeagueBook.HomeOf(Session, Session.HumanTeam);
        _filling = true;
        Region = mine?.Region ?? "USA";
        FillCompetitions();
        _filling = false;
        Selected = Competitions.FirstOrDefault(c => c.Key == mine?.Key) ?? Competitions.FirstOrDefault();
    }

    partial void OnRegionChanged(string value)
    {
        if (_filling) return;
        FillCompetitions();
        Selected = Competitions.FirstOrDefault();
    }

    private void FillCompetitions()
    {
        Competitions.Clear();
        foreach (var b in LeagueBook.All(Session).Where(b => b.Region == Region)) Competitions.Add(new CompetitionChoice(b.Key, b.Name));
    }

    partial void OnSelectedChanged(CompetitionChoice? value)
    {
        TeamFilter = "All teams";
        Refresh();
    }
    partial void OnGameFilterChanged(string value) => FillGames();
    partial void OnTeamFilterChanged(string value) => FillGames();
    partial void OnCategoryChanged(string value) => FillLeaders();

    public override void Refresh()
    {
        if (Selected == null) return;
        _book = LeagueBook.Get(Session, Selected.Key);
        if (_book is not { } b) return;
        Heading = $"{b.Name} · {b.Season} · {b.Status}";
        Note = $"{b.Teams.Count} teams";

        Table.Clear();
        var me = Session.HumanTeam;
        foreach (var grp in b.Table().GroupBy(r => r.Group))
        {
            int rank = 0;
            foreach (var r in grp)
                Table.Add(new WorldTableRow
                {
                    Rank = ++rank, Team = r.Team, Group = r.Group, W = r.W, L = r.L, Pct = r.G == 0 ? "-" : r.Pct.ToString(".000"),
                    Diff = r.G == 0 || (r.PointsFor == 0 && r.PointsAgainst == 0) ? "-" : r.Diff.ToString("+0.0;-0.0;0.0"), Zone = r.Zone, Mine = r.Team == me,
                });
        }

        var keep = TeamFilter;
        TeamFilters.Clear();
        TeamFilters.Add("All teams");
        foreach (var t in b.Teams.OrderBy(t => t.FullName)) TeamFilters.Add(t.FullName);
        TeamFilter = TeamFilters.Contains(keep) ? keep : "All teams";
        FillGames();
        FillLeaders();

        Rules.Clear();
        foreach (var (label, value) in b.Rules) Rules.Add(new RuleRow(label, value));
        var series = b.Series();
        Series = series.Count == 0 ? "" : string.Join("\n", series);
        Champions = b.Champions.Count == 0 ? "No champions yet in this save." : string.Join("\n", b.Champions);
    }

    private void FillGames()
    {
        Games.Clear();
        if (_book is not { } b) return;
        var games = b.Games().AsEnumerable();
        if (TeamFilter != "All teams") games = games.Where(g => g.Home.FullName == TeamFilter || g.Away.FullName == TeamFilter);
        games = GameFilter switch
        {
            "Results" => games.Where(g => g.Played).OrderByDescending(g => g.Date),
            "Coming up" => games.Where(g => !g.Played).OrderBy(g => g.Date),
            _ => games.OrderBy(g => g.Date),
        };
        foreach (var g in games.Take(400))
        {
            var box = g.Box;
            Games.Add(new LeagueGameRow
            {
                Date = g.Date == DateOnly.MinValue ? "" : g.Date.ToString("ddd MMM d"), Home = g.Home, Away = g.Away,
                Score = g.Played ? $"{g.HomePts} - {g.AwayPts}" : "vs", Stage = g.Stage, Box = box,
                Open = box == null ? null : new RelayCommand(() => Shell.ShowBoxScore(box)),
            });
        }
    }

    private void FillLeaders()
    {
        Leaders.Clear();
        if (_book is not { } b) return;
        var stats = b.Stats();
        int maxG = stats.Select(x => x.G).DefaultIfEmpty(0).Max();
        var qualified = stats.Where(x => x.G >= Math.Max(1, maxG * 0.4));
        Func<BookStat, double> key = Category switch
        {
            "Rebounds" => x => x.Rpg, "Assists" => x => x.Apg, "Steals" => x => x.Spg, "Blocks" => x => x.Bpg, "Minutes" => x => x.Mpg,
            "FG%" => x => x.FgPct, "3P%" => x => x.TpPct, _ => x => x.Ppg,
        };
        if (Category == "FG%") qualified = qualified.Where(x => x.Fga >= x.G * 4);
        if (Category == "3P%") qualified = qualified.Where(x => x.Tpa >= x.G * 2);
        var s = Session;
        int k = 0;
        foreach (var x in qualified.OrderByDescending(key).Take(50))
        {
            var p = FindPlayer(x.PlayerId);
            Leaders.Add(new WorldLeaderRow
            {
                Rank = ++k, Player = p, Name = p?.Name ?? "(left)", Team = s.FindTeam(x.TeamId), G = x.G,
                Mpg = x.Mpg.ToString("0.0"), Ppg = x.Ppg.ToString("0.0"), Rpg = x.Rpg.ToString("0.0"), Apg = x.Apg.ToString("0.0"),
                Spg = x.Spg.ToString("0.0"), Bpg = x.Bpg.ToString("0.0"), Fg = x.Fga > 0 ? x.FgPct.ToString(".000") : "-", Tp = x.Tpa > 0 ? x.TpPct.ToString(".000") : "-",
            });
        }
    }

    private Dictionary<int, Player>? _players;
    private Player? FindPlayer(int id)
    {
        var s = Session;
        _players ??= s.League.Teams.SelectMany(t => t.Roster).Concat(s.League.FreeAgents).Concat(s.World.Players)
            .Concat(s.World.College?.Players ?? Enumerable.Empty<Player>()).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        if (_players.TryGetValue(id, out var p)) return p;
        _players = null; // rosters change; rebuild once
        _players = s.League.Teams.SelectMany(t => t.Roster).Concat(s.League.FreeAgents).Concat(s.League.Retired).Concat(s.World.Players)
            .Concat(s.World.College?.Players ?? Enumerable.Empty<Player>()).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        return _players.GetValueOrDefault(id);
    }
}
