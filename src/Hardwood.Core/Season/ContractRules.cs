using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// Keeping your own players over the wage budget.
/// <para><b>D81 (run 159, Danny, Nov 22): no salary cap in the NBA.</b> "Teams build their revenue through the game and can
/// apply that to salaries for the players … keep the ones they want." An NBA club may keep any of its own players up to what
/// its finances can carry every season (<see cref="NbaMeans"/>): its income less staff wages and running costs, plus a share of its savings.
/// The 35% stretch (#928) no longer applies in the NBA. A club in debt keeps to its budget.</para>
/// <para><b>Everywhere else (H122, run 122, decision #928):</b> an owner stretches the wage budget by up to <see cref="Stretch"/>
/// to keep the club's own young star (CA 150+ and under 26), if the owner isn't in debt; the owner then raises the budget to
/// cover him. (Without Bird rights or restricted free agency, the open market #80, a contender lost its 23-year-old star:
/// the Spurs, seed 2026, May 2028, had $1.2-2.4M of room when Stephon Castle asked $45.5-48.3M, and he walked.)</para>
/// </summary>
public static class ContractRules
{
    public const int YoungStarAbility = 150;
    public const int YoungStarMaxAge = 25;
    /// <summary>Outside the NBA: how far over the wage budget the owner goes for his own young star, as a share of the budget.</summary>
    public const double Stretch = 0.35;
    /// <summary>D81: the savings an NBA club puts into wages are spread over this many seasons (the longest contract), so a deal
    /// kept at the club's means can be paid every year of it without the club going into debt.</summary>
    public const int BankYears = 5;
    /// <summary>…and never more than this share of a season's income a year.</summary>
    public const double BankCapOfIncome = 0.10;

    public static bool IsYoungStar(Player p, DateOnly on) => p.CurrentAbility >= YoungStarAbility && p.Age(on) <= YoungStarMaxAge;

    /// <summary>D81: the most an NBA club's finances can carry in wages, every season of a deal: its income (revenue and any
    /// parachute) less its staff wages and its facilities' running costs, plus a fifth of the savings it holds above the owner's
    /// reserve (<see cref="Franchise.Spendable"/>; at most a tenth of its income). 0 when the books aren't set or the club is in
    /// debt. (Review of run 159: a third of the whole balance, counted as yearly money, put a club at its means into debt
    /// within a few seasons, and in debt the summer budget no longer covers the wages, so it read "over budget".)</summary>
    public static long NbaMeans(Team team)
    {
        if (team.Money is not { Ready: true, Balance: >= 0 } m) return 0;
        long staff = team.Staff.Sum(s => s.Salary);
        long upkeep = Franchise.Upkeep(m.Facilities, Budgets.LeagueRevenue(1.0));
        long bank = (long)Math.Min(Franchise.Spendable(team) / (double)BankYears, m.Income * BankCapOfIncome);
        return Math.Max(0, m.Income - staff - upkeep + bank);
    }

    /// <summary>D81: how far over the wage budget an NBA club may go to keep one of its own players: up to its means.</summary>
    public static long NbaKeepAllowance(Team team, Player p) =>
        p.TeamId == team.Id ? Math.Max(0, NbaMeans(team) - team.Money.WageBudget) : 0;

    /// <summary>Outside the NBA: the extra wage room for keeping <paramref name="p"/> in contracts starting in
    /// <paramref name="season"/>: 0 unless he is the club's own young star and the owner can pay.</summary>
    public static long KeepOwnAllowance(Team team, Player p, DateOnly on, int season) =>
        p.TeamId == team.Id && IsYoungStar(p, on) ? Allowance(team, season) : 0;

    /// <summary>Outside the NBA: the club's stretch left this summer for a young star of its own: 35% over the budget as it was
    /// before the first stretch (review of #186: it mustn't compound), less what earlier stretches took; 0 for an owner in
    /// debt or a budget not set.</summary>
    public static long Allowance(Team team, int season)
    {
        if (team.Money is not { Ready: true, Balance: >= 0 } m) return 0;
        long b = m.StretchSeason == season && m.StretchBase > 0 ? m.StretchBase : m.WageBudget;
        return Math.Max(0, (long)(b * (1 + Stretch)) - m.WageBudget);
    }

    /// <summary>After keeping a player over the budget: the owner raises the budget to cover the deals, remembering the base
    /// he stretched from (no-op when the deals fit).</summary>
    public static void OwnerCovers(Team team, long committed, int season)
    {
        var m = team.Money;
        if (committed <= m.WageBudget) return;
        if (m.StretchSeason != season || m.StretchBase <= 0) { m.StretchBase = m.WageBudget; m.StretchSeason = season; }
        Budgets.OwnerFunds(team, committed);
    }
}
