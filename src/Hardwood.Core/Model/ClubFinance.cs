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
    /// <summary>Deep in debt (balance below half a season's revenue): no transfer fees until it recovers.</summary>
    public bool Embargo { get; set; }

    /// <summary>Money buys things (item M, #281): the club's facilities, projects and proposals.</summary>
    public ClubFacilities Facilities { get; set; } = new();

    /// <summary>Money in for a season: revenue plus any parachute.</summary>
    public long Income => Revenue + Parachute;
}
