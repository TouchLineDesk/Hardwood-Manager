using Hardwood.Core.Model;

namespace Hardwood.Core.World;

/// <summary>
/// K5, slice 2 (run 218; Danny's note `danny-world-leagues-nov26`): the real clubs of Australia, China, Japan, Korea, the
/// Philippines, Israel, the ABA League and the VTB United League, the same way as slice 1: each takes a made-up club's slot (same
/// draws, and the slot keeps its country, so the national pools don't move), with its city, arena and capacity where the
/// source gives one (0 leaves the capacity drawn at random, as before). Rosters stay generated.
/// <para>Sources (English Wikipedia, read on Nov 26-27, 2026): the 2025-26 NBL, Israeli Premier League, ABA League and PBA
/// season pages; the CBA, KBL, B.League and VTB United League pages; the lists of indoor arenas in China and Russia and the
/// list of basketball arenas for the CBA and VTB capacities.</para>
/// <para>Where the game has fewer places than the real league, the left-out clubs are: Israel, Hapoel HaEmek and Ironi
/// Ra'anana (the newest); the ABA, Dubai Basketball, Bosna, Ilirija, Spartak Subotica and U-BT Cluj-Napoca (outside the game's
/// six ABA countries, or with no slot for their country); Japan, ten of the 26 B1 clubs; VTB, Avtodor. Where a slot's country
/// has no club in the real league, a recent member fills it: Cibona (Zagreb), MZT Skopje, BC Minsk and BC Astana.
/// PBA clubs are company teams with no home city: the brand stands in the city's place ("San Miguel Beermen"), and all of them
/// play at the league's main venue, the Smart Araneta Coliseum.</para>
/// </summary>
public static partial class RealClubs
{
    private static ClubSeed R(string city, string name, string abbr, string? full, string? arena, int capacity, int standing, string wasCity, string wasName, string? country) =>
        new(city, name, abbr, country) { Full = full, Arena = arena, Capacity = capacity, Standing = standing, Was = $"{wasCity}|{wasName}" };

    /// <summary>The NBL (10).</summary>
    public static readonly ClubSeed[] Australia =
    {
        R("Sydney", "Kings", "SYD", null, "Sydney SuperDome", 18200, 4, "Sydney", "Harbourmen"),
        R("Melbourne", "United", "MEL", "Melbourne United", "John Cain Arena", 10175, 4, "Melbourne", "Laneways"),
        R("South East Melbourne", "Phoenix", "SEM", null, "John Cain Arena", 10175, 2, "Melbourne", "Southern Cross"),
        R("Perth", "Wildcats", "PER", null, "Perth Arena", 14800, 3, "Perth", "Black Swans"),
        R("Adelaide", "36ers", "ADL", null, "Adelaide Entertainment Centre", 11300, 1, "Adelaide", "Vignerons"),
        R("Brisbane", "Bullets", "BNE", null, "Brisbane Entertainment Centre", 10500, -2, "Brisbane", "Rivermen"),
        R("Cairns", "Taipans", "CNS", null, "Cairns Convention Centre", 5300, -2, "Cairns", "Reef"),
        R("Tasmania", "JackJumpers", "TAS", null, "Derwent Entertainment Centre", 4340, 1, "Tasmania", "Tigers"),
        R("Illawarra", "Hawks", "ILW", null, "Wollongong Entertainment Centre", 6000, 2, "Illawarra", "Steelers"),
        R("New Zealand", "Breakers", "NZB", null, "Spark Arena", 9740, 0, "Auckland", "Harbour Hawks"),
    };

