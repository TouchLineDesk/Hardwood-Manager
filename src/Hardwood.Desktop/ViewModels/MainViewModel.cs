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
    /// <summary>When the career was last saved (D74 B, #635): the game saves every 7 days, at phase changes and on quit.</summary>
    [ObservableProperty] private string _savedText = "";
    [ObservableProperty] private string _savedTip = "";
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
    public bool ShowSimButtons => InSeason && !InLiveGame && !InCollegeSpring;
    /// <summary>H77 (run 107): a college coach's spring with no games left. Sim week and Next game go; Continue becomes
    /// "Skip to the summer" and plays every day up to the offseason in one press.</summary>
    [ObservableProperty] private bool _inCollegeSpring;
    partial void OnInCollegeSpringChanged(bool value) { OnPropertyChanged(nameof(ShowSimButtons)); OnPropertyChanged(nameof(ShowNextGameButton)); }
    public const string SkipToSummerText = "Skip to the summer";

    /// <summary>H77: a college coach whose season is over (champion crowned, or May) and who has no game left to play.</summary>
    public static bool CollegeSpring(GameSession s) =>
        s.Employed && s.InCollege && s.Phase is SessionPhase.RegularSeason or SessionPhase.Postseason && s.Today.Month is >= 3 and <= 7
        && (s.World.College?.Season.ChampionId != null || s.Today.Month >= 5) && s.NextMyGame == null;
    /// <summary>Next game needs a club (bug hunt H15): out of work, Sim week and Continue still move time on.</summary>
    [ObservableProperty] private bool _employed = true;
    /// <summary>H20 (run 72): out of the postseason there's no next game to sim to, so Next game goes too.</summary>
    [ObservableProperty] private bool _seasonOver;
    public bool ShowNextGameButton => ShowSimButtons && Employed && !SeasonOver;
    partial void OnEmployedChanged(bool value) => OnPropertyChanged(nameof(ShowNextGameButton));
    partial void OnSeasonOverChanged(bool value) => OnPropertyChanged(nameof(ShowNextGameButton));
    partial void OnInSeasonChanged(bool value) { OnPropertyChanged(nameof(ShowSimButtons)); OnPropertyChanged(nameof(ShowNextGameButton)); }
    partial void OnInLiveGameChanged(bool value) { OnPropertyChanged(nameof(ShowSimButtons)); OnPropertyChanged(nameof(ShowNextGameButton)); Guide?.LiveGame(value, UiScale.Current); }

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
        // The top bar's last-save line follows every save, whichever page or thread made it (D74 B).
        session.Saved += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (Session == session) (SavedText, SavedTip) = SaveLine(session); });
        Controls.PlayerCard.BackSource = pl => Hardwood.Core.Season.CollectorCard.BackOf(session, pl);
        Controls.PlayerCard.Today = () => session.Today;
        Controls.PlayerCard.ClassYear = p => session.ClassYearOf(p)?.Short; // CS3 (run 127)
        // The college Top 25 rank travels with the team (#528): logos and team links read it from the live poll.
        Controls.CollegeRank.Of = t => session.World.College is { } cw && t.Id >= Hardwood.Core.College.CollegeWorld.FirstId ? cw.Rank(t.Id) : null;
        Controls.Portraits.UseSave(session.Save?.Path);
        Graphics.LogoPackService.UseTeams(session.League.Teams.Concat(session.World.Clubs).Concat(session.World.College?.Programs ?? new List<Team>()));
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

    /// <summary>
    /// Bug hunt H52 (run 84): the pages that change your own club. Out of work they're hidden from the menu, and a link,
    /// a Home button or Back that leads to one opens the Board page (the open jobs) instead, so nothing you touch there
    /// can change the club you left (its rotation, game plan or training).
    /// H15b (run 93): Squad and Offseason too: out of work Squad's talks only answered "He isn't one of your players" and
    /// Offseason showed three blank tabs under "You aren't coaching a club".
    /// </summary>
    public static readonly IReadOnlySet<string> ClosedOutOfWork = new HashSet<string> { "Transactions", "Rotation", "Tactics", "Staff & Training", "Squad", "Offseason" };

    /// <summary>True when this page is one of <see cref="ClosedOutOfWork"/> and you have no club.</summary>
    public bool IsClosed(string name) => Session is { Employed: false } && ClosedOutOfWork.Contains(name);

    [RelayCommand]
    public void Navigate(string name)
    {
        if (InLiveGame) return; // finish the game first
        if (IsClosed(name)) name = "Board";
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
        if (prev is PageViewModel closed && IsClosed(closed.Title)) { Navigate("Board"); return; }   // H52 (run 84)
        if (prev is PageViewModel p) p.Refresh();
        CurrentPage = prev;
        foreach (var n in Nav) n.IsActive = prev is PageViewModel pv && _pages.TryGetValue(n.Name, out var page) && page == pv;
    }

    public void ShowPlayer(Player player) => Open(new PlayerViewModel(this, player));

    /// <summary>The pre-game show for your next game, opened from Home ahead of the day (#446).</summary>
    public void ShowStudio()
    {
        if (Session?.NextMyGame is not { } next) return;
        var (home, away) = next.Home ? (Session.HumanTeam, next.Opponent) : (next.Opponent, Session.HumanTeam);
        Open(new StudioShowViewModel(this, null, new(), Session.BuildStudioShow(home, away, next.Date)));
    }

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
            if (InCollegeSpring && Session.Phase != SessionPhase.Offseason)
            {
                // H77 (run 107): nothing to coach until the summer, so play the days through in one go.
                var s = Session;
                await Task.Run(() => { for (int i = 0; i < 200 && s.Phase != SessionPhase.Offseason && CollegeSpring(s); i++) s.AdvanceDay(); });
                if (s.Phase == SessionPhase.Offseason) Navigate("Offseason");
            }
            else if (Session.Phase == SessionPhase.Offseason)
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
                    // Danny's pre-game studio show (#446) airs first, unless he's chosen to skip it.
                    CurrentPage = AppSettings.Current.StudioShow ? new StudioShowViewModel(this, live, played) : new LiveGameViewModel(this, live, played);
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
        Controls.CollegeRank.RaiseChanged();
        // H52 (run 84): a page closed out of work isn't refreshed (Rotation's refresh wrote a chart to the old club), and
        // if you were on one when the sack came, you go to the Board page and its open jobs.
        foreach (var (name, p) in _pages) if (!IsClosed(name)) p.Refresh();
        if (CurrentPage is PageViewModel cp && !_pages.ContainsValue(cp)) cp.Refresh();
        if (CurrentPage is PageViewModel open && IsClosed(open.Title) && !InLiveGame) Navigate("Board");
        RefreshHeader();
        RequestPortraits();
        Guide.OnTick();
    }

    [ObservableProperty] private string _jobText = "";
    /// <summary>Your club for the top bar's logo: follows a new job (review #14: it kept the old club's logo).</summary>
    [ObservableProperty] private Team? _headerTeam;
    [ObservableProperty] private Avalonia.Media.IBrush _jobBrush = Avalonia.Media.Brushes.Gray;

    [RelayCommand] private void GoBoard() => Navigate("Board");

    /// <summary>The top bar's phase line during the promotion playoff (H90): "Promotion playoff · v Seattle Highlanders (best of 3, 1-0)".</summary>
    public static string PromotionLine(Hardwood.Core.World.SeriesState ps, GameSession s)
    {
        bool high = ps.HighId == s.HumanTeam.Id;
        var opp = s.FindTeam(high ? ps.LowId : ps.HighId);
        int us = high ? ps.HighWins : ps.LowWins, them = high ? ps.LowWins : ps.HighWins;
        return $"Promotion playoff · v {opp?.FullName ?? "?"} (best of {ps.BestOf}" + (us + them > 0 ? $", {us}-{them})" : ")");
    }

    public void RefreshHeader()
    {
        if (Session == null) return;
        var s = Session;
        InSeason = s.Phase != SessionPhase.Offseason;
        if (s.Phase == SessionPhase.Offseason) InLiveGame = false;
        DateText = s.Today.ToString("ddd, MMM d yyyy");
        (SavedText, SavedTip) = SaveLine(s);
        Employed = s.Employed;
        // Bug hunt H15 (run 71): out of work, the top bar shows no club, record or next game.
        HeaderTeam = s.Employed ? s.HumanTeam : null;
        TeamText = s.Employed ? s.HumanTeam.FullName : $"{s.Coach.Name} · out of work";
        var (jobLabel, jobColour) = s.Employed ? Hardwood.Core.Jobs.Board.Mood(s.Job.Confidence, BoardViewModel.WinningForMood(s)) : ("Looking for work", "#8B96A5");
        JobText = s.Employed ? $"{(s.InCollege ? "AD" : "Board")}: {jobLabel}" : jobLabel;
        JobBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(jobColour));
        var rec = s.MyRecord;
        RecordText = !s.Employed ? "" : rec.Games > 0 ? $"{rec.Wins}-{rec.Losses}" : "0-0";
        var outText = SeasonOverText(s);
        SeasonOver = outText != null;
        InCollegeSpring = CollegeSpring(s);
        PhaseText = s.InCollege && s.Employed && s.World.College is { } cw && cw.Season.Year == s.League.Season
            ? $"{s.MyConference!.Short} · {(cw.Season.ChampionId != null ? "season over" : cw.StageText.ToLower())}"
            : s.Abroad && s.Phase != SessionPhase.Offseason && s.HumanLeague is { } hl
            ? $"{hl.Profile.Name} · {(hl.Season.IsComplete ? "season over" : outText ?? hl.Season.Stage.ToLower())}"
            : s.Phase switch
        {
                        // H96 (run 109): on opening night itself the season isn't still to open.
            SessionPhase.Preseason => s.Today >= s.OpeningNight ? $"Opening night · {s.OpeningNight:MMM d}" : $"Preseason · opens {s.OpeningNight:MMM d}",
            SessionPhase.RegularSeason => $"Regular season {s.League.Season}-{(s.League.Season + 1) % 100:00}",
            // H90 (run 106): a top-league club in the promotion playoff isn't in the playoffs' first round.
            SessionPhase.Postseason when s.Employed && s.InNabl && Hardwood.Core.World.UsaPyramid.PromotionSeriesOf(s.World, s.HumanTeam.Id) is { Complete: false } ps
                => PromotionLine(ps, s),
            SessionPhase.Postseason => outText != null ? $"Playoffs · {outText}" : $"Playoffs · {s.Postseason?.Stage}",
            SessionPhase.Offseason => $"Offseason · {s.Offseason?.Phase}",
            _ => "",
        };
        ContinueText = s.Phase == SessionPhase.Offseason
            ? s.Offseason?.Phase switch
            {
                // H15b (run 93): out of work there's no draft or free agency of yours to continue to.
                OffseasonPhase.ReSigning or OffseasonPhase.Draft when !s.Employed => "Continue the summer",
                OffseasonPhase.ReSigning => s.League.Finance.OpenMarket && s.Offseason?.CollegeDraftOn != true ? "Continue to free agency" : "Continue to the draft",
                OffseasonPhase.Draft => "Continue to free agency",
                OffseasonPhase.FreeAgency => "Start new season",
                _ => "Continue",
            } ?? "Continue"
            : InCollegeSpring ? SkipToSummerText
            : "Continue (1 day)";
        var next = s.NextMyGame;
        NextGameText = !s.Employed ? "" : s.InNabl && s.Phase == SessionPhase.Postseason && s.HumanSeries is { Complete: false } hs
            ? $"Series vs {(hs.Higher == s.HumanTeam ? hs.Lower : hs.Higher).Abbreviation}: {hs.Score}"
            : next != null ? $"Next: {(next.Home ? "vs" : "at")} {next.Opponent.Abbreviation}, {next.Date:MMM d}"
            : "";
        var inbox = Nav.FirstOrDefault(n => n.Name == "Inbox");
        if (inbox != null) { int unread = s.Inbox.Count(m => !m.Read); inbox.Badge = unread > 0 ? unread.ToString() : ""; }
        bool college = s.Employed && s.InCollege;
        foreach (var item in Nav.Where(n => n.Name is "Transactions" or "Offseason")) item.IsVisible = !college;
        // H43 (run 81): out of work there is no roster of yours to sign for, waive from or trade.
        // H52 (run 84): nor a rotation, a game plan or a training programme: those pages wrote to the old club.
        if (!s.Employed) foreach (var item in Nav.Where(n => ClosedOutOfWork.Contains(n.Name))) item.IsVisible = false;
        else foreach (var item in Nav.Where(n => n.Name is "Rotation" or "Tactics" or "Staff & Training" or "Squad")) item.IsVisible = true;
        var off = Nav.FirstOrDefault(n => n.Name == "Offseason");
        if (off != null) off.Badge = s.Phase == SessionPhase.Offseason ? "!" : "";
    }

    /// <summary>
    /// Bug hunt H10 (run 71): in the postseason the top bar showed the league's stage ("conference finals") for a club that
    /// was no longer playing. This is your own line once you're out: "your season is over" (you didn't make it) or
    /// "out in the first round" (you lost a series and have nothing left to play); null while you're still in it.
    /// A pro club only (college has its own line); a series you won with the next round not yet drawn counts as still in.
    /// </summary>
    public static string? SeasonOverText(GameSession s)
    {
        if (!s.Employed || s.InCollege) return null;
        var me = s.HumanTeam;
        if (s.InNabl)
        {
            if (s.Phase != SessionPhase.Postseason || s.Postseason == null) return null;
            if (s.NextMyGame != null) return null;
            var last = s.Postseason.Series.LastOrDefault(x => x.Higher == me || x.Lower == me);
            if (last == null) return "your season is over";
            if (!last.Complete || last.Loser != me) return null;
            // Losing the 7/8 play-in game still leaves the 8th-seed game.
            if (last.Round.Contains("Play-In 7/8")) return null;
            return $"out in the {RoundName(last.Round)}";
        }
        if (s.Phase == SessionPhase.Offseason || s.HumanLeague is not { } hl) return null;
        var cs = hl.Season;
        if (cs.Stage == Hardwood.Core.World.CompetitionSeason.RegularSeason || cs.IsComplete) return null;
        // Still in a continental cup (code review): not over yet.
        if (!s.HumanSeasonOver) return null;
        if (cs.Fixtures.Any(f => !f.Played && (f.HomeId == me.Id || f.AwayId == me.Id))) return null;
        if (cs.Series.Any(x => !x.Complete && (x.HighId == me.Id || x.LowId == me.Id))) return null;
        var mine = cs.Series.Where(x => x.HighId == me.Id || x.LowId == me.Id).ToList();
        if (mine.Count == 0) return "your season is over";
        var lastSeries = mine[^1];
        return lastSeries.LoserId == me.Id ? $"out in the {RoundName(lastSeries.Round)}" : null;
    }

    private static string RoundName(string round)
    {
        var r = round.StartsWith("East ") || round.StartsWith("West ") ? round[5..] : round;
        return r.ToLower();
    }

    /// <summary>The top bar's last-save line and its hover text (D74 B).</summary>
    public static (string text, string tip) SaveLine(GameSession s)
    {
        if (s.Save == null || s.LastSaved == default) return ("Not saved", "This career has no save file.");
        string text = s.LastSaved == s.Today ? "Saved today" : $"Saved {s.LastSaved:MMM d}";
        return (text, $"The game saves every {GameSession.SaveEveryDays} days, when the season moves on (preseason, season, playoffs, each summer step), after your decisions, and when you go to the main menu or close the window. Settings has Save now.");
    }

    /// <summary>Save before leaving the career (main menu or closing the window, D74 B). Not while a day is being played:
    /// the career is then at most a week behind, as after a crash.</summary>
    public bool SaveOnLeave()
    {
        if (Session == null || IsBusy) return false;
        // D75 A (run 69): leaving mid-game, a halftime read you applied was for the second half only: save your own plan.
        if (CurrentPage is LiveGameViewModel live) live.RestoreRead();
        try { Session.SaveNow(); return true; }
        catch (Exception e) { Console.Error.WriteLine($"Save on leave failed: {e.Message}"); return false; }
    }

    [RelayCommand]
    public void BackToStart()
    {
        SaveOnLeave();
        Session = null;
        HasSession = false;
        ThemeService.Apply(null);
        Nav.Clear();
        CurrentPage = new StartViewModel(this);
    }
}
