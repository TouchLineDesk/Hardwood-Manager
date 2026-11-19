namespace Hardwood.Core.Model;

/// <summary>
/// A club's money (decision #88): what it earns in a season, the wage and transfer budgets its owner allows,
/// its bank balance and its fan base (which grows with success and drives revenue).
/// </summary>
public sealed class ClubFinance
{
    /// <summary>Revenue for a season: gate, media and sponsorship.</summary>
    public long Revenue { get; set; }
    /// <summary>The most the owner lets the club spend on player wages this season.</summary>
    public long WageBudget { get; set; }
    /// <summary>Money the owner allows for transfer fees (used by the transfer market).</summary>
    public long TransferBudget { get; set; }
    /// <summary>Cash in the bank: revenue less wages and fees, season after season.</summary>
    public long Balance { get; set; }
    /// <summary>0-100: how big and loyal the support is. Success grows it slowly; losing shrinks it.</summary>
    public double FanBase { get; set; } = 50;
    /// <summary>Set once budgets have been worked out (old saves and new clubs get them on first use).</summary>
    public bool Ready { get; set; }
    /// <summary>Parachute money after relegation, paid on top of revenue: this season's amount and the seasons left (critic review, Nov 10).</summary>
    public long Parachute { get; set; }
    public int ParachuteYears { get; set; }
    /// <summary>The part of this season's parachute carried over from an earlier relegation that hadn't finished paying (#363): a club that drops twice keeps it.</summary>
    public long ParachuteCarried { get; set; }
    /// <summary>K1 (run 111): EPL-style parachutes are shares of the media money of the league the club left
    /// (<see cref="Season.Budgets.ParachuteShares"/>). The base it was set from (0 = an older save's half-the-lost-revenue
    /// parachute, which keeps halving until it runs out) and which year of the schedule is being paid (1-based).</summary>
    public long ParachuteBase { get; set; }
    public int ParachuteYear { get; set; }
    /// <summary>K1 (run 111): seasons the club has played at its current level (set to 0 when it moves, +1 each summer), so a
    /// club that goes straight back down after one season up gets the short parachute. Older saves start at 5.</summary>
    public int SeasonsAtLevel { get; set; } = 5;
    /// <summary>H122 (run 122): the wage budget before the owner first stretched it this summer to keep a young star, and the
    /// season that summer's contracts start in (0 = never). Further young stars that summer share the same 35% over that base,
    /// so the stretch doesn't compound.</summary>
    public long StretchBase { get; set; }
    public int StretchSeason { get; set; }
    /// <summary>Deep in debt (balance below half a season's revenue): no transfer fees until it recovers.</summary>
    public bool Embargo { get; set; }

    /// <summary>Money buys things (item M, #281): the club's facilities, projects and proposals.</summary>
    public ClubFacilities Facilities { get; set; } = new();

    /// <summary>Money in for a season: revenue plus any parachute.</summary>
    public long Income => Revenue + Parachute;
}
