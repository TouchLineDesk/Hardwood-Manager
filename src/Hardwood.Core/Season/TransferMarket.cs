using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>A club's answer to a transfer bid.</summary>
public enum BidAnswer { Accepted, Countered, Rejected }

public sealed record BidReply(BidAnswer Answer, long Fee, string Text);

/// <summary>A completed transfer, for the news and the Transactions page.</summary>
public sealed record TransferRecord(DateOnly Date, int PlayerId, string Player, int FromTeamId, string From, int ToTeamId, string To, long Fee);

/// <summary>
/// The transfer market (decision #87): clubs buy players who are under contract. A player's value follows his market
/// wage in his league, his age and upside, and the years left on his deal; the selling club wants more for a key player
/// and less when it needs the money or the deal is about to run out. A release clause (the contract's buyout) forces
/// the sale once a bid meets it. After the fee is agreed the player still has to agree personal terms.
/// </summary>
public static class TransferMarket
{
    /// <summary>What a player is worth on the market, in his current league's money.</summary>
    public static long Valuation(Player p, DateOnly on, double sellerScale, Finance fin)
    {
        double wage = ContractMarket.MarketSalary(p.CurrentAbility, p.Age(on), fin) * sellerScale;
        int years = Math.Clamp(p.Contract?.Years ?? 1, 1, 3);
        int age = p.Age(on);
        double ageF = age <= 24 ? 1.6 : age <= 27 ? 1.3 : age <= 30 ? 1.0 : 0.6;
        double upside = Math.Clamp((p.PotentialAbility - p.CurrentAbility) / 40.0, 0, 1);
        return Round(Math.Max(wage * 0.25, wage * years * 0.5 * ageF * (1 + upside * 0.5)));
    }

    /// <summary>Is he one of the club's three best players?</summary>
    public static bool KeyPlayer(Player p, Team club) =>
        club.Roster.OrderByDescending(x => x.CurrentAbility).Take(3).Contains(p);

    /// <summary>The fee the selling club wants.</summary>
    public static long Ask(Player p, Team seller, DateOnly on, double sellerScale, Finance fin)
    {
        double ask = Valuation(p, on, sellerScale, fin);
        if (KeyPlayer(p, seller)) ask *= 1.4;
        if (seller.Money.Ready && seller.Money.Balance < 0) ask *= 0.85;   // needs the money
        if ((p.Contract?.Years ?? 0) <= 1) ask *= 0.6;                     // he'd leave for nothing next summer
        return Round(ask);
    }

    /// <summary>The selling club's answer to a bid: the release clause forces it; otherwise accept the ask, counter near it, or refuse.</summary>
    public static BidReply Answer(Player p, Team seller, long bid, DateOnly on, double sellerScale, Finance fin)
    {
        long clause = p.Contract?.Buyout ?? 0;
        if (clause > 0 && bid >= clause)
            return new(BidAnswer.Accepted, bid, $"{seller.FullName} accept: your bid meets {p.LastName}'s release clause (${clause / 1e6:F2}M).");
        long ask = Ask(p, seller, on, sellerScale, fin);
        if (bid >= ask) return new(BidAnswer.Accepted, bid, $"{seller.FullName} accept your bid of ${bid / 1e6:F2}M for {p.Name}.");
        if (bid >= ask * 0.75)
            return new(BidAnswer.Countered, ask, $"{seller.FullName} want more for {p.Name}: they'd sell for ${ask / 1e6:F2}M.");
        return new(BidAnswer.Rejected, ask, $"{seller.FullName} reject the bid out of hand. {p.Name} isn't leaving for that.");
    }

    /// <summary>
    /// Will the player talk to the buying club at all? Good players won't drop to a clearly weaker league
    /// (unless they're near the end of their careers).
    /// </summary>
    public static string? WontJoin(Player p, double sellerScale, double buyerScale, DateOnly on) =>
        buyerScale < sellerScale * 0.8 && p.CurrentAbility >= 105 && p.Age(on) < 32
            ? $"{p.Name} doesn't want to move to a weaker league." : null;

    private static long Round(double v) => (long)Math.Round(v / 10_000) * 10_000;
}
