using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.ViewModels;

/// <summary>A side of the matchup on the big screen.</summary>
public sealed record ScreenSide(string Seed, Team? Team, string School, string Record, string Tag, bool Mine)
{
    public bool HasTeam => Team != null;
    public bool HasTag => Tag.Length > 0;
}

/// <summary>A line of the board beside the matchup: a pairing of the region (or the First Four), shown once revealed.</summary>
public sealed record ScreenBoardRow(string Top, string Bottom, bool Shown, bool Now, bool Mine)
{
    public double Fade => Shown ? 1.0 : 0.35;
}

/// <summary>
/// The Selection Show (B1, run 139): Selection Sunday on the HSN studio set. The beats come from
/// <see cref="SelectionShow.Build"/>; this keeps the place in the show per season while the game is open, drives
/// Next game / Autorun / Advance, and keeps the region reveal below in step (a region counts as revealed once its last
/// game has been shown).
/// </summary>
public sealed partial class CollegeViewModel
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CollegeSeason, System.Runtime.CompilerServices.StrongBox<int>> ShowAt = new();
    private List<ShowBeat> _beats = new();
    private CollegeSeason? _beatsFor;
    private DispatcherTimer? _autorun;

    [ObservableProperty] private bool _showOn;
    [ObservableProperty] private string _screenSite = "";
    [ObservableProperty] private string _screenRound = "";
    [ObservableProperty] private string _screenWhen = "";
    [ObservableProperty] private ScreenSide? _screenTop;
    [ObservableProperty] private ScreenSide? _screenBottom;
    [ObservableProperty] private bool _screenHasGame;
    [ObservableProperty] private string _screenTitle = "";
    [ObservableProperty] private string _screenBoardTitle = "";
    [ObservableProperty] private List<ScreenBoardRow> _screenBoard = new();
    [ObservableProperty] private string _bubbleText = "";
    [ObservableProperty] private string _bubbleSpeaker = "";
    [ObservableProperty] private bool _bubbleLeft = true;
    [ObservableProperty] private string _showProgress = "";
    [ObservableProperty] private bool _autorunning;
    [ObservableProperty] private bool _canShowNext;
    public bool BubbleRight => !BubbleLeft;
    partial void OnBubbleLeftChanged(bool value) => OnPropertyChanged(nameof(BubbleRight));
    public string AutorunText => Autorunning ? "Stop" : "Autorun";
    partial void OnAutorunningChanged(bool value) => OnPropertyChanged(nameof(AutorunText));
    /// <summary>The region reveal below the show: only once the show is over (or skipped).</summary>
    public bool ShowRegions => ShowSelection && !ShowOn;
    partial void OnShowOnChanged(bool value) => OnPropertyChanged(nameof(ShowRegions));
    partial void OnShowSelectionChanged(bool value) => OnPropertyChanged(nameof(ShowRegions));

    /// <summary>Beats shown so far this season (1 = the open).</summary>
    private int ShowPosition(CollegeSeason s) => ShowAt.GetValue(s, _ => new System.Runtime.CompilerServices.StrongBox<int>(1)).Value;

    private void FillShow(CollegeWorld c)
    {
        var s = c.Season;
        int? me = Session.Employed && Session.InCollege ? Session.HumanTeam.Id : null;
        _beats = SelectionShow.Build(c, me);
        _beatsFor = s;
        bool over = RevealedRegions >= 4 || _beats.Count == 0;
        int at = Math.Min(ShowPosition(s), _beats.Count);
        ShowOn = !over;
        if (!ShowOn) { StopAutorun(); return; }
        var beat = _beats[Math.Max(0, at - 1)];
        ScreenSite = beat.Site;
        ScreenRound = beat.Round;
        ScreenWhen = beat.When;
        ScreenHasGame = beat.IsGame;
        ScreenTitle = beat.Kind switch
        {
            "Open" => "THE FIELD OF 68",
            "Close" => "THE FIELD IS SET",
            _ => "",
        };
        ScreenTop = beat.Top is { } t ? Side(c, t) : null;
        ScreenBottom = beat.Bottom is { } b ? Side(c, b) : null;
        BubbleText = beat.Line;
        BubbleSpeaker = $"{beat.Speaker.Name} · {(beat.Speaker == SelectionShow.Host ? "host" : "analyst")}";
        BubbleLeft = beat.Speaker == SelectionShow.Host;
        ShowProgress = $"{at} of {_beats.Count}";
        CanShowNext = at < _beats.Count;
        FillBoard(c, beat, at);
    }

    private static ScreenSide Side(CollegeWorld c, ShowSide x) =>
        new(x.Seed.ToString(), x.TeamId > 0 ? c.Program(x.TeamId) : null, x.School, x.Record, x.Tag, x.Mine);

    /// <summary>The board beside the matchup: the current region's eight pairings (the First Four's four games), filled as revealed.</summary>
    private void FillBoard(CollegeWorld c, ShowBeat beat, int at)
    {
        string Short(ShowSide x) => x.TeamId > 0 ? $"{x.Seed} {c.Program(x.TeamId)?.Abbreviation}" : $"{x.Seed} PLAY-IN";
        var shownSoFar = _beats.Take(at).ToList();
        if (beat.Kind == "First Four")
        {
            var ff = _beats.Where(x => x.Kind == "First Four").ToList();
            ScreenBoardTitle = "FIRST FOUR · DAYTON";
            ScreenBoard = ff.Select(x => new ScreenBoardRow(Short(x.Top!), Short(x.Bottom!), shownSoFar.Contains(x), x == beat, x.Mine)).ToList();
            return;
        }
        if (beat.Region >= 0)
        {
            var reg = _beats.Where(x => x.Kind == "Game" && x.Region == beat.Region).ToList();
            ScreenBoardTitle = $"{MadnessBracket.Regions[beat.Region].ToUpperInvariant()} REGION";
            ScreenBoard = reg.Select(x => new ScreenBoardRow(Short(x.Top!), Short(x.Bottom!), shownSoFar.Contains(x), x == beat, x.Mine)).ToList();
            return;
        }
        // The open and the close: the No. 1 seeds once they're out.
        ScreenBoardTitle = "THE NO. 1 SEEDS";
        ScreenBoard = _beats.Where(x => x.Kind == "Game" && x.Pair == 0)
            .Select(x => new ScreenBoardRow($"{MadnessBracket.Regions[x.Region]}", shownSoFar.Contains(x) ? $"1 {c.Program(x.Top!.TeamId)?.Abbreviation}" : "?", shownSoFar.Contains(x), false, x.Mine)).ToList();
    }

    [RelayCommand]
    private void ShowNext()
    {
        if (Shell.IsBusy || Session.World.College is not { } c || !c.Season.Selected || !ReferenceEquals(c.Season, _beatsFor)) return;
        var box = ShowAt.GetValue(c.Season, _ => new System.Runtime.CompilerServices.StrongBox<int>(1));
        if (box.Value >= _beats.Count) { ShowAdvance(); return; }
        box.Value++;
        // A region counts as revealed below once its last first-round game has been on the screen.
        var shown = _beats.Take(box.Value).ToList();
        int done = Enumerable.Range(0, 4).Count(r => _beats.Where(x => x.Kind == "Game" && x.Region == r).All(shown.Contains));
        var rev = Revealed.GetValue(c.Season, _ => new System.Runtime.CompilerServices.StrongBox<int>(0));
        // The show's last beat (the close) hands over to the full reveal only on Advance, so the bubble can be read.
        rev.Value = Math.Max(rev.Value, Math.Min(done, 3));
        FillBracket();
        if (box.Value >= _beats.Count) StopAutorun();
    }

    [RelayCommand]
    private void ShowAutorun()
    {
        if (Autorunning) { StopAutorun(); return; }
        _autorun ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2400) };
        _autorun.Tick -= AutorunTick;
        _autorun.Tick += AutorunTick;
        _autorun.Start();
        Autorunning = true;
    }

    private void AutorunTick(object? sender, EventArgs e)
    {
        // Review (run 139): never tick behind the player's back: off this page, while the game is advancing on its
        // worker thread, after a different save was loaded, or once the season has moved on.
        if (!ShowOn || !CanShowNext || Shell.IsBusy || !ReferenceEquals(Shell.CurrentPage, this) || Shell.Session == null
            || Session.World.College is not { } c || !ReferenceEquals(c.Season, _beatsFor)) { StopAutorun(); return; }
        ShowNext();
    }

    private void StopAutorun()
    {
        _autorun?.Stop();
        Autorunning = false;
    }

    /// <summary>Advance: finish the show and show the whole field (the same bracket the College page has always shown).</summary>
    [RelayCommand]
    private void ShowAdvance()
    {
        StopAutorun();
        if (Session.World.College is not { } c || !c.Season.Selected) return;
        ShowAt.GetValue(c.Season, _ => new System.Runtime.CompilerServices.StrongBox<int>(1)).Value = int.MaxValue / 2;
        RevealAll();
    }
}
