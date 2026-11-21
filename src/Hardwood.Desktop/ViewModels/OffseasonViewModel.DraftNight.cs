using System.Runtime.CompilerServices;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

/// <summary>An envelope on the lottery's big screen: sealed until its pick is revealed, then the club's crest.</summary>
public sealed record EnvelopeRow(int Pick, Team? Team, bool Open, bool Now, bool Mine)
{
    public string PickText => Pick.ToString();
    public bool Sealed => !Open;
}

/// <summary>A row of the side board: a club and its odds (or its pick, once revealed), or a pick made.</summary>
public sealed record NightBoardRow(string Left, string Right, bool Lit, bool Mine);

/// <summary>A representative's crest card on the stage (picks 1–4): empty until that pick is revealed.</summary>
public sealed record RepCard(int Pick, Team? Team)
{
    public bool HasTeam => Team != null;
    public string Label => Team == null ? "?" : $"#{Pick}";
}

/// <summary>
/// Draft night (B3, run 142) on the Offseason page: the lottery show, then the draft broadcast, on the HSN stage. The
/// beats come from <see cref="DraftNight"/>; this keeps the place in each show per summer while the game is open and
/// drives Next / Autorun / Skip to the end. It never changes the draft: the order and the picks are the summer's own.
/// </summary>
public sealed partial class OffseasonViewModel
{
    public const int DraftNightTab = 5;
    private static readonly ConditionalWeakTable<Offseason, StrongBox<int>> LotteryAt = new(), DraftAt = new();
    private List<DraftBeat> _lottery = new(), _draftBeats = new();
    private Offseason? _nightFor;
    private DispatcherTimer? _nightTimer;

