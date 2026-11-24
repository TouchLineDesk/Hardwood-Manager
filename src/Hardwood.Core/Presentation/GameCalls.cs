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
        var shared = SharedSurnames(home.Lines.Concat(away.Lines).Select(l => l.Player));
        foreach (var line in home.Lines.Concat(away.Lines))
        {
            if (!involved.Contains(line.Player)) continue;
            string who = CallName(line.Player, shared);
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

    /// <summary>K25's leftover (run 183): the surnames two players in this game share (either side's box score).</summary>
    public static HashSet<string> SharedSurnames(IEnumerable<Player> players) =>
        players.Distinct().GroupBy(p => p.LastName).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();

    /// <summary>K25's leftover (run 183): a milestone or foul call says "J. Smith" when two players in the game are Smiths,
    /// as the possession's lines already do (run 174).</summary>
    public static string CallName(Player p, IReadOnlySet<string> shared) =>
        shared.Contains(p.LastName) && p.FirstName.Length > 0 ? $"{p.FirstName[0]}. {p.LastName}" : p.LastName;

    /// <summary>K25 (run 174): a name as a broadcaster says it: "Miami (FL)" is "Miami", unless the other side would then
    /// sound the same ("Miami (OH)" against "Miami (FL)").</summary>
    public static string BroadcastName(string name, string other)
    {
        static string Strip(string n) { int k = n.IndexOf(" ("); return k > 0 && n.EndsWith(')') ? n[..k] : n; }
        string s = Strip(name);
        return s != name && s == Strip(other) ? name : s;
    }

    /// <summary>K25 (run 174): what the game meant, for the result call.</summary>
    /// <param name="Stage">March: the round just won ("Round of 64" … "Championship"); null otherwise.</param>
    /// <param name="NextStage">March: the round the winner goes on to ("Sweet 16"); null after the final.</param>
    /// <param name="WinnerSeed">March seeds (0 when not seeded).</param>
    /// <param name="WinnerRank">Top 25 ranks at tip-off (null: unranked).</param>
    /// <param name="Rivalry">The rivalry's name, if the two are rivals.</param>
    public sealed record Occasion(string? Stage = null, string? NextStage = null, int WinnerSeed = 0, int LoserSeed = 0,
        int? WinnerRank = null, int? LoserRank = null, string? Rivalry = null);

    /// <summary>K25 (run 174): the call at the final horn, then up to two lines of context (a comeback, overtime, a March
    /// result or an upset, a rivalry). Read from the plays only; the engine and its random stream are never touched.</summary>
    /// <param name="plays">The whole game's plays, for the biggest deficit the winner came back from.</param>
    public static List<string> Final(IReadOnlyList<PlayEvent> plays, string homeName, string awayName, int homeScore, int awayScore,
        int periods, Occasion? occasion = null)
    {
        var lines = new List<string>();
        if (homeScore == awayScore) return lines;
        bool homeWon = homeScore > awayScore;
        string w = homeWon ? homeName : awayName, l = homeWon ? awayName : homeName;
        int ws = Math.Max(homeScore, awayScore), ls = Math.Min(homeScore, awayScore), margin = ws - ls;
        int down = 0;   // the most the winner trailed by
        foreach (var e in plays) down = Math.Max(down, homeWon ? e.AwayScore - e.HomeScore : e.HomeScore - e.AwayScore);
        // Review (run 174): a winner who wasn't ahead before the last score (a walk-off) didn't "hold on".
        int lastScore = -1;
        for (int k = plays.Count - 1; k >= 0 && lastScore < 0; k--)
            if (k == 0 ? plays[k].HomeScore + plays[k].AwayScore > 0 : plays[k].HomeScore != plays[k - 1].HomeScore || plays[k].AwayScore != plays[k - 1].AwayScore) lastScore = k;
        bool walkOff = lastScore > 0 && (homeWon ? plays[lastScore - 1].HomeScore <= plays[lastScore - 1].AwayScore : plays[lastScore - 1].AwayScore <= plays[lastScore - 1].HomeScore)
            && plays[lastScore].Period >= periods && plays[lastScore].Clock <= 30;
        int ots = plays.Count == 0 ? 0 : Math.Max(0, plays.Max(e => e.Period) - periods);
        var o = occasion ?? new Occasion();
        bool champion = o.Stage == "Championship";
        string verb = champion ? "are national champions"
            : down >= 10 ? "complete the comeback"
            : walkOff ? "steal it at the death"
            : margin <= 3 ? "hold on"
            : margin >= 20 ? "roll"
            : "win it";
        lines.Add($"{w.ToUpperInvariant()} {verb.ToUpperInvariant()}! Final: {w} {ws}, {l} {ls}{(ots == 1 ? " in overtime" : ots > 1 ? $" in {ots} overtimes" : "")}");

        var context = new List<string>();
        if (down >= 10 && !champion) context.Add($"{w} were down {down} and came all the way back");
        else if (down >= 10) context.Add($"From {down} down: {w} came all the way back to win it all");
        if (o.Stage != null && !champion && o.NextStage != null) context.Add($"{w} advance to the {o.NextStage}");
        if (o.WinnerSeed > 0 && o.LoserSeed > 0 && o.WinnerSeed - o.LoserSeed >= 4)
            context.Add($"An upset: the {o.WinnerSeed}-seed knocks out the {o.LoserSeed}-seed. Their season is over");
        else if (o.Stage != null && !champion && o.LoserSeed > 0) context.Add($"{l}'s season is over");
        else if (o.Stage == null && o.LoserRank is int lr && (o.WinnerRank == null || o.WinnerRank - lr >= 10))
            context.Add(o.WinnerRank == null ? $"An upset: unranked {w} take down No. {lr} {l}" : $"No. {o.WinnerRank} {w} take down No. {lr} {l}");
        if (o.Rivalry != null) context.Add($"{o.Rivalry}: this one goes to {w}");
        lines.AddRange(context.Take(2));
        return lines;
    }

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
