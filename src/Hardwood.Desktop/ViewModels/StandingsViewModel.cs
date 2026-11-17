using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class StandingRow
{
    public int Rank { get; init; }
    public required Team Team { get; init; }
    /// <summary>A shorter name (a college program goes by its school, C2).</summary>
    public string? Label { get; init; }
    public string Name => Label ?? Team.FullName;
    /// <summary>The team link puts a college program's Top 25 rank before the name (#528); off in the Top 25 table itself.</summary>
    public bool ShowRank { get; init; } = true;
    public string Division => Team.Division;
    /// <summary>The division column (#318): the NABL's divisions, a pyramid league's, or blank.</summary>
    public string Div { get; init; } = "";
    public int W { get; init; }
    public int L { get; init; }
    public string Pct { get; init; } = "";
    public string GB { get; init; } = "";
    public string Diff { get; init; } = "";
    public string Home { get; init; } = "";
    public string Last10 { get; init; } = "";
    public bool IsHuman { get; init; }
    public string Zone { get; init; } = "";
    /// <summary>The rank chip's colour: green for a playoff place, amber for the play-in.</summary>
    public Avalonia.Media.IBrush ZoneBrush => Zone switch
    {
        "Playoffs" => PlayoffBrush,
        "Play-in" or "Third place" => PlayInBrush,
        "Promotion" => PromotionBrush,
        "Relegation" => RelegationBrush,
        _ => Avalonia.Media.Brushes.Transparent,
    };
    /// <summary>H85 (run 103): a zone whose meaning depends on the league (tier 2's winners and the NBA's last play the
    /// promotion playoff, #873) says so here instead of the general wording.</summary>
    public string? ZoneNote { get; init; }
    public string ZoneTip => ZoneNote ?? Zone switch
    {
        "" => "Outside the postseason places",
        "Third place" => "Third in the division: the third-place tournament",
        "Promotion" => "Division winner: promoted, and in the playoffs",
        _ => Zone,
    };
    /// <summary>Your club's row on a division card stands out.</summary>
    public Avalonia.Media.FontWeight Weight => IsHuman ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
    public Avalonia.Media.IBrush RowBrush => IsHuman ? HumanBrush : Avalonia.Media.Brushes.Transparent;
    public static readonly Avalonia.Media.IBrush HumanBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#332F6FB3"));
    /// <summary>W-L in one column on the division cards.</summary>
    public string Record => $"{W}-{L}";
    public static readonly Avalonia.Media.IBrush PromotionBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2F6FB3"));
    public static readonly Avalonia.Media.IBrush PlayoffBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2E8B57"));
    public static readonly Avalonia.Media.IBrush PlayInBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#B7832A"));
    public static readonly Avalonia.Media.IBrush RelegationBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#B23A3A"));

    /// <summary>Tier 2's name, for the promotion playoff's wording ("NBA G League").</summary>
    public static string Tier2Name(GameSession s) =>
        s.World.Competitions.FirstOrDefault(c => c.Key == Hardwood.Core.World.UsaPyramid.Tier2)?.Profile.Name ?? "tier 2";
    /// <summary>H85 (run 103): tier 2's division winners don't go straight up any more (#873).</summary>
    public static string PromotionPlayoffKey => $"Division winner: promotion playoff v the {Hardwood.Core.Names.Brand.Top}'s last (best of 3)";
    /// <summary>H85 (run 103): the NBA's last in each division plays for its place (#873). Short for the legend chip.</summary>
    public const string StayUpKey = "Last in division: promotion playoff to stay up (best of 3)";
    /// <summary>The same, in full, for a row's tooltip.</summary>
    public static string StayUpTip(string tier2) => $"Last in division: plays the {tier2} division winner, best of 3, to stay up";
    /// <summary>Said once on the page, not on every chip (H85).</summary>
    public const string LevelRecords = "Level records: point difference decides.";
}

/// <summary>One four-team division as its own small table (Danny, #448 / D55: "a view that breaks out each 4some division").</summary>
public sealed record DivisionCard(string Title, IReadOnlyList<StandingRow> Rows)
{
    /// <summary>The last column's heading: the last ten games on Standings; the points difference (or conference record) on Leagues.</summary>
    public string LastHeader { get; init; } = "L10";
}

