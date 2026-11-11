using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;

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
    /// <summary>Summers until he turns pro (he graduates the summer he is 23).</summary>
    public int YearsLeft { get; init; }
    public string Turns => YearsLeft <= 1 ? "Turns pro next summer" : $"Pro in {YearsLeft} years";
}

/// <summary>A high-school prospect in a recruiting class (decision #162).</summary>
public sealed class RecruitRow
{
    public required Player Player { get; init; }
    public int Rank { get; init; }
    public string Stars { get; init; } = "";
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    public int Age { get; init; }
    public string HighSchool { get; init; } = "";
    public string State { get; init; } = "";
    public string Committed { get; init; } = "";
}

/// <summary>A program's recruiting class.</summary>
public sealed class ClassRankRow
{
    public int Rank { get; init; }
    public required Team Team { get; init; }
    public string Conference { get; init; } = "";
    public int Commits { get; init; }
    public int FiveStars { get; init; }
    public int FourStars { get; init; }
    public int Points { get; init; }
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
    public List<string> Ages { get; } = new() { "Turning pro next summer", "College (18-22)", "High school (14-17)", "All" };
    [ObservableProperty] private string _age = "Turning pro next summer";
    public List<string> Positions { get; } = new() { "All", "PG", "SG", "SF", "PF", "C" };
    [ObservableProperty] private string _position = "All";
    public ObservableCollection<ProspectListRow> Prospects { get; } = new();
    [ObservableProperty] private ProspectListRow? _selectedProspect;
    public bool HasCollege => Session.World.College != null;
    /// <summary>The recruiting tabs exist in careers with the high-school model (decision #162).</summary>
    public bool HasRecruiting => Session.World.College?.Recruiting == true;
    public List<string> Classes { get; private set; } = new();
    [ObservableProperty] private string _recruitClass = "";
    public List<string> StarFilters { get; } = new() { "All", "5-star", "4-star", "3-star", "2-star", "Uncommitted", "Committed" };
    [ObservableProperty] private string _starFilter = "All";
    public ObservableCollection<RecruitRow> Recruits { get; } = new();
    [ObservableProperty] private RecruitRow? _selectedRecruit;
    public ObservableCollection<Chip> RecruitChips { get; } = new();
    public ObservableCollection<ClassRankRow> ClassRanks { get; } = new();
    [ObservableProperty] private string _classNote = "";
    [ObservableProperty] private int _tab;

