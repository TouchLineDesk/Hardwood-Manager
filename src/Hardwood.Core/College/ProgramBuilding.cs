using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>
/// CS7, part 1 (run 170, K19): the coached program's boosters and facilities. Kept with the program's money
/// (<see cref="ClubFinance.Program"/>), so it saves with every club's books and needs no schema change.
/// </summary>
public sealed class ProgramBooks
{
    /// <summary>Set once the program's facilities and boosters have been seeded from its prestige.</summary>
    public bool Ready { get; set; }
    /// <summary>0-100: how happy the boosters are. Winning, March runs and rivalry wins lift it; it drifts back to 50.</summary>
    public double Boosters { get; set; } = 50;
    /// <summary>The booster fund: money given to the program and not yet spent.</summary>
    public long Fund { get; set; }
    /// <summary>The levels the practice gym and the arena had when the program was seeded: effects count from here, so a
    /// program you take over plays and recruits exactly as it did until something is built.</summary>
    public int GymBase { get; set; } = 3;
    public int ArenaBase { get; set; } = 3;
    /// <summary>An approved project, opening next summer (null: none).</summary>
    public FacilityKind? Pending { get; set; }
    /// <summary>The boosters' last summer gift, and what moved their mood (for the Board page and the inbox).</summary>
    public long LastGift { get; set; }
    public List<string> LastLines { get; set; } = new();
    /// <summary>Every dollar the boosters have given since the program was seeded.</summary>
    public long TotalRaised { get; set; }
}

/// <summary>CS7 part 1: the rules (no RNG, constants here for Danny's veto check).</summary>
public static class ProgramBuilding
{
    /// <summary>The facilities a college program builds: the practice gym (player growth) and the arena (recruiting).</summary>
    public static readonly FacilityKind[] Kinds = { FacilityKind.Practice, FacilityKind.Arena };

    /// <summary>Recruiting appeal for each level built past the seeded arena and practice gym (the coached program only;
    /// about the gap between two neighbouring programs per arena level).</summary>
    public const double ArenaAppeal = 4, GymAppeal = 2;
    /// <summary>The practice gym's training edge per level built (the pros' practice facility uses the same 0.4 a level).</summary>
    public const double GymEdge = 1.0;

    /// <summary>A program's seeded level from its prestige: 1 below 25, 2 below 50, 3 below 75, 4 above. Level 5 is only ever
    /// built, so even a blue blood has something to raise money for.</summary>
    public static int SeedLevel(double prestige) => Math.Clamp(1 + (int)Math.Floor(prestige / 25), 1, 4);

    /// <summary>The boosters' summer gift: $0.4M plus up to $2.6M by prestige, scaled by their mood (half at 0, 1.5× at 100).</summary>
    public static long Gift(double prestige, double boosters) =>
        RoundMoney((0.4 + Math.Clamp(prestige, 0, 100) / 100 * 2.6) * (0.5 + Math.Clamp(boosters, 0, 100) / 100) * 1_000_000);

    /// <summary>The price of the next level: $0.8M × the current level for the gym, $1.2M × for the arena.</summary>
    public static long Cost(FacilityKind k, int level) => RoundMoney((k == FacilityKind.Arena ? 1_200_000 : 800_000) * (double)level);

    public static string Name(FacilityKind k) => k == FacilityKind.Arena ? "Arena" : "Practice gym";

    /// <summary>Seed a program's books the first time they're used: facilities from prestige, boosters from prestige
    /// (40 at a 50 program), and two summers' gifts in the fund (enough for one gym level on day one).</summary>
    public static ProgramBooks Ensure(Team t, double prestige)
    {
        var b = t.Money.Program;
        if (b.Ready) return b;
        int level = SeedLevel(prestige);
        var f = t.Money.Facilities;
        f.Practice.Level = level; f.Arena.Level = level;
        f.Practice.Edge = 0; f.Arena.Edge = 0;
        b.GymBase = level; b.ArenaBase = level;
        b.Boosters = Math.Clamp(40 + (prestige - 50) * 0.5, 15, 85);
        b.Fund = 2 * Gift(prestige, b.Boosters);
        b.Ready = true;
        return b;
    }

    /// <summary>Levels built past the seeded ones.</summary>
    public static int Built(Team t, FacilityKind k) =>
        t.Money.Facilities.Get(k).Level - (k == FacilityKind.Arena ? t.Money.Program.ArenaBase : t.Money.Program.GymBase);

    /// <summary>The recruiting appeal the program's building adds (zero until something is built).</summary>
    public static double Appeal(Team t) =>
        !t.Money.Program.Ready ? 0 : ArenaAppeal * Built(t, FacilityKind.Arena) + GymAppeal * Built(t, FacilityKind.Practice);

