using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.International;

/// <summary>An injury picked up at a tournament (I5, run 213), kept in the tournament's record by id and name so the club carries it.</summary>
public sealed record CupInjury(int PlayerId, string Name, string Code, string Injury, InjurySeverity Severity, DateOnly Occurred, DateOnly Return);

/// <summary>What a tournament means to a player's mood for the first weeks of the season after it (I5): never saved, re-derived
/// from the saved history (<see cref="Callups.MoodFor"/>).</summary>
public sealed record InternationalMood(string Reason, double Effect, DateOnly From, DateOnly Until);

/// <summary>
/// International plan I5 (run 213, #110): what international duty does to players. Injuries picked up in full-engine games carry to the
/// club (rolled once, from the tournament's own results, at the same risk as a club game); being picked, medals and being left out
/// move morale for the first weeks of the next season. All of it is read from the saved tournament records, so a reload derives the
/// same lines and no shared stream draws anything.
/// </summary>
public static class Callups
{
    /// <summary>Morale for being picked or a medal, by tournament weight: (gold, silver, bronze, picked).</summary>
    public static (double Gold, double Silver, double Bronze, double Picked) Lift(WorldCupRecord rec) => rec.Kind switch
    {
        "" or "Olympics" => (8, 6, 5, 3),
        "Continental" => (6, 5, 4, 2),
        U23Cup.Kind => (5, 4, 3, 2),
        _ => (0, 0, 0, 0), // the quick Olympic qualifiers: the Olympics line covers the winners
    };

    /// <summary>Left out of a twelve he was in last time, fit and still in the pool.</summary>
    public const double LeftOutHit = -4;

    /// <summary>The lines last until this day (about seven weeks into the season after a summer tournament).</summary>
    public static DateOnly MoodUntil(int year) => new(year, 12, 1);

    /// <summary>The senior tournaments (the World Cup, the Olympics, the continental championships) and the U23 Nations Cup of <paramref name="year"/>.</summary>
    public static IEnumerable<WorldCupRecord> Summer(InternationalHistory h, int year) =>
        h.WorldCups.Concat(h.Olympics).Concat(h.Continentals).Concat(h.U23Cups).Where(r => r.Year == year && r.Final != null);

    /// <summary>The strongest line for <paramref name="p"/> from the latest summer's tournaments, or null.</summary>
    public static InternationalMood? MoodFor(InternationalHistory h, Player p)
    {
        InternationalMood? best = null;
        foreach (var year in h.WorldCups.Concat(h.Olympics).Concat(h.Continentals).Concat(h.U23Cups).Select(r => r.Year).Distinct().OrderByDescending(y => y).Take(1))
            foreach (var rec in Summer(h, year))
            {
                var from = ParseEnd(rec);
                var team = rec.Teams.FirstOrDefault(t => t.Squad.Contains(p.Id));
                InternationalMood? m = null;
                if (team != null)
                {
                    var lift = Lift(rec);
                    string place = GameSession.Placing(rec, team.Code);
                    double e = place switch { "gold" => lift.Gold, "silver" => lift.Silver, "bronze" => lift.Bronze, _ => lift.Picked };
                    if (e == 0) continue;
                    m = new InternationalMood(place is "gold" or "silver" or "bronze"
                        ? $"Back from {rec.Short} with {place} for {team.Name}"
                        : $"Proud to have played for {team.Name} at {rec.Short}", e, from, MoodUntil(year));
                }
                else if (rec.LeftOut.TryGetValue(p.Id, out var code))
                {
                    m = new InternationalMood($"Left out of {rec.NameOf(code)}'s twelve for {rec.Short}", LeftOutHit, from, MoodUntil(year));
                }
                if (m == null) continue;
                // A lift beats a snub (he played for someone); the bigger lift wins.
                if (best == null || (m.Effect > 0 && (best.Effect < 0 || m.Effect > best.Effect))) best = m;
            }
        return best;
    }

