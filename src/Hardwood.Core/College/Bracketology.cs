namespace Hardwood.Core.College;

/// <summary>
/// The March Madness field as the committee would pick it today (C4 part 1, #191): automatic bids for the conference
/// tournament champions (or, before the tournaments, each conference's leader), at-large bids by rating, seed lines and
/// the bubble. On Selection Sunday the real field is picked the same way, so the last projection is the bracket.
/// </summary>
public sealed class BracketProjection
{
    /// <summary>The 68 teams, best first (seed order).</summary>
    public List<int> Field { get; init; } = new();
    public Dictionary<int, int> Seeds { get; init; } = new();
    /// <summary>Automatic bids: the team and its conference's short name.</summary>
    public Dictionary<int, string> AutoBids { get; init; } = new();
    /// <summary>The four weakest at-large teams in the field, best first.</summary>
    public List<int> LastFourIn { get; init; } = new();
    public List<int> FirstFourOut { get; init; } = new();
    public List<int> NextFourOut { get; init; } = new();
    /// <summary>True once the real field is picked (the projection is then the bracket).</summary>
    public bool Final { get; init; }
    /// <summary>Conferences whose tournament champion (the automatic bid) is decided.</summary>
    public int AutoBidsDecided { get; init; }

    public bool In(int id) => Seeds.ContainsKey(id);

    /// <summary>Where a program stands: "No. 3 seed", "Last four in (No. 11 seed)", "First four out", "Next four out" or "Out".</summary>
    public string StatusOf(int id)
    {
        if (Seeds.TryGetValue(id, out var seed))
        {
            if (LastFourIn.Contains(id)) return $"Last four in (No. {seed} seed)";
            return $"No. {seed} seed" + (AutoBids.TryGetValue(id, out var conf) ? $" ({(Final ? "" : "projected ")}{conf} champions)" : "");
        }
        if (FirstFourOut.Contains(id)) return "First four out";
        if (NextFourOut.Contains(id)) return "Next four out";
        return "Out";
    }
}

public sealed partial class CollegeWorld
{
    public const int FieldSize = 68;

    /// <summary>A conference's automatic bid today: its tournament champion once decided, otherwise its regular-season leader.</summary>
    public int AutoBidHolder(ConferenceInfo conf) =>
        Season.EventWinners.TryGetValue($"{conf.Short} tournament", out var w) ? w : ConferenceTable(conf.Key)[0].TeamId;

    /// <summary>
    /// The field from these automatic bids: the best-rated at-large teams fill it to 68, lines 1-15 hold four teams each
    /// and the bottom eight are the First Four's 16-seeds. <see cref="Select"/> picks the real field with this, so a
    /// projection made once the conference tournaments are over is exactly the bracket.
    /// </summary>
    private BracketProjection Project(IReadOnlyDictionary<int, string> auto, bool final, int decided)
    {
        var elo = Season.Elo;
        double Rating(int id) => elo.GetValueOrDefault(id, 1500);
        var autoIds = auto.Keys.ToHashSet();
        var others = Programs.Select(t => t.Id).Where(i => !autoIds.Contains(i)).OrderByDescending(Rating).ToList();
        int atLargeCount = FieldSize - autoIds.Count;
        var atLarge = others.Take(atLargeCount).ToList();
        var field = autoIds.Concat(atLarge).OrderByDescending(Rating).ToList();
        var seeds = new Dictionary<int, int>();
        for (int i = 0; i < field.Count; i++) seeds[field[i]] = i < 60 ? i / 4 + 1 : 16;
        return new BracketProjection
        {
            Field = field, Seeds = seeds, AutoBids = new Dictionary<int, string>(auto), Final = final, AutoBidsDecided = decided,
            LastFourIn = atLarge.Skip(Math.Max(0, atLarge.Count - 4)).ToList(),
            FirstFourOut = others.Skip(atLargeCount).Take(4).ToList(),
            NextFourOut = others.Skip(atLargeCount + 4).Take(4).ToList(),
        };
    }

