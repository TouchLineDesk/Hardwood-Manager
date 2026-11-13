using Hardwood.Core.Model;

namespace Hardwood.Core.World;

/// <summary>
/// The USA pyramid below the NABL (decisions #73, #78-79, #91-94): two national tiers of 24, six regional leagues of 18
/// and sixteen local leagues of about 14, roughly 380 clubs, all linked by promotion and relegation. Every club has a
/// home area (from its city), so a club dropping out of a national tier lands in its own region, and a regional club
/// in its own local league.
/// </summary>
public static class UsaPyramid
{
    public const string Tier2 = "usa2", Tier3 = "usa3";
    /// <summary>The pro tiers (the top league, The Championship, National League One) have 32 clubs each since Danny's D33
    /// design (#344, D36 A): two conferences of four divisions of four (24 in tiers 2-3 before).</summary>
    public const int NationalSize = 32, ProSize = 32, RegionalSize = 18;
    /// <summary>Clubs moving between the pro tiers each summer: the 4th-placed team in each of the eight divisions down, the
    /// eight division winners below up (#344; six before, #319; three before that, #79, #91).</summary>
    public const int NationalSwaps = 8;

    /// <summary>The pro tiers' division names by conference, in quadrant order: north-east, south-east, north-west, south-west
    /// of the conference (#355). The NBA's six real names are kept where they fit; "South" and "Midwest" are the two new ones.</summary>
    public static readonly string[] EastDivisions = { "Atlantic", "Southeast", "Central", "South" };
    public static readonly string[] WestDivisions = { "Midwest", "Southwest", "Northwest", "Pacific" };

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
        CityArea.TryGetValue(t.City, out var a) ? a : TopCityArea.TryGetValue(t.City, out var c) ? c
        : DivisionHome.TryGetValue(t.Division, out var d) ? d : "prl";

