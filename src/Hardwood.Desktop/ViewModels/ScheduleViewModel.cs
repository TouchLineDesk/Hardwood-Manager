using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hardwood.Core.Engine;
using Hardwood.Core.Season;

namespace Hardwood.Desktop.ViewModels;

public sealed class GameRow
{
    public required MyGame Game { get; init; }
    public GameResult? Result => Game.Result;
    public string Date => Game.Date.ToString("MMM d");
    public string Opponent { get; init; } = "";
    public Hardwood.Core.Model.Team? OpponentTeam { get; init; }
    public string Competition { get; init; } = "";
    public string Score { get; init; } = "";
    public string Outcome { get; init; } = "";
    public string ResultText => Outcome.Length > 0 ? $"{Outcome} {Score}" : Score;
    public string Record { get; init; } = "";
    public string TopPerformer { get; init; } = "";
    /// <summary>#529: your record against this opponent since the career began, as of today.</summary>
    public string HeadToHead { get; init; } = "";
    public bool Played => Game.Played;
    public bool HasBox => Result != null && Result.Home.Lines.Count > 0;
    /// <summary>H8 (run 65): played before you took the job, so only the score is kept.</summary>
    public bool NotCoached => Game.NotCoached;

    // Run 72 (review #13 rank 9): the game as a scoreboard tile: a W/L stripe, the opponent's logo, the score big.
    /// <summary>"W", "L", "next" (your next game) or "later" (to come): picks the tile's stripe colour.</summary>
    public string Kind { get; init; } = "later";
    public bool IsNext => Kind == "next";
    /// <summary>"vs Boston Celtics" / "at #5 Duke": the opponent alone, without the event.</summary>
    public string TileOpponent { get; init; } = "";
    /// <summary>"Sat, Nov 22 · home", then the event when it isn't a league game.</summary>
    public string TileWhen => $"{Game.Date:ddd, MMM d} · {(Game.Home ? "home" : "away")}";
    /// <summary>C5 part 1 (run 94): "RIVALRY" / "IN-STATE", else "#5 v #9" for two ranked sides; empty otherwise.</summary>
    public string Tag => Game.Rivalry?.Tag ?? (Game.OurRank is { } a && Game.OpponentRank is { } b ? $"#{a} v #{b}" : "");
    public bool HasTag => Tag.Length > 0;
    public string TagTip => Game.Rivalry is { } r ? $"{r.Title}: {(r.InState ? "in-state conference rivals" : "a real college rivalry")}"
        : HasTag ? "Both sides ranked in the Top 25 at tip-off" : "";
    public bool HasEvent => Competition.Length > 0;
    /// <summary>The big line: "W 112-104" for a result, "Next game" or "To come".</summary>
    public string TileScore => Played ? ResultText : IsNext ? "Next game" : "To come";
    /// <summary>The small line: the top performer for a result; the head-to-head going in for one to come.</summary>
    public string TileFoot => Played ? (TopPerformer.Length > 0 ? TopPerformer : "Score only")
        : HeadToHead is "–" or "" ? "No meetings yet" : $"Head-to-head {HeadToHead} going in";
}

/// <summary>Run 72: one box of the form strip (the last ten results in the games shown).</summary>
public sealed class FormBox
{
    public required string Letter { get; init; }
    public bool Won => Letter == "W";
    public string Tip { get; init; } = "";
}

/// <summary>A Schedule &amp; Results filter chip (#529): its name, how many games and the record in them.</summary>
public sealed class FilterChip
{
    public required string Name { get; init; }
    public string Record { get; init; } = "";
    public bool Selected { get; init; }
    public string Tip { get; init; } = "";
}

public sealed class SeriesRow
{
    public Hardwood.Core.Model.Team? High { get; init; }
    public Hardwood.Core.Model.Team? Low { get; init; }
    public required string Round { get; init; }
    public required string Matchup { get; init; }
    public required string Score { get; init; }
    public bool Involved { get; init; }
}

