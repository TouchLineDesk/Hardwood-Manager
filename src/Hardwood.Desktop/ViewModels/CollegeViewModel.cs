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
public sealed class HighSchoolRow
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

/// <summary>One matchup box in the bracket view.</summary>
public sealed record BracketCard(string SeedA, string NameA, string ScoreA, bool WinA, string SeedB, string NameB, string ScoreB, bool WinB, string Note, bool Upset)
{
    public double OpacityA => WinB ? 0.55 : 1;
    public double OpacityB => WinA ? 0.55 : 1;
}

/// <summary>A round of the selected region: its name and boxes.</summary>
public sealed record BracketColumn(string Title, IReadOnlyList<BracketCard> Cards, int Rows);

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
    // March Madness as a bracket (college depth part 2): pick a region or the Final Four.
    public List<string> BracketViews { get; } = MadnessBracket.Regions.Append("Final Four").ToList();
    [ObservableProperty] private string _bracketView = "East";
    [ObservableProperty] private List<BracketColumn> _bracketColumns = new();
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _madnessChips = new();
    public bool HasBracket => BracketColumns.Count > 0;
    partial void OnBracketColumnsChanged(List<BracketColumn> value) => OnPropertyChanged(nameof(HasBracket));
    partial void OnBracketViewChanged(string value) => FillBracket();
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
    [ObservableProperty] private string _highSchoolClass = "";
    public List<string> StarFilters { get; } = new() { "All", "5-star", "4-star", "3-star", "2-star", "Uncommitted", "Committed" };
    [ObservableProperty] private string _starFilter = "All";
    public ObservableCollection<HighSchoolRow> HighSchoolers { get; } = new();
    [ObservableProperty] private HighSchoolRow? _selectedHighSchool;
    public ObservableCollection<Chip> HighSchoolChips { get; } = new();
    public ObservableCollection<ClassRankRow> ClassRanks { get; } = new();
    [ObservableProperty] private string _classNote = "";

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
    partial void OnHighSchoolClassChanged(string value) => FillHighSchool();
    partial void OnStarFilterChanged(string value) => FillHighSchool();

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
        if (c.Recruiting)
        {
            int senior = c.SeniorClass;
            var classes = Enumerable.Range(senior, 4).Select(y => ClassLabel(c, y)).ToList();
            if (!classes.SequenceEqual(Classes)) { Classes = classes; OnPropertyChanged(nameof(Classes)); }
            if (!Classes.Contains(HighSchoolClass)) HighSchoolClass = Classes[0]; else FillHighSchool();
        }
    }

    private string ClassLabel(CollegeWorld c, int year) => $"Class of {year}" + (year == c.SeniorClass ? " (seniors)" : year == c.SeniorClass + 3 ? " (freshmen)" : "");
    private int ClassYear => int.TryParse(HighSchoolClass.Replace("Class of ", "").Split(' ')[0], out var y) ? y : 0;

    private void FillHighSchool()
    {
        HighSchoolers.Clear(); HighSchoolChips.Clear(); ClassRanks.Clear();
        if (Session.World.College is not { Recruiting: true } c || ClassYear == 0) return;
        int year = ClassYear;
        var today = Session.Today;
        var all = c.ClassOf(year).Select(p => (p, r: c.Recruits[p.Id])).OrderBy(x => x.r.Rank).ToList();
        int committed = all.Count(x => x.r.CommittedTo != null);
        var signing = CollegeWorld.SigningDay(year);
        HighSchoolChips.Add(new Chip("Class", $"{year}", year == c.SeniorClass ? "seniors: enroll next summer" : $"enrolls in {year - today.Year} summers", "info"));
        HighSchoolChips.Add(new Chip("Committed", $"{committed} of {all.Count}", today < CollegeWorld.CommitmentsOpen(year) ? $"commitments open {CollegeWorld.CommitmentsOpen(year):MMM d, yyyy}" : "tracked prospects", committed > 0 ? "good" : ""));
        HighSchoolChips.Add(new Chip("Five-stars signed", $"{all.Count(x => x.r.Stars == 5 && x.r.CommittedTo != null)} of {CollegeWorld.FiveStars}", "the top 25 in the class", "info"));
        HighSchoolChips.Add(new Chip("Signing day", $"{signing:MMM d}", today > signing ? "done: the class is set" : $"{(signing.DayNumber - today.DayNumber)} days away", today > signing ? "good" : "warn"));
        var ranking = c.ClassRanking(year);
        if (ranking.Count > 0) HighSchoolChips.Add(new Chip("Top class", ranking[0].Program.FullName, $"{ranking[0].Points} pts · {ranking[0].FiveStars}× 5-star", ""));
        foreach (var (p, r) in all.Where(x => StarFilter switch
                 {
                     "5-star" => x.r.Stars == 5, "4-star" => x.r.Stars == 4, "3-star" => x.r.Stars == 3, "2-star" => x.r.Stars == 2,
                     "Uncommitted" => x.r.CommittedTo == null, "Committed" => x.r.CommittedTo != null, _ => true,
                 }).Take(300))
            HighSchoolers.Add(new HighSchoolRow
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
    private void OpenHighSchool(HighSchoolRow? row)
    {
        if ((row ?? SelectedHighSchool) is { } r) Shell.ShowPlayer(r.Player);
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
            new("Committed to you", ours.ToString(), Session.NoSpotFor() != null ? "no spots left this season" : $"sign when the season ends · up to {GameSession.MaxCommitments}", Session.NoSpotFor() != null ? "warn" : ours > 0 ? "good" : ""),
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
        BracketColumns = new(); MadnessChips = new();
        if (Session.World.College is not { } c) return;
        var s = c.Season;
        if (!s.Selected) { BracketNote = "The 68-team field is picked on Selection Sunday in mid-March: eight conference tournament champions and 60 at-large teams."; return; }
        BracketNote = s.ChampionId is { } ch ? $"Champions: {c.Program(ch)!.FullName}." : "Every March Madness game uses the full match engine. Upsets (a seed four or more lines worse winning) are marked.";
        var boxes = MadnessBracket.Boxes(s);
        // Brackets use the school name ("Jersey Christian"), as on TV; the full name is a click away.
        string Name(int? id) => id is { } x ? c.Program(x)?.City ?? "?" : "To be decided";
        BracketCard Card(BracketBox b) => new(
            b.TeamA != null ? b.SeedA.ToString() : "", Name(b.TeamA), b.ScoreA?.ToString() ?? "", b.Played && b.Winner == b.TeamA,
            b.TeamB != null ? b.SeedB.ToString() : "", b.TeamB == null && b.Round == 1 ? "First Four winner" : Name(b.TeamB), b.ScoreB?.ToString() ?? "", b.Played && b.Winner == b.TeamB,
            b.Played ? "Final" : b.Date is { } d ? d.ToString("MMM d") : "", b.Upset);
        int region = MadnessBracket.Regions.ToList().IndexOf(BracketView);
        if (region >= 0)
            BracketColumns = Enumerable.Range(1, 4).Select(r =>
            {
                var cards = boxes.Where(b => b.Round == r && MadnessBracket.RegionOf(b) == region).OrderBy(b => b.Slot).Select(Card).ToList();
                return new BracketColumn(CollegeWorld.RoundName(r), cards, cards.Count);
            }).ToList();
        else
            BracketColumns = new()
            {
                new BracketColumn("Final Four", boxes.Where(b => b.Round == 5).OrderBy(b => b.Slot).Select(Card).ToList(), 2),
                new BracketColumn("Championship", boxes.Where(b => b.Round == 6).Select(Card).ToList(), 1),
            };
        // The tournament at a glance.
        var played = boxes.Where(b => b.Played).ToList();
        var cinderella = boxes.Where(b => b.Round >= 3).SelectMany(b => new[] { (b.TeamA, b.SeedA), (b.TeamB, b.SeedB) })
            .Where(x => x.Item1 != null && x.Item2 >= 10).Select(x => x.Item1!.Value).Distinct().ToList();
        var biggest = played.Where(b => b.Upset).OrderByDescending(b => Math.Abs(b.SeedA - b.SeedB)).FirstOrDefault();
        var chips = new List<Hardwood.Desktop.Controls.Chip>
        {
            new("Stage", c.StageText.Replace("March Madness: ", ""), s.ChampionId != null ? "" : "next up", "team"),
            new("Upsets", played.Count(b => b.Upset).ToString(), "a seed 4+ lines worse winning", "warn"),
            new("Biggest upset", biggest == null ? "None yet" : $"{Math.Max(biggest.SeedA, biggest.SeedB)} over {Math.Min(biggest.SeedA, biggest.SeedB)}",
                biggest == null ? "" : c.Program(biggest.Winner!.Value)?.FullName ?? "", biggest == null ? "" : "warn"),
            new("Cinderellas", cinderella.Count.ToString(), cinderella.Count == 0 ? "no double-digit seed in the Sweet 16" : string.Join(", ", cinderella.Take(2).Select(id => c.Program(id)?.Name)), cinderella.Count > 0 ? "good" : ""),
        };
        if (s.ChampionId is { } champ) chips.Insert(0, new("Champions", c.Program(champ)!.Name, c.Program(champ)!.City, "good"));
        MadnessChips = chips;
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
