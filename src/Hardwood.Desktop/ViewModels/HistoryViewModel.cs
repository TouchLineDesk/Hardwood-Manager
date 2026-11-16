using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class HistoryRow
{
    public string Season { get; init; } = "";
    public string Champion { get; init; } = "";
    public string RunnerUp { get; init; } = "";
    public string Mvp { get; init; } = "";
    public string ScoringLeader { get; init; } = "";
    public string FinalsMvp { get; init; } = "";
    public string OurRecord { get; init; } = "";
}

/// <summary>Run 76 (review #13 rank 15): one season as a champion's card: the crest, the club's colour as a stripe,
/// who they beat and the season's MVPs. Used for this career's seasons and for the real NBA's recent past.</summary>
public sealed class SeasonCard
{
    public string Season { get; init; } = "";
    public Team? Champion { get; init; }
    public string ChampionName { get; init; } = "";
    public string Beat { get; init; } = "";
    public string Mvp { get; init; } = "";
    public string FinalsMvp { get; init; } = "";
    public string Extra { get; init; } = "";
    public bool HasExtra => Extra.Length > 0;
    public string OurRecord { get; init; } = "";
    public bool HasOurRecord => OurRecord.Length > 0;
    public bool HasCrest => Champion != null;
    public bool Mine { get; init; }
    public IBrush Stripe { get; init; } = Brushes.Gray;
    public string Tip => $"{Season}: {ChampionName} {Beat}. Finals MVP {FinalsMvp}. MVP {Mvp}.";
}

/// <summary>A real NBA season (R1, #191): the History page's "NBA history" tab.</summary>
public sealed record RealSeasonRow(string Season, string Champion, string RunnerUp, string FinalsMvp, string Mvp, bool Ours);

/// <summary>A real NBA record card: the mark, its holder and when; run 76 adds his photo when he's in the game, else the
/// club's crest, and the club's colour as a stripe.</summary>
public sealed record RealRecordCard(string Label, string Value, string Holder, string When)
{
    public Player? Player { get; init; }
    public Team? Club { get; init; }
    public IBrush Stripe { get; init; } = new SolidColorBrush(ThemeService.DefaultAccent);
    public bool HasPhoto => Player != null;
    public bool HasCrest => Player == null && Club != null;
    public bool HasIcon => Player == null && Club == null;
    public bool Broken => Holder.EndsWith("· new record", StringComparison.Ordinal);
}

/// <summary>A franchise's real titles, with its crest and colour (run 76).</summary>
public sealed record RealTitlesRow(string Team, int Titles, string Last, bool Ours)
{
    public Team? Club { get; init; }
    public IBrush Stripe { get; init; } = Brushes.Gray;
}

/// <summary>One line of a records table: rank, the number, who and when.</summary>
public sealed record RecordRow(int Rank, string Value, string Who, string When, bool Ours);

/// <summary>A records-book table (roadmap 4): "Points in a game", top five. Run 76: the holder (#1) leads the card with his
/// photo (or the club's crest for a team record) and the club's colour; the rest follow as lines.</summary>
public sealed record RecordTable(string Title, IReadOnlyList<RecordRow> Rows)
{
    public bool Empty => Rows.Count == 0;
    public Player? Leader { get; init; }
    public Team? LeaderClub { get; init; }
    public IBrush Stripe { get; init; } = new SolidColorBrush(Color.Parse("#2A323D"));
    public RecordRow? Top => Rows.Count > 0 ? Rows[0] : null;
    public bool HasTop => Rows.Count > 0;
    public IReadOnlyList<RecordRow> Rest => Rows.Skip(1).ToList();
    public bool HasPhoto => Leader != null;
    public bool HasCrest => Leader == null && LeaderClub != null;
    public bool TopOurs => Rows.Count > 0 && Rows[0].Ours;
}

