using Hardwood.Core.Model;

namespace Hardwood.Core.Generation;

/// <summary>
/// Where a generated player comes from: nationality, a matching name pool, and how likely each
/// appearance group (<see cref="Ethnicity"/>) is. The generator picks a heritage first, so a player's
/// name, nationality and portrait face agree. All names are generic; no real players.
/// </summary>
public sealed record Heritage(
    string Key,
    double Weight,
    string[] Nationalities,
    string[] FirstNames,
    string[] LastNames,
    (Ethnicity Ethnicity, double Weight)[] Ethnicities);

public static class Heritages
{
    private static readonly string[] UsFirst =
    {
        "Marcus","Jalen","Darius","Tyrese","Andre","Devin","Cameron","Isaiah","Malik","Jordan",
        "Trey","Xavier","Elijah","Kobe","Jaylen","Zion","Desmond","Terrence","Reggie","Donte",
        "Kendrick","Brandon","Anthony","Michael","Chris","Kevin","Jason","Derrick","Tyler","Caleb",
        "Chet","Paolo","Austin","Cole","Grant","Hunter","Jaden","Keegan","Miles","Quentin",
    };

    private static readonly string[] AngloLast =
    {
        "Johnson","Williams","Brown","Jones","Davis","Miller","Wilson","Moore","Taylor","Anderson",
        "Thomas","Jackson","White","Harris","Martin","Thompson","Robinson","Clark","Lewis","Walker",
        "Hall","Allen","Young","King","Wright","Scott","Green","Baker","Adams","Nelson",
        "Carter","Mitchell","Roberts","Turner","Phillips","Campbell","Parker","Evans","Edwards","Collins",
        "Stewart","Morris","Murphy","Cook","Rogers","Morgan","Cooper","Reed","Bailey","Bell",
        "Howard","Ward","Cox","Richardson","Wood","Watson","Brooks","Bennett","Gray","James",
        "Hughes","Price","Sanders","Myers","Long","Ross","Foster","Powell","Jenkins","Perry",
    };