    partial void OnConferenceChanged(string value) => FillConference();
    partial void OnAgeChanged(string value) => FillProspects();
    partial void OnPositionChanged(string value) => FillProspects();
    partial void OnRecruitClassChanged(string value) => FillRecruits();
    partial void OnStarFilterChanged(string value) => FillRecruits();

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
        if (c.Recruiting)
        {
            int senior = c.SeniorClass;
            var classes = Enumerable.Range(senior, 4).Select(y => ClassLabel(c, y)).ToList();
            if (!classes.SequenceEqual(Classes)) { Classes = classes; OnPropertyChanged(nameof(Classes)); }
            if (!Classes.Contains(RecruitClass)) RecruitClass = Classes[0]; else FillRecruits();
        }
    }

    private string ClassLabel(CollegeWorld c, int year) => $"Class of {year}" + (year == c.SeniorClass ? " (seniors)" : year == c.SeniorClass + 3 ? " (freshmen)" : "");
    private int ClassYear => int.TryParse(RecruitClass.Replace("Class of ", "").Split(' ')[0], out var y) ? y : 0;

    private void FillRecruits()
    {
        Recruits.Clear(); RecruitChips.Clear(); ClassRanks.Clear();
        if (Session.World.College is not { Recruiting: true } c || ClassYear == 0) return;
        int year = ClassYear;
        var today = Session.Today;
        var all = c.ClassOf(year).Select(p => (p, r: c.Recruits[p.Id])).OrderBy(x => x.r.Rank).ToList();
        int committed = all.Count(x => x.r.CommittedTo != null);
        var signing = CollegeWorld.SigningDay(year);
        RecruitChips.Add(new Chip("Class", $"{year}", year == c.SeniorClass ? "seniors: enroll next summer" : $"enrolls in {year - today.Year} summers", "info"));
        RecruitChips.Add(new Chip("Committed", $"{committed} of {all.Count}", today < CollegeWorld.CommitmentsOpen(year) ? $"commitments open {CollegeWorld.CommitmentsOpen(year):MMM d, yyyy}" : "tracked prospects", committed > 0 ? "good" : ""));
        RecruitChips.Add(new Chip("Five-stars signed", $"{all.Count(x => x.r.Stars == 5 && x.r.CommittedTo != null)} of {CollegeWorld.FiveStars}", "the top 25 in the class", "info"));
        RecruitChips.Add(new Chip("Signing day", $"{signing:MMM d}", today > signing ? "done: the class is set" : $"{(signing.DayNumber - today.DayNumber)} days away", today > signing ? "good" : "warn"));
        var ranking = c.ClassRanking(year);
        if (ranking.Count > 0) RecruitChips.Add(new Chip("Top class", ranking[0].Program.FullName, $"{ranking[0].Points} pts · {ranking[0].FiveStars}× 5-star", ""));
        foreach (var (p, r) in all.Where(x => StarFilter switch
                 {
                     "5-star" => x.r.Stars == 5, "4-star" => x.r.Stars == 4, "3-star" => x.r.Stars == 3, "2-star" => x.r.Stars == 2,
                     "Uncommitted" => x.r.CommittedTo == null, "Committed" => x.r.CommittedTo != null, _ => true,
                 }).Take(300))
            Recruits.Add(new RecruitRow
            {
                Player = p, Rank = r.Rank, Stars = CollegeWorld.StarText(r.Stars), Age = p.Age(today), HighSchool = r.HighSchool,
                State = r.State, Committed = r.CommittedTo is { } to ? c.Program(to)!.City : "Uncommitted",
            });
        int rank = 1;
        foreach (var x in ranking.Take(50))
            ClassRanks.Add(new ClassRankRow { Rank = rank++, Team = x.Program, Conference = ConfName(c, x.Program), Commits = x.Commits, FiveStars = x.FiveStars, FourStars = x.FourStars, Points = x.Points });
        ClassNote = ranking.Count == 0 ? $"No one in the Class of {year} has committed yet." : $"Points: 100 for a 5-star, 40 for a 4-star, 15 for a 3-star, 5 for a 2-star.";
    }

    [RelayCommand]
    private void OpenRecruit(RecruitRow? row)
    {
        if ((row ?? SelectedRecruit) is { } r) Shell.ShowPlayer(r.Player);
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
        // He turns pro the summer he is 23; "next summer" is the coming July.
        var nextSummer = new DateOnly(today.Month >= 7 ? today.Year + 1 : today.Year, 7, 1);
        int YearsLeft(Player p) => Math.Max(1, CollegeWorld.GraduateAge - p.Age(nextSummer) + 1);
        string Where(Player p, Team? t) => t != null ? t.FullName
            : c.RecruitOf(p) is { } r ? $"HS: {r.HighSchool}, {r.State}" + (r.CommittedTo is { } to ? $" → {c.Program(to)!.Abbreviation}" : "") : "";
        var everyone = c.Programs.SelectMany(t => t.Roster.Select(p => (p, t: (Team?)t))).Concat(c.Prospects.Select(p => (p, t: (Team?)null)));
        var rows = everyone
            .Where(x => Position == "All" || x.p.PrimaryPosition.ToString() == Position)
            .Select(x => (x.p, x.t, age: x.p.Age(today), left: YearsLeft(x.p)))
            .Where(x => Age switch { "Turning pro next summer" => x.left <= 1, "College (18-22)" => x.t != null && x.age >= 18, "High school (14-17)" => x.t == null || x.age <= 17, _ => true })
            .OrderByDescending(x => x.p.PotentialAbility * 0.6 + x.p.CurrentAbility * 0.4).Take(200);
        foreach (var (p, t, age, left) in rows)
        {
            c.Season.Stats.TryGetValue(p.Id, out var st);
            Prospects.Add(new ProspectListRow
            {
                Player = p, Age = age, Program = Where(p, t), Knowledge = Scouting.Knowledge(team, p, prospect: true), YearsLeft = left,
                Line = st is { G: > 0 } ? $"{st.Ppg:F1} pts · {st.Rpg:F1} reb · {st.Apg:F1} ast" : t == null ? "high school" : "-",
            });
        }
    }

    [RelayCommand]
    private void OpenProspect(ProspectListRow? row)
    {
        if ((row ?? SelectedProspect) is { } r) Shell.ShowPlayer(r.Player);
    }
}
