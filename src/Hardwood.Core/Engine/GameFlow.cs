using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>The score after a possession that changed it: seconds played, the period, both scores, and who scored last.</summary>
public readonly record struct FlowPoint(double Elapsed, int Period, int Home, int Away, Player? Scorer = null);

/// <summary>A scoring run: one side scored <see cref="For"/> while the other scored <see cref="Against"/>.</summary>
public sealed record ScoringRun(bool Home, string Team, int For, int Against, double StartElapsed, double EndElapsed, int StartPeriod, int EndPeriod)
{
    public string Score => $"{For}-{Against}";
}

/// <summary>
/// Reads the shape of a game from its score timeline (roadmap 2, match day): win probability, momentum, quarter
/// splits and scoring runs. Everything here is worked out from <see cref="GameResult.Flow"/>, so the live screen and the
/// box score tell the same story.
/// </summary>
public static class GameFlow
{
    /// <summary>A run is at least this many points…</summary>
    public const int RunMinPoints = 8;
    /// <summary>…while the other side scores no more than this.</summary>
    public const int RunMaxAgainst = 2;

    public static int RegulationSeconds(GameResult r) => r.Periods * r.PeriodSeconds;

    /// <summary>Seconds left in the game (or in the current overtime) at a point in the timeline.</summary>
    public static double SecondsLeft(GameResult r, double elapsed, int period)
    {
        double end = period <= r.Periods ? RegulationSeconds(r) : RegulationSeconds(r) + (period - r.Periods) * r.OvertimeSeconds;
        return Math.Max(0, end - elapsed);
    }

    /// <summary>
    /// The home side's chance to win, 0–1: the current margin plus the share of the pregame edge still to play,
    /// against the spread of what's left (a normal model; the spread shrinks with the square root of time left).
    /// </summary>
    public static double HomeWinProbability(GameResult r, int homeMinusAway, double secondsLeft)
    {
        double f = Math.Clamp(secondsLeft / Math.Max(1, RegulationSeconds(r)), 0, 1.5);
        if (f <= 1e-6) return homeMinusAway > 0 ? 1 : homeMinusAway < 0 ? 0 : 0.5;
        double mean = homeMinusAway + r.PregameEdge * f;
        double sd = Math.Max(0.5, r.MarginSpread * Math.Sqrt(f));
        return Phi(mean / sd);
    }

    /// <summary>Home win probability (0–100) after every scoring possession, starting from the opening tip.</summary>
    public static List<(double Elapsed, double Percent)> WinProbability(GameResult r)
    {
        var list = new List<(double, double)> { (0, 100 * HomeWinProbability(r, 0, RegulationSeconds(r))) };
        foreach (var p in r.Flow)
            list.Add((p.Elapsed, 100 * HomeWinProbability(r, p.Home - p.Away, SecondsLeft(r, p.Elapsed, p.Period))));
        if (r.Flow.Count > 0 && r.Flow[^1] is var last && last.Elapsed < r.Elapsed)
            list.Add((r.Elapsed, 100 * HomeWinProbability(r, last.Home - last.Away, SecondsLeft(r, r.Elapsed, r.CurrentPeriod))));
        return list;
    }

    /// <summary>Momentum: home points minus away points in each <paramref name="bucketSeconds"/> slice of the game so far.</summary>
    public static double[] Momentum(GameResult r, double bucketSeconds = 120)
    {
        int n = (int)Math.Ceiling(Math.Max(r.Elapsed, 1) / bucketSeconds);
        var m = new double[n];
        int h = 0, a = 0;
        foreach (var p in r.Flow)
        {
            int b = Math.Clamp((int)((p.Elapsed - 1e-6) / bucketSeconds), 0, n - 1);
            m[b] += (p.Home - h) - (p.Away - a);
            h = p.Home; a = p.Away;
        }
        return m;
    }

    /// <summary>Points per period for both sides, including the period being played.</summary>
    public static (int[] Home, int[] Away) Splits(GameResult r)
    {
        int periods = Math.Max(r.Periods, r.CurrentPeriod);
        var home = new int[periods]; var away = new int[periods];
        int h = 0, a = 0;
        foreach (var p in r.Flow)
        {
            int i = Math.Clamp(p.Period - 1, 0, periods - 1);
            home[i] += p.Home - h; away[i] += p.Away - a;
            h = p.Home; a = p.Away;
        }
        return (home, away);
    }

    /// <summary>Short period name: Q1–Q4 (or H1–H2 for halves), then OT, OT2…</summary>
    public static string PeriodName(GameResult r, int period) =>
        period > r.Periods ? (period - r.Periods == 1 ? "OT" : $"OT{period - r.Periods}") : (r.Periods == 2 ? $"H{period}" : $"Q{period}");