    /// <summary>Weights are relative shares of a North American league's intake.</summary>
    public static readonly Heritage[] All =
    {
        new("us", 70, new[] { "USA" }, UsFirst, AngloLast,
            new[] { (Ethnicity.African, 0.70), (Ethnicity.Caucasian, 0.22), (Ethnicity.SAMed, 0.03), (Ethnicity.SouthAmerican, 0.02), (Ethnicity.Asian, 0.02), (Ethnicity.MESA, 0.01) }),
        new("canada", 5, new[] { "Canada" },
            new[] { "Shai","Jamal","Andrew","RJ","Dillon","Kelly","Bennedict","Dwight","Trey","Zach","Kyle","Cory","Tristan","Nickeil","Lu" },
            AngloLast,
            new[] { (Ethnicity.African, 0.60), (Ethnicity.Caucasian, 0.33), (Ethnicity.Asian, 0.04), (Ethnicity.MESA, 0.03) }),
        new("balkan", 3, new[] { "Serbia", "Croatia", "Slovenia", "Bosnia and Herzegovina", "Montenegro" },
            new[] { "Luka","Nikola","Bogdan","Goran","Dario","Jusuf","Ante","Marko","Ivan","Dragan","Vasilije","Filip","Stefan","Aleksa","Nemanja" },
            new[] { "Petrovic","Jokovic","Markovic","Nikolic","Stojanovic","Bogdanovic","Saric","Hezonja","Zubac","Vucevic","Jovic","Micic","Ilic","Kalinic","Radovic" },
            new[] { (Ethnicity.CentralEuropean, 1.0) }),
        new("france", 4, new[] { "France" },
            new[] { "Evan","Rudy","Nicolas","Theo","Killian","Victor","Mathias","Bilal","Moussa","Sekou","Frank","Timothe","Elie","Guerschon","Yakuba" },
            new[] { "Fournier","Batum","Gobert","Yabusele","Maledon","Hayes","Okobo","Lessort","Poirier","Cordinier","Diallo","Traore","Lefevre","Moreau","Girard" },
            new[] { (Ethnicity.Caucasian, 0.50), (Ethnicity.African, 0.42), (Ethnicity.MENA, 0.08) }),
        new("germany", 2, new[] { "Germany", "Austria" },
            new[] { "Franz","Moritz","Dennis","Daniel","Maximilian","Johannes","Andreas","Jonas","Tim","Lukas" },
            new[] { "Schroder","Wagner","Theis","Bonga","Kleber","Voigtmann","Hartenstein","Obst","Thiemann","Becker","Hoffmann","Krause" },
            new[] { (Ethnicity.CentralEuropean, 0.85), (Ethnicity.African, 0.15) }),
        new("spain", 2, new[] { "Spain" },
            new[] { "Santi","Ricky","Sergio","Juan","Pau","Alex","Willy","Dario","Jaime","Usman" },
            new[] { "Hernangomez","Rubio","Llull","Abalde","Brizuela","Garuba","Aldama","Fernandez","Claver","Sastre","Navarro","Rodriguez" },
            new[] { (Ethnicity.Italmed, 0.88), (Ethnicity.African, 0.12) }),
        new("italy", 1, new[] { "Italy" },
            new[] { "Danilo","Marco","Nicolo","Simone","Achille","Matteo","Giampaolo","Alessandro","Luigi","Stefano" },
            new[] { "Gallinari","Belinelli","Melli","Fontecchio","Polonara","Spissu","Ricci","Mannion","Tonut","Procida" },
            new[] { (Ethnicity.Italmed, 0.92), (Ethnicity.African, 0.08) }),
        new("greece", 1.5, new[] { "Greece" },
            new[] { "Giannis","Kostas","Thanasis","Nikos","Vassilis","Georgios","Dimitris","Panagiotis","Ioannis","Kostis" },
            new[] { "Antetokounmpo","Papanikolaou","Sloukas","Calathes","Mitoglou","Larentzakis","Papagiannis","Bochoridis","Papadopoulos","Kalaitzakis" },
            new[] { (Ethnicity.Italmed, 0.85), (Ethnicity.African, 0.15) }),
        new("australia", 2.5, new[] { "Australia", "New Zealand" },
            new[] { "Josh","Patty","Ben","Dyson","Jock","Matisse","Joe","Xavier","Will","Jack" },
            new[] { "Giddey","Mills","Exum","Ingles","Dellavedova","Baynes","Landale","Bogut","Kay","Thybulle","Daniels","Green" },
            new[] { (Ethnicity.Caucasian, 0.78), (Ethnicity.African, 0.15), (Ethnicity.Seasian, 0.04), (Ethnicity.Asian, 0.03) }),
        new("westafrica", 3, new[] { "Senegal", "Cameroon", "Nigeria", "Mali", "Congo DR" },
            new[] { "Ousmane","Joel","Pascal","Serge","Bismack","Precious","Ibou","Amadou","Hassan","Mamadou","Chimezie","Udoka","Moussa","Cheikh","Ekpe" },
            new[] { "Dieng","Fall","Ndour","Diop","Sarr","Sow","Ba","Gueye","Seck","Mbaye","Okafor","Achiuwa","Okoro","Nwora","Siakam" },
            new[] { (Ethnicity.African, 1.0) }),
        new("southamerica", 2, new[] { "Argentina", "Brazil", "Uruguay" },
            new[] { "Leandro","Bruno","Raul","Facundo","Gabriel","Nicolas","Tomas","Luis","Mateo","Diego" },
            new[] { "Bolmaro","Campazzo","Deck","Laprovittola","Scola","Garino","Delia","Brussino","Vildoza","Redivo","Caboclo","Huertas" },
            new[] { (Ethnicity.SAMed, 0.72), (Ethnicity.SouthAmerican, 0.20), (Ethnicity.African, 0.08) }),
        new("caribbean", 1, new[] { "Dominican Republic", "Puerto Rico" },
            new[] { "Karl","Al","Francisco","Angel","Jose","Edgar","Victor","Eloy","Andres","Rafael" },
            new[] { "Vargas","Horford","Garcia","Delgado","Feliz","Liz","Rosario","Mejia","Santana","Cruz" },
            new[] { (Ethnicity.SouthAmerican, 0.55), (Ethnicity.African, 0.35), (Ethnicity.SAMed, 0.10) }),
        new("japan", 1, new[] { "Japan" },
            new[] { "Yuta","Rui","Yuki","Makoto","Kai","Keisei","Daiki","Ren","Shota","Haruki" },
            new[] { "Watanabe","Hachimura","Togashi","Hiejima","Kawamura","Baba","Yoshii","Tominaga","Tanaka","Saito" },
            new[] { (Ethnicity.Asian, 0.92), (Ethnicity.African, 0.05), (Ethnicity.Caucasian, 0.03) }),
        new("china", 0.5, new[] { "China" },
            new[] { "Wei","Jun","Zhou","Hao","Lei","Ming","Yi","Qi","Tao","Bo" },
            new[] { "Wang","Li","Zhang","Liu","Chen","Yang","Zhao","Huang","Zhou","Wu" },
            new[] { (Ethnicity.Asian, 1.0) }),
        new("philippines", 0.5, new[] { "Philippines" },
            new[] { "Kai","Dwight","Kiefer","Thirdy","Ricci","Rhenz","Carl","Juan","AJ","Scottie" },
            new[] { "Sotto","Ravena","Fajardo","Ramos","Abando","Belangel","Tamayo","Edu","Navarro","Rivero" },
            new[] { (Ethnicity.Seasian, 1.0) }),
        new("turkey", 1, new[] { "Turkey", "Israel", "Lebanon" },
            new[] { "Alperen","Cedi","Ersan","Furkan","Sertac","Omer","Deni","Yam","Wael","Ali" },
            new[] { "Sengun","Osman","Ilyasova","Korkmaz","Sanli","Yurtseven","Avdija","Madar","Arakji","Haddad" },
            new[] { (Ethnicity.MENA, 0.85), (Ethnicity.Caucasian, 0.15) }),
        new("southasia", 0.5, new[] { "India", "Pakistan" },
            new[] { "Amjyot","Satnam","Vishesh","Arvind","Princepal","Muin","Sahaj","Rohan","Arjun","Imran" },
            new[] { "Singh","Bhriguvanshi","Sharma","Kumar","Patel","Bhullar","Sekhon","Khan","Gill","Rana" },
            new[] { (Ethnicity.MESA, 1.0) }),
        new("nordic", 1, new[] { "Finland", "Sweden", "Norway", "Denmark" },
            new[] { "Lauri","Mikael","Erik","Anton","Oskar","Elias","Sasu","Viktor","Gustav","Magnus" },
            new[] { "Lindqvist","Johansson","Eriksson","Nilsson","Virtanen","Korhonen","Hansen","Salin","Jantunen","Larsen" },
            new[] { (Ethnicity.Scandinavian, 0.90), (Ethnicity.African, 0.10) }),
        new("easterneurope", 1.5, new[] { "Lithuania", "Latvia", "Ukraine", "Russia", "Georgia" },
            new[] { "Jonas","Domantas","Mindaugas","Arturas","Kristaps","Davis","Sviatoslav","Alexey","Dmitri","Tornike" },
            new[] { "Valanciunas","Kazlauskas","Jankauskas","Porzingis","Bertans","Mykhailiuk","Ivanov","Petrov","Shengelia","Kovalenko" },
            new[] { (Ethnicity.EECA, 1.0) }),
    };