    /// <summary>
    /// Bracketology: the field if it were picked today. After Selection Sunday it is the real field, with the bubble as
    /// it stood on the day (kept in the season).
    /// </summary>
    public BracketProjection Bracketology()
    {
        var s = Season;
        if (s.Selected)
        {
            // Seasons picked before v0.50.0 kept no order or bubble: seed lines, then today's rating.
            var field = s.SelectionOrder.Count == s.Seeds.Count ? s.SelectionOrder.ToList()
                      : s.Seeds.Keys.OrderBy(i => s.Seeds[i]).ThenByDescending(i => s.Elo.GetValueOrDefault(i, 1500)).ToList();
            return new BracketProjection
            {
                Field = field, Seeds = new(s.Seeds), Final = true, AutoBidsDecided = Active.Count(),
                AutoBids = Active.Where(c => s.ConferenceChampions.ContainsKey(c.Key)).GroupBy(c => s.ConferenceChampions[c.Key])
                                 .ToDictionary(g => g.Key, g => g.First().Short),
                LastFourIn = s.LastFourIn.ToList(), FirstFourOut = s.FirstFourOut.ToList(), NextFourOut = s.NextFourOut.ToList(),
            };
        }
        return ProjectNow();
    }

    /// <summary>The field if it were picked today, ignoring any real selection (tests compare it with Selection Sunday's).</summary>
    public BracketProjection ProjectNow()
    {
        var s = Season;
        var auto = new Dictionary<int, string>();
        int decided = 0;
        foreach (var conf in Active)
        {
            if (s.EventWinners.ContainsKey($"{conf.Short} tournament")) decided++;
            auto.TryAdd(AutoBidHolder(conf), conf.Short);
        }
        return Project(auto, final: false, decided);
    }

    /// <summary>The region (0-3, see <see cref="MadnessBracket.Regions"/>) a team plays in, or -1.</summary>
    public int RegionOf(int teamId)
    {
        var s = Season;
        int i = s.Bracket.IndexOf(teamId);
        if (i < 0)
            i = s.Games.FirstOrDefault(g => g.Event == Madness && g.Round == 0 && (g.HomeId == teamId || g.AwayId == teamId))?.Slot ?? -1;
        return i < 0 ? -1 : i / 16;
    }

    /// <summary>The Selection Sunday message for the coached program: its seed, region and first game, or why it was left out.</summary>
    private void SelectionNews(BracketProjection p)
    {
        if (ManagedProgramId is not { } me || Program(me) is not { } t) return;
        var s = Season;
        string Named(int id) => Program(id)?.FullName ?? "?";
        if (p.Seeds.TryGetValue(me, out var seed))
        {
            int region = RegionOf(me);
            string regionName = region >= 0 ? MadnessBracket.Regions[region] : "";
            var first = s.Games.Where(g => g.Event == Madness && (g.HomeId == me || g.AwayId == me)).OrderBy(g => g.Round).FirstOrDefault();
            string how = p.AutoBids.TryGetValue(me, out var conf) ? $"an automatic bid as {conf} tournament champions" : "an at-large bid";
            string opener;
            if (first != null)
            {
                int opp = first.HomeId == me ? first.AwayId : first.HomeId;
                int top = Enumerable.Range(region * 16, 16).Select(i => s.Bracket[i]).FirstOrDefault(i => i > 0 && s.Seeds.GetValueOrDefault(i) == 1);
                opener = $"First Four, {first.Date:ddd MMM d}: vs No. 16 {Named(opp)}. The winner plays No. 1 {Named(top)}.";
            }
            else
            {
                int slot = s.Bracket.IndexOf(me), pair = slot ^ 1;
                int opp = s.Bracket[pair];
                var date = s.RoundDates[Madness][1].AddDays(slot < 32 ? 0 : 1);
                opener = opp > 0 ? $"Round of 64, {date:ddd MMM d}: vs No. {s.Seeds[opp]} {Named(opp)}."
                                 : $"Round of 64, {date:ddd MMM d}: vs the winner of a First Four game (No. 16).";
            }
            string tag = seed <= 4 ? "a protected seed" : seed >= 12 ? "a long shot, if a dangerous one" : p.LastFourIn.Contains(me) ? "one of the last four in" : "";
            CoachNews.Add(($"Selection Sunday: we're a No. {seed} seed",
                $"{t.FullName} are the No. {seed} seed in the {regionName} region, with {how}{(tag.Length > 0 ? $": {tag}" : "")}.\n{opener}\nThe whole bracket is on the College page (March Madness)."));
        }
        else
        {
            string why = p.FirstFourOut.Contains(me) ? "We were among the first four left out: one or two more wins would have done it."
                       : p.NextFourOut.Contains(me) ? "We were among the next four out, close but not close enough."
                       : "We weren't near the bubble this year.";
            string last = p.LastFourIn.Count > 0 ? $" The last team in was {Named(p.LastFourIn[^1])}." : "";
            CoachNews.Add(("Selection Sunday: we're left out", $"{t.FullName} weren't picked for March Madness. {why}{last}"));
        }
    }
}