public sealed partial class ScheduleViewModel : PageViewModel
{
    public override string Title => "Schedule & Results";
    public ObservableCollection<GameRow> Games { get; } = new();
    public ObservableCollection<SeriesRow> Series { get; } = new();
    public ObservableCollection<string> LeagueScores { get; } = new();
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanOpenBox)), NotifyPropertyChangedFor(nameof(OpenBoxTip))] private GameRow? _selected;
    /// <summary>The hover line on Open box score: why it's greyed out for the game picked (H8, run 65).</summary>
    public string OpenBoxTip => Selected switch
    {
        { NotCoached: true } => Session.Employed ? NotCoachedTip : OutOfWorkTip,
        { HasBox: true } => "Open this game's box score (or double-click it)",
        _ => "Pick a game that has been played (or double-click one)",
    };
    public const string NotCoachedTip = "Played before you took the job: the score is kept, but box scores only for the games you coached.";
    public const string NotCoachedText = "Before you took over (score only)";
    public const string OutOfWorkTip = "Played while you were out of work: the score is kept, but box scores only for the games you coached.";
    public const string OutOfWorkText = "Not coached by you (score only)";
    /// <summary>Open box score is greyed out until a played game is picked (D71 A, bug #5).</summary>
    public bool CanOpenBox => Selected is { Result: not null, HasBox: true };
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _showPlayoffs;
    /// <summary>College (C2): the event gets its own column and opponents go by their school's name, so long names fit.</summary>
    [ObservableProperty] private bool _showEvent;

    // Danny's #529: filter by the season's parts, each tournament and the playoffs; the head-to-head since the career began.
    public ObservableCollection<FilterChip> Filters { get; } = new();
    [ObservableProperty] private string _filter = ScheduleFilter.All;
    [ObservableProperty] private string _filterSummary = "";
    [ObservableProperty] private bool _hasRival;
    [ObservableProperty] private Hardwood.Core.Model.Team? _rivalTeam;
    [ObservableProperty] private string _rivalName = "";
    [ObservableProperty] private string _rivalRecord = "";
    [ObservableProperty] private string _rivalPoints = "";
    [ObservableProperty] private string _rivalLast = "";
    [ObservableProperty] private string _rivalSince = "";
    [ObservableProperty] private bool _hasMeetings;
    /// <summary>C5 part 1: "Crosstown Shootout · You lead the series 2-1 since 2026-27" for a rival; empty otherwise.</summary>
    [ObservableProperty] private string _rivalryLine = "";
    public ObservableCollection<string> RivalMeetings { get; } = new();
    private List<MyGame> _all = new();

    // Run 72 (review #13 rank 9): the games as scoreboard tiles with a form strip first; the table one click away.
    public string[] GameViews { get; } = { "Cards", "Full table" };
    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowCards), nameof(ShowTable))] private string _gameView = "Cards";
    public bool ShowCards => GameView == "Cards";
    public bool ShowTable => !ShowCards;
    /// <summary>The last ten results in the games shown, oldest first.</summary>
    public ObservableCollection<FormBox> Form { get; } = new();
    [ObservableProperty] private string _formText = "";
    [ObservableProperty] private bool _hasForm;
    /// <summary>H20 (run 72): once your club is out of the postseason, the page says so.</summary>
    [ObservableProperty] private string _seasonOver = "";
    private Dictionary<int, H2HRecord> _h2h = new();

    public ScheduleViewModel(MainViewModel shell) : base(shell) { }

    /// <summary>C5 part 1: a college coach's filter for the games against rivals.</summary>
    public const string RivalryFilter = "Rivalry games";
    private static bool Matches(MyGame g, string filter) => filter == RivalryFilter ? g.Rivalry != null : ScheduleFilter.Matches(g, filter);
    /// <summary>Run 72: raised at the end of <see cref="Refresh"/> (and a filter change) so the tiles scroll to the game picked.</summary>
    public event Action? Refreshed;

    [RelayCommand]
    public void SetFilter(string? name)
    {
        if (name == null || name == Filter) return;
        Filter = name;
        Refresh();
    }

    partial void OnSelectedChanged(GameRow? value) => ShowRival(value?.OpponentTeam);

    /// <summary>The head-to-head card for the selected game's opponent.</summary>
    private void ShowRival(Hardwood.Core.Model.Team? opp)
    {
        HasRival = opp != null;
        RivalMeetings.Clear();
        HasMeetings = false;
        RivalryLine = "";
        if (opp == null) return;
        var s = Session;
        var r = _h2h.TryGetValue(opp.Id, out var rec) ? rec : s.HeadToHead(opp);
        RivalTeam = opp;
        RivalName = s.InCollege ? Hardwood.Core.College.CollegeWorld.WithRank(s.World.College?.Rank(opp.Id), opp.FullName) : opp.FullName;
        RivalRecord = r.Games == 0 ? "None yet" : $"{r.Wins}-{r.Losses}";
        RivalPoints = r.Games == 0 ? "" : $"{r.PointsFor / (double)r.Games:F1} for, {r.PointsAgainst / (double)r.Games:F1} against a game · {r.Games} game{(r.Games == 1 ? "" : "s")}";
        RivalLast = r.LastDate is { } d ? $"Last meeting: {(r.LastUs > r.LastThem ? "W" : "L")} {r.LastUs}-{r.LastThem} {(r.LastHome ? "at home" : "away")}, {d:MMM d, yyyy}" : "";
        RivalSince = $"Your record {s.HeadToHeadSince}.";
        if (s.InCollege && s.World.College is { } cw && cw.RivalryOf(s.HumanTeam.Id, opp.Id) is { } rv)
        {
            var series = cw.SeriesOf(s.HumanTeam.Id, opp.Id);
            string last = series?.LastDate is { } ld ? $" · last: {cw.Program(series.LastWinner)?.City} {series.LastWinnerPts}-{series.LastLoserPts}, {ld:MMM yyyy}" : "";
            RivalryLine = $"{rv.Title} · " + (cw.SeriesLine(s.HumanTeam.Id, opp.Id) ?? "no meetings yet in this career") + last;
        }
        foreach (var g in _all.Where(g => g.Opponent == opp))
            RivalMeetings.Add(g.Played ? $"{g.Date:MMM d} · {(g.Home ? "home" : "away")} · {(g.Won ? "W" : "L")} {g.Us}-{g.Them}{(g.Group != ScheduleFilter.Regular && g.Group != ScheduleFilter.NonConference ? $" · {g.Group}" : "")}"
                : $"{g.Date:MMM d} · {(g.Home ? "home" : "away")} · to come{(g.Group != ScheduleFilter.Regular && g.Group != ScheduleFilter.NonConference ? $" · {g.Group}" : "")}");
        HasMeetings = RivalMeetings.Count > 0;
    }

    [RelayCommand]
    public void OpenBox(GameRow? row)
    {
        if (row?.Result != null && row.HasBox) Shell.ShowBoxScore(row.Result);
    }

    public override void Refresh()
    {
        var s = Session; var us = s.HumanTeam;
        ShowEvent = s.InCollege;
        Games.Clear();
        _all = s.MyGames();
        _h2h = s.HeadToHeads(_all);
        var names = ScheduleFilter.For(_all);
        if (_all.Any(g => g.Rivalry != null)) names.Insert(Math.Min(names.Count, 1 + names.Count(n => n is ScheduleFilter.Regular or ScheduleFilter.NonConference or ScheduleFilter.Conference)), RivalryFilter);
        if (!names.Contains(Filter)) Filter = ScheduleFilter.All;
        Filters.Clear();
        foreach (var name in names)
        {
            var part = _all.Where(g => Matches(g, name)).ToList();
            int pw = part.Count(g => g.Played && g.Won), pl = part.Count(g => g.Played && !g.Won);
            Filters.Add(new FilterChip { Name = name, Record = pw + pl == 0 ? $"{part.Count} to play" : $"{pw}-{pl}", Selected = name == Filter,
                Tip = $"{part.Count} game{(part.Count == 1 ? "" : "s")}: {pw + pl} played, {part.Count - pw - pl} to come" });
        }
        // The H2H column reads as of each game (#529, "as of the current game"): before this season, plus this season's
        // meetings up to and including that game; a game to come shows the record going into it.
        var running = new Dictionary<int, H2HRecord>();
        var asOf = new Dictionary<MyGame, string>();
        foreach (var g in _all.OrderBy(g => g.Date))
        {
            if (!running.TryGetValue(g.Opponent.Id, out var h))
                running[g.Opponent.Id] = h = s.HeadToHeadBook.Past.TryGetValue(HeadToHeadBook.Key(us.Id, g.Opponent.Id), out var past) ? past.Copy() : new H2HRecord();
            if (g.Played) h.Add(g.Date, g.Us!.Value, g.Them!.Value, g.Home);
            asOf[g] = h.Line;
        }
        int w = 0, l = 0;
        var nextGame = _all.Where(g => !g.Played).OrderBy(g => g.Date).FirstOrDefault();
        foreach (var g in _all.Where(g => Matches(g, Filter)))
        {
            var r = g.Result;
            string score = "", outcome = "", top = "";
            if (g.Played)
            {
                if (g.Won) w++; else l++;
                score = $"{g.Us}-{g.Them}" + (r is { Overtimes: > 0 } ? (r.Overtimes == 1 ? " OT" : $" {r.Overtimes}OT") : "");
                outcome = g.Won ? "W" : "L";
                var ours = r == null ? null : r.Home.Team == us ? r.Home : r.Away;
                var best = ours?.Lines.OrderByDescending(x => x.Points).FirstOrDefault();
                top = best != null ? $"{best.Player.LastName} {best.Points} pts, {best.Rebounds} reb, {best.Assists} ast"
                    : g.NotCoached ? (s.Employed ? NotCoachedText : OutOfWorkText) : "";
            }
            string comp = g.Competition == s.HumanLeagueName ? (g.Stage == "Regular season" ? "" : g.Stage) : (g.Stage == "Regular season" ? g.Competition : $"{g.Competition}: {g.Stage}");
            if (s.InCollege && g.Competition == Hardwood.Core.College.CollegeWorld.Madness) comp = $"Madness: {g.Stage}";
            // The rank at tip-off for a result, the rank now for a game to come (#528).
            string opponent = s.InCollege ? $"{(g.Home ? "vs" : "at")} {Hardwood.Core.College.CollegeWorld.WithRank(g.OpponentRank, g.Opponent.City)}"
                : $"{(g.Home ? "vs" : "at")} {g.Opponent.FullName}" + (comp.Length > 0 ? $" · {comp}" : "");
            string tileOpp = s.InCollege ? $"{(g.Home ? "vs" : "at")} {Hardwood.Core.College.CollegeWorld.WithRank(g.OpponentRank, g.Opponent.City)}"
                : $"{(g.Home ? "vs" : "at")} {g.Opponent.FullName}";
            string kind = g.Played ? (g.Won ? "W" : "L") : nextGame == g ? "next" : "later";
            Games.Add(new GameRow
            {
                Game = g, Opponent = opponent, OpponentTeam = g.Opponent, Kind = kind, TileOpponent = tileOpp,
                Competition = comp, Score = score, Outcome = outcome, Record = g.Played ? $"{w}-{l}" : "", TopPerformer = top,
                HeadToHead = asOf.TryGetValue(g, out var line) ? line : "–",
            });
        }
        FilterSummary = Filter == ScheduleFilter.All ? "" : $"{Filter}: {w}-{l}" + (Games.Count(g => !g.Played) is > 0 and var left ? $", {left} to play" : "");
        var next = Games.FirstOrDefault(g => !g.Played);
        Selected = next ?? Games.LastOrDefault();
        (FormText, var boxes) = FormLine(Games.Where(g => g.Played).Select(g => g.Game).ToList());
        Form.Clear();
        foreach (var b in boxes) Form.Add(b);
        HasForm = Form.Count > 0;
        SeasonOver = MainViewModel.SeasonOverText(s) is { } over ? $"{char.ToUpper(over[0])}{over[1..]}: the rest of the playoffs go on without you. Continue or Sim week moves on to the summer." : "";
        ShowRival(Selected?.OpponentTeam);

        Series.Clear();
        ShowPlayoffs = s.Postseason != null || s.HumanLeague?.Season.Series.Count > 0;
        if (s.HumanLeague is { } hl)
            foreach (var se in hl.Season.Series)
            {
                var hi = s.FindTeam(se.HighId)!; var lo = s.FindTeam(se.LowId)!;
                Series.Add(new SeriesRow { High = hi, Low = lo, Round = se.Round, Matchup = $"{hi.FullName} vs {lo.FullName}",
                    Score = se.Complete ? $"{s.FindTeam(se.WinnerId!.Value)!.Abbreviation} wins {se.Score}" : se.Score, Involved = hi == us || lo == us });
            }
        if (s.Postseason != null)
            foreach (var se in s.Postseason.Series)
                Series.Add(new SeriesRow { High = se.Higher, Low = se.Lower, Round = se.Round, Matchup = $"{se.Higher.FullName} vs {se.Lower.FullName}", Score = se.Complete ? $"{se.Winner!.Abbreviation} wins {Math.Max(se.HigherWins, se.LowerWins)}-{Math.Min(se.HigherWins, se.LowerWins)}" : se.Score, Involved = se.Higher == us || se.Lower == us });

        LeagueScores.Clear();
        var yesterday = s.Today.AddDays(-1);
        foreach (var g in s.Schedule.Where(g => g.Date == yesterday && s.Results.ContainsKey(g.Index)))
            LeagueScores.Add(s.Results[g.Index].Summary());
        if (s.HumanLeague is { } league)
            foreach (var f in league.Season.Fixtures.Where(f => f.Date == yesterday && f.Played))
                LeagueScores.Add($"{s.FindTeam(f.AwayId)!.Abbreviation} {f.AwayPts} @ {s.FindTeam(f.HomeId)!.Abbreviation} {f.HomePts}");
        if (s.Postseason != null)
            foreach (var se in s.Postseason.Series)
            foreach (var r in se.Games.Where(r => r.Date == yesterday)) LeagueScores.Add($"{se.Round}: {r.Summary()}");

        var rec = s.MyRecord;
        Summary = $"{s.HumanLeagueName}: {rec.Wins}-{rec.Losses} · home {rec.HomeWins}-{rec.HomeLosses} · {rec.PointsFor / Math.Max(1.0, rec.Games):F1} for, {rec.PointsAgainst / Math.Max(1.0, rec.Games):F1} against";
        Refreshed?.Invoke();
    }
}

