namespace Hardwood.Core.College;

/// <summary>One line of the March Madness card on a pro career's Home (C4 part 2): a tag (UPSET, 1 SEED, FINAL…), the text and a detail.</summary>
public sealed record MadnessWatchRow(string Tag, string Text, string Detail, int? TeamId = null);

/// <summary>
/// March Madness as a pro coach sees it on Home (C4 part 2, college-sim-design #164, the last piece): where the tournament
/// is, the latest round's upsets (or the four 1-seeds, the Final Four, the champions), and the seniors still playing who turn
/// pro next summer. Read-only: built from the season as it stands, no random numbers.
/// </summary>
public sealed record MadnessWatch(string Title, string Stage, string Summary, IReadOnlyList<MadnessWatchRow> Results, string ResultsTitle,
    IReadOnlyList<MadnessWatchRow> Prospects, string ProspectsTitle);

public sealed partial class CollegeWorld
{
    /// <summary>Days after the final that the card stays on Home.</summary>
    public const int MadnessWatchDaysAfter = 10;

    public static readonly string[] MadnessRoundNames = { "First Four", "round of 64", "round of 32", "Sweet 16", "Elite Eight", "Final Four", "championship game" };

    /// <summary>The March Madness card for Home, from Selection Sunday until <see cref="MadnessWatchDaysAfter"/> days after the final; null otherwise.</summary>
    public MadnessWatch? Watch(DateOnly today)
    {
        var s = Season;
        if (!s.Selected || s.Bracket.Count < 64) return null;
        var games = s.Games.Where(g => g.Event == Madness).ToList();
        if (s.ChampionId != null)
        {
            var lastDay = games.Where(g => g.Played).Select(g => g.Date).DefaultIfEmpty(today).Max();
            if (today > lastDay.AddDays(MadnessWatchDaysAfter)) return null;
        }
        int y = s.Year + 1;
        int Seed(int id) => s.Seeds.GetValueOrDefault(id);
        string Name(int id) => Program(id)?.FullName ?? "?";
        string School(int id) => Program(id)?.City ?? "?";
        string WithSeed(int id) => $"{Seed(id)} {School(id)}";
        string Region(int id) => RegionOf(id) is var r and >= 0 ? MadnessBracket.Regions[r] : "";

        // The field: the 64 slots plus the First Four's teams; out = every loser so far.
        var field = s.Bracket.Where(id => id > 0).Concat(games.Where(g => g.Round == 0).SelectMany(g => new[] { g.HomeId, g.AwayId })).Distinct().ToList();
        var losers = games.Where(g => g.Played && g.WinnerId != null).Select(g => g.HomeId == g.WinnerId ? g.AwayId : g.HomeId).ToHashSet();
        var alive = field.Where(id => !losers.Contains(id)).ToList();

        // The last round finished (-1 = none yet) and the next to play.
        int expected(int r) => r == 0 ? games.Count(g => g.Round == 0) : 64 >> r;
        int done = -1;
        for (int r = 0; r <= 6; r++)
        {
            var round = games.Where(g => g.Round == r).ToList();
            if (r == 0 && round.Count == 0) { done = 0; continue; } // no First Four this year
            if (round.Count == expected(r) && round.All(g => g.Played)) done = r; else break;
        }
        var dates = s.RoundDates.GetValueOrDefault(Madness) ?? new List<DateOnly>();
        int next = done + 1;
        string stage = s.ChampionId is { } ch ? $"Champions: {Name(ch)}"
            : next <= 6 ? $"Next: the {MadnessRoundNames[next]}" + (next < dates.Count ? $", {dates[next]:MMM d}" + (next >= 5 ? $" in {MadnessBracket.FinalFourHost(y).Split(',')[0]}" : "") : "")
            : "";
        if (!games.Any(g => g.Played)) stage = $"The field of {field.Count} is set · " + stage[0].ToString().ToLower() + stage[1..];

        // The summary chips' line: teams left, the 1-seeds still alive, a Cinderella.
        int ones = alive.Count(id => Seed(id) == 1);
        var cinderella = alive.Where(id => done >= 2 && IsCinderella(Seed(id), done + 1)).OrderByDescending(Seed).FirstOrDefault();
        string summary = s.ChampionId is { } c2
            ? $"{Name(c2)} won it as {Article(Seed(c2))}-seed" + (s.RunnerUpId is { } ru ? $", beating {WithSeed(ru)} in the final" : "") + "."
            : $"{alive.Count} teams left · {ones} of 4 No. 1 seeds still in" + (cinderella > 0 ? $" · Cinderella: {WithSeed(cinderella)}" : "");

        // Results: the champions' final, the Final Four, the latest round's upsets, or the 1-seeds before the games.
        var results = new List<MadnessWatchRow>();
        string resultsTitle;
        string Score(CollegeGame g)
        {
            int w = g.WinnerId!.Value, l = g.HomeId == w ? g.AwayId : g.HomeId;
            int wp = g.HomeId == w ? g.HomePts!.Value : g.AwayPts!.Value, lp = g.HomeId == w ? g.AwayPts!.Value : g.HomePts!.Value;
            return $"{WithSeed(w)} {wp}, {WithSeed(l)} {lp}" + (g.Overtimes > 0 ? " (OT)" : "");
        }
        if (s.ChampionId is { } champ)
        {
            resultsTitle = "The champions' road";
            foreach (var g in games.Where(g => g.Round >= 1 && g.WinnerId == champ).OrderBy(g => g.Round))
                results.Add(new MadnessWatchRow(g.Round == 6 ? "FINAL" : IsUpset(g) ? "UPSET" : MadnessRoundNames[g.Round].ToUpperInvariant(), Score(g),
                    (g.WinnerId == g.HomeId ? g.HomeStar : g.AwayStar)?.Line ?? "", champ));
        }
        else if (done >= 4)
        {
            resultsTitle = done == 5 ? "The championship game" : $"The Final Four in {MadnessBracket.FinalFourHost(y)}";
            var four = done == 5 ? games.Where(g => g.Round == 5).OrderBy(g => g.Slot).Select(g => g.WinnerId!.Value).ToList()
                : games.Where(g => g.Round == 4).OrderBy(g => g.Slot).Select(g => g.WinnerId!.Value).ToList();
            foreach (var id in four)
                results.Add(new MadnessWatchRow(IsCinderella(Seed(id), 5) ? "CINDERELLA" : $"{Seed(id)} SEED", Name(id), $"{Region(id)} region · {Record(id).wins}-{Record(id).losses}", id));
            if (done == 5)
                foreach (var g in games.Where(g => g.Round == 5).OrderBy(g => g.Slot)) results.Add(new MadnessWatchRow("SEMI-FINAL", Score(g), "", g.WinnerId));
        }
        else if (done >= 1)
        {
            var round = games.Where(g => g.Round == done).ToList();
            var upsets = round.Where(IsUpset).OrderByDescending(g => Seed(g.WinnerId!.Value)).ToList();
            resultsTitle = upsets.Count == 0 ? $"The {MadnessRoundNames[done]}: the favourites held" : $"Upsets in the {MadnessRoundNames[done]}";
            foreach (var g in upsets.Take(4)) results.Add(new MadnessWatchRow("UPSET", Score(g), (g.WinnerId == g.HomeId ? g.HomeStar : g.AwayStar)?.Line ?? "", g.WinnerId));
            if (upsets.Count == 0)
                foreach (var g in round.OrderBy(g => Seed(g.WinnerId!.Value)).ThenBy(g => g.Slot).Take(4))
                    results.Add(new MadnessWatchRow(MadnessRoundNames[done].ToUpperInvariant(), Score(g), "", g.WinnerId));
        }
        else
        {
            resultsTitle = "The No. 1 seeds";
            foreach (var id in field.Where(id => Seed(id) == 1).OrderBy(RegionOf))
                results.Add(new MadnessWatchRow("1 SEED", Name(id), $"{Region(id)} region · {Record(id).wins}-{Record(id).losses}", id));
        }

        // Prospects: seniors who turn pro next summer, on teams still in (all of the field before the games; the finalists after).
        var nextSummer = new DateOnly(y, 7, 1);
        var pool = s.ChampionId != null ? new[] { s.ChampionId.Value, s.RunnerUpId ?? 0 }.Where(i => i > 0).ToList() : alive;
        var prospects = pool.Select(id => Program(id)).Where(t => t != null)
            .SelectMany(t => t!.Roster.Where(p => p.Age(nextSummer) >= GraduateAge).Select(p => (p, t: t!)))
            .OrderByDescending(x => x.p.PotentialAbility * 0.6 + x.p.CurrentAbility * 0.4).ThenBy(x => x.p.Id).Take(3)
            .Select(x =>
            {
                s.Stats.TryGetValue(x.p.Id, out var st);
                string line = st is { G: > 0 } ? $"{st.Ppg:F1} pts · {st.Rpg:F1} reb · {st.Apg:F1} ast" : "";
                return new MadnessWatchRow(x.p.PrimaryPosition.ToString(), x.p.Name, $"{WithSeed(x.t.Id)}" + (line.Length > 0 ? $" · {line}" : ""), x.t.Id);
            }).ToList();
        string prospectsTitle = s.ChampionId != null ? "Turning pro from the finalists" : "Turning pro next summer, still playing";

        return new MadnessWatch($"March Madness {y}", stage, summary, results, resultsTitle, prospects, prospectsTitle);
    }
}
