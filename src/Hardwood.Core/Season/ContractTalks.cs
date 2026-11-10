using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>What a player wants: a yearly wage and a number of years.</summary>
public sealed record ContractDemand(long Wage, int Years);

/// <summary>The player's answer to an offer.</summary>
public sealed record TalkReply(bool Accepted, bool WalkedAway, ContractDemand Ask, string Text);

/// <summary>
/// Contract talks, offer and counter (decision #89). A player's demand starts from the market rate for his ability and
/// age in the club's league, then moves with who he is and where he'd be: loyal players give their club a discount
/// when renewing, unhappy ones want more (or won't stay), stars ask more to join a weak team, and ambitious players
/// want to be paid like it. Offers close to the demand are accepted, reasonable ones get a counter (he gives a little
/// each round), and repeated low offers end the talks.
/// </summary>
public static class ContractTalks
{
    /// <summary>Rounds of low offers a player puts up with before walking away.</summary>
    public const int Patience = 3;

    /// <summary>The ability of a club's best eight (what a player judges a team by).</summary>
    public static double Level(Team t) =>
        t.Roster.Select(p => (double)p.CurrentAbility).OrderByDescending(x => x).Take(8).DefaultIfEmpty(80).Average();

    /// <summary>The years a player would like: long security when young or in his prime, short deals when older.</summary>
    public static int PreferredYears(Player p, DateOnly on)
    {
        int age = p.Age(on);
        return age <= 25 ? 4 : age <= 29 ? 3 : age <= 32 ? 2 : 1;
    }

    /// <summary>
    /// What the player asks of this club. <paramref name="salaryScale"/> is the league's wage level (1 for the NABL).
    /// <paramref name="renewal"/>: he's already at the club.
    /// </summary>
    public static ContractDemand Demand(Player p, Team club, DateOnly on, double salaryScale, Finance fin, bool renewal)
    {
        double wage = ContractMarket.MarketSalary(p.CurrentAbility, p.Age(on), fin) * salaryScale;
        var h = p.Hidden;
        if (renewal)
        {
            wage *= 1 - Math.Clamp(h.Loyalty - 10, 0, 10) * 0.012;        // loyal players give up to 12%
            if (p.Morale < 45) wage *= 1.08 + (45 - p.Morale) / 100.0;   // unhappy: pay me to stay
        }
        double gap = p.CurrentAbility - Level(club);
        if (gap > 8) wage *= 1 + Math.Min(0.2, (gap - 8) / 100.0);       // too good for this team
        wage *= 1 + Math.Max(0, h.Ambition - 12) * 0.01;                 // ambitious: wants top money
        long min = (long)(fin.MinSalary * salaryScale);
        return new ContractDemand(Math.Max(min, Round(wage)), PreferredYears(p, on));
    }

    /// <summary>
    /// The player's reply to an offer. <paramref name="round"/> counts this player's rounds of talks so far and is
    /// updated (reset when he agrees or walks). <paramref name="demand"/> is his opening ask.
    /// </summary>
    public static TalkReply Respond(Player p, ContractDemand demand, long wage, int years, ref int round)
    {
        // Each round he comes down a little toward a fair offer: 4% of the gap per round, up to 3 rounds.
        double give = 1 - 0.04 * Math.Min(round, 3);
        long ask = Round(demand.Wage * give);
        var counter = new ContractDemand(ask, demand.Years);
        bool yearsOk = Math.Abs(years - demand.Years) <= 1;
        if (wage >= ask * 0.98 && yearsOk)
        {
            round = 0;
            return new TalkReply(true, false, counter, $"{p.Name} accepts: ${wage / 1e6:F2}M a year for {years} year{(years == 1 ? "" : "s")}.");
        }
        round++;
        if (wage < ask * 0.8 && round >= Patience)
        {
            round = 0;
            return new TalkReply(false, true, counter, $"{p.Name}'s agent has ended the talks: your offers were nowhere near what he's worth.");
        }
        string why = wage < ask * 0.8 ? "That's well short." : wage < ask * 0.98 ? "Close, but not there yet." : "The money's right, but not the length.";
        return new TalkReply(false, false, counter,
            $"{why} {p.Name} wants ${ask / 1e6:F2}M a year for {demand.Years} year{(demand.Years == 1 ? "" : "s")}.");
    }

    private static long Round(double v) => (long)Math.Round(v / 10_000) * 10_000;
}