public sealed partial class StandingsViewModel : PageViewModel
{
    /// <summary>The division cards, East (or a lower tier's first conference) on the left.</summary>
    public ObservableCollection<DivisionCard> LeftCards { get; } = new();
    public ObservableCollection<DivisionCard> RightCards { get; } = new();
    /// <summary>Cards instead of the two conference tables.</summary>
    [ObservableProperty] private bool _showCards;
    public bool ShowTables => !ShowCards;
    partial void OnShowCardsChanged(bool value) => OnPropertyChanged(nameof(ShowTables));
    public override string Title => "Standings";
    public ObservableCollection<StandingRow> East { get; } = new();
    public ObservableCollection<StandingRow> West { get; } = new();
    [ObservableProperty] private string _note = "";
    /// <summary>The green chip's meaning: the playoffs, or a college conference tournament.</summary>
    [ObservableProperty] private string _playoffLabel = "Playoffs";
    [ObservableProperty] private bool _showPlayIn = true;
    /// <summary>The amber chip's meaning: the play-in, or the third-place tournament in the division format (D36).</summary>
    [ObservableProperty] private string _playInLabel = "Play-in";
    [ObservableProperty] private bool _showPromotion;
    /// <summary>H85 (run 103): the blue and red chips' meaning, which differs in tier 2 and the NBA (#873).</summary>
    [ObservableProperty] private string _promotionLabel = "Division winner: promoted";
    [ObservableProperty] private string _relegationLabel = "Last in division: relegated";
    [ObservableProperty] private string _leftTitle = "Eastern Conference";
    [ObservableProperty] private string _rightTitle = "Western Conference";
    /// <summary>Division cards (Danny, #448: they open first, D55 A) or the conference tables with the playoff seeds.</summary>
    public string[] Views { get; } = { "By division", "By conference" };
    [ObservableProperty] private string _view = "By division";
    [ObservableProperty] private bool _canDivide;
    [ObservableProperty] private bool _showRelegation;
    partial void OnViewChanged(string value) => Refresh();
    private bool ByDivision => View == "By division";

    /// <summary>A division's place in its conference's order (Danny's map: Cascade, Mojave, Frontier, Gulf...), else last.</summary>
    internal static int DivOrder(Team t)
    {
        int i = Array.IndexOf(Hardwood.Core.World.UsaPyramid.DivisionNames(t.Conference, true), t.Division);
        if (i < 0) i = Array.IndexOf(Hardwood.Core.World.UsaPyramid.DivisionNames(t.Conference, false), t.Division);
        return i < 0 ? 99 : i;
    }

    /// <summary>"East" → "Eastern Conference"; other names as they are.</summary>
    public static string ConfName(string c) => c is "East" ? "Eastern Conference" : c is "West" ? "Western Conference" : c;

    private static IEnumerable<IGrouping<string, StandingRow>> InOrder(IEnumerable<StandingRow> rows) =>
        rows.GroupBy(r => r.Div).OrderBy(g => DivOrder(g.First().Team)).ThenBy(g => g.Key);
    public IReadOnlyList<Hardwood.Desktop.Controls.LineSeries> Race { get; private set; } = Array.Empty<Hardwood.Desktop.Controls.LineSeries>();
    public string[] RaceLabels { get; private set; } = Array.Empty<string>();
    public bool HasRace => RaceLabels.Length >= 3;

    public StandingsViewModel(MainViewModel shell) : base(shell) { }

