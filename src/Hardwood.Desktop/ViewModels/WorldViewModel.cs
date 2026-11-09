using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Desktop.ViewModels;

public sealed class WorldTableRow
{
    public int Rank { get; init; }
    public required Team Team { get; init; }
    public string Name => Team.FullName;
    public int W { get; init; }
    public int L { get; init; }
    public string Pct { get; init; } = "";
    public string Diff { get; init; } = "";
    public string Zone { get; init; } = "";
}

public sealed class WorldLeaderRow
{
    public int Rank { get; init; }
    public string Player { get; init; } = "";
    public string Team { get; init; } = "";
    public int G { get; init; }
    public string Ppg { get; init; } = "";
    public string Rpg { get; init; } = "";
    public string Apg { get; init; } = "";
}

public sealed record CompetitionChoice(string Key, string Name);

/// <summary>Every competition outside the NABL: table, playoffs, results, fixtures, scoring leaders and champions.</summary>
public sealed partial class WorldViewModel : PageViewModel
{
    public override string Title => "World";
    public ObservableCollection<CompetitionChoice> Competitions { get; } = new();
    [ObservableProperty] private CompetitionChoice? _selected;
    [ObservableProperty] private string _heading = "";
    [ObservableProperty] private string _note = "";
    [ObservableProperty] private string _playoffs = "";
    [ObservableProperty] private string _results = "";
    [ObservableProperty] private string _upcoming = "";
    [ObservableProperty] private string _champions = "";
    public ObservableCollection<WorldTableRow> Table { get; } = new();
    public ObservableCollection<WorldLeaderRow> Leaders { get; } = new();

    public WorldViewModel(MainViewModel shell) : base(shell)
    {
        foreach (var c in Session.World.Playing) Competitions.Add(new CompetitionChoice(c.Key, c.Profile.Name));
        Selected = Competitions.FirstOrDefault();
    }

    partial void OnSelectedChanged(CompetitionChoice? value) => Refresh();

    public override void Refresh()
    {
        if (Selected == null) return;
        var world = Session.World;
        var comp = world[Selected.Key];
        var p = comp.Profile;
        var s = comp.Season;
        string Team(int id) => world.TeamById(id)?.FullName ?? "?";
        string Abbr(int id) => world.TeamById(id)?.Abbreviation ?? "?";

        Heading = $"{p.Name} · {s.Year}-{(s.Year + 1) % 100:00} · {(s.IsComplete ? $"Champions: {Team(s.ChampionId!.Value)}" : s.Stage)}";
        var limit = p.Kind == CompetitionKind.Domestic ? $" · up to {p.ForeignLimit} imports per club" : "";
        Note = $"{(comp.FullSim ? "Full match engine" : "Quick results")} · {p.Rules.Name} rules · {s.Participants.Count} clubs{limit}";

        Table.Clear();
        int playoffTeams = p.Playoffs.Length > 0 ? p.Playoffs[0].Teams : 0;
        var eliteKeys = world.Competitions.FirstOrDefault(c => c.Profile.Kind == CompetitionKind.Continental)?.Profile.Entrants
            .Where(e => e.LeagueKey == p.Key).Select(e => e.Clubs).FirstOrDefault() ?? 0;
        int rank = 0;
        foreach (var r in s.Table())
        {
            rank++;
            var zone = rank <= playoffTeams ? "Playoffs" : "";
            if (rank <= eliteKeys) zone = zone.Length > 0 ? "Playoffs · EEL" : "EEL";
            Table.Add(new WorldTableRow
            {
                Rank = rank, Team = world.TeamById(r.TeamId)!, W = r.Wins, L = r.Losses,
                Pct = r.Games == 0 ? "-" : r.Pct.ToString(".000"), Diff = r.Games == 0 ? "-" : (r.Diff / (double)r.Games).ToString("+0.0;-0.0;0.0"), Zone = zone,
            });
        }

        Playoffs = s.Series.Count == 0
            ? (p.Playoffs.Length == 0 ? "" : $"Top {playoffTeams} reach the playoffs: " + string.Join(" → ", p.Playoffs.Select(r => r.BestOf == 1 ? $"{r.Name} (one game)" : $"{r.Name} (best of {r.BestOf})")) + ".")
            : string.Join("\n", s.Series.GroupBy(x => x.Round).Select(g => g.Key + "\n" + string.Join("\n", g.Select(x =>
                $"   {Abbr(x.HighId)} {x.HighWins}-{x.LowWins} {Abbr(x.LowId)}" + (x.Complete ? $"   ({Abbr(x.WinnerId!.Value)} win{(x.BestOf == 1 ? "" : " " + x.Score)})" : "")))));

        Results = string.Join("\n", s.Fixtures.Where(f => f.Played).OrderByDescending(f => f.Date).ThenBy(f => f.Index).Take(12)
            .Select(f => $"{f.Date:ddd MMM d}  {Team(f.HomeId)} {f.HomePts}-{f.AwayPts} {Team(f.AwayId)}"));
        if (Results.Length == 0) Results = "No games played yet.";
        Upcoming = string.Join("\n", s.Fixtures.Where(f => !f.Played).OrderBy(f => f.Date).ThenBy(f => f.Index).Take(10)
            .Select(f => $"{f.Date:ddd MMM d}  {Team(f.HomeId)} vs {Team(f.AwayId)}" + (f.Stage != CompetitionSeason.RegularSeason ? $"  ({f.Stage})" : "")));
        if (Upcoming.Length == 0) Upcoming = s.IsComplete ? "Season complete." : "Waiting for the next round to be drawn.";

        Leaders.Clear();
        int maxG = s.Stats.Values.Select(l => l.G).DefaultIfEmpty(0).Max();
        var players = world.Players.ToDictionary(x => x.Id);
        int k = 0;
        foreach (var (id, l) in s.Stats.Where(kv => kv.Value.G >= Math.Max(1, maxG * 0.4)).OrderByDescending(kv => kv.Value.Ppg).Take(10))
            Leaders.Add(new WorldLeaderRow
            {
                Rank = ++k, Player = players.TryGetValue(id, out var pl) ? pl.Name : "(left)", Team = Abbr(l.TeamId), G = l.G,
                Ppg = l.Ppg.ToString("0.0"), Rpg = l.Rpg.ToString("0.0"), Apg = l.Apg.ToString("0.0"),
            });

        Champions = comp.History.Count == 0 ? "No champions yet in this save."
            : string.Join("\n", comp.History.AsEnumerable().Reverse().Select(h => $"{h.Season}-{(h.Season + 1) % 100:00}  {h.ChampionName} (beat {h.RunnerUpName})"));
    }
}
