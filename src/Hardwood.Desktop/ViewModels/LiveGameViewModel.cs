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
    private int[] _seenAwayAtt = new int[5], _seenAwayMk = new int[5], _seenHomeAtt = new int[5], _seenHomeMk = new int[5];
    public IReadOnlyList<Hardwood.Desktop.Controls.LiveShot> CourtShots => _shots;
    [ObservableProperty] private int _courtVersion;
    /// <summary>The live panel shows the court (default) or the game flow chart.</summary>
    [ObservableProperty] private bool _showCourt = true;
    public bool ShowFlow => !ShowCourt;
    [RelayCommand] private void ToCourt() => ShowCourt = true;
    [RelayCommand] private void ToFlow() => ShowCourt = false;
    partial void OnShowCourtChanged(bool value) => OnPropertyChanged(nameof(ShowFlow));
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
    public string CourtLegend => $"{AwayName} shoot left, {HomeName} shoot right · dot: made · ring: missed · gold ring: the latest shot";

    private static Avalonia.Media.Color Distinct(Team t, Avalonia.Media.Color other)
    {
        var c = Avalonia.Media.Color.Parse(t.PrimaryColor);
        int d = Math.Abs(c.R - other.R) + Math.Abs(c.G - other.G) + Math.Abs(c.B - other.B);
        return d >= 120 ? c : Avalonia.Media.Color.Parse(t.SecondaryColor);
    }

    private void AddShots(bool home, int[] att, int[] mk, ref int[] seenAtt, ref int[] seenMk)
    {
        for (int z = 0; z < 5; z++)
        {
            int newMk = Math.Max(0, mk[z] - seenMk[z]), newAtt = Math.Max(0, att[z] - seenAtt[z]);
            for (int i = 0; i < newAtt; i++)
                _shots.Add(new Hardwood.Desktop.Controls.LiveShot(home, (ShotZone)z, i < newMk, _shots.Count));
        }
        seenAtt = (int[])att.Clone(); seenMk = (int[])mk.Clone();
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
        Refresh();
        Status = "Press Play to tip off. Pause at any time to change your five or your tactics.";
    }

    partial void OnRunningChanged(bool value) { OnPropertyChanged(nameof(PlayPauseText)); OnPropertyChanged(nameof(PlayPauseIcon)); }
    partial void OnCompleteChanged(bool value) => OnPropertyChanged(nameof(PlayPauseText));

    private void Tick()
    {
        // A few possessions per tick at the faster speeds keeps the log readable without being sluggish.
        int steps = _speed == 2 ? 3 : 1;
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
        chips.Add(new("Win chance", WinChance, final ? "" : "heading into the next period", "info"));
        if (!final)
        {
            // The in-game read (#268): what the other bench is really running, and the assistant's counter.
            var them = r.Home.Team == Us ? r.Away.Team : r.Home.Team;
            var (cs, cd) = Hardwood.Core.Engine.CoachAi.Counter(Hardwood.Core.Engine.CoachAi.Rotation(Us, null), them.Tactic.System, them.Tactic.Scheme);
            bool already = cs == Us.Tactic.System && cd == Us.Tactic.Scheme;
            string N(Hardwood.Core.Model.OffensiveSystem x) => Hardwood.Core.Model.Systems.Name(x).ToLower();
            string D(Hardwood.Core.Model.DefensiveScheme x) => Hardwood.Core.Model.Systems.Name(x).ToLower();
            chips.Add(new($"{them.Abbreviation} tactics", already ? "Countered" : "Switch?",
                $"They run {N(them.Tactic.System)} and {D(them.Tactic.Scheme)}. " + (already ? "You're in the counter." : $"Counter: {N(cs)} and {D(cd)}."),
                already ? "good" : "warn"));
        }
        if (final)
            foreach (var h in Hardwood.Core.Engine.GameHighlights.For(r, final: true, max: 3))
                chips.Add(new(h.Title, h.Value, h.Detail, h.Tone));
        BreakChips = chips;
        ShowBreak = true;
    }

    public override void Refresh()
    {
        AwayScore = _live.AwayScore; HomeScore = _live.HomeScore;
        Clock = $"{_live.PeriodText}  {_live.ClockText}";
        var r = _live.Result;
        PeriodLine = $"{AwayName} {string.Join(" ", r.Away.PointsByPeriod)}   |   {HomeName} {string.Join(" ", r.Home.PointsByPeriod)}";

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
        // Keep the lineup pickers in sync with who is actually out there unless the user is editing while paused.
        if (Running || Lineup.All(s => s.Selected == null))
        {
            int i = 0;
            foreach (var p in ourFloor.OrderBy(p => p.PrimaryPosition)) { if (i < 5) Lineup[i++].Selected = p; }
        }

        var log = _live.Log;
        for (; _logShown < log.Count; _logShown++) Log.Insert(0, log[_logShown]);

        // Game flow: chart, quarter splits, shots and run alerts.
        FlowVersion++;
        WinChance = GameFlowInfo.WinChance(r);
        Splits = GameFlowInfo.Splits(r);
        AwayZoneAttempts = ZoneSum(r.Away, l => l.ZoneAttempts); AwayZoneMakes = ZoneSum(r.Away, l => l.ZoneMakes);
        HomeZoneAttempts = ZoneSum(r.Home, l => l.ZoneAttempts); HomeZoneMakes = ZoneSum(r.Home, l => l.ZoneMakes);
        int before = _shots.Count;
        AddShots(false, AwayZoneAttempts, AwayZoneMakes, ref _seenAwayAtt, ref _seenAwayMk);
        AddShots(true, HomeZoneAttempts, HomeZoneMakes, ref _seenHomeAtt, ref _seenHomeMk);
        if (_shots.Count != before) CourtVersion++;
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
        else { _timer.Start(); Running = true; Status = ""; ShowBreak = false; }
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
    }

    [RelayCommand]
    public void ApplyLineup()
    {
        var five = Lineup.Select(s => s.Selected).Where(p => p != null).Distinct().ToList();
        if (five.Count < 5) { Status = "Pick five different players."; return; }
        _live.RequestLineup(Us, five!);
        Status = Running ? "Lineup goes in at the next dead ball." : "Lineup set; it goes in at the next dead ball once you press Play.";
    }

    [RelayCommand]
    public void SimToEnd()
    {
        _timer.Stop(); Running = false;
        _live.Finish();
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
        Session.FinishLiveDay(_live, _played);
        Shell.AfterTick();
        Shell.ShowBoxScore(_live.Result);
    }
}
