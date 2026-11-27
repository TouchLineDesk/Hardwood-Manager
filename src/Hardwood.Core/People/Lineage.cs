using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.People;

/// <summary>One son of a retired player (W3, run 231): who, whose, the summer he was found, and what was said about him.</summary>
public sealed class Son
{
    public int SonId { get; set; }
    public int FatherId { get; set; }
    /// <summary>The summer (July year) he first appeared in a class.</summary>
    public int Year { get; set; }
    public string FatherName { get; set; } = "";
    /// <summary>The father's career, as it stood when the son appeared ("11 seasons, 2 titles with Boston, 18.4 ppg").</summary>
    public string FatherCareer { get; set; } = "";
    /// <summary>The stated likeness ("a power forward like his father, and 3 cm taller").</summary>
    public string Likeness { get; set; } = "";
    /// <summary>Where he came in: "high school" (the college recruiting pool) or "draft" (a pro graduate/prospect class).</summary>
    public string Pool { get; set; } = "";
    /// <summary>The first team he joined that the inbox has told you about (a college program or a club); null until then.</summary>
    public int? AnnouncedTeamId { get; set; }
}

/// <summary>Every son found in this save (saved as JSON in meta <c>lineage</c>; older saves have none).</summary>
public sealed class LineageBook
{
    public List<Son> Sons { get; set; } = new();
    /// <summary>The summers already searched (each summer's class is searched once).</summary>
    public List<int> Summers { get; set; } = new();

    public Son? SonOf(int playerId) => Sons.FirstOrDefault(s => s.SonId == playerId);
    public IEnumerable<Son> ChildrenOf(int fatherId) => Sons.Where(s => s.FatherId == fatherId);
}

/// <summary>
/// W3, second generation (`claude/world-stories-plan.md`): years into a save, sons of retired players appear in the new
/// classes (high-school recruits in a college world, the summer's graduates or draft prospects otherwise), carrying the
/// father's surname and appearance, with a line about the father's career and a stated likeness.
/// Rules (the plan's): only generated players are fathers (no invented family for real people); a father's career must
/// have begun in this save (so sons arrive about 18-25 years in) and run at least <see cref="MinFatherGames"/> games;
/// he is 24-40 years older than his son; one son per father; at most <see cref="MaxPerSummer"/> a summer.
/// No random draw: who has a son, and which recruit he is, come from a hash of the save's seed (the shared streams and
/// the characterization pins don't move). Only names and appearance change, never ability.
/// </summary>
public static class Lineage
{
    public const int MinFatherGames = 150, MinGap = 24, MaxGap = 40, MaxPerSummer = 3;

    /// <summary>The chance a given summer brings an eligible father's son: 1% for a journeyman up to 4% for a great.</summary>
    public static double YearlyChance(CareerTotals c) => 0.01 + 0.03 * Math.Min(1, c.HallScore / 40.0);

    /// <summary>Can this player be a father (generated, retired, a career that began in this save)?</summary>
    public static bool CanBeFather(League league, Player p, int firstSeason)
    {
        if (p.RookieSeason is not { } rookie || rookie < firstSeason) return false;
        if (!league.Careers.TryGetValue(p.Id, out var c) || c.Games < MinFatherGames) return false;
        if (RealWorld.RealNames.IsRealPlayer(p.Name) || RealWorld.RealCareers.Players.ContainsKey(p.Name)) return false;
        return true;
    }