    [ObservableProperty] private bool _showDraftNight;
    /// <summary>True for the draft broadcast, false for the lottery show.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NightOnLottery), nameof(NextText))] private bool _nightOnDraft;
    [ObservableProperty] private bool _canWatchDraft;
    public bool NightOnLottery => !NightOnDraft;
    [ObservableProperty] private string _nightTitle = "";
    [ObservableProperty] private string _nightSubtitle = "";
    [ObservableProperty] private bool _nightShowsTitle;
    [ObservableProperty] private bool _nightShowsEnvelopes;
    [ObservableProperty] private bool _nightShowsCard;
    [ObservableProperty] private List<EnvelopeRow> _envelopes = new();
    [ObservableProperty] private int _envelopeColumns = 6;
    [ObservableProperty] private string _boardTitle = "";
    [ObservableProperty] private List<NightBoardRow> _nightBoard = new();
    [ObservableProperty] private int _boardColumns = 1;
    [ObservableProperty] private List<RepCard> _repCards = new();
    // The pick card.
    [ObservableProperty] private string _cardPick = "";
    [ObservableProperty] private Team? _cardTeam;
    [ObservableProperty] private Player? _cardPlayer;
    [ObservableProperty] private string _cardAnnounce = "";
    [ObservableProperty] private string _cardName = "";
    [ObservableProperty] private string _cardLine = "";
    [ObservableProperty] private string _cardRange = "";
    [ObservableProperty] private bool _cardHasPlayer;
    [ObservableProperty] private bool _cardMine;
    // The speech bubble.
    [ObservableProperty] private string _nightSpeaker = "";
    [ObservableProperty] private string _nightLine = "";
    /// <summary>True when the line is the podium's (a bubble by the official), false for the HSN desk (a lower third).</summary>
    [ObservableProperty] private bool _nightAtPodium = true;
    [ObservableProperty] private string _nightProgress = "";
    [ObservableProperty] private bool _canNightNext;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(NightAutorunText))] private bool _nightAutorunning;
    public string NightAutorunText => NightAutorunning ? "Stop" : "Autorun";
    public string NextText => NightOnDraft ? "Next pick" : "Open the next envelope";
    public bool NightAtDesk => !NightAtPodium;
    partial void OnNightAtPodiumChanged(bool value) => OnPropertyChanged(nameof(NightAtDesk));

    /// <summary>The Draft night tab takes the page's full width; the side panels step aside.</summary>
    public int TabSpan => Tab == DraftNightTab ? 2 : 1;
    public bool ShowCollegePanel => CollegeSummer && Tab != DraftNightTab;
    partial void OnTabChanged(int value)
    {
        OnPropertyChanged(nameof(TabSpan)); OnPropertyChanged(nameof(ShowCollegePanel));
        if (value != DraftNightTab) StopNightAutorun();
    }
    partial void OnCollegeSummerChanged(bool value) => OnPropertyChanged(nameof(ShowCollegePanel));

    /// <summary>This summer's graduates of the coached program (college careers only).</summary>
    private HashSet<int>? MyGraduates(GameSession s, Offseason o) =>
        s.InCollege && s.World.College is { } c
            ? c.Alumni.Where(kv => kv.Value.ProgramId == s.HumanTeam.Id && kv.Value.Year == o.Season + 1).Select(kv => kv.Key).ToHashSet()
            : null;

    private string SchoolOf(GameSession s, Player p) =>
        s.World.College is { } c && c.Alumni.TryGetValue(p.Id, out var a) && c.Program(a.ProgramId) is { } prog ? prog.City : "college";

    private Scouting.Range RangeOf(GameSession s, Team viewer, Player p) =>
        s.InCollege ? s.CollegePotentialOf(p) ?? Scouting.CollegePotential(p, Scouting.Knowledge(viewer, p)) : Scouting.Potential(p, Scouting.Knowledge(viewer, p));

    /// <summary>Builds both shows for this summer and shows the current beat. Called from <see cref="Refresh"/>.</summary>
    private void FillDraftNight(GameSession s, Offseason o, Team team)
    {
        var grads = MyGraduates(s, o);
        int? mine = s.InCollege ? null : team.Id;
        _lottery = DraftNight.Lottery(o, mine, grads);
        _draftBeats = DraftNight.Draft(o, mine, p => SchoolOf(s, p), p => RangeOf(s, team, p), grads);
        bool fresh = !ReferenceEquals(_nightFor, o), had = CanWatchDraft && !fresh;
        _nightFor = o;
        ShowDraftNight = _lottery.Count > 0;
        CanWatchDraft = _draftBeats.Count > 0;
        // The draft broadcast as soon as there is one (its picks are made); the lottery before.
        if (fresh || !CanWatchDraft || !had) NightOnDraft = CanWatchDraft;
        ShowNightBeat();
    }

    private string Abbr(int id, string name) =>
        (_nightFor?.PickOwners.FirstOrDefault(t => t.Id == id) ?? _nightFor?.DraftOrder.FirstOrDefault(t => t.Id == id))?.Abbreviation ?? name;

    private StrongBox<int> Pos(Offseason o, bool draft) => (draft ? DraftAt : LotteryAt).GetValue(o, _ => new StrongBox<int>(1));

    private void ShowNightBeat()
    {
        var o = _nightFor;
        var beats = NightOnDraft ? _draftBeats : _lottery;
        if (o == null || beats.Count == 0) { CanNightNext = false; return; }
        var box = Pos(o, NightOnDraft);
        // A draft still going gains picks between visits; the position stays where the viewer left it.
        int at = Math.Clamp(box.Value, 1, beats.Count);
        var beat = beats[at - 1];
        NightProgress = $"{at} of {beats.Count}";
        CanNightNext = at < beats.Count;
        NightSpeaker = $"{beat.Speaker.Name} · {beat.Speaker.Role.ToLowerInvariant()}" + (beat.Speaker == DraftNight.Official ? "" : " · HSN");
        NightLine = beat.Line;
        NightAtPodium = beat.Speaker == DraftNight.Official;
        if (!NightOnDraft)
        {
            NightShowsTitle = beat.Kind == DraftNight.LotteryOpen;
            NightShowsEnvelopes = !NightShowsTitle;
            NightShowsCard = false;
            NightTitle = beat.Announce;
            NightSubtitle = $"{DraftNight.Odds(o)} for every club in the draw · one round";
            var shown = beats.Take(at).Where(b => b.Kind == DraftNight.Envelope).ToDictionary(b => b.Pick);
            int n = (o.DraftOrder.Count > 0 ? o.DraftOrder : o.PickOwners).Count;
            Envelopes = Enumerable.Range(1, n).Select(k => shown.TryGetValue(k, out var b)
                ? new EnvelopeRow(k, b.Team, true, b == beat, b.MyClub) : new EnvelopeRow(k, null, false, false, false)).ToList();
            EnvelopeColumns = n <= 12 ? 6 : n <= 21 ? 7 : 8;
            BoardTitle = "THE ODDS";
            // Up to 33 clubs: two columns of short names.
            var clubs = DraftNight.Board(o, beats, at, Session.InCollege ? null : Session.HumanTeam.Id);
            BoardColumns = clubs.Count > 16 ? 2 : 1;
            NightBoard = clubs.Select(c => new NightBoardRow(BoardColumns == 2 ? Abbr(c.TeamId, c.Name) : c.Name,
                c.Pick is { } k ? $"#{k}" : BoardColumns == 2 ? c.Odds.Replace("1 in ", "1/").Replace(" + last", "+") : c.Odds, c.Pick != null, c.Mine)).ToList();
            RepCards = Enumerable.Range(1, DraftNight.OnStage).Select(k => new RepCard(k, shown.TryGetValue(k, out var b) ? b.Team : null)).ToList();
        }
        else
        {
            NightShowsTitle = beat.Kind is DraftNight.DraftOpen or DraftNight.DraftClose;
            NightShowsCard = !NightShowsTitle;
            NightShowsEnvelopes = false;
            NightTitle = beat.Announce;
            NightSubtitle = beat.Kind == DraftNight.DraftOpen ? $"One round · {o.PickOwners.Count} picks" : $"{beats.Count(b => b.IsPick)} picks made";
            CardPick = beat.Pick > 0 ? $"PICK {beat.Pick}" : "";
            CardTeam = beat.Team;
            CardPlayer = beat.Player;
            CardHasPlayer = beat.Player != null;
            CardAnnounce = beat.Kind == DraftNight.Clock ? $"{beat.Team?.FullName} are on the clock" : beat.Announce;
            CardName = beat.Player?.Name ?? "";
            CardLine = beat.Player is { } p ? $"{p.PrimaryPosition} · {beat.School}" : "";
            CardRange = beat.Player == null ? "" : Session.InCollege ? $"Potential {beat.Range} (your staff's read)" : $"Potential {beat.Range}";
            CardMine = beat.MyGraduate || beat.MyClub;
            BoardTitle = "THE PICKS";
            BoardColumns = 1;
            NightBoard = beats.Take(at).Where(b => b.IsPick).TakeLast(14)
                .Select(b => new NightBoardRow($"{b.Pick}. {b.Team!.Abbreviation}", b.Player!.Name, b == beat, b.MyGraduate || b.MyClub)).ToList();
            var order = o.PickOwners;
            RepCards = Enumerable.Range(1, Math.Min(DraftNight.OnStage, order.Count)).Select(k => new RepCard(k, order[k - 1])).ToList();
        }
    }

    [RelayCommand]
    private void NightNext()
    {
        if (_nightFor is not { } o) return;
        var beats = NightOnDraft ? _draftBeats : _lottery;
        var box = Pos(o, NightOnDraft);
        if (box.Value >= beats.Count) { StopNightAutorun(); return; }
        box.Value++;
        ShowNightBeat();
        if (box.Value >= beats.Count) StopNightAutorun();
    }

    /// <summary>Skip to the end: the whole order (or every pick) at once.</summary>
    [RelayCommand]
    private void NightSkip()
    {
        StopNightAutorun();
        if (_nightFor is not { } o) return;
        Pos(o, NightOnDraft).Value = (NightOnDraft ? _draftBeats : _lottery).Count;
        ShowNightBeat();
    }

    [RelayCommand]
    private void NightLotteryShow() { StopNightAutorun(); NightOnDraft = false; ShowNightBeat(); }

    [RelayCommand]
    private void NightDraftShow() { if (!CanWatchDraft) return; StopNightAutorun(); NightOnDraft = true; ShowNightBeat(); }

    [RelayCommand]
    private void NightAutorun()
    {
        if (NightAutorunning) { StopNightAutorun(); return; }
        if (!CanNightNext) return;
        _nightTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
        _nightTimer.Tick -= NightTick;
        _nightTimer.Tick += NightTick;
        _nightTimer.Start();
        NightAutorunning = true;
    }

    private void NightTick(object? sender, EventArgs e)
    {
        // As the Selection Show (run 139's review): never tick off this page, while the game is busy, after a load, or
        // once the summer has moved on.
        if (!CanNightNext || Shell.IsBusy || !ReferenceEquals(Shell.CurrentPage, this) || Shell.Session == null
            || !ReferenceEquals(Session.Offseason, _nightFor) || Tab != DraftNightTab) { StopNightAutorun(); return; }
        NightNext();
    }

    private void StopNightAutorun()
    {
        _nightTimer?.Stop();
        NightAutorunning = false;
    }
}

