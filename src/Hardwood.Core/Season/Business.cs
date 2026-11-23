using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// The revenue levers (D81, Danny Nov 22): ticket prices, food and drink, parking and fan appreciation days, for a pro club.
/// About 30% of a club's revenue is the match day (tickets 22%, food and drink 6%, parking 2%), as in the NBA. Prices trade
/// money now against the fan base, and the fan base is what revenue grows from (<see cref="Budgets.Revenue"/>): a loyal,
/// big support fills the arena at any price, a small one stays home when prices go up. Every lever at standard earns exactly
/// the old revenue, so clubs the computer runs are unchanged. No random numbers.
/// </summary>
public static class Business
{
    public const double TicketShare = 0.22, FoodShare = 0.06, ParkingShare = 0.02;
    public const double MatchDayShare = TicketShare + FoodShare + ParkingShare;
    /// <summary>What one fan appreciation day costs (giveaways, staff, free seats), as a share of revenue: about $1M in the NBA.</summary>
    public const double FanDayCost = 0.004;
    public const int MaxFanDays = 4;

    /// <summary>Ticket price against standard: cheap 80%, standard, high 115%, premium 130%.</summary>
    public static double TicketPrice(int level) => level switch { <= -1 => 0.80, 0 => 1.0, 1 => 1.15, _ => 1.30 };
    /// <summary>Food and drink takings per fan against standard (a value menu sells more, at less each).</summary>
    public static double FoodTake(int level) => level switch { <= -1 => 0.85, 0 => 1.0, _ => 1.12 };
    /// <summary>Parking takings per fan against standard (free parking earns nothing).</summary>
    public static double ParkingTake(int level) => level switch { <= -1 => 0.0, 0 => 1.0, _ => 1.25 };

    public static readonly string[] TicketNames = { "Cheap", "Standard", "High", "Premium" };
    public static readonly string[] FoodNames = { "Value menu", "Standard", "Premium" };
    public static readonly string[] ParkingNames = { "Free", "Standard", "Premium" };
    public static string TicketName(int l) => TicketNames[Math.Clamp(l, -1, 2) + 1];
    public static string FoodName(int l) => FoodNames[Math.Clamp(l, -1, 1) + 1];
    public static string ParkingName(int l) => ParkingNames[Math.Clamp(l, -1, 1) + 1];

    /// <summary>The arena's fill at standard prices: 80% with no support, every seat with the biggest.</summary>
    public static double BaseFill(double fanBase) => 0.80 + 0.20 * Math.Clamp(fanBase, 0, 100) / 100.0;

    /// <summary>How much a price rise empties seats: a small support (fan base 20) leaves at once, a big one (90) hardly notices.</summary>
    public static double Elasticity(double fanBase) => 1.6 - 1.4 * Math.Clamp(fanBase, 0, 100) / 100.0;

    /// <summary>The share of seats sold at a ticket level (a cheap ticket can't sell more than every seat).</summary>
    public static double Fill(double fanBase, int tickets) =>
        Math.Clamp(BaseFill(fanBase) * (1 - Elasticity(fanBase) * (TicketPrice(tickets) - 1)), 0.4, 1.0);

    /// <summary>A season's match day under a plan, against the same club at standard prices.</summary>
    public readonly record struct MatchDay(long Tickets, long Food, long Parking, long FanDayCost, long Standard, double Fill)
    {
        public long Total => Tickets + Food + Parking - FanDayCost;
        /// <summary>What the plan adds to (or takes off) the season's revenue.</summary>
        public long Change => Total - Standard;
    }

    public static MatchDay Plan(long revenue, double fanBase, BusinessPlan p)
    {
        double fill = Fill(fanBase, p.Tickets), ratio = fill / BaseFill(fanBase);
        long tickets = R(revenue * TicketShare * TicketPrice(p.Tickets) * ratio);
        long food = R(revenue * FoodShare * FoodTake(p.Food) * ratio);
        long parking = R(revenue * ParkingShare * ParkingTake(p.Parking) * ratio);
        long days = R(revenue * FanDayCost * Math.Clamp(p.FanDays, 0, MaxFanDays));
        long standard = R(revenue * TicketShare) + R(revenue * FoodShare) + R(revenue * ParkingShare);
        return new MatchDay(tickets, food, parking, days, standard, fill);
    }

    /// <summary>What a club's plan adds to its season's revenue (0 at standard).</summary>
    public static long Change(ClubFinance m) => m.Business.Now.IsStandard ? 0 : Plan(m.Revenue, m.FanBase, m.Business.Now).Change;

    /// <summary>
    /// How a season under a plan moves the fan base at the summer (on top of results): cheap seats, a value menu, free parking
    /// and fan days win fans; premium prices lose them. The fan base drifts 6% a year back to 50, so a plan held for years
    /// settles about 17 times this away from where it would be.
    /// </summary>
    public static double FanDelta(BusinessPlan p)
    {
        double t = p.Tickets switch { <= -1 => 0.5, 0 => 0, 1 => -0.4, _ => -0.8 };
        double f = p.Food switch { <= -1 => 0.2, 0 => 0, _ => -0.2 };
        double k = p.Parking switch { <= -1 => 0.3, 0 => 0, _ => -0.2 };
        double d = Math.Clamp(p.FanDays, 0, MaxFanDays) switch { 0 => 0, 1 => 0.4, 2 => 0.7, 3 => 0.9, _ => 1.0 };
        return t + f + k + d;
    }

    /// <summary>A plan within the levers' ranges.</summary>
    public static BusinessPlan Clamp(BusinessPlan p) => new()
    {
        Tickets = Math.Clamp(p.Tickets, -1, 2), Food = Math.Clamp(p.Food, -1, 1), Parking = Math.Clamp(p.Parking, -1, 1),
        FanDays = Math.Clamp(p.FanDays, 0, MaxFanDays),
    };

    private static long R(double v) => (long)Math.Round(v / 10_000) * 10_000;
}