    /// <summary>
    /// Search one new class for sons. <paramref name="cohort"/> is the class (players nobody has linked yet); the matching
    /// recruits are renamed and returned. <paramref name="teamName"/> names a team id for the father's career line.
    /// </summary>
    public static List<Son> Link(League league, LineageBook book, IReadOnlyList<Player> cohort, int summerYear, string pool,
                                 ulong seed, Func<int, string?> teamName, Generation.NameBook? living = null)
    {
        var found = new List<Son>();
        if (cohort.Count == 0) return found;
        int firstSeason = league.Records.FirstSeason > 0 ? league.Records.FirstSeason : league.History.Count > 0 ? league.History.Min(h => h.Season) : league.Season;
        var fathers = new HashSet<int>(book.Sons.Select(s => s.FatherId));
        var sons = new HashSet<int>(book.Sons.Select(s => s.SonId));
        var free = cohort.Where(p => !sons.Contains(p.Id)).ToList();
        var candidates = league.Retired
            .Where(f => !fathers.Contains(f.Id) && CanBeFather(league, f, firstSeason))
            .OrderByDescending(f => league.Careers[f.Id].HallScore).ThenBy(f => f.Id).ToList();
        foreach (var f in candidates)
        {
            if (found.Count >= MaxPerSummer) break;
            var c = league.Careers[f.Id];
            if (Unit(seed, $"son:{f.Id}:{summerYear}") >= YearlyChance(c)) continue;
            var fits = free.Where(s => s.Nationality == f.Nationality
                                    && s.BirthDate.Year - f.BirthDate.Year is >= MinGap and <= MaxGap).ToList();
            if (fits.Count == 0) continue;
            // A great's son is among the best of his class; any father's is at least in its better half.
            int keep = Math.Max(1, fits.Count / (c.HallScore >= 40 ? 4 : 2));
            var pickFrom = fits.OrderByDescending(s => s.PotentialAbility).ThenBy(s => s.Id).Take(keep).ToList();
            var samePos = pickFrom.Where(s => s.PrimaryPosition == f.PrimaryPosition).ToList();
            if (samePos.Count > 0) pickFrom = samePos;
            // The generator's name rules hold for the new name too (review): not a real player's, not a living player's, not "Brock Brock".
            var son = pickFrom.OrderBy(s => MediaCast.Hash($"{seed}:pick:{f.Id}:{s.Id}"))
                .FirstOrDefault(s => SurnameFor(s, f) is var last && !string.Equals(s.FirstName, last, StringComparison.OrdinalIgnoreCase)
                                     && !RealWorld.RealNames.IsRealPlayer($"{s.FirstName} {last}") && !RealWorld.RealCareers.Players.ContainsKey($"{s.FirstName} {last}")
                                     && living?.Taken(s.FirstName, last) != true);
            if (son == null) continue;
            free.Remove(son);
            living?.Add(son.FirstName, SurnameFor(son, f));
            Rename(son, f);
            var entry = new Son
            {
                SonId = son.Id, FatherId = f.Id, Year = summerYear, FatherName = f.Name, Pool = pool,
                FatherCareer = CareerLine(c, teamName), Likeness = LikenessLine(son, f),
            };
            book.Sons.Add(entry);
            found.Add(entry);
        }
        return found;
    }

    /// <summary>The son takes his father's surname (a "Jr." when he shares the first name too) and appearance.</summary>
    public static void Rename(Player son, Player father)
    {
        son.LastName = SurnameFor(son, father);
        if (son.Ethnicity != father.Ethnicity) { son.Ethnicity = father.Ethnicity; son.FaceFile = null; } // a new face from the right folder
    }

    /// <summary>The son's surname: his father's, with "Jr." when he shares the first name too ("III" after a "Jr.").</summary>
    public static string SurnameFor(Player son, Player father)
    {
        if (son.FirstName != father.FirstName) return father.LastName;
        return father.LastName.EndsWith(" Jr.") ? father.LastName[..^4] + " III" : father.LastName + " Jr.";
    }

    /// <summary>"11 seasons, 2 titles, 18.4 ppg, mostly with the Boston Shamrocks".</summary>
    public static string CareerLine(CareerTotals c, Func<int, string?> teamName)
    {
        var bits = new List<string> { $"{c.Seasons} season{(c.Seasons == 1 ? "" : "s")}" };
        if (c.Mvps > 0) bits.Add($"{c.Mvps} MVP{(c.Mvps == 1 ? "" : "s")}");
        if (c.Titles > 0) bits.Add($"{c.Titles} title{(c.Titles == 1 ? "" : "s")}");
        if (c.AllStars > 0) bits.Add($"{c.AllStars}× All-Star");
        bits.Add($"{c.Ppg:F1} ppg");
        var line = string.Join(", ", bits);
        if (c.GamesByTeam.Count > 0)
        {
            var (team, games) = c.GamesByTeam.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => (kv.Key, kv.Value)).First();
            if (teamName(team) is { Length: > 0 } name)
                line += (games * 2 >= c.Games ? ", mostly with the " : ", longest with the ") + name;
        }
        return line;
    }

    /// <summary>The stated likeness: the position and the height against his father's.</summary>
    public static string LikenessLine(Player son, Player father)
    {
        string pos = son.PrimaryPosition == father.PrimaryPosition ? $"a {PositionName(son.PrimaryPosition)} like his father" : $"a {PositionName(son.PrimaryPosition)}, where his father was a {PositionName(father.PrimaryPosition)}";
        int d = son.HeightCm - father.HeightCm;
        string height = Math.Abs(d) <= 2 ? "and his height" : d > 0 ? $"and already {d} cm taller" : $"and {-d} cm shorter so far";
        return $"{pos}, {height}";
    }

    public static string PositionName(Position p) => p switch
    {
        Position.PG => "point guard", Position.SG => "shooting guard", Position.SF => "small forward", Position.PF => "power forward", _ => "center",
    };

    /// <summary>The player page's line for a son ("Son of …").</summary>
    public static string SonLine(Son s) => $"Son of {s.FatherName} ({s.FatherCareer}): {s.Likeness}.";

    /// <summary>The player page's line for a father.</summary>
    public static string FatherLine(IEnumerable<(Son Son, string Name)> children) =>
        "Father of " + string.Join(", ", children.Select(c => $"{c.Name} (found {c.Son.Year})")) + ".";

    private static double Unit(ulong seed, string key) => (MediaCast.Hash($"{seed}:{key}") >> 11) / (double)(1UL << 53);
}