    public override void Refresh()
    {
        var s = Session;
        if (s.InCollege && s.World.College is { } cw)
        {
            // College (C2): your conference on the left (its top eight play the conference tournament), the Top 25 on the right.
            FillRecords(East, s.MyStandings.All(), r => r <= 8 ? "Playoffs" : "", school: true);
            var top = cw.Top25().Select(s.CollegeRecord).ToList();
            FillRecords(West, top, _ => "");
            for (int i = 0; i < West.Count; i++) West[i] = new StandingRow
            {
                Rank = i + 1, Team = West[i].Team, Label = West[i].Team.City, W = West[i].W, L = West[i].L, Pct = West[i].Pct, GB = "", Diff = West[i].Diff,
                Home = West[i].Home, Last10 = West[i].Last10, IsHuman = West[i].IsHuman, ShowRank = false,
            };
            PlayoffLabel = "Conference tournament"; ShowPlayIn = false; ShowCards = false;
            LeftTitle = $"{s.MyConference!.Name} (conference games)"; RightTitle = "Top 25 (by rating, overall records)";
            Note = "Tournament winners get automatic March Madness bids.";
            Race = Array.Empty<Hardwood.Desktop.Controls.LineSeries>(); RaceLabels = Array.Empty<string>();
            OnPropertyChanged(nameof(Race)); OnPropertyChanged(nameof(RaceLabels)); OnPropertyChanged(nameof(HasRace));
            return;
        }
        PlayoffLabel = "Playoffs"; ShowPlayIn = true; CanDivide = false; ShowRelegation = false; ShowPromotion = false; PlayInLabel = "Play-in";
        PromotionLabel = "Division winner: promoted"; RelegationLabel = "Last in division: relegated";
        ShowCards = false; LeftCards.Clear(); RightCards.Clear();
        if (s.HumanLeague is { } hl)
        {
            // Abroad: your league on the left, the NABL on the right.
            string? cardsLeft = null, cardsRight = null;
            int playoff = hl.Profile.Playoffs.Length > 0 ? hl.Profile.Playoffs[0].Teams : 0;
            FillRecords(East, s.MyStandings.All(), r => r <= playoff ? "Playoffs" : "");
            FillRecords(West, s.Standings.All(), _ => "");
            if (Hardwood.Core.World.UsaPyramid.IsPyramid(hl.Key))
            {
                // A pyramid league by division (#318-#319), with its promotion and relegation places.
                var book = Hardwood.Core.Season.LeagueBook.Get(s, hl.Key);
                bool divisional = hl.Profile.DivisionPlayoffs && hl.Clubs.Count == Hardwood.Core.World.UsaPyramid.ProSize;
                bool tier2 = hl.Key == Hardwood.Core.World.UsaPyramid.Tier2 && Hardwood.Core.World.UsaPyramid.PlaysPromotion(s.World, s.League);   // review: only when the playoff runs
                if (book != null)
                {
                    var rows = book.Table();
                    East.Clear();
                    foreach (var g in rows.GroupBy(r => r.Group))
                    {
                        int k = 0; var lead = g.First();
                        foreach (var r in g)
                        {
                            double gb = ((lead.W - r.W) + (r.L - lead.L)) / 2.0;
                            East.Add(new StandingRow
                            {
                                Rank = ++k, Team = r.Team, W = r.W, L = r.L, Div = g.Key, Pct = r.G == 0 ? ".000" : r.Pct.ToString(".000"), GB = gb <= 0 ? "-" : gb.ToString("0.#"),
                                Diff = r.G == 0 ? "-" : r.Diff.ToString("+0.0;-0.0;0.0"), Zone = divisional || r.Zone != "Promotion" ? r.Zone : "Playoffs", IsHuman = r.Team == s.HumanTeam,
                                ZoneNote = tier2 && divisional && r.Zone == "Promotion" ? StandingRow.PromotionPlayoffKey : null,
                            });
                        }
                    }
                    if (divisional)
                    {
                        // Tiers 2-5 in the division format (D36, D39 A): winners go up and play the playoffs, 2nd plays them, 3rd the
                        // third-place tournament, 4th goes down.
                        // Tier 5 is the bottom (D39 A): nobody goes down from it.
                        ShowPromotion = true; PlayoffLabel = "Playoffs"; ShowPlayIn = true; PlayInLabel = "Third-place tournament"; ShowRelegation = hl.Key != Hardwood.Core.World.UsaPyramid.Tier5;
                        // H85 (run 103): tier 2's winners play the NBA's last for the place (#873); its runners-up play its playoffs.
                        if (tier2) { PromotionLabel = StandingRow.PromotionPlayoffKey; PlayoffLabel = "Playoffs (runners-up)"; }
                        // The division cards (D55): one per division, the first conference's on the left.
                        CanDivide = true;
                        if (ByDivision)
                        {
                            var groups = InOrder(East).ToList();
                            string firstConf = groups.Count > 0 ? groups[0].Key.Split(" · ")[0] : "";
                            foreach (var g in groups)
                                (g.Key.Split(" · ")[0] == firstConf ? LeftCards : RightCards).Add(new DivisionCard(g.Key, g.ToList()));
                            ShowCards = true;
                            // H21: both columns are your league's conferences, not the NBA (the table view's right-hand list).
                            string second = groups.Select(g => g.Key.Split(" · ")[0]).FirstOrDefault(c => c != firstConf) ?? "";
                            cardsLeft = $"{hl.Profile.Name} · {ConfName(firstConf)}";
                            cardsRight = second.Length > 0 ? $"{hl.Profile.Name} · {ConfName(second)}" : "";
                        }
                    }
                    else { PlayoffLabel = "Promotion"; ShowPlayIn = false; ShowRelegation = true; }
                }
            }
            LeftTitle = cardsLeft ?? hl.Profile.Name; RightTitle = cardsRight ?? Hardwood.Core.Names.Brand.Top;
            Note = hl.Profile.DivisionPlayoffs
                ? (hl.Clubs.Count == Hardwood.Core.World.UsaPyramid.ProSize ? "Colours by place in each division. " + StandingRow.LevelRecords : "The table decides the title this season.")
                : playoff > 0 ? $"Top {playoff} reach the playoffs ({string.Join(", ", hl.Profile.Playoffs.Select(p => p.Name.ToLower()))})." : "";
            Race = Array.Empty<Hardwood.Desktop.Controls.LineSeries>(); RaceLabels = Array.Empty<string>();
            OnPropertyChanged(nameof(Race)); OnPropertyChanged(nameof(RaceLabels)); OnPropertyChanged(nameof(HasRace));
            return;
        }
        LeftTitle = "Eastern Conference"; RightTitle = "Western Conference";
        CanDivide = true;
        bool divisions = Postseason.UsesDivisions(s.League);
        // Relegation shows in the division view, and in the division format (4th goes down) in either view.
        ShowRelegation = (ByDivision || divisions) && s.World.Competitions.Any(c => c.Key == Hardwood.Core.World.UsaPyramid.Tier2);
        if (divisions) PlayInLabel = "Third-place tournament";
        string t2 = StandingRow.Tier2Name(s);
        bool promoPlayoff = Hardwood.Core.World.UsaPyramid.PlaysPromotion(s.World, s.League);   // review: an older layout keeps the straight swap
        if (ShowRelegation && promoPlayoff) RelegationLabel = StandingRow.StayUpKey;   // H85 (run 103, #873)
        Fill(East, Conference.East, divisions);
        Fill(West, Conference.West, divisions);
        if (ByDivision)
        {
            foreach (var g in InOrder(East)) LeftCards.Add(new DivisionCard($"{g.Key}", g.ToList()));
            foreach (var g in InOrder(West)) RightCards.Add(new DivisionCard($"{g.Key}", g.ToList()));
            ShowCards = true;
        }
        Note = divisions
            ? (ByDivision ? $"Each division's top two make the playoffs, third plays the third-place tournament, {(promoPlayoff ? $"fourth plays the {t2} division winner (best of 3) to stay up" : "fourth goes down")}. {StandingRow.LevelRecords}" : "Colours by place in each division; seeds by record.")
            : ByDivision ? "By division: ranks and games back within each division; the playoff colours still follow the conference table."
            : "Top 6 qualify directly; 7-10 play in.";
        BuildRace();
    }

