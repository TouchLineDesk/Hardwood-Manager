using Hardwood.Core.Model;

namespace Hardwood.Core.World;

/// <summary>
/// K5, slice 3 (run 219; Danny's note `danny-world-leagues-nov26`): the real clubs of Poland, the Latvian-Estonian league,
/// Czechia, the BNXT League, Argentina, Brazil, Puerto Rico, Mexico and the Basketball Africa League, the same way as slices 1
/// and 2: each takes a made-up club's slot (same draws; the slot keeps its country), with its city, arena and capacity where the
/// source gives one (a null arena and 0 leave them drawn, as before). Rosters stay generated.
/// <para>Sources (English Wikipedia, read on Nov 27, 2026): the 2025-26 PLK, Latvian-Estonian Basketball League and BNXT League
/// season pages; the Liga Nacional de Básquetbol, Novo Basquete Brasil and Liga Nacional de Baloncesto Profesional pages; the
/// 2026 Baloncesto Superior Nacional and 2026 BAL season pages; the Czech National Basketball League page (club names only: it
/// gives no arenas, so the Czech arenas stay drawn).</para>
/// <para>Where the game has fewer places than the real league, the left-out clubs are: Poland, Gornik Walbrzych and Miasto Szkla
/// Krosno; the Baltic, Latvijas Universitate and Keila KK; Czechia, Plzen, Pisek and Kolin; the BNXT, Kortrijk, Limburg, Aalst
/// and BAL Weert; Argentina, Racing Chivilcoy; Brazil, Pato and Uniao Corinthians; Mexico, Correbasket UAT and Gambusinos. The
/// BAL keeps its slots' countries: where the 2026 field has no club from a slot's country, a recent member fills it (US Monastir,
/// Al Ittihad Alexandria, Rivers Hoopers, FAP), and Bamako and Kinshasa, with no recent member, keep their made-up clubs.
/// Libya, Kenya and Tanzania's 2026 entrants have no slot. (Review: the BAL's made-up seeds make <c>Rename</c>'s
/// "fresh" test false there, so a future BAL club whose key equals its own <c>Was</c> would not get its capacity on load.) Letters outside Latin-1 are written in plain Latin (Slask, Riga).</para>
/// </summary>
public static partial class RealClubs
{
    /// <summary>The PLK (14 of the real 16).</summary>
    public static readonly ClubSeed[] Poland =
    {
        R("Warsaw", "Legia", "WAR", "Legia Warsaw", "OSiR Bemowo", 1991, 3, "Warsaw", "Legion", "Poland"),
        R("Wroclaw", "Slask", "WRO", "Slask Wroclaw", "Hala Orbita", 3000, 3, "Wroclaw", "Silesians", "Poland"),
        R("Gdynia", "Arka", "GDY", "Arka Gdynia", "Gdynia Sports Arena", 4010, 0, "Gdynia", "Seahawks", "Poland"),
        R("Szczecin", "King", "SZC", "King Szczecin", "Enea Arena", 5055, 3, "Szczecin", "Kings", "Poland"),
        R("Zielona Gora", "Zastal", "ZIE", "Zastal Zielona Gora", "CRS Hall", 5080, 0, "Zielona Gora", "Vineyard", "Poland"),
        R("Lublin", "Start", "LBN", "Start Lublin", "Hala Globus", 4221, 2, "Lublin", "Starlings", "Poland"),
        R("Wloclawek", "Anwil", "WLO", "Anwil Wloclawek", "Hala Mistrzów", 3963, 2, "Wloclawek", "Anwil", "Poland"),
        R("Ostrow Wielkopolski", "Stal", "OST", "Stal Ostrow Wielkopolski", "Arena Ostrów", 3086, 1, "Ostrow", "Stallions", "Poland"),
        R("Sopot", "Trefl", "SOP", "Trefl Sopot", "Ergo Arena", 11409, 4, "Sopot", "Pier", "Poland"),
        R("Torun", "Twarde Pierniki", "TRN", "Twarde Pierniki Torun", "Kujawsko-Pomorska Arena", 6248, 0, "Torun", "Gingerbread", "Poland"),
        R("Gliwice", "GTK", "GLI", "GTK Gliwice", "Gliwice Arena", 3360, -3, "Gliwice", "Miners", "Poland"),
        R("Warsaw", "Dziki", "DZI", "Dziki Warsaw", "Hala Kolo", 1298, 0, "Krakow", "Wawel", "Poland"),
        R("Slupsk", "Czarni", "CZS", "Czarni Slupsk", "Hala Gryfia", 2249, -2, "Stargard", "Spojnia", "Poland"),
        R("Dabrowa Gornicza", "MKS", "MKS", "MKS Dabrowa Gornicza", "Centrum Hall", 2944, -2, "Dabrowa", "Furnace", "Poland"),
    };

