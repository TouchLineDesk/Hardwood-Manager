using Hardwood.Core.Model;

namespace Hardwood.Core.World;

/// <summary>
/// The USA pyramid below the NABL (decisions #73, #78-79, #91-94): two national tiers of 24, six regional leagues of 18
/// and sixteen local leagues of about 14, roughly 380 clubs, all linked by promotion and relegation. Every club has a
/// home area (from its city), so a club dropping out of a national tier lands in its own region, and a regional club
/// in its own local league. Since run 27 (Danny, #318–#319) every league is split into regionally close divisions, and
/// each division's winner goes up and its last club goes down.
/// </summary>
public static class UsaPyramid
{
    public const string Tier2 = "usa2", Tier3 = "usa3";
    public const int NationalSize = 24, RegionalSize = 18;

    public sealed record Area(string Key, string Name, string Short, string Region, string[] Cities);

    public static readonly (string Key, string Name, string Short)[] Regions =
    {
        ("ne", "Northeast League", "NEL"), ("se", "Southeast League", "SEL"), ("mw", "Midwest League", "MWL"),
        ("sc", "South Central League", "SCL"), ("mt", "Mountain League", "MTL"), ("pa", "Pacific League", "PCL"),
    };

    /// <summary>The sixteen local areas (each a local league), biggest markets first.</summary>
    public static readonly Area[] Areas =
    {
        new("nen", "New England League", "NEN", "ne", new[] { "Boston", "Providence", "Hartford", "Worcester", "Springfield", "New Haven", "Bridgeport", "Manchester", "Lewiston", "Burlington", "Lowell", "Stamford", "Waterbury", "Bangor", "Concord", "Pittsfield" }),
        new("nyl", "Empire State League", "ESL", "ne", new[] { "New York", "Buffalo", "Rochester", "Syracuse", "Albany", "Yonkers", "Long Island", "Newark", "Jersey City", "Binghamton", "Utica", "Paterson", "Trenton", "Ithaca", "Poughkeepsie", "Schenectady" }),
        new("mid", "Mid-Atlantic League", "MAL", "ne", new[] { "Philadelphia", "Pittsburgh", "Baltimore", "Washington", "Allentown", "Harrisburg", "Wilmington", "Scranton", "Erie", "Reading", "Lancaster", "Atlantic City", "Camden", "Annapolis", "Frederick", "Altoona" }),
        new("car", "Carolinas League", "CAR", "se", new[] { "Charlotte", "Raleigh", "Richmond", "Norfolk", "Greensboro", "Durham", "Virginia Beach", "Charleston", "Columbia", "Greenville", "Winston-Salem", "Asheville", "Rocky Mount", "Roanoke", "Fayetteville", "Myrtle Beach" }),
        new("dix", "Dixie League", "DIX", "se", new[] { "Atlanta", "Nashville", "Memphis", "Birmingham", "Knoxville", "Chattanooga", "Huntsville", "Savannah", "Montgomery", "Augusta", "Mobile", "Jackson", "Macon", "Valdosta", "Tuscaloosa", "Athens" }),
        new("fla", "Sunshine League", "SUN", "se", new[] { "Miami", "Orlando", "Tampa", "Jacksonville", "Fort Lauderdale", "St. Petersburg", "Tallahassee", "West Palm Beach", "Gainesville", "Pensacola", "Sarasota", "Fort Myers", "Daytona Beach", "Lakeland", "Naples", "Ocala" }),
        new("gle", "Great Lakes League", "GLL", "mw", new[] { "Detroit", "Cleveland", "Columbus", "Cincinnati", "Toledo", "Akron", "Dayton", "Grand Rapids", "Lansing", "Flint", "Ann Arbor", "Kalamazoo", "Youngstown", "Canton", "Saginaw", "Traverse City" }),
        new("prl", "Prairie League", "PRA", "mw", new[] { "Chicago", "Milwaukee", "Indianapolis", "Fort Wayne", "Madison", "Green Bay", "Rockford", "Peoria", "South Bend", "Evansville", "Joliet", "Champaign", "Gary", "Bloomington", "Kenosha", "Oshkosh" }),
        new("pln", "Plains League", "PLN", "mw", new[] { "Minneapolis", "St. Louis", "Kansas City", "St. Paul", "Omaha", "Des Moines", "Wichita", "Lincoln", "Sioux Falls", "Fargo", "Cedar Rapids", "Topeka", "Joplin", "Duluth", "St. Cloud", "Davenport" }),
        new("txn", "North Texas League", "NTX", "sc", new[] { "Dallas", "Fort Worth", "Arlington", "Plano", "Lubbock", "Amarillo", "Frisco", "Garland", "Irving", "Waco", "Denton", "Abilene", "Midland", "Odessa", "Tyler", "Wichita Falls" }),
        new("gul", "Gulf Coast League", "GUL", "sc", new[] { "Houston", "San Antonio", "Austin", "New Orleans", "El Paso", "Baton Rouge", "Corpus Christi", "Shreveport", "Laredo", "Lafayette", "Beaumont", "Brownsville", "McAllen", "Killeen", "Galveston", "Lake Charles" }),
        new("okr", "Red River League", "RRL", "sc", new[] { "Oklahoma City", "Tulsa", "Little Rock", "Norman", "Broken Arrow", "Edmond", "Fort Smith", "Rogers", "Lawton", "Jonesboro", "Stillwater", "Conway", "Enid", "Muskogee", "Hot Springs", "Pine Bluff" }),
        new("rky", "Rockies League", "RKY", "mt", new[] { "Denver", "Salt Lake City", "Colorado Springs", "Boise", "Aurora", "Fort Collins", "Provo", "Boulder", "Ogden", "Pueblo", "Billings", "Cheyenne", "Missoula", "Grand Junction", "Bozeman", "Idaho Falls" }),
        new("dsw", "Desert League", "DES", "mt", new[] { "Phoenix", "Las Vegas", "Tucson", "Albuquerque", "Mesa", "Henderson", "Scottsdale", "Reno", "Chandler", "Tempe", "Glendale", "Santa Fe", "Las Cruces", "Flagstaff", "Yuma", "Prescott" }),
        new("cal", "Golden Coast League", "GCL", "pa", new[] { "Los Angeles", "San Francisco", "San Diego", "San Jose", "Sacramento", "Oakland", "Long Beach", "Fresno", "Anaheim", "Riverside", "Bakersfield", "Irvine", "Stockton", "Santa Barbara", "Modesto", "Santa Cruz" }),
        new("pnw", "Cascade League", "CAS", "pa", new[] { "Seattle", "Portland", "Spokane", "Tacoma", "Honolulu", "Anchorage", "Vancouver", "Eugene", "Salem", "Everett", "Bellingham", "Olympia", "Yakima", "Bend", "Medford", "Kennewick" }),
    };