public sealed partial class HistoryViewModel : PageViewModel
{
    public override string Title => "History";
    public ObservableCollection<HistoryRow> Rows { get; } = new();
    [ObservableProperty] private string _summary = "";
    // The living history, part 1 (roadmap 4): records book and milestones.
    [ObservableProperty] private List<RecordTable> _gameRecords = new();
    [ObservableProperty] private List<RecordTable> _seasonRecords = new();
    [ObservableProperty] private List<RecordTable> _careerRecords = new();
    [ObservableProperty] private List<string> _milestones = new();
    // Run 76: a record not yet set is listed in one line under its section, not as an empty card.
    public List<RecordTable> GameRecordsSet => GameRecords.Where(t => !t.Empty).ToList();
    public List<RecordTable> SeasonRecordsSet => SeasonRecords.Where(t => !t.Empty).ToList();
    public List<RecordTable> CareerRecordsSet => CareerRecords.Where(t => !t.Empty).ToList();
    public string GameRecordsToSet => ToSet(GameRecords);
    public string SeasonRecordsToSet => ToSet(SeasonRecords);
    public string CareerRecordsToSet => ToSet(CareerRecords);
    private static string ToSet(List<RecordTable> tables) => tables.Where(t => t.Empty).Select(t => t.Title).ToList() is { Count: > 0 } left
        ? (left.Count == tables.Count ? "No records yet. Each is set the first time it happens: " : "Still to be set: ") + string.Join(" · ", left) + "."
        : "";
    partial void OnGameRecordsChanged(List<RecordTable> value) { OnPropertyChanged(nameof(GameRecordsSet)); OnPropertyChanged(nameof(GameRecordsToSet)); }
    partial void OnSeasonRecordsChanged(List<RecordTable> value) { OnPropertyChanged(nameof(SeasonRecordsSet)); OnPropertyChanged(nameof(SeasonRecordsToSet)); }
    partial void OnCareerRecordsChanged(List<RecordTable> value) { OnPropertyChanged(nameof(CareerRecordsSet)); OnPropertyChanged(nameof(CareerRecordsToSet)); }
    public bool HasMilestones => Milestones.Count > 0;
    partial void OnMilestonesChanged(List<string> value) => OnPropertyChanged(nameof(HasMilestones));

