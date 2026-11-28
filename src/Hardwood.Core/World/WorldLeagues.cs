using Hardwood.Core.Model;

namespace Hardwood.Core.World;

/// <summary>
/// The wider world (decisions #111-114): about twenty more top flights across Europe, the Americas, Asia and Africa,
/// a second European cup and an Americas club cup. One division per country, no promotion or relegation; quick results
/// unless you choose the full engine at career start. They bring every basketball country's players into the game,
/// so national teams have real pools to pick from. Run 217 (K5): the seven strongest European leagues have their real clubs
/// (<see cref="RealClubs"/>) and every league its real name in a real-world career; runs 218 and 219 gave the rest of the leagues
/// theirs (the BAL keeps two made-up clubs, in Bamako and Kinshasa).
/// H79 (run 107): no league repeats a nickname (Munich Bavarians beside Braunschweig Lions, Busan Egis beside Daegu Pegasus,
/// Batangas Bossing beside Manila Beermen), and Yokohama is the B-Corsairs, not the city's baseball team. Names only: the
/// abbreviations, the club count and order stay, so seeded worlds draw the same numbers.
/// </summary>
public static class WorldLeagues
{
    public const string Lithuania = "ltu", Aba = "aba", Germany = "ger", France = "fra", Italy = "ita", Greece = "gre", Israel = "isr",
        Vtb = "vtb", Poland = "pol", Baltic = "bal", Czechia = "cze", Benelux = "bnx", Argentina = "arg", Brazil = "bra", PuertoRico = "pur",
        Mexico = "mex", Japan = "jpn", Korea = "kor", Philippines = "phi", Africa = "afr", EuroCup = "ecc", Americas = "amc";

    private static CompetitionProfile[]? _profiles;
    public static CompetitionProfile[] Profiles => _profiles ??= Build();

    /// <summary>The Euro Elite League's source leagues once the European leagues exist. Run 222: the 19 licensed clubs below
    /// enter first; the leagues' tables fill the rest of the 20, one club from each league in this order.</summary>
    public static readonly (string, int)[] EliteEntrants =
    {
        (Competitions.Spain, 4), (Competitions.Turkey, 3), (Greece, 2), (Italy, 2), (Aba, 2), (Lithuania, 1), (Israel, 1), (France, 1), (Germany, 1), (Vtb, 1),
    };

    /// <summary>Run 222: the EuroLeague's 2025-26 clubs that are in the game ("City|Name"): the long-term licences and that
    /// season's wild cards. The 20th, Dubai Basketball, isn't in the game: the best club of the rest takes its place.</summary>
    public static readonly string[] EliteMembers =
    {
        "Madrid|Real Madrid", "Barcelona|Barça", "Vitoria-Gasteiz|Baskonia", "Valencia|Valencia Basket",
        "Istanbul|Fenerbahçe", "Istanbul|Anadolu Efes", "Athens|Panathinaikos", "Piraeus|Olympiacos",
        "Milan|Olimpia Milano", "Bologna|Virtus Bologna", "Kaunas|Zalgiris", "Munich|Bayern Munich",
        "Paris|Paris Basketball", "Villeurbanne|ASVEL", "Monaco|Monaco", "Tel Aviv|Maccabi Tel Aviv", "Tel Aviv|Hapoel Tel Aviv",
        "Belgrade|Partizan", "Belgrade|Crvena zvezda",
    };

    private static readonly PlayoffRound[] Eight = { new("Quarter-finals", 3, 8), new("Semi-finals", 5, 4), new("Final", 5, 2) };
    private static readonly PlayoffRound[] Four = { new("Semi-finals", 5, 4), new("Final", 5, 2) };
    private static readonly DayOfWeek[] Weekend = { DayOfWeek.Saturday };

