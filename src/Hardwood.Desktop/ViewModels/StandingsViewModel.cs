using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class StandingRow
{
    public int Rank { get; init; }
    public required Team Team { get; init; }
    public string Name => Team.FullName;
    public string Division => Team.Division;
    public int W { get; init; }
    public int L { get; init; }
    public string Pct { get; init; } = "";
    public string GB { get; init; } = "";
    public string Diff { get; init; } = "";
    public string Home { get; init; } = "";
    public string Last10 { get; init; } = "";
    public bool IsHuman { get; init; }
    public string Zone => Rank <= 6 ? "Playoffs" : Rank <= 10 ? "Play-in" : "";
}

public sealed partial class StandingsViewModel : PageViewModel
{
    public override string Title => "Standings";
    public ObservableCollection<StandingRow> East { get; } = new();
    public ObservableCollection<StandingRow> West { get; } = new();
    [ObservableProperty] private string _note = "";
    public IReadOnlyList<Hardwood.Desktop.Controls.LineSeries> Race { get; private set; } = Array.Empty<Hardwood.Desktop.Controls.LineSeries>();
    public string[] RaceLabels { get; private set; } = Array.Empty<string>();
    public bool HasRace => RaceLabels.Length >= 3;

    public StandingsViewModel(MainViewModel shell) : base(shell) { }

    public override void Refresh()
    {
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
                Rank = rank++, Team = r.Team, W = r.Wins, L = r.Losses,
                Pct = r.WinPct.ToString(".000"), GB = gb <= 0 ? "-" : gb.ToString("0.#"),
                Diff = r.PointDiff.ToString("+0.0;-0.0;0.0"), Home = $"{r.HomeWins}-{r.HomeLosses}",
                Last10 = last10.Count > 0 ? $"{last10.Count(x => x)}-{last10.Count(x => !x)}" : "",
                IsHuman = r.Team == Session.HumanTeam,
            });
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
                Rank = rank++, Player = p, Team = s.League.TeamById(x.TeamId).Abbreviation, GP = x.Games,
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

public sealed partial class HistoryViewModel : PageViewModel
{
    public override string Title => "History";
    public ObservableCollection<HistoryRow> Rows { get; } = new();
    [ObservableProperty] private string _summary = "";

    public HistoryViewModel(MainViewModel shell) : base(shell) { }

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
                Champion = s.League.TeamById(h.ChampionTeamId).FullName,
                RunnerUp = s.League.TeamById(h.RunnerUpTeamId).FullName,
                Mvp = h.MvpPlayerId is { } m && all.TryGetValue(m, out var mp) ? mp.Name : "-",
                ScoringLeader = h.ScoringLeaderPlayerId is { } l && all.TryGetValue(l, out var lp) ? lp.Name : "-",
                FinalsMvp = h.FinalsMvpPlayerId is { } f && all.TryGetValue(f, out var fp) ? fp.Name : "-",
                OurRecord = our.teamId == 0 ? "" : $"{our.wins}-{our.losses}" + (h.ChampionTeamId == s.HumanTeam.Id ? " · Champions" : h.RunnerUpTeamId == s.HumanTeam.Id ? " · Finals" : ""),
            });
        }
        int titles = s.League.History.Count(h => h.ChampionTeamId == s.HumanTeam.Id);
        Summary = s.League.History.Count == 0 ? "No completed seasons yet." : $"{s.League.History.Count} season{(s.League.History.Count == 1 ? "" : "s")} played · {titles} title{(titles == 1 ? "" : "s")} for {s.HumanTeam.FullName}";
    }
}
