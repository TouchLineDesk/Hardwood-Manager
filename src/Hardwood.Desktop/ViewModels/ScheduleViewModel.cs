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
    public string Date => Game.Date.ToString("ddd MMM d");
    public string Opponent { get; init; } = "";
    public Hardwood.Core.Model.Team? OpponentTeam { get; init; }
    public string Competition { get; init; } = "";
    public string Score { get; init; } = "";
    public string Outcome { get; init; } = "";
    public string Record { get; init; } = "";
    public string TopPerformer { get; init; } = "";
    public bool Played => Game.Played;
    public bool HasBox => Result != null && Result.Home.Lines.Count > 0;
}

public sealed class SeriesRow
{
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
    [ObservableProperty] private GameRow? _selected;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _showPlayoffs;
    /// <summary>College (C2): the event gets its own column and opponents go by their school's name, so long names fit.</summary>
    [ObservableProperty] private bool _showEvent;

    public ScheduleViewModel(MainViewModel shell) : base(shell) { }

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
        int w = 0, l = 0;
        foreach (var g in s.MyGames())
        {
            var r = g.Result;
            string score = "", outcome = "", top = "";
            if (g.Played)
            {
                if (g.Won) w++; else l++;
                score = $"{g.Us}-{g.Them}" + (r is { Overtimes: > 0 } ? $" ({r.Overtimes}OT)" : "");
                outcome = g.Won ? "W" : "L";
                var ours = r == null ? null : r.Home.Team == us ? r.Home : r.Away;
                var best = ours?.Lines.OrderByDescending(x => x.Points).FirstOrDefault();
                top = best != null ? $"{best.Player.LastName} {best.Points} pts, {best.Rebounds} reb, {best.Assists} ast" : "";
            }
            string comp = g.Competition == s.HumanLeagueName ? (g.Stage == "Regular season" ? "" : g.Stage) : (g.Stage == "Regular season" ? g.Competition : $"{g.Competition}: {g.Stage}");
            if (s.InCollege && g.Competition == Hardwood.Core.College.CollegeWorld.Madness) comp = $"Madness: {g.Stage}";
            string opponent = s.InCollege ? $"{(g.Home ? "vs" : "at")} {g.Opponent.City}"
                : $"{(g.Home ? "vs" : "at")} {g.Opponent.FullName}" + (comp.Length > 0 ? $" · {comp}" : "");
            Games.Add(new GameRow
            {
                Game = g, Opponent = opponent, OpponentTeam = g.Opponent,
                Competition = comp, Score = score, Outcome = outcome, Record = g.Played ? $"{w}-{l}" : "", TopPerformer = top,
            });
        }
        var next = Games.FirstOrDefault(g => !g.Played);
        Selected = next ?? Games.LastOrDefault();

        Series.Clear();
        ShowPlayoffs = s.Postseason != null || s.HumanLeague?.Season.Series.Count > 0;
        if (s.HumanLeague is { } hl)
            foreach (var se in hl.Season.Series)
            {
                var hi = s.FindTeam(se.HighId)!; var lo = s.FindTeam(se.LowId)!;
                Series.Add(new SeriesRow { Round = se.Round, Matchup = $"{hi.FullName} vs {lo.FullName}",
                    Score = se.Complete ? $"{s.FindTeam(se.WinnerId!.Value)!.Abbreviation} wins {se.Score}" : se.Score, Involved = hi == us || lo == us });
            }
        if (s.Postseason != null)
            foreach (var se in s.Postseason.Series)
                Series.Add(new SeriesRow { Round = se.Round, Matchup = $"{se.Higher.FullName} vs {se.Lower.FullName}", Score = se.Complete ? $"{se.Winner!.Abbreviation} wins {se.Score}" : se.Score, Involved = se.Higher == us || se.Lower == us });

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
