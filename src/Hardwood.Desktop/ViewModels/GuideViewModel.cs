using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Guide;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed partial class GuideTaskRow : ObservableObject
{
    public required GuideTask Task { get; init; }
    public string Title => Task.Title;
    public string Detail => Task.Detail;
    public bool Urgent => Task.Priority < 30;
    public string GoText => Task.Page == null ? "Continue" : $"Open {Task.Page}";
    public required IRelayCommand Go { get; init; }
    public required IRelayCommand Dismiss { get; init; }
}

/// <summary>
/// The assistant panel (decisions #115-118): a slim panel on the right of every page with what to do next as task
/// cards; the page's tip and strategy sit behind a Tips button (D63 A). Toggled from the top bar or Settings; collapses to a thin strip.
/// </summary>
public sealed partial class GuideViewModel : ObservableObject
{
    private readonly MainViewModel _shell;
    private string _page = "Home";
    private DateOnly? _lastDate;
    private SessionPhase? _lastPhase;

    public GuideViewModel(MainViewModel shell)
    {
        _shell = shell;
        _on = Settings.GuideOn;
        _collapsed = Settings.GuideCollapsed;
        _level = LevelName(Settings.GuideLevel);
    }

    private static AppSettings Settings => AppSettings.Current;

    [ObservableProperty] private bool _on;
    [ObservableProperty] private bool _collapsed;
    [ObservableProperty] private string _headline = "";
    [ObservableProperty] private string _stageText = "";
    [ObservableProperty] private string _pageTitle = "";
    [ObservableProperty] private string _pageTip = "";
    /// <summary>
    /// D63 A (#634): the panel shows only its task cards (and the last game's card). The page's tip and the strategy
    /// list sit behind one Tips button, closed by default; it stays as you left it while you move between pages.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TipsButtonText), nameof(ShowPageTip), nameof(ShowStrategy))]
    private bool _tipsOpen;
    [RelayCommand] private void ToggleTips() => TipsOpen = !TipsOpen;
    public bool ShowPageTip => TipsOpen && PageTip.Length > 0;
    public bool ShowStrategy => TipsOpen && HasStrategy;
    public bool HasTips => PageTip.Length > 0 || HasStrategy;
    public string TipsButtonText => TipsOpen ? "Hide tips" : TipCount == 1 ? "Tips (1)" : $"Tips ({TipCount})";
    private int TipCount => (PageTip.Length > 0 ? 1 : 0) + Strategy.Count;
    /// <summary>No task cards: say so, so the panel never looks empty or broken.</summary>
    [ObservableProperty] private bool _noTasks;
    [ObservableProperty] private bool _hasStrategy;
    [ObservableProperty] private string _level;
    public ObservableCollection<GuideTaskRow> Tasks { get; } = new();
    public ObservableCollection<string> Strategy { get; } = new();
    public ObservableCollection<string> LastGameWhy { get; } = new();
    [ObservableProperty] private string _lastGameTitle = "";
    [ObservableProperty] private bool _hasLastGame;
    // Pages with wide tables fold the panel to its strip unless you open it there (critic review #2: the transfer
    // market hid its CA, age and price columns behind the panel).
    private static readonly HashSet<string> WidePages = new() { "Transactions", "Roster", "Leagues", "League Leaders", "Tactics", "Offseason" };
    private bool _wide, _openedHere;
    /// <summary>H97 (run 116): at big text (120%+) the live court and the pre-game show need the room, so the panel folds
    /// to its strip for the live game (one click opens it, as on the wide pages). At normal text it stays open.</summary>
    public static bool FoldsInLiveGame(double scale) => UiScale.Clamp(scale) >= Views.MainWindow.BigTextScale;
    private bool _liveFold;
    /// <summary>The shell's live game started (true) or ended (false).</summary>
    public void LiveGame(bool live, double scale)
    {
        bool fold = live && FoldsInLiveGame(scale);
        if (fold == _liveFold) return;
        _liveFold = fold;
        if (fold) _openedHere = false;
        Notify();
    }
    public bool Expanded => On && !Collapsed && (!(_wide || _liveFold) || _openedHere);
    public bool ShowStrip => On && !Expanded;

    public static readonly List<string> Levels = new() { "Grows with me", "Beginner", "Experienced", "Strategy only" };
    public List<string> LevelChoices => Levels;
    private static string LevelName(GuideLevel l) => Levels[(int)l];

