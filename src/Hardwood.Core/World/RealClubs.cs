using Hardwood.Core.Model;

namespace Hardwood.Core.World;

/// <summary>
/// K5, slice 1 (run 217; Danny's note `danny-world-leagues-nov26`): the real clubs of the EuroLeague's seven strongest domestic
/// leagues (Spain, Turkey, Greece, Italy, France, Germany, Lithuania), with their cities, arenas and capacities. Rosters abroad
/// stay generated (Danny: "you dont have to go so far as to get the players too"); no logos or crests are copied.
/// <para>Source: the 2025-26 season pages on English Wikipedia ("Teams" / "Venues and locations"), read on Nov 26, 2026.
/// Sponsor names are dropped where a club is known without them. Where the game's league has fewer places than the real
/// one, the newest arrival is left out (Greece: Mykonos); where it has more, a recent member fills it (Lithuania: Wolves).
/// Letters outside Latin-1 are written in plain Latin (Besiktas, Zalgiris), as the card fonts need (<c>CardRenderer.Fold</c>).</para>
/// <para>Each club takes the place of a made-up club in the same slot, so a seeded world draws the same numbers in the same
/// order. <see cref="ClubSeed.Was"/> keeps the made-up name, and <see cref="Rename"/> turns an older save's clubs into the real
/// ones when it loads (as D44 renamed the pyramid). <see cref="ClubSeed.Standing"/> is the club's place in the real hierarchy,
/// added to its roster's quality in a new world, so Real Madrid and Zalgiris are strong and Lleida and Gargzdai are not.</para>
/// </summary>
public static partial class RealClubs
{
    private static ClubSeed R(string city, string name, string abbr, string? full, string arena, int capacity, int standing, string wasCity, string wasName) =>
        new(city, name, abbr) { Full = full, Arena = arena, Capacity = capacity, Standing = standing, Was = $"{wasCity}|{wasName}" };

    /// <summary>Liga ACB (18).</summary>
    public static readonly ClubSeed[] Spain =
    {
        R("Madrid", "Real Madrid", "RMA", "Real Madrid", "Movistar Arena", 13109, 8, "Madrid", "Capitales"),
        R("Barcelona", "Barça", "BCN", "FC Barcelona", "Palau Blaugrana", 7786, 7, "Barcelona", "Navegantes"),
        R("Valencia", "Valencia Basket", "VLC", "Valencia Basket", "Roig Arena", 15600, 5, "Valencia", "Naranjeros"),
        R("Vitoria-Gasteiz", "Baskonia", "BAS", "Baskonia", "Buesa Arena", 15504, 5, "Vitoria", "Alaveses"),
        R("Málaga", "Unicaja", "MLG", "Unicaja", "Martín Carpena", 10699, 4, "Malaga", "Boquerones"),
        R("Murcia", "UCAM Murcia", "UCM", "UCAM Murcia", "Palacio de Deportes de Murcia", 7454, 2, "Murcia", "Huertanos"),
        R("La Laguna", "Tenerife", "TFE", "La Laguna Tenerife", "Pabellón Santiago Martín", 5100, 2, "Tenerife", "Volcanes"),
        R("Las Palmas", "Gran Canaria", "GCA", "Gran Canaria", "Gran Canaria Arena", 9870, 2, "Gran Canaria", "Alisios"),
        R("Badalona", "Joventut", "JOV", "Joventut Badalona", "Palau Municipal d'Esports", 12760, 0, "Badalona", "Verdinegros"),
        R("Zaragoza", "Zaragoza", "ZAR", "Basket Zaragoza", "Pabellón Príncipe Felipe", 10744, -1, "Zaragoza", "Cierzo"),
        R("Manresa", "Manresa", "MNR", "Bàsquet Manresa", "Nou Congost", 5000, -2, "Manresa", "Tigres"),
        R("Bilbao", "Bilbao Basket", "BIO", "Bilbao Basket", "Bilbao Arena", 10014, -1, "Bilbao", "Leones"),
        R("Granada", "Granada", "GRX", "Fundación CB Granada", "Palacio de Deportes de Granada", 8100, -3, "Granada", "Nazaries"),
        R("Andorra la Vella", "Andorra", "AND", "BC Andorra", "Pavelló Toni Martí", 5001, -3, "Andorra", "Pirineus"),
        R("Girona", "Girona", "GRO", "Bàsquet Girona", "Fontajau", 5500, -3, "Girona", "Muralles"),
        R("Burgos", "San Pablo Burgos", "BUR", "San Pablo Burgos", "Coliseum Burgos", 9604, -4, "Burgos", "Castellanos"),
        R("Lugo", "Breogán", "LUG", "Río Breogán", "Pazo dos Deportes", 5310, -3, "Lugo", "Celtas"),
        R("Lleida", "Lleida", "LLE", "Força Lleida", "Barris Nord", 6100, -4, "Sevilla", "Giraldas"),
    };

