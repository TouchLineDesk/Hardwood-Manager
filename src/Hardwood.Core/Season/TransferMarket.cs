using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>A club's answer to a transfer bid.</summary>
public enum BidAnswer { Accepted, Countered, Rejected }

public sealed record BidReply(BidAnswer Answer, long Fee, string Text);

/// <summary>How the selling club feels about letting a player go (roadmap 2b).</summary>
public enum TransferStance { WontSell, WouldListen, WantsHimGone }

/// <summary>The selling club's stance, with a short label for a chip or column and the reason behind it.</summary>
public sealed record StanceInfo(TransferStance Stance, string Label, string Why);

/// <summary>What your scouts expect the fee to be: a range that holds the club's real price, narrower with a better head scout.</summary>
public readonly record struct FeeRange(long Low, long High)
{
    public string Text => Low == High ? $"${Low / 1e6:F2}M" : $"${Low / 1e6:F2}–{High / 1e6:F2}M";
    public string Short => Low == High ? $"${Low / 1e6:F1}M" : $"${Low / 1e6:F1}–{High / 1e6:F1}M";
    public long Mid => (Low + High) / 2 / 10_000 * 10_000;
}

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
        // Gentler past 30 (#219): a 31-year-old star kept 60% of his value before, so the real MVP runner-up with two
        // years left cost less than a role player of 24.
        double ageF = age <= 24 ? 1.6 : age <= 27 ? 1.3 : age <= 30 ? 1.0 : age <= 32 ? 0.8 : 0.6;
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
        if (Unsettled(p)) ask *= 0.85;                                     // he wants out, or is unhappy: they'll cash in
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
        var stance = Stance(p, seller);
        if (bid >= ask * (stance.Stance == TransferStance.WontSell ? 0.85 : 0.75))
            return new(BidAnswer.Countered, ask, $"{seller.FullName} want more for {p.Name}: they'd sell for ${ask / 1e6:F2}M.");
        return new(BidAnswer.Rejected, ask, $"{seller.FullName} reject the bid out of hand. {p.Name} isn't leaving for that.");
    }

    /// <summary>Unhappy in the Mood column's sense (below 50), or he's asked for a trade.</summary>
    public static bool Unsettled(Player p) => p.TradeRequested || p.Morale < 50;

    /// <summary>
    /// The selling club's stance (roadmap 2b), from the same factors as <see cref="Ask"/>, so the label never disagrees
    /// with the price: a player who wants out, or a club in debt, means they want him gone; a key player signed for two
    /// or more years means they won't sell without a premium (and a low bid gets no counter); anyone else they'd listen.
    /// </summary>
    public static StanceInfo Stance(Player p, Team seller)
    {
        int years = p.Contract?.Years ?? 0;
        if (p.TradeRequested) return new(TransferStance.WantsHimGone, "Wants him gone", $"{p.LastName} has asked for a trade, so they'll take less.");
        if (seller.Money.Ready && seller.Money.Balance < 0) return new(TransferStance.WantsHimGone, "Wants him gone", "The club is in debt and needs the money.");
        if (KeyPlayer(p, seller) && years >= 2)
            return new(TransferStance.WontSell, "Won't sell", $"One of their three best, signed for {years} more years: only a premium will move him.");
        if (p.Morale < 50) return new(TransferStance.WantsHimGone, "Wants him gone", $"{p.LastName} is unhappy there, so they'll cash in.");
        if (years <= 1) return new(TransferStance.WouldListen, "Would listen", "His deal is up next summer, so the price is low.");
        return new(TransferStance.WouldListen, "Would listen", "Not a key player for them: a fair bid will do.");
    }

    /// <summary>
    /// What your scouts expect the club to want: a range around the real asking price, ±6% with a top scout and up to ±30%
    /// with a poor one, centred on a stable per-player misjudgement (like <see cref="Scouting"/>). The real ask always lies
    /// inside it, so a bid at the top end is accepted; a release clause caps it.
    /// </summary>
    public static FeeRange LikelyFee(Player p, long ask, double knowledge)
    {
        double err = 0.06 + 0.24 * (1 - Math.Clamp(knowledge, 0, 1));
        double mid = ask * (1 + Scouting.Bias(p.Id, 977) * err * 0.6);
        long low = Math.Min(ask, (long)Math.Floor(mid * (1 - err) / 50_000) * 50_000);
        long high = Math.Max(ask, (long)Math.Ceiling(mid * (1 + err) / 50_000) * 50_000);
        if (p.Contract is { Buyout: > 0 } c) { high = Math.Min(high, c.Buyout); low = Math.Min(low, high); }
        return new(Math.Max(0, low), high);
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