public sealed partial class ScheduleViewModel
{
    /// <summary>Run 72: the form strip's boxes (the last ten results, oldest first) and its line:
    /// "Last 10: 7-3 · won 3 in a row". Empty with no game played.</summary>
    public static (string text, List<FormBox> boxes) FormLine(IReadOnlyList<MyGame> played)
    {
        var last = played.OrderBy(g => g.Date).TakeLast(10).ToList();
        if (last.Count == 0) return ("", new());
        var boxes = last.Select(g => new FormBox { Letter = g.Won ? "W" : "L",
            Tip = $"{g.Date:MMM d} · {(g.Home ? "vs" : "at")} {g.Opponent.FullName} · {(g.Won ? "W" : "L")} {g.Us}-{g.Them}" }).ToList();
        int lw = last.Count(g => g.Won);
        bool won = last[^1].Won;
        int run = last.AsEnumerable().Reverse().TakeWhile(g => g.Won == won).Count();
        string streak = run >= 2 ? $"{(won ? "won" : "lost")} {run} in a row" : won ? "won the last one" : "lost the last one";
        return ($"Last {last.Count}: {lw}-{last.Count - lw} · {streak}", boxes);
    }
}

public sealed class BoxLineRow
{
    public required PlayerLine Line { get; init; }
    public string Name => Line.Player.Name + (Line.Started ? "" : "");
    public string Pos => Line.Player.PrimaryPosition.ToString();
    public string Min => Line.Minutes.ToString("0.0");
    public int Pts => Line.Points;
    public string FG => $"{Line.FGM}-{Line.FGA}";
    public string TP => $"{Line.ThreePM}-{Line.ThreePA}";
    public string FT => $"{Line.FTM}-{Line.FTA}";
    public int Reb => Line.Rebounds;
    public int Ast => Line.Assists;
    public int Stl => Line.Steals;
    public int Blk => Line.Blocks;
    public int Tov => Line.Turnovers;
    public int PF => Line.Fouls;
    public string PM => Line.PlusMinus.ToString("+#;-#;0");
    public bool Started => Line.Started;
}

