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
/// <summary>One colour in the standings key (playoffs, third place, promotion, relegation).</summary>
public sealed record ZoneKey(Avalonia.Media.IBrush Brush, string Label);
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

    // ---- Danny, #463: every league's standings as division cards, like your own Standings page.
    public ObservableCollection<DivisionCard> LeftCards { get; } = new();
    public ObservableCollection<DivisionCard> RightCards { get; } = new();
    public ObservableCollection<ZoneKey> Keys { get; } = new();
    [ObservableProperty] private string _leftTitle = "";
    [ObservableProperty] private string _rightTitle = "";
    [ObservableProperty] private bool _hasRight;
    public string[] Views { get; } = { "Cards", "Full table" };
    /// <summary>Cards open first, as on Standings (D55 A); the full table is one click away.</summary>
    [ObservableProperty] private string _view = "Cards";
    public bool ShowCards => View == "Cards";
    public bool ShowTable => !ShowCards;
    partial void OnViewChanged(string value) { OnPropertyChanged(nameof(ShowCards)); OnPropertyChanged(nameof(ShowTable)); }

    // ---- Danny, #470: the playoffs drawn as a bracket, the third-place tournament beside it, champions as a roll of honour.
    [ObservableProperty] private IReadOnlyList<Hardwood.Core.Season.BracketColumn> _mainBracket = Array.Empty<Hardwood.Core.Season.BracketColumn>();
    [ObservableProperty] private IReadOnlyList<Hardwood.Core.Season.BracketColumn> _thirdBracket = Array.Empty<Hardwood.Core.Season.BracketColumn>();
    public bool HasMainBracket => MainBracket.Count > 0;
    public bool HasThirdBracket => ThirdBracket.Count > 0;
    partial void OnMainBracketChanged(IReadOnlyList<Hardwood.Core.Season.BracketColumn> value) => OnPropertyChanged(nameof(HasMainBracket));
    partial void OnThirdBracketChanged(IReadOnlyList<Hardwood.Core.Season.BracketColumn> value) => OnPropertyChanged(nameof(HasThirdBracket));
    [ObservableProperty] private string _bracketNote = "";
    public ObservableCollection<BookChampion> ChampionRows { get; } = new();
    public bool HasChampions => ChampionRows.Count > 0;
    public Team Mine => Session.HumanTeam;

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
        FillCards(b);
        var bracket = b.Bracket();
        MainBracket = LeagueBook.Columns(bracket.Where(x => !x.ThirdPlace));
        ThirdBracket = LeagueBook.Columns(bracket.Where(x => x.ThirdPlace));
        BracketNote = MainBracket.Count > 0 ? ""
            : b.Rules.FirstOrDefault(r => r.Label == "Playoffs").Value is { } po && (po.StartsWith("None") || po.Length == 0) ? po
            : b.Key == LeagueBook.CollegeKey ? "March Madness has its own bracket: College → March Madness."
            : "The bracket fills in here when the playoffs start.";
        ChampionRows.Clear();
        foreach (var c in b.ChampionRows().Take(100)) ChampionRows.Add(c);
        OnPropertyChanged(nameof(HasChampions));
    }

    /// <summary>
    /// The league's table as cards (Danny, #463): one per division (NBA and USA tiers: East on the left, West on the right),
    /// one per conference in college, or one card for a league with a single table. Ranks and games back are within the card;
    /// the chip colours are the league's own zones.
    /// </summary>
    private void FillCards(LeagueBook b)
    {
        LeftCards.Clear(); RightCards.Clear(); Keys.Clear();
        var me = Session.HumanTeam;
        bool college = b.Key == LeagueBook.CollegeKey;
        var groups = b.Table().GroupBy(r => r.Group).ToList();
        bool split = groups.Count > 1 && groups.All(g => g.Key.Contains(" · "));
        if (b.Key == LeagueBook.NablKey) groups = groups.OrderBy(g => g.Key.Split(" · ")[0]).ThenBy(g => StandingsViewModel.DivOrder(g.First().Team)).ThenBy(g => g.Key).ToList();
        string firstConf = split ? groups[0].Key.Split(" · ")[0] : "";
        int n = 0;
        foreach (var g in groups)
        {
            var rows = new List<StandingRow>();
            // College: ranked and games back on the conference record, overall record as the tiebreak (the card shows both).
            static (int w, int l) ConfRecord(BookTableRow r)
            {
                var parts = r.Zone.Replace(" in conf", "").Split('-');
                return parts.Length == 2 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var l) ? (w, l) : (0, 0);
            }
            var ordered = college
                ? g.OrderByDescending(r => { var (w, l) = ConfRecord(r); return w + l == 0 ? 0.5 : (double)w / (w + l); }).ThenByDescending(r => r.Pct).ThenBy(r => r.Team.City).ToList()
                : g.ToList();
            var lead = ordered[0];
            int k = 0;
            foreach (var r in ordered)
            {
                var (lw, ll) = college ? ConfRecord(lead) : (lead.W, lead.L);
                var (rw, rl) = college ? ConfRecord(r) : (r.W, r.L);
                double gb = ((lw - rw) + (rl - ll)) / 2.0;
                rows.Add(new StandingRow
                {
                    Rank = ++k, Team = r.Team, Label = college ? r.Team.City : null, W = r.W, L = r.L, Div = g.Key,
                    Pct = r.G == 0 ? ".000" : r.Pct.ToString(".000"), GB = gb <= 0 ? "-" : gb.ToString("0.#"),
                    Last10 = college ? r.Zone.Replace(" in conf", "") : r.G == 0 || (r.PointsFor == 0 && r.PointsAgainst == 0) ? "-" : r.Diff.ToString("+0.0;-0.0;0.0"),
                    Zone = college ? "" : r.Zone, IsHuman = r.Team == me,
                });
            }
            string title = split ? g.Key.Split(" · ", 2)[1] : g.Key.Length > 0 ? g.Key : b.Name;
            var card = new DivisionCard(title, rows) { LastHeader = college ? "Conf" : "Diff" };
            bool left = split ? g.Key.Split(" · ")[0] == firstConf : n % 2 == 0 || groups.Count == 1;
            (left ? LeftCards : RightCards).Add(card);
            n++;
        }
        string Conf(string c) => c is "East" ? "Eastern Conference" : c is "West" ? "Western Conference" : c;
        if (split)
        {
            LeftTitle = Conf(firstConf);
            RightTitle = Conf(groups.Select(g => g.Key.Split(" · ")[0]).FirstOrDefault(c => c != firstConf) ?? "");
        }
        else { LeftTitle = ""; RightTitle = ""; }
        HasRight = RightCards.Count > 0;
        var zones = LeftCards.Concat(RightCards).SelectMany(c => c.Rows).Select(r => r.Zone).Where(z => z.Length > 0).Distinct().ToHashSet();
        if (zones.Contains("Promotion")) Keys.Add(new ZoneKey(StandingRow.PromotionBrush, b.Key == LeagueBook.NablKey ? "Division winner" : "Division winner: promoted"));
        if (zones.Contains("Playoffs")) Keys.Add(new ZoneKey(StandingRow.PlayoffBrush, "Playoffs"));
        if (zones.Contains("Play-in")) Keys.Add(new ZoneKey(StandingRow.PlayInBrush, "Play-in"));
        if (zones.Contains("Third place")) Keys.Add(new ZoneKey(StandingRow.PlayInBrush, "Third-place tournament"));
        if (zones.Contains("Relegation")) Keys.Add(new ZoneKey(StandingRow.RelegationBrush, "Relegated"));
        if (college) Keys.Add(new ZoneKey(Avalonia.Media.Brushes.Transparent, "One card per conference, ranked on the conference record (Conf); W-L is the overall record"));
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