    /// <summary>Games above .500 by week for the human team's conference; your team is emphasised.</summary>
    private void BuildRace()
    {
        var s = Session; var conf = s.HumanTeam.Conference;
        var played = s.Schedule.Where(g => s.Results.ContainsKey(g.Index)).OrderBy(g => g.Date).ToList();
        if (played.Count < 15) { Race = Array.Empty<Hardwood.Desktop.Controls.LineSeries>(); RaceLabels = Array.Empty<string>(); OnPropertyChanged(nameof(HasRace)); return; }
        var start = played.First().Date; var end = played.Last().Date;
        var weeks = new List<DateOnly>();
        for (var d = start.AddDays(6); d <= end; d = d.AddDays(7)) weeks.Add(d);
        if (weeks.Count == 0 || weeks.Last() < end) weeks.Add(end);
        var teams = s.League.Teams.Where(t => t.Conference == conf).ToList();
        var list = new List<Hardwood.Desktop.Controls.LineSeries>();
        foreach (var t in teams)
        {
            var values = new double[weeks.Count];
            int w = 0, l = 0, k = 0;
            foreach (var g in played)
            {
                if (g.Home != t && g.Away != t) { }
                else if (s.Results[g.Index].Winner.Team == t) w++; else l++;
                while (k < weeks.Count && g.Date > weeks[k]) { values[k] = w - l; k++; }
            }
            for (; k < weeks.Count; k++) values[k] = w - l;
            list.Add(new Hardwood.Desktop.Controls.LineSeries { Name = t.Abbreviation, Values = values, Color = Avalonia.Media.Color.Parse(t.PrimaryColor), Emphasis = t == s.HumanTeam });
        }
        Race = list;
        RaceLabels = weeks.Select(d => d.ToString("MMM d")).ToArray();
        OnPropertyChanged(nameof(Race)); OnPropertyChanged(nameof(RaceLabels)); OnPropertyChanged(nameof(HasRace));
    }

