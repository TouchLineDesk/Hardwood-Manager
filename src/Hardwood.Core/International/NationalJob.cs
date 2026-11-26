using Hardwood.Core.Season;

namespace Hardwood.Core.International;

/// <summary>A federation's offer of its head coach's job (I6a, run 214, #108). Open until <see cref="Expires"/>.</summary>
public sealed record NationalOffer(string Code, string Name, int Rank, DateOnly Made, DateOnly Expires);

/// <summary>One finished spell in charge of a national team.</summary>
public sealed record NationalSpell(string Code, string Name, DateOnly From, DateOnly To, bool Sacked = false);

/// <summary>
/// International plan I6a (run 214, #108): the human coach's national-team job, alongside a club (or college) job. Saved inside
/// <see cref="InternationalHistory"/> as an optional field, so older saves load with no job and no offers.
/// </summary>
public sealed class NationalCoaching
{
    /// <summary>The nation you coach (its FIBA code), or null.</summary>
    public string? Code { get; set; }
    public string Name { get; set; } = "";
    public DateOnly? Since { get; set; }
    /// <summary>Your twelve, by player id. Empty means the federation's best twelve. A pick who isn't available on a game day
    /// (injured, too young, gone from the world) is replaced by the federation's next best for that event.</summary>
    public List<int> Picks { get; set; } = new();
    public List<NationalOffer> Offers { get; set; } = new();
    /// <summary>The last October the federations were asked (one round of offers a year).</summary>
    public int LastOfferYear { get; set; }
    public List<NationalSpell> Past { get; set; } = new();
    /// <summary>I6b (run 215): your nation's window games played on their day (live or by your assistant), until the window is
    /// played; then they are in its record. Kept for the window being played only.</summary>
    public List<CoachedGame> Games { get; set; } = new();
    /// <summary>I6b: how your national results have moved your reputation, all told (shown on the International page).</summary>
    public double ReputationEarned { get; set; }
    /// <summary>I6b: the federation's verdicts, newest last ("2027 World Cup: out in the quarter-finals, seeded 6th · satisfied").</summary>
    public List<string> Verdicts { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore] public bool IsEmpty => Code == null && Offers.Count == 0 && LastOfferYear == 0 && Past.Count == 0;

    /// <summary>The picks to apply when the teams are built: your nation's twelve, or none.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyDictionary<string, IReadOnlyCollection<int>>? Overrides =>
        Code != null && Picks.Count > 0 ? new Dictionary<string, IReadOnlyCollection<int>> { [Code] = Picks } : null;
}

/// <summary>
/// I6a (run 214, #108): who calls, and when. Once a year, from Oct 15 (after the college summer's jump and before the November
/// window), a coach without a national team hears from up to two federations. Your reputation decides how high up the ranking the
/// calls come from: smaller nations first. The draw has its own stream (the seed and the year), so no shared stream moves.
/// </summary>
public static class NationalJob
{
    public const int OfferMonth = 10, OfferDay = 15, OfferDays = 21, MaxOffers = 2;
    /// <summary>How far below the best nation that would call the offers may come from.</summary>
    public const int Band = 12;

    /// <summary>The best world rank whose federation would call a coach of this reputation: 30 (a new coach) → 36th,
    /// 50 → 26th, 70 → 16th, 90 → 6th, 100 → 1st.</summary>
    public static int BestRankFor(double reputation) => Math.Clamp((int)Math.Round(1 + (100 - Math.Clamp(reputation, 0, 100)) * 0.5), 1, 999);

    /// <summary>The day the year's offers come.</summary>
    public static DateOnly OfferDate(int year) => new(year, OfferMonth, OfferDay);

    /// <summary>The year's offers for a coach of <paramref name="reputation"/>: up to <see cref="MaxOffers"/> ranked nations from
    /// <see cref="BestRankFor"/> down to <see cref="Band"/> places below it (or the lowest-ranked nations when the world is small).
    /// Deterministic for (seed, year).</summary>
    public static List<NationalOffer> OffersFor(IReadOnlyList<NationalTeam> ranked, double reputation, ulong seed, DateOnly today, string? exclude = null)
    {
        var teams = ranked.Where(t => t.Rank > 0 && t.Nation.Code != exclude).OrderBy(t => t.Rank).ToList();
        if (teams.Count == 0) return new();
        int best = Math.Min(BestRankFor(reputation), teams[^1].Rank);
        var band = teams.Where(t => t.Rank >= best && t.Rank <= best + Band).ToList();
        if (band.Count == 0) band = teams.TakeLast(Band).ToList();
        var rng = new Rng(seed ^ 0x4E41_5449_4F4E_4A42UL ^ ((ulong)(uint)today.Year * 0x9E3779B97F4A7C15UL));
        for (int i = band.Count - 1; i > 0; i--) { int j = rng.Next(0, i + 1); (band[i], band[j]) = (band[j], band[i]); }
        return band.Take(MaxOffers).OrderBy(t => t.Rank)
            .Select(t => new NationalOffer(t.Nation.Code, t.Name, t.Rank, today, today.AddDays(OfferDays))).ToList();
    }
}
