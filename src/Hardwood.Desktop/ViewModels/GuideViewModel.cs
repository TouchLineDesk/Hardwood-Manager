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
/// The assistant panel (decisions #115-118): a slim panel on the right of every page with what to do next, a tip for
/// the page you're on, and strategy. Toggled from the top bar or Settings; collapses to a thin strip.
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
    [ObservableProperty] private bool _hasStrategy;
    [ObservableProperty] private string _level;
    public ObservableCollection<GuideTaskRow> Tasks { get; } = new();
    public ObservableCollection<string> Strategy { get; } = new();
    public bool Expanded => On && !Collapsed;
    public bool ShowStrip => On && Collapsed;

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
    [RelayCommand] private void Collapse() => Collapsed = true;
    [RelayCommand] private void Expand() => Collapsed = false;
    [RelayCommand] private void ResetDismissed() { Settings.GuideProgress.Dismissed.Clear(); Settings.Save(); Refresh(); }

    /// <summary>A career was opened: start counting days from its date.</summary>
    public void NewSession() { _lastDate = null; _lastPhase = null; }

    /// <summary>The page you're looking at (for its tip and the first-steps checklist).</summary>
    public void OnPage(string page)
    {
        _page = page;
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
        if (changed) Settings.Save();
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
        Strategy.Clear();
        foreach (var tip in advice.Strategy.Take(4)) Strategy.Add(tip);
        HasStrategy = Strategy.Count > 0;
    }

    private void Go(GuideTask t)
    {
        if (t.Page == null) _ = _shell.ContinueCommand.ExecuteAsync(null);
        else _shell.Navigate(t.Page);
    }
}