    /// <summary>The Latvian-Estonian Basketball League (12 of the real 14: six Latvian clubs and six Estonian).</summary>
    public static readonly ClubSeed[] Baltic =
    {
        R("Riga", "VEF", "RIG", "VEF Riga", "Rimi Olympic Sports Center", 830, 3, "Riga", "Vef", "Latvia"),
        R("Riga", "Zelli", "RDA", "Rigas Zelli", "Latvian Team Sports Hall", 3000, 2, "Riga", "Daugava", "Latvia"),
        R("Ventspils", "Ventspils", "VSP", "BK Ventspils", "Ventspils Olympic Center", 3085, 1, "Ventspils", "Port", "Latvia"),
        R("Liepaja", "Liepaja", "LIE", "BK Liepaja", "Liepaja Olympic Center", 2542, -1, "Liepaja", "Wind", "Latvia"),
        R("Valmiera", "Valmiera Glass VIA", "VLM", "Valmiera Glass VIA", "Valmiera Olympic Center", 1500, -1, "Valmiera", "Glass", "Latvia"),
        R("Ogre", "Ogre", "OGR", "BK Ogre", "Arena Ogre", 1700, -2, "Jelgava", "Zemgale", "Latvia"),
        R("Tallinn", "Kalev", "TLL", "Kalev/Cramo", "Tondiraba Sports Center", 7700, 3, "Tallinn", "Kalev", "Estonia"),
        R("Tartu", "Tartu Ülikool", "TAT", "Tartu Ülikool", "University of Tartu Sports Hall", 2600, 1, "Tartu", "Rock", "Estonia"),
        R("Pärnu", "Pärnu", "PRN", "Transcom Pärnu", "Pärnu Sports Hall", 1820, 0, "Parnu", "Seaside", "Estonia"),
        R("Tallinn", "TalTech", "TTE", "TalTech/ALEXELA", "TalTech Sports Hall", 1000, -1, "Rakvere", "Tarvas", "Estonia"),
        R("Viimsi", "Viimsi", "VII", "BC Viimsi", "Forus Sports Center", 500, -2, "Viimsi", "Coast", "Estonia"),
        R("Keila", "Coolbet", "KEI", null, "Keila Health Center", 800, -2, "Keila", "Coolbet", "Estonia"),
    };

    /// <summary>The Czech National Basketball League (10 of the real 13). The source gives no arenas, so they stay drawn.</summary>
    public static readonly ClubSeed[] Czechia =
    {
        R("Nymburk", "Nymburk", "NYM", "Basketball Nymburk", null, 0, 6, "Nymburk", "Elbe", "Czechia"),
        R("Prague", "USK", "USK", "USK Praha", null, 0, 0, "Prague", "Vltava", "Czechia"),
        R("Brno", "Brno", "BRN", "Basket Brno", null, 0, -1, "Brno", "Moravians", "Czechia"),
        R("Opava", "Opava", "OPA", "BK Opava", null, 0, 3, "Opava", "Silesians", "Czechia"),
        R("Pardubice", "Pardubice", "PCE", "BK Pardubice", null, 0, 1, "Pardubice", "Gingerbread", "Czechia"),
        R("Ostrava", "NH Ostrava", "OSV", "NH Ostrava", null, 0, 0, "Ostrava", "Steel", "Czechia"),
        R("Hradec Králové", "Hradec Králové", "HKR", "GAPA Hradec Králové", null, 0, -2, "Kolin", "Royals", "Czechia"),
        R("Decin", "Decin", "DCN", "BK Decin", null, 0, 0, "Decin", "Warriors", "Czechia"),
        R("Usti nad Labem", "Sluneta", "UST", "Sluneta Usti nad Labem", null, 0, -2, "Usti", "Elbe Gate", "Czechia"),
        R("Olomouc", "Olomoucko", "OLO", "BK Olomoucko", null, 0, -1, "Olomouc", "Hanaks", "Czechia"),
    };

