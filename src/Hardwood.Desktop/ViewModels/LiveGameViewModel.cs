using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.ViewModels;

public sealed partial class OnCourtRow : ObservableObject
{
    public required Player Player { get; init; }
    public string Name => Player.Name;
    public string Pos => Player.PrimaryPosition.ToString();
    [ObservableProperty] private string _line = "";
    [ObservableProperty] private bool _onCourt;
}

/// <summary>One row of the live game's compact box score (the tracker's second slice, run 83).</summary>
public sealed record LiveBoxRow(string Name, string Min, string Pts, string Reb, string Ast, string Fg, string Three, string Pf, bool OnCourt, bool Total = false)
{
    public Avalonia.Media.FontWeight Weight => Total ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
}

/// <summary>B2b (run 141): one row of an arena "On the floor" panel, with its energy bar drawn to <see cref="BarMax"/>.</summary>
public sealed record FloorPanelRow(Hardwood.Core.Engine.FloorRow Row)
{
    public const double BarMax = 210;
    public string Pos => Row.Pos;
    public string Name => Row.Name;
    public string Pts => Row.Pts.ToString();
    public string Reb => Row.Reb.ToString();
    public string Ast => Row.Ast.ToString();
    public string Pf => Row.Pf.ToString();
    public string Fg => Row.Fg;
    public double BarWidth => Math.Max(4, BarMax * Row.Energy);
    private static readonly Avalonia.Media.IBrush Fresh = Avalonia.Media.SolidColorBrush.Parse("#7AD85F"), Ok = Avalonia.Media.SolidColorBrush.Parse("#E9C440"),
        Tired = Avalonia.Media.SolidColorBrush.Parse("#E86040"), Trouble = Avalonia.Media.SolidColorBrush.Parse("#F0A33A"), Plain = Avalonia.Media.Brushes.White;
    public Avalonia.Media.IBrush BarBrush => Row.EnergyBand switch { "fresh" => Fresh, "ok" => Ok, _ => Tired };
    public Avalonia.Media.IBrush PfBrush => Row.FoulTrouble ? Trouble : Plain;
}

/// <summary>B2b (run 141): one line of the jumbotron, the leading side's value lit.</summary>
public sealed record JumboRow(Hardwood.Core.Engine.BoardRow Row)
{
    public string Label => Row.Label;
    public string Away => Row.Away;
    public string Home => Row.Home;
    private static readonly Avalonia.Media.IBrush Lit = Avalonia.Media.SolidColorBrush.Parse("#FFD24A");
    private static readonly Avalonia.Media.IBrush Dim = Avalonia.Media.SolidColorBrush.Parse("#C9D1D9");
    public Avalonia.Media.IBrush AwayBrush => Row.Lead < 0 ? Lit : Dim;
    public Avalonia.Media.IBrush HomeBrush => Row.Lead > 0 ? Lit : Dim;
}

public sealed partial class LineupSlot : ObservableObject
{
    public LiveGameViewModel Owner { get; init; } = null!;
    public ObservableCollection<Player> Options { get; } = new();
    [ObservableProperty] private Player? _selected;
}

/// <summary>Streams a live game: steps the engine on a timer, shows the scoreboard, floor and play log, and lets you pause and change things.</summary>
public sealed partial class LiveGameViewModel : PageViewModel
{
    private readonly GameSimulator.LiveGame _live;
    private readonly List<GameResult> _played;
    private readonly DispatcherTimer _timer;
    private int _logShown;

