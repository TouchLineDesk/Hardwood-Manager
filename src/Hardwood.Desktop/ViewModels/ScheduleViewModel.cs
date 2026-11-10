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

    public ScheduleViewModel(MainViewModel shell) : base(shell) { }

    [RelayCommand]
    public void OpenBox(GameRow? row)
    {
        if (row?.Result != null && row.HasBox) Shell.ShowBoxScore(row.Result);
    }

    public override void Refresh()
    {
        var s = Session; var us = s.HumanTeam;
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
            Games.Add(new GameRow
            {
                Game = g, Opponent = $"{(g.Home ? "vs" : "at")} {g.Opponent.FullName}" + (comp.Length > 0 ? $" · {comp}" : ""), OpponentTeam = g.Opponent,
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

    private static string Totals(TeamBox b) => $"FG {b.FGM}-{b.FGA} ({(b.FGA > 0 ? 100.0 * b.FGM / b.FGA : 0):F1}%) · 3P {b.ThreePM}-{b.ThreePA} · FT {b.FTM}-{b.FTA} · Reb {b.OReb + b.DReb} ({b.OReb} off) · Ast {b.Assists} · Stl {b.Steals} · Blk {b.Blocks} · TO {b.Turnovers} · Poss {b.Possessions}";

    [RelayCommand] public void Back() => Shell.GoBack();
    public override void Refresh() { }
}