    /// <summary>The CBA (20). Capacities the arena lists don't give are left drawn.</summary>
    public static readonly ClubSeed[] China =
    {
        R("Beijing", "Ducks", "BJD", null, "Wukesong Arena", 18000, 3, "Beijing", "Dragons"),
        R("Beijing", "Royal Fighters", "BRF", null, "Beijing Olympic Sports Center Gymnasium", 7000, 0, "Beijing", "Imperials"),
        R("Shanghai", "Sharks", "SHA", null, "Shanghai Indoor Stadium", 13000, 3, "Shanghai", "Tide"),
        R("Guangdong", "Southern Tigers", "GST", null, "Dongguan Basketball Center", 16133, 6, "Guangdong", "Pearl River"),
        R("Shenzhen", "Leopards", "SZN", null, "Shenzhen Gymnasium", 0, 0, "Shenzhen", "Aviators"),
        R("Liaoning", "Flying Leopards", "LIA", null, "Liaoning Gymnasium", 10000, 6, "Liaoning", "Ironmen"),
        R("Xinjiang", "Flying Tigers", "XJT", null, "Urumqi Olympic Sports Center Gymnasium", 12000, 3, "Xinjiang", "Snow Leopards"),
        R("Zhejiang", "Lions", "ZJL", "Zhejiang Guangsha Lions", "Hangzhou Gymnasium", 5136, 3, "Zhejiang", "Silk"),
        R("Shandong", "Kirin", "SDK", "Shandong Hi-Speed Kirin", "Shandong Arena", 8800, 2, "Shandong", "Mountaineers"),
        R("Shanxi", "Loongs", "SXL", null, "Shanxi Sports Centre Gymnasium", 0, 2, "Shanxi", "Loong"),
        R("Jiangsu", "Dragons", "JSU", null, "Suzhou Sports Center Gymnasium", 0, -3, "Jiangsu", "Mariners"),
        R("Sichuan", "Blue Whales", "SCB", null, "Jinqiang International Event Center", 15000, -3, "Sichuan", "Pandas"),
        R("Tianjin", "Pioneers", "TJN", null, "Dongli Gymnasium", 0, -3, "Tianjin", "Pioneers"),
        R("Qingdao", "Eagles", "QDE", null, "Guoxin Gymnasium", 12500, 1, "Qingdao", "Eagles"),
        R("Fujian", "Sturgeons", "FJS", null, "Zuchang Gymnasium", 6000, -2, "Fujian", "Seafarers"),
        R("Guangzhou", "Loong Lions", "GZL", null, "Tianhe Gymnasium", 7600, 0, "Guangzhou", "Rams"),
        R("Nanjing", "Monkey Kings", "NJM", null, "Nanjing Youth Olympic Sports Park Arena", 21000, -1, "Nanjing", "Plum Blossoms"),
        R("Ningbo", "Rockets", "NGB", null, "Youngor Arena", 5000, -3, "Ningbo", "Harbour"),
        R("Jilin", "Northeast Tigers", "JNT", null, "Changchun Gymnasium", 0, -1, "Wuhan", "Cranes"),
        R("Zhejiang", "Golden Bulls", "ZGB", null, "Binjiang Gymnasium", 0, 2, "Chongqing", "Mist"),
    };

    /// <summary>B.League B1 (16 of the 26; SeaHorses Mikawa take Niigata's place, Niigata being in B2 now).</summary>
    public static readonly ClubSeed[] Japan =
    {
        R("Tokyo", "Alvark", "TKY", "Alvark Tokyo", "Toyota Arena Tokyo", 10000, 6, "Tokyo", "Alvarks", "Japan"),
        R("Tokyo", "Sun Rockers", "TKS", null, "Toyota Arena Tokyo", 10000, 2, "Tokyo", "Sunrockers", "Japan"),
        R("Chiba", "Jets", "CHB", null, "LaLa Arena Tokyo-Bay", 11000, 5, "Chiba", "Jets", "Japan"),
        R("Utsunomiya", "Brex", "UTS", null, "Brex Arena Utsunomiya", 2900, 6, "Utsunomiya", "Brex", "Japan"),
        R("Okinawa", "Golden Kings", "OKI", "Ryukyu Golden Kings", "Okinawa Suntory Arena", 10000, 6, "Okinawa", "Kings", "Japan"),
        R("Nagoya", "Diamond Dolphins", "NAG", null, "IG Arena", 17000, 3, "Nagoya", "Diamonds", "Japan"),
        R("Osaka", "Evessa", "OSA", null, "Ookini Arena Maishima", 7056, 0, "Osaka", "Evessa", "Japan"),
        R("Kawasaki", "Brave Thunders", "KAW", null, "Kawasaki Todoroki Arena", 6500, 0, "Kawasaki", "Thunders", "Japan"),
        R("Yokohama", "B-Corsairs", "YOK", null, "Yokohama International Swimming Pool", 5000, 2, "Yokohama", "B-Corsairs", "Japan"),
        R("Matsue", "Susanoo Magic", "SHI", "Shimane Susanoo Magic", "Matsue City General Gymnasium", 4550, 3, "Shimane", "Magic", "Japan"),
        R("Hiroshima", "Dragonflies", "HIR", null, "Hiroshima Sun Plaza", 0, 3, "Hiroshima", "Dragonflies", "Japan"),
        R("Sendai", "89ers", "SEN", null, "Xebio Arena Sendai", 5038, -1, "Sendai", "Eighty Niners", "Japan"),
        R("Sapporo", "Levanga", "HOK", "Levanga Hokkaido", "Hokkai Kitayell", 8000, -2, "Hokkaido", "Levanga", "Japan"),
        R("Otsu", "Lakes", "SHG", "Shiga Lakes", "Shiga Daihatsu Arena", 5000, -1, "Shiga", "Lakes", "Japan"),
        R("Kyoto", "Hannaryz", "KYO", null, "Hannaryz Arena", 4000, -2, "Kyoto", "Hannaryz", "Japan"),
        R("Kariya", "SeaHorses", "MIK", "SeaHorses Mikawa", "Wing Arena Kariya", 2376, 2, "Niigata", "Albirex", "Japan"),
    };

