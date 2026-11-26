using Hardwood.Core.Model;

namespace Hardwood.Core.World;

/// <summary>
/// The wider world (decisions #111-114): about twenty more top flights across Europe, the Americas, Asia and Africa,
/// a second European cup and an Americas club cup. One division per country, no promotion or relegation; quick results
/// unless you choose the full engine at career start. They bring every basketball country's players into the game,
/// so national teams have real pools to pick from. Run 217 (K5): the seven strongest European leagues have their real clubs
/// (<see cref="RealClubs"/>) and every league its real name in a real-world career; the rest of the clubs are still made up.
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

    /// <summary>The Euro Elite League's entrants once the European leagues exist (18 clubs).</summary>
    public static readonly (string, int)[] EliteEntrants =
    {
        (Competitions.Spain, 4), (Competitions.Turkey, 3), (Greece, 2), (Italy, 2), (Aba, 2), (Lithuania, 1), (Israel, 1), (France, 1), (Germany, 1), (Vtb, 1),
    };

    private static readonly PlayoffRound[] Eight = { new("Quarter-finals", 3, 8), new("Semi-finals", 5, 4), new("Final", 5, 2) };
    private static readonly PlayoffRound[] Four = { new("Semi-finals", 5, 4), new("Final", 5, 2) };
    private static readonly DayOfWeek[] Weekend = { DayOfWeek.Saturday };

    private static CompetitionProfile League(string key, string name, string shortName, string country, string[] locals, int offset, int foreign,
        double salary, double prestige, ClubSeed[] clubs, PlayoffRound[]? playoffs = null, bool stars = false, int robins = 2,
        (int, int)? start = null, DayOfWeek[]? days = null, bool twoDay = true, string? real = null) => new()
    {
        Key = key, Name = name, Short = shortName, RealName = real, Country = country, LocalNationalities = locals, StrengthOffset = offset, ForeignLimit = foreign,
        ImportsAreStars = stars, SalaryScale = salary, JobPrestige = prestige, Clubs = clubs, Playoffs = playoffs ?? Eight, RoundRobins = robins,
        Start = start ?? (9, 26), RoundDays = days ?? Weekend, TwoDayRounds = twoDay && days == null,
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
            new ClubSeed[]
            {
                new("Belgrade", "Red Stars", "CZV", "Serbia"), new("Belgrade", "Partisans", "BPA", "Serbia"), new("Novi Sad", "Danubians", "NSD", "Serbia"),
                new("Nis", "Nisava", "NIS", "Serbia"), new("Zagreb", "Cibona Lions", "ZAG", "Croatia"), new("Split", "Dalmatians", "SPL", "Croatia"),
                new("Ljubljana", "Dragons", "LJU", "Slovenia"), new("Krka", "Novo Mesto", "KRK", "Slovenia"), new("Podgorica", "Buducnost Eagles", "PDG", "Montenegro"),
                new("Niksic", "Ironworks", "NIK", "Montenegro"), new("Sarajevo", "Bascarsija", "SJJ", "Bosnia and Herzegovina"),
                new("Skopje", "Vardar", "SKP", "North Macedonia"), new("Zadar", "Adriatics", "ZAD", "Croatia"), new("Mega", "Belgrade Youth", "MEG", "Serbia"),
            }, start: (9, 27), real: "ABA League"),
        League(Germany, "German League", "BBL", "Germany", new[] { "Germany", "Austria" }, -6, 6, 0.08, -14,
            RealClubs.Germany, start: (9, 26), real: "Basketball Bundesliga"),
        League(France, "French League", "LNB", "France", new[] { "France" }, -5, 5, 0.08, -14,
            RealClubs.France, real: "LNB Élite"),
        League(Italy, "Italian League", "LBA", "Italy", new[] { "Italy" }, -5, 6, 0.09, -13,
            RealClubs.Italy, real: "Lega Basket Serie A"),
        League(Greece, "Greek League", "GBL", "Greece", new[] { "Greece" }, -4, 6, 0.09, -13,
            RealClubs.Greece, real: "Greek Basket League"),
        League(Israel, "Israeli League", "IBL", "Israel", new[] { "Israel" }, -9, 5, 0.07, -16,
            Clubs("Israel", ("Tel Aviv", "Yellows", "TAY"), ("Tel Aviv", "Reds", "TAR"), ("Jerusalem", "Capitals", "JER"), ("Holon", "Southerners", "HOL"),
                ("Haifa", "Carmel", "HAI"), ("Rishon", "Lezion", "RIS"), ("Ness Ziona", "Orchards", "NZI"), ("Hadera", "Riverside", "HAD"),
                ("Herzliya", "Seaside", "HER"), ("Galil", "Gilboa", "GIL"), ("Ironi Kiryat", "Ata", "KIR"), ("Eilat", "Red Sea", "EIL")), real: "Israeli Premier League"),
        League(Vtb, "VTB United League", "VTB", "", new[] { "Russia", "Belarus", "Kazakhstan" }, -6, 4, 0.10, -15,
            new ClubSeed[]
            {
                new("Moscow", "Army", "MSA", "Russia"), new("Moscow", "Railwaymen", "MSR", "Russia"), new("St. Petersburg", "Zenith", "SPB", "Russia"),
                new("Kazan", "Unicorns", "KZN", "Russia"), new("Krasnodar", "Kuban", "KRA", "Russia"), new("Nizhny Novgorod", "Volga", "NNV", "Russia"),
                new("Samara", "Rockets", "SAM", "Russia"), new("Perm", "Parma", "PRM", "Russia"), new("Yekaterinburg", "Urals", "YEK", "Russia"),
                new("Minsk", "Tsmoki", "TSM", "Belarus"), new("Astana", "Steppe", "AST", "Kazakhstan"), new("Novosibirsk", "Siberians", "NOV", "Russia"),
            }, start: (9, 28)),
        League(Poland, "Polish League", "PLK", "Poland", new[] { "Poland" }, -15, 5, 0.04, -22,
            Clubs("Poland", ("Warsaw", "Legion", "WAR"), ("Wroclaw", "Silesians", "WRO"), ("Gdynia", "Seahawks", "GDY"), ("Szczecin", "Kings", "SZC"),
                ("Zielona Gora", "Vineyard", "ZIE"), ("Lublin", "Starlings", "LUB"), ("Wloclawek", "Anwil", "WLO"), ("Ostrow", "Stallions", "OST"),
                ("Sopot", "Pier", "SOP"), ("Torun", "Gingerbread", "TRN"), ("Gliwice", "Miners", "GLI"), ("Krakow", "Wawel", "KRW"),
                ("Stargard", "Spojnia", "STA"), ("Dabrowa", "Furnace", "DAB")), real: "Polish Basketball League"),
        League(Baltic, "Latvian-Estonian League", "LEL", "", new[] { "Latvia", "Estonia" }, -17, 4, 0.03, -26,
            new ClubSeed[]
            {
                new("Riga", "Vef", "RIG", "Latvia"), new("Riga", "Daugava", "RDA", "Latvia"), new("Ventspils", "Port", "VSP", "Latvia"),
                new("Liepaja", "Wind", "LIE", "Latvia"), new("Valmiera", "Glass", "VLM", "Latvia"), new("Jelgava", "Zemgale", "JEL", "Latvia"),
                new("Tallinn", "Kalev", "TLL", "Estonia"), new("Tartu", "Rock", "TAT", "Estonia"), new("Parnu", "Seaside", "PRN", "Estonia"),
                new("Rakvere", "Tarvas", "RAK", "Estonia"), new("Viimsi", "Coast", "VII", "Estonia"), new("Keila", "Coolbet", "KEI", "Estonia"),
            }),
        League(Czechia, "Czech League", "NBL", "Czechia", new[] { "Czechia" }, -19, 4, 0.03, -27,
            Clubs("Czechia", ("Nymburk", "Elbe", "NYM"), ("Prague", "Vltava", "PRG"), ("Brno", "Moravians", "BRN"), ("Opava", "Silesians", "OPA"),
                ("Pardubice", "Gingerbread", "PCE"), ("Ostrava", "Steel", "OSV"), ("Kolin", "Royals", "KOL"), ("Decin", "Warriors", "DEC"),
                ("Usti", "Elbe Gate", "UST"), ("Olomouc", "Hanaks", "OLO")), real: "Czech National Basketball League"),
        League(Benelux, "BNXT League", "BNX", "", new[] { "Belgium", "Netherlands" }, -17, 5, 0.035, -25,
            new ClubSeed[]
            {
                new("Ostend", "Seagulls", "OSN", "Belgium"), new("Antwerp", "Giants", "ANT", "Belgium"), new("Mons", "Hainaut", "MON", "Belgium"),
                new("Leuven", "Bears", "LEU", "Belgium"), new("Brussels", "Capitals", "BRU", "Belgium"), new("Liege", "Meuse", "LGE", "Belgium"),
                new("Mechelen", "Kangaroos", "MEC", "Belgium"), new("Groningen", "Donar", "GRN", "Netherlands"), new("Leiden", "Keys", "LEI", "Netherlands"),
                new("Den Bosch", "Dragons", "DBO", "Netherlands"), new("Amsterdam", "Canals", "AMS", "Netherlands"), new("Rotterdam", "Port", "ROT", "Netherlands"),
                new("Zwolle", "Landstede", "ZWO", "Netherlands"), new("Den Helder", "Suns", "DHE", "Netherlands"),
            }),
        // ---------------------------------------------------------------- Americas
        League(Argentina, "Argentine League", "LNA", "Argentina", new[] { "Argentina" }, -14, 3, 0.03, -24,
            Clubs("Argentina", ("Buenos Aires", "Porteños", "BUE"), ("Buenos Aires", "Boquenses", "BBQ"), ("Bahia Blanca", "Southerners", "BAH"),
                ("Cordoba", "Atenas", "COR"), ("Mar del Plata", "Peñarol", "MDP"), ("San Lorenzo", "Almagro", "SLO"), ("Obras", "Sanitarias", "OBR"),
                ("Quimsa", "Santiago", "QUI"), ("Rosario", "Lepers", "RSR"), ("Mendoza", "Vintners", "MDZ"), ("Comodoro", "Gimnasia", "CMD"),
                ("Corrientes", "Regatas", "CRR"), ("La Plata", "Platenses", "LPL"), ("Salta", "Andeans", "SAL"), ("Tucuman", "Garden", "TUC"),
                ("Parana", "Echague", "PRA"), ("Olavarria", "Estudiantes", "OLA"), ("Zarate", "Basket", "ZRT")), real: "Liga Nacional de Básquet"),
        League(Brazil, "Brazilian League", "NBB", "Brazil", new[] { "Brazil" }, -15, 3, 0.035, -24,
            Clubs("Brazil", ("Sao Paulo", "Tricolor", "SAO"), ("Rio de Janeiro", "Flamingos", "RIO"), ("Franca", "Coffee", "FRN"), ("Brasilia", "Capital", "BSB"),
                ("Bauru", "Dragons", "BAU"), ("Minas", "Tennis", "MIS"), ("Mogi", "Cruzes", "MOG"), ("Pinheiros", "Pines", "PIN"),
                ("Fortaleza", "Sharks", "FOR"), ("Unifacisa", "Paraiba", "UNI"), ("Paulistano", "Athletics", "PAO"), ("Corinthians", "Timao", "CTH"),
                ("Botafogo", "Lone Star", "BOT"), ("Vasco", "Admirals", "VAS"), ("Caxias", "Gauchos", "CAX"), ("Sao Jose", "Valley", "SJO")), real: "Novo Basquete Brasil"),
        League(PuertoRico, "Puerto Rico League", "BSN", "Puerto Rico", new[] { "Puerto Rico" }, -16, 3, 0.04, -25,
            Clubs("Puerto Rico", ("San Juan", "Capitanes", "SJU"), ("Bayamon", "Cowboys", "BYM"), ("Ponce", "Lions", "PON"), ("Mayaguez", "Indians", "MAY"),
                ("Arecibo", "Captains", "ARE"), ("Carolina", "Giants", "CAR"), ("Caguas", "Creoles", "CAG"), ("Guaynabo", "Mets", "GUA"),
                ("Quebradillas", "Pirates", "QBR"), ("Manati", "Athenians", "MAN"), ("Aguada", "Sailors", "AGU"), ("Humacao", "Grays", "HUM")),
            stars: true, real: "Baloncesto Superior Nacional"),
        League(Mexico, "Mexican League", "LNM", "Mexico", new[] { "Mexico" }, -20, 4, 0.03, -28,
            Clubs("Mexico", ("Mexico City", "Capitanes", "MXC"), ("Monterrey", "Fuerza", "MTY"), ("Guadalajara", "Astros", "GDL"), ("Tijuana", "Zonkeys", "TIJ"),
                ("Chihuahua", "Dorados", "DOR"), ("Puebla", "Angels", "PUE"), ("Merida", "Mayans", "MID"), ("Leon", "Lions", "LEO"),
                ("Hermosillo", "Suns", "HMO"), ("Mexicali", "Soles", "MXL"), ("Queretaro", "Libertadores", "QRO"), ("Veracruz", "Halcones", "VER")), real: "Liga Nacional de Baloncesto Profesional"),
        // ---------------------------------------------------------------- Asia and Africa
        League(Japan, "Japan B.League", "BLG", "Japan", new[] { "Japan" }, -11, 3, 0.08, -18,
            Clubs("Japan", ("Tokyo", "Alvarks", "TKY"), ("Tokyo", "Sunrockers", "TKS"), ("Chiba", "Jets", "CHB"), ("Utsunomiya", "Brex", "UTS"),
                ("Okinawa", "Kings", "OKI"), ("Nagoya", "Diamonds", "NAG"), ("Osaka", "Evessa", "OSA"), ("Kawasaki", "Thunders", "KAW"),
                ("Yokohama", "B-Corsairs", "YOK"), ("Shimane", "Magic", "SHI"), ("Hiroshima", "Dragonflies", "HIR"), ("Sendai", "Eighty Niners", "SEN"),
                ("Hokkaido", "Levanga", "HOK"), ("Shiga", "Lakes", "SHG"), ("Kyoto", "Hannaryz", "KYO"), ("Niigata", "Albirex", "NII")),
            stars: true, days: new[] { DayOfWeek.Saturday, DayOfWeek.Wednesday }, start: (10, 3), real: "B.League"),
        League(Korea, "Korean League", "KBL", "Korea", new[] { "Korea" }, -16, 2, 0.06, -22,
            Clubs("Korea", ("Seoul", "Knights", "SEO"), ("Seoul", "Thunders", "SET"), ("Anyang", "Red Boosters", "ANY"), ("Suwon", "Sonic Boom", "SUW"),
                ("Ulsan", "Phoebus", "ULS"), ("Wonju", "Promy", "WON"), ("Changwon", "Sakers", "CWN"), ("Busan", "Egis", "BUS"),
                ("Goyang", "Sono", "GOY"), ("Daegu", "Pegasus", "DAE")),
            stars: true, robins: 4, days: new[] { DayOfWeek.Saturday, DayOfWeek.Wednesday }, start: (10, 17), playoffs: Four, real: "Korean Basketball League"),
        League(Philippines, "Philippine League", "PBA", "Philippines", new[] { "Philippines" }, -20, 1, 0.04, -28,
            Clubs("Philippines", ("Manila", "Beermen", "MNL"), ("Manila", "Gin Kings", "MGK"), ("Quezon City", "Tropang", "QCT"), ("Pasig", "Bolts", "PSG"),
                ("Makati", "Hotshots", "MKT"), ("Cebu", "Fuel Masters", "CEB"), ("Davao", "Painters", "DVO"), ("Pampanga", "Dyip", "PMP"),
                ("Taguig", "Road Warriors", "TAG"), ("Laguna", "Elasto", "LAG"), ("Batangas", "Bossing", "BTG"), ("Iloilo", "Converge", "ILO")),
            stars: true, robins: 3, days: new[] { DayOfWeek.Sunday, DayOfWeek.Wednesday }, start: (10, 4), real: "Philippine Basketball Association"),
        League(Africa, "Basketball Africa League", "BAL", "", new[]
            {
                "Senegal", "Nigeria", "Egypt", "Tunisia", "Angola", "Rwanda", "Morocco", "Mali", "Cameroon", "South Africa", "Ivory Coast", "Congo DR",
            }, -20, 4, 0.02, -28,
            new ClubSeed[]
            {
                new("Dakar", "Lions", "DKR", "Senegal"), new("Lagos", "Rivers Hoopers", "LGS", "Nigeria"), new("Cairo", "Pharaohs", "CAI", "Egypt"),
                new("Alexandria", "Ittihad", "ALX", "Egypt"), new("Tunis", "Esperance", "TUN", "Tunisia"), new("Monastir", "Ribat", "MNT", "Tunisia"),
                new("Luanda", "Petro", "LUA", "Angola"), new("Kigali", "Patriots", "KGL", "Rwanda"), new("Casablanca", "Fus", "CAS", "Morocco"),
                new("Bamako", "Stade", "BMK", "Mali"), new("Yaounde", "Forces", "YAO", "Cameroon"), new("Johannesburg", "Cape Tigers", "JNB", "South Africa"),
                new("Abidjan", "Elephants", "ABJ", "Ivory Coast"), new("Kinshasa", "Leopards", "KIN", "Congo DR"),
            }, robins: 2, days: new[] { DayOfWeek.Sunday }, playoffs: Four),
        // ---------------------------------------------------------------- Cups
        new()
        {
            Key = EuroCup, Name = "Euro Challenge Cup", Short = "ECC", RealName = "EuroCup", Kind = CompetitionKind.Continental,
            RoundRobins = 1, Start = (10, 7), RoundDays = new[] { DayOfWeek.Wednesday }, PlayoffDays = new[] { DayOfWeek.Wednesday, DayOfWeek.Friday },
            Playoffs = new[] { new PlayoffRound("Quarter-finals", 3, 8), new PlayoffRound("Semi-finals", 3, 4), new PlayoffRound("Final", 3, 2) },
            Entrants = new[]
            {
                (Competitions.Spain, 2), (Competitions.Turkey, 2), (Italy, 2), (France, 2), (Germany, 2), (Aba, 2), (Greece, 1), (Israel, 1),
                (Lithuania, 1), (Poland, 1),
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
