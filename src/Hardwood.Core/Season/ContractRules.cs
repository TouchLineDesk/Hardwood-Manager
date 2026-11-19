using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// H122 (run 122, decision #928): an owner stretches the wage budget to keep the club's own young star. Without Bird rights
/// or restricted free agency (the open market, #80), a contender lost its 23-year-old star to the budget: the Spurs (seed
/// 2026, May 11 2028) had $2.4M of room when Stephon Castle (CA 162, PA 188) asked $48.29M, and he walked.
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

    /// <summary>The extra wage room for keeping <paramref name="p"/>: 0 unless he is the club's own young star and the owner can pay.</summary>
    public static long KeepOwnAllowance(Team team, Player p, DateOnly on) =>
        p.TeamId == team.Id && IsYoungStar(p, on) ? Allowance(team) : 0;

    /// <summary>The club's stretch for a young star of its own (0 for an owner in debt or a budget not set yet).</summary>
    public static long Allowance(Team team) =>
        team.Money is { Ready: true, Balance: >= 0 } m ? (long)(m.WageBudget * Stretch) : 0;
}