    /// <summary>The KBL (10). The company names are part of the clubs' names.</summary>
    public static readonly ClubSeed[] Korea =
    {
        R("Seoul", "SK Knights", "SEO", null, "Jamsil Students' Gymnasium", 5400, 3, "Seoul", "Knights", "Korea"),
        R("Seoul", "Samsung Thunders", "SST", null, "Jamsil Arena", 11069, -2, "Seoul", "Thunders", "Korea"),
        R("Anyang", "Red Boosters", "ANY", "Anyang Jung Kwan Jang Red Boosters", "Anyang Gymnasium", 6690, 1, "Anyang", "Red Boosters", "Korea"),
        R("Suwon", "KT Sonicboom", "SUW", null, "Suwon KT Sonicboom Arena", 3660, 1, "Suwon", "Sonic Boom", "Korea"),
        R("Ulsan", "Hyundai Mobis Phoebus", "ULS", null, "Dongchun Gymnasium", 5831, 2, "Ulsan", "Phoebus", "Korea"),
        R("Wonju", "DB Promy", "WON", null, "Wonju Gymnasium", 4600, 1, "Wonju", "Promy", "Korea"),
        R("Changwon", "LG Sakers", "CWN", null, "Changwon Gymnasium", 6000, 3, "Changwon", "Sakers", "Korea"),
        R("Busan", "KCC Egis", "BUS", null, "Busan Sajik Gymnasium", 14099, 2, "Busan", "Egis", "Korea"),
        R("Goyang", "Sono Skygunners", "GOY", null, "Goyang Gymnasium", 6216, -2, "Goyang", "Sono", "Korea"),
        R("Daegu", "KOGAS Pegasus", "DAE", null, "Daegu Gymnasium", 3867, 0, "Daegu", "Pegasus", "Korea"),
    };

    private const string Araneta = "Smart Araneta Coliseum";
    private const int AranetaSeats = 14429;

    /// <summary>The PBA (12): the brand in the city's place, all at the Araneta Coliseum.</summary>
    public static readonly ClubSeed[] Philippines =
    {
        R("San Miguel", "Beermen", "SMB", null, Araneta, AranetaSeats, 6, "Manila", "Beermen", "Philippines"),
        R("Ginebra", "Gin Kings", "BGS", "Barangay Ginebra San Miguel", Araneta, AranetaSeats, 5, "Manila", "Gin Kings", "Philippines"),
        R("TNT", "Tropang 5G", "TNT", null, Araneta, AranetaSeats, 5, "Quezon City", "Tropang", "Philippines"),
        R("Meralco", "Bolts", "MRL", null, Araneta, AranetaSeats, 2, "Pasig", "Bolts", "Philippines"),
        R("Magnolia", "Hotshots", "MAG", null, Araneta, AranetaSeats, 2, "Makati", "Hotshots", "Philippines"),
        R("Phoenix", "Fuel Masters", "PFM", null, Araneta, AranetaSeats, -1, "Cebu", "Fuel Masters", "Philippines"),
        R("Rain or Shine", "Elasto Painters", "RNS", null, Araneta, AranetaSeats, 1, "Davao", "Painters", "Philippines"),
        R("Terrafirma", "Dyip", "TER", null, Araneta, AranetaSeats, -4, "Pampanga", "Dyip", "Philippines"),
        R("NLEX", "Road Warriors", "NLX", null, Araneta, AranetaSeats, 0, "Taguig", "Road Warriors", "Philippines"),
        R("Titan Ultra", "Giant Risers", "TTN", null, Araneta, AranetaSeats, -4, "Laguna", "Elasto", "Philippines"),
        R("Blackwater", "Bossing", "BLW", null, Araneta, AranetaSeats, -3, "Batangas", "Bossing", "Philippines"),
        R("Converge", "FiberXers", "CNV", null, Araneta, AranetaSeats, 0, "Iloilo", "Converge", "Philippines"),
    };

