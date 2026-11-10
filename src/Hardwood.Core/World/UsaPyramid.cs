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
    public const int NationalSize = 24, RegionalSize = 18;
    /// <summary>Clubs moving between the NABL and tier 2, and between tiers 2 and 3, each summer (decisions #79, #91).</summary>
    public const int NationalSwaps = 3;

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
        CityArea.TryGetValue(t.City, out var a) ? a : DivisionHome.TryGetValue(t.Division, out var d) ? d : "prl";

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
    // Promotion and relegation (each summer, before the next season's fixtures are made).

    /// <summary>
    /// Move clubs between levels using the finished tables: NABL bottom 3 ↔ tier-2 top 3, tier-2 bottom 3 ↔ tier-3 top 3,
    /// tier-3 bottom 6 down to their regions ↔ the six regional champions up, each regional league's bottom clubs down
    /// to their local leagues ↔ each local champion up; then regional leagues are brought back to 18.
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

        void Move(Team t, WorldCompetition from, WorldCompetition to) { from.Clubs.Remove(t); to.Clubs.Add(t); }
        string Names(IEnumerable<Team> ts) => string.Join(", ", ts.Select(t => t.FullName));

        // NABL ↔ tier 2 (3 straight up and down, decision #79).
        var last = nabl.History.LastOrDefault();
        if (last != null && last.Records.Count > 0)
        {
            var wins = last.Records.ToDictionary(r => r.teamId, r => (r.wins, r.losses));
            var down = nabl.Teams.OrderBy(t => wins.TryGetValue(t.Id, out var w) ? w.wins : 41).ThenByDescending(t => wins.TryGetValue(t.Id, out var w) ? w.losses : 41)
                .Take(NationalSwaps).ToList();
            var up = tables[Tier2].Take(NationalSwaps).ToList();
            for (int i = 0; i < Math.Min(down.Count, up.Count); i++)
            {
                var r = down[i]; var p = up[i];
                nabl.Teams.Remove(r); t2.Clubs.Add(r);
                t2.Clubs.Remove(p); nabl.Teams.Add(p);
                p.Conference = r.Conference; p.Division = r.Division;
                r.Division = t2.Profile.Short;
                p.Tactic.Pace = new Tactic().Pace;
                if (p.Staff.Count == 0)
                    foreach (var role in Enum.GetValues<StaffRole>()) { var st = staffGen.Generate(nabl, role, on, 8); st.TeamId = p.Id; p.Staff.Add(st); }
            }
            news.Add(($"Promoted to the NABL: {Names(up)}", $"{Names(up)} finished top of {t2.Profile.Name} and move up to the NABL."));
            news.Add(($"Relegated from the NABL: {Names(down)}", $"{Names(down)} finished bottom of the NABL and drop into {t2.Profile.Name}."));
        }
        // Tier 2 ↔ tier 3.
        var t2Down = tables[Tier2].Where(t => t2.Clubs.Contains(t)).TakeLast(NationalSwaps).ToList();
        var t3Up = tables[Tier3].Take(NationalSwaps).ToList();
        foreach (var t in t2Down) Move(t, t2, t3);
        foreach (var t in t3Up) Move(t, t3, t2);
        news.Add(($"{t2.Profile.Name}: up and down", $"Promoted from {t3.Profile.Name}: {Names(t3Up)}. Relegated to {t3.Profile.Name}: {Names(t2Down)}."));
        // Tier 3 ↔ regional leagues: the regional champions go up; tier 3's bottom clubs go home to their regions.
        var regionalChamps = regionals.Select(c => tables[c.Key].FirstOrDefault()).Where(t => t != null).Cast<Team>().ToList();
        var t3Down = tables[Tier3].Where(t => t3.Clubs.Contains(t) && !t3Up.Contains(t)).TakeLast(regionalChamps.Count).ToList();
        foreach (var t in t3Down) Move(t, t3, regionals.First(c => c.Key == RegionalKey(RegionOf(t))));
        foreach (var t in regionalChamps) Move(t, regionals.First(c => c.Clubs.Contains(t)), t3);
        news.Add(($"{t3.Profile.Name}: up and down", $"Regional champions promoted: {Names(regionalChamps)}. Relegated to their regions: {Names(t3Down)}."));
        // Regional ↔ local leagues: each local champion goes up; each regional league sends as many back down.
        foreach (var reg in regionals)
        {
            var area = reg.Key["usa-r-".Length..];
            var myLocals = locals.Where(c => Areas.First(a => LocalKey(a.Key) == c.Key).Region == area).ToList();
            var champs = myLocals.Select(c => tables[c.Key].FirstOrDefault()).Where(t => t != null).Cast<Team>().ToList();
            var down = tables[reg.Key].Where(t => reg.Clubs.Contains(t) && !regionalChamps.Contains(t)).TakeLast(champs.Count).ToList();
            foreach (var t in down) Move(t, reg, myLocals.FirstOrDefault(c => c.Key == LocalKey(AreaOf(t))) ?? myLocals[0]);
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
        foreach (var t in nabl.Teams.Where(t => before.ContainsKey(t))) Season.Budgets.Rescale(t, 1, nablArena, ScaleOf(before[t]));
        foreach (var c in world.Competitions.Where(c => IsPyramid(c.Key)))
        {
            double arena = c.Clubs.Average(t => (double)t.ArenaCapacity);
            foreach (var t in c.Clubs)
            {
                t.Division = c.Profile.Short;
                if (nabl.Teams.Any(x => x.Id == t.Id)) continue;
                if (!before.TryGetValue(t, out var was))
                    Season.Budgets.Rescale(t, c.Profile.SalaryScale, arena, 1); // down from the NABL
                else if (was != c.Key) Season.Budgets.Rescale(t, c.Profile.SalaryScale, arena, ScaleOf(was));
            }
        }
        world.Index();
        return news;
    }
}
