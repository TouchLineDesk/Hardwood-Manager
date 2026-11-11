using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public abstract class ViewModelBase : ObservableObject { }

/// <summary>A page inside the shell. Refresh() is called after every simulated day.</summary>
public abstract class PageViewModel : ViewModelBase
{
    public MainViewModel Shell { get; }
    public GameSession Session => Shell.Session!;
    public abstract string Title { get; }
    protected PageViewModel(MainViewModel shell) => Shell = shell;
    public abstract void Refresh();
}

public sealed partial class NavItem : ObservableObject
{
    public required string Name { get; init; }
    public required Func<PageViewModel> Factory { get; init; }
    /// <summary>Lucide icon name (see Controls.IconData).</summary>
    public string Icon { get; init; } = "";
    public IRelayCommand? Command { get; set; }
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _badge = "";
    /// <summary>Hidden when the page doesn't apply (a college coach has no transfers or pro offseason, C2).</summary>
    [ObservableProperty] private bool _isVisible = true;
}

public sealed partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] private GameSession? _session;
    [ObservableProperty] private ViewModelBase? _currentPage;
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _teamText = "";
    [ObservableProperty] private string _recordText = "";
    [ObservableProperty] private string _phaseText = "";
    [ObservableProperty] private string _continueText = "Continue";
    [ObservableProperty] private string _nextGameText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasSession;
    [ObservableProperty] private bool _inSeason;
    /// <summary>Watch the human team's games live instead of simming them instantly.</summary>
    [ObservableProperty] private bool _liveGames = true;
    [ObservableProperty] private bool _inLiveGame;
    /// <summary>Progress of the automatic portrait drawing (empty when up to date).</summary>
    [ObservableProperty] private string _portraitStatus = "";
    public bool ShowSimButtons => InSeason && !InLiveGame;
    partial void OnInSeasonChanged(bool value) => OnPropertyChanged(nameof(ShowSimButtons));
    partial void OnInLiveGameChanged(bool value) => OnPropertyChanged(nameof(ShowSimButtons));

    public ObservableCollection<NavItem> Nav { get; } = new();
    /// <summary>The assistant panel (decisions #115-118).</summary>
    public GuideViewModel Guide { get; }
    private readonly Dictionary<string, PageViewModel> _pages = new();

    public MainViewModel()
    {
        Guide = new GuideViewModel(this);
        CurrentPage = new StartViewModel(this);
        Portraits.PortraitService.Instance.StatusChanged += () => PortraitStatus = Portraits.PortraitService.Instance.Status;
    }

    /// <summary>Bring player card portraits up to date (cheap when nothing changed). Skipped while a sim runs on another thread.</summary>
    public void RequestPortraits()
    {
        if (IsBusy) return;
        Portraits.PortraitService.Instance.Request(Session);
    }

    public void AttachSession(GameSession session)
    {
        Session = session;
        HasSession = true;
        Controls.Portraits.UseSave(session.Save?.Path);
        Graphics.LogoPackService.UseTeams(session.League.Teams.Concat(session.World.Clubs));
        Portraits.PortraitService.Instance.Request(session);
        _pages.Clear();
        Nav.Clear();
        foreach (var (name, icon, factory) in new (string, string, Func<PageViewModel>)[]
        {
            ("Home", "LayoutDashboard", () => new HomeViewModel(this)),
            ("Inbox", "Inbox", () => new InboxViewModel(this)),
            ("Roster", "Users", () => new RosterViewModel(this)),
            ("Rotation", "Repeat", () => new RotationViewModel(this)),
            ("Tactics", "ClipboardList", () => new TacticsViewModel(this)),
            ("Squad", "HeartHandshake", () => new SquadViewModel(this)),
            ("Board", "Shield", () => new BoardViewModel(this)),
            ("Staff & Training", "Dumbbell", () => new StaffViewModel(this)),
            ("Schedule & Results", "CalendarDays", () => new ScheduleViewModel(this)),
            ("Standings", "ListOrdered", () => new StandingsViewModel(this)),
            ("Leagues", "Globe", () => new WorldViewModel(this)),
            ("College", "GraduationCap", () => new CollegeViewModel(this)),
            ("League Leaders", "ChartColumnIncreasing", () => new LeadersViewModel(this)),
            ("Transactions", "ArrowLeftRight", () => new TransactionsViewModel(this)),
            ("Offseason", "Sun", () => new OffseasonViewModel(this)),
            ("Awards", "Award", () => new AwardsViewModel(this)),
            ("History", "History", () => new HistoryViewModel(this)),
            ("Settings", "Settings", () => new SettingsViewModel(this)),
        })
        {
            var item = new NavItem { Name = name, Icon = icon, Factory = factory };
            item.Command = new RelayCommand(() => Navigate(item.Name));
            Nav.Add(item);
        }
        ThemeService.Apply(session.HumanTeam);
        Guide.NewSession();
        Navigate("Home");
        RefreshHeader();
    }

    public PageViewModel Page(string name)
    {
        if (!_pages.TryGetValue(name, out var page))
        {
            page = Nav.First(n => n.Name == name).Factory();
            _pages[name] = page;
            page.Refresh();
        }
        return page;
    }

    [RelayCommand]
    public void Navigate(string name)
    {
        if (InLiveGame) return; // finish the game first
        Controls.Portraits.Reload(); // pick up portraits rebuilt on disk
        RequestPortraits();
        var page = Page(name);
        page.Refresh();
        _back.Clear();
        CurrentPage = page;
        foreach (var n in Nav) n.IsActive = n.Name == name;
        Guide.OnPage(name);
    }

    // Pages opened from a link (player, team, box score) remember where you came from, so Back returns there.
    private readonly Stack<ViewModelBase> _back = new();

    private void Open(PageViewModel vm)
    {
        if (CurrentPage != null && !InLiveGame) _back.Push(CurrentPage);
        if (_back.Count > 30) { var keep = _back.Take(30).Reverse().ToList(); _back.Clear(); foreach (var k in keep) _back.Push(k); }
        vm.Refresh();
        CurrentPage = vm;
        foreach (var n in Nav) n.IsActive = false;
    }

    /// <summary>Back to the page before the last link you followed (Home if there's none).</summary>
    public void GoBack()
    {
        if (_back.Count == 0) { Navigate("Home"); return; }
        var prev = _back.Pop();
        if (prev is PageViewModel p) p.Refresh();
        CurrentPage = prev;
        foreach (var n in Nav) n.IsActive = prev is PageViewModel pv && _pages.TryGetValue(n.Name, out var page) && page == pv;
    }

    public void ShowPlayer(Player player) => Open(new PlayerViewModel(this, player));

    /// <summary>Any team's page: NABL, a club abroad, or a college program.</summary>
    public void ShowTeam(Team team) => Open(new TeamViewModel(this, team));

    public void ShowBoxScore(Core.Engine.GameResult result)
    {
        InLiveGame = false;
        Open(new BoxScoreViewModel(this, result));
    }

    [RelayCommand]
    public async Task Continue()
    {
        if (Session == null || IsBusy) return;
        IsBusy = true;
        try
        {
            if (Session.Phase == SessionPhase.Offseason)
            {
                Session.AdvanceOffseason();
                Navigate(Session.Phase == SessionPhase.Offseason ? "Offseason" : "Inbox");
            }
            else if (LiveGames && Session.HumanPlaysToday)
            {
                var (live, played) = await Task.Run(() => Session.BeginLiveDay());
                if (live != null)
                {
                    InLiveGame = true;
                    CurrentPage = new LiveGameViewModel(this, live, played);
                    foreach (var n in Nav) n.IsActive = false;
                    return;
                }
                Session.FinishLiveDay(null, played);
            }
            else
            {
                await Task.Run(() => Session.AdvanceDay());
                if (Session.Phase == SessionPhase.Offseason) Navigate("Offseason");
            }
            AfterTick();
        }
        finally { IsBusy = false; RequestPortraits(); }
    }

    [RelayCommand]
    public async Task SimToNextGame()
    {
        if (Session == null || IsBusy || Session.Phase == SessionPhase.Offseason) return;
        IsBusy = true;
        try
        {
            await Task.Run(() => Session.AdvanceToNextGame());
            if (Session.Phase == SessionPhase.Offseason) Navigate("Offseason");
            AfterTick();
        }
        finally { IsBusy = false; RequestPortraits(); }
    }

    [RelayCommand]
    public async Task SimWeek()
    {
        if (Session == null || IsBusy || Session.Phase == SessionPhase.Offseason) return;
        IsBusy = true;
        try
        {
            await Task.Run(() => { for (int i = 0; i < 7 && Session.Phase != SessionPhase.Offseason; i++) Session.AdvanceDay(); });
            if (Session.Phase == SessionPhase.Offseason) Navigate("Offseason");
            AfterTick();
        }
        finally { IsBusy = false; RequestPortraits(); }
    }

    public void AfterTick()
    {
        foreach (var p in _pages.Values) p.Refresh();
        if (CurrentPage is PageViewModel cp && !_pages.ContainsValue(cp)) cp.Refresh();
        RefreshHeader();
        RequestPortraits();
        Guide.OnTick();
    }

    [ObservableProperty] private string _jobText = "";
    [ObservableProperty] private Avalonia.Media.IBrush _jobBrush = Avalonia.Media.Brushes.Gray;

    [RelayCommand] private void GoBoard() => Navigate("Board");

    public void RefreshHeader()
    {
        if (Session == null) return;
        var s = Session;
        InSeason = s.Phase != SessionPhase.Offseason;
        if (s.Phase == SessionPhase.Offseason) InLiveGame = false;
        DateText = s.Today.ToString("ddd, MMM d yyyy");
        TeamText = s.Employed ? s.HumanTeam.FullName : $"{s.Coach.Name} · out of work";
        var (jobLabel, jobColour) = s.Employed ? BoardViewModel.Mood(s.Job.Confidence) : ("Looking for work", "#8B96A5");
        JobText = s.Employed ? $"{(s.InCollege ? "AD" : "Board")}: {jobLabel}" : jobLabel;
        JobBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(jobColour));
        var rec = s.MyRecord;
        RecordText = rec.Games > 0 ? $"{rec.Wins}-{rec.Losses}" : "0-0";
        PhaseText = s.InCollege && s.Employed && s.World.College is { } cw && cw.Season.Year == s.League.Season
            ? $"{s.MyConference!.Short} · {(cw.Season.ChampionId != null ? "season over" : cw.StageText.ToLower())}"
            : s.Abroad && s.Phase != SessionPhase.Offseason && s.HumanLeague is { } hl
            ? $"{hl.Profile.Name} · {(hl.Season.IsComplete ? "season over" : hl.Season.Stage.ToLower())}"
            : s.Phase switch
        {
            SessionPhase.Preseason => $"Preseason · opens {s.OpeningNight:MMM d}",
            SessionPhase.RegularSeason => $"Regular season {s.League.Season}-{(s.League.Season + 1) % 100:00}",
            SessionPhase.Postseason => $"Playoffs · {s.Postseason?.Stage}",
            SessionPhase.Offseason => $"Offseason · {s.Offseason?.Phase}",
            _ => "",
        };
        ContinueText = s.Phase == SessionPhase.Offseason
            ? s.Offseason?.Phase switch
            {
                OffseasonPhase.ReSigning => s.League.Finance.OpenMarket ? "Continue to free agency" : "Continue to draft",
                OffseasonPhase.Draft => "Continue to free agency",
                OffseasonPhase.FreeAgency => "Start new season",
                _ => "Continue",
            } ?? "Continue"
            : "Continue (1 day)";
        var next = s.NextMyGame;
        NextGameText = s.InNabl && s.Phase == SessionPhase.Postseason && s.HumanSeries is { Complete: false } hs
            ? $"Series vs {(hs.Higher == s.HumanTeam ? hs.Lower : hs.Higher).Abbreviation}: {hs.Score}"
            : next != null ? $"Next: {(next.Home ? "vs" : "at")} {next.Opponent.Abbreviation}, {next.Date:MMM d}"
            : "";
        var inbox = Nav.FirstOrDefault(n => n.Name == "Inbox");
        if (inbox != null) { int unread = s.Inbox.Count(m => !m.Read); inbox.Badge = unread > 0 ? unread.ToString() : ""; }
        bool college = s.Employed && s.InCollege;
        foreach (var item in Nav.Where(n => n.Name is "Transactions" or "Offseason")) item.IsVisible = !college;
        var off = Nav.FirstOrDefault(n => n.Name == "Offseason");
        if (off != null) off.Badge = s.Phase == SessionPhase.Offseason ? "!" : "";
    }

    [RelayCommand]
    public void BackToStart()
    {
        Session = null;
        HasSession = false;
        ThemeService.Apply(null);
        Nav.Clear();
        CurrentPage = new StartViewModel(this);
    }
}
