using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.International;

/// <summary>FIBA's four zones. Oceania plays in the Asia Cup and the Asia-Pacific qualifiers, so it sits with Asia.</summary>
public enum FibaZone { Africa, Americas, AsiaPacific, Europe }

/// <summary>A country as FIBA knows it: its three-letter code and zone. <see cref="Suspended"/> marks a federation FIBA has barred
/// from its competitions (Russia and Belarus since 2022): its players still have a nationality, but the team isn't ranked.</summary>
public sealed record Nation(string Name, string Code, FibaZone Zone, bool Suspended = false);

/// <summary>One player in a national team's pool or twelve.</summary>
public sealed record NationalPick(Player Player, Team? Club, bool College, int Age);

/// <summary>A national team built from every eligible player of that nationality in the world (#105, international plan I1).</summary>
public sealed class NationalTeam
{
    public required Nation Nation { get; init; }
    /// <summary>Every eligible player (17 or older, on a pro or college roster, or a free agent).</summary>
    public required IReadOnlyList<NationalPick> Pool { get; init; }
    /// <summary>The federation's twelve, best first.</summary>
    public required IReadOnlyList<NationalPick> Squad { get; init; }
    /// <summary>A team strength from the twelve's true ability (the federation's view, not your scouts').</summary>
    public double Strength { get; init; }
    /// <summary>1-based rank among the ranked (not suspended) teams; 0 when suspended.</summary>
    public int Rank { get; set; }
    public int ZoneRank { get; set; }
    /// <summary>I3 (run 210): FIBA ranking points (0 until <see cref="FibaRanking.Apply"/> ranks the teams by results).</summary>
    public double Points { get; set; }
    public string Name => Nation.Name;
}

/// <summary>
/// International plan, slice I1 (run 207): every country with enough players gets a national team, picked from the whole world
/// (pros, clubs abroad, college players and free agents). Pure and deterministic: no random draw, nothing saved, nothing written to
/// the session, so opening the page can't change a later result (run 200's lesson). Later slices play the FIBA calendar with these
/// teams (`claude/international-plan.md`).
/// </summary>
public static class NationalTeams
{
    /// <summary>#105: a country needs at least this many players anywhere to field a team.</summary>
    public const int MinPool = 12;
    public const int SquadSize = 12;
    /// <summary>The youngest a senior international may be (FIBA has no minimum; real senior call-ups start at about 17).</summary>
    public const int MinAge = 17;
    public const int MinGuards = 4, MinBigs = 3;

