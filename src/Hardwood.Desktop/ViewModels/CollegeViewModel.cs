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

/// <summary>CP3 (run 132): one of your program's graduates in the pros (the College page's Your pros tab).</summary>
public sealed class AlumnusRow
{
    public required AlumnusLine Line { get; init; }
    public Player Player => Line.Player;
    public string Name => Line.Player.Name;
    public string Pos => Line.Player.PrimaryPosition.ToString();
    /// <summary>K12b (run 137): a real alumnus from before this career has no class year here ("Earlier").</summary>
    public string Class => Line.Class == 0 ? "Earlier" : Line.Class.ToString();
    public string Club => Line.Club;
    public string League => Line.League;
    public int CA => Line.Player.CurrentAbility;
    public int Age { get; init; }
    /// <summary>H161 (run 138): a real alumnus shows his real draft ("2012 · pick 1"); "-" only when the pack doesn't know it.</summary>
    public string Draft => Line.Draft is { } d ? $"{d.Year} · pick {d.Pick}" : Line.Real is { } r ? r.DraftText : Line.Class == 0 ? "-" : "Undrafted";
    /// <summary>"2 All-Star · 1 award" (or "-"); a real alumnus who has been an All-Star reads "All-Star".</summary>
    public string Honours => !Line.EverAllStar && Line.Awards == 0 ? "-"
        : string.Join(" · ", new[] { Line.AllStars > 0 ? $"{Line.AllStars} All-Star" : Line.EverAllStar ? "All-Star" : "", Line.Awards > 0 ? $"{Line.Awards} award{(Line.Awards == 1 ? "" : "s")}" : "" }.Where(x => x != ""));
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
    /// <summary>CP3 (run 132): potential as your scouts' range ("150-168"), never exact for a college player or recruit.</summary>
    public Scouting.Range PaRange { get; init; }
    public string PA => PaRange.ToString();
    public string Line { get; init; } = "";
    /// <summary>Summers until he turns pro (he graduates the summer he is 22, D80 A).</summary>
    public int YearsLeft { get; init; }
    public string Turns => YearsLeft <= 1 ? "Turns pro next summer" : $"Pro in {YearsLeft} years";
}

/// <summary>A high-school prospect in a recruiting class (decision #162).</summary>
public sealed partial class HighSchoolRow : ObservableObject
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
    // Run 68 (review #13 rank 7): the prospect as a star card.
    public int StarCount { get; init; }
    public Team? CommittedTeam { get; init; }
    [ObservableProperty] private bool _picked;
    public string RankText => $"#{Rank}";
    public string StarsOn => CollegeCards.On(StarCount);
    public string StarsOff => CollegeCards.Off(StarCount);
    public Avalonia.Media.IBrush StarBrush => CollegeCards.StarEdge(StarCount);
    public string CardLine => $"{Pos} · {PlayerRow.FormatHeight(Player.HeightCm)} · age {Age}";
    public string Where => $"{HighSchool}, {CollegeWorld.StateName(State)}";
    public bool HasCommit => CommittedTeam != null;
    public string CommitText => CommittedTeam is { } t ? $"Committed to {t.City}" : "Uncommitted";
    public string StarTip => CollegeCards.StarTip(StarCount, Rank);
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
public sealed record SeedCell(string Name, string Record, string Tag, bool Mine, string Detail = "")
{
    public bool HasTag => Tag.Length > 0;
    /// <summary>Run 193: the bubble's résumé line ("NET 44 · Q1 3-6").</summary>
    public bool HasDetail => Detail.Length > 0;
}

public static class CollegeConverters
{
    /// <summary>Your program's cells get the accent edge; everyone else a quiet one.</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter MineEdge =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(b => b
            ? new Avalonia.Media.SolidColorBrush(Hardwood.Desktop.ThemeService.Accent)
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3A4654")));
    /// <summary>The Selection Show's board: the pairing on screen now is lit.</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter NowFill =
        new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(b => b
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#7C5CFF"))
            : Avalonia.Media.Brushes.Transparent);
}

/// <summary>A seed line of the projected field: the seed and its four teams (eight on the 16 line, the First Four).</summary>
public sealed record SeedLine(string Seed, IReadOnlyList<SeedCell> Teams, bool Mine);

/// <summary>A bubble list: "Last four in", "First four out", "Next four out".</summary>
public sealed record BubbleList(string Title, IReadOnlyList<SeedCell> Teams, string Tone);

/// <summary>A team (or a First Four pair) on Selection Sunday's reveal (C4 part 2).</summary>
public sealed record RevealRow(string Seed, string Name, string Record, string Tag, bool Mine)
{
    public bool HasTag => Tag.Length > 0;
}

/// <summary>A first-round pairing on the reveal: 1 v 16, 8 v 9 …</summary>
public sealed record RevealPair(RevealRow Top, RevealRow Bottom, bool Mine);

/// <summary>A region on Selection Sunday: hidden until revealed, then its eight pairings.</summary>
public sealed record RevealRegion(string Name, bool Shown, string Line, IReadOnlyList<RevealPair> Pairs, bool Mine)
{
    public bool Hidden => !Shown;
}

/// <summary>A team on the Final Four weekend page (C4 part 2): its region, seed, record and the road there.</summary>
public sealed record FinalFourTeam(string Region, string Seed, string Name, string Record, string Route, string Tag, bool Mine, bool Out)
{
    public bool HasTag => Tag.Length > 0;
    public double Fade => Out ? 0.55 : 1.0;
}

/// <summary>One side of a Final Four game: seed, school, score, and whether it won.</summary>
public sealed record WeekendSide(string Seed, string Name, string Score, bool Won, bool Mine);

/// <summary>A national semi-final or the championship game: both sides, the date or "Final", and each side's leading scorer.</summary>
public sealed record WeekendGame(string Title, string When, WeekendSide A, WeekendSide B, string Stars, bool Mine)
{
    public bool HasStars => Stars.Length > 0;
}