    /// <summary>Basketbol Super Ligi (16).</summary>
    public static readonly ClubSeed[] Turkey =
    {
        R("Istanbul", "Fenerbahçe", "FEN", "Fenerbahçe", "Ülker Sports Arena", 13800, 8, "Istanbul", "Kartallar"),
        R("Istanbul", "Anadolu Efes", "EFS", "Anadolu Efes", "Basketbol Gelisim Merkezi", 10000, 6, "Istanbul", "Sarilar"),
        R("Istanbul", "Galatasaray", "GSY", "Galatasaray", "Basketbol Gelisim Merkezi", 10000, 3, "Istanbul", "Bogazici"),
        R("Istanbul", "Besiktas", "BJK", "Besiktas", "Akatlar Arena", 3200, 2, "Istanbul", "Halic"),
        R("Ankara", "Türk Telekom", "TTK", "Türk Telekom", "Ankara Arena", 10400, 1, "Ankara", "Baskent"),
        R("Izmir", "Karsiyaka", "KSK", "Karsiyaka", "Karsiyaka Arena", 5000, 0, "Izmir", "Korfez"),
        R("Bursa", "Tofas", "TOF", "Tofas", "Tofas Nilüfer Sports Hall", 7500, 1, "Bursa", "Uludag"),
        R("Istanbul", "Bahcesehir Koleji", "BKO", "Bahcesehir Koleji", "Sinan Erdem Dome", 13800, 1, "Antalya", "Akdeniz"),
        R("Aliaga", "Petkimspor", "PET", "Aliaga Petkimspor", "Aliaga ENKA Sports Hall", 3000, -1, "Gaziantep", "Sahinler"),
        R("Istanbul", "Erokspor", "ERO", "Esenler Erokspor", "Sinan Erdem Dome", 13800, -2, "Konya", "Selcuklu"),
        R("Mersin", "Mersin MSK", "MSK", "Mersin MSK", "Servet Tazegül Arena", 7500, -2, "Mersin", "Limanlar"),
        R("Bursa", "Bursaspor", "BSP", "Bursaspor Basketbol", "Tofas Nilüfer Sports Hall", 7500, -2, "Samsun", "Karadeniz"),
        R("Trabzon", "Trabzonspor", "TZS", "Trabzonspor", "Hayri Gür Arena", 7500, -2, "Trabzon", "Firtina"),
        R("Istanbul", "Büyükçekmece", "BYK", "Büyükçekmece Basketbol", "Gazanfer Bilge Sports Hall", 3000, -3, "Eskisehir", "Porsuk"),
        R("Denizli", "Merkezefendi", "MKE", "Merkezefendi Basket", "Pamukkale University Arena", 3490, -3, "Kayseri", "Erciyes"),
        R("Manisa", "Manisa Basket", "MBK", "Manisa Basket", "Muradiye Sports Hall", 3500, -3, "Manisa", "Spil"),
    };