    /// <summary>The tournament's last day (its record keeps "MMM d" days and the year).</summary>
    public static DateOnly ParseEnd(WorldCupRecord rec)
    {
        var f = rec.Final!;
        return DateOnly.ParseExact($"{f.Day} {rec.Year}", "MMM d yyyy", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>One player's minutes in one full-engine game, gathered while a tournament is played.</summary>
    public readonly record struct Appearance(Player Player, string Code, DateOnly Day, double Minutes);

    /// <summary>
    /// The tournament's injuries, rolled once after its games from a stream seeded by its own results (never the tournament's or
    /// a shared stream, so the games and every other draw are unchanged). The risk per game is the club game's
    /// (<see cref="InjuryEngine"/>: minutes, proneness, age, durability), without the staff's effect; a player hurt once is out of
    /// the rest of the rolls. Nothing is written to the player here: <see cref="GameSession"/> carries them at the summer's jump.
    /// </summary>
    public static List<CupInjury> RollInjuries(WorldCupRecord rec, IReadOnlyList<Appearance> apps, EngineConfig? cfg = null)
    {
        cfg ??= EngineConfig.Default;
        var rng = new Rng(SeedOf(rec));
        var hurt = new List<CupInjury>();
        var done = new HashSet<int>();
        foreach (var a in apps)
        {
            if (a.Minutes <= 0 || done.Contains(a.Player.Id)) continue;
            var p = a.Player;
            int age = p.Age(a.Day);
            double proneness = 0.6 + p.Hidden.InjuryProneness / 20.0 * 0.8;
            double ageFactor = age <= 25 ? 0.9 : age <= 30 ? 1.0 : 1.0 + (age - 30) * 0.08;
            double durability = 1.25 - p.Attributes[Attr.Durability] / 20.0 * 0.5;
            double prob = cfg.InjuryBaseRate * (a.Minutes / 30.0) * proneness * ageFactor * durability;
            if (!rng.Chance(prob)) continue;
            var kinds = InjuryCatalogue.Kinds;
            var weights = kinds.Select(k => k.Severity >= InjurySeverity.Severe ? k.Weight * proneness * (age >= 30 ? 1.3 : 1.0) : k.Weight).ToArray();
            var kind = kinds[rng.WeightedIndex(weights)];
            int days = Math.Max(1, rng.Next(kind.MinDays, kind.MaxDays + 1));
            hurt.Add(new CupInjury(p.Id, p.Name, a.Code, kind.Name, kind.Severity, a.Day, a.Day.AddDays(days)));
            done.Add(p.Id);
        }
        return hurt;
    }

    /// <summary>A seed from the tournament's name, year and every score (FNV-1a), so each career's tournament rolls its own.</summary>
    public static ulong SeedOf(WorldCupRecord rec)
    {
        ulong h = 14695981039346656037UL;
        void Mix(int v) { unchecked { h ^= (uint)v; h *= 1099511628211UL; } }
        foreach (char c in rec.Title) Mix(c);
        Mix(rec.Year);
        foreach (var g in rec.Games) { Mix(g.HomePts); Mix(g.AwayPts); }
        return h ^ 0x1A7E_5C0B_0213_0005UL;
    }

    /// <summary>
    /// The players in each nation's previous senior twelve (the latest earlier World Cup, Olympics or continental championship it
    /// played) who weren't picked this time though they were in the pool and fit on the first day. Code by player id.
    /// </summary>
    public static Dictionary<int, string> LeftOut(InternationalHistory h, WorldCupRecord rec, IReadOnlyList<NationalTeam> pools, DateOnly firstDay)
    {
        var result = new Dictionary<int, string>();
        if (rec.Kind is U23Cup.Kind or "Olympic qualifying") return result;
        var earlier = h.WorldCups.Concat(h.Olympics).Concat(h.Continentals).Where(r => r.Year < rec.Year).OrderByDescending(r => r.Year).ToList();
        foreach (var team in rec.Teams)
        {
            var prev = earlier.Select(r => r.Teams.FirstOrDefault(t => t.Code == team.Code)).FirstOrDefault(t => t != null);
            if (prev == null || pools.FirstOrDefault(n => n.Nation.Code == team.Code) is not { } pool) continue;
            var fit = pool.Pool.Where(x => x.Player.IsAvailable(firstDay)).Select(x => x.Player.Id).ToHashSet();
            foreach (var id in prev.Squad)
                if (!team.Squad.Contains(id) && fit.Contains(id)) result[id] = team.Code;
        }
        return result;
    }
}

/// <summary>
/// The U23 Nations Cup (I5, run 213): Danny asked for "a u23 competition", and FIBA has no men's U23 world event, so this is a
/// clearly named game event (international plan). 16 nations' under-23 twelves (born in or after the year minus 23, at least 17),
/// in the even summers without an Olympics (2030, 2034, …): four groups of four, quarter-finals, semi-finals, bronze and the final,
/// every game in the full engine on FIBA national rules. The field is the 16 strongest under-23 twelves; it doesn't move the ranking.
/// Own stream; nothing shared draws.
/// </summary>
public static class U23Cup
{
    public const string Kind = "U23";
    public const int Teams = 16, MaxAge = 23;

    public static bool IsYear(int y) => y >= 2030 && (y - 2030) % 4 == 0;

    public static FibaEvent? EventIn(int year) => IsYear(year) ? FibaCalendar.InYear(year).FirstOrDefault(e => e.Kind == FibaEventKind.U23) : null;

    /// <summary>An under-23 player in <paramref name="year"/> (by birth year, as youth events count).</summary>
    public static bool Eligible(Player p, int year) => year - EligibleShift - p.BirthDate.Year <= MaxAge;

    /// <summary>A test seam (0 in the game): a new 2026 world has few under-23s for 2030 (its 2030 teenagers aren't generated
    /// yet), so the tests count ages this many years earlier. Not saved.</summary>
    public static int EligibleShift { get; set; }

    public static WorldCupRecord? Play(GameSession s, int year, GameSimulator? sim = null, List<NationalTeam>? pools = null)
    {
        if (EventIn(year) is not { } ev) return null;
        var entries = WorldCup.Entries(s, ev.Start).Where(e => Eligible(e.P, year)).ToList();
        var built = NationalTeams.Build(entries, ev.Start);
        pools?.AddRange(built);
        var field = built.Where(t => t.Rank > 0).OrderByDescending(t => t.Strength).ThenBy(t => t.Name, StringComparer.Ordinal).Take(Teams).ToList();
        if (field.Count < 8) return null;
        // Ranked among themselves for the snake and the bracket.
        for (int i = 0; i < field.Count; i++) field[i].Rank = i + 1;
        var rng = new Rng(s.Seed ^ 0xF1BA_0023_0000_0213UL ^ ((ulong)(uint)year * 0x9E3779B97F4A7C15UL));
        return Tournament.Play(ev.Name, Kind, field, ev, new TournamentFormat(4, 8), rng, sim);
    }
}