    /// <summary>A plain table (a league abroad, or the whole NABL).</summary>
    private void FillRecords(ObservableCollection<StandingRow> list, List<TeamRecord> table, Func<int, string> zone, bool school = false)
    {
        list.Clear();
        var leader = table.FirstOrDefault();
        int rank = 1;
        foreach (var r in table)
        {
            double gb = leader == null ? 0 : ((leader.Wins - r.Wins) + (r.Losses - leader.Losses)) / 2.0;
            list.Add(new StandingRow
            {
                Rank = rank, Team = r.Team, Label = school ? r.Team.City : null, W = r.Wins, L = r.Losses, Zone = zone(rank),
                Pct = r.WinPct.ToString(".000"), GB = gb <= 0 ? "-" : gb.ToString("0.#"),
                Diff = r.PointDiff.ToString("+0.0;-0.0;0.0"), Home = $"{r.HomeWins}-{r.HomeLosses}",
                IsHuman = r.Team == Session.HumanTeam,
            });
            rank++;
        }
    }

    private void Fill(ObservableCollection<StandingRow> list, Conference conf, bool divisions = false)
    {
        list.Clear();
        var confTable = Session.Standings.Conference(conf);
        // The division format (D36): a team's zone comes from its place in its division, whichever way the table is shown.
        var placeInDivision = confTable.GroupBy(r => r.Team.Division).SelectMany(g => g.Select((r, i) => (r.Team, i))).ToDictionary(x => x.Team, x => x.i + 1);
        var confRank = confTable.Select((r, i) => (r.Team, i)).ToDictionary(x => x.Team, x => x.i + 1);
        var table = ByDivision ? confTable.GroupBy(r => r.Team.Division).OrderBy(g => g.Key).SelectMany(g => g).ToList() : confTable;
        var leader = table.FirstOrDefault();
        int rank = 1;
        string? division = null;
        foreach (var r in table)
        {
            if (ByDivision && r.Team.Division != division) { division = r.Team.Division; leader = r; rank = 1; }
            int cr = confRank[r.Team];
            bool lastInDivision = ShowRelegation && table.LastOrDefault(x => x.Team.Division == r.Team.Division) == r && r.Wins + r.Losses > 0;
            double gb = leader == null ? 0 : ((leader.Wins - r.Wins) + (r.Losses - leader.Losses)) / 2.0;
            var last10 = Session.Schedule.Where(g => (g.Home == r.Team || g.Away == r.Team) && Session.Results.ContainsKey(g.Index))
                .OrderByDescending(g => g.Index).Take(10).Select(g => Session.Results[g.Index].Winner.Team == r.Team).ToList();
            list.Add(new StandingRow
            {
                Rank = rank, Zone = lastInDivision ? "Relegation" : divisions ? LeagueBook.DivisionZone(placeInDivision[r.Team]) : cr <= 6 ? "Playoffs" : cr <= 10 ? "Play-in" : "", Team = r.Team, W = r.Wins, L = r.Losses, Div = r.Team.Division,
                Pct = r.WinPct.ToString(".000"), GB = gb <= 0 ? "-" : gb.ToString("0.#"),
                Diff = r.PointDiff.ToString("+0.0;-0.0;0.0"), Home = $"{r.HomeWins}-{r.HomeLosses}",
                Last10 = last10.Count > 0 ? $"{last10.Count(x => x)}-{last10.Count(x => !x)}" : "",
                IsHuman = r.Team == Session.HumanTeam,
                ZoneNote = lastInDivision && Hardwood.Core.World.UsaPyramid.PlaysPromotion(Session.World, Session.League) ? StandingRow.StayUpTip(StandingRow.Tier2Name(Session)) : null,
            });
            rank++;
        }
    }
}

public sealed class LeaderRow
{
    public int Rank { get; init; }
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public string Team { get; init; } = "";
    public string Pos => Player.PrimaryPosition.ToString();
    public int GP { get; init; }
    public double MPG { get; init; }
    public double PPG { get; init; }
    public double RPG { get; init; }
    public double APG { get; init; }
    public double SPG { get; init; }
    public double BPG { get; init; }
    public string FG { get; init; } = "";
    public string TP { get; init; } = "";
    public double Value { get; init; }
}