/// <summary>Draft night's brushes (static: the stage redraws on every beat).</summary>
public static class DraftNightConverters
{
    private static readonly Avalonia.Media.IBrush Sealed = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#3B2F7A"));
    private static readonly Avalonia.Media.IBrush Opened = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#F4F6FA"));
    private static readonly Avalonia.Media.IBrush Gold = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#F0A33A"));
    private static readonly Avalonia.Media.IBrush Quiet = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#2A3344"));
    private static readonly Avalonia.Media.IBrush Dark = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#101318"));
    private static readonly Avalonia.Media.IBrush Lilac = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#C9B8FF"));
    private static readonly Avalonia.Media.IBrush Soft = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(Avalonia.Media.Color.Parse("#C9D1D9"));

    public static readonly Avalonia.Data.Converters.IValueConverter EnvelopeFill = new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(open => open ? Opened : Sealed);
    public static readonly Avalonia.Data.Converters.IValueConverter PickInk = new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(open => open ? Dark : Lilac);
    public static readonly Avalonia.Data.Converters.IValueConverter NowEdge = new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(now => now ? Gold : Quiet);
    public static readonly Avalonia.Data.Converters.IValueConverter MineEdge = new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(mine => mine ? Gold : Quiet);
    public static readonly Avalonia.Data.Converters.IValueConverter MineInk = new Avalonia.Data.Converters.FuncValueConverter<bool, Avalonia.Media.IBrush>(mine => mine ? Gold : Soft);
    /// <summary>The side board: revealed clubs and the pick on screen are lit; the rest sit back.</summary>
    public static readonly Avalonia.Data.Converters.IValueConverter LitFade = new Avalonia.Data.Converters.FuncValueConverter<bool, double>(lit => lit ? 1.0 : 0.62);
}
