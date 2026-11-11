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

/// <summary>A senior on the recruiting board (decision #157).</summary>
public sealed class RecruitRow
{
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public string Program { get; init; } = "";
    public string CA { get; init; } = "";
    public string PA { get; init; } = "";
    public double Interest { get; init; }
    public string InterestText { get; init; } = "";
    public string Status { get; init; } = "";
    public bool Listed { get; init; }
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

    // Senior recruiting (decision #157): court seniors so they sign for you on graduation day.
    public ObservableCollection<RecruitRow> Recruits { get; } = new();
    [ObservableProperty] private RecruitRow? _selectedRecruit;
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _recruitChips = new();
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _interestChips = new();
    [ObservableProperty] private string _recruitLean = "";
    [ObservableProperty] private string _recruitReply = "";
    [ObservableProperty] private string _recruitNote = "";
    [ObservableProperty] private decimal _offerWage;
    [ObservableProperty] private decimal _offerYears = 3;
    [ObservableProperty] private bool _listedOnly;
    public bool HasRecruit => SelectedRecruit != null;
    partial void OnSelectedRecruitChanged(RecruitRow? value) { OnPropertyChanged(nameof(HasRecruit)); RecruitReply = ""; FillRecruitPanel(); }
    partial void OnListedOnlyChanged(bool value) => FillRecruits();
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
        FillRecruits();
    }

    private void FillRecruits()
    {
        var keep = SelectedRecruit?.Player;
        Recruits.Clear();
        if (Session.World.College is not { } c) return;
        var board = Session.Recruiting;
        var team = Session.HumanTeam;
        int ours = 0, rivals = 0;
        foreach (var p in Session.Seniors().Take(80))
        {
            var st = board?.Find(p.Id);
            if (st?.CommittedTeamId == team.Id) ours++; else if (st?.CommittedTeamId != null) rivals++;
            bool listed = st is { Shortlisted: true } && st.TeamId == team.Id;
            if (ListedOnly && !listed && st?.CommittedTeamId != team.Id) continue;
            var i = Session.InterestIn(p);
            double k = Scouting.Knowledge(team, p, prospect: true);
            Recruits.Add(new RecruitRow
            {
                Player = p, Program = c.Program(p.TeamId ?? 0)?.FullName ?? "", CA = Scouting.Ability(p, k).ToString(), PA = Scouting.Potential(p, k).ToString(),
                Interest = i.Blocked != null ? 0 : i.Total, InterestText = i.Blocked != null ? "-" : $"{i.Total:0} / {i.Need:0}",
                Listed = listed,
                Status = st?.CommittedTeamId is { } t ? (t == team.Id ? "Committed to you" : $"Committed: {Session.FindTeam(t)?.Abbreviation}")
                       : i.Blocked != null ? "Aims higher" : st is { OfferWage: > 0 } && st.TeamId == team.Id ? "Your offer is in" : listed ? "On your list" : "Open",
            });
        }
        RecruitNote = !Session.RecruitingOpen
            ? "Recruiting opens with the college season in November and closes when the season ends and the seniors turn pro."
            : "Seniors turn pro when the season ends. Win them over with calls and visits, then offer terms: a senior who commits signs for you then, before free agency opens.";
        RecruitChips = new()
        {
            new("Recruiting points", $"{board?.Points ?? 0}", $"tops up monthly (scout), max {Hardwood.Core.College.RecruitingBoard.PointsCap}", "team"),
            new("Committed to you", ours.ToString(), "sign when the season ends", ours > 0 ? "good" : ""),
            new("Committed elsewhere", rivals.ToString(), "of the top 80 seniors", rivals > 40 ? "warn" : ""),
            new("Seniors", Session.Seniors().Count.ToString(), "turning pro this summer", "info"),
        };
        SelectedRecruit = keep == null ? null : Recruits.FirstOrDefault(r => r.Player == keep);
        FillRecruitPanel();
    }

    private void FillRecruitPanel()
    {
        if (SelectedRecruit?.Player is not { } p) { InterestChips = new(); RecruitLean = ""; return; }
        var i = Session.InterestIn(p);
        InterestChips = new()
        {
            new("Club pull", $"{i.Appeal * 0.45:0}", $"{i.Appeal:0}/100 × 45%: league, record, your name", i.Appeal >= 60 ? "good" : i.Appeal < 40 ? "bad" : ""),
            new("Playing time", $"{i.Minutes * 0.25:0}", $"{i.Minutes:0}/100 × 25%: who's ahead of him", i.Minutes >= 70 ? "good" : i.Minutes < 40 ? "bad" : ""),
            new("Your effort", $"{i.Effort:0}", "calls and visits (fades 3% a week)", i.Effort >= 15 ? "good" : ""),
            new("Interest", $"{i.Total:0}", $"he needs {i.Need:0} to commit", i.Enough ? "good" : "warn"),
        };
        RecruitLean = Session.Lean(p);
        if (_wageFor != p) { _wageFor = p; OfferWage = Math.Round((decimal)(Session.RecruitDemand(p).Wage / 1e6), 2); }
    }

    private Player? _wageFor;

    private void AfterRecruitAction(string reply)
    {
        RecruitReply = reply;
        FillRecruits();
        Shell.Guide.Refresh();
    }

    [RelayCommand] private void ShortlistRecruit() { if (SelectedRecruit?.Player is { } p) AfterRecruitAction(Session.Shortlist(p)); }
    [RelayCommand] private void CallRecruit() { if (SelectedRecruit?.Player is { } p) AfterRecruitAction(Session.CallRecruit(p)); }
    [RelayCommand] private void VisitRecruit() { if (SelectedRecruit?.Player is { } p) AfterRecruitAction(Session.VisitRecruit(p)); }
    [RelayCommand] private void OfferRecruit()
    {
        if (SelectedRecruit?.Player is not { } p) return;
        var wage = OfferWage; var years = OfferYears;
        AfterRecruitAction(Session.OfferRecruit(p, (long)(wage * 1_000_000m), (int)years));
    }
    [RelayCommand] private void OpenRecruit() { if (SelectedRecruit?.Player is { } p) Shell.ShowPlayer(p); }

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