    /// <summary>The BNXT League (14 of the real 18: seven Belgian clubs and seven Dutch).</summary>
    public static readonly ClubSeed[] Benelux =
    {
        R("Ostend", "Oostende", "OSN", "Filou Oostende", "Coretec Dôme", 5000, 3, "Ostend", "Seagulls", "Belgium"),
        R("Antwerp", "Giants", "ANT", null, "Lotto Arena", 5218, 2, "Antwerp", "Giants", "Belgium"),
        R("Mons", "Mons-Hainaut", "MON", "Union Mons-Hainaut", "Mons Arena", 4000, 0, "Mons", "Hainaut", "Belgium"),
        R("Leuven", "Bears", "LEU", null, "Sportoase", 3400, 0, "Leuven", "Bears", "Belgium"),
        R("Brussels", "Brussels Basketball", "BRU", "Brussels Basketball", "Neder-Over-Heembeek Sports Complex", 1200, -2, "Brussels", "Capitals", "Belgium"),
        R("Charleroi", "Spirou", "SPI", "Spirou Charleroi", "Spiroudome", 6200, 0, "Liege", "Meuse", "Belgium"),
        R("Mechelen", "Kangoeroes", "MEC", "Kangoeroes Mechelen", "Winketkaai", 1500, -1, "Mechelen", "Kangaroos", "Belgium"),
        R("Groningen", "Donar", "GRN", "Donar Groningen", "MartiniPlaza", 4350, 2, "Groningen", "Donar", "Netherlands"),
        R("Leiden", "ZZ Leiden", "LEI", "ZZ Leiden", "Vijf Meihal", 2000, 1, "Leiden", "Keys", "Netherlands"),
        R("'s-Hertogenbosch", "Heroes", "DBO", "Heroes Den Bosch", "Maaspoort", 2800, 2, "Den Bosch", "Dragons", "Netherlands"),
        R("Leeuwarden", "LWD Basket", "LWD", "LWD Basket", "Kalverdijkje", 1700, -2, "Amsterdam", "Canals", "Netherlands"),
        R("Rotterdam", "City", "ROT", "Rotterdam City Basketball", "Topsportcentrum Rotterdam", 2500, -1, "Rotterdam", "Port", "Netherlands"),
        R("Zwolle", "Landstede Hammers", "ZWO", "Landstede Hammers", "Landstede Sportcentrum", 1200, 0, "Zwolle", "Landstede", "Netherlands"),
        R("Den Helder", "Suns", "DHE", null, "Sporthal Quelderduijn", 1250, -2, "Den Helder", "Suns", "Netherlands"),
    };

