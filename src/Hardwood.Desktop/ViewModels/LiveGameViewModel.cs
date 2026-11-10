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
    public string Headline => $"{_live.Result.Away.Team.FullName} at {_live.Result.Home.Team.FullName}";
    public string AwayName => _live.Result.Away.Team.Abbreviation;
    public string HomeName => _live.Result.Home.Team.Abbreviation;
    public Team AwayTeam => _live.Result.Away.Team;
    public Team HomeTeam => _live.Result.Home.Team;
    public Avalonia.Media.IBrush AwayBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(AwayTeam.PrimaryColor));
    public Avalonia.Media.IBrush HomeBrush => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(HomeTeam.PrimaryColor));
    public Avalonia.Media.Color HomeColor => ThemeService.AccentFor(HomeTeam);

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
        for (int i = 0; i < steps && !_live.Complete; i++) _live.Step();
        Refresh();
        if (_live.Complete)
        {
            _timer.Stop(); Running = false; Complete = true;
            Status = $"Final: {AwayName} {AwayScore}, {HomeName} {HomeScore}. Continue to record the result and move on.";
        }
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
        while (Log.Count > 300) Log.RemoveAt(Log.Count - 1);
    }

    [RelayCommand]
    public void PlayPause()
    {
        if (Complete) return;
        if (Running) { _timer.Stop(); Running = false; Status = "Paused. Change your lineup or tactics, then press Play."; }
        else { _timer.Start(); Running = true; Status = ""; }
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
