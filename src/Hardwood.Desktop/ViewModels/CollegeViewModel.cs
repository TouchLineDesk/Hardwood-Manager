using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class RankRow
{
    public int Rank { get; init; }
    public required Team Team { get; init; }
    public string Name => Team.FullName;
    public string Conference { get; init; } = "";
    public string Record { get; init; } = "";
    public string ConfRecord { get; init; } = "";
    public string Rating { get; init; } = "";
}

public sealed class ProspectListRow
{
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public string Program { get; init; } = "";
    public double Knowledge { get; init; } = 1;
    public string CA => Scouting.Ability(Player, Knowledge).ToString();
    public string PA => Scouting.Potential(Player, Knowledge).ToString();
    public string Line { get; init; } = "";
    public string Turns => Age >= 22 ? "Turns pro next summer" : $"Pro in {23 - Age} years";
}

/// <summary>The college hub (decision #97): Top 25, conference tables, the March Madness bracket, and prospects by age.</summary>
public sealed partial class CollegeViewModel : PageViewModel
{
    public override string Title => "College";
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _summary = "";
    public ObservableCollection<RankRow> Top25 { get; } = new();
    public List<string> ConferenceNames { get; private set; } = CollegeWorld.Conferences.Select(c => c.Name).ToList();
    [ObservableProperty] private string _conference = CollegeWorld.Conferences[0].Name;
    public ObservableCollection<RankRow> ConferenceRows { get; } = new();
    public ObservableCollection<string> Bracket { get; } = new();
    [ObservableProperty] private string _bracketNote = "";
    public List<string> Ages { get; } = new() { "Turning pro next summer", "18-21", "14-17", "All" };
    [ObservableProperty] private string _age = "Turning pro next summer";
    public List<string> Positions { get; } = new() { "All", "PG", "SG", "SF", "PF", "C" };
    [ObservableProperty] private string _position = "All";
    public ObservableCollection<ProspectListRow> Prospects { get; } = new();
    [ObservableProperty] private ProspectListRow? _selectedProspect;
    public bool HasCollege => Session.World.College != null;
    [ObservableProperty] private int _tab;

    partial void OnConferenceChanged(string value) => FillConference();
    partial void OnAgeChanged(string value) => FillProspects();
    partial void OnPositionChanged(string value) => FillProspects();

    public CollegeViewModel(MainViewModel shell) : base(shell)
    {
        // Only the conferences this career has (careers from before decision #121 have eight).
        if (Session.World.College is { } c) ConferenceNames = c.Active.Select(x => x.Name).ToList();
    }

    public override void Refresh()
    {
        var c = Session.World.College;
        if (c == null)
        {
            Headline = "College";
            Summary = "This career was started before college basketball existed. Start a new career to get it.";
            return;
        }
        var s = c.Season;
        Headline = $"College basketball {s.Year}-{(s.Year + 1) % 100:00}";
        var last = c.History.LastOrDefault();
        Summary = $"{c.StageText} · {c.Programs.Count} programs in {c.Active.Count()} conferences"
                  + (s.ChampionId is { } ch ? $" · champions: {c.Program(ch)!.FullName}" : last != null ? $" · last champions: {last.Champion}" : "");
        Top25.Clear();
        int rank = 1;
        foreach (var t in c.Top25())
        {
            var (w, l) = c.Record(t.Id);
            Top25.Add(new RankRow { Rank = rank++, Team = t, Conference = ConfName(c, t), Record = $"{w}-{l}", Rating = $"{s.Elo[t.Id]:0}" });
        }
        FillConference();
        FillBracket();
        FillProspects();
    }

    private static string ConfName(CollegeWorld c, Team t) => CollegeWorld.Conferences.First(x => x.Key == c.ConferenceOf[t.Id]).Short;

    private void FillConference()
    {
        ConferenceRows.Clear();
        if (Session.World.College is not { } c) return;
        var conf = CollegeWorld.Conferences.First(x => x.Name == Conference);
        int rank = 1;
        foreach (var (id, w, l) in c.ConferenceTable(conf.Key))
        {
            var t = c.Program(id)!; var (ow, ol) = c.Record(id);
            ConferenceRows.Add(new RankRow { Rank = rank++, Team = t, Conference = conf.Short, ConfRecord = $"{w}-{l}", Record = $"{ow}-{ol}", Rating = $"{c.Season.Elo[id]:0}" });
        }
    }

    private void FillBracket()
    {
        Bracket.Clear();
        if (Session.World.College is not { } c) return;
        var s = c.Season;
        if (!s.Selected) { BracketNote = "The 68-team field is picked on Selection Sunday in mid-March: eight conference tournament champions and 60 at-large teams."; return; }
        BracketNote = s.ChampionId is { } ch ? $"Champions: {c.Program(ch)!.FullName}." : "Games use the full match engine.";
        string T(int id) => $"({s.Seeds.GetValueOrDefault(id)}) {c.Program(id)!.FullName}";
        foreach (var round in s.Games.Where(g => g.Event == CollegeWorld.Madness).GroupBy(g => g.Round).OrderByDescending(g => g.Key))
        {
            Bracket.Add($"— {CollegeWorld.RoundName(round.Key)} —");
            foreach (var g in round.OrderBy(g => g.Slot))
                Bracket.Add(g.Played ? $"{T(g.WinnerId!.Value)} beat {T(g.WinnerId == g.HomeId ? g.AwayId : g.HomeId)} {Math.Max(g.HomePts!.Value, g.AwayPts!.Value)}-{Math.Min(g.HomePts!.Value, g.AwayPts!.Value)}"
                                     : $"{g.Date:MMM d}: {T(g.HomeId)} vs {T(g.AwayId)}");
        }
    }

    private void FillProspects()
    {
        Prospects.Clear();
        if (Session.World.College is not { } c) return;
        var today = Session.Today;
        var team = Session.HumanTeam;
        var rows = c.Programs.SelectMany(t => t.Roster.Select(p => (p, t)))
            .Where(x => Position == "All" || x.p.PrimaryPosition.ToString() == Position)
            .Select(x => (x.p, x.t, age: x.p.Age(today)))
            .Where(x => Age switch { "Turning pro next summer" => x.age >= 22, "18-21" => x.age is >= 18 and <= 21, "14-17" => x.age <= 17, _ => true })
            .OrderByDescending(x => x.p.PotentialAbility * 0.6 + x.p.CurrentAbility * 0.4).Take(200);
        foreach (var (p, t, age) in rows)
        {
            c.Season.Stats.TryGetValue(p.Id, out var st);
            Prospects.Add(new ProspectListRow
            {
                Player = p, Age = age, Program = t.FullName, Knowledge = Scouting.Knowledge(team, p, prospect: true),
                Line = st is { G: > 0 } ? $"{st.Ppg:F1} pts · {st.Rpg:F1} reb · {st.Apg:F1} ast" : "-",
            });
        }
    }

    [RelayCommand]
    private void OpenProspect(ProspectListRow? row)
    {
        if ((row ?? SelectedProspect) is { } r) Shell.ShowPlayer(r.Player);
    }
}