    /// <summary>Set the practice gym's training edge from what's been built (read by <c>StaffEffects.TrainingQuality</c>).</summary>
    public static void ApplyEdges(Team t)
    {
        if (!t.Money.Program.Ready) return;
        t.Money.Facilities.Practice.Edge = GymEdge * Built(t, FacilityKind.Practice);
        t.Money.Facilities.Arena.Edge = 0;
    }

    /// <summary>Why the program can't build this now (null: it can).</summary>
    public static string? Blocked(Team t, FacilityKind k)
    {
        var b = t.Money.Program; var f = t.Money.Facilities.Get(k);
        if (b.Pending is { } p) return $"The {Name(p).ToLowerInvariant()} project is already under way.";
        if (f.Level >= 5) return $"The {Name(k).ToLowerInvariant()} is already the best in the country.";
        long cost = Cost(k, f.Level);
        if (b.Fund < cost) return $"The booster fund has {Money(b.Fund)}; the next {Name(k).ToLowerInvariant()} level costs {Money(cost)}.";
        return null;
    }

    /// <summary>Pay for the next level from the booster fund. It opens at once when no game of the season is being played
    /// (between the last game and the next season's first), otherwise next summer.</summary>
    public static string Build(Team t, FacilityKind k, bool openNow)
    {
        if (Blocked(t, k) is { } why) return why;
        var b = t.Money.Program; var f = t.Money.Facilities.Get(k);
        long cost = Cost(k, f.Level);
        b.Fund -= cost;
        if (openNow)
        {
            f.Level++; ApplyEdges(t);
            return $"{Name(k)} level {f.Level} is ready for the season ({Money(cost)} from the booster fund).";
        }
        b.Pending = k;
        return $"{Name(k)} level {f.Level + 1} is being built ({Money(cost)} from the booster fund); it opens next summer.";
    }

    /// <summary>
    /// The summer books (before the season resets): a project opens, the boosters' mood moves with the season (record,
    /// March, rivalry games) and drifts 10% back toward 50, and they give their gift.
    /// </summary>
    public static void Summer(Team t, CollegeWorld c)
    {
        double prestige = c.Prestige.GetValueOrDefault(t.Id, 50);
        var b = Ensure(t, prestige);
        var lines = new List<string>();
        if (b.Pending is { } k)
        {
            var f = t.Money.Facilities.Get(k);
            f.Level = Math.Min(5, f.Level + 1); b.Pending = null;
            lines.Add($"The new {Name(k).ToLowerInvariant()} opened: level {f.Level}.");
        }
        ApplyEdges(t);
        var (w, l) = c.Record(t.Id);
        var s = c.Season;
        int marchWins = s.Games.Count(g => g.Played && g.Event == CollegeWorld.Madness && g.Round >= 1 && g.WinnerId == t.Id);
        bool inField = s.Seeds.ContainsKey(t.Id);
        int rivalWins = 0, rivalLosses = 0;
        foreach (var g in s.Games)
        {
            if (!g.Played || (g.HomeId != t.Id && g.AwayId != t.Id)) continue;
            if (c.RivalryOf(g.HomeId, g.AwayId) == null) continue;
            if (g.WinnerId == t.Id) rivalWins++; else rivalLosses++;
        }
        double move = (w + l > 0 ? (w / (double)(w + l) - 0.5) * 20 : 0) + (inField ? 3 : 0) + marchWins * 2
                      + (s.ChampionId == t.Id ? 5 : 0) + 2 * (rivalWins - rivalLosses);
        double before = b.Boosters;
        b.Boosters = Math.Clamp(b.Boosters + move + (50 - b.Boosters) * 0.10, 0, 100);
        string why = $"{w}-{l}" + (inField ? marchWins > 0 ? $", {marchWins} March win{(marchWins == 1 ? "" : "s")}" : ", in the tournament" : ", no tournament")
                     + (rivalWins + rivalLosses > 0 ? $", {rivalWins}-{rivalLosses} against rivals" : "");
        lines.Add($"Booster mood {before:0} → {b.Boosters:0} ({why}).");
        b.LastGift = Gift(prestige, b.Boosters);
        b.Fund += b.LastGift; b.TotalRaised += b.LastGift;
        lines.Add($"The boosters gave {Money(b.LastGift)}; the fund holds {Money(b.Fund)}.");
        b.LastLines = lines;
    }

    /// <summary>The boosters' mood in a word.</summary>
    public static string Mood(double boosters) => boosters switch
    {
        >= 75 => "delighted", >= 60 => "happy", >= 45 => "content", >= 30 => "restless", _ => "angry",
    };

    public static string Money(long v) => $"${v / 1e6:0.0}M";
    private static long RoundMoney(double v) => (long)Math.Round(v / 100_000) * 100_000;
}
