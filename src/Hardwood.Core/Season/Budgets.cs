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

    /// <summary>
    /// What staff are paid in a league, relative to NABL staff (critic review #3: a tier-5 club paid NABL-style staff
    /// wages out of a $0.8M revenue and lost money every season, even going 32-2).
    /// </summary>
    public static double StaffScale(double salaryScale) => salaryScale >= 0.999 ? 1 : Math.Min(1, salaryScale * 1.5);

    /// <summary>Put a club's staff on its league's pay scale.</summary>
    public static void ScaleStaff(Team t, double salaryScale)
    {
        foreach (var st in t.Staff)
            st.Salary = Math.Max(1_000, (long)Math.Round(Generation.StaffGenerator.SalaryFor(st) * StaffScale(salaryScale) / 1000) * 1000);
    }

    /// <summary>K1 (run 111): a league's media money per club, the part of its revenue a parachute is a share of (about 40%, as
    /// the NBA's national TV money is of a club's revenue).</summary>
    public static long MediaMoney(double salaryScale) => Round(LeagueRevenue(salaryScale) * MediaShare);
    public const double MediaShare = 0.4;
    /// <summary>K1 (run 111): the EPL's parachute schedule, as shares of the media money of the league the club left.</summary>
    public static readonly double[] ParachuteShares = { 0.55, 0.45, 0.20 };
    /// <summary>A club that spent only one season up gets the first two years (as in the EPL).</summary>
    public const int ShortParachuteYears = 2;

    /// <summary>Base revenue of a league's average club: the NABL, or an overseas league by its wage scale.</summary>
    public static long LeagueRevenue(double salaryScale) => salaryScale >= 0.999 ? NablRevenue : (long)(NablRevenue * salaryScale * OverseasFactor);

    /// <summary>A season's revenue: league market × (fan base, arena size against the league's typical arena, last season's record).</summary>
    public static long Revenue(Team t, long leagueRevenue, double typicalArena, double lastWinPct)
    {
        double fan = Math.Clamp(t.Money.FanBase, 0, 100) / 100.0;
        double arena = Math.Clamp(t.ArenaCapacity / Math.Max(1, typicalArena), 0.5, 1.5);
        // Item M (#281): premium seating and a renovated arena earn more than the league's average arena.
        double premium = 0.02 * t.Money.Facilities.Arena.Edge;
        double f = 0.85 + 0.3 * fan + 0.08 * (arena - 1) + 0.12 * (lastWinPct - 0.5) + premium;
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
        // Item M (#281): facilities, seeded per club without touching the random stream.
        foreach (var t in clubs) Franchise.Seed(t, typical);
        Franchise.SetEdges(clubs);
    }

    /// <summary>
    /// End of a season: the books close (revenue in, wages and staff out), the fan base moves with results, and
    /// the owner sets next season's budgets. Clubs deep in debt are made to cut back.
    /// </summary>
    /// <param name="endedSeason">The season that just finished: contracts running past it are already committed, and an
    /// owner who isn't in debt won't set a budget below them (critic review #3: a club whose revenue dipped was "over
    /// budget" all season on deals it signed within budget the year before). -1 leaves budgets purely to revenue.</param>
    /// <param name="pending">College seniors who committed to a club during the season, with their agreed wage: they sign
    /// on graduation day, after these budgets are set, so the floor makes room for them (run 19, #256).</param>
    public static void Summer(IReadOnlyList<Team> clubs, double salaryScale, Func<Team, double> winPct, Func<Team, bool> champion, int endedSeason = -1, bool fillRoster = false,
        Func<Team, (long wages, int count)>? pending = null)
    {
        if (clubs.Count == 0) return;
        double typical = clubs.Average(t => (double)t.ArenaCapacity);
        long lr = LeagueRevenue(salaryScale);
        foreach (var t in clubs)
        {
            var m = t.Money;
            double wp = winPct(t);
            long staff = t.Staff.Sum(s => s.Salary);
            m.Balance += m.Income - ContractMarket.Payroll(t) - staff;
            // Item M (#281): running costs, projects, ageing, and the owner's investments (or your proposals).
            Franchise.Summer(t, lr, typical, endedSeason + 1);
            // D81 (run 169): the season's match-day plan moves the fan base (0 at standard), then next season's prices come in.
            m.FanBase = Math.Clamp(m.FanBase + (wp - 0.5) * 10 + (champion(t) ? 3 : 0) + (50 - m.FanBase) * 0.06 + 0.6 * m.Facilities.Arena.Edge
                + Business.FanDelta(m.Business.Now), 5, 100);
            m.Business.Now = Business.Clamp(m.Business.Next);
            m.Business.BooksClosed = true;
            m.Revenue = Revenue(t, lr, typical, wp);
            // K1 (run 111): an EPL-style parachute pays the next share of its schedule; an older save's (no base) halves as before.
            if (m.ParachuteYears > 0)
            {
                m.ParachuteYears--; m.ParachuteYear++;
                m.Parachute = m.ParachuteYears <= 0 ? 0
                    : m.ParachuteBase > 0 ? Round(m.ParachuteBase * ParachuteShares[Math.Min(m.ParachuteYear, ParachuteShares.Length) - 1])
                    : m.Parachute / 2;
            }
            if (m.ParachuteYears <= 0) { m.Parachute = 0; m.ParachuteBase = 0; m.ParachuteYear = 0; }
            m.ParachuteCarried = 0;
            m.SeasonsAtLevel++;
            double share = WageShare(t.Owner);
            if (m.Balance < -m.Revenue * 0.25) share -= 0.06;       // in debt: the owner cuts back
            else if (m.Balance > m.Revenue * 0.5) share += 0.02;    // flush: a little more to spend
            m.WageBudget = Round(m.Income * share);
            if (endedSeason >= 0 && m.Balance >= 0)
            {
                // Contract.Years counts the seasons left including the one just ended (it's decremented after the books close),
                // so a deal that runs into next season has more than one left.
                var signed = t.Roster.Where(p => p.Contract is { Years: > 1 }).ToList();
                long committed = signed.Sum(p => p.Contract!.Salary);
                // Committed seniors are signings the owner already approved (the offer was checked against the budget),
                // so they count as signed: without them a commitment signed on top of the floor and the club opened the
                // season over budget (run 18: SAS and POR, 29 weeks each).
                var (owed, owedCount) = pending?.Invoke(t) ?? (0, 0);
                committed += owed;
                int filled = signed.Count + owedCount;
                // In the real NBA, plus minimum deals to fill the roster to its legal size (run 17, #221): real payrolls
                // sit at this floor, so a floor of only what's signed left a club over budget the moment it made up its
                // numbers, and the owner then marked "keep wages within the budget" as failed. (Fictional leagues open
                // inside their budgets and keep the old floor: the balance audit moved when they got it too.)
                if (fillRoster) committed += Math.Max(0, MinRoster - filled) * (long)(MinWage * Math.Min(1, salaryScale));
                m.WageBudget = Math.Max(m.WageBudget, (long)Math.Ceiling(committed / 100_000.0) * 100_000);
            }
            // Deep debt: a transfer embargo until the balance recovers (critic review, Nov 10).
            m.Embargo = m.Balance < -m.Revenue * 0.5;
            m.TransferBudget = m.Embargo ? 0 : Round(Math.Max(0, m.Revenue * 0.04 * (0.5 + t.Owner.Ambition / 20.0) + Math.Max(0, m.Balance) * 0.15));
            m.Ready = true;
        }
        Franchise.SetEdges(clubs);
    }

    /// <summary>
    /// A club that changed league (promotion or relegation): revenue and budgets for its new league straight away.
    /// Its balance and fan base carry over. Going down (critic review, Nov 10): every contract has a relegation
    /// clause that halves the wage (never below what the new league would pay him), and the club gets parachute
    /// money worth half the revenue it lost, then a quarter, so it can see out its old contracts; a parachute still being
    /// paid from an earlier drop carries over on top (#363). Going up, the parachute stops.
    /// </summary>
    public static void Rescale(Team t, double salaryScale, double typicalArena, double oldScale = 0)
    {
        var m = t.Money;
        long oldBudget = m.Ready ? m.WageBudget : 0;
        long oldRevenue = m.Ready ? m.Revenue : (oldScale > 0 ? Revenue(t, LeagueRevenue(oldScale), typicalArena, 0.5) : 0);
        m.Revenue = Revenue(t, LeagueRevenue(salaryScale), typicalArena, 0.5);
        if (oldScale > salaryScale)
        {
            double ratio = salaryScale / oldScale;
            foreach (var p in t.Roster)
                if (p.Contract is { } c)
                    c.Salary = (long)Math.Round(Math.Max(c.Salary * 0.5, c.Salary * ratio) / 1000) * 1000;
            // K1 (run 111, Danny's Nov 17 decision): EPL-style parachutes, shares of the media money of the league the club left
            // (55%, 45%, 20% over three seasons; a club that spent only one season up gets the first two). A second drop while a
            // parachute is still being paid (#363) keeps the old one, from a richer league, on its schedule (a club that fell
            // NBA → tier 2 → tier 3 in two summers otherwise had tier-3 money and half-cut NBA contracts).
            if (m.ParachuteYears > 0) m.ParachuteCarried = m.Parachute;
            else
            {
                m.ParachuteBase = MediaMoney(oldScale);
                m.ParachuteYear = 1;
                m.ParachuteYears = m.SeasonsAtLevel <= 1 ? ShortParachuteYears : ParachuteShares.Length;
                m.Parachute = Round(m.ParachuteBase * ParachuteShares[0]);
                m.ParachuteCarried = 0;
            }
        }
        else if (oldScale > 0 && oldScale < salaryScale) { m.Parachute = 0; m.ParachuteYears = 0; m.ParachuteCarried = 0; m.ParachuteBase = 0; m.ParachuteYear = 0; }
        if (oldScale > 0 && Math.Abs(oldScale - salaryScale) > 1e-9) m.SeasonsAtLevel = 0;
        if (oldScale > 0 && Math.Abs(oldScale - salaryScale) > 1e-9 && t.Staff.Count > 0) ScaleStaff(t, salaryScale);
        m.WageBudget = Round(m.Income * WageShare(t.Owner));
        // Going down (#363, run 31): the contracts the club carries were signed within its old budget and already cut by
        // the relegation clauses, so an owner who isn't in debt funds them, as the summer budget does (critic review #3).
        // Without it a relegated club was "over budget" (and failing the board's wage goal) for deals it couldn't shed.
        if (oldScale > salaryScale && m.Balance >= 0)
            m.WageBudget = Math.Max(m.WageBudget, (long)Math.Ceiling(ContractMarket.Payroll(t) / 100_000.0) * 100_000);
        // Going up (H86, run 104): a promoted club never has less to spend than it had in the league below. Its old budget
        // could include a parachute that stops on promotion (and the payroll floor from a relegation), so the new league's
        // revenue alone gave the Kings $14.5M in the G League after $28.8M in the ABA, $6.5M under the contracts they
        // carried up. A solvent owner keeps the old budget and funds the contracts the club brings with it.
        if (oldScale > 0 && oldScale < salaryScale && m.Balance >= 0)
            m.WageBudget = Math.Max(m.WageBudget, Math.Max(oldBudget, (long)Math.Ceiling(ContractMarket.Payroll(t) / 100_000.0) * 100_000));
        m.TransferBudget = Round(m.Revenue * 0.04 * (0.5 + t.Owner.Ambition / 20.0) + Math.Max(0, m.Balance) * 0.15);
        m.Ready = true;
    }

    /// <summary>The roster a club must carry (Finance.RosterMin) and the league minimum (Finance.MinSalary), for the budget floor.</summary>
    private const int MinRoster = 13;
    private static long MinWage => Finance.HardCapDefault.MinSalary;

    /// <summary>
    /// H56 (run 87): the owner funds wages the coach can't refuse: the contracts he inherits when he takes the job, and the
    /// minimum deals that make up a short roster. An owner who isn't in debt raises the wage budget to cover
    /// <paramref name="committed"/> (rounded up to $0.1M), as #374's floor does at the summer and on relegation; an owner in
    /// debt doesn't. Returns how much the budget went up (0 if it already covered them).
    /// Before, a solvent club's budget could sit below wages nobody could shed: the Knicks hired a coach on Oct 21 with
    /// $190.2M of contracts on a $181.0M budget (28 weeks "over"), and the Aces' sporting director filled the roster past a
    /// budget set at exactly the signed deals (30 weeks over by $0.6M), so the board judged the coach on a budget he couldn't meet.
    /// </summary>
    public static long OwnerFunds(Team t, long committed)
    {
        var m = t.Money;
        if (!m.Ready || m.Balance < 0 || committed <= m.WageBudget) return 0;
        long was = m.WageBudget;
        m.WageBudget = (long)Math.Ceiling(committed / 100_000.0) * 100_000;
        return m.WageBudget - was;
    }

    /// <summary>Wages the club has room for under its budget right now.</summary>
    public static long Room(Team t) => t.Money.WageBudget - ContractMarket.Payroll(t);

    /// <summary>Money as the budget lines show it: "$58M", or "$4.2M" under ten million.</summary>
    // H98 (run 109): one decimal under $100M ("$12.6M", not "$13M"), so a budget, its wages and what's left add up;
    // a whole number drops its ".0" from $10M up ("$34M").
    // H115 (run 117): wages under $100K (tier 5) read in thousands, not "$0.0M".
    public static string Money(long v) => v != 0 && Math.Abs(v) < 100_000 ? $"${v / 1e3:0}K" : Math.Abs(v) >= 100_000_000 ? $"${v / 1e6:F0}M" : Math.Abs(v) >= 10_000_000 ? $"${v / 1e6:0.#}M" : $"${v / 1e6:F1}M";

    /// <summary>The wage line. <paramref name="payroll"/> defaults to every contract on the roster; the summer passes the
    /// committed wages instead (H45, run 81: <see cref="GameSession.MyBudgetLine"/>).</summary>
    public static string Describe(Team t, long? payroll = null, string label = "wages")
    {
        static string M(long v) => Money(v);
        long pay = payroll ?? ContractMarket.Payroll(t);
        return $"{label} {M(pay)} of a {M(t.Money.WageBudget)} budget" + (pay > t.Money.WageBudget ? $" ({M(pay - t.Money.WageBudget)} over)" : $" · {M(t.Money.WageBudget - pay)} to spend")
            + (t.Money.Parachute > 0 ? $" · parachute {M(t.Money.Parachute)}" : "") + (t.Money.Embargo ? " · TRANSFER EMBARGO (debt)" : "");
    }

    private static long Round(double v) => (long)Math.Round(v / 100_000) * 100_000;
}