    /// <summary>The Liga Nacional de Básquet (18 of the real 19).</summary>
    public static readonly ClubSeed[] Argentina =
    {
        R("Buenos Aires", "Ferro", "BUE", "Ferro Carril Oeste", "Héctor Etchart", 4500, 0, "Buenos Aires", "Porteños", "Argentina"),
        R("Buenos Aires", "Boca Juniors", "BBQ", "Boca Juniors", "Luis Conde", 2000, 3, "Buenos Aires", "Boquenses", "Argentina"),
        R("Junín", "Argentino", "JUN", "Argentino de Junín", "El Fortín de las Morochas", 1465, -2, "Bahia Blanca", "Southerners", "Argentina"),
        R("Córdoba", "Atenas", "ATE", "Atenas de Córdoba", "Carlos Cerutti", 3500, 1, "Cordoba", "Atenas", "Argentina"),
        R("Mar del Plata", "Peñarol", "MDP", "Peñarol de Mar del Plata", "Islas Malvinas", 8000, 1, "Mar del Plata", "Peñarol", "Argentina"),
        R("Buenos Aires", "San Lorenzo", "SLO", "San Lorenzo", "Roberto Pando", 2700, 2, "San Lorenzo", "Almagro", "Argentina"),
        R("Florida", "Platense", "OBR", "Platense", "Microestadio Vicente López", 800, -2, "Obras", "Sanitarias", "Argentina"),
        R("Santiago del Estero", "Quimsa", "QUI", "Quimsa", "Estadio Ciudad", 5200, 4, "Quimsa", "Santiago", "Argentina"),
        R("Santa Fe", "Unión", "RSR", "Unión de Santa Fe", "Ángel Malvicino", 5000, 0, "Rosario", "Lepers", "Argentina"),
        R("Oliva", "Independiente", "MDZ", "Independiente de Oliva", "El Gigante", 1800, -2, "Mendoza", "Vintners", "Argentina"),
        R("Comodoro Rivadavia", "Gimnasia", "CMD", "Gimnasia y Esgrima", "Socios Fundadores", 2276, 1, "Comodoro", "Gimnasia", "Argentina"),
        R("Corrientes", "Regatas", "CRR", "Regatas Corrientes", "José Jorge Contte", 4000, 2, "Corrientes", "Regatas", "Argentina"),
        R("Lanús", "Lanús", "LAN", "Lanús", "Microestadio Antonio Rotili", 3000, -1, "La Plata", "Platenses", "Argentina"),
        R("La Banda", "Olímpico", "SAL", "Ciclista Olímpico", "Vicente Rosales", 3964, -1, "Salta", "Andeans", "Argentina"),
        R("Oberá", "Oberá Tenis Club", "OTC", "Oberá Tenis Club", "Dr. Luis Augusto Derna", 2000, -2, "Tucuman", "Garden", "Argentina"),
        R("Corrientes", "San Martín", "PRA", "San Martín de Corrientes", "Raúl A. Ortiz", 2500, -1, "Parana", "Echague", "Argentina"),
        R("Córdoba", "Instituto", "OLA", "Instituto de Córdoba", "Angel Sandrin", 2000, 3, "Olavarria", "Estudiantes", "Argentina"),
        R("Formosa", "La Unión", "ZRT", "La Unión de Formosa", "Cincuentenario", 4500, -1, "Zarate", "Basket", "Argentina"),
    };

    /// <summary>The NBB (16 of the real 18).</summary>
    public static readonly ClubSeed[] Brazil =
    {
        R("São Paulo", "São Paulo", "SPF", "São Paulo FC", "Ginásio do Morumbi", 1918, 2, "Sao Paulo", "Tricolor", "Brazil"),
        R("Rio de Janeiro", "Flamengo", "RIO", "Flamengo", "Maracanãzinho", 11800, 5, "Rio de Janeiro", "Flamingos", "Brazil"),
        R("Franca", "Franca", "FRN", "Franca Basquete", "Ginásio Pedrocão", 6000, 4, "Franca", "Coffee", "Brazil"),
        R("Brasília", "Brasília", "BSB", "Brasília Basquete", "Nilson Nelson Gymnasium", 11397, 0, "Brasilia", "Capital", "Brazil"),
        R("Bauru", "Bauru", "BAU", "Bauru Basket", "Ginásio Panela de Pressão", 2000, 1, "Bauru", "Dragons", "Brazil"),
        R("Belo Horizonte", "Minas", "MTC", "Minas Tênis Clube", "Juscelino Kubitschek Arena", 4000, 2, "Minas", "Tennis", "Brazil"),
        R("Mogi das Cruzes", "Mogi", "MOG", "Mogi Basquete", "Ginásio Professor Hugo Ramos", 5000, 0, "Mogi", "Cruzes", "Brazil"),
        R("São Paulo", "Pinheiros", "PIN", "Esporte Clube Pinheiros", "Poliesportivo Henrique Villaboim", 850, 0, "Pinheiros", "Pines", "Brazil"),
        R("Fortaleza", "Fortaleza", "FBC", "Fortaleza Basquete Cearense", "Centro de Formação Olímpica", 17100, 0, "Fortaleza", "Sharks", "Brazil"),
        R("Campina Grande", "Unifacisa", "UFS", "Unifacisa", "Arena Unifacisa", 1200, -1, "Unifacisa", "Paraiba", "Brazil"),
        R("São Paulo", "Paulistano", "PAO", "Club Athletico Paulistano", "Ginásio Antônio Prado Junior", 1280, -1, "Paulistano", "Athletics", "Brazil"),
        R("São Paulo", "Corinthians", "CTH", "Corinthians", "Ginásio Wlamir Marques", 6500, 0, "Corinthians", "Timao", "Brazil"),
        R("Rio de Janeiro", "Botafogo", "BOT", "Botafogo", "Ginásio Oscar Zelaya", 1500, -2, "Botafogo", "Lone Star", "Brazil"),
        R("Rio de Janeiro", "Vasco", "VAS", "Vasco da Gama", "Ginásio Vasco da Gama", 1000, -1, "Vasco", "Admirals", "Brazil"),
        R("Caxias do Sul", "Caxias do Sul", "CAX", "Caxias do Sul Basquete", "Ginásio do SESI", 4500, -2, "Caxias", "Gauchos", "Brazil"),
        R("São José dos Campos", "São José", "SJO", "São José Basketball", "Ginásio Lineu de Moura", 2620, -2, "Sao Jose", "Valley", "Brazil"),
    };