    partial void OnOnChanged(bool value) { Settings.GuideOn = value; Settings.Save(); OnPropertyChanged(nameof(Expanded)); OnPropertyChanged(nameof(ShowStrip)); Refresh(); }
    partial void OnCollapsedChanged(bool value) { Settings.GuideCollapsed = value; Settings.Save(); OnPropertyChanged(nameof(Expanded)); OnPropertyChanged(nameof(ShowStrip)); }
    partial void OnLevelChanged(string value)
    {
        int i = Levels.IndexOf(value);
        if (i < 0) return;
        Settings.GuideLevel = (GuideLevel)i; Settings.Save(); Refresh();
    }

    [RelayCommand] private void Toggle() => On = !On;
    [RelayCommand]
    private void Collapse()
    {
        if ((_wide || _liveFold) && _openedHere) { _openedHere = false; Notify(); return; }
        Collapsed = true;
    }
    [RelayCommand]
    private void Expand()
    {
        if (_wide || _liveFold) _openedHere = true;
        Collapsed = false;
        Notify();
    }

    private void Notify() { OnPropertyChanged(nameof(Expanded)); OnPropertyChanged(nameof(ShowStrip)); }
    [RelayCommand]
    private void ResetDismissed()
    {
        int n = Settings.GuideProgress.Dismissed.Count;
        Settings.GuideProgress.Dismissed.Clear(); Settings.Save(); Refresh();
        // Review #14 bug #8: a line saying what happened.
        ResetNote = (n == 0 ? "No tips were hidden: every tip already shows" : n == 1 ? "1 hidden tip will show again" : $"{n} hidden tips will show again") + $" ({DateTime.Now:h:mm:ss tt}).";
    }
    [ObservableProperty] private string _resetNote = "";

    /// <summary>A career was opened: start counting days from its date.</summary>
    public void NewSession() { _lastDate = null; _lastPhase = null; }

    /// <summary>The page you're looking at (for its tip and the first-steps checklist).</summary>
    public void OnPage(string page)
    {
        _page = page;
        _wide = WidePages.Contains(page); _openedHere = false;
        Notify();
        if (Settings.GuideProgress.PagesSeen.Add(page)) Settings.Save();
        Refresh();
    }

    /// <summary>After time passes: count days and finished seasons (Auto level grows with them), then refresh.</summary>
    public void OnTick()
    {
        if (_shell.Session is not { } s) return;
        var prog = Settings.GuideProgress;
        bool changed = false;
        if (_lastDate is { } last && s.Today > last) { prog.DaysPlayed += Math.Min(400, s.Today.DayNumber - last.DayNumber); changed = true; }
        if (_lastPhase is { } ph && ph != SessionPhase.Offseason && s.Phase == SessionPhase.Offseason) { prog.SeasonsPlayed++; changed = true; }
        _lastDate = s.Today; _lastPhase = s.Phase;
        Settings.Save(); // days, seasons and strategy-note counts
        Refresh();
    }

    public void Refresh()
    {
        if (!On || _shell.Session is not { } s) return;
        _lastDate ??= s.Today; _lastPhase ??= s.Phase;
        var advice = Assistant.Advise(s, _page, Settings.GuideLevel, Settings.GuideProgress);
        Headline = advice.Headline;
        StageText = advice.Stage switch { GuideStage.Beginner => "Learning the ropes", GuideStage.Experienced => "Settling in", _ => "Strategy" };
        PageTitle = advice.PageTitle;
        PageTip = advice.PageTip;
        Tasks.Clear();
        foreach (var t in advice.Tasks.Take(6))
            Tasks.Add(new GuideTaskRow
            {
                Task = t,
                Go = new RelayCommand(() => Go(t)),
                Dismiss = new RelayCommand(() => { Settings.GuideProgress.Dismissed.Add(t.Key); Settings.Save(); Refresh(); }),
            });
        LastGameTitle = advice.LastGameTitle;
        LastGameWhy.Clear();
        foreach (var w in advice.LastGameWhy ?? Array.Empty<string>()) LastGameWhy.Add(w);
        HasLastGame = LastGameWhy.Count > 0;
        Strategy.Clear();
        foreach (var tip in advice.Strategy.Take(4)) Strategy.Add(tip);

        HasStrategy = Strategy.Count > 0;
        NoTasks = Tasks.Count == 0;
        OnPropertyChanged(nameof(ShowPageTip)); OnPropertyChanged(nameof(ShowStrategy));
        OnPropertyChanged(nameof(HasTips)); OnPropertyChanged(nameof(TipsButtonText));
    }

    private void Go(GuideTask t)
    {
        if (t.Page == null) _ = _shell.ContinueCommand.ExecuteAsync(null);
        else _shell.Navigate(t.Page);
    }
}