public sealed partial class BoxScoreViewModel : PageViewModel
{
    public GameResult Result { get; }
    public override string Title => Result.Summary();
    public string Headline => Result.Summary();
    public string Periods { get; }
    public string AwayName => Result.Away.Team.FullName;
    public string HomeName => Result.Home.Team.FullName;
    public Hardwood.Core.Model.Team AwayTeam => Result.Away.Team;
    public Hardwood.Core.Model.Team HomeTeam => Result.Home.Team;
    public string AwayTotals => Totals(Result.Away);
    public string HomeTotals => Totals(Result.Home);
    public ObservableCollection<BoxLineRow> Away { get; } = new();
    public ObservableCollection<BoxLineRow> Home { get; } = new();
    public ObservableCollection<string> Log { get; } = new();
    public bool HasLog => Log.Count > 0;
    // Match day (roadmap 2): the game's shape, for games with a timeline (yours).
    public bool HasFlow => Result.Flow.Count > 0;
    public Avalonia.Media.Color FlowHome => GameFlowInfo.Colours(Result).Home;
    public Avalonia.Media.Color FlowAway => GameFlowInfo.Colours(Result).Away;
    public List<SplitRow> Splits => GameFlowInfo.Splits(Result);
    public List<string> RunLines => GameFlowInfo.RunLines(Result);
    /// <summary>Match day part 3: the commentary's recap of each period ("The 3rd quarter goes to BOS, 31-22. Parker led the way with 12").</summary>
    public List<string> PeriodRecaps => Result.PlayLog.Where(l => l.Contains(" goes to ") || l.Contains(" is even")).Select(l => l.Split("  ", 2).Last()).ToList();
    public bool HasPeriodRecaps => PeriodRecaps.Count > 0;
    public bool HasRuns => RunLines.Count > 0;
    public List<Hardwood.Desktop.Controls.Chip> FlowChips => GameFlowInfo.Chips(Result);
    // Match day part 2: the game's highlight cards and the team comparison.
    public List<Highlight> Highlights => GameHighlights.For(Result);
    public bool HasHighlights => Result.Home.Lines.Any(l => l.Seconds > 0);
    public List<TeamStatRow> TeamStats => GameFlowInfo.TeamStats(Result);
    public int[] AwayZoneAttempts => Sum(Result.Away, l => l.ZoneAttempts);
    public int[] AwayZoneMakes => Sum(Result.Away, l => l.ZoneMakes);
    public int[] HomeZoneAttempts => Sum(Result.Home, l => l.ZoneAttempts);
    public int[] HomeZoneMakes => Sum(Result.Home, l => l.ZoneMakes);
    private static int[] Sum(TeamBox b, Func<PlayerLine, int[]> f)
    {
        var r = new int[5];
        foreach (var l in b.Lines) { var z = f(l); for (int i = 0; i < 5; i++) r[i] += z[i]; }
        return r;
    }

