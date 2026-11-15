using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>
/// The play-by-play commentary for one game (Phase 1 part 2, #614): the time-stamped play log, the broadcast-style
/// call for a made basket, and the recap at the end of each period. Moved out of <c>GameSimulator.GameState</c>
/// without changing a word of its output.
/// <para>It only exists when a game keeps its log, and it never touches the random stream: phrasing is chosen from
/// the log's position, so commentary can never change a result. <c>GameState</c> hands it the events it resolves;
/// it reads nothing back from the engine beyond what each call is given.</para>
/// </summary>
internal sealed class PlayByPlay
{
    private readonly GameResult _result;
    private readonly Rules _rules;

    /// <summary>Points scored by each player in the period being played (for the period recap).</summary>
    private readonly Dictionary<Player, int> _periodPoints = new();

    private static readonly string[] Threes = { "buries a three", "knocks down the three", "drills it from deep", "hits from downtown", "splashes the triple" };
    private static readonly string[] Rims = { "finishes at the rim", "lays it in", "attacks the basket and scores", "gets to the rim for two", "finishes inside" };
    private static readonly string[] Jumpers = { "hits the jumper", "pulls up and connects", "knocks down the mid-range J", "rises and hits", "sinks the jumper" };

    public PlayByPlay(GameResult result, Rules rules)
    {
        _result = result; _rules = rules;
    }

    /// <summary>Add a plain line to the log, stamped with the period and the clock ("Q3 04:12  ...").</summary>
    public void Line(int period, double clock, string text)
    {
        int m = (int)(clock / 60), sec = (int)(clock % 60);
        string p = period > _rules.Periods ? $"OT{period - _rules.Periods}" : $"{(_rules.Periods == 2 ? "H" : "Q")}{period}";
        _result.PlayLog.Add($"{p} {m:00}:{sec:00}  {text}");
    }

    /// <summary>
    /// A made field goal (match day part 3): varied phrasing, the assist, and one piece of context: a lead change or
    /// tie late on, a scoring milestone for the night, or a hot hand from three. The shooter's line and both scores
    /// already include the basket.
    /// </summary>
    public void MadeBasket(int period, double clock, string offAbbr, int us, int them, PlayerLine line, Player shooter,
                           Player? assister, ShotZone zone, bool andOne)
    {
        int pts = zone.IsThree() ? 3 : 2;
        _periodPoints[shooter] = _periodPoints.GetValueOrDefault(shooter) + pts;
        int pick = _result.PlayLog.Count;
        string verb = zone.IsThree() ? Threes[pick % Threes.Length] : zone == ShotZone.Rim ? Rims[pick % Rims.Length] : Jumpers[pick % Jumpers.Length];
        string call = $"{shooter.LastName} {verb}" + (andOne ? ", and the foul! And one" : "") + (assister is { } a ? $" (assist {a.LastName})" : "");
        int before = us - pts;
        bool late = period >= _rules.Periods && clock <= 120;
        string? context =
            late && before < them && us > them ? $"{offAbbr} take the lead, {us}-{them}!" :
            late && us == them ? $"All square at {us}!" :
            before < them && us > them ? $"{offAbbr} back in front, {us}-{them}." :
            line.Points - pts < 30 && line.Points >= 30 ? $"That's 30 for {shooter.LastName} tonight." :
            line.Points - pts < 20 && line.Points >= 20 ? $"{shooter.LastName} has 20." :
            zone.IsThree() && line.ThreePM >= 4 ? $"His {Ordinal(line.ThreePM)} three of the night." :
            null;
        Line(period, clock, context == null ? call : $"{call}. {context}");
    }

    /// <summary>A made free throw counts towards the period recap (free throws get no line of their own).</summary>
    public void FreeThrow(Player shooter) => _periodPoints[shooter] = _periodPoints.GetValueOrDefault(shooter) + 1;

    /// <summary>
    /// The end of a period: the score line, then the recap (who won the period, by how much, and its top scorer).
    /// <paramref name="homePeriod"/> and <paramref name="awayPeriod"/> are the points each side scored in the period.
    /// </summary>
    public void PeriodEnd(int period, double clock, string homeAbbr, string awayAbbr, int homeScore, int awayScore, int homePeriod, int awayPeriod)
    {
        Line(period, clock, $"End of period {period}: {awayAbbr} {awayScore} - {homeAbbr} {homeScore}");
        int h = homePeriod, a = awayPeriod;
        string name = period <= _rules.Periods ? (_rules.Periods == 2 ? $"The {(period == 1 ? "first" : "second")} half" : $"The {Ordinal(period)} quarter") : "Overtime";
        string who = h == a ? $"{name} is even, {h}-{a}" : h > a ? $"{name} goes to {homeAbbr}, {h}-{a}" : $"{name} goes to {awayAbbr}, {a}-{h}";
        var top = _periodPoints.OrderByDescending(kv => kv.Value).FirstOrDefault();
        Line(period, clock, top.Key == null ? $"{who}." : $"{who}. {top.Key.LastName} led the way with {top.Value} in the period.");
        _periodPoints.Clear();
    }

    internal static string Ordinal(int n) => n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };
}
