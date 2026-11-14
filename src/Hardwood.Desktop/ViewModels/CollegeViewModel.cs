using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.College;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;

namespace Hardwood.Desktop.ViewModels;

/// <summary>One tile of the Top 25 grid (Danny, #528, his AP-style graphic): the rank, the logo, the school, the record and the move.</summary>
public sealed class PollTile
{
    public required PollEntry Entry { get; init; }
    public required System.Windows.Input.ICommand Open { get; init; }
    public int Rank => Entry.Rank;
    public Team Team => Entry.Team;
    public string School => Team.City;
    public string Record { get; init; } = "";
    public string Conference { get; init; } = "";
    public string Move => Entry.MoveText;
    public bool Mine { get; init; }
    public Avalonia.Media.IBrush MoveBrush => Entry.Move switch
    {
        null => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FACC15")),
        > 0 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#22C55E")),
        < 0 => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#EF4444")),
        _ => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#8B95A5")),
    };
    public Avalonia.Media.IBrush Edge => Mine ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FACC15"))
        : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Team.PrimaryColor));
    public string Tip => $"{Team.FullName} · {Record} · {Conference}" + (Entry.Before is { } b ? $" · #{b} before the latest games" : " · new to the Top 25");
}

public sealed class RankRow
{
    public int Rank { get; init; }
    public string Move { get; init; } = "";
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

/// <summary>One team on a bracketology seed line (C4 part 1).</summary>
public sealed record SeedCell(string Name, string Record, string Tag, bool Mine)
{
    public bool HasTag => Tag.Length > 0;
}

public static class CollegeConverters
{
    /// <summary>Your program's cells get the accent edge; everyone else a quiet one.</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter MineEdge =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(b => b
            ? new Avalonia.Media.SolidColorBrush(Hardwood.Desktop.ThemeService.Accent)
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A4654")));
}

/// <summary>A seed line of the projected field: the seed and its four teams (eight on the 16 line, the First Four).</summary>
public sealed record SeedLine(string Seed, IReadOnlyList<SeedCell> Teams, bool Mine);

/// <summary>A bubble list: "Last four in", "First four out", "Next four out".</summary>
public sealed record BubbleList(string Title, IReadOnlyList<SeedCell> Teams, string Tone);

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
    /// <summary>The Top 25 as a 5 × 5 grid of logos (Danny, #528); the table is one click away.</summary>
    [ObservableProperty] private List<PollTile> _pollTiles = new();
    [ObservableProperty] private string _pollNote = "";
    [ObservableProperty] private string _droppedOut = "";
    public bool HasDroppedOut => DroppedOut.Length > 0;
    partial void OnDroppedOutChanged(string value) => OnPropertyChanged(nameof(HasDroppedOut));
    public string[] PollViews { get; } = { "Grid", "Table" };
    [ObservableProperty] private string _pollView = "Grid";
    public bool ShowPollGrid => PollView == "Grid";
    public bool ShowPollTable => !ShowPollGrid;
    partial void OnPollViewChanged(string value) { OnPropertyChanged(nameof(ShowPollGrid)); OnPropertyChanged(nameof(ShowPollTable)); }
    [RelayCommand] private void OpenTeam(Team? team) { if (team != null) Shell.ShowTeam(team); }
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
    /// <summary>A region's bracket needs the room for eight games; the Final Four view only two rows (C4 part 2).</summary>
    [ObservableProperty] private double _bracketHeight = 620;
    // Bracketology (C4 part 1, #191): the field as it would be picked today, until Selection Sunday.
    [ObservableProperty] private List<SeedLine> _seedLines = new();
    [ObservableProperty] private List<BubbleList> _bubble = new();
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _bracketologyChips = new();
    public bool HasBracketology => SeedLines.Count > 0;
    partial void OnSeedLinesChanged(List<SeedLine> value) => OnPropertyChanged(nameof(HasBracketology));
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