    /// <summary>Independent random stream for one player's heritage, derived from the world seed and player id.</summary>
    public static Rng StreamFor(ulong worldSeed, int playerId) =>
        new(unchecked(worldSeed * 0x9E3779B97F4A7C15UL ^ (ulong)playerId * 0xD1B54A32D192ED03UL ^ 0x48455249UL));

    public static Heritage Pick(Rng rng)
    {
        Span<double> w = stackalloc double[All.Length];
        for (int i = 0; i < All.Length; i++) w[i] = All[i].Weight;
        return All[rng.WeightedIndex(w)];
    }

    public static Ethnicity PickEthnicity(Heritage h, Rng rng)
    {
        Span<double> w = stackalloc double[h.Ethnicities.Length];
        for (int i = 0; i < w.Length; i++) w[i] = h.Ethnicities[i].Weight;
        return h.Ethnicities[rng.WeightedIndex(w)].Ethnicity;
    }

    /// <summary>
    /// For saves made before ethnicity existed: a stable guess from nationality and player id,
    /// so the same player always gets the same group. Danny can override it in the face registry.
    /// </summary>
    public static Ethnicity Backfill(int playerId, string? nationality)
    {
        var h = All.FirstOrDefault(x => x.Nationalities.Contains(nationality ?? "")) ?? All[0];
        uint x = unchecked((uint)playerId * 2654435761u);
        x ^= x >> 15; x = unchecked(x * 2246822519u); x ^= x >> 13;
        double r = (x % 100000) / 100000.0 * h.Ethnicities.Sum(e => e.Weight);
        foreach (var (e, w) in h.Ethnicities)
        {
            r -= w;
            if (r < 0) return e;
        }
        return h.Ethnicities[^1].Ethnicity;
    }
}
