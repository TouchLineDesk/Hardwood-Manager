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
    /// <summary>K25: the result call and its context, for the play-by-play log once the last possession has played out.</summary>
    private List<string> _finalCalls = new();
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
    partial void OnSpeedIndexChanged(int value) { SpeedLabel = value == KeySpeed ? "Key moments" : $"{Speeds[value]:0}×"; UpdateBed(); }

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
    /// <summary>The drawer on the right (Box, Play-by-play, Tactics, Subs, Shots, Stats, Flow, On the floor), or −1 when closed.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DrawerOpen), nameof(DrawerLog), nameof(DrawerSide), nameof(DrawerFlow), nameof(DrawerFloor), nameof(DrawerTitle))]
    private int _drawer = -1;
    public bool DrawerOpen => Drawer >= 0;
    public bool DrawerLog => Drawer is 0 or 1;
    public bool DrawerSide => Drawer is >= 2 and <= 5;
    public bool DrawerFlow => Drawer == 6;
    public bool DrawerFloor => Drawer == 7;
    public static readonly string[] DrawerNames = { "Box score", "Play by play", "Tactics (live)", "Subs", "Shots", "Stats", "Game flow", "On the floor" };
    public string DrawerTitle => Drawer >= 0 && Drawer < DrawerNames.Length ? DrawerNames[Drawer] : "";
    /// <summary>The side tabs' page for the drawer (Lineup 0, Shots 1, Stats 2, Tactics 3).</summary>
    [ObservableProperty] private int _sideTab;
    /// <summary>The drawer tabs hide while the break is up. K23 (run 158): they stay beside an open drawer, so every other
    /// drawer is one press away (the open one is lit; pressing it again closes it).</summary>
    public bool ShowDrawerTabs => !ShowBreak;
    public bool Tab0 => Drawer == 0; public bool Tab1 => Drawer == 1; public bool Tab2 => Drawer == 2; public bool Tab3 => Drawer == 3;
    public bool Tab4 => Drawer == 4; public bool Tab5 => Drawer == 5; public bool Tab6 => Drawer == 6; public bool Tab7 => Drawer == 7;
    private static readonly string[] TabNames = { nameof(Tab0), nameof(Tab1), nameof(Tab2), nameof(Tab3), nameof(Tab4), nameof(Tab5), nameof(Tab6), nameof(Tab7) };
    partial void OnDrawerChanged(int value)
    {
        OnPropertyChanged(nameof(ShowDrawerTabs));
        foreach (var n in TabNames) OnPropertyChanged(n);
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
    public string OffenceTagHome => (MatchFrame?.HomeOffense ?? true) ? $"OFF: {Systems.Name(Playing(HomeTeam, _clutchHome).System).ToUpperInvariant()}" : $"DEF: {Systems.Name(Playing(HomeTeam, _clutchHome).Scheme).ToUpperInvariant()}";
    public string OffenceTagAway => (MatchFrame?.HomeOffense ?? true) ? $"DEF: {Systems.Name(Playing(AwayTeam, _clutchAway).Scheme).ToUpperInvariant()}" : $"OFF: {Systems.Name(Playing(AwayTeam, _clutchAway).System).ToUpperInvariant()}";
    /// <summary>M5: the system and scheme in play: the clutch plan's while it's on, else the tactic's.</summary>
    private static (OffensiveSystem System, DefensiveScheme Scheme) Playing(Team t, bool clutch) =>
        clutch && t.Tactic.Presets.FirstOrDefault(p => p.Name == t.Tactic.LateGamePreset) is { } pre ? (pre.System, pre.Scheme) : (t.Tactic.System, t.Tactic.Scheme);

    private void InitMatch()
    {
        var numbers = new Dictionary<Player, int>();
        foreach (var t in new[] { HomeTeam, AwayTeam }) foreach (var kv in ShirtNumbers.For(t)) numbers[kv.Key] = kv.Value;
        Numbers = numbers;
        var game = CollegeGameNow ? Session.World.College?.GameOn(Us.Id, Session.Today) : null;
        FloorDress = DressFor(Session, HomeTeam, AwayTeam, game);
        Markings = _live.Result.Periods == 2 ? CourtMarkings.College : _live.MinutesPerPeriod == 10 ? CourtMarkings.Fiba : CourtMarkings.Nba;
        BarAwayScore = _live.AwayScore; BarHomeScore = _live.HomeScore;
        BarClock = BugClock; BarPeriod = BugPeriod;
        Commentary = $"Welcome to {HomeTeam.City}: {Nick(AwayTeam)} at {Nick(HomeTeam)}. Press Play for the tip.";
        MatchFrame = TipFrame();
        InitShouts();
        // Fight songs A (review): start the home band's song rendering now, so it's ready by the tip.
        if (BandFor() is { } band) System.Threading.Tasks.Task.Run(() => { try { Hardwood.Desktop.Audio.FightSongLibrary.For(band.School, band.Spec); } catch (Exception) { } });
    }

    /// <summary>M4 (run 151): the floor for this game: the college program's or the event's, or the pro club's with the
    /// playoffs', the Finals' or a continental cup's dressing.</summary>
    public static Hardwood.Core.College.ArenaDress DressFor(Hardwood.Core.Season.GameSession s, Team home, Team away, Hardwood.Core.College.CollegeGame? game)
    {
        // I6b (run 215): your nation's qualifier: the FIBA event floor, both nations' fans.
        if (s.LiveNational is { } nat)
            return Hardwood.Core.College.ArenaDress.ForPro(home, away, "FIBA", cup: "FIBA Qualifiers")
                with { Sideline = $"{home.City.ToUpperInvariant()} · {nat.Group.ToUpperInvariant()}" };
        if (s.InCollege) return Hardwood.Core.College.ArenaDress.For(home, game, away, s.World.College);
        // Run 223: a cup knockout game: the cup's floor, both clubs' fans; Las Vegas for the semi-finals and final.
        if (s.LiveCup is { } tie)
            return Hardwood.Core.College.ArenaDress.ForPro(home, away, Hardwood.Core.Names.Brand.TopShort, cup: Hardwood.Core.Season.NbaCup.Name)
                with { Sideline = $"{(tie.Neutral ? Hardwood.Core.Season.NbaCup.Venue.ToUpperInvariant() : home.City.ToUpperInvariant())} · {tie.Round.ToUpperInvariant()}" };
        if (s.LiveCompetition is { } comp)
            return comp.Kind == Hardwood.Core.World.CompetitionKind.Continental
                ? Hardwood.Core.College.ArenaDress.ForPro(home, away, comp.Short, cup: comp.Name)
                : Hardwood.Core.College.ArenaDress.ForPro(home, away, comp.Short);
        return Hardwood.Core.College.ArenaDress.ForPro(home, away, Hardwood.Core.Names.Brand.TopShort, s.LiveSeries);
    }

    /// <summary>The ten at the centre circle before the tip.</summary>
    private ChoreoFrame TipFrame()
    {
        var c = PossessionChoreographer.Build(Input(Array.Empty<PlayEvent>(), _live.MinutesPerPeriod * 60.0, _live.MinutesPerPeriod * 60.0, true));
        return c.Sample(0);
    }

    /// <summary>The side's name as the commentary says it. K25 (run 174): a broadcast name ("Miami", not "Miami (FL)").</summary>
    private string Short(Team t) => CollegeGameNow
        ? GameCalls.BroadcastName(t.City, (t == HomeTeam ? AwayTeam : HomeTeam).City)
        : Nick(t);
    /// <summary>The side's nickname, or (a national team, run 215) its country.</summary>
    internal static string Nick(Team t) => t.Name.Length > 0 ? t.Name : t.City;

    /// <summary>K25 (run 174): every commentary line of the game so far, in order, and the late live-ball sub lines (for tests).</summary>
    public List<CommentaryLine> SaidForTest { get; } = new();
    public int LateLiveSubLinesForTest { get; private set; }

    /// <summary>K25: who had the ball as the last possession ended.</summary>
    private Player? _ballWith;

    /// <summary>K25 (run 174): what this game means, for the result call: the March round and seeds, the Top 25 ranks and
    /// the rivalry (college only).</summary>
    private GameCalls.Occasion? FinalOccasion(bool homeWon)
    {
        if (!CollegeGameNow || Session.World.College is not { } cw) return null;
        var g = cw.GameOn(Us.Id, Session.Today);
        if (g == null) return null;
        int wId = homeWon ? HomeTeam.Id : AwayTeam.Id, lId = homeWon ? AwayTeam.Id : HomeTeam.Id;
        bool march = g.Event == Hardwood.Core.College.CollegeWorld.Madness;
        string? stage = march ? Hardwood.Core.College.CollegeWorld.RoundName(g.Round) : null;
        string? next = march && g.Round < 6 ? Hardwood.Core.College.CollegeWorld.RoundName(g.Round + 1) : null;
        // Review (run 174): the game's stamped ranks are written when the day is recorded, after the live game, so read
        // the poll as it stands at the tip.
        int? Rank(int id) => (id == g.HomeId ? g.HomeRank : id == g.AwayId ? g.AwayRank : null) ?? cw.Rank(id);
        return new GameCalls.Occasion(stage, next, march ? cw.SeedOf(wId) : 0, march ? cw.SeedOf(lId) : 0,
            Rank(wId), Rank(lId), cw.RivalryOf(HomeTeam.Id, AwayTeam.Id)?.Title);
    }

    private PossessionInput Input(IReadOnlyList<PlayEvent> events, double start, double end, bool homeBall, IReadOnlyList<string>? notes = null, IReadOnlyList<string>? calls = null)
    {
        var r = _live.Result;
        bool late = _live.Period >= r.Periods && end < 120 && Math.Abs(_live.HomeScore - _live.AwayScore) <= 6;
        return new PossessionInput(_possIndex, r.Seed, events, start, end, _live.HomeOnCourt.ToList(), _live.AwayOnCourt.ToList(), _spots,
            BarHomeScore, BarAwayScore, homeBall, Short(HomeTeam), Short(AwayTeam), Playing(HomeTeam, _clutchHome).Scheme, Playing(AwayTeam, _clutchAway).Scheme,
            _nextStart, r.Periods, late)
        {
            // M2 (run 149): the sets by system and role, the defence's aggression, transition by pace, tired legs.
            HomeSystem = Playing(HomeTeam, _clutchHome).System, AwaySystem = Playing(AwayTeam, _clutchAway).System,   // M5: the clutch plan's set while it plays
            RoleOf = p => (HomeTeam.Roster.Contains(p) ? HomeTeam : AwayTeam).Tactic.RoleOf(p),
            EnergyOf = _live.EnergyOf,
            Dressed = HomeTeam.Roster.Concat(AwayTeam.Roster).ToList(),   // H215 (run 224)
            HomePace = HomeTeam.Tactic.Pace, AwayPace = AwayTeam.Tactic.Pace,
            HomeAggression = HomeTeam.Tactic.DefensiveAggression, AwayAggression = AwayTeam.Tactic.DefensiveAggression,
            HomePress = HomeTeam.Tactic.FullCourtPress, AwayPress = AwayTeam.Tactic.FullCourtPress,
            // M3 (run 150): the shouts' levers show in the shape, and the commentary names what changed.
            HomeThrees = HomeTeam.Tactic.ThreePointEmphasis, AwayThrees = AwayTeam.Tactic.ThreePointEmphasis,
            HomeGlass = HomeTeam.Tactic.CrashGlass, AwayGlass = AwayTeam.Tactic.CrashGlass,
            HomeDouble = HomeTeam.Tactic.DoubleStar, AwayDouble = AwayTeam.Tactic.DoubleStar,
            Notes = notes ?? Array.Empty<string>(),
            Calls = calls ?? Array.Empty<string>(),
            BallWith = _ballWith,
        };
    }

    /// <summary>Step the engine one possession and choreograph it (the play then runs on screen).</summary>
    private void BeginPossession()
    {
        if (_live.Complete) { _script = null; return; }
        // K32 (run 186): the game is under way, so the tip-off hint goes (however play began).
        if (Status?.StartsWith("Press Play to tip off", StringComparison.Ordinal) == true) Status = "";
        var r = _live.Result;
        int before = r.Plays.Count;
        double elapsed = r.Elapsed;
        int homeBefore = _live.HomeScore, awayBefore = _live.AwayScore;
        // M5 (run 152): the clutch plan, read as the engine reads it at the start of the possession (GameSimulator.SetTactics).
        var clutchNotes = ClutchNotes();
        int timeoutsBefore = r.Timeouts.Count, mediaBefore = r.MediaTimeouts.Count;
        _live.Step();
        var events = r.Plays.Skip(before).ToList();
        double duration = Math.Max(0, r.Elapsed - elapsed);
        double end = events.Count > 0 ? events[^1].Clock : _live.Clock;
        double start = end + duration;
        _scriptEndsPeriod = events.Any(e => e.Kind == PlayKind.PeriodEnd);
        BarHomeScore = homeBefore; BarAwayScore = awayBefore;
        if (events.FirstOrDefault() is { } first && first.Period > 0) BarPeriod = BugPeriodName(first.Period, r.Periods);
        // M3: the levers the engine just played with are the ones drawn, and any change since the last possession is said.
        // #1064 (run 168): a timeout comes first, at the dead ball before the possession.
        var notes = MediaNotes(mediaBefore);
        notes.AddRange(TimeoutNotes(timeoutsBefore));
        notes.AddRange(TakeChangeNotes());
        notes.AddRange(clutchNotes);
        // M5: the calls on the last possession are said as this one starts; this one's are kept for the next.
        var calls = _pendingCalls;
        _pendingCalls = _calls.For(events, r.Home, r.Away, homeBefore, awayBefore, Short(HomeTeam), Short(AwayTeam), r.Periods, _live.FoulsToFoulOut);
        _script = PossessionChoreographer.Build(Input(events, start, end, true, notes, calls));
        // The log gets this possession's calls when it has played out (after its plays), not before.
        _logCalls = _pendingCalls.ToList();
        // Review (run 152): a possession that ends a period or the game has no next possession to say its calls, so
        // they follow the period's last line, 2 s each.
        if (_scriptEndsPeriod && _pendingCalls.Count > 0)
        {
            foreach (var line in _pendingCalls) { _script.Lines.Add(new CommentaryLine(_script.Length, line, true)); _script.Length += PossessionChoreographer.NoteTime; }
            _pendingCalls = new();
        }
        // K25 (run 174): the game's over: the result call, then what it meant (2 s each), last on the bar.
        if (_live.Complete)
        {
            var final = GameCalls.Final(r.Plays, Short(HomeTeam), Short(AwayTeam), _live.HomeScore, _live.AwayScore, r.Periods,
                FinalOccasion(_live.HomeScore > _live.AwayScore));
            for (int n = 0; n < final.Count; n++) { _script.Lines.Add(new CommentaryLine(_script.Length, final[n], n == 0)); _script.Length += PossessionChoreographer.NoteTime; }
            // K25's leftover (run 186): the result is said once; it goes back on the bar when the possession has played
            // out (FinishPossession), not as a second line.
            _finalCalls = final;
        }
        // K25 (run 174): what was said, for tests (the whole game's lines in order).
        SaidForTest.AddRange(_script.Lines);
        if (_script.Input.How == PossessionStart.AfterStop && events.FirstOrDefault() is { } fb && fb.Period >= r.Periods && start <= PossessionChoreographer.LateLiveClock
            && _script.Lines.Any(l => l.Text.StartsWith("Substitution"))) LateLiveSubLinesForTest++;
        _scriptT = 0;
        _possIndex++;
        PlanSound();   // M6 (run 153): the crowd's reactions to this possession's events, at their playback times
        ShowFrame();
    }

    /// <summary>
    /// K25's leftover (run 182): simmed to the end, the game still gets its result call. The bar kept the last line it had
    /// said ("RUTLEDGE! He finishes at the rim. George Mason lead 39-37" under FINAL); now it says the result, and the log
    /// gets the result and what it meant, as a game watched to the end does.
    /// </summary>
    private void CallTheResult()
    {
        var r = _live.Result;
        _pendingCalls = new(); _logCalls = new(); _finalCalls = new();
        var final = GameCalls.Final(r.Plays, Short(HomeTeam), Short(AwayTeam), _live.HomeScore, _live.AwayScore, r.Periods,
            FinalOccasion(_live.HomeScore > _live.AwayScore));
        if (final.Count == 0) return;
        Commentary = final[0]; CommentaryBig = true;
        if (Log.Any(l => l.EndsWith(final[0], StringComparison.Ordinal))) return; // already said: the last possession had played out
        foreach (var c in final) Log.Insert(0, $"{_live.PeriodText} {_live.ClockText}  >> {c}");
        SaidForTest.AddRange(final.Select((t, n) => new CommentaryLine(0, t, n == 0)));
    }

    /// <summary>The possession on screen has played out: hand everything else the engine's state, then go on.</summary>
    private void FinishPossession()
    {
        if (_script == null) return;
        _spots = _script.FinalSpots();
        _ballWith = _script.EndHolder;
        _nextStart = _script.Next;
        bool periodEnded = _scriptEndsPeriod;
        _script = null;
        Refresh();
        foreach (var c in _logCalls) Log.Insert(0, $"{_live.PeriodText} {_live.ClockText}  >> {c}");
        _logCalls = new();
        foreach (var c in _finalCalls) Log.Insert(0, $"{_live.PeriodText} {_live.ClockText}  >> {c}");
        if (_finalCalls.Count > 0) { Commentary = _finalCalls[0]; CommentaryBig = true; }   // the result is what stays on the bar
        _finalCalls = new();
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
            FireSound(_scriptT);
            ShowFrame();
            FinishPossession();
            UpdateBed();
            return;
        }
        FireSound(_scriptT);
        ShowFrame();
        UpdateBed();
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

    // M5 (run 152): the commentary's calls across the game (runs, lead changes, milestones, foul trouble).
    private readonly GameCalls _calls = new();
    private List<string> _pendingCalls = new();
    private List<string> _logCalls = new();

    /// <summary>The clutch plan (a team's late-game preset) is on when the engine plays it: the last period or overtime,
    /// five minutes or less to go, within six points (GameSimulator.SetTactics).</summary>
    public static bool ClutchOn(Team t, int period, int periods, double clock, int homeScore, int awayScore) =>
        t.Tactic.LateGamePreset is { } name && period >= periods && clock <= 300 && Math.Abs(homeScore - awayScore) <= 6
        && t.Tactic.Presets.Any(p => p.Name == name);
    private bool _clutchHome, _clutchAway;
    /// <summary>The chip under the shouts while our clutch plan plays: shouts move the base plan, which comes back if the game opens up.</summary>
    [ObservableProperty] private string _clutchChip = "";
    public bool HasClutchChip => ClutchChip.Length > 0;
    partial void OnClutchChipChanged(string value) => OnPropertyChanged(nameof(HasClutchChip));

    private List<string> ClutchNotes()
    {
        var notes = new List<string>();
        // Between periods the engine has moved to the next period but the clock reads 0:00 until it opens: judge with the
        // full clock the period opens with (5 minutes in overtime), or the plan would flash on at every break.
        int period = Math.Max(1, _live.Period);
        double clock = _live.Clock > 0 ? _live.Clock : period <= _live.Result.Periods ? _live.MinutesPerPeriod * 60.0 : 300;
        foreach (var home in new[] { true, false })
        {
            var t = home ? HomeTeam : AwayTeam;
            bool on = ClutchOn(t, period, _live.Result.Periods, clock, _live.HomeScore, _live.AwayScore);
            ref bool was = ref home ? ref _clutchHome : ref _clutchAway;
            if (on && !was) notes.Add($"{Short(t)} go to their clutch plan, {t.Tactic.LateGamePreset}");
            else if (!on && was && !_live.Complete) notes.Add($"{Short(t)} go back to their base plan");
            was = on;
            if (t == Us) ClutchChip = on ? $"Clutch plan on: {t.Tactic.LateGamePreset}. Shouts change your base plan, which comes back if the game opens up." : "";
        }
        return notes;
    }

    [RelayCommand] private void SpeedUp() => SpeedIndex = Math.Min(KeySpeed, SpeedIndex + 1);
    [RelayCommand] private void SpeedDown() => SpeedIndex = Math.Max(0, SpeedIndex - 1);
}
