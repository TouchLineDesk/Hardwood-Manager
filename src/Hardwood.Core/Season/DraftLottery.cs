using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

public sealed record LotteryResult(List<Team> Order, List<(Team team, int preLotterySlot, int pick)> Jumps);

/// <summary>
/// NBA-style weighted lottery: the 14 non-playoff teams draw for the top 4 picks with flattened odds
/// (14/14/14/12.5/10.5/9/7.5/6/4.5/3/2/1.5/1/0.5 %), the rest fall in reverse-record order,
/// then playoff teams by record. Second round is straight reverse order.
/// </summary>
public static class DraftLottery
{
    public static readonly double[] Odds = { 14, 14, 14, 12.5, 10.5, 9, 7.5, 6, 4.5, 3, 2, 1.5, 1, 0.5 };

    /// <param name="reverseStandings">All teams, worst record first.</param>
    /// <param name="playoffTeamIds">Teams that made the 16-team bracket (play-in losers count as non-playoff).</param>
    public static LotteryResult Run(List<Team> reverseStandings, HashSet<int> playoffTeamIds, Rng rng)
    {
        var lotteryTeams = reverseStandings.Where(t => !playoffTeamIds.Contains(t.Id)).Take(14).ToList();
        var others = reverseStandings.Where(t => !lotteryTeams.Contains(t)).ToList();

        var remaining = new List<Team>(lotteryTeams);
        var weights = new List<double>(Odds.Take(lotteryTeams.Count));
        var winners = new List<Team>();
        for (int k = 0; k < 4 && remaining.Count > 0; k++)
        {
            int idx = rng.WeightedIndex(weights.ToArray());
            winners.Add(remaining[idx]);
            remaining.RemoveAt(idx);
            weights.RemoveAt(idx);
        }

        var order = new List<Team>(winners);
        order.AddRange(remaining);   // still worst-first
        order.AddRange(others);

        var jumps = new List<(Team, int, int)>();
        for (int i = 0; i < lotteryTeams.Count; i++)
        {
            int pick = order.IndexOf(lotteryTeams[i]) + 1;
            if (pick != i + 1) jumps.Add((lotteryTeams[i], i + 1, pick));
        }
        return new LotteryResult(order, jumps);
    }
}
