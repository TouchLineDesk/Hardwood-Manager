namespace Hardwood.Core.World;

/// <summary>
/// Run 233 (Danny's note `danny-world-leagues-nov26`, the domestic real club counts): the real clubs slices 1-3 left out where
/// the game had fewer places than the real league, so each of these leagues now has its real 2025-26 size. A new world adds
/// them after the slotted clubs, from their own random stream (<see cref="GameWorld"/>'s <c>AddRealCounts</c>), so the slotted
/// clubs, the college world and the NBA draw the same numbers as before. An older save keeps the clubs it has.
/// <para>Sizes: Greek Basket League 13, Israeli Premier League 14, PLK 16, Latvian-Estonian League 14, Czech NBL 13, BNXT
/// League 18 (10 Belgian, 8 Dutch), Liga Nacional 19, NBB 18, LNBP 14, B.League B1 26. The ABA League, VTB, the BAL and the
/// LKL keep theirs (their left-out clubs come from countries the league's slots don't hold, or the count already matches).</para>
/// <para>Sources (read Nov 28, 2026): English Wikipedia's 2025-26 pages for the Greek Basket League and Mykonos B.C., the
/// Israeli Premier League (a March 2026 mirror), the PLK, the Latvian-Estonian League and the BNXT League (venue tables), and
/// the 2025-26 B1 League season (club names only). Run 219's list of left-out clubs names the Czech, Argentine, Brazilian and
/// Mexican ones; their sources give no arenas, so those stay drawn (null and 0), as the Czech clubs' do. The ten B1 clubs'
/// arenas are their well-known home halls; their capacities are left drawn. Letters outside Latin-1 are written in plain Latin.</para>
/// </summary>
public static partial class RealClubs
{
    private static ClubSeed A(string city, string name, string abbr, string? full, string? arena, int capacity, int standing, string country) =>
        new(city, name, abbr, country) { Full = full, Arena = arena, Capacity = capacity, Standing = standing };

    /// <summary>The clubs a league adds to reach its real size (empty when it already has it).</summary>
    public static ClubSeed[] MoreOf(string key) => More.TryGetValue(key, out var m) ? m : Array.Empty<ClubSeed>();

    /// <summary>Every added club (for <see cref="Seeds"/>).</summary>
    private static IEnumerable<ClubSeed> Counts() => More.Values.SelectMany(m => m);

    private static readonly Dictionary<string, ClubSeed[]> More = new()
    {
        [WorldLeagues.Greece] = new[]
        {
            A("Mykonos", "Mykonos", "MYK", "Mykonos B.C.", "P. Chanioti Ano Mera Indoor Hall", 1000, -4, "Greece"),
        },
        [WorldLeagues.Israel] = new[]
        {
            A("Gan Ner", "Hapoel HaEmek", "HEM", "Hapoel HaEmek", "Gan Ner Sports Hall", 2057, -2, "Israel"),
            A("Ra'anana", "Maccabi Ironi Ra'anana", "RAA", "Maccabi Ironi Ra'anana", "Metro West", 1668, -3, "Israel"),
        },
        [WorldLeagues.Poland] = new[]
        {
            A("Walbrzych", "Górnik", "GWA", "Górnik Walbrzych", "Aqua Zdrój", 2023, -2, "Poland"),
            A("Krosno", "Miasto Szkla", "KRO", "Miasto Szkla Krosno", "Hala MOSiR", 1690, -4, "Poland"),
        },
        [WorldLeagues.Baltic] = new[]
        {
            A("Riga", "Latvijas Universitate", "LUR", "Latvijas Universitate", "O. Kalpaka RTDP", 258, -3, "Latvia"),
            A("Keila", "KK", "KEK", "Keila KK", null, 0, -3, "Estonia"),
        },
        [WorldLeagues.Czechia] = new[]
        {
            A("Plzen", "Plzen", "PLZ", "Basket Plzen", null, 0, -3, "Czechia"),
            A("Písek", "Srsni", "PIS", "Srsni Písek", null, 0, -3, "Czechia"),
            A("Kolín", "Kolín", "KOL", "BC Kolín", null, 0, -2, "Czechia"),
        },
        [WorldLeagues.Benelux] = new[]
        {
            A("Kortrijk", "Spurs", "KRT", "Kortrijk Spurs", "Sportcampus Lange Munte", 2400, -1, "Belgium"),
            A("Hasselt", "Limburg United", "LIM", "Limburg United", "Alverberg Sporthal", 1730, 0, "Belgium"),
            A("Aalst", "Okapi", "AAL", "Okapi Aalst", "Okapi Forum", 2800, -1, "Belgium"),
            A("Weert", "BAL", "WEE", "BAL Weert", "Sporthal Boshoven", 1000, -2, "Netherlands"),
        },
        [WorldLeagues.Argentina] = new[]
        {
            A("Chivilcoy", "Racing", "RCH", "Racing de Chivilcoy", null, 0, -2, "Argentina"),
        },
        [WorldLeagues.Brazil] = new[]
        {
            A("Pato Branco", "Pato", "PTB", "Pato Basquete", null, 0, -2, "Brazil"),
            A("Santa Cruz do Sul", "União Corinthians", "UCS", "União Corinthians", null, 0, -2, "Brazil"),
        },
        [WorldLeagues.Mexico] = new[]
        {
            A("Ciudad Victoria", "Correbasket UAT", "UAT", "Correbasket UAT", null, 0, -2, "Mexico"),
            A("Fresnillo", "Gambusinos", "FRE", "Gambusinos de Fresnillo", null, 0, -2, "Mexico"),
        },
        [WorldLeagues.Japan] = new[]
        {
            A("Ota", "Crane Thunders", "GUN", "Gunma Crane Thunders", "Open House Arena Ota", 0, 1, "Japan"),
            A("Koshigaya", "Alphas", "KOS", "Koshigaya Alphas", "Koshigaya City Gymnasium", 0, -2, "Japan"),
            A("Mito", "Robots", "IBA", "Ibaraki Robots", "Adastria Mito Arena", 0, -3, "Japan"),
            A("Chiba", "Altiri", "ALT", "Altiri Chiba", "Chiba Port Arena", 0, -3, "Japan"),
            A("Akita", "Northern Happinets", "AKI", "Akita Northern Happinets", "CNA Arena Akita", 0, -1, "Japan"),
            A("Nagasaki", "Velca", "NGS", "Nagasaki Velca", "Happiness Arena", 0, 2, "Japan"),
            A("Toyohashi", "NeoPhoenix", "SNE", "San-en NeoPhoenix", "Toyohashi City General Gymnasium", 0, 1, "Japan"),
            A("Saga", "Ballooners", "SAG", "Saga Ballooners", "SAGA Arena", 0, 1, "Japan"),
            A("Toyama", "Grouses", "TOY", "Toyama Grouses", "Toyama City Gymnasium", 0, -2, "Japan"),
            A("Nagoya", "Fighting Eagles", "FEG", "Fighting Eagles Nagoya", null, 0, 0, "Japan"),
        },
    };
}
