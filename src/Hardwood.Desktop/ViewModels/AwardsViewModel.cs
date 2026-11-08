using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class VoteRow
{
    public int Rank { get; init; }
    public string Name { get; init; } = "";
    public string Team { get; init; } = "";
    public string Share { get; init; } = "";
    public string Points { get; init; } = "";
    public string StatLine { get; init; } = "";
}

public sealed class HallRow
{
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public int Year { get; init; }
    public string Career { get; init; } = "";
    public string Honours { get; init; } = "";
    public double Score { get; init; }
}

public sealed partial class AwardsViewModel : PageViewModel
{
    public override string Title => "Awards";
    public ObservableCollection<string> Seasons { get; } = new();
    [ObservableProperty] private string? _season;
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _allLeague = "";
    [ObservableProperty] private string _allStars = "";
    public ObservableCollection<VoteRow> Mvp { get; } = new();
    public ObservableCollection<VoteRow> Roy { get; } = new();
    public ObservableCollection<VoteRow> Dpoy { get; } = new();
    public ObservableCollection<VoteRow> SixthMan { get; } = new();
    public ObservableCollection<HallRow> Hall { get; } = new();
    [ObservableProperty] private string _hallNote = "";

    public AwardsViewModel(MainViewModel shell) : base(shell) { }

    partial void OnSeasonChanged(string? value) => Show();

    public override void Refresh()
    {
        var current = Season;
        Seasons.Clear();
        foreach (var h in Session.League.History.OrderByDescending(h => h.Season)) Seasons.Add($"{h.Season}-{(h.Season + 1) % 100:00}");
        if (Session.Midseason != null) Seasons.Insert(0, $"{Session.League.Season}-{(Session.League.Season + 1) % 100:00} (in progress)");
        Season = Seasons.Contains(current ?? "") ? current : Seasons.FirstOrDefault();
        FillHall();
        Show();
    }

    private void Show()
    {
        Mvp.Clear(); Roy.Clear(); Dpoy.Clear(); SixthMan.Clear();
        var s = Session;
        var all = s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.League.Retired).ToDictionary(p => p.Id);
        string Name(int? id) => id is { } i && all.TryGetValue(i, out var p) ? p.Name : "-";
        string TeamOf(int? id) => id is { } i && all.TryGetValue(i, out var p) && p.TeamId is { } t ? s.League.TeamById(t).Abbreviation : "";

        if (Season == null) { Headline = "No awards yet"; Summary = "Awards are voted after the regular season; All-Stars are named in February."; AllLeague = ""; AllStars = ""; return; }
        SeasonAwards? a; SeasonSummary? summary = null;
        if (Season.EndsWith("(in progress)")) a = s.Midseason;
        else
        {
            int year = int.Parse(Season[..4]);
            summary = s.League.History.First(h => h.Season == year);
            a = summary.Awards;
        }
        if (a == null) { Headline = Season; Summary = "No award data for this season."; return; }

        Headline = summary != null ? $"{Season} awards" : $"{Season}: All-Star weekend";
        Summary = summary != null
            ? $"Champions: {s.League.TeamById(summary.ChampionTeamId).FullName} over {s.League.TeamById(summary.RunnerUpTeamId).FullName} · Finals MVP {Name(summary.FinalsMvpPlayerId)} · Coach of the Year: {(a.CoachOfYearTeamId is { } c ? s.League.TeamById(c).FullName : "-")} ({a.CoachOfYearNote})"
            : "Season in progress. Full awards arrive after the Finals.";
        AllLeague = a.AllLeague.Count == 0 ? "" : string.Join("\n", a.AllLeague.Select((t, i) => $"{(i == 0 ? "First" : i == 1 ? "Second" : "Third")} team: {string.Join(", ", t.Select(id => $"{Name(id)} ({TeamOf(id)})"))}"));
        AllStars = a.AllStarsEast.Count == 0 ? "No All-Star Game this season." :
            $"All-Star Game: {a.AllStarGame}. MVP {Name(a.AllStarMvpId)}.\nEast: {string.Join(", ", a.AllStarsEast.Select(id => Name(id)))}\nWest: {string.Join(", ", a.AllStarsWest.Select(id => Name(id)))}";

        void Fill(ObservableCollection<VoteRow> list, List<VoteLine> votes)
        {
            int rank = 1;
            foreach (var v in votes) list.Add(new VoteRow { Rank = rank++, Name = v.Name, Team = v.Team, Share = $"{v.Share * 100:F0}%", Points = v.Points.ToString("0"), StatLine = v.StatLine });
        }
        Fill(Mvp, a.MvpVoting); Fill(Roy, a.RoyVoting); Fill(Dpoy, a.DpoyVoting); Fill(SixthMan, a.SixthManVoting);
    }

    private void FillHall()
    {
        Hall.Clear();
        var s = Session;
        var all = s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.League.Retired).ToDictionary(p => p.Id);
        foreach (var h in s.League.HallOfFame.OrderByDescending(h => h.Year).ThenByDescending(h => h.Score))
        {
            if (!all.TryGetValue(h.PlayerId, out var p)) continue;
            s.League.Careers.TryGetValue(h.PlayerId, out var c);
            Hall.Add(new HallRow
            {
                Player = p, Year = h.Year, Score = h.Score,
                Career = c == null ? "" : $"{c.Seasons} seasons · {c.Points:N0} pts · {c.Ppg:F1}/{c.Rpg:F1}/{c.Apg:F1}",
                Honours = c == null ? "" : string.Join(" · ", new[] { (c.Mvps, "MVP"), (c.Titles, "title"), (c.FinalsMvps, "Finals MVP"), (c.AllLeague, "All-League"), (c.AllStars, "All-Star"), (c.Dpoys, "DPOY") }.Where(x => x.Item1 > 0).Select(x => $"{x.Item1}× {x.Item2}")),
            });
        }
        HallNote = Hall.Count == 0 ? "No inductees yet. Players are considered when they retire." : $"{Hall.Count} inductee{(Hall.Count == 1 ? "" : "s")}.";
    }
}