/// <summary>A highlight card of the champions' run: the round, the result, the star and what made it.</summary>
public sealed record MontageCard(string Round, string Result, string Opponent, string Star, string Tag)
{
    public bool HasStar => Star.Length > 0;
    public bool HasTag => Tag.Length > 0;
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
    /// <summary>C5 (run 96): the picked conference's playing style.</summary>
    [ObservableProperty] private string _conferenceStyle = "";
    public ObservableCollection<string> Bracket { get; } = new();
    // March Madness as a bracket (college depth part 2): pick a region or the Final Four.
    public const string SelectionView = "Selection Sunday";
    /// <summary>Run 190 (Danny, Nov 24, `claude/bracket-redesign.md`): the whole field of 64 on one screen, the TV graphic.</summary>
    public const string FullView = "Full bracket";
    public List<string> BracketViews { get; } = MadnessBracket.Regions.Prepend(FullView).Prepend(SelectionView).Append("Final Four").ToList();
    [ObservableProperty] private string _bracketView = FullView;
    /// <summary>Run 190: what the drawn bracket shows (the whole field or one region); null on the reveal and the weekend page.</summary>
    [ObservableProperty] private Hardwood.Desktop.Controls.MadnessFieldData? _madnessField;
    public bool ShowField => MadnessField != null;
    partial void OnMadnessFieldChanged(Hardwood.Desktop.Controls.MadnessFieldData? value) => OnPropertyChanged(nameof(ShowField));
    [ObservableProperty] private List<BracketColumn> _bracketColumns = new();
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _madnessChips = new();
    public bool HasBracket => BracketColumns.Count > 0;
    partial void OnBracketColumnsChanged(List<BracketColumn> value) => OnPropertyChanged(nameof(HasBracket));
    partial void OnBracketViewChanged(string value) => FillBracket();
    /// <summary>The field has been picked: the view picker and the tournament chips show (the bracket or the reveal).</summary>
    [ObservableProperty] private bool _fieldPicked;

    // Selection Sunday as a reveal (C4 part 2, college-sim-design #164): the four regions one at a time, then the bubble.
    // How many regions you've revealed is kept per season while the game is open; once a game is played it's all shown.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CollegeSeason, System.Runtime.CompilerServices.StrongBox<int>> Revealed = new();
    [ObservableProperty] private bool _showSelection;
    [ObservableProperty] private List<RevealRegion> _revealRegions = new();
    [ObservableProperty] private List<BubbleList> _selectionBubble = new();
    [ObservableProperty] private string _selectionIntro = "";
    [ObservableProperty] private string _revealButton = "";
    [ObservableProperty] private bool _canReveal;
    public bool HasSelectionBubble => SelectionBubble.Count > 0;
    partial void OnSelectionBubbleChanged(List<BubbleList> value) => OnPropertyChanged(nameof(HasSelectionBubble));
    private CollegeSeason? _revealOpenedFor, _bracketOpenedFor;

    /// <summary>Regions revealed so far this season (0-4).</summary>
    public int RevealedRegions
    {
        get
        {
            if (Session.World.College is not { } c || !c.Season.Selected) return 0;
            if (c.Season.Games.Any(g => g.Event == CollegeWorld.Madness && g.Played)) return 4;
            return Revealed.GetValue(c.Season, _ => new System.Runtime.CompilerServices.StrongBox<int>(0)).Value;
        }
    }

    [RelayCommand]
    private void RevealNext()
    {
        if (Session.World.College is not { } c || !c.Season.Selected) return;
        var box = Revealed.GetValue(c.Season, _ => new System.Runtime.CompilerServices.StrongBox<int>(0));
        box.Value = Math.Min(4, Math.Max(box.Value, RevealedRegions) + 1);
        FillBracket();
    }

    [RelayCommand]
    private void RevealAll()
    {
        if (Session.World.College is not { } c || !c.Season.Selected) return;
        Revealed.GetValue(c.Season, _ => new System.Runtime.CompilerServices.StrongBox<int>(0)).Value = 4;
        FillBracket();
    }
    // The Final Four weekend (C4 part 2, #164): the neutral site, the four teams and their roads, the semis and the final,
    // then the champions' run as highlight cards.
    public const string FinalFourView = "Final Four";
    [ObservableProperty] private bool _showFinalFour;
    [ObservableProperty] private string _weekendTitle = "";
    [ObservableProperty] private string _weekendSub = "";
    [ObservableProperty] private List<FinalFourTeam> _weekendTeams = new();
    [ObservableProperty] private List<WeekendGame> _weekendGames = new();
    [ObservableProperty] private List<MontageCard> _montage = new();
    [ObservableProperty] private string _montageTitle = "";
    [ObservableProperty] private string _montageSub = "";
    public bool HasMontage => Montage.Count > 0;
    partial void OnMontageChanged(List<MontageCard> value) => OnPropertyChanged(nameof(HasMontage));
    private CollegeSeason? _finalFourOpenedFor;
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
    // CP3 (run 132): your graduates in the pros.
    public ObservableCollection<AlumnusRow> Alumni { get; } = new();
    [ObservableProperty] private AlumnusRow? _selectedAlumnus;
    [ObservableProperty] private List<Chip> _alumniChips = new();
    [ObservableProperty] private string _alumniNote = "";
    public bool HasAlumni => Alumni.Count > 0;
    public bool NoAlumni => Alumni.Count == 0;
    [RelayCommand] private void OpenAlumnus(AlumnusRow? row) { if ((row ?? SelectedAlumnus) is { } r) Shell.ShowPlayer(r.Player); }
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