    /// <summary>
    /// Every scoring run of <see cref="RunMinPoints"/>+ points while conceding <see cref="RunMaxAgainst"/> or fewer,
    /// longest first from each starting basket (runs don't overlap).
    /// </summary>
    public static List<ScoringRun> Runs(GameResult r)
    {
        var events = Events(r);
        var runs = new List<ScoringRun>();
        int k = 0;
        while (k < events.Count)
        {
            bool side = events[k].Home;
            int forPts = 0, against = 0, bestEnd = -1, bestFor = 0, bestAgainst = 0;
            for (int j = k; j < events.Count; j++)
            {
                if (events[j].Home == side) { forPts += events[j].Points; bestEnd = j; bestFor = forPts; bestAgainst = against; }
                else { against += events[j].Points; if (against > RunMaxAgainst) break; }
            }
            if (bestFor >= RunMinPoints && bestFor - bestAgainst >= RunMinPoints - 1)
            {
                var team = side ? r.Home.Team : r.Away.Team;
                runs.Add(new ScoringRun(side, team.Abbreviation, bestFor, bestAgainst, events[k].Point.Elapsed, events[bestEnd].Point.Elapsed, events[k].Point.Period, events[bestEnd].Point.Period));
                k = bestEnd + 1;
            }
            else k++;
        }
        return runs;
    }

    /// <summary>The run still going (its side scored last), if there is one: "BOS on a 12-0 run".</summary>
    public static ScoringRun? CurrentRun(GameResult r)
    {
        var runs = Runs(r);
        if (runs.Count == 0 || r.Flow.Count == 0) return null;
        var last = runs[^1];
        return Math.Abs(last.EndElapsed - r.Flow[^1].Elapsed) < 1e-6 ? last : null;
    }

    /// <summary>The run as a line for the box score: "BOS 12-0 run (Q2, 7:40 to 4:05)".</summary>
    public static string Describe(GameResult r, ScoringRun run)
    {
        string When(double elapsed, int period)
        {
            double left = SecondsLeft(r, elapsed, period);
            double periodLeft = period <= r.Periods ? left - (r.Periods - period) * r.PeriodSeconds : left;
            periodLeft = Math.Clamp(periodLeft, 0, period <= r.Periods ? r.PeriodSeconds : r.OvertimeSeconds);
            return $"{(int)(periodLeft / 60)}:{(int)(periodLeft % 60):00}";
        }
        string span = run.StartPeriod == run.EndPeriod
            ? $"{PeriodName(r, run.StartPeriod)}, {When(run.StartElapsed, run.StartPeriod)} to {When(run.EndElapsed, run.EndPeriod)}"
            : $"{PeriodName(r, run.StartPeriod)} {When(run.StartElapsed, run.StartPeriod)} to {PeriodName(r, run.EndPeriod)} {When(run.EndElapsed, run.EndPeriod)}";
        return $"{run.Team} {run.Score} run ({span})";
    }

    /// <summary>The biggest lead each side held.</summary>
    public static (int Home, int Away) BiggestLeads(GameResult r)
    {
        int h = 0, a = 0;
        foreach (var p in r.Flow) { h = Math.Max(h, p.Home - p.Away); a = Math.Max(a, p.Away - p.Home); }
        return (h, a);
    }

    /// <summary>How many times the lead changed hands.</summary>
    public static int LeadChanges(GameResult r)
    {
        int changes = 0, leader = 0;
        foreach (var p in r.Flow)
        {
            int now = Math.Sign(p.Home - p.Away);
            if (now != 0 && leader != 0 && now != leader) changes++;
            if (now != 0) leader = now;
        }
        return changes;
    }

    private readonly record struct ScoreEvent(bool Home, int Points, FlowPoint Point);

    private static List<ScoreEvent> Events(GameResult r)
    {
        var list = new List<ScoreEvent>();
        int h = 0, a = 0;
        foreach (var p in r.Flow)
        {
            // A possession can see points both ways only at a period change; count each side's share separately.
            if (p.Home > h) list.Add(new ScoreEvent(true, p.Home - h, p));
            if (p.Away > a) list.Add(new ScoreEvent(false, p.Away - a, p));
            h = p.Home; a = p.Away;
        }
        return list;
    }

    /// <summary>Standard normal cumulative distribution (Abramowitz–Stegun 7.1.26, error under 1.5e-7).</summary>
    public static double Phi(double x)
    {
        double t = 1 / (1 + 0.3275911 * Math.Abs(x) / Math.Sqrt(2));
        double y = 1 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t + 0.254829592) * t * Math.Exp(-x * x / 2);
        return x >= 0 ? (1 + y) / 2 : (1 - y) / 2;
    }
}