    public BoxScoreViewModel(MainViewModel shell, GameResult result) : base(shell)
    {
        Result = result;
        Periods = $"{result.Away.Team.Abbreviation}  {string.Join("  ", result.Away.PointsByPeriod)}  |  {result.Away.Points}\n{result.Home.Team.Abbreviation}  {string.Join("  ", result.Home.PointsByPeriod)}  |  {result.Home.Points}";
        foreach (var l in result.Away.Lines.Where(l => l.Seconds > 0).OrderByDescending(l => l.Started).ThenByDescending(l => l.Seconds)) Away.Add(new BoxLineRow { Line = l });
        foreach (var l in result.Home.Lines.Where(l => l.Seconds > 0).OrderByDescending(l => l.Started).ThenByDescending(l => l.Seconds)) Home.Add(new BoxLineRow { Line = l });
        foreach (var line in result.PlayLog) Log.Add(line);
    }

    /// <summary>Roadmap 1: the game's leaders (points, rebounds, assists) as cards under the score.</summary>
    public List<Hardwood.Desktop.Controls.Chip> Leaders
    {
        get
        {
            var all = Result.Away.Lines.Select(l => (l, t: Result.Away.Team)).Concat(Result.Home.Lines.Select(l => (l, t: Result.Home.Team)))
                .Where(x => x.l.Seconds > 0).ToList();
            var chips = new List<Hardwood.Desktop.Controls.Chip>();
            if (all.Count == 0) return chips;
            void Add(string label, Func<PlayerLine, int> stat, string unit)
            {
                var top = all.OrderByDescending(x => stat(x.l)).ThenByDescending(x => x.l.Points).First();
                chips.Add(new(label, $"{stat(top.l)} {unit}", $"{top.l.Player.Name} ({top.t.Abbreviation})", "team"));
            }
            Add("Top scorer", l => l.Points, "pts");
            Add("Rebounds", l => l.Rebounds, "reb");
            Add("Assists", l => l.Assists, "ast");
            var plus = all.OrderByDescending(x => x.l.PlusMinus).First();
            chips.Add(new("Best +/-", plus.l.PlusMinus.ToString("+#;-#;0"), $"{plus.l.Player.Name} ({plus.t.Abbreviation})", "info"));
            return chips;
        }
    }