    /// <summary>The BSN (all 12).</summary>
    public static readonly ClubSeed[] PuertoRico =
    {
        R("Santurce", "Cangrejeros", "CGS", "Cangrejeros de Santurce", "Roberto Clemente Coliseum", 9000, 3, "San Juan", "Capitanes", "Puerto Rico"),
        R("Bayamón", "Vaqueros", "BYM", "Vaqueros de Bayamón", "Ruben Rodriguez Coliseum", 12000, 3, "Bayamon", "Cowboys", "Puerto Rico"),
        R("Ponce", "Leones", "PON", "Leones de Ponce", "Juan Pachín Vicéns Auditorium", 11000, 1, "Ponce", "Lions", "Puerto Rico"),
        R("Mayagüez", "Indios", "MAY", "Indios de Mayagüez", "Palacio de Recreación y Deportes", 5500, 0, "Mayaguez", "Indians", "Puerto Rico"),
        R("Arecibo", "Capitanes", "ARE", "Capitanes de Arecibo", "Manuel Iguina Coliseum", 12000, 2, "Arecibo", "Captains", "Puerto Rico"),
        R("Carolina", "Gigantes", "CAR", "Gigantes de Carolina", "Guillermo Angulo Coliseum", 5000, -1, "Carolina", "Giants", "Puerto Rico"),
        R("Caguas", "Criollos", "CAG", "Criollos de Caguas", "Coliseo Roger Mendoza", 3000, 0, "Caguas", "Creoles", "Puerto Rico"),
        R("Guaynabo", "Mets", "GUA", "Mets de Guaynabo", "Mario Morales Coliseum", 5500, 0, "Guaynabo", "Mets", "Puerto Rico"),
        R("Quebradillas", "Piratas", "QBR", "Piratas de Quebradillas", "Raymond Dalmau Coliseum", 5500, -1, "Quebradillas", "Pirates", "Puerto Rico"),
        R("Manatí", "Osos", "OSO", "Osos de Manatí", "Juan Cruz Abreu Coliseum", 8000, 0, "Manati", "Athenians", "Puerto Rico"),
        R("Aguada", "Santeros", "AGU", "Santeros de Aguada", "Ismael Delgado Coliseum", 6000, 1, "Aguada", "Sailors", "Puerto Rico"),
        R("San Germán", "Atléticos", "SGE", "Atléticos de San Germán", "Arquelio Torres Ramírez Coliseum", 5000, -2, "Humacao", "Grays", "Puerto Rico"),
    };

