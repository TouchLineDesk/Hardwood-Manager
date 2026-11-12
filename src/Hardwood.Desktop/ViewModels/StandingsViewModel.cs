using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
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
    public string Division => Team.Division;
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
        "Play-in" => PlayInBrush,
        _ => Avalonia.Media.Brushes.Transparent,
    };
    public string ZoneTip => Zone == "" ? "Outside the postseason places" : Zone;
    public static readonly Avalonia.Media.IBrush PlayoffBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2E8B57"));
    public static readonly Avalonia.Media.IBrush PlayInBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#B7832A"));
}

public sealed partial class StandingsViewModel : PageViewModel
{
    public override string Title => "Standings";
    public ObservableCollection<StandingRow> East { get; } = new();
    public ObservableCollection<StandingRow> West { get; } = new();
    [ObservableProperty] private string _note = "";
    /// <summary>The green chip's meaning: the playoffs, or a college conference tournament.</summary>
    [ObservableProperty] private string _playoffLabel = "Playoffs";
    [ObservableProperty] private bool _showPlayIn = true;
    [ObservableProperty] private string _leftTitle = "Eastern Conference";
    [ObservableProperty] private string _rightTitle = "Western Conference";
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
                Home = West[i].Home, Last10 = West[i].Last10, IsHuman = West[i].IsHuman,
            };
            PlayoffLabel = "Conference tournament"; ShowPlayIn = false;
            LeftTitle = $"{s.MyConference!.Name} (conference games)"; RightTitle = "Top 25 (by rating, overall records)";
            Note = "Tournament winners get automatic March Madness bids.";
            Race = Array.Empty<Hardwood.Desktop.Controls.LineSeries>(); RaceLabels = Array.Empty<string>();
            OnPropertyChanged(nameof(Race)); OnPropertyChanged(nameof(RaceLabels)); OnPropertyChanged(nameof(HasRace));
            return;
        }
        PlayoffLabel = "Playoffs"; ShowPlayIn = true;
        if (s.HumanLeague is { } hl)
        {
            // Abroad: your league on the left, the NABL on the right.
            int playoff = hl.Profile.Playoffs.Length > 0 ? hl.Profile.Playoffs[0].Teams : 0;
            FillRecords(East, s.MyStandings.All(), r => r <= playoff ? "Playoffs" : "");
            FillRecords(West, s.Standings.All(), _ => "");
            LeftTitle = hl.Profile.Name; RightTitle = Hardwood.Core.Names.Brand.Top;
            Note = playoff > 0 ? $"Top {playoff} reach the playoffs ({string.Join(", ", hl.Profile.Playoffs.Select(p => p.Name.ToLower()))})." : "";
            Race = Array.Empty<Hardwood.Desktop.Controls.LineSeries>(); RaceLabels = Array.Empty<string>();
            OnPropertyChanged(nameof(Race)); OnPropertyChanged(nameof(RaceLabels)); OnPropertyChanged(nameof(HasRace));
            return;
        }
        LeftTitle = "Eastern Conference"; RightTitle = "Western Conference";
        Fill(East, Conference.East);
        Fill(West, Conference.West);
        Note = "Top 6 qualify directly; 7-10 play in.";
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

    private void Fill(ObservableCollection<StandingRow> list, Conference conf)
    {
        list.Clear();
        var table = Session.Standings.Conference(conf);
        var leader = table.FirstOrDefault();
        int rank = 1;
        foreach (var r in table)
        {
            double gb = leader == null ? 0 : ((leader.Wins - r.Wins) + (r.Losses - leader.Losses)) / 2.0;
            var last10 = Session.Schedule.Where(g => (g.Home == r.Team || g.Away == r.Team) && Session.Results.ContainsKey(g.Index))
                .OrderByDescending(g => g.Index).Take(10).Select(g => Session.Results[g.Index].Winner.Team == r.Team).ToList();
            list.Add(new StandingRow
            {
                Rank = rank, Zone = rank <= 6 ? "Playoffs" : rank <= 10 ? "Play-in" : "", Team = r.Team, W = r.Wins, L = r.Losses,
                Pct = r.WinPct.ToString(".000"), GB = gb <= 0 ? "-" : gb.ToString("0.#"),
                Diff = r.PointDiff.ToString("+0.0;-0.0;0.0"), Home = $"{r.HomeWins}-{r.HomeLosses}",
                Last10 = last10.Count > 0 ? $"{last10.Count(x => x)}-{last10.Count(x => !x)}" : "",
                IsHuman = r.Team == Session.HumanTeam,
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

    public LeadersViewModel(MainViewModel shell) : base(shell) { }
    partial void OnCategoryChanged(string value) => Refresh();

    public override void Refresh()
    {
        Rows.Clear();
        var s = Session;
        int minGames = Math.Max(1, (int)(s.Standings.All().Average(r => r.Games) * 0.6));
        var players = s.League.AllActivePlayers.Concat(s.League.FreeAgents).ToDictionary(p => p.Id);
        var stats = s.Stats.Values.Where(x => x.Games >= minGames && players.ContainsKey(x.PlayerId));
        Func<PlayerSeasonStats, double> key = Category switch
        {
            "Rebounds" => x => x.Rpg, "Assists" => x => x.Apg, "Steals" => x => (double)x.Steals / x.Games, "Blocks" => x => (double)x.Blocks / x.Games,
            "Minutes" => x => x.Mpg, "FG%" => x => x.FGA >= x.Games * 5 ? x.FgPct : 0, "3P%" => x => x.ThreePA >= x.Games * 2 ? x.ThreePct : 0,
            _ => x => x.Ppg,
        };
        int rank = 1;
        foreach (var x in stats.OrderByDescending(key).Take(40))
        {
            var p = players[x.PlayerId];
            Rows.Add(new LeaderRow
            {
                Rank = rank++, Player = p, Team = s.FindTeam(x.TeamId)!.Abbreviation, GP = x.Games,
                MPG = Math.Round(x.Mpg, 1), PPG = Math.Round(x.Ppg, 1), RPG = Math.Round(x.Rpg, 1), APG = Math.Round(x.Apg, 1),
                SPG = Math.Round((double)x.Steals / x.Games, 1), BPG = Math.Round((double)x.Blocks / x.Games, 1),
                FG = $"{x.FgPct * 100:F1}", TP = x.ThreePA > 0 ? $"{x.ThreePct * 100:F1}" : "-", Value = Math.Round(key(x), 3),
            });
        }
        Note = $"Minimum {minGames} games played.";
    }
}

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

/// <summary>A real NBA season (R1, #191): the History page's "NBA history" tab.</summary>
public sealed record RealSeasonRow(string Season, string Champion, string RunnerUp, string FinalsMvp, string Mvp, bool Ours);

/// <summary>A real NBA record card: the mark, its holder and when.</summary>
public sealed record RealRecordCard(string Label, string Value, string Holder, string When);

/// <summary>A franchise's real titles.</summary>
public sealed record RealTitlesRow(string Team, int Titles, string Last, bool Ours);

/// <summary>One line of a records table: rank, the number, who and when.</summary>
public sealed record RecordRow(int Rank, string Value, string Who, string When, bool Ours);

/// <summary>A records-book table (roadmap 4): "Points in a game", top five.</summary>
public sealed record RecordTable(string Title, IReadOnlyList<RecordRow> Rows)
{
    public bool Empty => Rows.Count == 0;
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
    public bool HasMilestones => Milestones.Count > 0;
    partial void OnMilestonesChanged(List<string> value) => OnPropertyChanged(nameof(HasMilestones));

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
    public string RealSource => $"Checked {Hardwood.Core.RealWorld.RealWorldData.HistoryChecked}. {Hardwood.Core.RealWorld.RealWorldData.HistorySource}";

    private void BuildReal()
    {
        var s = Session;
        IsReal = Hardwood.Core.RealWorld.RealWorldData.IsReal(s.League);
        if (!IsReal) return;
        // The real past is tied to today's franchises: the club in the Celtics' place inherits Boston's history.
        var abbrs = Hardwood.Core.RealWorld.RealWorldData.Teams.Select(t => t.Abbr).ToList();
        // The 30 franchises keep ids 1-30 wherever the pyramid sends them.
        string? ours = s.HumanTeam.Id is >= 1 and <= 30 && s.HumanTeam.Id <= abbrs.Count ? abbrs[s.HumanTeam.Id - 1] : null;
        RealSeasons = Hardwood.Core.RealWorld.RealWorldData.Seasons.OrderByDescending(x => x.Season)
            .Select(x => new RealSeasonRow(x.Label, x.Champion, x.RunnerUp, x.FinalsMvp ?? "-", x.Mvp ?? "-", ours != null && x.ChampionFranchise == ours)).ToList();
        static RealRecordCard Card(Hardwood.Core.RealWorld.RealRecord r) =>
            new(r.Label, r.Value, r.Team is { Length: > 0 } t && t != "active" ? $"{r.Holder} ({t})" : r.Team == "active" ? $"{r.Holder} (active)" : r.Holder, r.When);
        RealGameRecords = Hardwood.Core.RealWorld.RealWorldData.GameRecords.Select(Card).ToList();
        RealSeasonRecords = Hardwood.Core.RealWorld.RealWorldData.SeasonRecords.Select(Card).ToList();
        RealCareerRecords = Hardwood.Core.RealWorld.RealWorldData.CareerRecords.Select(Card).ToList();
        RealTitles = Hardwood.Core.RealWorld.RealWorldData.TitleTable()
            .Select(x => new RealTitlesRow(x.Team.FullName, x.Titles, x.LastSeason.ToString(), x.Team.Abbr == ours)).ToList();
        int mine = ours == null ? 0 : Hardwood.Core.RealWorld.RealWorldData.Titles(ours);
        var last = Hardwood.Core.RealWorld.RealWorldData.Seasons.Last();
        RealSummary = $"{Hardwood.Core.RealWorld.RealWorldData.Seasons.Count} NBA seasons, 1946-47 to {last.Label}. Reigning champions: {last.Champion}."
            + (ours != null ? $" {s.HumanTeam.FullName}: {(mine == 0 ? "no titles yet" : $"{mine} title{(mine == 1 ? "" : "s")}")}." : "");
    }

    public override void Refresh()
    {
        Rows.Clear();
        var s = Session;
        var all = s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.League.Retired).ToDictionary(p => p.Id);
        foreach (var h in s.League.History.OrderByDescending(h => h.Season))
        {
            var our = h.Records.FirstOrDefault(r => r.teamId == s.HumanTeam.Id);
            Rows.Add(new HistoryRow
            {
                Season = $"{h.Season}-{(h.Season + 1) % 100:00}",
                Champion = s.FindTeam(h.ChampionTeamId)!.FullName,
                RunnerUp = s.FindTeam(h.RunnerUpTeamId)!.FullName,
                Mvp = h.MvpPlayerId is { } m && all.TryGetValue(m, out var mp) ? mp.Name : "-",
                ScoringLeader = h.ScoringLeaderPlayerId is { } l && all.TryGetValue(l, out var lp) ? lp.Name : "-",
                FinalsMvp = h.FinalsMvpPlayerId is { } f && all.TryGetValue(f, out var fp) ? fp.Name : "-",
                OurRecord = our.teamId == 0 ? "" : $"{our.wins}-{our.losses}" + (h.ChampionTeamId == s.HumanTeam.Id ? " · Champions" : h.RunnerUpTeamId == s.HumanTeam.Id ? " · Finals" : ""),
            });
        }
        BuildRecords(all);
        BuildReal();
        int titles = s.League.History.Count(h => h.ChampionTeamId == s.HumanTeam.Id);
        Summary = s.League.History.Count == 0 ? "No completed seasons yet." : $"{s.League.History.Count} season{(s.League.History.Count == 1 ? "" : "s")} played · {titles} title{(titles == 1 ? "" : "s")} for {s.HumanTeam.FullName}";
    }

    private static string SeasonText(int season) => $"{season}-{(season + 1) % 100:00}";

    /// <summary>The records book: single-game records as kept, single-season bests from the stored stats, and career leaders including this season.</summary>
    private void BuildRecords(Dictionary<int, Hardwood.Core.Model.Player> players)
    {
        var s = Session; var book = s.League.Records; int us = s.HumanTeam.Id;
        string Abbr(int teamId) => s.FindTeam(teamId)?.Abbreviation ?? "";

        GameRecords = RecordBook.PlayerGame.Select(c => (c.Key, c.Label, c.Unit))
            .Concat(RecordBook.TeamGame.Select(c => (c.Key, c.Label, c.Unit)))
            .Select(c => new RecordTable(c.Label, book.Top(c.Key).Take(5).Select((e, i) => new RecordRow(i + 1, $"{e.Value:0}",
                e.PlayerId != null ? $"{e.PlayerName} ({e.Team})" : e.Team,
                $"vs {e.Opponent}, {(e.Date is { } d ? d.ToString("MMM d, yyyy") : SeasonText(e.Season))}", e.TeamId == us)).ToList()))
            .ToList();

        // Single seasons: stored past seasons plus this one so far.
        var seasons = (s.Save?.LoadStats() ?? new List<PlayerSeasonStats>()).Where(x => x.Season != s.League.Season).Concat(s.Stats.Values).ToList();
        int minGames = Math.Max(1, s.League.Rules == Hardwood.Core.Model.Rules.Nba ? 41 : 20);
        RecordTable Season(string title, Func<PlayerSeasonStats, double> of, string fmt, int min) =>
            new(title, RecordBook.SeasonLeaders(seasons, of, min).Select((x, i) => new RecordRow(i + 1, x.Value.ToString(fmt),
                $"{(players.TryGetValue(x.Stats.PlayerId, out var p) ? p.Name : "Unknown")} ({Abbr(x.Stats.TeamId)})",
                SeasonText(x.Stats.Season) + (x.Stats.Season == s.League.Season ? " (so far)" : ""), x.Stats.TeamId == us)).ToList());
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
        RecordTable Career(string title, Func<CareerTotals, double> of) =>
            new(title, RecordBook.CareerLeaders(careers, of).Select((c, i) => new RecordRow(i + 1, of(c).ToString("N0"),
                players.TryGetValue(c.PlayerId, out var p) ? p.Name + (p.TeamId is { } t ? $" ({Abbr(t)})" : s.League.Retired.Contains(p) ? " (retired)" : "") : "Unknown",
                $"{c.Games:N0} games", players.TryGetValue(c.PlayerId, out var q) && q.TeamId == us)).ToList());
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