    /// <summary>Top-league "cities" the area lists don't name (state or region names, and Toronto), so a relegated club still
    /// plays in its own region's division (#318).</summary>
    private static readonly Dictionary<string, string> TopCityArea = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Toronto"] = "nyl", ["Brooklyn"] = "nyl", ["Indiana"] = "prl", ["Minnesota"] = "pln", ["Utah"] = "rky", ["Golden State"] = "cal",
    };

    public static string RegionOf(Team t) => Areas.First(a => a.Key == AreaOf(t)).Region;

    /// <summary>Rough centre (latitude, longitude) of each local area, to group clubs that are close together (#318).</summary>
    private static readonly Dictionary<string, (double Lat, double Lon)> AreaCentre = new()
    {
        ["nen"] = (42.4, -71.5), ["nyl"] = (42.0, -75.5), ["mid"] = (40.0, -77.0), ["car"] = (35.5, -79.0), ["dix"] = (34.0, -86.0),
        ["fla"] = (28.0, -81.7), ["gle"] = (42.0, -83.5), ["prl"] = (42.0, -88.0), ["pln"] = (41.5, -95.0), ["txn"] = (32.8, -97.0),
        ["gul"] = (30.0, -95.5), ["okr"] = (35.5, -96.5), ["rky"] = (40.5, -108.0), ["dsw"] = (34.0, -112.0), ["cal"] = (36.0, -120.0),
        ["pnw"] = (46.0, -122.5),
    };

    /// <summary>Where the bigger cities are (latitude, longitude), so pro divisions group clubs that are really close (#355).
    /// Anything not listed uses its local area's centre.</summary>
    private static readonly Dictionary<string, (double Lat, double Lon)> CityAt = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Boston"] = (42.36, -71.06), ["New York"] = (40.71, -74.01), ["Brooklyn"] = (40.68, -73.94), ["Philadelphia"] = (39.95, -75.17),
        ["Toronto"] = (43.65, -79.38), ["Washington"] = (38.91, -77.04), ["Charlotte"] = (35.23, -80.84), ["Miami"] = (25.76, -80.19),
        ["Orlando"] = (28.54, -81.38), ["Atlanta"] = (33.75, -84.39), ["Cleveland"] = (41.50, -81.69), ["Detroit"] = (42.33, -83.05),
        ["Indiana"] = (39.77, -86.16), ["Indianapolis"] = (39.77, -86.16), ["Chicago"] = (41.88, -87.63), ["Milwaukee"] = (43.04, -87.91),
        ["Memphis"] = (35.15, -90.05), ["New Orleans"] = (29.95, -90.07), ["Minnesota"] = (44.98, -93.27), ["Minneapolis"] = (44.98, -93.27),
        ["Dallas"] = (32.78, -96.80), ["Houston"] = (29.76, -95.37), ["San Antonio"] = (29.42, -98.49), ["Oklahoma City"] = (35.47, -97.52),
        ["Denver"] = (39.74, -104.99), ["Utah"] = (40.76, -111.89), ["Salt Lake City"] = (40.76, -111.89), ["Phoenix"] = (33.45, -112.07),
        ["Los Angeles"] = (34.05, -118.24), ["Golden State"] = (37.80, -122.27), ["San Francisco"] = (37.77, -122.42), ["Oakland"] = (37.80, -122.27),
        ["Sacramento"] = (38.58, -121.49), ["Portland"] = (45.52, -122.68), ["Seattle"] = (47.61, -122.33),
        ["Providence"] = (41.82, -71.41), ["Hartford"] = (41.77, -72.67), ["Buffalo"] = (42.89, -78.88), ["Rochester"] = (43.16, -77.61),
        ["Pittsburgh"] = (40.44, -79.99), ["Baltimore"] = (39.29, -76.61), ["Newark"] = (40.74, -74.17), ["Long Island"] = (40.79, -73.13),
        ["Raleigh"] = (35.78, -78.64), ["Richmond"] = (37.54, -77.44), ["Norfolk"] = (36.85, -76.29), ["Nashville"] = (36.16, -86.78),
        ["Birmingham"] = (33.52, -86.80), ["Tampa"] = (27.95, -82.46), ["Jacksonville"] = (30.33, -81.66), ["Fort Lauderdale"] = (26.12, -80.14),
        ["Columbus"] = (39.96, -83.00), ["Cincinnati"] = (39.10, -84.51), ["St. Louis"] = (38.63, -90.20), ["Kansas City"] = (39.10, -94.58),
        ["Omaha"] = (41.26, -95.93), ["Fort Worth"] = (32.76, -97.33), ["Austin"] = (30.27, -97.74), ["El Paso"] = (31.76, -106.49),
        ["Tulsa"] = (36.15, -95.99), ["Little Rock"] = (34.75, -92.29), ["Colorado Springs"] = (38.83, -104.82), ["Boise"] = (43.62, -116.20),
        ["Las Vegas"] = (36.17, -115.14), ["Tucson"] = (32.22, -110.97), ["Albuquerque"] = (35.08, -106.65), ["San Diego"] = (32.72, -117.16),
        ["San Jose"] = (37.34, -121.89), ["Long Beach"] = (33.77, -118.19), ["Fresno"] = (36.74, -119.79), ["Spokane"] = (47.66, -117.43),
        ["Tacoma"] = (47.25, -122.44), ["Honolulu"] = (21.31, -157.86), ["Anchorage"] = (61.22, -149.90), ["Vancouver"] = (45.64, -122.66),
        ["Arlington"] = (32.74, -97.11), ["Plano"] = (33.02, -96.70), ["Lubbock"] = (33.58, -101.86), ["Baton Rouge"] = (30.45, -91.19),
        ["Grand Rapids"] = (42.96, -85.67), ["Toledo"] = (41.65, -83.54), ["Madison"] = (43.07, -89.40), ["Fort Wayne"] = (41.08, -85.14),
        ["Worcester"] = (42.26, -71.80), ["Syracuse"] = (43.05, -76.15), ["Albany"] = (42.65, -73.76), ["Greensboro"] = (36.07, -79.79),
        ["Durham"] = (35.99, -78.90), ["Knoxville"] = (35.96, -83.92), ["Chattanooga"] = (35.05, -85.31), ["St. Petersburg"] = (27.77, -82.64),
        ["Akron"] = (41.08, -81.52), ["Dayton"] = (39.76, -84.19), ["St. Paul"] = (44.95, -93.09), ["Des Moines"] = (41.59, -93.62),
        ["Wichita"] = (37.69, -97.34), ["Corpus Christi"] = (27.80, -97.40), ["Norman"] = (35.22, -97.44), ["Aurora"] = (39.73, -104.83),
        ["Mesa"] = (33.42, -111.83), ["Henderson"] = (36.04, -114.98), ["Anaheim"] = (33.84, -117.91), ["Riverside"] = (33.95, -117.40),
    };

    /// <summary>A club's location: its city if listed, else its local area's centre.</summary>
    public static (double Lat, double Lon) Location(Team t) =>
        CityAt.TryGetValue(t.City, out var c) ? c : AreaCentre.TryGetValue(AreaOf(t), out var a) ? a : (40, -90);

    /// <summary>
    /// One conference's four divisions (Danny's D33 design, #344, #355): the conference's eastern half split north and south,
    /// then its western half, named by quadrant (<see cref="EastDivisions"/>, <see cref="WestDivisions"/>). Eight clubs a half,
    /// four a division, when the conference has sixteen.
    /// </summary>
    public static List<(string Name, List<Team> Teams)> ConferenceDivisions(Conference conf, IEnumerable<Team> clubs)
    {
        var names = conf == Conference.East ? EastDivisions : WestDivisions;
        var byLon = clubs.OrderByDescending(t => Location(t).Lon).ThenBy(t => t.Id).ToList();
        int eastCount = (byLon.Count + 1) / 2;
        var result = new List<(string, List<Team>)>();
        int q = 0;
        foreach (var half in new[] { byLon.Take(eastCount).ToList(), byLon.Skip(eastCount).ToList() })
        {
            var ns = half.OrderByDescending(t => Location(t).Lat).ThenBy(t => t.Id).ToList();
            int north = (ns.Count + 1) / 2;
            result.Add((names[q++], ns.Take(north).OrderBy(t => t.Id).ToList()));
            result.Add((names[q++], ns.Skip(north).OrderBy(t => t.Id).ToList()));
        }
        return result.Where(d => d.Item2.Count > 0).ToList();
    }

    /// <summary>A national tier's two conferences: the eastern half of its clubs by longitude, then the western half.</summary>
    public static (List<Team> East, List<Team> West) ProConferences(IReadOnlyList<Team> clubs)
    {
        var byLon = clubs.OrderByDescending(t => Location(t).Lon).ThenBy(t => t.Id).ToList();
        int east = (byLon.Count + 1) / 2;
        return (byLon.Take(east).ToList(), byLon.Skip(east).ToList());
    }

    /// <summary>
    /// Lay out the top league in two conferences of four regional divisions (#344, #355), writing each club's conference and
    /// division. Clubs keep their conference; <paramref name="newcomers"/> join the conference nearer to them; then the
    /// conferences are evened out (the club nearest the other conference crosses, newcomers first). Only for a league with a
    /// pyramid below it at the pro size; anything else keeps its divisions.
    /// </summary>
    public static void RealignTop(League top, IEnumerable<Team> newcomers)
    {
        if (top.Teams.Count != ProSize) return;
        var fresh = newcomers.ToHashSet();
        var east = top.Teams.Where(t => !fresh.Contains(t) && t.Conference == Conference.East).ToList();
        var west = top.Teams.Where(t => !fresh.Contains(t) && t.Conference == Conference.West).ToList();
        double Mean(List<Team> l, double fallback) => l.Count == 0 ? fallback : l.Average(t => Location(t).Lon);
        foreach (var t in fresh.Where(top.Teams.Contains).OrderBy(t => t.Id))
        {
            double lon = Location(t).Lon;
            (Math.Abs(lon - Mean(east, -80)) <= Math.Abs(lon - Mean(west, -105)) ? east : west).Add(t);
        }
        int eastSize = ProSize / 2;
        while (east.Count > eastSize)
        {
            var t = east.OrderBy(x => fresh.Contains(x) ? 0 : 1).ThenBy(x => Location(x).Lon).ThenBy(x => x.Id).First();
            east.Remove(t); west.Add(t);
        }
        while (west.Count > ProSize - eastSize)
        {
            var t = west.OrderBy(x => fresh.Contains(x) ? 0 : 1).ThenByDescending(x => Location(x).Lon).ThenBy(x => x.Id).First();
            west.Remove(t); east.Add(t);
        }
        foreach (var (conf, list) in new[] { (Conference.East, east), (Conference.West, west) })
        foreach (var (name, teams) in ConferenceDivisions(conf, list))
        foreach (var t in teams) { t.Conference = conf; t.Division = name; }
    }

    /// <summary>
    /// A pyramid league's divisions (Danny, #318), worked out from its clubs each time (no save change), East first:
    /// tiers 2 and 3 in six divisions of equal size, made of clubs close together (the eastern half split north to south in
    /// three, then the western half), each named after the local area most of its clubs come from; a regional league in one
    /// division per local area; a local league is one area, so one division. The six national divisions line up with the
    /// top league's (Atlantic, Central, Southeast; Northwest, Pacific, Southwest): a division winner takes the vacancy there.
    /// </summary>
    public static List<(string Name, List<Team> Teams)> Divisions(string key, IReadOnlyList<Team> clubs)
    {
        int level = Level(key);
        if (level is 2 or 3)
        {
            // Two conferences of four regional divisions (Danny's D33 design, #344, #355), East first.
            var (east, west) = ProConferences(clubs);
            return ConferenceDivisions(Conference.East, east).Select(d => ($"East · {d.Name}", d.Teams))
                .Concat(ConferenceDivisions(Conference.West, west).Select(d => ($"West · {d.Name}", d.Teams))).ToList();
        }
        if (level == 4)
        {
            var region = key["usa-r-".Length..];
            return Areas.Where(a => a.Region == region)
                .Select(a => (a.Name.Replace(" League", ""), clubs.Where(t => AreaOf(t) == a.Key).OrderBy(t => t.Id).ToList()))
                .Where(d => d.Item2.Count > 0).ToList();
        }
        var name1 = Profiles.FirstOrDefault(p => p.Key == key)?.Name ?? key;
        return new() { (name1, clubs.ToList()) };
    }

    /// <summary>Won its division in tier 2 or 3 this season (so it goes up, #319).</summary>
    public static bool WonDivision(WorldCompetition c, Team t)
    {
        if (Level(c.Key) is not (2 or 3) || !c.Season.Fixtures.Any(f => f.Played)) return false;
        var table = c.Season.Table().Select(r => r.TeamId).ToList();
        var div = Divisions(c.Key, c.Clubs).FirstOrDefault(d => d.Teams.Contains(t)).Teams;
        return div != null && table.FirstOrDefault(id => div.Any(x => x.Id == id)) == t.Id;
    }

    /// <summary>The division a pyramid club plays in this season ("" outside the pyramid).</summary>
    public static string DivisionOf(string key, IReadOnlyList<Team> clubs, Team t) =>
        IsPyramid(key) ? Divisions(key, clubs).FirstOrDefault(d => d.Teams.Contains(t)).Name ?? "" : "";

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
        Start = (10, 21), RoundDays = new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Saturday }, Clubs = clubs.ToArray(), // 62 games in 32-club tiers (#355)
        // The division format's playoffs (D36, #364): 16 teams by conference, then the final; third place plays its own tournament.
        DivisionPlayoffs = true,
        Playoffs = new[] { new PlayoffRound("First round", 5, 16), new PlayoffRound("Conference semi-finals", 5, 8), new PlayoffRound("Conference finals", 7, 4), new PlayoffRound("Final", 7, 2) },
    };

    /// <summary>
    /// The division format's playoff field for a tier-2/3 league (D36): each conference's eight seeds (the top two in each of its
    /// four divisions, best record first) and the eight third-placed teams (best record first). Null when the league isn't laid
    /// out in two conferences of four divisions of four (an older save's 24 before its summer).
    /// </summary>
    public static (List<int> East, List<int> West, List<int> Thirds)? DivisionField(string key, IReadOnlyList<Team> clubs, List<TableRow> table)
    {
        var divs = Divisions(key, clubs);
        if (divs.Count != 8 || divs.Any(d => d.Teams.Count != 4)) return null;
        var order = table.Select((r, i) => (r.TeamId, i)).ToDictionary(x => x.TeamId, x => x.i);
        int Rank(Team t) => order.TryGetValue(t.Id, out var i) ? i : int.MaxValue;
        var east = new List<int>(); var west = new List<int>(); var thirds = new List<int>();
        foreach (var (name, teams) in divs)
        {
            var ranked = teams.OrderBy(Rank).ThenBy(t => t.Id).ToList();
            (name.StartsWith("East") ? east : west).AddRange(ranked.Take(2).Select(t => t.Id));
            thirds.Add(ranked[2].Id);
        }
        List<int> Sort(List<int> ids) => ids.OrderBy(id => order.GetValueOrDefault(id, int.MaxValue)).ToList();
        return (Sort(east), Sort(west), Sort(thirds));
    }

    private static CompetitionProfile Lower(string key, string name, string shortName, int strength, double wages, int imports, double prestige, int roundsPerWeek, IEnumerable<ClubSeed> clubs) => new()
    {
        Key = key, Name = name, Short = shortName, Country = "USA", LocalNationalities = new[] { "USA" },
        StrengthOffset = strength, ForeignLimit = imports, SalaryScale = wages, JobPrestige = prestige, Rules = Rules.Nba, MinAge = 23,
        Start = (10, 24), RoundDays = roundsPerWeek == 2 ? new[] { DayOfWeek.Tuesday, DayOfWeek.Saturday } : new[] { DayOfWeek.Saturday },
        Clubs = clubs.ToArray(),
    };

    // ------------------------------------------------------------------------------------------
    // Promotion and relegation (each summer, before the next season's fixtures are made).

    /// <summary>
    /// Move clubs between levels using the finished tables (Danny's D33 design, #344, #355): the 4th-placed (last) team in each
    /// top-league division ↔ the eight tier-2 division winners; the same between tiers 2 and 3; the last in each tier-3 division
    /// home to its region ↔ the six regional champions and the two best other regional division winners; each regional
    /// league's last in each area down to its local league ↔ each local champion up. Then every pro tier is brought to 32
    /// (an older save's 30 and 24 grow at its first summer on this version) and each regional league back to 18.
    /// Returns headlines. Does nothing if the world has no pyramid (an older career).
    /// </summary>
    public static List<(string Title, string Body)> Promote(GameWorld world, League nabl, Generation.StaffGenerator staffGen, DateOnly on)
    {
        var news = new List<(string, string)>();
        var t2 = world.Competitions.FirstOrDefault(c => c.Key == Tier2);
        var t3 = world.Competitions.FirstOrDefault(c => c.Key == Tier3);
        if (t2 == null || t3 == null) return news;
        // Final tables, all taken before anyone moves.
        List<Team> Table(WorldCompetition c) =>
            c.Season.Fixtures.Any(f => f.Played)
                ? c.Season.Table().Select(r => world.TeamById(r.TeamId)).Where(t => t != null && c.Clubs.Contains(t)).Cast<Team>()
                    .Concat(c.Clubs.Where(t => !c.Season.Participants.Contains(t.Id))).ToList()
                : c.Clubs.ToList();
        var regionals = Regions.Select(r => world.Competitions.FirstOrDefault(c => c.Key == RegionalKey(r.Key))).Where(c => c != null).Cast<WorldCompetition>().ToList();
        var locals = Areas.Select(a => world.Competitions.FirstOrDefault(c => c.Key == LocalKey(a.Key))).Where(c => c != null).Cast<WorldCompetition>().ToList();
        var tables = world.Competitions.Where(c => IsPyramid(c.Key)).ToDictionary(c => c.Key, Table);
        var pct = world.Competitions.Where(c => IsPyramid(c.Key) && c.Season.Fixtures.Any(f => f.Played))
            .SelectMany(c => c.Season.Table().Select(r => (r.TeamId, r.Pct))).GroupBy(x => x.TeamId).ToDictionary(g => g.Key, g => g.First().Pct);
        double Pct(Team t) => pct.TryGetValue(t.Id, out var v) ? v : 0;

        void Move(Team t, WorldCompetition from, WorldCompetition to) { from.Clubs.Remove(t); to.Clubs.Add(t); }
        string Names(IEnumerable<Team> ts) => string.Join(", ", ts.Select(t => t.FullName));

        // Divisions as they stood during the season (Danny, #318-#319, #344): the last team in each division goes down, each
        // division winner goes up (none up from the top league).
        List<Team> InTableOrder(string key, List<Team> div) => tables[key].Where(div.Contains).ToList();
        List<(string Name, List<Team> Teams)> DivisionsNow(WorldCompetition c) =>
            Divisions(c.Key, c.Clubs).Select(d => (d.Name, InTableOrder(c.Key, d.Teams))).Where(d => d.Item2.Count > 0).ToList();
        var t2Divs = DivisionsNow(t2);
        var t3Divs = DivisionsNow(t3);

        var up = new List<Team>(); var down = new List<Team>();
        void JoinTop(Team p, WorldCompetition from)
        {
            from.Clubs.Remove(p); nabl.Teams.Add(p); up.Add(p);
            p.Tactic.Pace = new Tactic().Pace;
            if (p.Staff.Count == 0)
                foreach (var role in Enum.GetValues<StaffRole>()) { var st = staffGen.Generate(nabl, role, on, 8); st.TeamId = p.Id; p.Staff.Add(st); }
        }
        // The top league ↔ tier 2: the last team in each division ↔ every tier-2 division winner; the newcomers are then laid
        // out with everyone else in the regional divisions (RealignTop).
        var last = nabl.History.LastOrDefault();
        bool topPlayed = last != null && last.Records.Count > 0;
        if (topPlayed)
        {
            var wins = last!.Records.ToDictionary(r => r.teamId, r => (r.wins, r.losses));
            int W(Team t) => wins.TryGetValue(t.Id, out var w) ? w.wins : 41;
            int L(Team t) => wins.TryGetValue(t.Id, out var w) ? w.losses : 41;
            foreach (var div in nabl.Teams.GroupBy(t => (t.Conference, t.Division)).OrderBy(g => g.Key.Conference).ThenBy(g => g.Key.Division).ToList())
                down.Add(div.OrderBy(W).ThenByDescending(L).ThenBy(t => t.Id).First());
            foreach (var r in down) { nabl.Teams.Remove(r); t2.Clubs.Add(r); r.Division = t2.Profile.Short; }
            foreach (var d in t2Divs) JoinTop(d.Teams[0], t2);
        }
        // Tier 2 ↔ tier 3: the last in each tier-2 division ↔ the tier-3 division winners.
        var t2Down = t2Divs.Select(d => d.Teams.Last()).Where(t => t2.Clubs.Contains(t) && !up.Contains(t) && !down.Contains(t)).ToList();
        var t3Up = t3Divs.Select(d => d.Teams[0]).ToList();
        foreach (var t in t2Down) Move(t, t2, t3);
        foreach (var t in t3Up) Move(t, t3, t2);
        // Tier 3 ↔ regional leagues: the last in each tier-3 division goes home to its region; each regional champion goes up, and
        // the best other regional division winners fill the rest of the places (eight a year, #344).
        var t3Down = t3Divs.Select(d => d.Teams.Last()).Where(t => t3.Clubs.Contains(t) && !t3Up.Contains(t) && !t2Down.Contains(t)).ToList();
        var regionalChamps = regionals.Select(c => tables[c.Key].FirstOrDefault()).Where(t => t != null).Cast<Team>().ToList();
        var otherWinners = regionals.SelectMany(c => Divisions(c.Key, tables[c.Key]).Select(d => InTableOrder(c.Key, d.Teams).FirstOrDefault()))
            .Where(t => t != null && !regionalChamps.Contains(t)).Cast<Team>()
            .OrderByDescending(Pct).ThenBy(t => t.Id).Take(Math.Max(0, t3Divs.Count - regionalChamps.Count)).ToList();
        var regionalUp = regionalChamps.Concat(otherWinners).ToList();
        foreach (var t in t3Down) Move(t, t3, regionals.First(c => c.Key == RegionalKey(RegionOf(t))));
        foreach (var t in regionalUp) Move(t, regionals.First(c => c.Clubs.Contains(t)), t3);

        // Every pro tier at 32 (#344): an older save's top league of 30 and tiers of 24 grow here, at its first summer on this
        // version, with the next best finishers from the level below (none of the clubs just relegated).
        var fillTop = new List<Team>(); var fill2 = new List<Team>(); var fill3 = new List<Team>();
        if (topPlayed)
            foreach (var t in tables[Tier2].Where(t => t2.Clubs.Contains(t) && !down.Contains(t)).ToList())
            {
                if (nabl.Teams.Count >= ProSize) break;
                JoinTop(t, t2); fillTop.Add(t);
            }
        // (When tier 3 itself is short, as in the first conversion, the clubs just up from the regions are next.)
        foreach (var t in tables[Tier3].Where(t => t3.Clubs.Contains(t) && !t2Down.Contains(t))
                     .Concat(t3.Clubs.Where(t => !tables[Tier3].Contains(t) && !t2Down.Contains(t)).OrderByDescending(Pct).ThenBy(t => t.Id)).ToList())
        {
            if (t2.Clubs.Count >= NationalSize) break;
            Move(t, t3, t2); fill2.Add(t);
        }
        foreach (var t in regionals.SelectMany(c => tables[c.Key].Where(c.Clubs.Contains).Select((t, i) => (t, i, c)))
                     .Where(x => !t3Down.Contains(x.t)).OrderByDescending(x => Pct(x.t)).ThenBy(x => x.i).ThenBy(x => x.t.Id).ToList())
        {
            if (t3.Clubs.Count >= NationalSize) break;
            Move(t.t, t.c, t3); fill3.Add(t.t);
        }
        if (topPlayed)
        {
            RealignTop(nabl, up);
            news.Add(($"Promoted to the {Hardwood.Core.Names.Brand.Top}: {Names(up)}", $"{Names(up.Except(fillTop))} won their divisions in {t2.Profile.Name} and move up to the {Hardwood.Core.Names.Brand.Top}."
                + (fillTop.Count > 0 ? $" The league grows to {ProSize} teams: {Names(fillTop)} also come up." : "")));
            news.Add(($"Relegated from the {Hardwood.Core.Names.Brand.Top}: {Names(down)}", $"{Names(down)} finished last in their divisions and drop into {t2.Profile.Name}."));
        }
        news.Add(($"{t2.Profile.Name}: up and down", $"Division winners promoted from {t3.Profile.Name}: {Names(t3Up)}. Last in their divisions, relegated to {t3.Profile.Name}: {Names(t2Down)}."
            + (fill2.Count > 0 ? $" {t2.Profile.Name} grows to {NationalSize} clubs: {Names(fill2)} also come up." : "")));
        news.Add(($"{t3.Profile.Name}: up and down", $"Promoted: the regional champions {Names(regionalChamps)}" + (otherWinners.Count > 0 ? $", and the best other division winners, {Names(otherWinners)}" : "")
            + $". Last in their divisions, relegated to their regions: {Names(t3Down)}."
            + (fill3.Count > 0 ? $" {t3.Profile.Name} grows to {NationalSize} clubs: {Names(fill3)} also come up." : "")));
        var t3Gone = regionalUp.Concat(fill3).ToHashSet();
        // Regional ↔ local leagues: a regional league's divisions are its local areas. The last in each goes down to its local
        // league; each local champion comes up.
        foreach (var reg in regionals)
        {
            var area = reg.Key["usa-r-".Length..];
            var myLocals = locals.Where(c => Areas.First(a => LocalKey(a.Key) == c.Key).Region == area).ToList();
            var champs = myLocals.Select(c => tables[c.Key].FirstOrDefault()).Where(t => t != null).Cast<Team>().ToList();
            var regDivs = Divisions(reg.Key, reg.Clubs.Where(t => tables[reg.Key].Contains(t)).ToList())
                .Select(d => InTableOrder(reg.Key, d.Teams)).Where(d => d.Count > 0).ToList();
            var down4 = regDivs.Select(d => d.Last()).Where(t => reg.Clubs.Contains(t) && !t3Gone.Contains(t) && !t3Down.Contains(t)).ToList();
            foreach (var t in down4) Move(t, reg, myLocals.FirstOrDefault(c => c.Key == LocalKey(AreaOf(t))) ?? myLocals[0]);
            foreach (var t in champs) Move(t, myLocals.First(c => c.Clubs.Contains(t)), reg);
        // Back to 18: too many → the lowest finishers drop; too few → the best local runners-up come up.
            while (reg.Clubs.Count > RegionalSize)
            {
                var t = reg.Clubs.Where(x => !champs.Contains(x) && !t3Down.Contains(x)).OrderBy(x => tables[reg.Key].IndexOf(x) < 0 ? -1 : tables[reg.Key].IndexOf(x)).Last();
                Move(t, reg, myLocals.FirstOrDefault(c => c.Key == LocalKey(AreaOf(t))) ?? myLocals[0]);
            }
            while (reg.Clubs.Count < RegionalSize)
            {
                var next = myLocals.SelectMany(c => tables[c.Key].Where(t => c.Clubs.Contains(t)).Select((t, i) => (t, c, i))).OrderBy(x => x.i).FirstOrDefault();
                if (next.t == null) break;
                Move(next.t, next.c, reg);
            }
        }
        // Clubs that moved get budgets for their new league (decision #88).
        var before = tables.SelectMany(kv => kv.Value.Select(t => (t, kv.Key))).GroupBy(x => x.t).ToDictionary(g => g.Key, g => g.First().Key);
        double nablArena = nabl.Teams.Average(t => (double)t.ArenaCapacity);
        double ScaleOf(string key) => Competitions.Get(key).SalaryScale;
        foreach (var t in nabl.Teams.Where(t => before.ContainsKey(t)))
        {
            Season.Budgets.Rescale(t, 1, nablArena, ScaleOf(before[t]));
            // Promotion clauses (#357), the mirror of the relegation ones: wages double on reaching the top league (tier-2 deals
            // are a tenth of its scale), so a promoted club's payroll is a top-league one, not a tier-2 one in a big budget.
            if (t.Id != world.ManagedClubId)
                foreach (var p in t.Roster)
                    if (p.Contract is { } c) c.Salary = (long)Math.Round(c.Salary * Math.Min(2.0, 1 / Math.Max(0.01, ScaleOf(before[t]))) / 1000) * 1000;
        }
        foreach (var c in world.Competitions.Where(c => IsPyramid(c.Key)))
        {
            double arena = c.Clubs.Average(t => (double)t.ArenaCapacity);
            foreach (var t in c.Clubs)
            {
                t.Division = c.Profile.Short;
                if (nabl.Teams.Any(x => x.Id == t.Id)) continue;
                bool fell = false;
                if (!before.TryGetValue(t, out var was)) { Season.Budgets.Rescale(t, c.Profile.SalaryScale, arena, 1); fell = true; } // down from the NABL
                else if (was != c.Key) { Season.Budgets.Rescale(t, c.Profile.SalaryScale, arena, ScaleOf(was)); fell = Level(c.Key) > Level(was); }
                if (fell) news.AddRange(RelegationReleases(world, nabl, t, c, on));
            }
        }
        news.AddRange(RebuildPromoted(world, nabl, up, on));
        world.Index();
        return news;
    }

    /// <summary>
    /// Promoted clubs rebuild for the top league (#356). Promotion comes after the top league's free agency, so each club
    /// (not the coach's own) lets go of up to three players well below a typical top-league 10th man (keeping ten), then signs
    /// rotation-level free agents (about a typical 10th man) it can fit in its new wage budget at top-league wages up to the roster minimum, and fills
    /// any gap left with minimum deals. Eight tier-2 rosters a year otherwise left the worst records too low.
    /// </summary>
    private static List<(string, string)> RebuildPromoted(GameWorld world, League top, List<Team> up, DateOnly on)
    {
        var news = new List<(string, string)>();
        var stayed = top.Teams.Except(up).ToList();
        if (up.Count == 0 || stayed.Count == 0) return news;
        var fin = top.Finance;
        var tenth = stayed.Select(t => t.Roster.Select(p => p.CurrentAbility).OrderByDescending(x => x).Skip(9).DefaultIfEmpty(0).First()).OrderBy(x => x).ToList();
        int bar = tenth[tenth.Count / 2] - 4;
        var rng = new Rng((ulong)(on.Year * 7919 + up.Count));
        foreach (var t in up.Where(t => t.Id != world.ManagedClubId))
        {
            var gone = t.Roster.Where(p => p.CurrentAbility < bar).OrderBy(p => p.CurrentAbility).Take(Math.Min(3, Math.Max(0, t.Roster.Count - 10))).ToList();
            foreach (var p in gone) { t.Roster.Remove(p); p.TeamId = null; p.Contract = null; top.FreeAgents.Add(p); }
            var signed = new List<Player>();
            void Sign(Player p, long salary)
            {
                top.FreeAgents.Remove(p);
                p.Contract = new Contract { Years = Season.ContractMarket.ContractLength(p, on, rng), Salary = salary, StartSeason = on.Year };
                p.TeamId = t.Id; p.DraftRightsTeamId = null;
                t.Roster.Add(p); signed.Add(p);
            }
            foreach (var p in top.FreeAgents.Where(p => !gone.Contains(p) && p.DraftRightsTeamId == null && p.CurrentAbility >= bar && p.Age(on) <= 33)
                         .OrderBy(p => Math.Abs(p.CurrentAbility - (bar + 6))).ThenBy(p => p.Id).ToList()) // rotation players, not the pool's stars
            {
                if (t.Roster.Count >= fin.RosterMin) break;
                long salary = Math.Max(fin.MinSalary, Season.ContractMarket.MarketSalary(p.CurrentAbility, p.Age(on), fin));
                if (Season.ContractMarket.Payroll(t) + salary <= t.Money.WageBudget) Sign(p, salary);
            }
            foreach (var p in top.FreeAgents.Where(p => !gone.Contains(p) && p.DraftRightsTeamId == null).OrderByDescending(p => p.CurrentAbility).ToList())
            {
                if (t.Roster.Count >= fin.RosterMin) break;
                Sign(p, fin.MinSalary);
            }
            if (gone.Count + signed.Count > 0)
                news.Add(($"{t.FullName} rebuild for the {Hardwood.Core.Names.Brand.Top}",
                    (gone.Count > 0 ? $"{t.FullName} let {string.Join(", ", gone.Select(p => p.Name))} go. " : "")
                    + (signed.Count > 0 ? $"In: {string.Join(", ", signed.Select(p => $"{p.Name} ({p.PrimaryPosition})"))}." : "")));
        }
        return news;
    }

    /// <summary>
    /// A new world's top league grows to 32 (Danny's D33 design, D36a A, #355): the strongest tier-2 clubs join it, the strongest
    /// tier-3 clubs fill tier 2, the strongest regional clubs tier 3, and each regional league refills from its local leagues.
    /// Then the top league is laid out in its eight regional divisions. Nothing happens without a pyramid.
    /// </summary>
    public static void ExpandTop(GameWorld world, League top, Generation.StaffGenerator staffGen, DateOnly on)
    {
        var t2 = world.Competitions.FirstOrDefault(c => c.Key == Tier2);
        var t3 = world.Competitions.FirstOrDefault(c => c.Key == Tier3);
        if (t2 == null || t3 == null || top.Teams.Count >= ProSize) return;
        double S(Team t) => GameWorld.Strength(t);
        var newcomers = new List<Team>();
        foreach (var p in t2.Clubs.OrderByDescending(S).ThenBy(t => t.Id).Take(ProSize - top.Teams.Count).ToList())
        {
            t2.Clubs.Remove(p); top.Teams.Add(p); newcomers.Add(p);
            p.Tactic.Pace = new Tactic().Pace;
            // They start the career in the top league, so their deals are top-league ones (at least the minimum).
            foreach (var pl in p.Roster)
                if (pl.Contract is { } c) c.Salary = Math.Max(top.Finance.MinSalary, Season.ContractMarket.MarketSalary(pl.CurrentAbility, pl.Age(on), top.Finance));
            if (p.Staff.Count == 0)
                foreach (var role in Enum.GetValues<StaffRole>()) { var st = staffGen.Generate(top, role, on, 8); st.TeamId = p.Id; p.Staff.Add(st); }
        }
        void Fill(WorldCompetition to, IEnumerable<(Team t, WorldCompetition from)> pool)
        {
            foreach (var (t, from) in pool.OrderByDescending(x => S(x.t)).ThenBy(x => x.t.Id).ToList())
            {
                if (to.Clubs.Count >= NationalSize) break;
                from.Clubs.Remove(t); to.Clubs.Add(t);
            }
        }
        Fill(t2, t3.Clubs.Select(t => (t, t3)));
        var regionals = world.Competitions.Where(c => Level(c.Key) == 4).ToList();
        Fill(t3, regionals.SelectMany(c => c.Clubs.Select(t => (t, c))));
        foreach (var reg in regionals)
        {
            var region = reg.Key["usa-r-".Length..];
            var myLocals = world.Competitions.Where(c => Level(c.Key) == 5 && Areas.First(a => LocalKey(a.Key) == c.Key).Region == region).ToList();
            foreach (var (t, from) in myLocals.SelectMany(c => c.Clubs.Select(t => (t, c))).OrderByDescending(x => S(x.t)).ThenBy(x => x.t.Id).ToList())
            {
                if (reg.Clubs.Count >= RegionalSize) break;
                from.Clubs.Remove(t); reg.Clubs.Add(t);
            }
        }
        foreach (var c in world.Competitions.Where(c => IsPyramid(c.Key))) foreach (var t in c.Clubs) t.Division = c.Profile.Short;
        RealignTop(top, newcomers);
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
