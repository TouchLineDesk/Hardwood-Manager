using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.Presentation;

/// <summary>
/// M5 of the 2D match engine (run 152): the commentary's bigger calls across a game, read from what the engine already
/// recorded: scoring runs, lead changes late, players' milestones (20, 30 and 40 points, a double-double or a
/// triple-double) and foul trouble. Pure and read-only: it never touches the engine or its random stream. The live view
/// feeds it each possession's plays and box score and says the calls as the next possession starts.
/// </summary>
public sealed class GameCalls
{
    /// <summary>The most calls said at once (the bar shows one line at a time, 2 s each).</summary>
    public const int MaxPerPossession = 2;
    /// <summary>A run is called at 8 unanswered points, then every 4 more.</summary>
    public const int FirstRun = 8, RunStep = 4;

    private readonly HashSet<(Player, string)> _said = new();
    private int _runSide, _runPoints, _runCalled;
    private int _leader, _leadChanges;

    /// <summary>The unanswered points of the side on a run (+ home, − away), for tests.</summary>
    public int Run => _runSide * _runPoints;
    public int LeadChanges => _leadChanges;

    /// <summary>The calls for one possession just played, most important first, at most <see cref="MaxPerPossession"/>.</summary>
    /// <param name="events">The possession's plays.</param>
    /// <param name="home">The home box score after the possession.</param>
    /// <param name="away">The away box score after it.</param>
    /// <param name="startHome">The home score before the possession.</param>
    /// <param name="startAway">The away score before it.</param>
    /// <param name="periods">Regulation periods (2 in college, 4 in the pros).</param>
    /// <param name="foulOut">Fouls to foul out (5 in college and FIBA, 6 in the NBA).</param>
    public List<string> For(IReadOnlyList<PlayEvent> events, TeamBox home, TeamBox away, int startHome, int startAway,
        string homeName, string awayName, int periods, int foulOut)
    {
        var big = new List<string>();     // foul-outs, runs and lead changes
        var small = new List<string>();   // milestones and foul trouble
        int hs = startHome, aw = startAway;
        int period = 0;
        foreach (var e in events)
        {
            period = Math.Max(period, e.Period);
            int dh = e.HomeScore - hs, da = e.AwayScore - aw;
            if (dh <= 0 && da <= 0) continue;
            hs = e.HomeScore; aw = e.AwayScore;
            int side = dh > 0 ? 1 : -1, pts = dh > 0 ? dh : da;
            if (side == _runSide) _runPoints += pts;
            else { _runSide = side; _runPoints = pts; _runCalled = 0; }
            int lead = Math.Sign(hs - aw);
            if (lead != 0 && _leader != 0 && lead != _leader)
            {
                _leadChanges++;
                if (period >= periods)
                    big.Add($"Lead change, the {Ordinal(_leadChanges)} of the game: {(lead > 0 ? homeName : awayName)} in front, {Math.Max(hs, aw)}-{Math.Min(hs, aw)}");
            }
            if (lead != 0) _leader = lead;
        }
        int step = _runPoints < FirstRun ? 0 : FirstRun + (_runPoints - FirstRun) / RunStep * RunStep;
        if (step > _runCalled)
        {
            _runCalled = step;
            big.Insert(0, $"That's {Article(_runPoints)} {_runPoints}-0 run for {(_runSide > 0 ? homeName : awayName)}");
        }

        // Milestones and fouls, for the players in this possession's plays (the others haven't changed).
        var involved = new HashSet<Player>();
        foreach (var e in events) { if (e.Actor != null) involved.Add(e.Actor); if (e.Other != null) involved.Add(e.Other); }
        foreach (var line in home.Lines.Concat(away.Lines))
        {
            if (!involved.Contains(line.Player)) continue;
            string who = PossessionChoreographer.Last(line.Player);
            if (line.FouledOut && Say(line.Player, "out")) big.Insert(0, $"{who} has fouled out with {line.Points} points");
            else if (line.Fouls == foulOut - 1 && foulOut > 1 && Say(line.Player, "trouble"))
                small.Add($"{who} picks up his {Ordinal(line.Fouls)} foul: one more and he's done");
            foreach (int m in new[] { 40, 30, 20 })
                if (line.Points >= m && Say(line.Player, $"pts{m}")) { small.Insert(0, $"{who} has {line.Points} points"); for (int k = m - 10; k >= 20; k -= 10) Say(line.Player, $"pts{k}"); break; }
            int tens = (line.Points >= 10 ? 1 : 0) + (line.Rebounds >= 10 ? 1 : 0) + (line.Assists >= 10 ? 1 : 0) + (line.Blocks >= 10 ? 1 : 0) + (line.Steals >= 10 ? 1 : 0);
            if (tens >= 3 && Say(line.Player, "triple")) { Say(line.Player, "double"); small.Insert(0, $"Triple-double for {who}: {Statline(line)}"); }
            else if (tens == 2 && Say(line.Player, "double")) small.Add($"A double-double for {who}: {Statline(line)}");
        }
        return big.Concat(small).Take(MaxPerPossession).ToList();
    }

    private bool Say(Player p, string what) => _said.Add((p, what));

    private static string Statline(PlayerLine l)
    {
        var parts = new List<string> { $"{l.Points} points" };
        if (l.Rebounds >= 10) parts.Add($"{l.Rebounds} rebounds");
        if (l.Assists >= 10) parts.Add($"{l.Assists} assists");
        if (l.Blocks >= 10) parts.Add($"{l.Blocks} blocks");
        if (l.Steals >= 10) parts.Add($"{l.Steals} steals");
        return string.Join(", ", parts);
    }

    /// <summary>"an" before a number said with a vowel sound (8, 11, 18, 80–89), else "a".</summary>
    public static string Article(int n) => n is 8 or 11 or 18 or (>= 80 and <= 89) ? "an" : "a";

    public static string Ordinal(int n) => n + (n % 100 is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });
}