    /// <summary>The LNBP (12 of the real 14).</summary>
    public static readonly ClubSeed[] Mexico =
    {
        R("Mexico City", "Diablos Rojos", "MXC", "Diablos Rojos del México", "Gimnasio Juan de la Barrera", 5242, 2, "Mexico City", "Capitanes", "Mexico"),
        R("Monterrey", "Fuerza Regia", "MTY", "Fuerza Regia de Monterrey", "Arena Mobil", 5000, 3, "Monterrey", "Fuerza", "Mexico"),
        R("Guadalajara", "Astros", "GDL", "Astros de Jalisco", "Arena Astros", 4000, 2, "Guadalajara", "Astros", "Mexico"),
        R("Zacatecas", "Mineros", "ZAC", "Mineros de Zacatecas", "Gimnasio Marcelino González", 3458, 0, "Tijuana", "Zonkeys", "Mexico"),
        R("Chihuahua", "Dorados", "DOR", "Dorados de Chihuahua", "Gimnasio Manuel Bernardo Aguirre", 9600, 0, "Chihuahua", "Dorados", "Mexico"),
        R("Puebla", "Lobos", "PUE", "Lobos de Puebla", "Gimnasio Miguel Hidalgo", 4000, -1, "Puebla", "Angels", "Mexico"),
        R("Cancún", "El Calor", "CUN", "El Calor de Cancún", "Polifórum Benito Juárez", 4800, -1, "Merida", "Mayans", "Mexico"),
        R("León", "Abejas", "LEO", "Abejas de León", "Domo de la Feria", 4463, 0, "Leon", "Lions", "Mexico"),
        R("San Luis Potosí", "Santos", "SLP", "Santos del Potosí", "Auditorio Miguel Barragán", 3400, 1, "Hermosillo", "Suns", "Mexico"),
        R("Mexicali", "Soles", "MXL", "Soles de Mexicali", "Auditorio PSF", 4779, 1, "Mexicali", "Soles", "Mexico"),
        R("Irapuato", "Freseros", "IRP", "Freseros de Irapuato", "Inforum Irapuato", 3000, -2, "Queretaro", "Libertadores", "Mexico"),
        R("Aguascalientes", "Panteras", "AGS", "Panteras de Aguascalientes", "Auditorio Hermanos Carreón", 3000, -1, "Veracruz", "Halcones", "Mexico"),
    };

    /// <summary>
    /// The BAL (12 of its 14 slots real: the 2026 field where its country has a slot, recent members where it doesn't). The source
    /// gives no arenas, so they stay drawn. Bamako and Kinshasa keep their made-up clubs (<see cref="Africa"/> leaves them as they were).
    /// </summary>
    public static readonly ClubSeed[] Africa =
    {
        R("Dakar", "Ville de Dakar", "DKR", "ASC Ville de Dakar", null, 0, 1, "Dakar", "Lions", "Senegal"),
        R("Port Harcourt", "Rivers Hoopers", "RIV", "Rivers Hoopers", null, 0, 0, "Lagos", "Rivers Hoopers", "Nigeria"),
        R("Cairo", "Al Ahly", "CAI", "Al Ahly", null, 0, 4, "Cairo", "Pharaohs", "Egypt"),
        R("Alexandria", "Al Ittihad", "ALX", "Al Ittihad Alexandria", null, 0, 1, "Alexandria", "Ittihad", "Egypt"),
        R("Tunis", "Club Africain", "TUN", "Club Africain", null, 0, 2, "Tunis", "Esperance", "Tunisia"),
        R("Monastir", "US Monastir", "MNT", "US Monastir", null, 0, 3, "Monastir", "Ribat", "Tunisia"),
        R("Luanda", "Petro de Luanda", "LUA", "Petro de Luanda", null, 0, 4, "Luanda", "Petro", "Angola"),
        R("Kigali", "RSSB Tigers", "KGL", "RSSB Tigers", null, 0, 0, "Kigali", "Patriots", "Rwanda"),
        R("Rabat", "FUS Rabat", "CAS", "FUS Rabat", null, 0, 2, "Casablanca", "Fus", "Morocco"),
        new("Bamako", "Stade", "BMK", "Mali"),
        R("Yaoundé", "FAP", "YAO", "FAP Yaoundé", null, 0, -1, "Yaounde", "Forces", "Cameroon"),
        R("Johannesburg", "Giants", "JNB", null, null, 0, -1, "Johannesburg", "Cape Tigers", "South Africa"),
        R("Abidjan", "JCA", "ABJ", "Jeunesse Club d'Abidjan", null, 0, 0, "Abidjan", "Elephants", "Ivory Coast"),
        new("Kinshasa", "Leopards", "KIN", "Congo DR"),
    };

    private static IEnumerable<ClubSeed> Slice3() =>
        Poland.Concat(Baltic).Concat(Czechia).Concat(Benelux).Concat(Argentina).Concat(Brazil).Concat(PuertoRico).Concat(Mexico).Concat(Africa.Where(s => s.Was != null));
}