    /// <summary>NABL divisions → the area a relegated club whose city isn't listed calls home.</summary>
    private static readonly Dictionary<string, string> DivisionHome = new()
    {
        ["Atlantic"] = "nyl", ["Central"] = "prl", ["Southeast"] = "dix", ["Northwest"] = "rky", ["Pacific"] = "cal", ["Southwest"] = "gul",
    };

    private static readonly Dictionary<string, string> CityArea =
        Areas.SelectMany(a => a.Cities.Select(c => (c, a.Key))).ToDictionary(x => x.c, x => x.Key, StringComparer.OrdinalIgnoreCase);

    public static string RegionalKey(string region) => $"usa-r-{region}";
    public static string LocalKey(string area) => $"usa-l-{area}";
    public static bool IsPyramid(string key) => key == Tier2 || key == Tier3 || key.StartsWith("usa-");

    /// <summary>A club's home area: from its city, else (an NABL club from a city we don't list) its division.</summary>
    public static string AreaOf(Team t) =>
        CityArea.TryGetValue(t.City, out var a) ? a : TeamHome.TryGetValue(t.City, out var h) ? h : DivisionHome.TryGetValue(t.Division, out var d) ? d : "prl";

    /// <summary>Top-league clubs named after a state or a borough (#318): their home area, so they keep it in any division.</summary>
    private static readonly Dictionary<string, string> TeamHome = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Brooklyn"] = "nyl", ["Toronto"] = "gle", ["Indiana"] = "prl", ["Minnesota"] = "pln", ["Utah"] = "rky", ["Golden State"] = "cal",
    };

    public static string RegionOf(Team t) => Areas.First(a => a.Key == AreaOf(t)).Region;

    /// <summary>Pyramid level: 2 and 3 national, 4 regional, 5 local; 0 for anything else.</summary>
    public static int Level(string key) => key == Tier2 ? 2 : key == Tier3 ? 3 : key.StartsWith("usa-r-") ? 4 : key.StartsWith("usa-l-") ? 5 : 0;

    // ------------------------------------------------------------------------------------------
    // The clubs and leagues (fixed data, built the same way every time).

    /// <summary>The 24 leagues (built once, after the name lists below exist).</summary>
    public static CompetitionProfile[] Profiles => _profiles ??= Build();
    private static CompetitionProfile[]? _profiles;

    private static readonly string[] Nicknames =
    {
        "Roughnecks", "Pines", "Foundry", "Mustangs", "Pioneers", "Rivermen", "Comets", "Thunder", "Mariners", "Ironmen", "Hawks", "Lumberjacks",
        "Stallions", "Express", "Monarchs", "Rapids", "Bison", "Coyotes", "Generals", "Admirals", "Blaze", "Vipers", "Sentinels", "Outlaws",
        "Grizzlies", "Redwoods", "Cyclones", "Hornets", "Marlins", "Stingrays", "Wranglers", "Steelheads", "Owls", "Falcons", "Knights", "Patriots",
        "Rattlers", "Miners", "Voyagers", "Barons", "Gators", "Bulldogs", "Raptors", "Tritons", "Condors", "Mavericks", "Spartans", "Colonels",
        "Rebels", "Clippers", "Navigators", "Highlanders", "Rangers", "Huskies", "Shockers", "Rockets", "Toros", "Bobcats", "Wolves", "Dragons",
    };
    private static readonly string[] ClubStyles = { "United", "City", "BC", "Athletic", "Basketball Club", "FC" };

    private static CompetitionProfile[] Build()
    {
        var rng = new Rng(0x5059524D4944UL);
        // Every city gets a club; the biggest half of each area also gets a second ("United", "City"...).
        var clubs = new List<(ClubSeed seed, string area, double market)>();
        // Abbreviations must not clash with the NABL's (or each other's).
        var used = new HashSet<string>(Generation.NamePool.Franchises.Select(f => f.Abbr));
        string Abbr(string city)
        {
            var letters = new string(city.Where(char.IsLetter).ToArray()).ToUpperInvariant();
            var words = city.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string a = words.Length >= 2 ? $"{words[0][0]}{words[1][0]}{(words.Length > 2 ? words[2][0] : letters[^1])}".ToUpperInvariant() : letters[..Math.Min(3, letters.Length)];
            if (used.Add(a)) return a;
            // Other letters of the name before resorting to a number.
            var l = letters;
            foreach (var b in new[] { $"{l[0]}{l[1]}{l[^1]}", $"{l[0]}{l[2 % l.Length]}{l[^1]}", $"{l[0]}{l[^2]}{l[^1]}", $"{l[0]}{l[1]}{l[3 % l.Length]}", $"{l[0]}{l[3 % l.Length]}{l[^1]}" })
                if (used.Add(b)) return b;
            for (int i = 2; i < 10; i++) { var b = a[..2] + i; if (used.Add(b)) return b; }
            return a;
        }
        foreach (var area in Areas)
        {
            for (int i = 0; i < area.Cities.Length; i++)
            {
                var city = area.Cities[i];
                bool fcStyle = rng.Chance(0.3);
                string name = fcStyle ? rng.Pick(ClubStyles) : rng.Pick(Nicknames);
                clubs.Add((new ClubSeed(city, name, Abbr(city), "USA"), area.Key, 16 - i + rng.NextDouble() * 4));
                if (i < 8)
                {
                    string second = rng.Pick(ClubStyles.Where(s => s != name).ToArray());
                    clubs.Add((new ClubSeed(city, second, Abbr(city + " " + second), "USA"), area.Key, 12 - i + rng.NextDouble() * 4));
                }
            }
        }
        // Biggest markets start highest: tier 2, tier 3, then each region's next 18, the rest local.
        var byMarket = clubs.OrderByDescending(c => c.market).ToList();
        var tier2 = byMarket.Take(NationalSize).ToList();
        var tier3 = byMarket.Skip(NationalSize).Take(NationalSize).ToList();
        var rest = byMarket.Skip(NationalSize * 2).ToList();
        var regional = new Dictionary<string, List<(ClubSeed seed, string area, double market)>>();
        foreach (var (rk, _, _) in Regions)
            regional[rk] = rest.Where(c => Areas.First(a => a.Key == c.area).Region == rk).Take(RegionalSize).ToList();
        var inRegional = regional.Values.SelectMany(x => x).Select(x => x.seed).ToHashSet();
        var local = rest.Where(c => !inRegional.Contains(c.seed)).GroupBy(c => c.area).ToDictionary(g => g.Key, g => g.ToList());

        var profiles = new List<CompetitionProfile>
        {
            National(Tier2, "The Championship", "CHP", -2, 0.10, 4, -10, tier2.Select(x => x.seed)),
            National(Tier3, "National League One", "NL1", -10, 0.06, 3, -16, tier3.Select(x => x.seed)),
        };
        foreach (var (rk, name, shortName) in Regions)
            profiles.Add(Lower(RegionalKey(rk), name, shortName, -22, 0.02, 2, -26, 2, regional[rk].Select(x => x.seed)));
        foreach (var a in Areas)
            profiles.Add(Lower(LocalKey(a.Key), a.Name, a.Short, -34, 0.006, 1, -34, 1, local.TryGetValue(a.Key, out var l) ? l.Select(x => x.seed) : Enumerable.Empty<ClubSeed>()));
        return profiles.ToArray();
    }

    private static CompetitionProfile National(string key, string name, string shortName, int strength, double wages, int imports, double prestige, IEnumerable<ClubSeed> clubs) => new()
    {
        Key = key, Name = name, Short = shortName, Country = "USA", LocalNationalities = new[] { "USA" },
        StrengthOffset = strength, ForeignLimit = imports, SalaryScale = wages, JobPrestige = prestige, Rules = Rules.Nba, MinAge = 23,
        Start = (10, 21), RoundDays = new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday }, Clubs = clubs.ToArray(),
    };

    private static CompetitionProfile Lower(string key, string name, string shortName, int strength, double wages, int imports, double prestige, int roundsPerWeek, IEnumerable<ClubSeed> clubs) => new()
    {
        Key = key, Name = name, Short = shortName, Country = "USA", LocalNationalities = new[] { "USA" },
        StrengthOffset = strength, ForeignLimit = imports, SalaryScale = wages, JobPrestige = prestige, Rules = Rules.Nba, MinAge = 23,
        Start = (10, 24), RoundDays = roundsPerWeek == 2 ? new[] { DayOfWeek.Tuesday, DayOfWeek.Saturday } : new[] { DayOfWeek.Saturday },
        Clubs = clubs.ToArray(),
    };

    // ------------------------------------------------------------------------------------------
    // Divisions (Danny, #318): every pyramid league is split into regionally close divisions. They are drawn from the
    // clubs' home areas each summer (after promotion and relegation) and when a world loads, so they never need saving.

    /// <summary>The areas from east to west: how divisions and conferences are drawn.</summary>
    private static readonly string[] EastToWest = { "nen", "nyl", "mid", "car", "fla", "dix", "gle", "prl", "pln", "okr", "txn", "gul", "rky", "dsw", "cal", "pnw" };

    /// <summary>
    /// Each local area's two halves (general knowledge of the cities' geography): the first half's name and cities, then
    /// the second half's name. A local league's two divisions are named after the half most of their clubs come from.
    /// </summary>
    private static readonly Dictionary<string, (string A, string[] Cities, string B)> Halves = new()
    {
        ["nen"] = ("North", new[] { "Boston", "Worcester", "Manchester", "Lewiston", "Burlington", "Lowell", "Bangor", "Concord" }, "South"),
        ["nyl"] = ("Upstate", new[] { "Buffalo", "Rochester", "Syracuse", "Albany", "Binghamton", "Utica", "Ithaca", "Poughkeepsie", "Schenectady" }, "Metro"),
        ["mid"] = ("North", new[] { "Pittsburgh", "Allentown", "Harrisburg", "Scranton", "Erie", "Reading", "Lancaster", "Altoona" }, "South"),
        ["car"] = ("North", new[] { "Raleigh", "Richmond", "Norfolk", "Greensboro", "Durham", "Virginia Beach", "Winston-Salem", "Rocky Mount", "Roanoke" }, "South"),
        ["dix"] = ("East", new[] { "Atlanta", "Knoxville", "Chattanooga", "Savannah", "Augusta", "Macon", "Valdosta", "Athens" }, "West"),
        ["fla"] = ("North", new[] { "Orlando", "Jacksonville", "Tallahassee", "Gainesville", "Pensacola", "Daytona Beach", "Lakeland", "Ocala" }, "South"),
        ["gle"] = ("Michigan", new[] { "Detroit", "Grand Rapids", "Lansing", "Flint", "Ann Arbor", "Kalamazoo", "Saginaw", "Traverse City" }, "Ohio"),
        ["prl"] = ("North", new[] { "Chicago", "Milwaukee", "Madison", "Green Bay", "Rockford", "Joliet", "Gary", "Kenosha", "Oshkosh" }, "South"),
        ["pln"] = ("North", new[] { "Minneapolis", "St. Paul", "Sioux Falls", "Fargo", "Cedar Rapids", "Duluth", "St. Cloud", "Davenport" }, "South"),
        ["txn"] = ("East", new[] { "Dallas", "Fort Worth", "Arlington", "Plano", "Frisco", "Garland", "Irving", "Waco", "Denton", "Tyler" }, "West"),
        ["gul"] = ("East", new[] { "Houston", "New Orleans", "Baton Rouge", "Shreveport", "Lafayette", "Beaumont", "Galveston", "Lake Charles" }, "West"),
        ["okr"] = ("Oklahoma", new[] { "Oklahoma City", "Tulsa", "Norman", "Broken Arrow", "Edmond", "Lawton", "Stillwater", "Enid", "Muskogee" }, "Arkansas"),
        ["rky"] = ("North", new[] { "Salt Lake City", "Boise", "Provo", "Ogden", "Billings", "Missoula", "Bozeman", "Idaho Falls" }, "South"),
        ["dsw"] = ("Arizona", new[] { "Phoenix", "Tucson", "Mesa", "Scottsdale", "Chandler", "Tempe", "Glendale", "Flagstaff", "Yuma", "Prescott" }, "Nevada & New Mexico"),
        ["cal"] = ("North", new[] { "San Francisco", "San Jose", "Sacramento", "Oakland", "Fresno", "Stockton", "Modesto", "Santa Cruz" }, "South"),
        ["pnw"] = ("North", new[] { "Seattle", "Spokane", "Tacoma", "Anchorage", "Vancouver", "Everett", "Bellingham", "Olympia", "Yakima", "Kennewick" }, "South"),
    };

    /// <summary>How many divisions a pyramid league has: six in the national tiers (three per conference, as the top
    /// league), two in a regional league, two in a local league of eight or more.</summary>
    public static int DivisionCount(string key, int clubs) => Level(key) switch { 2 or 3 => 6, 4 => 2, 5 => clubs >= 8 ? 2 : 1, _ => 1 };

    private static bool FirstHalf(Team t) => Halves.TryGetValue(AreaOf(t), out var h) && h.Cities.Contains(t.City, StringComparer.OrdinalIgnoreCase);

    /// <summary>A club's place from east to west (area, then its half of the area, then its market).</summary>
    private static int GeoRank(Team t)
    {
        var area = AreaOf(t);
        int a = Array.IndexOf(EastToWest, area);
        var cities = Areas.First(x => x.Key == area).Cities;
        int c = Array.FindIndex(cities, x => x.Equals(t.City, StringComparison.OrdinalIgnoreCase));
        return a * 100 + (FirstHalf(t) ? 0 : 50) + Math.Max(0, c);
    }

    /// <summary>Give every pyramid club its conference and division for the coming season (Danny, #318).</summary>
    public static void AssignDivisions(GameWorld world)
    {
        foreach (var c in world.Competitions.Where(c => IsPyramid(c.Key) && c.Clubs.Count > 0))
        {
            int level = Level(c.Key);
            var ordered = c.Clubs.OrderBy(GeoRank).ThenBy(t => t.Id).ToList();
            int n = ordered.Count, divs = Math.Min(DivisionCount(c.Key, n), n);
            var used = new HashSet<string>();
            int at = 0;
            for (int d = 0; d < divs; d++)
            {
                int size = n / divs + (d < n % divs ? 1 : 0);
                var group = ordered.Skip(at).Take(size).ToList();
                at += size;
                string name;
                if (divs == 1) name = c.Profile.Short;
                else if (level == 5)
                {
                    var h = Halves.TryGetValue(AreaOf(group[0]), out var hh) ? hh : (A: "North", Cities: Array.Empty<string>(), B: "South");
                    name = group.Count(FirstHalf) * 2 >= group.Count ? h.A : h.B;
                    if (!used.Add(name)) { name = name == h.A ? h.B : h.A; used.Add(name); }
                }
                else
                {
                    // Named after the region most of its clubs come from ("Southeast"), or else the area ("Great Lakes"), not yet taken.
                    var regions = group.GroupBy(RegionOf).OrderByDescending(g => g.Count()).ThenBy(g => Array.FindIndex(Regions, r => r.Key == g.Key)).ToList();
                    string RegionName(string k) => Regions.First(r => r.Key == k).Name.Replace(" League", "");
                    var areaNames = group.GroupBy(AreaOf).OrderByDescending(g => g.Count()).ThenBy(g => Array.IndexOf(EastToWest, g.Key))
                        .Select(g => Areas.First(a => a.Key == g.Key).Name.Replace(" League", ""));
                    // A regional league is its region already: its divisions take their areas' names.
                    if (level == 4) regions.Clear();
                    name = regions.Where(g => g.Count() * 2 >= group.Count).Select(g => RegionName(g.Key))
                        .Concat(areaNames).Concat(regions.Select(g => RegionName(g.Key)))
                        .FirstOrDefault(x => !used.Contains(x)) ?? $"Division {d + 1}";
                    used.Add(name);
                }
                var conf = level <= 3 && d >= divs / 2 ? Conference.West : Conference.East;
                foreach (var t in group) { t.Division = name; t.Conference = conf; }
            }
        }
    }

    /// <summary>A league's divisions in the order they were drawn, each from first to last.</summary>
    public static List<List<T>> ByDivision<T>(IEnumerable<T> table, Func<T, Team> team) =>
        table.GroupBy(x => team(x).Division).Select(g => g.ToList()).OrderBy(g => g.Min(x => GeoRank(team(x)))).ToList();

    // ------------------------------------------------------------------------------------------
    // Promotion and relegation (each summer, before the next season's fixtures are made).

    /// <summary>A club's place in its final table: its division's rows are in order, first to last.</summary>
    public sealed record Standing(Team Team, int Wins, int Losses)
    {
        public double Pct => Wins + Losses == 0 ? 0.5 : (double)Wins / (Wins + Losses);
    }

    /// <summary>
    /// Who swaps between two levels (Danny, #319): the last club in each of the upper league's divisions goes down and
    /// each division winner below goes up. When the counts differ, the worst second-to-last clubs (then third-to-last)
    /// go down too, or the best runners-up go up, until they match. A division's winner never goes down, nor its last up.
    /// </summary>
    public static (List<Team> Down, List<Team> Up) Swap(List<List<Standing>> upper, List<List<Standing>> lower)
    {
        var down = upper.Where(d => d.Count > 1).Select(d => d[^1]).ToList();
        var up = lower.Where(d => d.Count > 1).Select(d => d[0]).ToList();
        int n = Math.Max(down.Count, up.Count);
        for (int k = 2; down.Count < n && upper.Any(d => d.Count > k); k++)
            down.AddRange(upper.Where(d => d.Count > k).Select(d => d[^k]).OrderBy(x => x.Pct).ThenBy(x => x.Wins).Take(n - down.Count).ToList());
        for (int k = 2; up.Count < n && lower.Any(d => d.Count > k); k++)
            up.AddRange(lower.Where(d => d.Count > k).Select(d => d[k - 1]).OrderByDescending(x => x.Pct).ThenByDescending(x => x.Wins).Take(n - up.Count).ToList());
        n = Math.Min(down.Count, up.Count);
        return (down.Take(n).Select(x => x.Team).ToList(), up.Take(n).Select(x => x.Team).ToList());
    }

    /// <summary>Every move at season's end, worked out from the tables before anyone moves.</summary>
    public sealed class Plan
    {
        public List<Team> NablDown = new(), T2Up = new(), T2Down = new(), T3Up = new(), T3Down = new(), RegionalUp = new();
        /// <summary>Per regional league: its clubs going down to the local leagues, and the local clubs coming up.</summary>
        public Dictionary<string, (List<Team> Down, List<Team> Up)> Regional = new();
        public HashSet<Team> Promoted => T2Up.Concat(T3Up).Concat(RegionalUp).Concat(Regional.Values.SelectMany(r => r.Up)).ToHashSet();
        public HashSet<Team> Relegated => NablDown.Concat(T2Down).Concat(T3Down).Concat(Regional.Values.SelectMany(r => r.Down)).ToHashSet();
    }

    /// <summary>A pyramid league's table, divisions in order, from the season played so far (or the clubs as listed).</summary>
    public static List<List<Standing>> Divisions(GameWorld world, WorldCompetition c)
    {
        var rows = c.Season.Fixtures.Any(f => f.Played)
            ? c.Season.Table().Where(r => world.TeamById(r.TeamId) is { } t && c.Clubs.Contains(t)).Select(r => new Standing(world.TeamById(r.TeamId)!, r.Wins, r.Losses)).ToList()
            : new List<Standing>();
        rows.AddRange(c.Clubs.Where(t => rows.All(r => r.Team != t)).Select(t => new Standing(t, 0, 0)));
        return ByDivision(rows, r => r.Team);
    }

    /// <summary>
    /// The season's moves. <paramref name="nabl"/> is the top league's final (or current) table, best first.
    /// Returns null if the world has no pyramid (an older career).
    /// </summary>
    public static Plan? MakePlan(GameWorld world, IEnumerable<Standing> nabl)
    {
        var t2 = world.Competitions.FirstOrDefault(c => c.Key == Tier2);
        var t3 = world.Competitions.FirstOrDefault(c => c.Key == Tier3);
        if (t2 == null || t3 == null) return null;
        var plan = new Plan();
        var top = ByDivision(nabl.OrderByDescending(r => r.Pct).ThenByDescending(r => r.Wins).ToList(), r => r.Team);
        var d2 = Divisions(world, t2);
        var d3 = Divisions(world, t3);
        (plan.NablDown, plan.T2Up) = Swap(top, d2);
        (plan.T2Down, plan.T3Up) = Swap(d2, d3);
        var regionals = Regions.Select(r => world.Competitions.FirstOrDefault(c => c.Key == RegionalKey(r.Key))).OfType<WorldCompetition>().ToList();
        var dr = regionals.ToDictionary(c => c.Key, c => Divisions(world, c));
        (plan.T3Down, plan.RegionalUp) = Swap(d3, dr.Values.SelectMany(x => x).ToList());
        foreach (var reg in regionals)
        {
            var region = reg.Key["usa-r-".Length..];
            var locals = Areas.Where(a => a.Region == region).Select(a => world.Competitions.FirstOrDefault(c => c.Key == LocalKey(a.Key)))
                .OfType<WorldCompetition>().SelectMany(c => Divisions(world, c)).ToList();
            plan.Regional[reg.Key] = Swap(dr[reg.Key], locals);
        }
        return plan;
    }

    /// <summary>The top league's table for the summer's moves: last season's records.</summary>
    public static List<Standing> NablTable(League nabl)
    {
        var last = nabl.History.LastOrDefault();
        if (last == null || last.Records.Count == 0) return new();
        var wins = last.Records.ToDictionary(r => r.teamId, r => (r.wins, r.losses));
        return nabl.Teams.Select(t => wins.TryGetValue(t.Id, out var w) ? new Standing(t, w.wins, w.losses) : new Standing(t, 41, 41)).ToList();
    }

    /// <summary>The NBA's divisions from east to west, so a promoted club takes the vacated place nearest its home.</summary>
    private static int TopDivisionRank(Team t) => t.Division switch
    {
        "Atlantic" => 0, "Southeast" => 1, "Central" => 2, "Southwest" => 3, "Northwest" => 4, "Pacific" => 5,
        _ => t.Conference == Conference.East ? 1 : 4,
    };

    /// <summary>
    /// Move clubs between levels using the finished tables (Danny, #318–#319): the last club in each of the top league's
    /// six divisions goes down and each of tier 2's six division winners comes up; the same between every pair of levels
    /// below, the counts matched by <see cref="Swap"/>. League sizes stay fixed (regional leagues are brought back to 18).
    /// Returns headlines. Does nothing if the world has no pyramid (an older career).
    /// </summary>
    public static List<(string Title, string Body)> Promote(GameWorld world, League nabl, Generation.StaffGenerator staffGen, DateOnly on)
    {
        var news = new List<(string, string)>();
        var t2 = world.Competitions.FirstOrDefault(c => c.Key == Tier2);
        var t3 = world.Competitions.FirstOrDefault(c => c.Key == Tier3);
        if (t2 == null || t3 == null) return news;
        // Clubs from older saves may not have divisions yet.
        AssignDivisions(world);
        var nablTable = NablTable(nabl);
        var plan = MakePlan(world, nablTable.Count > 0 ? nablTable : nabl.Teams.Select(t => new Standing(t, 0, 0)))!;
        if (nablTable.Count == 0) plan.NablDown.Clear();
        var regionals = Regions.Select(r => world.Competitions.FirstOrDefault(c => c.Key == RegionalKey(r.Key))).OfType<WorldCompetition>().ToList();
        var locals = Areas.Select(a => world.Competitions.FirstOrDefault(c => c.Key == LocalKey(a.Key))).OfType<WorldCompetition>().ToList();
        var tables = world.Competitions.Where(c => IsPyramid(c.Key)).ToDictionary(c => c.Key, c => Divisions(world, c).SelectMany(d => d).Select(r => r.Team).ToList());

        void Move(Team t, WorldCompetition to)
        {
            foreach (var c in world.Competitions.Where(c => IsPyramid(c.Key) && c.Clubs.Contains(t))) c.Clubs.Remove(t);
            to.Clubs.Add(t);
        }
        string Names(IEnumerable<Team> ts) => string.Join(", ", ts.Select(t => t.FullName));
        string Divs(IEnumerable<Team> ts) => string.Join(", ", ts.Select(t => $"{t.FullName} ({t.Division})"));

        // The top league ↔ tier 2: each division's last team down, each tier-2 division winner up (none up from the top).
        var nablUp = plan.T2Up.Take(plan.NablDown.Count).ToList();
        if (plan.NablDown.Count > 0)
        {
            string downText = Divs(plan.NablDown), upText = Divs(nablUp);
            var slots = plan.NablDown.OrderBy(TopDivisionRank).Select(t => (t.Conference, t.Division)).ToList();
            var arriving = nablUp.OrderBy(t => Array.IndexOf(EastToWest, AreaOf(t))).ThenBy(t => t.Id).ToList();
            foreach (var r in plan.NablDown) { nabl.Teams.Remove(r); t2.Clubs.Add(r); }
            for (int i = 0; i < arriving.Count; i++)
            {
                var p = arriving[i];
                t2.Clubs.Remove(p); nabl.Teams.Add(p);
                (p.Conference, p.Division) = slots[i];
                p.Tactic.Pace = new Tactic().Pace;
                if (p.Staff.Count == 0)
                    foreach (var role in Enum.GetValues<StaffRole>()) { var st = staffGen.Generate(nabl, role, on, 8); st.TeamId = p.Id; p.Staff.Add(st); }
            }
            news.Add(($"Promoted to the {Hardwood.Core.Names.Brand.Top}: {Names(nablUp)}", $"The division winners of {t2.Profile.Name} move up to the {Hardwood.Core.Names.Brand.Top}: {upText}."));
            news.Add(($"Relegated from the {Hardwood.Core.Names.Brand.Top}: {Names(plan.NablDown)}", $"The last team in each {Hardwood.Core.Names.Brand.Top} division drops into {t2.Profile.Name}: {downText}."));
        }
        // Tier 2 ↔ tier 3.
        foreach (var t in plan.T2Down) Move(t, t3);
        foreach (var t in plan.T3Up) Move(t, t2);
        news.Add(($"{t2.Profile.Name}: up and down", $"Division winners promoted from {t3.Profile.Name}: {Divs(plan.T3Up)}. Relegated to {t3.Profile.Name}: {Divs(plan.T2Down)}."));
        // Tier 3 ↔ regional leagues: every regional division winner goes up; tier 3's clubs go home to their regions.
        foreach (var t in plan.T3Down) Move(t, regionals.First(c => c.Key == RegionalKey(RegionOf(t))));
        foreach (var t in plan.RegionalUp) Move(t, t3);
        news.Add(($"{t3.Profile.Name}: up and down", $"Regional division winners promoted: {Divs(plan.RegionalUp)}. Relegated to their regions: {Divs(plan.T3Down)}."));
        // Regional ↔ local leagues.
        foreach (var reg in regionals)
        {
            var area = reg.Key["usa-r-".Length..];
            var myLocals = locals.Where(c => Areas.First(a => LocalKey(a.Key) == c.Key).Region == area).ToList();
            var (down, champs) = plan.Regional[reg.Key];
            foreach (var t in down) Move(t, myLocals.FirstOrDefault(c => c.Key == LocalKey(AreaOf(t))) ?? myLocals[0]);
            foreach (var t in champs) Move(t, reg);
            // Back to 18: too many → the lowest finishers drop; too few → the best local runners-up come up.
            while (reg.Clubs.Count > RegionalSize)
            {
                var t = reg.Clubs.Where(x => !champs.Contains(x) && !plan.T3Down.Contains(x)).OrderBy(x => tables[reg.Key].IndexOf(x) < 0 ? -1 : tables[reg.Key].IndexOf(x)).Last();
                Move(t, myLocals.FirstOrDefault(c => c.Key == LocalKey(AreaOf(t))) ?? myLocals[0]);
            }
            while (reg.Clubs.Count < RegionalSize)
            {
                var next = myLocals.SelectMany(c => tables[c.Key].Where(t => c.Clubs.Contains(t)).Select((t, i) => (t, c, i))).OrderBy(x => x.i).FirstOrDefault();
                if (next.t == null) break;
                Move(next.t, reg);
            }
        }
        // Clubs that moved get budgets for their new league (decision #88).
        var before = tables.SelectMany(kv => kv.Value.Select(t => (t, kv.Key))).GroupBy(x => x.t).ToDictionary(g => g.Key, g => g.First().Key);
        double nablArena = nabl.Teams.Average(t => (double)t.ArenaCapacity);
        double ScaleOf(string key) => Competitions.Get(key).SalaryScale;
        foreach (var t in nabl.Teams.Where(t => before.ContainsKey(t))) Season.Budgets.Rescale(t, 1, nablArena, ScaleOf(before[t]));
        foreach (var c in world.Competitions.Where(c => IsPyramid(c.Key)))
        {
            double arena = c.Clubs.Average(t => (double)t.ArenaCapacity);
            foreach (var t in c.Clubs)
            {
                if (nabl.Teams.Any(x => x.Id == t.Id)) continue;
                bool down = false;
                if (!before.TryGetValue(t, out var was)) { Season.Budgets.Rescale(t, c.Profile.SalaryScale, arena, 1); down = true; } // down from the NABL
                else if (was != c.Key) { Season.Budgets.Rescale(t, c.Profile.SalaryScale, arena, ScaleOf(was)); down = Level(c.Key) > Level(was); }
                if (down) news.AddRange(RelegationReleases(world, nabl, t, c, on));
            }
        }
        // New divisions for next season, with the new arrivals among their neighbours.
        AssignDivisions(world);
        world.Index();
        return news;
    }

    /// <summary>
    /// Relegation release clauses (critic review #2: a relegated club's long contracts outlived its parachute): young
    /// players far too good for the new level may walk away free, which also takes their wages off the books.
    /// </summary>
    private static IEnumerable<(string, string)> RelegationReleases(GameWorld world, League nabl, Team t, WorldCompetition c, DateOnly date)
    {
        if (!world.Levels.TryGetValue(c.Key, out var level)) level = c.Clubs.SelectMany(x => x.Roster).Select(p => (double)p.CurrentAbility).DefaultIfEmpty(80).Average();
        var rng = new Rng((ulong)(t.Id * 7919 + date.Year));
        var gone = t.Roster.Where(p => p.CurrentAbility > level + 22 && p.Age(date) < 31 && rng.Chance(0.6)).ToList();
        foreach (var p in gone)
        {
            t.Roster.Remove(p); p.TeamId = null; p.Contract = null;
            nabl.FreeAgents.Add(p);
        }
        if (gone.Count > 0)
            yield return ($"{t.FullName}: relegation clauses", $"{string.Join(", ", gone.Select(p => p.Name))} used the relegation release in {(gone.Count == 1 ? "his contract" : "their contracts")} and left on a free. Their wages are off the books.");
    }
}
