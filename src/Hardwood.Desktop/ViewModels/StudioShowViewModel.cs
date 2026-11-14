using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed record StudioVoice(string Name, string Role, string Initials, IBrush Colour, string Bio);
public sealed record StudioSay(string Speaker, IBrush Colour, string Text);
public sealed record StudioCard(string Kicker, string Title, IReadOnlyList<StudioSay> Lines);
public sealed record StudioTape(string Label, string Away, string Home, FontWeight AwayWeight, FontWeight HomeWeight, IBrush AwayBrush, IBrush HomeBrush);
public sealed record StudioImpactRow(string Place, Team Team, string Name, string Record, string Back, string Zone, IBrush ZoneBrush, IBrush Row);
public sealed record StudioImpactTable(string Title, IReadOnlyList<StudioImpactRow> Rows);
public sealed record StudioRace(string Rank, string Name, string Team, string Line, IBrush Row);
public sealed record StudioPickRow(string Name, string Initials, IBrush Colour, Team Pick, string PickText, string Why, string Record);

/// <summary>
/// The pre-game studio show (Danny's design, #446, D53 B; part 1): opens before a game you watch, with the HSN desk,
/// then tips off into the live game. "Skip the show" (remembered in the settings) goes straight to the game.
/// </summary>
public sealed partial class StudioShowViewModel : PageViewModel
{
    private readonly GameSimulator.LiveGame? _live;
    private readonly List<GameResult> _played;
    public StudioShow Show { get; }
    public override string Title => "Pre-game show";

    public StudioShowViewModel(MainViewModel shell, GameSimulator.LiveGame? live, List<GameResult> played, StudioShow? show = null) : base(shell)
    {
        _live = live; _played = played;
        Show = show ?? Session.BuildStudioShow(live!.Result.Home.Team, live.Result.Away.Team);
        // The picks are on the record once the show airs before the game itself.
        if (live != null) Session.LockStudioPicks(Show);
        Build();
    }

    private static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static readonly IBrush Dim = B("#8B96A5"), Plain = B("#E6EDF3"), Good = B("#3FB950"), Row = B("#00000000"), Tonight = B("#2A58A6FF");

    public IBrush AccentBrush => B(Show.Accent);
    public IBrush SecondBrush => B(Show.Second);
    public Color Glow => Color.Parse(Show.Accent);
    public string Network => StudioShow.Network;
    public string ShowName => Show.ShowName;
    public string SetLine => Show.SetLine;
    public string Where => string.Join(" · ", new[] { Show.Date.ToString("dddd, MMMM d"), Show.TipTime, Show.Venue }.Where(s => s != ""));
    public Team AwayTeam => Show.Away;
    public Team HomeTeam => Show.Home;
    public string AwayName => Show.Away.FullName;
    public string HomeName => Show.Home.FullName;
    public string AwayRecord => Show.AwayRecord;
    public string HomeRecord => Show.HomeRecord;
    public string Stakes => Show.Stakes;
    public bool HasStakes => Show.Stakes != "";
    public string Chance => $"{Show.Away.Abbreviation} {(1 - Show.HomeChance) * 100:0}% · {Show.Home.Abbreviation} {Show.HomeChance * 100:0}%";
    public bool CanTipOff => _live != null;

    public List<StudioVoice> Desk { get; private set; } = new();
    public List<StudioCard> Cards { get; private set; } = new();
    public List<StudioTape> Tape { get; private set; } = new();
    public List<StudioImpactTable> Impact { get; private set; } = new();
    public List<StudioSay> ImpactLines { get; private set; } = new();
    public bool HasImpact => Impact.Count > 0 || ImpactLines.Count > 0;
    public bool HasTables => Impact.Count > 0;
    /// <summary>A college night is set on the home program's own court from Danny's pack (run 45).</summary>
    public IBrush? CourtBrush => Graphics.GraphicsPackService.CourtFor(Show.Home) is { } b ? new ImageBrush(b) { Stretch = Stretch.UniformToFill, Opacity = 0.55 } : null;
    public bool HasCourt => CourtBrush != null;
    public string RaceTitle => Show.RaceTitle;
    public List<StudioRace> Race { get; private set; } = new();
    public List<StudioSay> RaceLines { get; private set; } = new();
    public bool HasRace => Race.Count > 0;
    public List<StudioPickRow> Picks { get; private set; } = new();

    public bool SkipShow
    {
        get => !AppSettings.Current.StudioShow;
        set { AppSettings.Current.StudioShow = !value; AppSettings.Current.Save(); OnPropertyChanged(); }
    }

    private void Build()
    {
        var voices = new[] { Show.Host }.Concat(Show.Panel).ToList();
        Desk = voices.Select(a => new StudioVoice(a.Name, a.Role, a.Initials, B(a.Colour), a.Bio)).ToList();
        StudioSay Say(StudioLine l) => new(l.Speaker.Name, B(l.Speaker.Colour), l.Text);
        Cards = Show.Segments.Select(s => new StudioCard(s.Kicker, s.Title, s.Lines.Select(Say).ToList())).ToList();
        Tape = Show.Tape.Select(t => new StudioTape(t.Label, t.Away, t.Home,
            t.Edge < 0 ? FontWeight.Bold : FontWeight.Normal, t.Edge > 0 ? FontWeight.Bold : FontWeight.Normal,
            t.Edge < 0 ? Good : Plain, t.Edge > 0 ? Good : Plain)).ToList();
        Impact = Show.Impact.Select(t => new StudioImpactTable(t.Title, t.Rows.Select(r => new StudioImpactRow(r.Place.ToString(), r.Team, r.Team.FullName, r.Record, r.Back, r.Zone,
            r.Zone switch { "Playoffs" => B("#3FB950"), "Promotion" => B("#58A6FF"), "Third place" => B("#E3B341"), "Relegation" => B("#F85149"), _ => Dim },
            r.Tonight ? Tonight : Row)).ToList())).ToList();
        ImpactLines = Show.ImpactLines.Select(Say).ToList();
        Race = Show.Race.Select(r => new StudioRace(r.Rank.ToString(), r.Name, r.Team, r.Line, r.Tonight ? Tonight : Row)).ToList();
        RaceLines = Show.RaceLines.Select(Say).ToList();
        Picks = Show.Picks.Select(p => new StudioPickRow(p.Analyst.Name, p.Analyst.Initials, B(p.Analyst.Colour), p.Pick, $"{p.Pick.Abbreviation} by {p.Margin}", p.Why,
            p.Wins + p.Losses == 0 ? "First pick of the season" : $"Season record on your games: {p.Record}")).ToList();
    }

    public override void Refresh() { }

    [RelayCommand]
    public void TipOff()
    {
        if (_live == null) { Shell.GoBack(); return; }
        Shell.CurrentPage = new LiveGameViewModel(Shell, _live, _played);
    }

    /// <summary>Play the game without watching it and go to the box score.</summary>
    [RelayCommand]
    public void SimGame()
    {
        if (_live == null) { Shell.GoBack(); return; }
        _live.Finish();
        Session.FinishLiveDay(_live, _played);
        Shell.AfterTick();
        Shell.ShowBoxScore(_live.Result);
    }
}