    /// <summary>Greek Basket League (12 of the real 13: Mykonos, the newest, is left out).</summary>
    public static readonly ClubSeed[] Greece =
    {
        R("Athens", "Panathinaikos", "PAN", "Panathinaikos", "Telekom Center Athens", 19443, 8, "Athens", "Shamrocks"),
        R("Piraeus", "Olympiacos", "OLY", "Olympiacos", "Peace and Friendship Stadium", 11319, 8, "Piraeus", "Reds"),
        R("Athens", "AEK", "AEK", "AEK Athens", "Ano Liosia Olympic Hall", 9327, 3, "Athens", "Eagles"),
        R("Thessaloniki", "PAOK", "PKS", "PAOK", "PAOK Sports Arena", 8500, 2, "Thessaloniki", "Black Eagles"),
        R("Thessaloniki", "Aris", "ARI", "Aris Thessaloniki", "Alexandrio Melathron", 5138, 1, "Thessaloniki", "Iraklis"),
        R("Peristeri", "Peristeri", "PRS", "Peristeri", "Peristeri Arena", 4000, 0, "Peristeri", "Westsiders"),
        R("Patras", "Promitheas", "PAT", "Promitheas Patras", "Dimitris Tofalos Arena", 4150, 0, "Patras", "Achaeans"),
        R("Karditsa", "Karditsa", "KRD", "Karditsa", "Karditsa New Indoor Hall", 3000, -2, "Larissa", "Thessalians"),
        R("Rhodes", "Kolossos", "RHO", "Kolossos Rodou", "Kallithea Palais des Sports", 1400, -2, "Kolossos", "Rhodes"),
        R("Thessaloniki", "Iraklis", "IRA", "Iraklis", "Ivanofeio Sports Arena", 2580, -3, "Lavrio", "Miners"),
        R("Glyfada", "Panionios", "PNI", "Panionios", "Glyfada Makis Liougas Hall", 3500, -2, "Kalamata", "Messenians"),
        R("Marousi", "Maroussi", "MAR", "Maroussi", "Saint Thomas Indoor Hall", 1700, -3, "Marousi", "Northerners"),
    };

    /// <summary>Lega Basket Serie A (16).</summary>
    public static readonly ClubSeed[] Italy =
    {
        R("Milan", "Olimpia Milano", "MLN", "Olimpia Milano", "Forum di Milano", 12331, 7, "Milan", "Olimpians"),
        R("Bologna", "Virtus Bologna", "BOL", "Virtus Bologna", "Virtus Arena", 9980, 6, "Bologna", "Virtus"),
        R("Venice", "Reyer Venezia", "VEN", "Reyer Venezia", "Palasport Taliercio", 3506, 2, "Venice", "Lagoon"),
        R("Sassari", "Dinamo Sassari", "DSS", "Dinamo Sassari", "PalaSerradimigni", 5000, 1, "Sassari", "Dinamo"),
        R("Brescia", "Brescia", "BSA", "Pallacanestro Brescia", "PalaLeonessa", 5200, 2, "Brescia", "Leonessa"),
        R("Tortona", "Derthona", "DER", "Derthona Basket", "PalaFerraris", 3510, 1, "Tortona", "Derthona"),
        R("Trento", "Trento", "TRE", "Aquila Basket Trento", "PalaTrento", 4360, 1, "Trento", "Aquila"),
        R("Varese", "Varese", "VAR", "Pallacanestro Varese", "Palasport Lino Oldrini", 5107, -1, "Varese", "Lakesiders"),
        R("Reggio Emilia", "Reggiana", "REG", "Pallacanestro Reggiana", "PalaBigi", 4530, 0, "Reggio Emilia", "Reggiani"),
        R("Udine", "Udine", "UDI", "APU Udine", "PalaCarnera", 3850, -2, "Pesaro", "Adriatic"),
        R("Treviso", "Treviso", "TRV", "Treviso Basket", "PalaVerde", 5134, -2, "Treviso", "Lions"),
        R("Naples", "Napoli", "NAP", "Napoli Basket", "PalaBarbuto", 5500, -2, "Napoli", "Partenopei"),
        R("Cremona", "Cremona", "CRE", "Vanoli Cremona", "PalaRadi", 3519, -3, "Cremona", "Vanoli"),
        R("Trieste", "Trieste", "TRI", "Pallacanestro Trieste", "PalaTrieste", 6943, -1, "Pistoia", "Tuscans"),
        R("Trapani", "Trapani", "TPS", "Trapani Shark", "Palallio", 4575, -2, "Scafati", "Giants"),
        R("Cantù", "Cantù", "CNT", "Pallacanestro Cantù", "PalaDesio", 6700, -2, "Cantu", "Brianza"),
    };