public sealed partial class LeadersViewModel : PageViewModel
{
    public override string Title => "League Leaders";
    public ObservableCollection<LeaderRow> Rows { get; } = new();
    public string[] Categories { get; } = { "Points", "Rebounds", "Assists", "Steals", "Blocks", "Minutes", "FG%", "3P%" };
    [ObservableProperty] private string _category = "Points";
    [ObservableProperty] private string _note = "";
    /// <summary>The green chip's meaning: the playoffs, or a college conference tournament.</summary>
    [ObservableProperty] private string _playoffLabel = "Playoffs";
    [ObservableProperty] private bool _showPlayIn = true;

    // Run 64 (review #13 rank 3, #464): leader cards first, the full table one click away (as Standings, D55 A).
    public string[] Views { get; } = { "Cards", "Full table" };
    [ObservableProperty] private string _view = "Cards";
    public bool ShowCards => View == "Cards";
    public bool ShowTable => !ShowCards;
    public ObservableCollection<LeaderCard> Cards { get; } = new();
    partial void OnViewChanged(string value) { OnPropertyChanged(nameof(ShowCards)); OnPropertyChanged(nameof(ShowTable)); }

    // Run 71 (bug hunt H13): the page shows your own league's leaders (a G League or college coach saw the NBA's and
    // the page never said so). The League box lists your league first and the NBA second; the note names the league.
    public ObservableCollection<string> LeagueNames { get; } = new();
    [ObservableProperty] private string? _leagueName;
    public bool HasLeagueChoice => LeagueNames.Count > 1;
    private readonly Dictionary<string, string> _keys = new();
    partial void OnLeagueNameChanged(string? value) { if (!_filling) Refresh(); }
    private bool _filling;
    private string? _homeKey;

    public LeadersViewModel(MainViewModel shell) : base(shell) => FillLeagues();
    partial void OnCategoryChanged(string value) => FillTable();

    /// <summary>Your league first (where your club plays), then the NBA when that isn't yours.</summary>
    private void FillLeagues()
    {
        var s = Session;
        _filling = true;
        LeagueNames.Clear(); _keys.Clear();
        _homeKey = LeagueBook.HomeOf(s, s.HumanTeam)?.Key;
        void Add(LeagueBook? b) { if (b != null && !_keys.ContainsValue(b.Key)) { LeagueNames.Add(b.Name); _keys[b.Name] = b.Key; } }
        Add(LeagueBook.HomeOf(s, s.HumanTeam));
        Add(LeagueBook.Get(s, LeagueBook.NablKey));
        LeagueName = LeagueNames.FirstOrDefault();
        _filling = false;
        OnPropertyChanged(nameof(HasLeagueChoice));
    }

    /// <summary>The key of the league on show (the NBA when nothing else is known).</summary>
    public string LeagueKey => LeagueName != null && _keys.TryGetValue(LeagueName, out var k) ? k : LeagueBook.NablKey;

    /// <summary>A card's "Full list": that stat as the table.</summary>
    public void ShowFull(string category) { Category = category; View = "Full table"; }

    private int _minGames = 1;

    /// <summary>One player's season in the league on show, in one shape whatever league runs it.</summary>
    private sealed record Line(Player Player, int TeamId, int Games, double Mpg, double Ppg, double Rpg, double Apg, double Spg, double Bpg,
        double FgPct, double TpPct, int Fga, int Tpa);
    private List<Line> _qualified = new();

    /// <summary>The number a stat ranks by; 0 for a shooter without enough attempts.</summary>
    public static Func<PlayerSeasonStats, double> Key(string category) => category switch
    {
        "Rebounds" => x => x.Rpg, "Assists" => x => x.Apg, "Steals" => x => (double)x.Steals / x.Games, "Blocks" => x => (double)x.Blocks / x.Games,
        "Minutes" => x => x.Mpg, "FG%" => x => x.FGA >= x.Games * 5 ? x.FgPct : 0, "3P%" => x => x.ThreePA >= x.Games * 2 ? x.ThreePct : 0,
        _ => x => x.Ppg,
    };