    private static CompetitionProfile League(string key, string name, string shortName, string country, string[] locals, int offset, int foreign,
        double salary, double prestige, ClubSeed[] clubs, PlayoffRound[]? playoffs = null, bool stars = false, int robins = 2,
        (int, int)? start = null, DayOfWeek[]? days = null, bool twoDay = true, string? real = null, (int, int)? end = null) => new()
    {
        Key = key, Name = name, Short = shortName, RealName = real, Country = country, LocalNationalities = locals, StrengthOffset = offset, ForeignLimit = foreign,
        ImportsAreStars = stars, SalaryScale = salary, JobPrestige = prestige, Clubs = clubs, Playoffs = playoffs ?? Eight, RoundRobins = robins,
        Start = start ?? (9, 26), RoundDays = days ?? Weekend, TwoDayRounds = twoDay && days == null, RegularEnd = end, More = RealClubs.MoreOf(key),
    };

    private static ClubSeed[] Clubs(string country, params (string City, string Name, string Abbr)[] c) =>
        c.Select(x => new ClubSeed(x.City, x.Name, x.Abbr, country)).ToArray();

    private static CompetitionProfile[] Build() => new[]
    {
        // ---------------------------------------------------------------- Europe
        League(Lithuania, "Lithuanian League", "LKL", "Lithuania", new[] { "Lithuania" }, -8, 4, 0.07, -16,
            RealClubs.Lithuania,
            robins: 3, start: (9, 25), real: "Lithuanian Basketball League"),
        League(Aba, "Adriatic League", "ABA", "", new[] { "Serbia", "Croatia", "Slovenia", "Bosnia and Herzegovina", "Montenegro", "North Macedonia" },
            -5, 4, 0.07, -14,
            RealClubs.Aba, start: (9, 27), real: "ABA League"),
        League(Germany, "German League", "BBL", "Germany", new[] { "Germany", "Austria" }, -6, 6, 0.08, -14,
            RealClubs.Germany, start: (9, 26), real: "Basketball Bundesliga"),
        League(France, "French League", "LNB", "France", new[] { "France" }, -5, 5, 0.08, -14,
            RealClubs.France, real: "LNB Élite"),
        League(Italy, "Italian League", "LBA", "Italy", new[] { "Italy" }, -5, 6, 0.09, -13,
            RealClubs.Italy, real: "Lega Basket Serie A"),
        League(Greece, "Greek League", "GBL", "Greece", new[] { "Greece" }, -4, 6, 0.09, -13,
            RealClubs.Greece, real: "Greek Basket League"),
        League(Israel, "Israeli League", "IBL", "Israel", new[] { "Israel" }, -9, 5, 0.07, -16,
            RealClubs.Israel, real: "Israeli Premier League"),
        League(Vtb, "VTB United League", "VTB", "", new[] { "Russia", "Belarus", "Kazakhstan" }, -6, 4, 0.10, -15,
            RealClubs.Vtb, start: (9, 28)),
        League(Poland, "Polish League", "PLK", "Poland", new[] { "Poland" }, -15, 5, 0.04, -22,
            RealClubs.Poland, real: "Polish Basketball League"),
        League(Baltic, "Latvian-Estonian League", "LEL", "", new[] { "Latvia", "Estonia" }, -17, 4, 0.03, -26,
            RealClubs.Baltic, real: "Latvian-Estonian Basketball League"),
        League(Czechia, "Czech League", "NBL", "Czechia", new[] { "Czechia" }, -19, 4, 0.03, -27,
            RealClubs.Czechia, real: "Czech National Basketball League"),
        League(Benelux, "BNXT League", "BNX", "", new[] { "Belgium", "Netherlands" }, -17, 5, 0.035, -25,
            RealClubs.Benelux, real: "BNXT League"),
        // ---------------------------------------------------------------- Americas
        League(Argentina, "Argentine League", "LNA", "Argentina", new[] { "Argentina" }, -14, 3, 0.03, -24,
            // Run 233: 19 clubs (38 rounds) don't fit the weekends before the summer: Wednesdays and Saturdays to mid-April.
            RealClubs.Argentina, days: new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday }, end: (4, 18), real: "Liga Nacional de Básquet"),
        League(Brazil, "Brazilian League", "NBB", "Brazil", new[] { "Brazil" }, -15, 3, 0.035, -24,
            RealClubs.Brazil, real: "Novo Basquete Brasil"),
        League(PuertoRico, "Puerto Rico League", "BSN", "Puerto Rico", new[] { "Puerto Rico" }, -16, 3, 0.04, -25,
            RealClubs.PuertoRico,
            stars: true, real: "Baloncesto Superior Nacional"),
        League(Mexico, "Mexican League", "LNM", "Mexico", new[] { "Mexico" }, -20, 4, 0.03, -28,
            RealClubs.Mexico, real: "Liga Nacional de Baloncesto Profesional"),
        // ---------------------------------------------------------------- Asia and Africa
        League(Japan, "Japan B.League", "BLG", "Japan", new[] { "Japan" }, -11, 3, 0.08, -18,
            RealClubs.Japan,
            stars: true, days: new[] { DayOfWeek.Saturday, DayOfWeek.Wednesday }, start: (10, 3), real: "B.League"),
        League(Korea, "Korean League", "KBL", "Korea", new[] { "Korea" }, -16, 2, 0.06, -22,
            RealClubs.Korea,
            stars: true, robins: 4, days: new[] { DayOfWeek.Saturday, DayOfWeek.Wednesday }, start: (10, 17), playoffs: Four, real: "Korean Basketball League"),
        League(Philippines, "Philippine League", "PBA", "Philippines", new[] { "Philippines" }, -20, 1, 0.04, -28,
            RealClubs.Philippines,
            stars: true, robins: 3, days: new[] { DayOfWeek.Sunday, DayOfWeek.Wednesday }, start: (10, 4), real: "Philippine Basketball Association"),
        League(Africa, "Basketball Africa League", "BAL", "", new[]
            {
                "Senegal", "Nigeria", "Egypt", "Tunisia", "Angola", "Rwanda", "Morocco", "Mali", "Cameroon", "South Africa", "Ivory Coast", "Congo DR",
            }, -20, 4, 0.02, -28,
            RealClubs.Africa, robins: 2, days: new[] { DayOfWeek.Sunday }, playoffs: Four),
        // ---------------------------------------------------------------- Cups
        new()
        {
            // Run 222 (the real format): 20 clubs, single-game knockouts and a best-of-three final. Run 227: in two groups of ten,
            // home and away (18 rounds, Oct to early March); the top two in each group go straight to the quarter-finals, 3rd-6th
            // play the eighth-finals.
            Key = EuroCup, Name = "Euro Challenge Cup", Short = "ECC", RealName = "EuroCup", Kind = CompetitionKind.Continental,
            RoundRobins = 2, Groups = 2, Start = (10, 7), RegularEnd = (3, 4), RoundDays = new[] { DayOfWeek.Wednesday }, PlayoffDays = new[] { DayOfWeek.Wednesday, DayOfWeek.Friday },
            Playoffs = new[]
            {
                new PlayoffRound("Eighth-finals", 1, 16), new PlayoffRound("Quarter-finals", 1, 8), new PlayoffRound("Semi-finals", 1, 4), new PlayoffRound("Final", 3, 2),
            },
            Entrants = new[]
            {
                (Competitions.Spain, 3), (Competitions.Turkey, 3), (Italy, 2), (France, 1), (Germany, 3), (Aba, 2), (Greece, 2), (Israel, 1),
                (Lithuania, 2), (Poland, 1),
            },
        },
        new()
        {
            Key = Americas, Name = "Americas Club Cup", Short = "ACC", RealName = "Basketball Champions League Americas", Kind = CompetitionKind.Continental,
            RoundRobins = 1, Start = (11, 4), RoundDays = new[] { DayOfWeek.Tuesday }, PlayoffDays = new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday },
            Playoffs = new[] { new PlayoffRound("Semi-finals", 1, 4), new PlayoffRound("Final", 1, 2) },
            Entrants = new[] { (Argentina, 3), (Brazil, 3), (PuertoRico, 1), (Mexico, 1) },
        },
    };
}
