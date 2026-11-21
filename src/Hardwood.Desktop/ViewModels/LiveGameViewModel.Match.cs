using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Presentation;

namespace Hardwood.Desktop.ViewModels;

/// <summary>The lines drawn on the floor: college (a 22' 1¾" arc), NBA or FIBA.</summary>
public enum CourtMarkings { College, Nba, Fiba }

/// <summary>
/// M1 of the 2D match engine (run 148, `claude/match-engine-2d-design.md`): the live game plays out in real time on a
/// full-screen court. The engine is stepped one possession ahead; that possession's plays are choreographed and played
/// over their real game-clock time (1×, 2×, 4×, 8× or Key moments). The score bar, the commentary bar and the drawers
/// follow what's on screen. Presentation only: the engine decides every result, and its random stream is never touched.
/// </summary>
public sealed partial class LiveGameViewModel
{
    private Choreography? _script;
    private double _scriptT;
    private bool _scriptEndsPeriod;
    private int _possIndex;
    private Dictionary<Player, Spot>? _spots;
    private PossessionStart _nextStart = PossessionStart.PeriodStart;
    private readonly Stopwatch _sw = new();
    private TimeSpan _lastFrame;

    /// <summary>Playback speeds: 1×, 2×, 4×, 8×, then Key moments (makes, blocks, steals and late, close possessions at
    /// 2×; the rest at 16×).</summary>
    public static readonly double[] Speeds = { 1, 2, 4, 8 };
    public const int KeySpeed = 4;
    public const double KeyWatch = 2, KeySkip = 16;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Speed1), nameof(Speed2), nameof(Speed4), nameof(Speed8), nameof(SpeedKey))]
    private int _speedIndex;
    public bool Speed1 => SpeedIndex == 0;
    public bool Speed2 => SpeedIndex == 1;
    public bool Speed4 => SpeedIndex == 2;
    public bool Speed8 => SpeedIndex == 3;
    public bool SpeedKey => SpeedIndex == KeySpeed;
    partial void OnSpeedIndexChanged(int value) => SpeedLabel = value == KeySpeed ? "Key moments" : $"{Speeds[value]:0}×";

    /// <summary>The moment on screen: ten players, the ball, the clock, the score and the commentary.</summary>
    [ObservableProperty] private ChoreoFrame? _matchFrame;
    [ObservableProperty] private string _commentary = "";
    [ObservableProperty] private bool _commentaryBig;
    [ObservableProperty] private int _barAwayScore;
    [ObservableProperty] private int _barHomeScore;
    [ObservableProperty] private string _barClock = "";
    /// <summary>The period on the score bar: the possession on screen's (review, run 148: it read the last break's name
    /// through the next period's first possession).</summary>
    [ObservableProperty] private string _barPeriod = "";
    /// <summary>The shirt numbers on the discs (the game keeps none, so they're made up once per game).</summary>
    public IReadOnlyDictionary<Player, int> Numbers { get; private set; } = new Dictionary<Player, int>();
    /// <summary>How the floor is dressed: the home side's colours and names, or the event's floor (every game, M1).</summary>
    public Hardwood.Core.College.ArenaDress FloorDress { get; private set; } = null!;
    public CourtMarkings Markings { get; private set; }
    public Avalonia.Media.Color HomeKit => Avalonia.Media.Color.Parse(HomeTeam.PrimaryColor);
    public Avalonia.Media.Color HomeKitTrim => Avalonia.Media.Color.Parse(HomeTeam.SecondaryColor);
    /// <summary>The away side wears white with its colour as trim (or its own colour if it's light), like the reference.</summary>
    public Avalonia.Media.Color AwayKit => Avalonia.Media.Color.Parse("#F2F4F8");
    public Avalonia.Media.Color AwayKitTrim => Distinct(AwayTeam, Avalonia.Media.Color.Parse("#F2F4F8"));
    /// <summary>The possession on screen, for tests and the drawers.</summary>
    public Choreography? Script => _script;
    public double ScriptTime => _scriptT;
    /// <summary>The drawer on the right (Box, Play-by-play, Tactics, Subs, Shots, Stats, Flow), or −1 when closed.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DrawerOpen), nameof(DrawerLog), nameof(DrawerSide), nameof(DrawerFlow), nameof(DrawerTitle))]
    private int _drawer = -1;
    public bool DrawerOpen => Drawer >= 0;
    public bool DrawerLog => Drawer is 0 or 1;
    public bool DrawerSide => Drawer is >= 2 and <= 5;
    public bool DrawerFlow => Drawer == 6;
    public static readonly string[] DrawerNames = { "Box score", "Play by play", "Tactics (live)", "Subs", "Shots", "Stats", "Game flow" };
    public string DrawerTitle => Drawer >= 0 && Drawer < DrawerNames.Length ? DrawerNames[Drawer] : "";
    /// <summary>The side tabs' page for the drawer (Lineup 0, Shots 1, Stats 2, Tactics 3).</summary>
    [ObservableProperty] private int _sideTab;
    /// <summary>The drawer tabs hide while a drawer or the break is up (they would sit under it).</summary>
    public bool ShowDrawerTabs => !DrawerOpen && !ShowBreak;
    partial void OnDrawerChanged(int value)
    {
        OnPropertyChanged(nameof(ShowDrawerTabs));
        // A drawer and the break card share the middle of the court: opening one puts the other away ("Break card" brings it back).
        if (value >= 0 && ShowBreak) ShowBreak = false;
        if (value == 0) ShowBox = true;
        else if (value == 1) ShowBox = false;
        SideTab = value switch { 2 => 3, 3 => 0, 4 => 1, 5 => 2, _ => SideTab };
        if (value == 6) FlowVersion++;
    }
    public bool HasStatus => !string.IsNullOrEmpty(Status);
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    partial void OnCommentaryBigChanged(bool value) => OnPropertyChanged(nameof(CommentaryWeight));
    public Avalonia.Media.FontWeight CommentaryWeight => CommentaryBig ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
    public Avalonia.Media.IBrush HomeTrimBrush => new Avalonia.Media.SolidColorBrush(HomeKitTrim);
    /// <summary>The commentary bar wears the home colour, darkened until white text reads on it.</summary>
    public Avalonia.Media.IBrush CommentaryBrush => new Avalonia.Media.SolidColorBrush(Darken(HomeKit));
    public static Avalonia.Media.Color Darken(Avalonia.Media.Color c)
    {
        double lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        double k = lum <= 0.32 ? 1 : 0.32 / lum;
        return Avalonia.Media.Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
    }
    [RelayCommand] private void OpenDrawer(string tab) { int i = int.Parse(tab); Drawer = Drawer == i ? -1 : i; }
    [RelayCommand] private void CloseDrawer() => Drawer = -1;
    public string OffenceTagHome => (MatchFrame?.HomeOffense ?? true) ? $"OFF: {Systems.Name(HomeTeam.Tactic.System).ToUpperInvariant()}" : $"DEF: {Systems.Name(HomeTeam.Tactic.Scheme).ToUpperInvariant()}";
    public string OffenceTagAway => (MatchFrame?.HomeOffense ?? true) ? $"DEF: {Systems.Name(AwayTeam.Tactic.Scheme).ToUpperInvariant()}" : $"OFF: {Systems.Name(AwayTeam.Tactic.System).ToUpperInvariant()}";

    private void InitMatch()
    {
        var numbers = new Dictionary<Player, int>();
        foreach (var t in new[] { HomeTeam, AwayTeam }) foreach (var kv in ShirtNumbers.For(t)) numbers[kv.Key] = kv.Value;
        Numbers = numbers;
        var game = Session.InCollege ? Session.World.College?.GameOn(Us.Id, Session.Today) : null;
        FloorDress = Hardwood.Core.College.ArenaDress.For(HomeTeam, game);
        Markings = _live.Result.Periods == 2 ? CourtMarkings.College : _live.MinutesPerPeriod == 10 ? CourtMarkings.Fiba : CourtMarkings.Nba;
        BarAwayScore = _live.AwayScore; BarHomeScore = _live.HomeScore;
        BarClock = BugClock; BarPeriod = BugPeriod;
        Commentary = $"Welcome to {HomeTeam.City}: {AwayTeam.Name} at {HomeTeam.Name}. Press Play for the tip.";
        MatchFrame = TipFrame();
    }

    /// <summary>The ten at the centre circle before the tip.</summary>
    private ChoreoFrame TipFrame()
    {
        var c = PossessionChoreographer.Build(Input(Array.Empty<PlayEvent>(), _live.MinutesPerPeriod * 60.0, _live.MinutesPerPeriod * 60.0, true));
        return c.Sample(0);
    }

    private string Short(Team t) => Session.InCollege ? t.City : t.Name;

    private PossessionInput Input(IReadOnlyList<PlayEvent> events, double start, double end, bool homeBall)
    {
        var r = _live.Result;
        bool late = _live.Period >= r.Periods && end < 120 && Math.Abs(_live.HomeScore - _live.AwayScore) <= 6;
        return new PossessionInput(_possIndex, r.Seed, events, start, end, _live.HomeOnCourt.ToList(), _live.AwayOnCourt.ToList(), _spots,
            BarHomeScore, BarAwayScore, homeBall, Short(HomeTeam), Short(AwayTeam), HomeTeam.Tactic.Scheme, AwayTeam.Tactic.Scheme,
            _nextStart, r.Periods, late);
    }

    /// <summary>Step the engine one possession and choreograph it (the play then runs on screen).</summary>
    private void BeginPossession()
    {
        if (_live.Complete) { _script = null; return; }
        var r = _live.Result;
        int before = r.Plays.Count;
        double elapsed = r.Elapsed;
        int homeBefore = _live.HomeScore, awayBefore = _live.AwayScore;
        _live.Step();
        var events = r.Plays.Skip(before).ToList();
        double duration = Math.Max(0, r.Elapsed - elapsed);
        double end = events.Count > 0 ? events[^1].Clock : _live.Clock;
        double start = end + duration;
        _scriptEndsPeriod = events.Any(e => e.Kind == PlayKind.PeriodEnd);
        BarHomeScore = homeBefore; BarAwayScore = awayBefore;
        if (events.FirstOrDefault() is { } first && first.Period > 0) BarPeriod = BugPeriodName(first.Period, r.Periods);
        _script = PossessionChoreographer.Build(Input(events, start, end, true));
        _scriptT = 0;
        _possIndex++;
        ShowFrame();
    }

    /// <summary>The possession on screen has played out: hand everything else the engine's state, then go on.</summary>
    private void FinishPossession()
    {
        if (_script == null) return;
        _spots = _script.FinalSpots();
        _nextStart = _script.Next;
        bool periodEnded = _scriptEndsPeriod;
        _script = null;
        Refresh();
        AfterStep(periodEnded);
    }

    private void ShowFrame()
    {
        if (_script == null) return;
        var f = _script.Sample(_scriptT);
        MatchFrame = f;
        if (f.HomeScore != BarHomeScore) BarHomeScore = f.HomeScore;
        if (f.AwayScore != BarAwayScore) BarAwayScore = f.AwayScore;
        string clock = $"{(int)(f.Clock / 60)}:{(int)(f.Clock % 60):00}";
        if (clock != BarClock) BarClock = clock;
        if (f.Line.Length > 0 && f.Line != Commentary) { Commentary = f.Line; CommentaryBig = f.LineBig; }
        OnPropertyChanged(nameof(OffenceTagHome)); OnPropertyChanged(nameof(OffenceTagAway));
    }

    /// <summary>The playback rate now: the chosen speed, or Key moments' watch or skip rate for this possession.</summary>
    public double Rate => SpeedIndex == KeySpeed ? (_script?.Key == true ? KeyWatch : KeySkip) : Speeds[Math.Clamp(SpeedIndex, 0, Speeds.Length - 1)];

    private void FrameTick()
    {
        var now = _sw.Elapsed;
        double dt = Math.Min(0.1, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;
        Play(dt);
    }

    /// <summary>Advance the picture by <paramref name="realSeconds"/> of real time at the current speed (the frame
    /// timer calls this; tests call it directly).</summary>
    public void Play(double realSeconds)
    {
        if (Complete) return;
        if (_script == null)
        {
            BeginPossession();
            if (_script == null) return;
        }
        _scriptT += realSeconds * Rate;
        if (_scriptT >= _script.Length)
        {
            _scriptT = _script.Length;
            ShowFrame();
            FinishPossession();
            return;
        }
        ShowFrame();
    }

    /// <summary>Play the rest of the game on screen at the given rate, frame by frame (tests: proves the viewer changes
    /// no result). Stops at breaks only if the game is set to.</summary>
    public int PlayThrough(double frame = 1.0 / 60, int maxFrames = 5_000_000)
    {
        int frames = 0;
        bool pause = PauseAtBreaks;
        PauseAtBreaks = false;
        while (!Complete && frames < maxFrames) { Play(frame); frames++; }
        PauseAtBreaks = pause;
        return frames;
    }

    [RelayCommand]
    private void SetSpeed(string which)
    {
        int i = which == "key" ? KeySpeed : double.TryParse(which, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? Array.IndexOf(Speeds, x) : -1;
        SpeedIndex = Math.Clamp(i, 0, KeySpeed);
    }

    [RelayCommand] private void SpeedUp() => SpeedIndex = Math.Min(KeySpeed, SpeedIndex + 1);
    [RelayCommand] private void SpeedDown() => SpeedIndex = Math.Max(0, SpeedIndex - 1);
}