    public override string Title => "Live game";
    public Team Us => Session.HumanTeam;
    public Team Them => _live.Result.Home.Team == Us ? _live.Result.Away.Team : _live.Result.Home.Team;
    // The college Top 25 rank travels with the team (#528).
    public string Headline => $"{Hardwood.Desktop.Controls.CollegeRank.Prefix(_live.Result.Away.Team, _live.Result.Away.Team.FullName)} at {Hardwood.Desktop.Controls.CollegeRank.Prefix(_live.Result.Home.Team, _live.Result.Home.Team.FullName)}";
    public string AwayName => _live.Result.Away.Team.Abbreviation;
    public string HomeName => _live.Result.Home.Team.Abbreviation;
    public Team AwayTeam => _live.Result.Away.Team;
    public Team HomeTeam => _live.Result.Home.Team;
    public Avalonia.Media.IBrush AwayBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(AwayTeam.PrimaryColor));
    public Avalonia.Media.IBrush HomeBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(HomeTeam.PrimaryColor));
    public Avalonia.Media.Color HomeColor => ThemeService.AccentFor(HomeTeam);
    /// <summary>A college game is played on the home program's court from Danny's pack, under the scoreboard (run 45).</summary>
    public Avalonia.Media.Imaging.Bitmap? HomeCourt => _court ??= Graphics.GraphicsPackService.CourtFor(HomeTeam);
    private Avalonia.Media.Imaging.Bitmap? _court;
    public bool HasCourt => HomeCourt != null;

    // The whole court (Danny, run 45): every shot placed as it happens, from the zone counts' changes between refreshes.
    private readonly List<Hardwood.Desktop.Controls.LiveShot> _shots = new();
    private int _shotPlaysSeen;
    public IReadOnlyList<Hardwood.Desktop.Controls.LiveShot> CourtShots => _shots;
    [ObservableProperty] private int _courtVersion;
    /// <summary>The live panel shows the court (default) or the game flow chart.</summary>
    [ObservableProperty] private bool _showCourt = true;
    public bool ShowFlow => !ShowCourt;
    /// <summary>H57 (run 86): at a break the card takes the court panel; the court or the flow chart shows otherwise.</summary>
    public bool CourtShown => ShowCourt && !ShowBreak;
    public bool FlowShown => ShowFlow && !ShowBreak;
    [RelayCommand] private void ToCourt() { ShowCourt = true; ShowBreak = false; }
    [RelayCommand] private void ToFlow() { ShowCourt = false; ShowBreak = false; }

    /// <summary>The big view (run 85, tracker slice 3): the court takes the room the window has, the play by play and
    /// box score folding under it; off keeps the court at 300 px. Remembered in the settings.</summary>
    [ObservableProperty] private bool _bigCourt = AppSettings.Current.LiveBigCourt;
    public string BigCourtLabel => BigCourt ? "Smaller court" : "Bigger court";
    partial void OnBigCourtChanged(bool value)
    {
        OnPropertyChanged(nameof(BigCourtLabel));
        var settings = AppSettings.Current;
        if (settings.LiveBigCourt == value) return;
        settings.LiveBigCourt = value;
        settings.Save();   // never throws
    }
    [RelayCommand] private void ToggleBigCourt() => BigCourt = !BigCourt;

    // Beside the court (run 83): the play by play or a compact live box score, one tab at a time.
    [ObservableProperty] private bool _showBox;
    public bool ShowLog => !ShowBox;
    partial void OnShowBoxChanged(bool value) { OnPropertyChanged(nameof(ShowLog)); if (value) UpdateBox(); }
    [RelayCommand] private void ToLog() => ShowBox = false;
    [RelayCommand] private void ToBox() => ShowBox = true;
    [ObservableProperty] private IReadOnlyList<LiveBoxRow> _awayBox = Array.Empty<LiveBoxRow>();
    [ObservableProperty] private IReadOnlyList<LiveBoxRow> _homeBox = Array.Empty<LiveBoxRow>();
    public string AwayBoxTitle => $"{AwayTeam.Name} (away)";
    public string HomeBoxTitle => $"{HomeTeam.Name} (home)";

    /// <summary>The compact box score: everyone who has played (or is on the floor), starters first, then by minutes,
    /// and the team's totals. Only rebuilt while its tab is showing.</summary>
    private void UpdateBox()
    {
        if (!ShowBox) return;
        var r = _live.Result;
        AwayBox = BoxRows(r.Away, _live.AwayOnCourt);
        HomeBox = BoxRows(r.Home, _live.HomeOnCourt);
    }

    public static IReadOnlyList<LiveBoxRow> BoxRows(TeamBox box, IReadOnlyList<Player> floor)
    {
        var on = floor.ToHashSet();
        static string Pct(int m, int a) => $"{m}-{a}";
        var rows = box.Lines.Where(l => l.Seconds > 0 || l.Started || on.Contains(l.Player))
            .OrderByDescending(l => l.Started).ThenByDescending(l => l.Seconds)
            .Select(l => new LiveBoxRow(l.Player.Name + (l.FouledOut ? " (out)" : ""), $"{l.Minutes:0}", l.Points.ToString(), l.Rebounds.ToString(),
                l.Assists.ToString(), Pct(l.FGM, l.FGA), Pct(l.ThreePM, l.ThreePA), l.Fouls.ToString(), on.Contains(l.Player)))
            .ToList();
        rows.Add(new LiveBoxRow("Team", "", box.Points.ToString(), box.Lines.Sum(l => l.Rebounds).ToString(), box.Lines.Sum(l => l.Assists).ToString(),
            Pct(box.FGM, box.FGA), Pct(box.ThreePM, box.ThreePA), box.Lines.Sum(l => l.Fouls).ToString(), false, Total: true));
        return rows;
    }

    /// <summary>How long the tracker's motion lasts (run 83): most of a tick at Slow and Normal, none at Fast (too quick
    /// to follow), and the full glide when you Step while paused.</summary>
    public double MotionMs => Running ? Hardwood.Desktop.Controls.TrackerMotion.DurationFor(_speedsMs[_speed]) : 650;
    partial void OnShowCourtChanged(bool value) { OnPropertyChanged(nameof(ShowFlow)); OnPropertyChanged(nameof(CourtShown)); OnPropertyChanged(nameof(FlowShown)); }
    partial void OnShowBreakChanged(bool value)
    {
        OnPropertyChanged(nameof(CourtShown)); OnPropertyChanged(nameof(FlowShown)); OnPropertyChanged(nameof(CanReopenBreak));
        OnPropertyChanged(nameof(DeskUp)); OnPropertyChanged(nameof(CardUp)); OnPropertyChanged(nameof(DeskSpan));
        if (!value) HalftimeReel?.StopAutorun();
    }

    // B4a (run 143): the halftime desk on the HSN set takes the break card's place at halftime (the card, with the
    // assistant's read, is one button away). "Skip the show" in Settings keeps the card only.
    [ObservableProperty] private DeskReelViewModel? _halftimeReel;
    [ObservableProperty] private bool _deskShown;
    partial void OnHalftimeReelChanging(DeskReelViewModel? value) => HalftimeReel?.StopAutorun();   // review, run 143: Sim to end with autorun on
    partial void OnHalftimeReelChanged(DeskReelViewModel? value) { OnPropertyChanged(nameof(DeskUp)); OnPropertyChanged(nameof(CardUp)); OnPropertyChanged(nameof(DeskSpan)); OnPropertyChanged(nameof(HasDesk)); }
    partial void OnDeskShownChanged(bool value) { OnPropertyChanged(nameof(DeskUp)); OnPropertyChanged(nameof(CardUp)); OnPropertyChanged(nameof(DeskSpan)); if (!value) HalftimeReel?.StopAutorun(); }
    /// <summary>The halftime desk is on screen (in the court's place).</summary>
    public bool DeskUp => ShowBreak && DeskShown && HalftimeReel != null;
    /// <summary>The desk takes both columns while it's up (the lineup tabs come back with the card or Hide).</summary>
    public int DeskSpan => DeskUp ? 2 : 1;
    /// <summary>The break card is on screen.</summary>
    public bool CardUp => ShowBreak && !DeskUp;
    /// <summary>This break has a halftime desk to go back to.</summary>
    public bool HasDesk => HalftimeReel != null;
    /// <summary>B4b (run 145): the desk up now is the postgame show (at the final), not the halftime desk.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(DeskName), nameof(DeskButton), nameof(DeskButtonTip), nameof(CardTip))]
    private bool _postgameDesk;
    public string DeskName => PostgameDesk ? "HSN postgame show" : "HSN halftime desk";
    public string DeskButton => PostgameDesk ? "Postgame show" : "Halftime desk";
    public string DeskButtonTip => PostgameDesk ? "Back to the HSN postgame show: the final, the player of the game, how it was won and what it means"
        : "Back to the HSN desk: the half's story, the stat that decided it and the pick";
    public string CardTip => PostgameDesk ? "The final's numbers and highlights" : "The numbers and the assistant's halftime read";
    [RelayCommand] private void ToDesk() { if (HalftimeReel != null) { DeskShown = true; ShowBreak = true; } }
    [RelayCommand] private void ToCard() { DeskShown = false; ShowBreak = true; }
    // The card of the current break or the final, put away with Hide / On the floor / Game flow, comes back with "Break card"
    // until play goes on (code review, run 86).
    private bool _breakCardUp;
    public bool CanReopenBreak => _breakCardUp && !ShowBreak;
    [RelayCommand] private void ReopenBreak() { if (_breakCardUp) ShowBreak = true; }
    private void EndBreakCard() { _breakCardUp = false; ShowBreak = false; DeskShown = false; HalftimeReel = null; OnPropertyChanged(nameof(CanReopenBreak)); }
    public Avalonia.Media.Color HomeDot => Bright(Avalonia.Media.Color.Parse(HomeTeam.PrimaryColor));
    public Avalonia.Media.Color AwayDot => Bright(Distinct(AwayTeam, Avalonia.Media.Color.Parse(HomeTeam.PrimaryColor)));

    /// <summary>Dark club colours (navy, black) lifted so the dots read on a wooden floor.</summary>
    private static Avalonia.Media.Color Bright(Avalonia.Media.Color c)
    {
        double lum = (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
        if (lum >= 0.35) return c;
        double t = Math.Min(0.6, 0.35 - lum + 0.3);
        return Avalonia.Media.Color.FromRgb((byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
    }
    public Avalonia.Media.Color FloorColor => Avalonia.Media.Color.Parse(HomeTeam.PrimaryColor);
    public Avalonia.Media.Color FloorTrim => Avalonia.Media.Color.Parse(HomeTeam.SecondaryColor);
    public string FloorName => HomeTeam.Name;
    // Run 118: the key in two short lines at 130% (it was four, and a two-line play caption then took the play by play
    // under its 180 px); how the court is drawn is in the tooltip.
    public string CourtLegend => $"{AwayName} attack left, {HomeName} right · gold ring: the play · dotted: the pass · faint dots: shots";
    public string CourtLegendTip => "Player spots and movement are drawn for show; every result comes from the game engine.";

    // The 2D tracker (D67 B, run 80): the latest play drawn on the court, with the ten players and the ball.
    [ObservableProperty] private Hardwood.Desktop.Controls.TrackerFrame? _frame;
    /// <summary>The frame for whichever court is shown (code review, run 140: the hidden one gets none, so only one
    /// court runs its motion loop).</summary>
    public Hardwood.Desktop.Controls.TrackerFrame? FlatFrame => InArena ? null : Frame;
    public Hardwood.Desktop.Controls.TrackerFrame? ArenaFrame => InArena ? Frame : null;
    partial void OnFrameChanged(Hardwood.Desktop.Controls.TrackerFrame? value) { OnPropertyChanged(nameof(FlatFrame)); OnPropertyChanged(nameof(ArenaFrame)); }
    [ObservableProperty] private string _playCaption = "Tip-off.";
    private int _framePlay = -1;

    /// <summary>Build the tracker's frame from the engine's latest play and the two fives on the floor.</summary>
    private void UpdateFrame()
    {
        var plays = _live.Result.Plays;
        if (plays.Count == _framePlay + 1 && Frame != null) return;
        int first = _framePlay + 1;
        _framePlay = plays.Count - 1;
        if (plays.Count == 0)
        {
            PlayCaption = "Tip-off.";
            Frame = Hardwood.Desktop.Controls.TrackerLayout.Build(null, 0, _live.HomeOnCourt, _live.AwayOnCourt, PlayCaption);
            return;
        }
        int idx = HeadlinePlay(plays, first);
        var play = plays[idx];
        var last = plays[^1];
        string line = Caption(play);
        if (last != play && last.Kind is not PlayKind.Foul) line += " · " + Caption(last);
        PlayCaption = $"{_live.PeriodText} {Clk(play.Clock)}  {line}";
        Frame = Hardwood.Desktop.Controls.TrackerLayout.Build(play, idx, _live.HomeOnCourt, _live.AwayOnCourt, PlayCaption);
    }

    /// <summary>
    /// The play the tracker draws for the possessions just played: the latest shot, steal, turnover or free throw (a
    /// rebound or foul shows the shot or trip it came from); free throws after a made basket (an and-one) show the basket.
    /// </summary>
    public static int HeadlinePlay(IReadOnlyList<PlayEvent> plays, int first)
    {
        first = Math.Clamp(first, 0, plays.Count - 1);
        static bool Ball(PlayKind k) => k is PlayKind.Shot or PlayKind.Steal or PlayKind.Turnover or PlayKind.FreeThrow;
        for (int i = plays.Count - 1; i >= first; i--)
        {
            if (!Ball(plays[i].Kind)) continue;
            if (plays[i].Kind == PlayKind.FreeThrow)
            {
                int j = i;
                var ft = plays[i].Actor;
                // Only his own trip: his free throws and the foul on him.
                while (j - 1 >= first && (plays[j - 1] is { Kind: PlayKind.FreeThrow } f && f.Actor == ft || plays[j - 1] is { Kind: PlayKind.Foul } g && g.Other == ft)) j--;
                if (j - 1 >= first && plays[j - 1] is { Kind: PlayKind.Shot, Made: true } shot && shot.Actor == plays[i].Actor) return j - 1;
            }
            return i;
        }
        for (int i = plays.Count - 1; i >= first; i--) if (plays[i].Kind == PlayKind.PeriodEnd) return i;
        return plays.Count - 1;
    }

    private static string Clk(double c) => $"{(int)(c / 60)}:{(int)(c % 60):00}";

    /// <summary>One line for a play, as the tracker shows it.</summary>
    public string Caption(PlayEvent e)
    {
        string team = (e.Home ? HomeTeam : AwayTeam).Abbreviation;
        string Who(Player? p) => p?.LastName ?? "";
        string shot = e.Zone switch
        {
            ShotZone.Rim => "at the rim", ShotZone.ShortMid => "a short jumper", ShotZone.LongMid => "a long two",
            ShotZone.CornerThree => "a corner three", _ => "a three",
        };
        return e.Kind switch
        {
            PlayKind.Shot when e.Made => $"{team}: {Who(e.Actor)} scores {(e.Zone == ShotZone.Rim ? "at the rim" : "with " + shot)}" + (e.Other != null ? $" (assist {Who(e.Other)})" : ""),
            PlayKind.Shot when e.Blocked => $"{team}: {Who(e.Actor)} is blocked by {Who(e.Other)}",
            PlayKind.Shot => $"{team}: {Who(e.Actor)} misses {(e.Zone == ShotZone.Rim ? "at the rim" : shot)}",
            PlayKind.FreeThrow => $"{team}: {Who(e.Actor)} at the line, {(e.Made ? "good" : "no good")}",
            PlayKind.Turnover => $"{team}: turnover by {Who(e.Actor)}",
            PlayKind.Steal => $"{team}: {Who(e.Actor)} steals it from {Who(e.Other)}",
            PlayKind.OffensiveRebound => $"{team}: offensive rebound, {Who(e.Actor)}",
            PlayKind.DefensiveRebound => $"{team}: {Who(e.Actor)} rebounds",
            PlayKind.Foul => e.Other != null ? $"{team}: foul by {Who(e.Actor)} on {Who(e.Other)}" : $"{team}: foul by {Who(e.Actor)}",
            PlayKind.PeriodEnd => $"End of the period: {AwayName} {e.AwayScore}, {HomeName} {e.HomeScore}",
            _ => "",
        };
    }

    private static Avalonia.Media.Color Distinct(Team t, Avalonia.Media.Color other)
    {
        var c = Avalonia.Media.Color.Parse(t.PrimaryColor);
        int d = Math.Abs(c.R - other.R) + Math.Abs(c.G - other.G) + Math.Abs(c.B - other.B);
        return d >= 120 ? c : Avalonia.Media.Color.Parse(t.SecondaryColor);
    }

    /// <summary>The court as a brush, so the picture fills the scoreboard without setting its size.</summary>
    public Avalonia.Media.IBrush? CourtBrush => HomeCourt is { } b ? new Avalonia.Media.ImageBrush(b) { Stretch = Avalonia.Media.Stretch.UniformToFill, Opacity = 0.75 } : null;

    // Match day (roadmap 2): the game's shape as it happens.
    public GameResult Result => _live.Result;
    public Avalonia.Media.Color FlowHome => GameFlowInfo.Colours(_live.Result).Home;
    public Avalonia.Media.Color FlowAway => GameFlowInfo.Colours(_live.Result).Away;
    [ObservableProperty] private int _flowVersion;
    [ObservableProperty] private string _winChance = "";
    [ObservableProperty] private string? _runAlert;
    [ObservableProperty] private List<SplitRow> _splits = new();
    [ObservableProperty] private int[] _awayZoneAttempts = new int[5];
    [ObservableProperty] private int[] _awayZoneMakes = new int[5];
    [ObservableProperty] private int[] _homeZoneAttempts = new int[5];
    [ObservableProperty] private int[] _homeZoneMakes = new int[5];
    // Match day part 2: highlight cards, team stats and the ticker.
    [ObservableProperty] private List<Highlight> _highlights = new();
    [ObservableProperty] private List<TeamStatRow> _teamStats = new();
    [ObservableProperty] private string _ticker = "";
    private int _tickerIndex, _tickerTicks;
    /// <summary>The ticker moves on to its next fact every this many refreshes.</summary>
    public const int TickerEvery = 6;
    public bool HasRunAlert => RunAlert != null;
    partial void OnRunAlertChanged(string? value) => OnPropertyChanged(nameof(HasRunAlert));
    private string? _lastRunKey;

    [ObservableProperty] private int _awayScore;
    [ObservableProperty] private int _homeScore;
    [ObservableProperty] private string _clock = "";
    [ObservableProperty] private bool _running;
    [ObservableProperty] private bool _complete;
    [ObservableProperty] private string _speedLabel = "Normal";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _periodLine = "";
    public ObservableCollection<string> Log { get; } = new();
    public ObservableCollection<OnCourtRow> OurPlayers { get; } = new();
    public ObservableCollection<OnCourtRow> TheirFloor { get; } = new();
    public ObservableCollection<LineupSlot> Lineup { get; } = new();
    public string PlayPauseText => Complete ? "Final" : Running ? "Pause" : "Play";
    public string PlayPauseIcon => Running ? "Pause" : "Play";

    // Tactics bound straight to the team: the engine reads them every possession.
    public double Pace { get => Us.Tactic.Pace; set { Us.Tactic.Pace = value; OnPropertyChanged(); } }
    public double ThreeEmphasis { get => Us.Tactic.ThreePointEmphasis; set { Us.Tactic.ThreePointEmphasis = (int)value; OnPropertyChanged(); } }
    public double CrashGlass { get => Us.Tactic.CrashGlass; set { Us.Tactic.CrashGlass = (int)value; OnPropertyChanged(); } }
    public double Aggression { get => Us.Tactic.DefensiveAggression; set { Us.Tactic.DefensiveAggression = (int)value; OnPropertyChanged(); } }
    public double StarUsage { get => Us.Tactic.StarUsage; set { Us.Tactic.StarUsage = (int)value; OnPropertyChanged(); } }
    public List<SystemOption> SystemChoices { get; } = Enum.GetValues<Hardwood.Core.Model.OffensiveSystem>()
        .Select(x => new SystemOption { Name = Hardwood.Core.Model.Systems.Name(x), Description = "", Fit = 0.5, System = x }).ToList();
    public List<SystemOption> SchemeChoices { get; } = Enum.GetValues<Hardwood.Core.Model.DefensiveScheme>()
        .Select(x => new SystemOption { Name = Hardwood.Core.Model.Systems.Name(x), Description = "", Fit = 0.5, Scheme = x }).ToList();
    public SystemOption? OffenseChoice
    {
        get => SystemChoices.FirstOrDefault(o => o.System == Us.Tactic.System);
        set { if (value?.System is { } x) { Us.Tactic.System = x; OnPropertyChanged(); } }
    }
    public SystemOption? DefenseChoice
    {
        get => SchemeChoices.FirstOrDefault(o => o.Scheme == Us.Tactic.Scheme);
        set { if (value?.Scheme is { } x) { Us.Tactic.Scheme = x; OnPropertyChanged(); } }
    }
    public bool DoubleStar { get => Us.Tactic.DoubleStar; set { Us.Tactic.DoubleStar = value; OnPropertyChanged(); } }

    private readonly int[] _speedsMs = { 700, 250, 60 };
    private int _speed = 1;

    public LiveGameViewModel(MainViewModel shell, GameSimulator.LiveGame live, List<GameResult> played) : base(shell)
    {
        _live = live; _played = played;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(_speedsMs[_speed]) };
        _timer.Tick += (_, _) => Tick();

        var ours = _live.Result.Home.Team == Us ? _live.Result.Home : _live.Result.Away;
        foreach (var p in ours.Lines.Where(l => Us.Roster.Contains(l.Player) && l.Player.IsAvailable(Session.Today)).Select(l => l.Player).OrderByDescending(p => p.Overall))
            OurPlayers.Add(new OnCourtRow { Player = p });
        for (int i = 0; i < 5; i++)
        {
            var slot = new LineupSlot { Owner = this };
            foreach (var row in OurPlayers) slot.Options.Add(row.Player);
            Lineup.Add(slot);
        }
        if (Session.InCollege && Graphics.BroadcastArt.Arena is { Frame: not null, Court: not null } arena)
        {
            Arena = arena;
            Dress = Hardwood.Core.College.ArenaDress.For(HomeTeam, Session.World.College?.GameOn(Us.Id, Session.Today));
        }
        // K10 (run 136): the assistant's panel reads this game while it's on.
        Shell.Guide.SetLive(() => Hardwood.Core.Guide.Assistant.LiveRead(_live.Result, Us, _live.PeriodText, _live.ClockText, _live.Complete));
        Refresh();
        Status = "Press Play to tip off. Pause at any time to change your five or your tactics.";
    }

    // B2a (run 140): a college game is shown in the arena: the floor in perspective, dressed for the home program or
    // the event, the crowd in its colours, and a broadcast score bug. Presentation only: it reads the game.
    /// <summary>The arena frame, for a college game when the build carries it (else the flat court).</summary>
    public Graphics.BroadcastSet? Arena { get; }
    public Hardwood.Core.College.ArenaDress? Dress { get; }
    public bool InArena => Arena != null;
    public bool FlatCourt => !InArena;
    public static string BugRank(Team t) => Hardwood.Desktop.Controls.CollegeRank.For(t) is { } r ? r.ToString() : "";
    public string AwayRank => BugRank(AwayTeam);
    public string HomeRank => BugRank(HomeTeam);
    public bool AwayRanked => AwayRank.Length > 0;
    public bool HomeRanked => HomeRank.Length > 0;
    /// <summary>"1ST HALF", "2ND HALF", "OT", "2OT" (or quarters in the pros), and FINAL at the end.</summary>
    public string BugPeriod => _live.Complete ? (_live.Period > _live.Result.Periods ? (_live.Period - _live.Result.Periods == 1 ? "FINAL/OT" : $"FINAL/{_live.Period - _live.Result.Periods}OT") : "FINAL")
        : BetweenPeriods ? BreakName(_live.Period - 1, _live.Result.Periods)   // run 141: it read "2ND HALF 0:00" at halftime
        : BugPeriodName(Math.Max(1, _live.Period), _live.Result.Periods);   // before the tip the game is in period 0
    /// <summary>True between periods: the engine has closed one (its clock at 0) and not yet opened the next.</summary>
    public bool BetweenPeriods => !_live.Complete && _live.Period > 1 && _live.Clock <= 0;
    /// <summary>"HALFTIME", "END OF 1ST QTR", "END OF 2ND HALF" (heading to overtime), "END OF OT".</summary>
    public static string BreakName(int ended, int periods) => ended * 2 == periods ? "HALFTIME" : $"END OF {BugPeriodName(ended, periods)}";
    public static string BugPeriodName(int period, int periods) =>
        period > periods ? (period - periods == 1 ? "OT" : $"{period - periods}OT")
        : periods == 2 ? (period == 1 ? "1ST HALF" : "2ND HALF")
        : period switch { 1 => "1ST QTR", 2 => "2ND QTR", 3 => "3RD QTR", _ => "4TH QTR" };
    public string BugClock => _live.Complete || BetweenPeriods ? "" : _live.Period == 0 ? $"{_live.MinutesPerPeriod}:00" : _live.ClockText;
    public string AwayFouls => $"FOULS {_live.AwayTeamFouls}";
    public string HomeFouls => $"FOULS {_live.HomeTeamFouls}";
    /// <summary>"BONUS" (a one-and-one in college) or "DBL BONUS" beside the side that shoots free throws on a common foul.</summary>
    public static string BonusLabel(int shots, bool oneAndOne) => shots == 0 ? "" : oneAndOne && shots == 1 ? "BONUS" : oneAndOne ? "DBL BONUS" : "BONUS";
    public string AwayBonus => _live.Complete ? "" : BonusLabel(_live.BonusFor(homeShoots: false), _live.OneAndOne);
    public string HomeBonus => _live.Complete ? "" : BonusLabel(_live.BonusFor(homeShoots: true), _live.OneAndOne);
    public bool AwayInBonus => AwayBonus.Length > 0;
    public bool HomeInBonus => HomeBonus.Length > 0;
    // B2b (run 141): the jumbotron (the team comparison, live), the two "On the floor" panels with energy bars, the
    // offence and defence tags and the momentum chip. Presentation only: read from the box score and the engine.
    [ObservableProperty] private IReadOnlyList<Hardwood.Core.Engine.BoardRow> _board = Array.Empty<Hardwood.Core.Engine.BoardRow>();
    /// <summary>The rows the jumbotron shows (all but FG%, which the FG line's light already says).</summary>
    [ObservableProperty] private IReadOnlyList<JumboRow> _jumboRows = Array.Empty<JumboRow>();
    partial void OnBoardChanged(IReadOnlyList<Hardwood.Core.Engine.BoardRow> value) => JumboRows = value.Where(b => b.Label != "FG%").Select(b => new JumboRow(b)).ToList();
    [ObservableProperty] private IReadOnlyList<FloorPanelRow> _awayFloor = Array.Empty<FloorPanelRow>();
    [ObservableProperty] private IReadOnlyList<FloorPanelRow> _homeFloor = Array.Empty<FloorPanelRow>();
    [ObservableProperty] private Hardwood.Core.Engine.MomentumRead _momentum = new(0, "EVEN");
    /// <summary>The jumbotron's title: the half and clock in play, the break's name at a break, FINAL at the end.</summary>
    public string BoardTitle => _live.Complete || BetweenPeriods ? BugPeriod
        : _live.Period == 0 ? "TIP-OFF" : $"{BugPeriod} · {BugClock}";
    public string AwayFloorTitle => $"ON THE FLOOR · {AwayName}";
    public string HomeFloorTitle => $"ON THE FLOOR · {HomeName}";
    /// <summary>The momentum bar is <see cref="MomentumBar"/> wide: the away colour from the left, the home colour from the right.</summary>
    public const double MomentumBar = 296;
    public double MomentumHomeWidth => MomentumBar * Hardwood.Core.Engine.ArenaBoard.HomeShare(Momentum);
    public double MomentumAwayWidth => MomentumBar - MomentumHomeWidth;
    partial void OnMomentumChanged(Hardwood.Core.Engine.MomentumRead value) { OnPropertyChanged(nameof(MomentumHomeWidth)); OnPropertyChanged(nameof(MomentumAwayWidth)); }
    /// <summary>Which side has the ball in the play shown (home attacks the right-hand basket).</summary>
    public bool HomeOffence => Frame?.HomeOffense ?? true;
    /// <summary>No ball between periods or after the final: the tags then just name the sides (code review, run 141).</summary>
    private bool NoBall => _live.Complete || BetweenPeriods;
    public string AwayTag => NoBall ? AwayName : $"{(HomeOffence ? "DEFENCE" : "OFFENCE")} · {AwayName}";
    public string HomeTag => NoBall ? HomeName : $"{(HomeOffence ? "OFFENCE" : "DEFENCE")} · {HomeName}";
    public bool AwayOnOffence => !NoBall && !HomeOffence;
    public bool HomeOnOffence => !NoBall && HomeOffence;
    /// <summary>The side with the ball has its tag lit; the defence's is dimmed.</summary>
    public double AwayTagOpacity => AwayOnOffence || NoBall ? 1 : 0.6;
    public double HomeTagOpacity => HomeOnOffence || NoBall ? 1 : 0.6;

    private void UpdateBoard()
    {
        if (!InArena) return;
        var r = _live.Result;
        // Code review (run 141): only hand the views new lists when something changed, so the rows aren't rebuilt every tick.
        var board = Hardwood.Core.Engine.ArenaBoard.Compare(r);
        if (!board.SequenceEqual(Board)) Board = board;
        IReadOnlyList<FloorPanelRow> Five(TeamBox box, IReadOnlyList<Player> five) => Hardwood.Core.Engine.ArenaBoard
            .OnFloor(box, five, _live.EnergyOf, _live.FoulsToFoulOut, _live.SubOutEnergy).Select(x => new FloorPanelRow(x)).ToList();
        var away = Five(r.Away, _live.AwayOnCourt);
        if (!away.SequenceEqual(AwayFloor)) AwayFloor = away;
        var home = Five(r.Home, _live.HomeOnCourt);
        if (!home.SequenceEqual(HomeFloor)) HomeFloor = home;
        Momentum = Hardwood.Core.Engine.ArenaBoard.Momentum(r);
        foreach (var n in BoardProps) OnPropertyChanged(n);
    }
    private static readonly string[] BoardProps = { nameof(BoardTitle), nameof(HomeOffence), nameof(AwayTag), nameof(HomeTag), nameof(AwayOnOffence), nameof(HomeOnOffence), nameof(AwayTagOpacity), nameof(HomeTagOpacity) };

    private static readonly string[] BugProps = { nameof(BugPeriod), nameof(BugClock), nameof(AwayFouls), nameof(HomeFouls), nameof(AwayBonus), nameof(HomeBonus),
        nameof(AwayInBonus), nameof(HomeInBonus), nameof(AwayRank), nameof(HomeRank), nameof(AwayRanked), nameof(HomeRanked) };

    partial void OnRunningChanged(bool value) { OnPropertyChanged(nameof(MotionMs)); OnPropertyChanged(nameof(PlayPauseText)); OnPropertyChanged(nameof(PlayPauseIcon)); OnPropertyChanged(nameof(CanStep)); }
    partial void OnCompleteChanged(bool value)
    {
        OnPropertyChanged(nameof(PlayPauseText)); OnPropertyChanged(nameof(CanStep)); OnPropertyChanged(nameof(TickerTag));
        if (value) Refresh();   // H71: the top bar's line turns to the result
    }

    private void Tick() => Advance(_speed == 2 ? 3 : 1);

    /// <summary>While paused: play one possession and stop (the tracker's step control).</summary>
    [RelayCommand]
    public void StepOne()
    {
        if (Complete || Running) return;
        EndBreakCard();
        Advance(1);
        if (!Complete && !Running && !ShowBreak) Status = "Stepped one possession. Step again, or press Play.";
    }
    public bool CanStep => !Complete && !Running;

    private void Advance(int steps)
    {
        // A few possessions per tick at the faster speeds keeps the log readable without being sluggish.
        bool periodEnded = false;
        for (int i = 0; i < steps && !_live.Complete && !periodEnded; i++)
        {
            int before = _live.Log.Count;
            _live.Step();
            for (int k = before; k < _live.Log.Count; k++) if (_live.Log[k].Contains("End of period")) periodEnded = true;
        }
        Refresh();
        if (_live.Complete)
        {
            _timer.Stop(); Running = false; Complete = true;
            RestoreRead();
            Status = $"Final: {AwayName} {AwayScore}, {HomeName} {HomeScore}. Continue to record the result and move on.";
            ShowBreakCard(final: true);
        }
        else if (periodEnded && PauseAtBreaks)
        {
            // Match day part 3: the broadcast stops at the break for a recap card; press Play to go on.
            _timer.Stop(); Running = false;
            ShowBreakCard(final: false);
            Status = "Break in play. Make changes now, or press Play for the next period.";
        }
    }

    // Match day part 3: the break card (end of a period, halftime, final).
    [ObservableProperty] private bool _pauseAtBreaks = true;
    [ObservableProperty] private bool _showBreak;
    /// <summary>H57 (run 86): put the break card away to see the court again.</summary>
    [RelayCommand] private void HideBreak() => ShowBreak = false;
    [ObservableProperty] private string _breakTitle = "";
    [ObservableProperty] private string _breakLine = "";
    [ObservableProperty] private List<Hardwood.Desktop.Controls.Chip> _breakChips = new();

    /// <summary>Fill the break card: the period just played, the leaders so far, and (at the final) the game's highlights.</summary>
    public void ShowBreakCard(bool final)
    {
        var r = _live.Result;
        int period = r.CurrentPeriod;
        // The period that just ended is the last "End of period" in the log.
        var endLine = _live.Log.LastOrDefault(l => l.Contains("End of period"));
        if (endLine != null && int.TryParse(endLine.Split("End of period ")[1].Split(':')[0], out var ended)) period = ended;
        BreakTitle = final ? (r.Overtimes > 0 ? $"Final ({(r.Overtimes == 1 ? "OT" : $"{r.Overtimes}OT")})" : "Final")
                   : period * 2 == r.Periods ? "Halftime"
                   : $"End of {Hardwood.Core.Engine.GameFlow.PeriodName(r, period)}";
        BreakLine = _live.Log.LastOrDefault(l => l.Contains("quarter goes") || l.Contains("half goes") || l.Contains("is even") || l.Contains("Overtime goes"))?.Split("  ", 2).Last() ?? "";
        var chips = new List<Hardwood.Desktop.Controls.Chip>();
        foreach (var box in new[] { r.Away, r.Home })
        {
            var top = box.Lines.Where(l => l.Seconds > 0).OrderByDescending(l => l.Points).FirstOrDefault();
            if (top != null) chips.Add(new($"{box.Team.Abbreviation} leader", $"{top.Points} pts", $"{top.Player.Name} · {top.Rebounds} reb · {top.Assists} ast", "team"));
        }
        string Pct(TeamBox b) => b.FGA == 0 ? "-" : $"{100.0 * b.FGM / b.FGA:0}%";
        chips.Add(new("Shooting", $"{Pct(r.Away)} · {Pct(r.Home)}", $"{r.Away.Team.Abbreviation} · {r.Home.Team.Abbreviation} from the field", "info"));
        if (!final) chips.Add(new("Win chance", WinChance, "heading into the next period", "info"));   // H71 (review): not on the final card
        bool halftime = !final && period * 2 == r.Periods;
        ReadSuggestion = null;
        if (!final && _live.HalftimeRead(Us) is var (cs, cd))
        {
            // The in-game read (#268): what the other bench is really running, and the assistant's counter, worked out
            // exactly as Continue does at halftime (the players still able to play).
            bool already = cs == Us.Tactic.System && cd == Us.Tactic.Scheme;
            if (halftime && !already)
                // D75 A (run 69): at halftime the read is a one-click suggestion instead of a chip.
                ReadSuggestion = (cs, cd);
            else
                chips.Add(new($"{Them.Abbreviation} tactics", already ? "Countered" : "Switch?",
                    $"They run {N(Them.Tactic.System)} and {D(Them.Tactic.Scheme)}. " + (already ? "You're in the counter." : $"Counter: {N(cs)} and {D(cd)}."),
                    already ? "good" : "warn"));
        }
        UpdateRead();
        if (final)
            foreach (var h in Hardwood.Core.Engine.GameHighlights.For(r, final: true, max: 3))
                chips.Add(new(h.Title, h.Value, h.Detail, h.Tone));
        BreakChips = chips;
        // B4a: at halftime the HSN desk comes on first, unless the shows are skipped. B4b (run 145): so does the postgame
        // show at the final.
        PostgameDesk = final;
        if (halftime && AppSettings.Current.StudioShow)
        {
            var wp = Hardwood.Core.Engine.GameFlow.WinProbability(r);
            double home = wp.Count == 0 ? 0.5 : wp[^1].Percent / 100.0;
            HalftimeReel = new DeskReelViewModel(Hardwood.Core.Season.HalftimeDesk.Build(r, home, Session.HalftimeAround(r.Home.Team, r.Away.Team),
                t => _live.HalftimeRead(t)), r.Away.Team, r.Home.Team, "Halftime");
            DeskShown = true;
        }
        else if (final && AppSettings.Current.StudioShow)
        {
            var (rows, line) = Session.PostgameTable(r);
            HalftimeReel = new DeskReelViewModel(Hardwood.Core.Season.PostgameDesk.Build(r, rows, line, Session.HalftimeAround(r.Home.Team, r.Away.Team)),
                r.Away.Team, r.Home.Team, "Postgame");
            DeskShown = true;
        }
        else { HalftimeReel = null; DeskShown = false; }
        _breakCardUp = true;
        ShowBreak = true;
        OnPropertyChanged(nameof(DeskUp)); OnPropertyChanged(nameof(CardUp)); OnPropertyChanged(nameof(DeskSpan));
    }

    private static string N(OffensiveSystem x) => Systems.Name(x).ToLower();
    private static string D(DefensiveScheme x) => Systems.Name(x).ToLower();

    // D75 A (run 69): the halftime read as a suggestion on the halftime card, applied with one click.
    /// <summary>The counter the assistant suggests at this halftime, or null (not halftime, or you're already in it).</summary>
    public (OffensiveSystem System, DefensiveScheme Scheme)? ReadSuggestion { get; private set; }
    /// <summary>Your own system and scheme before you applied the read: put back when the game ends (second half only).</summary>
    private (OffensiveSystem System, DefensiveScheme Scheme)? _beforeRead;
    [ObservableProperty] private bool _hasReadSuggestion;
    [ObservableProperty] private bool _readApplied;
    [ObservableProperty] private string _readTitle = "";
    [ObservableProperty] private string _readLine = "";
    [ObservableProperty] private string _readNote = "";
    public bool CanApplyRead => HasReadSuggestion && !ReadApplied;
    partial void OnHasReadSuggestionChanged(bool value) => OnPropertyChanged(nameof(CanApplyRead));
    partial void OnReadAppliedChanged(bool value) => OnPropertyChanged(nameof(CanApplyRead));

    private void UpdateRead()
    {
        HasReadSuggestion = ReadSuggestion != null || ReadApplied;
        if (!HasReadSuggestion) return;
        var t = Them.Tactic;
        if (ReadApplied && _applied is { } a)
        {
            ReadTitle = "Assistant's read: applied";
            ReadLine = $"You switched to {N(a.System)} and {D(a.Scheme)} against their {N(t.System)} and {D(t.Scheme)}.";
            ReadNote = _beforeRead is { } b
                ? $"For the second half only: after the game your tactics go back to {N(b.System)} and {D(b.Scheme)}."
                : "For the second half only.";
            return;
        }
        var (cs, cd) = ReadSuggestion!.Value;
        var changes = new List<string>();
        if (cs != Us.Tactic.System) changes.Add($"run {N(cs)}");
        if (cd != Us.Tactic.Scheme) changes.Add($"play {D(cd)}");
        ReadTitle = "Assistant's halftime read";
        ReadLine = $"{Them.Abbreviation} are running {N(t.System)} and {D(t.Scheme)}. Our players suit the counter: {string.Join(" and ", changes)}.";
        ReadNote = Us.Tactic.HalftimeAdjust && !Us.Tactic.Auto
            ? "On Continue this is applied for you (Tactics: halftime read on). Watching live, it's your call. Second half only."
            : "Second half only: your Tactics page keeps your own plan.";
    }

    private (OffensiveSystem System, DefensiveScheme Scheme)? _applied;

    /// <summary>Apply the assistant's halftime read for the second half (D75 A).</summary>
    [RelayCommand]
    public void ApplyRead()
    {
        if (ReadSuggestion is not { } s || ReadApplied) return;
        _beforeRead = (Us.Tactic.System, Us.Tactic.Scheme);
        Us.Tactic.System = s.System; Us.Tactic.Scheme = s.Scheme;
        _applied = s;
        ReadApplied = true;
        OnPropertyChanged(nameof(OffenseChoice)); OnPropertyChanged(nameof(DefenseChoice));
        UpdateRead();
        // The chip strip's read is the same; no need to rebuild it.
        Status = $"Second half: {N(s.System)} and {D(s.Scheme)}. Press Play to go on.";
    }

    /// <summary>Take the read back off and return to your own plan.</summary>
    [RelayCommand]
    public void UndoRead()
    {
        if (!ReadApplied) return;
        RestoreRead();
        Status = $"Back to your plan: {N(Us.Tactic.System)} and {D(Us.Tactic.Scheme)}.";
    }

    /// <summary>
    /// Put your own system and scheme back (at Undo, or when the game ends, so the read is for the second half only, as on
    /// Continue). Anything you changed yourself after applying it is kept.
    /// </summary>
    public void RestoreRead()
    {
        if (!ReadApplied || _beforeRead is not { } b || _applied is not { } a) return;
        if (Us.Tactic.System == a.System) Us.Tactic.System = b.System;
        if (Us.Tactic.Scheme == a.Scheme) Us.Tactic.Scheme = b.Scheme;
        ReadApplied = false; _beforeRead = null; _applied = null;
        OnPropertyChanged(nameof(OffenseChoice)); OnPropertyChanged(nameof(DefenseChoice));
        UpdateRead();
    }

    /// <summary>H71 (run 106): the top bar talks about the game on screen ("Now: vs GSW · Q2"), not "Next:" about it, and gives
    /// the result after the final. The real next game comes back when Continue records this one.</summary>
    public static string TopBarLine(bool home, string opponent, string period, bool final, int us, int them) =>
        final ? $"Final: {(us > them ? "W" : "L")} {us}-{them} {(home ? "vs" : "at")} {opponent}"
              : $"Now: {(home ? "vs" : "at")} {opponent} · {period}";

    /// <summary>H106 (run 112): the top bar's record once the final is on screen, before Continue records the game.</summary>
    public static string RecordAfter(int wins, int losses, bool won) => won ? $"{wins + 1}-{losses}" : $"{wins}-{losses + 1}";

    /// <summary>The ticker's tag: LIVE during the game, FINAL after it (H71).</summary>
    public string TickerTag => Complete ? "FINAL" : "LIVE";

    public override void Refresh()
    {
        AwayScore = _live.AwayScore; HomeScore = _live.HomeScore;
        // Run 144 (broadcast check): the top scoreboard read "H2 0:00" at halftime (the arena's bug was fixed in run 141).
        // Run 145: it read "H2 0:00" at the final too.
        string? breakText = _live.Complete ? (_live.Result.Overtimes == 0 ? "FINAL" : _live.Result.Overtimes == 1 ? "FINAL/OT" : $"FINAL/{_live.Result.Overtimes}OT")
            : BetweenPeriods ? ((_live.Period - 1) * 2 == _live.Result.Periods ? "HALFTIME" : "BREAK") : null;
        Clock = breakText ?? $"{_live.PeriodText}  {_live.ClockText}";
        if (InArena) foreach (var n in BugProps) OnPropertyChanged(n);
        Shell.Guide.LiveTick();
        var r = _live.Result;
        PeriodLine = $"{AwayName} {string.Join(" ", r.Away.PointsByPeriod)}   |   {HomeName} {string.Join(" ", r.Home.PointsByPeriod)}";
        Shell.NextGameText = TopBarLine(r.Home.Team == Us, (r.Home.Team == Us ? r.Away : r.Home).Team.Abbreviation, breakText == "HALFTIME" ? "Halftime" : _live.PeriodText, _live.Complete,
            r.Home.Team == Us ? HomeScore : AwayScore, r.Home.Team == Us ? AwayScore : HomeScore);
        // H106 (run 112): with FINAL on screen the bar said "1-0 · Final: L 123-129"; the record counts this game already.
        if (_live.Complete && Session.Employed && Session.LiveGameCountsInRecord)
            Shell.RecordText = RecordAfter(Session.MyRecord.Wins, Session.MyRecord.Losses, (r.Home.Team == Us ? HomeScore : AwayScore) > (r.Home.Team == Us ? AwayScore : HomeScore));

        var ourBox = r.Home.Team == Us ? r.Home : r.Away;
        var theirBox = ourBox == r.Home ? r.Away : r.Home;
        var ourFloor = (r.Home.Team == Us ? _live.HomeOnCourt : _live.AwayOnCourt).ToHashSet();
        foreach (var row in OurPlayers)
        {
            var l = ourBox.LineFor(row.Player);
            row.OnCourt = ourFloor.Contains(row.Player);
            row.Line = $"{l.Minutes:0} min · {l.Points} pts · {l.Rebounds} reb · {l.Assists} ast · {l.Fouls} pf{(l.FouledOut ? " · OUT" : "")}";
        }
        var theirFloor = r.Home.Team == Us ? _live.AwayOnCourt : _live.HomeOnCourt;
        TheirFloor.Clear();
        foreach (var p in theirFloor)
        {
            var l = theirBox.LineFor(p);
            TheirFloor.Add(new OnCourtRow { Player = p, OnCourt = true, Line = $"{l.Points} pts · {l.Rebounds} reb · {l.Assists} ast · {l.Fouls} pf" });
        }
        // Keep the lineup pickers in sync with who is actually out there (H55, run 86): after every play, Step, Sim to end
        // and the final, unless the user has changed a picker since the last sync (while paused) or an applied five is
        // still waiting for the next dead ball.
        if (ShouldSyncLineup(Running, _live.Complete, _live.LineupPending(Us), LineupEdited))
            SyncLineup(ourFloor.OrderBy(p => p.PrimaryPosition).Take(5).ToList());

        var log = _live.Log;
        for (; _logShown < log.Count; _logShown++) Log.Insert(0, log[_logShown]);

        // Game flow: chart, quarter splits, shots and run alerts.
        FlowVersion++;
        WinChance = GameFlowInfo.WinChance(r);
        Splits = GameFlowInfo.Splits(r);
        AwayZoneAttempts = ZoneSum(r.Away, l => l.ZoneAttempts); AwayZoneMakes = ZoneSum(r.Away, l => l.ZoneMakes);
        HomeZoneAttempts = ZoneSum(r.Home, l => l.ZoneAttempts); HomeZoneMakes = ZoneSum(r.Home, l => l.ZoneMakes);
        // The court's shot dots come from the engine's plays (run 80), in order, so each dot sits where the tracker
        // drew that shot.
        int before = _shots.Count;
        var plays = r.Plays;
        for (; _shotPlaysSeen < plays.Count; _shotPlaysSeen++)
            if (plays[_shotPlaysSeen] is { Kind: PlayKind.Shot } sp)
                _shots.Add(new Hardwood.Desktop.Controls.LiveShot(sp.Home, sp.Zone, sp.Made, _shotPlaysSeen));
        if (_shots.Count != before) CourtVersion++;
        UpdateFrame();
        UpdateBox();
        UpdateBoard();
        Highlights = GameHighlights.For(r, final: _live.Complete, max: 4);
        TeamStats = GameFlowInfo.TeamStats(r);
        var facts = GameHighlights.Ticker(r);
        if (++_tickerTicks >= TickerEvery) { _tickerTicks = 0; _tickerIndex++; }
        Ticker = facts.Count == 0 ? "Tip-off: the ticker fills in as the game goes." : facts[_tickerIndex % facts.Count];
        var run = GameFlow.CurrentRun(r);
        RunAlert = run == null ? null : $"{run.Team} on a {run.Score} run";
        // Call each run out in the play by play once, the first time it qualifies.
        if (run != null)
        {
            string key = $"{run.Team}@{run.StartElapsed:0.0}";
            if (key != _lastRunKey) { _lastRunKey = key; Log.Insert(0, $"{_live.PeriodText} {_live.ClockText}  >> {RunAlert}"); }
        }
        while (Log.Count > 300) Log.RemoveAt(Log.Count - 1);
    }

    // What the pickers last showed from the floor (or the five last applied): a picker that differs is the user's edit.
    private readonly Player?[] _lastSynced = new Player?[5];
    public bool LineupEdited => Lineup.Select((s, i) => s.Selected != _lastSynced[i]).Any(d => d);

    /// <summary>H55: the pickers follow the floor while the game runs and at the final; while paused they follow it too,
    /// unless the user has changed one; and never while an applied five is still waiting to go in.</summary>
    public static bool ShouldSyncLineup(bool running, bool complete, bool pending, bool edited) =>
        complete || (!pending && (running || !edited));

    private void SyncLineup(IReadOnlyList<Player> floor)
    {
        for (int i = 0; i < 5; i++)
        {
            var p = i < floor.Count ? floor[i] : null;
            // A player the picker can't list (hurt but dressed) shows as empty, and that isn't counted as an edit.
            if (p != null && !Lineup[i].Options.Contains(p)) p = null;
            if (Lineup[i].Selected != p) Lineup[i].Selected = p;
            _lastSynced[i] = p;
        }
    }

    public static int[] ZoneSum(TeamBox b, Func<PlayerLine, int[]> f)
    {
        var z = new int[5];
        foreach (var l in b.Lines) { var a = f(l); for (int i = 0; i < 5; i++) z[i] += a[i]; }
        return z;
    }

    [RelayCommand]
    public void PlayPause()
    {
        if (Complete) return;
        if (Running) { _timer.Stop(); Running = false; Status = "Paused. Change your lineup or tactics, then press Play."; }
        else { _timer.Start(); Running = true; Status = ""; EndBreakCard(); }
    }

    /// <summary>Skip the ticker to its next fact.</summary>
    [RelayCommand]
    public void NextFact()
    {
        var facts = GameHighlights.Ticker(_live.Result);
        _tickerIndex++; _tickerTicks = 0;
        Ticker = facts.Count == 0 ? Ticker : facts[_tickerIndex % facts.Count];
    }

    [RelayCommand]
    public void CycleSpeed()
    {
        _speed = (_speed + 1) % _speedsMs.Length;
        _timer.Interval = TimeSpan.FromMilliseconds(_speedsMs[_speed]);
        SpeedLabel = _speed switch { 0 => "Slow", 1 => "Normal", _ => "Fast" };
        OnPropertyChanged(nameof(MotionMs));
    }

    [RelayCommand]
    public void ApplyLineup()
    {
        if (Complete) return;
        var five = Lineup.Select(s => s.Selected).Where(p => p != null).Distinct().ToList();
        if (five.Count < 5) { Status = "Pick five different players."; return; }
        // H55: say who can't go in, rather than quietly sending four.
        var ourBox = _live.Result.Home.Team == Us ? _live.Result.Home : _live.Result.Away;
        var out_ = five.Where(p => ourBox.LineFor(p!).FouledOut).ToList();
        if (out_.Count > 0) { Status = $"{string.Join(", ", out_.Select(p => p!.LastName))} fouled out: pick someone else."; return; }
        _live.RequestLineup(Us, five!);
        for (int i = 0; i < 5; i++) _lastSynced[i] = Lineup[i].Selected;
        Status = Running ? "Lineup goes in at the next dead ball." : "Lineup set; it goes in at the next dead ball once you press Play.";
    }

    [RelayCommand]
    public void SimToEnd()
    {
        _timer.Stop(); Running = false;
        _live.Finish();
        RestoreRead();
        Refresh();
        Complete = true;
        Status = $"Final: {AwayName} {AwayScore}, {HomeName} {HomeScore}.";
        ShowBreakCard(final: true);
    }

    [RelayCommand]
    public void Continue()
    {
        _timer.Stop();
        if (!_live.Complete) _live.Finish();
        RestoreRead();
        Session.FinishLiveDay(_live, _played);
        Shell.AfterTick();
        Shell.ShowBoxScore(_live.Result);
    }
}