    /// <summary>LNB Élite (16).</summary>
    public static readonly ClubSeed[] France =
    {
        R("Paris", "Paris Basketball", "PBK", "Paris Basketball", "Adidas Arena", 8000, 5, "Paris", "Seine"),
        R("Villeurbanne", "ASVEL", "ASV", "ASVEL", "Astroballe", 5556, 4, "Villeurbanne", "Lyon Lions"),
        R("Monaco", "Monaco", "MCO", "AS Monaco", "Salle Gaston Médecin", 5000, 6, "Monaco", "Princes"),
        R("Boulazac", "Boulazac", "BBD", "Boulazac Basket Dordogne", "Le Palio", 5200, -3, "Pau", "Bearnais"),
        R("Strasbourg", "Strasbourg", "SIG", "SIG Strasbourg", "Rhénus Sport", 6200, 0, "Strasbourg", "Rhinemen"),
        R("Le Mans", "Le Mans", "LMS", "Le Mans Sarthe", "Antarès", 6023, 0, "Le Mans", "Racers"),
        R("Nanterre", "Nanterre 92", "NAN", "Nanterre 92", "Palais des Sports Maurice Thorez", 3000, -1, "Nanterre", "Nanterriens"),
        R("Dijon", "Dijon", "JDA", "JDA Dijon", "Palais des Sports Jean-Michel Geoffroy", 5000, 1, "Dijon", "Dukes"),
        R("Bourg-en-Bresse", "Bourg", "JLB", "JL Bourg", "Ekinox", 3548, 1, "Bourg", "Bressans"),
        R("Cholet", "Cholet", "CHO", "Cholet Basket", "La Meilleraie", 5191, -1, "Cholet", "Choletais"),
        R("Limoges", "Limoges", "CSP", "Limoges CSP", "Palais des Sports de Beaublanc", 6506, 0, "Limoges", "Porcelain"),
        R("Gravelines", "Gravelines-Dunkerque", "BCM", "BCM Gravelines-Dunkerque", "Stade des Flandres", 2500, -2, "Gravelines", "Dunkirk"),
        R("Le Portel", "Le Portel", "LPO", "ESSM Le Portel", "Le Chaudron", 3500, -3, "Le Portel", "Lighthouse"),
        R("Chalon-sur-Saône", "Chalon", "ELC", "Élan Chalon", "Le Colisée", 4540, -2, "Chalon", "Saone"),
        R("Nancy", "Nancy", "NCY", "SLUC Nancy", "Palais des Sports Jean Weille", 6027, -2, "Nancy", "Lorraine"),
        R("Saint-Quentin", "Saint-Quentin", "SQB", "Saint-Quentin Basket", "Palais des Sports Pierre Ratte", 3800, -2, "Roanne", "Chorale"),
    };

    /// <summary>Basketball Bundesliga (18).</summary>
    public static readonly ClubSeed[] Germany =
    {
        R("Berlin", "Alba Berlin", "ABB", "Alba Berlin", "Uber Arena", 14500, 5, "Berlin", "Albatrosses"),
        R("Munich", "Bayern Munich", "FCB", "Bayern Munich", "BMW Park", 6700, 6, "Munich", "Bavarians"),
        R("Bamberg", "Bamberg Baskets", "BAM", "Bamberg Baskets", "Brose Arena", 6150, -1, "Bamberg", "Brewers"),
        R("Ulm", "Ulm", "RUL", "ratiopharm Ulm", "ratiopharm arena", 6000, 2, "Ulm", "Minster"),
        R("Ludwigsburg", "Ludwigsburg", "LUD", "Riesen Ludwigsburg", "MHP-Arena", 5300, 1, "Ludwigsburg", "Riesen"),
        R("Oldenburg", "Oldenburg", "OLD", "Baskets Oldenburg", "Große EWE Arena", 6069, 0, "Oldenburg", "Baskets"),
        R("Bonn", "Bonn", "BON", "Telekom Baskets Bonn", "Telekom Dome", 6000, 1, "Bonn", "Telekom"),
        R("Hamburg", "Towers", "HAM", null, "Inselpark Arena", 3400, -1, "Hamburg", "Towers"),
        R("Frankfurt", "Skyliners", "FRA", "Skyliners Frankfurt", "Süwag Energie Arena", 5002, -2, "Frankfurt", "Skyliners"),
        R("Würzburg", "Würzburg Baskets", "WUR", "Würzburg Baskets", "Tectake Arena", 3140, 0, "Wurzburg", "Main"),
        R("Chemnitz", "Niners", "CHE", "Niners Chemnitz", "Chemnitz Arena", 5200, 0, "Chemnitz", "Niners"),
        R("Braunschweig", "Löwen", "BRA", "Löwen Braunschweig", "Volkswagen Halle", 6600, -2, "Braunschweig", "Lions"),
        R("Jena", "Jena", "JEN", "Science City Jena", "Sparkassen-Arena", 3000, -3, "Gottingen", "Veilchen"),
        R("Rostock", "Seawolves", "ROS", null, "Stadthalle Rostock", 4550, -1, "Rostock", "Seawolves"),
        R("Heidelberg", "Academics", "HEI", "Academics Heidelberg", "SNP Dome", 5000, -2, "Heidelberg", "Academics"),
        R("Weissenfels", "MBC", "MBC", "Mitteldeutscher BC", "Stadthalle Weissenfels", 3000, -3, "Weissenfels", "Wolves"),
        R("Trier", "Gladiators", "TRR", "Gladiators Trier", "SWT-Arena", 5495, -3, "Bayreuth", "Festival"),
        R("Vechta", "Rasta", "VEC", "Rasta Vechta", "Rasta Dome", 3140, -1, "Vechta", "Rasta"),
    };