    private static readonly Nation[] Known =
    {
        // Americas
        new("USA", "USA", FibaZone.Americas), new("Canada", "CAN", FibaZone.Americas), new("Argentina", "ARG", FibaZone.Americas),
        new("Brazil", "BRA", FibaZone.Americas), new("Uruguay", "URU", FibaZone.Americas), new("Dominican Republic", "DOM", FibaZone.Americas),
        new("Puerto Rico", "PUR", FibaZone.Americas), new("Mexico", "MEX", FibaZone.Americas), new("Bahamas", "BAH", FibaZone.Americas),
        new("Jamaica", "JAM", FibaZone.Americas), new("Haiti", "HAI", FibaZone.Americas), new("Venezuela", "VEN", FibaZone.Americas),
        new("Colombia", "COL", FibaZone.Americas), new("Chile", "CHI", FibaZone.Americas), new("Panama", "PAN", FibaZone.Americas),
        new("Cuba", "CUB", FibaZone.Americas), new("Paraguay", "PAR", FibaZone.Americas), new("Peru", "PER", FibaZone.Americas),
        new("Ecuador", "ECU", FibaZone.Americas), new("Trinidad and Tobago", "TTO", FibaZone.Americas), new("US Virgin Islands", "ISV", FibaZone.Americas),
        new("Nicaragua", "NCA", FibaZone.Americas), new("Costa Rica", "CRC", FibaZone.Americas), new("Honduras", "HON", FibaZone.Americas),
        // Europe
        new("Serbia", "SRB", FibaZone.Europe), new("Croatia", "CRO", FibaZone.Europe), new("Slovenia", "SLO", FibaZone.Europe),
        new("Bosnia and Herzegovina", "BIH", FibaZone.Europe), new("Montenegro", "MNE", FibaZone.Europe), new("North Macedonia", "MKD", FibaZone.Europe),
        new("France", "FRA", FibaZone.Europe), new("Germany", "GER", FibaZone.Europe), new("Austria", "AUT", FibaZone.Europe),
        new("Spain", "ESP", FibaZone.Europe), new("Italy", "ITA", FibaZone.Europe), new("Greece", "GRE", FibaZone.Europe),
        new("Turkey", "TUR", FibaZone.Europe), new("Israel", "ISR", FibaZone.Europe), new("Finland", "FIN", FibaZone.Europe),
        new("Sweden", "SWE", FibaZone.Europe), new("Norway", "NOR", FibaZone.Europe), new("Denmark", "DEN", FibaZone.Europe),
        new("Lithuania", "LTU", FibaZone.Europe), new("Latvia", "LAT", FibaZone.Europe), new("Estonia", "EST", FibaZone.Europe),
        new("Ukraine", "UKR", FibaZone.Europe), new("Georgia", "GEO", FibaZone.Europe), new("Belgium", "BEL", FibaZone.Europe),
        new("Netherlands", "NED", FibaZone.Europe), new("Poland", "POL", FibaZone.Europe), new("Czechia", "CZE", FibaZone.Europe),
        new("Portugal", "POR", FibaZone.Europe), new("Switzerland", "SUI", FibaZone.Europe), new("Great Britain", "GBR", FibaZone.Europe),
        new("Ireland", "IRL", FibaZone.Europe), new("Hungary", "HUN", FibaZone.Europe),
        new("Slovakia", "SVK", FibaZone.Europe), new("Bulgaria", "BUL", FibaZone.Europe), new("Romania", "ROU", FibaZone.Europe),
        new("Iceland", "ISL", FibaZone.Europe), new("Cyprus", "CYP", FibaZone.Europe),
        new("Russia", "RUS", FibaZone.Europe, Suspended: true), new("Belarus", "BLR", FibaZone.Europe, Suspended: true),
        // Africa
        new("Senegal", "SEN", FibaZone.Africa), new("Cameroon", "CMR", FibaZone.Africa), new("Nigeria", "NGR", FibaZone.Africa),
        new("Mali", "MLI", FibaZone.Africa), new("Congo DR", "COD", FibaZone.Africa), new("Egypt", "EGY", FibaZone.Africa),
        new("Tunisia", "TUN", FibaZone.Africa), new("Morocco", "MAR", FibaZone.Africa), new("Angola", "ANG", FibaZone.Africa),
        new("Rwanda", "RWA", FibaZone.Africa), new("South Africa", "RSA", FibaZone.Africa), new("Ivory Coast", "CIV", FibaZone.Africa),
        new("South Sudan", "SSD", FibaZone.Africa), new("Sudan", "SUD", FibaZone.Africa), new("Guinea", "GUI", FibaZone.Africa),
        new("Cape Verde", "CPV", FibaZone.Africa), new("Gabon", "GAB", FibaZone.Africa), new("Ghana", "GHA", FibaZone.Africa),
        new("Kenya", "KEN", FibaZone.Africa), new("Uganda", "UGA", FibaZone.Africa), new("Congo", "CGO", FibaZone.Africa),
        new("Central African Republic", "CAF", FibaZone.Africa),
        // Asia and Oceania
        new("Australia", "AUS", FibaZone.AsiaPacific), new("New Zealand", "NZL", FibaZone.AsiaPacific), new("Japan", "JPN", FibaZone.AsiaPacific),
        new("China", "CHN", FibaZone.AsiaPacific), new("Philippines", "PHI", FibaZone.AsiaPacific), new("Korea", "KOR", FibaZone.AsiaPacific),
        new("Lebanon", "LBN", FibaZone.AsiaPacific), new("India", "IND", FibaZone.AsiaPacific), new("Pakistan", "PAK", FibaZone.AsiaPacific),
        new("Kazakhstan", "KAZ", FibaZone.AsiaPacific), new("Iran", "IRI", FibaZone.AsiaPacific), new("Jordan", "JOR", FibaZone.AsiaPacific),
        new("Chinese Taipei", "TPE", FibaZone.AsiaPacific), new("Qatar", "QAT", FibaZone.AsiaPacific),
        new("Saudi Arabia", "KSA", FibaZone.AsiaPacific), new("Indonesia", "INA", FibaZone.AsiaPacific), new("Syria", "SYR", FibaZone.AsiaPacific),
        new("Bahrain", "BRN", FibaZone.AsiaPacific), new("Guam", "GUM", FibaZone.AsiaPacific),
    };

