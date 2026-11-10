using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// Club money (decision #88): revenue from the league's market, the arena, the fan base and recent success;
/// the owner turns it into a wage budget (ambitious owners spend more of it) and a transfer budget.
/// Calibrated so an average NABL club earns about $245M and may pay about $160M in wages (the old payrolls
/// averaged $150M), and clubs abroad earn in proportion to their league's wages.
/// </summary>
public static class Budgets
{
    /// <summary>An average NABL club's revenue.</summary>
    public const long NablRevenue = 245_000_000;
    /// <summary>Overseas clubs' revenue relative to their league's wage scale (their payrolls sit at ~0.85 of the NABL's once scaled).</summary>
    public const double OverseasFactor = 0.85;

    /// <summary>Share of revenue the owner lets the club spend on wages: 50% (careful) to 80% (win-now).</summary>
    /// <remarks>Kept narrow so the richest, keenest club tops out near the old apron (~$220M): without it a rich winner
    /// kept every star and won 5-6 titles a decade (balance audit, Nov 9).</remarks>
    public static double WageShare(Jobs.Owner o) => Math.Clamp(0.65 + (o.Ambition - 11) * 0.012, 0.55, 0.74);

    /// <summary>Base revenue of a league's average club: the NABL, or an overseas league by its wage scale.</summary>
    public static long LeagueRevenue(double salaryScale) => salaryScale >= 0.999 ? NablRevenue : (long)(NablRevenue * salaryScale * OverseasFactor);

    /// <summary>A season's revenue: league market × (fan base, arena size against the league's typical arena, last season's record).</summary>
    public static long Revenue(Team t, long leagueRevenue, double typicalArena, double lastWinPct)
    {
        double fan = Math.Clamp(t.Money.FanBase, 0, 100) / 100.0;
        double arena = Math.Clamp(t.ArenaCapacity / Math.Max(1, typicalArena), 0.5, 1.5);
        double f = 0.85 + 0.3 * fan + 0.08 * (arena - 1) + 0.12 * (lastWinPct - 0.5);
        return Round(leagueRevenue * Math.Clamp(f, 0.8, 1.2));
    }

    /// <summary>
    /// First time a club is seen (a new career, a club that just appeared, or a save from before the money model):
    /// a fan base from its arena and a little luck, revenue, and a wage budget that at least covers the wages it already pays.
    /// </summary>
    public static void Initialize(IReadOnlyList<Team> clubs, double salaryScale, Rng rng)
    {
        if (clubs.Count == 0) return;
        double typical = clubs.Average(t => (double)t.ArenaCapacity);
        long lr = LeagueRevenue(salaryScale);
        foreach (var t in clubs)
        {
            var m = t.Money;
            if (m.Ready) continue;
            m.FanBase = Math.Clamp(50 + (t.ArenaCapacity / typical - 1) * 60 + rng.NextGaussian(0, 10), 15, 95);
            m.Revenue = Revenue(t, lr, typical, 0.5);
            long payroll = ContractMarket.Payroll(t);
            m.WageBudget = Math.Max(Round(m.Revenue * WageShare(t.Owner)), Round(payroll * 1.04));
            m.TransferBudget = Round(m.Revenue * 0.04 * (0.5 + t.Owner.Ambition / 20.0));
            m.Balance = Round(m.Revenue * rng.NextDouble() * 0.2);
            m.Ready = true;
        }
    }

    /// <summary>
    /// End of a season: the books close (revenue in, wages and staff out), the fan base moves with results, and
    /// the owner sets next season's budgets. Clubs deep in debt are made to cut back.
    /// </summary>
    public static void Summer(IReadOnlyList<Team> clubs, double salaryScale, Func<Team, double> winPct, Func<Team, bool> champion)
    {
        if (clubs.Count == 0) return;
        double typical = clubs.Average(t => (double)t.ArenaCapacity);
        long lr = LeagueRevenue(salaryScale);
        foreach (var t in clubs)
        {
            var m = t.Money;
            double wp = winPct(t);
            long staff = t.Staff.Sum(s => s.Salary);
            m.Balance += m.Revenue - ContractMarket.Payroll(t) - staff;
            m.FanBase = Math.Clamp(m.FanBase + (wp - 0.5) * 10 + (champion(t) ? 3 : 0) + (50 - m.FanBase) * 0.06, 5, 100);
            m.Revenue = Revenue(t, lr, typical, wp);
            double share = WageShare(t.Owner);
            if (m.Balance < -m.Revenue * 0.25) share -= 0.06;       // in debt: the owner cuts back
            else if (m.Balance > m.Revenue * 0.5) share += 0.02;    // flush: a little more to spend
            m.WageBudget = Round(m.Revenue * share);
            m.TransferBudget = Round(Math.Max(0, m.Revenue * 0.04 * (0.5 + t.Owner.Ambition / 20.0) + Math.Max(0, m.Balance) * 0.15));
            m.Ready = true;
        }
    }

    /// <summary>Wages the club has room for under its budget right now.</summary>
    public static long Room(Team t) => t.Money.WageBudget - ContractMarket.Payroll(t);

    public static string Describe(Team t)
    {
        static string M(long v) => Math.Abs(v) >= 10_000_000 ? $"${v / 1e6:F0}M" : $"${v / 1e6:F1}M";
        long pay = ContractMarket.Payroll(t);
        return $"wages {M(pay)} of a {M(t.Money.WageBudget)} budget" + (pay > t.Money.WageBudget ? $" ({M(pay - t.Money.WageBudget)} over)" : $" · {M(t.Money.WageBudget - pay)} to spend");
    }

    private static long Round(double v) => (long)Math.Round(v / 100_000) * 100_000;
}
