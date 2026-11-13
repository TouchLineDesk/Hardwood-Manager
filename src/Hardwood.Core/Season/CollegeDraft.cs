using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

/// <summary>
/// Danny's draft (D40, #410), in real-world careers only (#409). Each June the best college players leaving school enter
/// a one-round draft. The picks belong to next season's NBA clubs: the top three in each NBA division and the tier-2
/// division winners (promoted at the end of the summer). The order is a lottery with equal odds for every eligible club;
/// the third-place tournament's winner also gets an extra pick, the last one. Undrafted graduates go to free agency.
/// Committed seniors (#157) have already signed with their clubs and aren't in the pool.
/// </summary>
public static class CollegeDraft
{
    /// <summary>Top three in a division, as in the playoffs and the third-place tournament.</summary>
    public const int PlacesPerDivision = 3;

    public static bool Applies(League league, GameWorld? world) =>
        world != null && league.Finance.OpenMarket && RealWorld.RealWorldData.IsReal(league) && world.College != null;

    /// <summary>The clubs with a pick (in no order) and the third-place tournament's winner (the extra pick), if any.</summary>
    public static (List<Team> Clubs, Team? Extra) Eligible(League league, GameWorld world, SeasonOutcome outcome)
    {
        var clubs = new List<Team>();
        // The NBA's top three in each division, in the table order the season finished in.
        var table = outcome.Standings.All().Select(r => r.Team).Where(league.Teams.Contains).ToList();
        // The club that goes down is picked as UsaPyramid.Promote picks it (fewest wins, then most losses, then the lowest id),
        // which can differ from the table's tie-break; it never has a pick.
        foreach (var div in table.GroupBy(t => (t.Conference, t.Division)))
        {
            var down = div.OrderBy(t => outcome.Standings[t].Wins).ThenByDescending(t => outcome.Standings[t].Losses).ThenBy(t => t.Id).First();
            clubs.AddRange(div.Where(t => t != down).Take(PlacesPerDivision));
        }
        // Tier-2 division winners go up at the end of the summer: their picks are next season's NBA picks.
        if (world.Competitions.FirstOrDefault(c => c.Key == UsaPyramid.Tier2) is { } t2)
            clubs.AddRange(t2.Clubs.Where(t => UsaPyramid.WonDivision(t2, t)));
        var extra = outcome.ThirdPlaceWinner is { } w && clubs.Contains(w) ? w : null;
        return (clubs.Distinct().ToList(), extra);
    }

    /// <summary>The lottery: every eligible club has the same chance at every pick (Danny, "Equal odds for all").</summary>
    public static List<Team> Lottery(IReadOnlyList<Team> clubs, Team? extra, Rng rng)
    {
        var order = clubs.OrderBy(t => t.Id).ToList();
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        if (extra != null) order.Add(extra);
        return order;
    }

    /// <summary>
    /// Sets up this summer's draft on the offseason: the lottery, and the pool (the best of this year's graduates still in
    /// the free-agent pool, one per pick). Runs after the world's summer has graduated the college seniors.
    /// </summary>
    public static bool SetUp(Offseason o, League league, GameWorld world, Rng rng)
    {
        if (!Applies(league, world) || o.Phase != OffseasonPhase.ReSigning) return false;
        var (clubs, extra) = Eligible(league, world, o.Outcome);
        if (clubs.Count == 0) return false;
        var order = Lottery(clubs, extra, rng);
        int year = o.Season + 1;
        var pool = league.FreeAgents.Where(p => p.RookieSeason == year && p.Contract == null)
            .OrderByDescending(p => ContractMarket.Value(p, o.Date)).ThenBy(p => p.Id).Take(order.Count).ToList();
        if (pool.Count == 0) return false;
        o.StartCollegeDraft(order, pool);
        return true;
    }
}