    public List<Hardwood.Desktop.Controls.Chip> AwayChips => TeamChips(Result.Away, Result.Home);
    public List<Hardwood.Desktop.Controls.Chip> HomeChips => TeamChips(Result.Home, Result.Away);
    public Avalonia.Media.Color AwayAccent => ThemeService.AccentFor(Result.Away.Team);
    public Avalonia.Media.Color HomeAccent => ThemeService.AccentFor(Result.Home.Team);

    /// <summary>A team's shooting, boards, assists and turnovers as chips; green where it beat the other side.</summary>
    public static List<Hardwood.Desktop.Controls.Chip> TeamChips(TeamBox b, TeamBox o)
    {
        static double Pct(int m, int a) => a > 0 ? 100.0 * m / a : 0;
        static string Edge(double mine, double theirs, bool lowerIsBetter = false) =>
            Math.Abs(mine - theirs) < 1e-9 ? "" : (mine > theirs) != lowerIsBetter ? "good" : "";
        return new List<Hardwood.Desktop.Controls.Chip>
        {
            new("FG", $"{Pct(b.FGM, b.FGA):0.0}%", $"{b.FGM}-{b.FGA}", Edge(Pct(b.FGM, b.FGA), Pct(o.FGM, o.FGA))),
            new("3PT", $"{Pct(b.ThreePM, b.ThreePA):0.0}%", $"{b.ThreePM}-{b.ThreePA}", Edge(Pct(b.ThreePM, b.ThreePA), Pct(o.ThreePM, o.ThreePA))),
            new("FT", $"{b.FTM}-{b.FTA}", $"{Pct(b.FTM, b.FTA):0}%", Edge(b.FTM, o.FTM)),
            new("Rebounds", (b.OReb + b.DReb).ToString(), $"{b.OReb} offensive", Edge(b.OReb + b.DReb, o.OReb + o.DReb)),
            new("Assists", b.Assists.ToString(), $"{b.Steals} stl · {b.Blocks} blk", Edge(b.Assists, o.Assists)),
            new("Turnovers", b.Turnovers.ToString(), $"{b.Possessions} possessions", Edge(b.Turnovers, o.Turnovers, lowerIsBetter: true)),
        };
    }

    private static string Totals(TeamBox b) => $"FG {b.FGM}-{b.FGA} ({(b.FGA > 0 ? 100.0 * b.FGM / b.FGA : 0):F1}%) · 3P {b.ThreePM}-{b.ThreePA} · FT {b.FTM}-{b.FTA} · Reb {b.OReb + b.DReb} ({b.OReb} off) · Ast {b.Assists} · Stl {b.Steals} · Blk {b.Blocks} · TO {b.Turnovers} · Poss {b.Possessions}";

    [RelayCommand] public void Back() => Shell.GoBack();
    public override void Refresh() { }
}