    /// <summary>LKL (10: the real 9 and Wolves, an LKL club until recently).</summary>
    public static readonly ClubSeed[] Lithuania =
    {
        R("Kaunas", "Zalgiris", "ZAL", "Zalgiris Kaunas", "Zalgirio Arena", 15415, 8, "Kaunas", "Green Knights"),
        R("Vilnius", "Rytas", "RYT", "Rytas Vilnius", "Arena Vilnius", 10000, 4, "Vilnius", "Gediminas"),
        R("Klaipeda", "Neptunas", "NEP", "Neptunas Klaipeda", "Svyturys Arena", 6200, 1, "Klaipeda", "Mariners"),
        R("Panevezys", "Lietkabelis", "LKB", "Lietkabelis", "Kalnapilis Arena", 5950, 1, "Panevezys", "Ravens"),
        R("Siauliai", "Siauliai", "SIA", "BC Siauliai", "Siauliai Arena", 5700, -1, "Siauliai", "Sun City"),
        R("Gargzdai", "Gargzdai", "GAR", "BC Gargzdai", "Gargzdai Arena", 1400, -2, "Alytus", "Dzukai"),
        R("Utena", "Juventus", "UTE", "Juventus Utena", "Utena Arena", 2500, -1, "Utena", "Lakers"),
        R("Vilnius", "Wolves", "WLV", "Wolves Vilnius", "Active Vilnius Arena", 2500, -1, "Marijampole", "Sudovians"),
        R("Jonava", "Jonava", "JON", "BC Jonava", "Jonava Arena", 2200, -2, "Jonava", "Neris"),
        R("Kedainiai", "Nevezis", "KED", "Nevezis Kedainiai", "Kedainiai Arena", 2200, -2, "Kedainiai", "Nevezis"),
    };

    private static Dictionary<string, ClubSeed>? _byName, _byWas;
    private static Dictionary<string, ClubSeed> ByName => _byName ??= Seeds().GroupBy(s => Key(s.City, s.Name)).ToDictionary(g => g.Key, g => g.First());
    private static Dictionary<string, ClubSeed> ByWas => _byWas ??= Seeds().Where(s => s.Was != null).GroupBy(s => s.Was!).ToDictionary(g => g.Key, g => g.First());

    /// <summary>Every real club abroad.</summary>
    public static IEnumerable<ClubSeed> Seeds() => Spain.Concat(Turkey).Concat(Greece).Concat(Italy).Concat(France).Concat(Germany).Concat(Lithuania).Concat(Slice2()).Concat(Slice3()).Concat(Counts());

    private static string Key(string city, string name) => city + "|" + name;