    // Run 68 (review #13 rank 7, Danny's #464): the recruiting boards open on star cards; the full tables are one click away.
    public string[] BoardViews { get; } = { "Cards", "Full table" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowBoardCards), nameof(ShowBoardTable))] private string _boardView = "Cards";
    public bool ShowBoardCards => BoardView == "Cards";
    public bool ShowBoardTable => !ShowBoardCards;
    public ObservableCollection<HighSchoolRow> HighSchoolCards { get; } = new();
    [ObservableProperty] private string _highSchoolCardNote = "";
    [ObservableProperty] private string _highSchoolHint = "";
    public bool HasHighSchoolHint => HighSchoolHint.Length > 0;
    partial void OnHighSchoolHintChanged(string value) => OnPropertyChanged(nameof(HasHighSchoolHint));
    partial void OnSelectedHighSchoolChanged(HighSchoolRow? value)
    {
        foreach (var r in HighSchoolers) r.Picked = r == value;
        HighSchoolHint = value == null ? "" : $"{value.Name} ({char.ToLowerInvariant(value.CommitText[0])}{value.CommitText[1..]}) picked: press Open player for his page, or pick another card.";
    }
    /// <summary>A card press picks him, as a row click in the table does.</summary>
    public void PickHighSchool(HighSchoolRow row) => SelectedHighSchool = row;

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
    // Bug list #13: the panel binds to these, not SelectedRecruit.X, so no selection logs no binding errors.
    public string RecruitName => SelectedRecruit?.Name ?? "";
    public string RecruitProgram => SelectedRecruit?.Program ?? "";
    partial void OnSelectedRecruitChanged(RecruitRow? value)
    {
        OnPropertyChanged(nameof(HasRecruit)); OnPropertyChanged(nameof(RecruitName)); OnPropertyChanged(nameof(RecruitProgram));
        RecruitReply = ""; FillRecruitPanel();
    }
    partial void OnListedOnlyChanged(bool value) => FillRecruits();
    [ObservableProperty] private int _tab;

    // H72 (run 92): the picker used to start on CollegeWorld.Conferences[0], a conference real careers don't have, so the
    // ComboBox pushed null back and the tab stayed empty. A null or unknown pick now goes back to a real one.
    partial void OnConferenceChanged(string value)
    {
        if (value == null || !ConferenceNames.Contains(value)) { if (ConferenceNames.Count > 0 && value != DefaultConference()) Conference = DefaultConference(); return; }
        FillConference();
    }

    /// <summary>Your own program's conference in a college career, otherwise the first one this world has.</summary>
    private string DefaultConference()
    {
        if (Session.World.College is { } c && Session.InCollege && c.ConferenceOf.TryGetValue(Session.HumanTeam.Id, out var key)
            && CollegeWorld.Conferences.FirstOrDefault(x => x.Key == key) is { } mine && ConferenceNames.Contains(mine.Name))
            return mine.Name;
        return ConferenceNames.FirstOrDefault() ?? "";
    }
    /// <summary>H163 (run 155): what the Prospects tab says when its filter has nobody ("" when it has rows).</summary>
    [ObservableProperty] private string _prospectsEmpty = "";
    public bool HasProspects => Prospects.Count > 0;
    public bool ProspectsOfferCollege => !HasProspects && Age == "Turning pro next summer" && Position == "All";
    [RelayCommand] private void ShowCollegeProspects() => Age = "College (18-22)";

    partial void OnAgeChanged(string value) => FillProspects();
    partial void OnPositionChanged(string value) => FillProspects();
    partial void OnHighSchoolClassChanged(string value) => FillHighSchool();
    partial void OnStarFilterChanged(string value) => FillHighSchool();

    public CollegeViewModel(MainViewModel shell) : base(shell)
    {
        // Only the conferences this career has (careers from before decision #121 have eight).
        if (Session.World.College is { } c) ConferenceNames = c.Active.Select(x => x.Name).ToList();
        _conference = DefaultConference();
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
        // H148 (run 133): the page answers "where are we?" first, and says the poll weighs the record.
        string mine = me != null && Session.Employed ? (c.Rank(me.Id) is { } myRank ? $"{me.City}: No. {myRank}" : $"{me.City}: unranked (No. {c.PollPosition(me.Id)})") + " · " : "";
        PollNote = mine + (c.FinalPoll
            ? "Final poll: the champion first, then the record and the rating with every March Madness win counted"
            : started
            ? (s.PollRelease.Count > 0 && s.PollDay is { } released
                ? $"Released every Monday, on the rating and the record · this poll: {released:ddd MMM d} · arrows: since the week before"
                : $"Re-ranked after every game until the first Monday release, on the rating and the record")
            : "Preseason: ranked on the rating. Once the season tips off, a new poll comes out every Monday, on the rating and the record.");
        FillConference();
        // Between Selection Sunday and the first game, March Madness opens on the reveal.
        // (The page is kept while the game is open, so this is decided once per season.) Once the games start, a page
        // still on the reveal moves to the full bracket (run 190).
        bool madnessStarted = s.Games.Any(g => g.Event == CollegeWorld.Madness && g.Played);
        if (s.Selected && !madnessStarted && _revealOpenedFor != s) { _revealOpenedFor = s; BracketView = SelectionView; }
        if (madnessStarted && _bracketOpenedFor != s)
        {
            _bracketOpenedFor = s;
            // Run 190: the whole field, as on TV (it was your region's four columns).
            if (BracketView == SelectionView) BracketView = FullView;
        }
        // Once the Final Four is set, the page opens on the weekend (once a season, so you can still look back at a region).
        bool fourSet = s.Games.Count(g => g.Event == CollegeWorld.Madness && g.Round == 4 && g.Played) == 4;
        if (fourSet && _finalFourOpenedFor != s) { _finalFourOpenedFor = s; BracketView = FinalFourView; }
        FillBracket();
        FillProspects();
        FillAlumni();
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

    /// <summary>H177: the High school table lists at most this many prospects of a class.</summary>
    public const int HighSchoolTable = 300;

    private void FillHighSchool()
    {
        HighSchoolers.Clear(); HighSchoolCards.Clear(); HighSchoolChips.Clear(); ClassRanks.Clear(); HighSchoolCardNote = "";
        if (Session.World.College is not { Recruiting: true } c || ClassYear == 0) return;
        int year = ClassYear;
        var today = Session.Today;
        var all = c.ClassOf(year).Select(p => (p, r: c.Recruits[p.Id])).OrderBy(x => x.r.Rank).ToList();
        int committed = all.Count(x => x.r.CommittedTo != null);
        var signing = CollegeWorld.SigningDay(year);
        HighSchoolChips.Add(new Chip("Class", $"{year}", year == c.SeniorClass ? "seniors: enroll next summer" : "rising seniors: commit from Sep 1", "info"));
        HighSchoolChips.Add(new Chip("Committed", $"{committed} of {all.Count:N0}", today < CollegeWorld.CommitmentsOpen(year) ? $"commitments open {CollegeWorld.CommitmentsOpen(year):MMM d, yyyy}" : "tracked prospects", committed > 0 ? "good" : ""));
        HighSchoolChips.Add(new Chip("Five-stars signed", $"{all.Count(x => x.r.Stars == 5 && x.r.CommittedTo != null)} of {CollegeWorld.FiveStars}", "the top 25 in the class", "info"));
        HighSchoolChips.Add(new Chip("Signing day", $"{signing:MMM d}", today > signing ? "done: the class is set" : $"{(signing.DayNumber - today.DayNumber)} days away", today > signing ? "good" : "warn"));
        var ranking = c.ClassRanking(year);
        if (ranking.Count > 0) HighSchoolChips.Add(new Chip("Top class", ranking[0].Program.FullName, $"{ranking[0].Points} pts · {ranking[0].FiveStars}× 5-star", ""));
        var shown = all.Where(x => StarFilter switch
                 {
                     "5-star" => x.r.Stars == 5, "4-star" => x.r.Stars == 4, "3-star" => x.r.Stars == 3, "2-star" => x.r.Stars == 2,
                     "Uncommitted" => x.r.CommittedTo == null, "Committed" => x.r.CommittedTo != null, _ => true,
                 }).ToList();
        foreach (var (p, r) in shown.Take(HighSchoolTable))
            HighSchoolers.Add(new HighSchoolRow
            {
                Player = p, Rank = r.Rank, Stars = r.Stars > 0 ? new string('★', r.Stars) : "–", Age = p.Age(today), HighSchool = r.HighSchool,
                State = r.State, Committed = r.CommittedTo is { } to ? c.Program(to)!.City : "Uncommitted",
                StarCount = r.Stars, CommittedTeam = r.CommittedTo is { } to2 ? c.Program(to2) : null,
            });
        foreach (var r in HighSchoolers.Take(CollegeCards.CardCount)) HighSchoolCards.Add(r);
        string which = StarFilter == "All" ? "" : $" ({StarFilter.ToLowerInvariant()})";
        HighSchoolCardNote = HighSchoolers.Count == 0 ? $"No prospects{which} in the Class of {year}. Pick another filter."
            : HighSchoolers.Count <= CollegeCards.CardCount ? $"All {HighSchoolers.Count} prospects{which} in the Class of {year}, best first. Pick a card, then Open player."
            // H177 (run 172): the note counts the whole class, as the Committed chip does ("0 of 1429" beside "of 300").
            : shown.Count > HighSchoolTable
                ? $"The best {CollegeCards.CardCount} of {shown.Count:N0}{which} in the Class of {year}. Pick a card, then Open player; the table lists the top {HighSchoolTable}."
                : $"The best {CollegeCards.CardCount} of {shown.Count:N0}{which} in the Class of {year}. Pick a card, then Open player; the full table lists them all.";
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
        ConferenceStyle = "";
        if (Session.World.College is not { } c) return;
        if (c.Active.FirstOrDefault(x => x.Name == Conference) is not { } conf) return;
        ConferenceStyle = CollegeWorld.StyleLine(conf.Key) + " in conference play and the conference tournament.";
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
        BracketColumns = new(); MadnessChips = new(); MadnessField = null;
        if (Session.World.College is not { } c) return;
        var s = c.Season;
        SeedLines = new(); Bubble = new(); BracketologyChips = new();
        RevealRegions = new(); SelectionBubble = new();
        WeekendTeams = new(); WeekendGames = new(); Montage = new();
        FieldPicked = s.Selected;
        ShowFinalFour = s.Selected && BracketView == FinalFourView;
        ShowSelection = s.Selected && BracketView == SelectionView;
        if (!ShowSelection) { ShowOn = false; StopAutorun(); }
        if (!s.Selected) { FillBracketology(c); return; }
        if (ShowSelection) { FillSelection(c); return; }
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
        BracketHeight = 620;
        if (region >= 0 || BracketView == FullView)
            MadnessField = new Hardwood.Desktop.Controls.MadnessFieldData
            {
                World = c, Mine = Session.Employed && Session.InCollege ? Session.HumanTeam.Id : null, Region = region,
            };
        if (BracketView == FinalFourView) FillFinalFour(c, boxes);
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
            new("Cinderellas", cinderella.Count.ToString(), cinderella.Count == 0 ? "no double-digit seed in the Sweet 16" : string.Join(", ", cinderella.Take(2).Select(id => c.Program(id)?.City)) /* K24: the school, not "Tigers" */, cinderella.Count > 0 ? "good" : ""),
        };
        if (s.ChampionId is { } champ) chips.Insert(0, new("Champions", c.Program(champ)!.City, c.Program(champ)!.Name, "good")); // K24: the school first
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
            if (c.InNit(me)) chips.Insert(1, NitChip(c, me));
        }
        if (s.EventWinners.TryGetValue(CollegeWorld.Nit, out var nitChamp)) chips.Add(new("NIT champions", c.Program(nitChamp)?.City ?? "", CollegeWorld.NitFinalSite, ""));
        MadnessChips = chips;
    }

    /// <summary>
    /// The Final Four weekend (C4 part 2, college-sim-design #164): the neutral site and dates, the four regional champions
    /// with their road there (or who's still alive), the two national semi-finals and the championship game with each side's
    /// leading scorer, and once it's won, the champions' run as highlight cards with the Most Outstanding Player.
    /// </summary>
    private void FillFinalFour(CollegeWorld c, List<BracketBox> boxes)
    {
        var s = c.Season;
        int y = s.Year + 1;
        int? me = Session.Employed && Session.InCollege ? Session.HumanTeam.Id : null;
        var games = s.Games.Where(g => g.Event == CollegeWorld.Madness).ToList();
        var losers = games.Where(g => g.Played).Select(g => g.WinnerId == g.HomeId ? g.AwayId : g.HomeId).ToHashSet();
        string School(int id) => c.Program(id)?.City ?? "?";
        string Rec(int id) { var (w, l) = c.Record(id); return $"{w}-{l}"; }
        int Seed(int id) => s.Seeds.GetValueOrDefault(id);
        var dates = s.RoundDates.TryGetValue(CollegeWorld.Madness, out var d) ? d : new List<DateOnly>();
        string host = MadnessBracket.FinalFourHost(y);
        WeekendTitle = $"The {y} Final Four · {host}";
        WeekendSub = dates.Count >= 7
            ? $"A neutral court. National semi-finals {dates[5]:dddd, MMMM d} · championship game {dates[6]:dddd, MMMM d}."
            : "A neutral court.";

        // The four regional champions (or who's left in each region).
        var teams = new List<FinalFourTeam>();
        for (int r = 0; r < 4; r++)
        {
            string region = MadnessBracket.Regions[r];
            var e8 = boxes.FirstOrDefault(b => b.Round == 4 && b.Slot == r);
            if (e8?.Winner is not { } w)
            {
                var alive = MadnessBracket.RegionField(s, r).SelectMany(p => p.Top.Teams.Concat(p.Bottom.Teams))
                    .Where(id => !losers.Contains(id)).OrderBy(Seed).ToList();
                string still = alive.Count == 0 ? "" : $"Still alive ({alive.Count}): " + string.Join(", ", alive.Take(4).Select(id => $"{Seed(id)} {School(id)}")) + (alive.Count > 4 ? " …" : "");
                string when = dates.Count > 4 ? $"Regional final {dates[4]:MMM d}." : "";
                teams.Add(new(region.ToUpperInvariant(), "", "To be decided", when, still, "", me is { } m0 && alive.Contains(m0), false));
                continue;
            }
            var road = games.Where(g => g.Played && g.Round <= 4 && (g.HomeId == w || g.AwayId == w)).OrderBy(g => g.Round).ToList();
            double margin = road.Count == 0 ? 0 : road.Average(g => Math.Abs(g.HomePts!.Value - g.AwayPts!.Value));
            var last = road.LastOrDefault();
            string beat = last == null ? "" : $" Beat {Seed(Opp(last, w))} {School(Opp(last, w))} in the Elite Eight.";
            string routeLine = $"{road.Count} wins, by {margin:0.0} a game.{beat}";
            string tag = Seed(w) >= 10 ? "CINDERELLA" : Seed(w) == 1 ? "TOP SEED" : "";
            teams.Add(new(region.ToUpperInvariant(), $"No. {Seed(w)} seed", c.Program(w)?.FullName ?? "?", Rec(w), routeLine, tag, w == me, losers.Contains(w)));
        }
        WeekendTeams = teams;

        // The two semis and the final.
        WeekendSide Side(int? id, string fallback, int? score, bool won) => id is { } x
            ? new(Seed(x).ToString(), School(x), score?.ToString() ?? "", won, x == me)
            : new("", fallback, "", false, false);
        string StarsOf(CollegeGame? g) => g == null ? "" : string.Join("  ·  ", new[] { (g.HomeStar, g.HomeId), (g.AwayStar, g.AwayId) }
            .Where(x => x.Item1 != null).Select(x => $"{x.Item1!.Line} ({School(x.Item2)})"));
        WeekendGame Game(BracketBox? b, string title, string fa, string fb)
        {
            var g = b == null ? null : games.FirstOrDefault(x => x.Round == b.Round && x.Slot == b.Slot);
            string when = b is { Played: true } ? "Final" + (g?.Overtimes switch { null or 0 => "", 1 => " (OT)", var n => $" ({n}OT)" })
                : b?.Date is { } bd ? bd.ToString("ddd, MMM d") : "";
            bool mine = me is { } mm && (b?.TeamA == mm || b?.TeamB == mm);
            return new(title, when, Side(b?.TeamA, fa, b?.ScoreA, b?.Played == true && b.Winner == b.TeamA),
                Side(b?.TeamB, fb, b?.ScoreB, b?.Played == true && b.Winner == b.TeamB), b?.Played == true ? StarsOf(g) : "", mine);
        }
        var R = MadnessBracket.Regions;
        WeekendGames = new()
        {
            Game(boxes.FirstOrDefault(b => b.Round == 5 && b.Slot == 0), $"National semi-final · {R[0]} v {R[1]}", $"{R[0]} champion", $"{R[1]} champion"),
            Game(boxes.FirstOrDefault(b => b.Round == 5 && b.Slot == 1), $"National semi-final · {R[2]} v {R[3]}", $"{R[2]} champion", $"{R[3]} champion"),
            Game(boxes.FirstOrDefault(b => b.Round == 6), "Championship game", "Semi-final winner", "Semi-final winner"),
        };

        // The champions' run: a highlight card for every game they won, and the weekend's Most Outstanding Player.
        MontageTitle = MontageSub = "";
        if (s.ChampionId is not { } champ) return;
        var run = games.Where(g => g.Played && (g.HomeId == champ || g.AwayId == champ)).OrderBy(g => g.Round).ToList();
        var cards = new List<MontageCard>();
        foreach (var g in run)
        {
            bool home = g.HomeId == champ;
            int ours = home ? g.HomePts!.Value : g.AwayPts!.Value, theirs = home ? g.AwayPts!.Value : g.HomePts!.Value, opp = Opp(g, champ);
            int m = ours - theirs;
            string ot = g.Overtimes switch { 0 => "", 1 => " (OT)", var n => $" ({n}OT)" };
            string tag = g.Round == 6 ? "NATIONAL CHAMPIONS"
                : Seed(champ) - Seed(opp) >= 4 ? $"UPSET · a {Seed(champ)} over a {Seed(opp)}"
                : m <= 3 ? $"SURVIVED · by {m}"
                : g.Overtimes > 0 ? "IN OVERTIME"
                : m >= 20 ? $"ROUT · by {m}" : "";
            var star = home ? g.HomeStar : g.AwayStar;
            cards.Add(new(CollegeWorld.RoundName(g.Round).ToUpperInvariant(), $"W {ours}-{theirs}{ot}", $"v No. {Seed(opp)} {c.Program(opp)?.FullName}", star?.Line ?? "", tag));
        }
        Montage = cards;
        var team = c.Program(champ);
        int reg = c.RegionOf(champ);
        double avg = run.Count == 0 ? 0 : run.Average(g => Math.Abs(g.HomePts!.Value - g.AwayPts!.Value));
        MontageTitle = $"The champions' run: {team?.FullName}";
        var weekend = run.Where(g => g.Round >= 5).Select(g => g.HomeId == champ ? g.HomeStar : g.AwayStar).Where(x => x != null).ToList();
        var mop = weekend.GroupBy(x => x!.PlayerId).Select(gr => (gr.First()!.Name, Pts: gr.Sum(x => x!.Pts), N: gr.Count()))
            .OrderByDescending(x => x.Pts).FirstOrDefault();
        MontageSub = $"No. {Seed(champ)} seed{(reg >= 0 ? $" in the {R[reg]}" : "")} · {run.Count} wins, by {avg:0.0} a game"
            + (mop.Name != null ? $" · Most Outstanding Player: {mop.Name}, {mop.Pts} points in the Final Four" : "");
        WeekendSub = $"Champions: {team?.FullName}, in {host}." + (dates.Count >= 7 ? $" National semi-finals {dates[5]:MMMM d} · championship game {dates[6]:MMMM d}." : "");
    }

    private static int Opp(CollegeGame g, int id) => g.HomeId == id ? g.AwayId : g.HomeId;

    /// <summary>
    /// Selection Sunday as a reveal (C4 part 2): the regions one at a time in bracket order (1 v 16, 8 v 9 …), your seed
    /// once your region is out, and the bubble when all four are.
    /// </summary>
    private void FillSelection(CollegeWorld c)
    {
        FillShow(c);
        var s = c.Season;
        int shown = RevealedRegions;
        int? me = Session.Employed && Session.InCollege ? Session.HumanTeam.Id : null;
        var auto = new Dictionary<int, string>();
        foreach (var conf in c.Active)
            if (s.ConferenceChampions.TryGetValue(conf.Key, out var champ)) auto.TryAdd(champ, conf.Short);
        string School(int id) => c.Program(id)?.City ?? "?";
        string Rec(int id) { var (w, l) = c.Record(id); return $"{w}-{l}"; }
        RevealRow Row(SelectionLine line)
        {
            bool mine = me is { } m && line.Teams.Contains(m);
            if (line.Teams.Count == 0) return new(line.Seed.ToString(), "To be decided", "", "", false);
            if (line.FirstFour)
                return new(line.Seed.ToString(), string.Join(" / ", line.Teams.Select(School)), string.Join(" · ", line.Teams.Select(Rec)), "FIRST FOUR", mine);
            int id = line.Teams[0];
            string tag = auto.TryGetValue(id, out var cs) ? cs : s.LastFourIn.Contains(id) ? "LAST IN" : "";
            return new(line.Seed.ToString(), School(id), Rec(id), tag, mine);
        }
        int myRegion = me is { } mr ? c.RegionOf(mr) : -1;
        var regions = new List<RevealRegion>();
        for (int r = 0; r < 4; r++)
        {
            string name = MadnessBracket.Regions[r];
            if (r >= shown)
            {
                regions.Add(new(name, false, r == shown ? "Revealed next" : "To come", Array.Empty<RevealPair>(), false));
                continue;
            }
            var pairs = MadnessBracket.RegionField(s, r).Select(p => new RevealPair(Row(p.Top), Row(p.Bottom), Row(p.Top).Mine || Row(p.Bottom).Mine)).ToList();
            var top = MadnessBracket.RegionField(s, r).FirstOrDefault().Top;
            string line = top is { Teams.Count: > 0 } ? $"No. 1 seed: {c.Program(top.Teams[0])?.FullName}" : "";
            regions.Add(new(name, true, line, pairs, r == myRegion));
        }
        RevealRegions = regions;
        int autos = auto.Count;
        bool all = shown >= 4;
        SelectionIntro = all
            ? $"The field of 68: {autos} conference tournament champions and {68 - autos} at-large teams. Pick a region above for its bracket as the games are played."
            : $"The committee has picked the field of 68: {autos} conference tournament champions and {68 - autos} at-large teams. The four regions are revealed one at a time, in bracket order.";
        CanReveal = !all;
        RevealButton = all ? "All four regions revealed" : $"Reveal the {MadnessBracket.Regions[shown]}";
        BracketNote = "";
        // Chips: your seed (once your region is out), the regions so far, the No. 1 seeds; the bubble at the end.
        var chips = new List<Hardwood.Desktop.Controls.Chip>();
        if (me is { } mine)
        {
            if (myRegion >= 0 && myRegion < shown)
            {
                int seed = s.Seeds.GetValueOrDefault(mine);
                chips.Add(new("Our seed", $"No. {seed}", $"{MadnessBracket.Regions[myRegion]} · {FirstOpponent(c, mine)}", "good"));
            }
            else if (myRegion >= 0 || !all)
                chips.Add(new("Our seed", "?", all ? "" : myRegion >= 0 ? "keep revealing" : "still to come", "team"));
            else
                chips.Add(new("Our seed", "Left out", s.FirstFourOut.Contains(mine) ? "first four out" : s.NextFourOut.Contains(mine) ? "next four out" : "not near the bubble", "bad"));
        }
        chips.Add(new("Regions revealed", $"{shown} of 4", all ? "the field is set" : $"next: {MadnessBracket.Regions[shown]}", "team"));
        var ones = Enumerable.Range(0, shown).Select(r => MadnessBracket.RegionField(s, r).FirstOrDefault().Top).Where(t => t is { Teams.Count: > 0 }).Select(t => School(t!.Teams[0])).ToList();
        chips.Add(new("No. 1 seeds", ones.Count == 0 ? "?" : ones.Count.ToString(), string.Join(", ", ones.Take(2)) + (ones.Count > 2 ? " …" : ""), ones.Count > 0 ? "good" : ""));
        if (all && s.LastFourIn.Count > 0) chips.Add(new("Last team in", School(s.LastFourIn[^1]), Rec(s.LastFourIn[^1]), "warn"));
        if (all && s.FirstFourOut.Count > 0) chips.Add(new("First team out", School(s.FirstFourOut[0]), Rec(s.FirstFourOut[0]), ""));
        MadnessChips = chips;
        if (all && s.LastFourIn.Count > 0)
        {
            SeedCell Cell(int id) => new(School(id), Rec(id), "", id == me, c.Resume(id));
            SelectionBubble = new()
            {
                new("Last four in", s.LastFourIn.Select(Cell).ToList(), "good"),
                new("First four out", s.FirstFourOut.Select(Cell).ToList(), "warn"),
                new("Next four out", s.NextFourOut.Select(Cell).ToList(), "bad"),
            };
        }
    }

    /// <summary>Run 193: our NIT run: the seed, and the next game or how far we went.</summary>
    private static Hardwood.Desktop.Controls.Chip NitChip(CollegeWorld c, int me)
    {
        var games = c.Season.Games.Where(g => g.Event == CollegeWorld.Nit && (g.HomeId == me || g.AwayId == me)).OrderBy(g => g.Round).ToList();
        var last = games.LastOrDefault();
        string sub = last == null ? ""
            : c.Season.EventWinners.TryGetValue(CollegeWorld.Nit, out var w) && w == me ? "champions"
            : last.Played && last.WinnerId != me ? $"out in the {CollegeWorld.NitRoundName(last.Round).ToLowerInvariant()}"
            : !last.Played ? $"{CollegeWorld.NitRoundName(last.Round)}: {(last.HomeId == me ? "vs" : "at")} {c.Program(last.HomeId == me ? last.AwayId : last.HomeId)?.City}, {last.Date:MMM d}"
            : "through";
        return new("NIT", $"No. {c.NitSeedOf(me)} seed", sub, sub == "champions" ? "good" : sub.StartsWith("out") ? "bad" : "team");
    }

    /// <summary>"vs No. 13 Vermont, Thu Mar 19", or the First Four game, for the reveal's seed chip.</summary>
    private static string FirstOpponent(CollegeWorld c, int me)
    {
        var s = c.Season;
        var ff = s.Games.FirstOrDefault(g => g.Event == CollegeWorld.Madness && g.Round == 0 && (g.HomeId == me || g.AwayId == me));
        if (ff != null) return $"First Four vs {c.Program(ff.HomeId == me ? ff.AwayId : ff.HomeId)?.City}";
        int slot = s.Bracket.IndexOf(me);
        if (slot < 0) return "";
        int opp = s.Bracket[slot ^ 1];
        return opp > 0 ? $"vs No. {s.Seeds.GetValueOrDefault(opp)} {c.Program(opp)?.City}" : "vs a First Four winner";
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
        // Run 193: each bubble team's résumé, the NET and the Quad 1 record, as the committee reads it.
        SeedCell Bub(int id) => Cell(id) with { Detail = c.Resume(id) };
        Bubble = new()
        {
            new("Last four in", p.LastFourIn.Select(Bub).ToList(), "good"),
            new("First four out", p.FirstFourOut.Select(id => Bub(id) with { Tag = "" }).ToList(), "warn"),
            new("Next four out", p.NextFourOut.Select(id => Bub(id) with { Tag = "" }).ToList(), "bad"),
        };
        var chips = new List<Hardwood.Desktop.Controls.Chip>();
        if (me is { } m)
        {
            string status = p.StatusOf(m);
            string tone = !p.In(m) ? "bad" : p.LastFourIn.Contains(m) ? "warn" : "good";
            string value = p.Seeds.TryGetValue(m, out var sd) ? $"No. {sd}" : status == "Out" ? "Out" : "Bubble";
            chips.Add(new("Our projection", value, p.In(m) ? (p.LastFourIn.Contains(m) ? "last four in" : p.AutoBids.TryGetValue(m, out var cf) ? $"automatic bid (projected {cf} champions)" : "at-large") : status.ToLowerInvariant(), tone));
            // Run 193: the résumé the committee reads: the NET rank and the record by quadrant.
            var q = c.Quads(m);
            chips.Add(new("Our NET", $"No. {c.NetRank(m)}", $"Q1 {q.Of(1)} · Q2 {q.Of(2)} · Q3 {q.Of(3)} · Q4 {q.Of(4)}", q.BadLosses > 1 ? "warn" : "team"));
        }
        chips.Add(new("Automatic bids decided", $"{p.AutoBidsDecided} of {autos}", "conference tournaments", "team"));
        if (p.LastFourIn.Count > 0) chips.Add(new("Last team in", c.Program(p.LastFourIn[^1])?.City ?? "", Rec(p.LastFourIn[^1]), "warn"));
        if (p.FirstFourOut.Count > 0) chips.Add(new("First team out", c.Program(p.FirstFourOut[0])?.City ?? "", Rec(p.FirstFourOut[0]), ""));
        var ones = p.Field.Take(4).ToList();
        chips.Add(new("Top seed", c.Program(ones[0])?.City ?? "", Rec(ones[0]), "good"));
        BracketologyChips = chips;
    }

    /// <summary>CP3 (run 132): the alumni tracker, your program's graduates wherever they play now.</summary>
    private void FillAlumni()
    {
        Alumni.Clear();
        if (!IsCollegeCoach) { AlumniChips = new(); AlumniNote = ""; OnPropertyChanged(nameof(HasAlumni)); OnPropertyChanged(nameof(NoAlumni)); return; }
        var rec = Session.ProRecordOf(Session.HumanTeam);
        string top = Hardwood.Core.Names.Brand.TopShort;
        foreach (var l in rec.Lines) Alumni.Add(new AlumnusRow { Line = l, Age = l.Player.Age(Session.Today) });
        var best = rec.Lines.Where(l => !l.Retired).OrderByDescending(l => l.Player.CurrentAbility).FirstOrDefault();
        var c = Session.World.College!;
        AlumniChips = new()
        {
            new("Pros", rec.Pros.ToString(), rec.Abroad > 0 ? $"on a pro roster · {rec.Abroad} abroad" : "graduates on a pro roster", rec.Pros > 0 ? "good" : ""),
            new($"In the {top}", rec.TopLeague.ToString(), "the top league", rec.TopLeague > 0 ? "good" : ""),
            new("All-Stars", rec.AllStars.ToString(), "graduates who have been All-Stars", rec.AllStars > 0 ? "good" : ""),
            new("Best now", best == null ? "-" : best.Player.CurrentAbility.ToString(), best == null ? "nobody yet" : $"{best.Player.Name} · {best.Club}", "info"),
            new("Recruiting pitch", c.ProPitch > 0 ? $"+{c.ProPitch:0}" : "0", c.ProPitch > 0 ? "appeal with top recruits" : $"until one reaches the {top}", c.ProPitch > 0 ? "good" : ""),
        };
        int earlier = rec.Lines.Count(l => l.Class == 0);
        string realNote = earlier > 0 ? $" Rows marked Earlier are real NBA players who played here before this career began." : "";
        AlumniNote = rec.Lines.Count == 0
            ? $"Nobody has graduated from {Session.HumanTeam.TheFullName} in this career yet. Each summer your seniors turn pro; this tab follows them through the draft and their careers, and every one who reaches the {top} helps you recruit (up to +{CollegeWorld.MaxProPitch:0} appeal with 4- and 5-star prospects, half that with the rest)."
            : $"{rec.Summary(top)}. Every graduate in the {top} adds 1 to your appeal with 4- and 5-star recruits and each All-Star 2 more, up to +{CollegeWorld.MaxProPitch:0} (half for other recruits)." + realNote + " Pick a row and press Open player for his page.";
        OnPropertyChanged(nameof(HasAlumni)); OnPropertyChanged(nameof(NoAlumni));
    }

    private void FillProspects()
    {
        Prospects.Clear();
        if (Session.World.College is not { } c) return;
        var today = Session.Today;
        var team = Session.HumanTeam;
        // He turns pro the summer he is 22 (D80 A); "next summer" is the coming July.
        var nextSummer = new DateOnly(today.Month >= 7 ? today.Year + 1 : today.Year, 7, 1);
        int YearsLeft(Player p) => Math.Max(1, CollegeWorld.GraduateAge - p.Age(nextSummer) + 1);
        string Where(Player p, Team? t) => t != null ? t.FullName
            : c.RecruitOf(p) is { } r ? $"HS: {r.HighSchool}, {r.State}" + (r.CommittedTo is { } to ? $" → {c.Program(to)!.Abbreviation}" : "") : "";
        var everyone = c.Programs.SelectMany(t => t.Roster.Select(p => (p, t: (Team?)t))).Concat(c.Prospects.Select(p => (p, t: (Team?)null)));
        var rows = everyone
            .Where(x => Position == "All" || x.p.PrimaryPosition.ToString() == Position)
            .Select(x => (x.p, x.t, age: x.p.Age(today), left: YearsLeft(x.p)))
            .Where(x => Age switch { "Turning pro next summer" => x.left <= 1, "College (18-22)" => x.t != null && x.age >= 18, "High school (14-17)" => x.t == null || x.age <= 17, _ => true })
            // CP3 (run 132): ordered as your scouts see them (the middle of each potential range), not by the hidden number.
            .Select(x => (x.p, x.t, x.age, x.left, pa: Scouting.CollegePotential(x.p, Scouting.CollegeKnowledge(team, x.p,
                x.t != null ? (c.ClassYearOf(x.p, today)?.Year ?? 1) - 1 : 0, x.t == null && c.RecruitOf(x.p) is { Scouted: true } r && r.CourtedBy == team.Id))))
            .OrderByDescending(x => x.pa.Mid * 0.6 + x.p.CurrentAbility * 0.4).Take(200);
        foreach (var (p, t, age, left, pa) in rows)
        {
            c.Season.Stats.TryGetValue(p.Id, out var st);
            Prospects.Add(new ProspectListRow
            {
                Player = p, Age = age, Program = Where(p, t), Knowledge = Scouting.Knowledge(team, p, prospect: true), YearsLeft = left, PaRange = pa,
                Line = st is { G: > 0 } ? $"{st.Ppg:F1} pts · {st.Rpg:F1} reb · {st.Apg:F1} ast" : t == null ? "high school" : "-",
            });
        }
        // H163 (run 155): after the season the "Turning pro next summer" list was an empty table with a scrollbar under nothing.
        ProspectsEmpty = Prospects.Count > 0 ? ""
            : Age == "Turning pro next summer" && Position == "All"
                ? "Nobody is left in this group: this year's seniors have finished their college careers and turn pro this summer. Next year's seniors are among the college players."
                : "No players match these filters. Try another position or age group.";
        OnPropertyChanged(nameof(HasProspects)); OnPropertyChanged(nameof(ProspectsOfferCollege));
    }

    [RelayCommand]
    private void OpenProspect(ProspectListRow? row)
    {
        if ((row ?? SelectedProspect) is { } r) Shell.ShowPlayer(r.Player);
    }
}
