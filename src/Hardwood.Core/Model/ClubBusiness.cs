namespace Hardwood.Core.Model;

/// <summary>
/// The revenue levers (Danny, Nov 22, D81: "they grow revenue and use it to sign players … as well as other ways to increase
/// revenue in the long term (fan appreciation day, food, ticket prices, parking, new stadium upgrades, etc)"). A pro club's
/// match-day business: what it charges for tickets, food and parking, and how many fan appreciation days it holds. Arena
/// upgrades and new arenas are the facilities (item M, #281). The plan in force is <see cref="Now"/>; changes made once the
/// season is under way wait for next season (<see cref="Next"/>), when season tickets are priced.
/// </summary>
public sealed class ClubBusiness
{
    /// <summary>This season's prices (what the books close on).</summary>
    public BusinessPlan Now { get; set; } = new();
    /// <summary>Next season's prices (the same as <see cref="Now"/> unless you changed them mid-season).</summary>
    public BusinessPlan Next { get; set; } = new();
    /// <summary>Set when the summer closes the books and cleared when the new season starts: a change made in between is for the
    /// season to come, so it takes effect at once.</summary>
    public bool BooksClosed { get; set; }
}

/// <summary>One season's match-day plan. Every lever at 0 is the league's standard pricing, which earns exactly the old revenue.</summary>
public sealed class BusinessPlan
{
    /// <summary>Ticket prices: -1 cheap, 0 standard, 1 high, 2 premium.</summary>
    public int Tickets { get; set; }
    /// <summary>Food and drink: -1 value menu, 0 standard, 1 premium.</summary>
    public int Food { get; set; }
    /// <summary>Parking: -1 free, 0 standard, 1 premium.</summary>
    public int Parking { get; set; }
    /// <summary>Fan appreciation days this season (0-4): giveaways, open practice, autographs.</summary>
    public int FanDays { get; set; }

    public bool IsStandard => Tickets == 0 && Food == 0 && Parking == 0 && FanDays == 0;
    public BusinessPlan Copy() => new() { Tickets = Tickets, Food = Food, Parking = Parking, FanDays = FanDays };
    public bool Same(BusinessPlan o) => Tickets == o.Tickets && Food == o.Food && Parking == o.Parking && FanDays == o.FanDays;
}
