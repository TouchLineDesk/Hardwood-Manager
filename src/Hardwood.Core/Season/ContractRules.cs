using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// H122 (run 122, decision #928): an owner stretches the wage budget to keep the club's own young star. Without Bird rights
/// or restricted free agency (the open market, #80), a contender lost its 23-year-old star to the budget: the Spurs (seed
/// 2026, May 2028) had $1.2-2.4M of room when Stephon Castle (23, CA 161-162, PA 188) asked $45.5-48.3M, and he walked.
/// A player whose deal is up with his own club, CA 150+ and under 26, may be re-signed up to
/// <see cref="Stretch"/> of the wage budget over it, if the owner isn't in debt; the owner then raises the budget to cover him.
/// </summary>
public static class ContractRules
{
    public const int YoungStarAbility = 150;
    public const int YoungStarMaxAge = 25;
    /// <summary>How far over the wage budget the owner goes for his own young star, as a share of the budget.</summary>
    public const double Stretch = 0.35;

    public static bool IsYoungStar(Player p, DateOnly on) => p.CurrentAbility >= YoungStarAbility && p.Age(on) <= YoungStarMaxAge;

    /// <summary>The extra wage room for keeping <paramref name="p"/> in contracts starting in <paramref name="season"/>:
    /// 0 unless he is the club's own young star and the owner can pay.</summary>
    public static long KeepOwnAllowance(Team team, Player p, DateOnly on, int season) =>
        p.TeamId == team.Id && IsYoungStar(p, on) ? Allowance(team, season) : 0;

    /// <summary>The club's stretch left this summer for a young star of its own: 35% over the budget as it was before the first
    /// stretch (review of #186: it mustn't compound), less what earlier stretches took; 0 for an owner in debt or a budget not set.</summary>
    public static long Allowance(Team team, int season)
    {
        if (team.Money is not { Ready: true, Balance: >= 0 } m) return 0;
        long b = m.StretchSeason == season && m.StretchBase > 0 ? m.StretchBase : m.WageBudget;
        return Math.Max(0, (long)(b * (1 + Stretch)) - m.WageBudget);
    }

    /// <summary>After keeping a young star: the owner raises the budget to cover the deals, remembering the base he stretched from.</summary>
    public static void OwnerCovers(Team team, long committed, int season)
    {
        var m = team.Money;
        if (committed <= m.WageBudget) return;
        if (m.StretchSeason != season || m.StretchBase <= 0) { m.StretchBase = m.WageBudget; m.StretchSeason = season; }
        Budgets.OwnerFunds(team, committed);
    }
}