    private static DateOnly LastGameDay(CollegeWorld c) => c.Season.Games.Where(g => g.Played).Max(g => g.Date);

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
        var poll = c.Poll();
        var me = Session.InCollege ? Session.HumanTeam : null;
        foreach (var e in poll)
            Top25.Add(new RankRow { Rank = e.Rank, Move = e.MoveText, Team = e.Team, Conference = ConfName(c, e.Team), Record = $"{e.Wins}-{e.Losses}", Rating = $"{s.Elo[e.Team.Id]:0}" });
        PollTiles = poll.Select(e => new PollTile
        {
            Entry = e, Open = OpenTeamCommand, Record = $"{e.Wins}-{e.Losses}", Conference = ConfName(c, e.Team), Mine = e.Team == me,
        }).ToList();
        var dropped = c.DroppedOut();
        DroppedOut = dropped.Count == 0 ? "" : "Dropped out: " + string.Join(", ", dropped.Select(t => $"{t.City} (was #{s.PollBefore[t.Id]})"));
        bool started = s.Games.Any(g => g.Played);
        PollNote = started
            ? $"Re-ranked after every game, on the rating · arrows: since the last day of games ({LastGameDay(c):MMM d})"
            : "Preseason: ranked on the rating. Re-ranked after every game once the season tips off.";
        FillConference();
        FillBracket();
        FillProspects();
        FillRecruits();
        if (c.Recruiting)
        {
            // Seniors only (Danny, #320, D34 A): this year's seniors, and after their signing day next year's too (the class
            // being recruited from then on). Younger classes stay in the world but aren't scouted.
            int senior = c.SeniorClass, recruiting = c.RecruitingClass(Session.Today);
            var classes = Enumerable.Range(senior, recruiting - senior + 1).Reverse().Select(y => ClassLabel(c, y)).ToList();
            if (!classes.SequenceEqual(Classes))
            {
                Classes = classes; OnPropertyChanged(nameof(Classes));
                TrailClass = Classes[0]; // after signing day the rising seniors come first
                if (HighSchoolClass != Classes[0]) HighSchoolClass = Classes[0];
            }
            if (!Classes.Contains(HighSchoolClass)) HighSchoolClass = Classes[0]; else FillHighSchool();
        }
        FillTrail();
    }

    private string ClassLabel(CollegeWorld c, int year) => $"Class of {year}" + (year == c.SeniorClass ? " (seniors)" : " (rising seniors)");
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
        HighSchoolChips.Add(new Chip("Class", $"{year}", year == c.SeniorClass ? "seniors: enroll next summer" : "rising seniors: commit from Sep 1", "info"));
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
                Player = p, Rank = r.Rank, Stars = r.Stars > 0 ? new string('★', r.Stars) : "–", Age = p.Age(today), HighSchool = r.HighSchool,
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
        SeedLines = new(); Bubble = new(); BracketologyChips = new();
        if (!s.Selected) { FillBracketology(c); return; }
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
        BracketHeight = region >= 0 ? 620 : 200;
        if (region < 0)
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
        // Your program on Selection Sunday and since: its seed and region, and how far it got.
        if (Session.Employed && Session.InCollege)
        {
            int me = Session.HumanTeam.Id;
            if (s.Seeds.TryGetValue(me, out var seed))
            {
                int reached = c.MadnessReached(me), myRegion = c.RegionOf(me);
                bool alive = s.ChampionId == null && !boxes.Any(b => b.Played && (b.TeamA == me || b.TeamB == me) && b.Winner != me)
                             && !s.Games.Any(g => g.Event == CollegeWorld.Madness && g.Round == 0 && g.Played && (g.HomeId == me || g.AwayId == me) && g.WinnerId != me);
                string where = myRegion >= 0 ? MadnessBracket.Regions[myRegion] : "";
                string status = s.ChampionId == me ? "champions" : s.RunnerUpId == me ? "runners-up" : alive ? "still alive" : $"out in the {CollegeWorld.RoundName(Math.Max(0, reached))}";
                string tone = alive || s.ChampionId == me ? "good" : s.RunnerUpId == me || reached >= 5 ? "warn" : "bad";
                chips.Insert(0, new("Our seed", $"No. {seed}", $"{where} · {status}", tone));
            }
            else chips.Insert(0, new("Our seed", "Left out", c.Bracketology().StatusOf(me), "bad"));
        }
        MadnessChips = chips;
    }

    /// <summary>Bracketology (C4 part 1): seed lines, the bubble and your projection, until the real field is picked.</summary>
    private void FillBracketology(CollegeWorld c)
    {
        var s = c.Season;
        if (!s.Games.Any(g => g.Played))
        {
            int n = c.Active.Count();
            BracketNote = $"The 68-team field is picked on Selection Sunday in mid-March: the {n} conference tournament champions and {68 - n} at-large teams. Bracketology starts once the season does.";
            return;
        }
        var p = c.Bracketology();
        int? me = Session.Employed && Session.InCollege ? Session.HumanTeam.Id : null;
        var today = Session.Today;
        bool early = today.Month is >= 7 and <= 12 || today.Month == 1;
        int autos = c.Active.Count();
        BracketNote = (early ? "Early bracketology: " : "Bracketology: ")
            + $"the field if it were picked today, from the ratings and each conference's leader (its automatic bid until the conference tournament decides it). "
            + "Selection Sunday picks the real field the same way.";
        string Rec(int id) { var (w, l) = c.Record(id); return $"{w}-{l}"; }
        SeedCell Cell(int id) => new(c.Program(id)?.City ?? "?", Rec(id),
            p.AutoBids.TryGetValue(id, out var conf) ? conf : p.LastFourIn.Contains(id) ? "LAST IN" : "", id == me);
        SeedLines = Enumerable.Range(1, 16).Select(seed =>
        {
            var teams = p.Field.Where(id => p.Seeds[id] == seed).Select(Cell).ToList();
            return new SeedLine(seed.ToString(), teams, teams.Any(t => t.Mine));
        }).ToList();
        Bubble = new()
        {
            new("Last four in", p.LastFourIn.Select(Cell).ToList(), "good"),
            new("First four out", p.FirstFourOut.Select(id => Cell(id) with { Tag = "" }).ToList(), "warn"),
            new("Next four out", p.NextFourOut.Select(id => Cell(id) with { Tag = "" }).ToList(), "bad"),
        };
        var chips = new List<Hardwood.Desktop.Controls.Chip>();
        if (me is { } m)
        {
            string status = p.StatusOf(m);
            string tone = !p.In(m) ? "bad" : p.LastFourIn.Contains(m) ? "warn" : "good";
            string value = p.Seeds.TryGetValue(m, out var sd) ? $"No. {sd}" : status == "Out" ? "Out" : "Bubble";
            chips.Add(new("Our projection", value, p.In(m) ? (p.LastFourIn.Contains(m) ? "last four in" : p.AutoBids.TryGetValue(m, out var cf) ? $"automatic bid (projected {cf} champions)" : "at-large") : status.ToLowerInvariant(), tone));
        }
        chips.Add(new("Automatic bids decided", $"{p.AutoBidsDecided} of {autos}", "conference tournaments", "team"));
        if (p.LastFourIn.Count > 0) chips.Add(new("Last team in", c.Program(p.LastFourIn[^1])?.City ?? "", Rec(p.LastFourIn[^1]), "warn"));
        if (p.FirstFourOut.Count > 0) chips.Add(new("First team out", c.Program(p.FirstFourOut[0])?.City ?? "", Rec(p.FirstFourOut[0]), ""));
        var ones = p.Field.Take(4).ToList();
        chips.Add(new("Top seed", c.Program(ones[0])?.City ?? "", Rec(ones[0]), "good"));
        BracketologyChips = chips;
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