    private static Func<Line, double> LineKey(string category) => category switch
    {
        "Rebounds" => x => x.Rpg, "Assists" => x => x.Apg, "Steals" => x => x.Spg, "Blocks" => x => x.Bpg,
        "Minutes" => x => x.Mpg, "FG%" => x => x.Fga >= x.Games * 5 ? x.FgPct : 0, "3P%" => x => x.Tpa >= x.Games * 2 ? x.TpPct : 0,
        _ => x => x.Ppg,
    };

    public override void Refresh()
    {
        var s = Session;
        // A job move changes your league (code review): the page is cached, so rebuild the list when it does.
        if (LeagueName == null || !LeagueNames.Contains(LeagueName) || LeagueBook.HomeOf(s, s.HumanTeam)?.Key != _homeKey) FillLeagues();
        var players = s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.World.Players)
            .Concat(s.World.College?.Players ?? Enumerable.Empty<Player>())
            .GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        if (LeagueKey == LeagueBook.NablKey)
        {
            var all = s.Standings.All().ToList();
            _minGames = Math.Max(1, (int)((all.Count == 0 ? 0 : all.Average(r => r.Games)) * 0.6));
            _qualified = s.Stats.Values.Where(x => x.Games >= _minGames && players.ContainsKey(x.PlayerId))
                .Select(x => new Line(players[x.PlayerId], x.TeamId, x.Games, x.Mpg, x.Ppg, x.Rpg, x.Apg, (double)x.Steals / x.Games,
                    (double)x.Blocks / x.Games, x.FgPct, x.ThreePct, x.FGA, x.ThreePA)).ToList();
        }
        else
        {
            var stats = LeagueBook.Get(s, LeagueKey)?.Stats() ?? new List<BookStat>();
            // As on Leagues → Stats: 40% of the most games anyone has played.
            int maxG = stats.Select(x => x.G).DefaultIfEmpty(0).Max();
            _minGames = Math.Max(1, (int)(maxG * 0.4));
            _qualified = stats.Where(x => x.G >= _minGames && players.ContainsKey(x.PlayerId))
                .Select(x => new Line(players[x.PlayerId], x.TeamId, x.G, x.Mpg, x.Ppg, x.Rpg, x.Apg, x.Spg, x.Bpg, x.FgPct, x.TpPct, x.Fga, x.Tpa))
                .ToList();
        }
        FillCards();
        FillTable();
        var name = LeagueName ?? "";
        Note = Cards.Any(c => c.HasAny)
            ? $"{name} · minimum {_minGames} games played. The top {LeaderCards.PerCard} in each; Full list opens the top 40."
            : $"{name} · the leaders fill in after the first games.";
    }

    private void FillCards()
    {
        Cards.Clear();
        var s = Session;
        foreach (var (cat, title, unit) in LeaderCards.Stats)
        {
            var key = LineKey(cat);
            int rank = 0;
            var entries = _qualified.Where(q => key(q) > 0).OrderByDescending(key).Take(LeaderCards.PerCard)
                .Select(q => new LeaderEntry
                {
                    Rank = ++rank, Player = q.Player, Name = q.Player.Name, Team = s.FindTeam(q.TeamId),
                    Value = LeaderCards.IsPct(cat) ? LeaderCards.Pct(key(q)) : LeaderCards.PerGame(key(q)),
                    Ours = q.TeamId == s.HumanTeam.Id,
                }).ToList();
            string c = cat;
            Cards.Add(new LeaderCard { Category = cat, Title = title, Unit = unit, Entries = entries, ShowAll = new RelayCommand(() => ShowFull(c)) });
        }
    }

    private void FillTable()
    {
        Rows.Clear();
        var s = Session;
        var key = LineKey(Category);
        int rank = 1;
        foreach (var x in _qualified.OrderByDescending(key).Take(40))
        {
            Rows.Add(new LeaderRow
            {
                Rank = rank++, Player = x.Player, Team = s.FindTeam(x.TeamId)?.Abbreviation ?? "", GP = x.Games,
                MPG = Math.Round(x.Mpg, 1), PPG = Math.Round(x.Ppg, 1), RPG = Math.Round(x.Rpg, 1), APG = Math.Round(x.Apg, 1),
                SPG = Math.Round(x.Spg, 1), BPG = Math.Round(x.Bpg, 1),
                FG = $"{x.FgPct * 100:F1}", TP = x.Tpa > 0 ? $"{x.TpPct * 100:F1}" : "-", Value = Math.Round(key(x), 3),
            });
        }
    }
}