    /// <summary>The Israeli Premier League (12 of the real 14).</summary>
    public static readonly ClubSeed[] Israel =
    {
        R("Tel Aviv", "Maccabi Tel Aviv", "MTA", "Maccabi Tel Aviv", "Menora Mivtachim Arena", 10383, 7, "Tel Aviv", "Yellows", "Israel"),
        R("Tel Aviv", "Hapoel Tel Aviv", "HTA", "Hapoel Tel Aviv", "Menora Mivtachim Arena", 10383, 5, "Tel Aviv", "Reds", "Israel"),
        R("Jerusalem", "Hapoel Jerusalem", "JER", "Hapoel Jerusalem", "Pais Arena", 11000, 4, "Jerusalem", "Capitals", "Israel"),
        R("Holon", "Hapoel Holon", "HOL", "Hapoel Holon", "Holon Toto Hall", 5500, 1, "Holon", "Southerners", "Israel"),
        R("Be'er Sheva", "Hapoel Be'er Sheva", "HBS", "Hapoel Be'er Sheva", "Conch Arena", 3000, 0, "Haifa", "Carmel", "Israel"),
        R("Rishon LeZion", "Maccabi Rishon LeZion", "RIS", "Maccabi Rishon LeZion", "Beit Maccabi Rishon", 2500, -2, "Rishon", "Lezion", "Israel"),
        R("Ness Ziona", "Ironi Ness Ziona", "NZI", "Ironi Ness Ziona", "Lev Hamoshava", 1300, -1, "Ness Ziona", "Orchards", "Israel"),
        R("Netanya", "Elitzur Netanya", "NET", "Elitzur Netanya", "Netanya Arena", 2500, -3, "Hadera", "Riverside", "Israel"),
        R("Herzliya", "Bnei Herzliya", "HER", "Bnei Herzliya", "HaYovel Herzliya", 1500, -1, "Herzliya", "Seaside", "Israel"),
        R("Galil Elyon", "Hapoel Galil Elyon", "GIL", "Hapoel Galil Elyon", "HaPais Kfar Blum", 2000, 0, "Galil", "Gilboa", "Israel"),
        R("Kiryat Ata", "Ironi Kiryat Ata", "KIR", "Ironi Kiryat Ata", "Ramaz Hall", 1200, -1, "Ironi Kiryat", "Ata", "Israel"),
        R("Ramat Gan", "Maccabi Ironi Ramat Gan", "RMG", "Maccabi Ironi Ramat Gan", "Zisman Hall", 1500, -2, "Eilat", "Red Sea", "Israel"),
    };