    /// <summary>Made-up names run 107 (H79) replaced with other made-up names, which the real clubs replaced in turn.</summary>
    private static readonly Dictionary<string, string> Older = new()
    {
        ["Munich|Lions"] = "Munich|Bavarians", ["Yokohama|Bay Stars"] = "Yokohama|B-Corsairs",
        ["Busan|Pegasus"] = "Busan|Egis", ["Batangas|Beer Men"] = "Batangas|Bossing",
    };

    /// <summary>The real club a team is, if it is one.</summary>
    public static ClubSeed? Of(string city, string name) => ByName.TryGetValue(Key(city, name), out var s) ? s : null;
    public static ClubSeed? Of(Team t) => Of(t.City, t.Name);

    /// <summary>The club's name as it is written ("FC Barcelona", "Zalgiris Kaunas"), or null when "City Name" already reads right.</summary>
    public static string? FullOf(string city, string name) => Of(city, name)?.Full;

    /// <summary>The made-up name a real club replaced ("Madrid Capitales"), so name and logo packs written for it still match.</summary>
    public static string? FormerFullName(Team t) => Of(t)?.Was?.Replace('|', ' ');

    /// <summary>The club's arena ("Palau Blaugrana"), if it's a real club abroad.</summary>
    public static string? ArenaOf(Team t) => Of(t)?.Arena;

    /// <summary>
    /// An older save's made-up clubs become the real ones (rename on load, as D44 did for the pyramid): the city, the name and
    /// the arena's real size. The rosters stay as they are. The abbreviation changes too unless another club already has it.
    /// Returns how many clubs were renamed (none for a world that is already real).
    /// </summary>
    public static int Rename(GameWorld world, IEnumerable<Team>? others = null)
    {
        int n = 0;
        var used = new HashSet<string>(world.Clubs.Select(c => c.Abbreviation).Concat((others ?? Enumerable.Empty<Team>()).Select(t => t.Abbreviation)));
        foreach (var comp in world.Competitions.Where(c => c.Profile.Kind == CompetitionKind.Domestic && c.Profile.Clubs.Any(s => s.Was != null)))
        {
            // Already real (a new world, or a save renamed before): leave it. A made-up name can equal another real club's
            // ("Thessaloniki Iraklis" was Aris's slot), so a league is renamed all at once, by its old names, or not at all.
            // Review (run 217): a name pack may have renamed some of a real league's clubs, so "not all real" doesn't mean
            // "made up". A league is an older save's only while it still holds a purely made-up name (one that was replaced
            // and isn't a real club's too): then every club is renamed by its old name, once.
            var real = comp.Profile.Clubs.Select(s => Key(s.City, s.Name)).ToHashSet();
            var was = comp.Profile.Clubs.Where(s => s.Was != null).Select(s => s.Was!).ToHashSet();
            if (!comp.Clubs.Any(t => (was.Contains(Key(t.City, t.Name)) || Older.ContainsKey(Key(t.City, t.Name))) && !real.Contains(Key(t.City, t.Name)))) continue;
            // Review (run 218): a league that is already partly real (a pre-run-107 name left in a save renamed by v0.205) is
            // renamed only where a club still holds a made-up name; a club already under its real name keeps its arena size.
            bool fresh = !comp.Clubs.Any(t => real.Contains(Key(t.City, t.Name)) && !was.Contains(Key(t.City, t.Name)));
            foreach (var team in comp.Clubs)
            {
                string k = Key(team.City, team.Name);
                if (real.Contains(k) && (!was.Contains(k) || !fresh)) continue;
                Rename(team, comp, used, ref n);
            }
        }
        return n;
    }

    private static void Rename(Team team, WorldCompetition comp, HashSet<string> used, ref int n)
    {
        {
            // Run 218: a save from before run 107 still holds the made-up names that run renamed ("Busan Pegasus").
            string was = Key(team.City, team.Name);
            if (Older.TryGetValue(was, out var later)) was = later;
            if (!ByWas.TryGetValue(was, out var real) || !comp.Profile.Clubs.Contains(real)) return;
            team.City = real.City; team.Name = real.Name;
            if (real.Capacity > 0) team.ArenaCapacity = real.Capacity;
            if (team.Abbreviation != real.Abbr && used.Add(real.Abbr)) team.Abbreviation = real.Abbr;
            n++;
        }
    }
}
