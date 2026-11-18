using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>Salary expectations and roster valuation shared by the generator, re-signing, free agency and (later) trades.</summary>
public static class ContractMarket
{
    /// <summary>Market salary from ability and age. CA 70 → ~$1.2M, CA 120 → ~$9M, CA 160 → ~$30M, capped at ~$45M. A full roster of average players sits at ~75% of the hard cap.</summary>
    public static long MarketSalary(int ca, int age, Finance finance)
    {
        double m = Math.Pow(Math.Max(0, ca - 55) / 100.0, 2.4) * 36.0 + 1.1;
        if (age < 23) m *= 0.55;      // rookie-scale discount
        if (age >= 33) m *= 0.75;     // short-term veteran deals
        long salary = (long)Math.Round(Math.Min(m, 45.0) * 1_000_000 / 10_000) * 10_000;
        return Math.Max(salary, finance.MinSalary);
    }

    /// <summary>
    /// H61 (run 110, the contracts-economy pass): the least a player takes to play in a league, from what he could earn in
    /// the best league that would have him. Wages below the top league are its market times the league's scale, so a
    /// star's ask fell with the league (Jarrett Allen, CA 145, for $1.05M in tier 4; Giannis renewing in the G League for
    /// $2.35M). Each of <paramref name="options"/> is a league open to him (the top league, and the leagues where he counts
    /// as a local) by its level (mean CA) and wage scale; he qualifies for one when he is <see cref="StarMargin"/> CA above
    /// its level, as its own stars are. He asks <see cref="OptionShare"/> of what the best of those pays him, so he never
    /// prices himself out of the league that sets his floor, only out of leagues that pay well below it.
    /// </summary>
    public static long AskingFloor(Player p, DateOnly on, IEnumerable<(double Level, double Scale)> options)
    {
        double best = 0;
        foreach (var (level, scale) in options)
            if (level > 0 && p.CurrentAbility >= level + StarMargin && scale > best) best = scale;
        if (best <= 0) return 0;
        long top = MarketSalary(p.CurrentAbility, p.Age(on), Finance.HardCapDefault);
        return (long)Math.Round(top * best * OptionShare / 10_000) * 10_000;
    }

    /// <summary>How far above a league's level (CA) a player is among its very best (the best in Spain, Japan or the G League are
    /// 40-45 above their league's mean), so a club there would pay its top wage for him.</summary>
    public const double StarMargin = 40;
    /// <summary>The share of his best option's wage a player asks of any other league.</summary>
    public const double OptionShare = 0.75;

    /// <summary>
    /// K1 (run 111, Danny's Nov 17 decision): a relegation release clause is a contract term the very best players ask for in
    /// the top league (about its top 15%: CA <see cref="ReleaseClauseCa"/>+), and a few ambitious near-stars too. The clause
    /// lets him leave on a free if his club goes down.
    /// </summary>
    public static bool AsksForReleaseClause(Player p) =>
        p.CurrentAbility >= ReleaseClauseCa || (p.Hidden.Ambition >= 17 && p.CurrentAbility >= ReleaseClauseCa - 10);

    /// <summary>
    /// K1 (run 111): deals already running when the clause arrives (a new real-world career, or a save from before format 13):
    /// the top <see cref="ReleaseClauseTopDeals"/> deals in the top league by salary carry it, and so does anyone who would
    /// ask for it today. Nobody else.
    /// </summary>
    public static int GrantReleaseClauses(League top)
    {
        var signed = top.Teams.SelectMany(t => t.Roster).Where(p => p.Contract != null).ToList();
        var topDeals = signed.OrderByDescending(p => p.Contract!.Salary).ThenBy(p => p.Id).Take(ReleaseClauseTopDeals).ToHashSet();
        int n = 0;
        foreach (var p in signed)
            if (topDeals.Contains(p) || AsksForReleaseClause(p)) { p.Contract!.RelegationRelease = true; n++; }
        return n;
    }

    /// <summary>How many of the top league's biggest running deals carry a release clause at the start (K1 design, part 2).</summary>
    public const int ReleaseClauseTopDeals = 60;

    /// <summary>The ability from which every top-league player asks for a relegation release clause.</summary>
    public const int ReleaseClauseCa = 148;

    /// <summary>How a front office values a player: today's ability plus upside, discounted for age.</summary>
    public static double Value(Player p, DateOnly on)
    {
        int age = p.Age(on);
        double upside = Math.Max(0, p.PotentialAbility - p.CurrentAbility);
        double ageFactor = age switch
        {
            <= 24 => 1.04,
            <= 29 => 1.00,
            <= 32 => 0.96,
            <= 34 => 0.88,
            _ => 0.78,
        };
        return (p.CurrentAbility + upside * (age <= 22 ? 0.35 : age <= 25 ? 0.2 : 0.05)) * ageFactor;
    }

    public static long Payroll(Team t) => t.Roster.Sum(p => p.Contract?.Salary ?? 0);

    public static int ContractLength(Player p, DateOnly on, Rng rng)
    {
        int age = p.Age(on);
        if (age <= 24) return rng.Next(3, 5);
        if (age <= 29) return rng.Next(2, 5);
        if (age <= 32) return rng.Next(1, 4);
        return rng.Next(1, 3);
    }
}