    // Run 76 (review #13 rank 15): Seasons opens on champion cards; the table is one switch away.
    public string[] Views { get; } = { "Cards", "Full table" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowCards), nameof(ShowTable))] private string _view = "Cards";
    public bool ShowCards => View == "Cards";
    public bool ShowTable => !ShowCards;
    [ObservableProperty] private List<SeasonCard> _seasonCards = new();
    /// <summary>In a real career, the real NBA's last ten champions under this career's own (empty otherwise).</summary>
    [ObservableProperty] private List<SeasonCard> _realPastCards = new();
    [ObservableProperty] private string _realPastTitle = "";
    [ObservableProperty] private string _seasonsNote = "";
    public bool HasSeasonCards => SeasonCards.Count > 0;
    public bool HasRealPast => RealPastCards.Count > 0;
    public bool HasSeasonsNote => SeasonsNote.Length > 0;
    partial void OnSeasonCardsChanged(List<SeasonCard> value) => OnPropertyChanged(nameof(HasSeasonCards));
    partial void OnRealPastCardsChanged(List<SeasonCard> value) => OnPropertyChanged(nameof(HasRealPast));
    partial void OnSeasonsNoteChanged(string value) => OnPropertyChanged(nameof(HasSeasonsNote));

    public HistoryViewModel(MainViewModel shell) : base(shell) { }

    // The real NBA's past (R1, #191), shown in a real-world career.
    [ObservableProperty] private bool _isReal;
    [ObservableProperty] private int _tab;
    [ObservableProperty] private List<RealSeasonRow> _realSeasons = new();
    [ObservableProperty] private List<RealRecordCard> _realGameRecords = new();
    [ObservableProperty] private List<RealRecordCard> _realSeasonRecords = new();
    [ObservableProperty] private List<RealRecordCard> _realCareerRecords = new();
    [ObservableProperty] private List<RealTitlesRow> _realTitles = new();
    [ObservableProperty] private string _realSummary = "";
    public string RealSource => $"Checked {RealWorldData.HistoryChecked}. {RealWorldData.HistorySource}";

    private static IBrush StripeFor(Team? club) => new SolidColorBrush(club != null ? ThemeService.AccentFor(club) : ThemeService.DefaultAccent);

    /// <summary>The club in a real franchise's place today: the 30 franchises keep ids 1-30 wherever the pyramid sends them.</summary>
    private Team? Franchise(string? abbr)
    {
        if (abbr == null) return null;
        int i = RealWorldData.Teams.ToList().FindIndex(t => t.Abbr == abbr);
        return i < 0 ? null : Session.FindTeam(i + 1);
    }

    /// <summary>Old names in the record book, as today's franchises (the history file's own rule: Seattle SuperSonics = OKC).</summary>
    private static readonly Dictionary<string, string> OldNames = new()
    {
        ["Philadelphia Warriors"] = "GSW", ["San Francisco Warriors"] = "GSW", ["New Jersey Nets"] = "BKN", ["Charlotte Bobcats"] = "CHA",
        ["Seattle SuperSonics"] = "OKC", ["Minneapolis Lakers"] = "LAL", ["Syracuse Nationals"] = "PHI", ["Vancouver Grizzlies"] = "MEM",
        ["New Orleans Hornets"] = "NOP", ["Washington Bullets"] = "WAS", ["Kansas City Kings"] = "SAC", ["San Diego Clippers"] = "LAC",
        ["Buffalo Braves"] = "LAC", ["New Orleans Jazz"] = "UTA", ["St. Louis Hawks"] = "ATL", ["Fort Wayne Pistons"] = "DET",
    };

    /// <summary>A record's club by its real name ("Utah Jazz", or an old name such as "Philadelphia Warriors"), as the club in
    /// that franchise's place today. Two holders ("San Antonio Spurs; New Jersey Nets") have no single crest.</summary>
    private Team? ClubNamed(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains(';')) return null;
        if (OldNames.TryGetValue(name, out var abbr)) return Franchise(abbr);
        var real = RealWorldData.Teams.FirstOrDefault(t => t.FullName == name);
        return real == null ? null : Franchise(real.Abbr);
    }

    private void BuildReal(Dictionary<int, Player> players)
    {
        var s = Session;
        IsReal = RealWorldData.IsReal(s.League);
        RealPastCards = new();
        RealPastTitle = "";
        if (!IsReal) return;
        // The real past is tied to today's franchises: the club in the Celtics' place inherits Boston's history.
        var abbrs = RealWorldData.Teams.Select(t => t.Abbr).ToList();
        string? ours = s.HumanTeam.Id is >= 1 and <= 30 && s.HumanTeam.Id <= abbrs.Count && s.Employed ? abbrs[s.HumanTeam.Id - 1] : null;
        RealSeasons = RealWorldData.Seasons.OrderByDescending(x => x.Season)
            .Select(x => new RealSeasonRow(x.Label, x.Champion, x.RunnerUp, x.FinalsMvp ?? "-", x.Mvp ?? "-", ours != null && x.ChampionFranchise == ours)).ToList();

        // Run 76: Seasons shows the real NBA's last ten champions, so a new real career's page isn't empty.
        int firstOfCareer = s.League.History.Count > 0 ? Math.Min(s.League.History.Min(h => h.Season), s.League.Season) : s.League.Season;
        RealPastCards = RealWorldData.Seasons.Where(x => x.Season < firstOfCareer)
            .OrderByDescending(x => x.Season).Take(10).Select(x =>
            {
                var club = Franchise(x.ChampionFranchise);
                return new SeasonCard
                {
                    Season = x.Label, Champion = club, ChampionName = x.Champion, Beat = $"beat the {x.RunnerUp}",
                    Mvp = x.Mvp ?? "-", FinalsMvp = x.FinalsMvp ?? "-", Stripe = StripeFor(club), Mine = ours != null && x.ChampionFranchise == ours,
                };
            }).ToList();
        if (RealPastCards.Count > 0) RealPastTitle = "Before this career: the real NBA's last ten champions";

        var byName = players.Values.GroupBy(p => p.Name).ToDictionary(g => g.Key, g => g.First());
        Player? Named(string name) => byName.TryGetValue(name, out var p) ? p : null;
        var marks = s.League.Records.RealMarks;
        // The records as they stand in this career (R2 part 2b): a broken record shows its new holder, and an active
        // holder's career mark (LeBron's points) grows as he plays.
        RealRecordCard Card(RealRecord r, string section)
        {
            string holder = r.Team is { Length: > 0 } t && t != "active" ? $"{r.Holder} ({t})" : r.Team == "active" ? $"{r.Holder} (active)" : r.Holder;
            RealRecordCard card;
            Player? newHolder = null; bool broken = false;
            if (RealRecordChase.KeyFor(section, r.Label) is { } key && marks.TryGetValue(key, out var m) && m.Holder != r.Holder)
            {
                card = new(r.Label, $"{m.Value:N0}", $"{m.Holder} · new record", $"{m.When} · was {r.Value} by {r.Holder}");
                // The new holder by id, not name (two players can share a name; code review).
                newHolder = m.HolderId is { } hid && players.TryGetValue(hid, out var hp) ? hp : null;
                broken = true;
            }
            else if (RealRecordChase.KeyFor(section, r.Label) is { } key2 && marks.TryGetValue(key2, out var m2)
                     && decimal.TryParse(r.Value.Replace(",", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && (double)v < m2.Value)
                card = new(r.Label, $"{m2.Value:N0}", holder, "and counting");
            else card = new(r.Label, r.Value, holder, r.When);
            // His photo if he's in this game (LeBron, Curry, a new holder), else the club's crest (a team record, or the
            // holder's club under today's name), else the trophy.
            var player = broken ? newHolder : Named(r.Holder);
            var club = player?.TeamId is { } tid ? s.FindTeam(tid) : null;
            // A broken record never takes the old holder's club (code review): the new holder's club, or none.
            if (!broken) club ??= ClubNamed(r.Team is { Length: > 0 } rt && rt != "active" ? rt : null) ?? ClubNamed(r.Holder);
            return card with { Player = player, Club = club, Stripe = StripeFor(club) };
        }
        RealGameRecords = RealWorldData.GameRecords.Select(r => Card(r, "game")).ToList();
        RealSeasonRecords = RealWorldData.SeasonRecords.Select(r => Card(r, "season")).ToList();
        RealCareerRecords = RealWorldData.CareerRecords.Select(r => Card(r, "career")).ToList();
        RealTitles = RealWorldData.TitleTable()
            .Select(x =>
            {
                var club = Franchise(x.Team.Abbr);
                return new RealTitlesRow(x.Team.FullName, x.Titles, x.LastSeason.ToString(), x.Team.Abbr == ours) { Club = club, Stripe = StripeFor(club) };
            }).ToList();
        int mine = ours == null ? 0 : RealWorldData.Titles(ours);
        var last = RealWorldData.Seasons.Last();
        RealSummary = $"{RealWorldData.Seasons.Count} NBA seasons, 1946-47 to {last.Label}. Reigning champions: {last.Champion}."
            + (ours != null ? $" {s.HumanTeam.FullName}: {(mine == 0 ? "no titles yet" : $"{mine} title{(mine == 1 ? "" : "s")}")}." : "");
    }

    public override void Refresh()
    {
        Rows.Clear();
        var s = Session;
        var all = s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.League.Retired).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        string Name(int? id) => id is { } i && all.TryGetValue(i, out var p) ? p.Name : "-";
        var cards = new List<SeasonCard>();
        foreach (var h in s.League.History.OrderByDescending(h => h.Season))
        {
            var our = h.Records.FirstOrDefault(r => r.teamId == s.HumanTeam.Id);
            var champ = s.FindTeam(h.ChampionTeamId);
            var runner = s.FindTeam(h.RunnerUpTeamId);
            string ourRecord = our.teamId == 0 ? "" : $"{our.wins}-{our.losses}" + (h.ChampionTeamId == s.HumanTeam.Id ? " · Champions" : h.RunnerUpTeamId == s.HumanTeam.Id ? " · Finals" : "");
            Rows.Add(new HistoryRow
            {
                Season = SeasonText(h.Season),
                Champion = champ?.FullName ?? "-",
                RunnerUp = runner?.FullName ?? "-",
                Mvp = Name(h.MvpPlayerId),
                ScoringLeader = Name(h.ScoringLeaderPlayerId),
                FinalsMvp = Name(h.FinalsMvpPlayerId),
                OurRecord = ourRecord,
            });
            cards.Add(new SeasonCard
            {
                Season = SeasonText(h.Season), Champion = champ, ChampionName = champ?.FullName ?? "-",
                Beat = runner != null ? $"beat the {runner.FullName}" : "",
                Mvp = Name(h.MvpPlayerId), FinalsMvp = Name(h.FinalsMvpPlayerId),
                Extra = h.ScoringLeaderPlayerId != null ? $"Scoring leader {Name(h.ScoringLeaderPlayerId)}" : "",
                OurRecord = ourRecord.Length > 0 ? $"You: {ourRecord}" : "",
                Mine = champ != null && champ == s.HumanTeam && s.Employed, Stripe = StripeFor(champ),
            });
        }
        SeasonCards = cards;
        BuildRecords(all);
        BuildReal(all);
        int titles = s.League.History.Count(h => h.ChampionTeamId == s.HumanTeam.Id);
        Summary = s.League.History.Count == 0 ? "No completed seasons yet." : $"{s.League.History.Count} season{(s.League.History.Count == 1 ? "" : "s")} played · {titles} title{(titles == 1 ? "" : "s")} for {s.HumanTeam.FullName}";
        SeasonsNote = s.League.History.Count > 0 ? ""
            : HasRealPast ? "No seasons finished in this career yet. Each one joins the list when its Finals end; until then, here is the real NBA's recent past."
            : "No seasons finished in this career yet. Each one joins the list when its Finals end.";
    }

    private static string SeasonText(int season) => $"{season}-{(season + 1) % 100:00}";

    /// <summary>The records book: single-game records as kept, single-season bests from the stored stats, and career leaders including this season.</summary>
    private void BuildRecords(Dictionary<int, Player> players)
    {
        var s = Session; var book = s.League.Records; int us = s.HumanTeam.Id;
        string Abbr(int teamId) => s.FindTeam(teamId)?.Abbreviation ?? "";
        Player? P(int? id) => id is { } i && players.TryGetValue(i, out var p) ? p : null;
        RecordTable Lead(RecordTable t, Player? leader, Team? club) => t with { Leader = leader, LeaderClub = club, Stripe = StripeFor(club) };

        GameRecords = RecordBook.PlayerGame.Select(c => (c.Key, c.Label, c.Unit))
            .Concat(RecordBook.TeamGame.Select(c => (c.Key, c.Label, c.Unit)))
            .Select(c =>
            {
                var top = book.Top(c.Key).Take(5).ToList();
                var table = new RecordTable(c.Label, top.Select((e, i) => new RecordRow(i + 1, $"{e.Value:0}",
                    e.PlayerId != null ? $"{e.PlayerName} ({e.Team})" : e.Team,
                    $"vs {e.Opponent}, {(e.Date is { } d ? d.ToString("MMM d, yyyy") : SeasonText(e.Season))}", e.TeamId == us)).ToList());
                return top.Count == 0 ? table : Lead(table, P(top[0].PlayerId), s.FindTeam(top[0].TeamId));
            })
            .ToList();

        // Single seasons: stored past seasons plus this one so far.
        var seasons = (s.Save?.LoadStats() ?? new List<PlayerSeasonStats>()).Where(x => x.Season != s.League.Season).Concat(s.Stats.Values).ToList();
        int minGames = Math.Max(1, s.League.Rules == Rules.Nba ? 41 : 20);
        RecordTable Season(string title, Func<PlayerSeasonStats, double> of, string fmt, int min)
        {
            var leaders = RecordBook.SeasonLeaders(seasons, of, min).ToList();
            var table = new RecordTable(title, leaders.Select((x, i) => new RecordRow(i + 1, x.Value.ToString(fmt),
                $"{(players.TryGetValue(x.Stats.PlayerId, out var p) ? p.Name : "Unknown")} ({Abbr(x.Stats.TeamId)})",
                SeasonText(x.Stats.Season) + (x.Stats.Season == s.League.Season ? " (so far)" : ""), x.Stats.TeamId == us)).ToList());
            return leaders.Count == 0 ? table : Lead(table, P(leaders[0].Stats.PlayerId), s.FindTeam(leaders[0].Stats.TeamId));
        }
        SeasonRecords = new()
        {
            Season("Points per game", x => x.Ppg, "0.0", minGames),
            Season("Rebounds per game", x => x.Rpg, "0.0", minGames),
            Season("Assists per game", x => x.Apg, "0.0", minGames),
            Season("Points in a season", x => x.Points, "N0", 1),
            Season("Threes in a season", x => x.ThreePM, "N0", 1),
            Season("Blocks in a season", x => x.Blocks, "N0", 1),
        };

        var careers = RecordBook.CareerWithCurrent(s.League.Careers, s.Stats);
        RecordTable Career(string title, Func<CareerTotals, double> of)
        {
            var leaders = RecordBook.CareerLeaders(careers, of).ToList();
            var table = new RecordTable(title, leaders.Select((c, i) => new RecordRow(i + 1, of(c).ToString("N0"),
                players.TryGetValue(c.PlayerId, out var p) ? p.Name + (p.TeamId is { } t ? $" ({Abbr(t)})" : s.League.Retired.Contains(p) ? " (retired)" : "") : "Unknown",
                $"{c.Games:N0} games", players.TryGetValue(c.PlayerId, out var q) && q.TeamId == us)).ToList());
            if (leaders.Count == 0) return table;
            // The holder's club: where he plays now, or where he played most.
            var leader = P(leaders[0].PlayerId);
            var club = leader?.TeamId is { } tid ? s.FindTeam(tid)
                : leaders[0].GamesByTeam.Count > 0 ? s.FindTeam(leaders[0].GamesByTeam.MaxBy(x => x.Value).Key) : null;
            return Lead(table, leader, club);
        }
        CareerRecords = new()
        {
            Career("Career points", c => c.Points),
            Career("Career rebounds", c => c.Rebounds),
            Career("Career assists", c => c.Assists),
            Career("Career steals", c => c.Steals),
            Career("Career blocks", c => c.Blocks),
            Career("Games played", c => c.Games),
            Career("Titles", c => c.Titles),
            Career("MVP awards", c => c.Mvps),
        };

        Milestones = book.Milestones.OrderByDescending(m => m.Date).Take(40).Select(m => $"{m.Date:MMM d, yyyy}  ·  {m.Text}").ToList();
    }
}