    /// <summary>Review (run 207): other spellings map to one nation, so a country never splits into two teams with one code.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["England"] = "Great Britain", ["United Kingdom"] = "Great Britain", ["Scotland"] = "Great Britain", ["Wales"] = "Great Britain",
        ["Taiwan"] = "Chinese Taipei", ["South Korea"] = "Korea", ["United States"] = "USA", ["DR Congo"] = "Congo DR",
        ["Côte d'Ivoire"] = "Ivory Coast", ["Turkiye"] = "Turkey", ["Türkiye"] = "Turkey", ["Czech Republic"] = "Czechia",
        ["Bosnia"] = "Bosnia and Herzegovina", ["Macedonia"] = "North Macedonia",
    };

    private static readonly Dictionary<string, Nation> ByName = BuildIndex();

    private static Dictionary<string, Nation> BuildIndex()
    {
        var d = Known.ToDictionary(n => n.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, name) in Aliases) d[alias] = d[name];
        return d;
    }

    /// <summary>The FIBA nation for a player's nationality, or null when the game doesn't know it.</summary>
    public static Nation? NationOf(string nationality) => ByName.GetValueOrDefault(nationality.Trim());

    public static string ZoneName(FibaZone z) => z switch
    {
        FibaZone.Africa => "Africa",
        FibaZone.Americas => "Americas",
        FibaZone.AsiaPacific => "Asia-Pacific",
        _ => "Europe",
    };

    /// <summary>Every national team in the session's world, ranked. College players count; high-school prospects don't.</summary>
    public static IReadOnlyList<NationalTeam> Build(GameSession s)
    {
        var entries = new List<(Player P, Team? Club, bool College)>();
        var seen = new HashSet<int>();
        foreach (var t in s.League.Teams) foreach (var p in t.Roster) if (seen.Add(p.Id)) entries.Add((p, t, false));
        foreach (var t in s.World.Clubs) foreach (var p in t.Roster) if (seen.Add(p.Id)) entries.Add((p, t, false));
        if (s.World.College?.Programs is { } programs)
            foreach (var t in programs) foreach (var p in t.Roster) if (seen.Add(p.Id)) entries.Add((p, t, true));
        foreach (var p in s.League.FreeAgents) if (seen.Add(p.Id)) entries.Add((p, null, false));
        return Build(entries, s.Today);
    }

    /// <summary>I3 (run 210): the teams ranked by FIBA ranking points (the saved ranking, or its starting seed), not strength.
    /// Pure: the history is only read.</summary>
    public static IReadOnlyList<NationalTeam> Ranked(GameSession s)
    {
        var teams = Build(s);
        return FibaRanking.Apply(teams, FibaRanking.Current(s.World.International, teams));
    }

    /// <summary>The teams for a list of players (each with his club, or null for a free agent).</summary>
    public static IReadOnlyList<NationalTeam> Build(IEnumerable<(Player P, Team? Club, bool College)> players, DateOnly today)
    {
        var byNation = new Dictionary<Nation, List<NationalPick>>();
        foreach (var (p, club, college) in players)
        {
            if (NationOf(p.Nationality) is not { } n) continue;
            int age = p.Age(today);
            if (age < MinAge) continue;
            if (!byNation.TryGetValue(n, out var list)) byNation[n] = list = new();
            list.Add(new NationalPick(p, club, college, age));
        }

        var teams = new List<NationalTeam>();
        foreach (var (n, pool) in byNation)
        {
            if (pool.Count < MinPool) continue;
            var ordered = pool.OrderByDescending(x => x.Player.CurrentAbility).ThenBy(x => x.Player.Id).ToList();
            var squad = PickTwelve(ordered);
            teams.Add(new NationalTeam { Nation = n, Pool = ordered, Squad = squad, Strength = StrengthOf(squad) });
        }

        int rank = 0;
        foreach (var t in teams.OrderByDescending(t => t.Strength).ThenBy(t => t.Name, StringComparer.Ordinal))
            t.Rank = t.Nation.Suspended ? 0 : ++rank;
        foreach (var zone in teams.Where(t => !t.Nation.Suspended).GroupBy(t => t.Nation.Zone))
        {
            int z = 0;
            foreach (var t in zone.OrderBy(t => t.Rank)) t.ZoneRank = ++z;
        }
        return teams.OrderBy(t => t.Rank == 0 ? int.MaxValue : t.Rank).ThenBy(t => t.Name, StringComparer.Ordinal).ToList();
    }

    private static bool Guard(Player p) => p.PrimaryPosition is Position.PG or Position.SG;
    private static bool Big(Player p) => p.PrimaryPosition is Position.PF or Position.C;

    /// <summary>The best twelve, keeping at least four guards and three bigs when the pool has them. Best first.</summary>
    public static IReadOnlyList<NationalPick> PickTwelve(IReadOnlyList<NationalPick> bestFirst)
    {
        var squad = bestFirst.Take(SquadSize).ToList();
        Balance(squad, bestFirst, x => Guard(x.Player), MinGuards, x => !Guard(x.Player) && (!Big(x.Player) || squad.Count(y => Big(y.Player)) > MinBigs));
        Balance(squad, bestFirst, x => Big(x.Player), MinBigs, x => !Big(x.Player) && (!Guard(x.Player) || squad.Count(y => Guard(y.Player)) > MinGuards));
        return squad.OrderByDescending(x => x.Player.CurrentAbility).ThenBy(x => x.Player.Id).ToList();
    }

    private static void Balance(List<NationalPick> squad, IReadOnlyList<NationalPick> bestFirst, Func<NationalPick, bool> need, int min, Func<NationalPick, bool> droppable)
    {
        while (squad.Count(need) < min)
        {
            var add = bestFirst.FirstOrDefault(x => need(x) && !squad.Contains(x));
            var drop = squad.Where(droppable).OrderBy(x => x.Player.CurrentAbility).ThenByDescending(x => x.Player.Id).FirstOrDefault();
            if (add == null || drop == null) return;
            squad.Remove(drop);
            squad.Add(add);
        }
    }

    /// <summary>The starting five count most, the bench less: 70% the top five's average ability, 30% the next five's.</summary>
    public static double StrengthOf(IReadOnlyList<NationalPick> squad)
    {
        var cas = squad.Select(x => (double)x.Player.CurrentAbility).OrderByDescending(x => x).ToList();
        if (cas.Count == 0) return 0;
        double top = cas.Take(5).Average();
        double next = cas.Skip(5).Take(5).DefaultIfEmpty(top).Average();
        return Math.Round(0.7 * top + 0.3 * next, 1);
    }
}