    /// <summary>The ABA League (14). Each slot keeps its country.</summary>
    public static readonly ClubSeed[] Aba =
    {
        R("Belgrade", "Crvena zvezda", "CZV", "Crvena zvezda", "Aleksandar Nikolic Hall", 8000, 7, "Belgrade", "Red Stars", "Serbia"),
        R("Belgrade", "Partizan", "PTZ", "Partizan", "Belgrade Arena", 18386, 7, "Belgrade", "Partisans", "Serbia"),
        R("Belgrade", "FMP", "FMP", "FMP", "Zeleznik Hall", 3000, 0, "Novi Sad", "Danubians", "Serbia"),
        R("Cacak", "Borac", "BOR", "Borac Cacak", "Borac Hall", 4000, -1, "Nis", "Nisava", "Serbia"),
        R("Zagreb", "Cibona", "CIB", "Cibona", "Drazen Petrovic Hall", 5400, -1, "Zagreb", "Cibona Lions", "Croatia"),
        R("Split", "Split", "KKS", "KK Split", "Arena Gripe", 3500, -2, "Split", "Dalmatians", "Croatia"),
        R("Ljubljana", "Cedevita Olimpija", "CDO", "Cedevita Olimpija", "Arena Stozice", 12480, 3, "Ljubljana", "Dragons", "Slovenia"),
        R("Novo Mesto", "Krka", "KRK", "Krka Novo Mesto", "Leon Stukelj Hall", 2500, -3, "Krka", "Novo Mesto", "Slovenia"),
        R("Podgorica", "Buducnost", "BUD", "Buducnost", "Moraca Sports Center", 6000, 4, "Podgorica", "Buducnost Eagles", "Montenegro"),
        R("Podgorica", "SC Derby", "DRB", "SC Derby", "Moraca Sports Center", 6000, -1, "Niksic", "Ironworks", "Montenegro"),
        R("Laktasi", "Igokea", "IGO", "Igokea", "Laktasi Sports Hall", 3050, 1, "Sarajevo", "Bascarsija", "Bosnia and Herzegovina"),
        R("Skopje", "MZT", "MZT", "MZT Skopje", null, 0, -3, "Skopje", "Vardar", "North Macedonia"),
        R("Zadar", "Zadar", "ZAD", "KK Zadar", "Kresimir Cosic Hall", 7997, -1, "Zadar", "Adriatics", "Croatia"),
        R("Belgrade", "Mega", "MEG", "Mega Basket", "Ranko Zeravica Sports Hall", 5000, 0, "Mega", "Belgrade Youth", "Serbia"),
    };

    /// <summary>The VTB United League (12: ten of its eleven Russian clubs, and Minsk and Astana, members until recently).</summary>
    public static readonly ClubSeed[] Vtb =
    {
        R("Moscow", "CSKA", "CSK", "CSKA Moscow", "Megasport Arena", 13926, 7, "Moscow", "Army", "Russia"),
        R("Moscow", "MBA", "MBA", "MBA Moscow", "Krylatskoye Sports Palace", 5000, -1, "Moscow", "Railwaymen", "Russia"),
        R("St. Petersburg", "Zenit", "ZEN", "Zenit St. Petersburg", "Sibur Arena", 7120, 5, "St. Petersburg", "Zenith", "Russia"),
        R("Kazan", "UNICS", "KZN", "UNICS Kazan", "Basket-Hall Kazan", 7482, 4, "Kazan", "Unicorns", "Russia"),
        R("Krasnodar", "Lokomotiv Kuban", "LOK", "Lokomotiv Kuban", "Basket-Hall Krasnodar", 7500, 3, "Krasnodar", "Kuban", "Russia"),
        R("Nizhny Novgorod", "Nizhny Novgorod", "NNV", "BC Nizhny Novgorod", "Trade Union Sport Palace", 5500, 1, "Nizhny Novgorod", "Volga", "Russia"),
        R("Samara", "Samara", "SMR", "BC Samara", "Ice Sports Palace", 5000, 0, "Samara", "Rockets", "Russia"),
        R("Perm", "Parma", "PRM", "Parma Perm", "Molot Sports Palace", 7000, -1, "Perm", "Parma", "Russia"),
        R("Yekaterinburg", "Uralmash", "URM", "Uralmash Yekaterinburg", "Palace of Sporting Games", 5000, 0, "Yekaterinburg", "Urals", "Russia"),
        R("Minsk", "Minsk", "TSM", "BC Minsk", null, 0, -3, "Minsk", "Tsmoki", "Belarus"),
        R("Astana", "Astana", "AST", "BC Astana", null, 0, -3, "Astana", "Steppe", "Kazakhstan"),
        R("Krasnoyarsk", "Enisey", "ENI", "Enisey Krasnoyarsk", "Arena Sever", 4100, -2, "Novosibirsk", "Siberians", "Russia"),
    };

    private static IEnumerable<ClubSeed> Slice2() => Australia.Concat(China).Concat(Japan).Concat(Korea).Concat(Philippines).Concat(Israel).Concat(Aba).Concat(Vtb);
}
