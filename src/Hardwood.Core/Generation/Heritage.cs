using Hardwood.Core.Model;

namespace Hardwood.Core.Generation;

/// <summary>
/// Where a generated player comes from: nationality, a matching name pool, and how likely each
/// appearance group (<see cref="Ethnicity"/>) is. The generator picks a heritage first, so a player's
/// name, nationality and portrait face agree. All names are generic; the best-known real NBA surnames were taken out in run 21 (#263), and a full name that matches a real NBA player is re-rolled (<see cref="RealNames"/>).
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
            new[] { "Petrovic","Jovanovic","Markovic","Nikolic","Stojanovic","Pavlovic","Popovic","Vukovic","Djordjevic","Ilic","Jovic","Micic","Ilic","Stankovic","Radovic" },
            new[] { (Ethnicity.YugoGreek, 1.0) }),
        new("france", 4, new[] { "France" },
            new[] { "Evan","Rudy","Nicolas","Theo","Killian","Victor","Mathias","Bilal","Moussa","Sekou","Frank","Timothe","Elie","Guerschon","Yakuba" },
            new[] { "Fournier","Moreau","Girard","Diallo","Traore","Hayes","Lambert","Bonnet","Rousseau","Fontaine","Diallo","Traore","Lefevre","Moreau","Girard" },
            new[] { (Ethnicity.Caucasian, 0.50), (Ethnicity.African, 0.42), (Ethnicity.MENA, 0.08) }),
        new("germany", 2, new[] { "Germany", "Austria" },
            new[] { "Franz","Moritz","Dennis","Daniel","Maximilian","Johannes","Andreas","Jonas","Tim","Lukas" },
            new[] { "Schroder","Wagner","Becker","Hoffmann","Krause","Schmidt","Weber","Fischer","Meyer","Becker","Hoffmann","Krause" },
            new[] { (Ethnicity.CentralEuropean, 0.85), (Ethnicity.African, 0.15) }),
        new("spain", 2, new[] { "Spain" },
            new[] { "Santi","Ricky","Sergio","Juan","Pau","Alex","Willy","Dario","Jaime","Usman" },
            new[] { "Garcia","Ortega","Romero","Torres","Ruiz","Moreno","Jimenez","Fernandez","Alonso","Serrano","Navarro","Rodriguez" },
            new[] { (Ethnicity.SpanMed, 0.88), (Ethnicity.African, 0.12) }),
        new("italy", 1, new[] { "Italy" },
            new[] { "Danilo","Marco","Nicolo","Simone","Achille","Matteo","Giampaolo","Alessandro","Luigi","Stefano" },
            new[] { "Rossi","Russo","Ferrari","Esposito","Bianchi","Romano","Ricci","Colombo","Bruno","Greco" },
            new[] { (Ethnicity.Italmed, 0.92), (Ethnicity.African, 0.08) }),
        new("greece", 1.5, new[] { "Greece" },
            new[] { "Giannis","Kostas","Thanasis","Nikos","Vassilis","Georgios","Dimitris","Panagiotis","Ioannis","Kostis" },
            new[] { "Papadopoulos","Georgiou","Nikolaidis","Konstantinou","Alexiou","Dimitriou","Karras","Vlachos","Papadopoulos","Kalaitzakis" },
            new[] { (Ethnicity.YugoGreek, 0.85), (Ethnicity.African, 0.15) }),
        new("australia", 2.5, new[] { "Australia", "New Zealand" },
            new[] { "Josh","Patty","Ben","Dyson","Jock","Matisse","Joe","Xavier","Will","Jack" },
            new[] { "Murray","Mills","Kelly","Ryan","O'Brien","Walsh","Hunter","Fraser","Kay","Byrne","Daniels","Green" },
            new[] { (Ethnicity.Caucasian, 0.78), (Ethnicity.African, 0.15), (Ethnicity.Seasian, 0.04), (Ethnicity.Asian, 0.03) }),
        new("westafrica", 3, new[] { "Senegal", "Cameroon", "Nigeria", "Mali", "Congo DR" },
            new[] { "Ousmane","Joel","Pascal","Serge","Bismack","Precious","Ibou","Amadou","Hassan","Mamadou","Chimezie","Udoka","Moussa","Cheikh","Ekpe" },
            new[] { "Dieng","Fall","Ndour","Diop","Sarr","Sow","Ba","Gueye","Seck","Mbaye","Okafor","Eze","Okoro","Obi","Nwankwo" },
            new[] { (Ethnicity.African, 1.0) }),
        new("southamerica", 2, new[] { "Argentina", "Brazil", "Uruguay" },
            new[] { "Leandro","Bruno","Raul","Facundo","Gabriel","Nicolas","Tomas","Luis","Mateo","Diego" },
            new[] { "Gomez","Diaz","Alvarez","Acosta","Benitez","Medina","Herrera","Sosa","Silva","Pereira","Souza","Costa" },
            new[] { (Ethnicity.SAMed, 0.72), (Ethnicity.SouthAmerican, 0.20), (Ethnicity.African, 0.08) }),
        new("caribbean", 1, new[] { "Dominican Republic", "Puerto Rico" },
            new[] { "Karl","Al","Francisco","Angel","Jose","Edgar","Victor","Eloy","Andres","Rafael" },
            new[] { "Vargas","Perez","Garcia","Delgado","Feliz","Liz","Rosario","Mejia","Santana","Cruz" },
            new[] { (Ethnicity.SouthAmerican, 0.55), (Ethnicity.African, 0.35), (Ethnicity.SAMed, 0.10) }),
        new("japan", 1, new[] { "Japan" },
            new[] { "Yuta","Rui","Yuki","Makoto","Kai","Keisei","Daiki","Ren","Shota","Haruki" },
            new[] { "Watanabe","Suzuki","Takahashi","Ito","Yamamoto","Nakamura","Kobayashi","Kato","Tanaka","Saito" },
            new[] { (Ethnicity.Asian, 0.92), (Ethnicity.African, 0.05), (Ethnicity.Caucasian, 0.03) }),
        new("china", 0.5, new[] { "China" },
            new[] { "Wei","Jun","Zhou","Hao","Lei","Ming","Yi","Qi","Tao","Bo" },
            new[] { "Wang","Li","Zhang","Liu","Chen","Yang","Zhao","Huang","Zhou","Wu" },
            new[] { (Ethnicity.Asian, 1.0) }),
        new("philippines", 0.5, new[] { "Philippines" },
            new[] { "Kai","Dwight","Kiefer","Thirdy","Ricci","Rhenz","Carl","Juan","AJ","Scottie" },
            new[] { "Santos","Reyes","Aquino","Ramos","Bautista","Villanueva","Mendoza","Flores","Navarro","Rivero" },
            new[] { (Ethnicity.Seasian, 1.0) }),
        new("turkey", 1, new[] { "Turkey", "Israel", "Lebanon" },
            new[] { "Alperen","Cedi","Ersan","Furkan","Sertac","Omer","Deni","Yam","Wael","Ali" },
            new[] { "Yilmaz","Kaya","Demir","Sahin","Celik","Aydin","Cohen","Levi","Khoury","Haddad" },
            new[] { (Ethnicity.MENA, 0.85), (Ethnicity.Caucasian, 0.15) }),
        new("southasia", 0.5, new[] { "India", "Pakistan" },
            new[] { "Amjyot","Satnam","Vishesh","Arvind","Princepal","Muin","Sahaj","Rohan","Arjun","Imran" },
            new[] { "Singh","Verma","Sharma","Kumar","Patel","Bhullar","Sekhon","Khan","Gill","Rana" },
            new[] { (Ethnicity.MESA, 1.0) }),
        new("nordic", 1, new[] { "Finland", "Sweden", "Norway", "Denmark" },
            new[] { "Lauri","Mikael","Erik","Anton","Oskar","Elias","Sasu","Viktor","Gustav","Magnus" },
            new[] { "Lindqvist","Johansson","Eriksson","Nilsson","Virtanen","Korhonen","Hansen","Makinen","Heikkinen","Larsen" },
            new[] { (Ethnicity.Scandinavian, 0.90), (Ethnicity.African, 0.10) }),
        new("easterneurope", 1.5, new[] { "Lithuania", "Latvia", "Ukraine", "Russia", "Georgia" },
            new[] { "Jonas","Domantas","Mindaugas","Arturas","Kristaps","Davis","Sviatoslav","Alexey","Dmitri","Tornike" },
            new[] { "Petrauskas","Kazlauskas","Jankauskas","Berzins","Kalnins","Shevchenko","Ivanov","Petrov","Beridze","Kovalenko" },
            new[] { (Ethnicity.EECA, 1.0) }),
        // The wider world's home players (decision #111). Weight 0: they come through their own leagues, not the NABL's intake.
        // Country-specific names where a shared group above would mix them (a Lithuanian league full of Georgian names).
        new("lithuania", 0, new[] { "Lithuania" },
            new[] { "Jonas","Mindaugas","Arturas","Domantas","Rokas","Marius","Tadas","Lukas","Arnas","Gytis","Paulius","Ignas" },
            new[] { "Kazlauskas","Jankauskas","Petrauskas","Stankevicius","Vasiliauskas","Zukauskas","Butkus","Paulauskas","Urbonas","Kavaliauskas","Navickas","Rimkus" },
            new[] { (Ethnicity.EECA, 1.0) }),
        new("latvia", 0, new[] { "Latvia" },
            new[] { "Kristaps","Davis","Janis","Rolands","Arturs","Rihards","Andrejs","Martins","Karlis","Edgars" },
            new[] { "Berzins","Kalnins","Ozolins","Jansons","Liepins","Krumins","Ozols","Smits","Vitols","Grinbergs" },
            new[] { (Ethnicity.EECA, 1.0) }),
        new("russia", 0, new[] { "Russia", "Ukraine" },
            new[] { "Alexey","Dmitri","Sergei","Andrei","Nikita","Ivan","Mikhail","Artem","Vitaly","Egor" },
            new[] { "Ivanov","Petrov","Smirnov","Kuznetsov","Popov","Sokolov","Volkov","Fedorov","Morozov","Orlov","Lebedev","Kovalenko" },
            new[] { (Ethnicity.EECA, 1.0) }),
        new("serbia", 0, new[] { "Serbia", "Montenegro", "Bosnia and Herzegovina" },
            new[] { "Nikola","Bogdan","Vasilije","Stefan","Aleksa","Nemanja","Filip","Marko","Ognjen","Uros","Dusan","Milos" },
            new[] { "Petrovic","Markovic","Nikolic","Stojanovic","Jovanovic","Ilic","Djordjevic","Pavlovic","Stankovic","Radovic","Popovic","Vukovic" },
            new[] { (Ethnicity.YugoGreek, 1.0) }),
        new("croatia", 0, new[] { "Croatia", "Slovenia" },
            new[] { "Ante","Dario","Ivan","Mario","Luka","Josip","Zoran","Jaka","Klemen","Ziga" },
            new[] { "Horvat","Kovacic","Babic","Maric","Juric","Novak","Kranjc","Zupan","Hrovat","Simunovic","Popovic","Kos" },
            new[] { (Ethnicity.YugoGreek, 1.0) }),
        new("korea", 0, new[] { "Korea" },
            new[] { "Min-jun","Seo-jun","Ji-hoon","Hyun-woo","Jae-won","Dong-hyun","Sung-min","Tae-yang","Woo-jin","Joon-ho" },
            new[] { "Kim","Lee","Park","Choi","Jung","Kang","Cho","Yoon","Jang","Lim","Han","Oh" },
            new[] { (Ethnicity.Asian, 1.0) }),
        new("benelux", 0, new[] { "Belgium", "Netherlands" },
            new[] { "Thijs","Bram","Lars","Sem","Ruben","Wout","Jelle","Kevin","Maxime","Arnaud" },
            new[] { "de Jong","Jansen","Peeters","Janssens","Maes","Visser","Smits","Mulder","de Vries","Claes","Willems","Bakker" },
            new[] { (Ethnicity.CentralEuropean, 0.80), (Ethnicity.African, 0.20) }),
        new("centraleurope", 0, new[] { "Poland", "Czechia" },
            new[] { "Mateusz","Jakub","Michal","Tomasz","Kamil","Ondrej","Jan","Lukas","Petr","Adam" },
            new[] { "Nowak","Kowalski","Wisniewski","Wojcik","Kaminski","Novak","Svoboda","Dvorak","Cerny","Prochazka","Zielinski","Lewandowski" },
            new[] { (Ethnicity.CentralEuropean, 0.75), (Ethnicity.EECA, 0.25) }),
        new("baltic", 0, new[] { "Estonia", "Belarus", "Kazakhstan" },
            new[] { "Kristjan","Martin","Sander","Rait","Artem","Maksim","Yuri","Nurlan","Alibek","Pavel" },
            new[] { "Tamm","Saar","Sepp","Kask","Novik","Kovalchuk","Petrenko","Abenov","Nurpeisov","Volkov","Ilves","Lepik" },
            new[] { (Ethnicity.EECA, 0.85), (Ethnicity.Asian, 0.15) }),
        new("macedonia", 0, new[] { "North Macedonia" },
            new[] { "Bojan","Darko","Aleksandar","Kiril","Vlatko","Damjan","Dimitar","Andrej","Pero","Toni" },
            new[] { "Stojanovski","Petrovski","Nikolovski","Dimitrovski","Trajkovski","Angelov","Ristovski","Jovanovski","Georgiev","Kostov" },
            new[] { (Ethnicity.YugoGreek, 1.0) }),
        new("mexico", 0, new[] { "Mexico" },
            new[] { "Jorge","Gustavo","Paul","Francisco","Alejandro","Emilio","Fernando","Ricardo","Hector","Ivan" },
            new[] { "Hernandez","Lopez","Ramirez","Gonzalez","Martinez","Sanchez","Ayala","Gutierrez","Reyes","Flores","Morales","Ortiz" },
            new[] { (Ethnicity.SouthAmerican, 0.70), (Ethnicity.SAMed, 0.25), (Ethnicity.African, 0.05) }),
        new("northafrica", 0, new[] { "Egypt", "Tunisia", "Morocco" },
            new[] { "Ahmed","Mohamed","Omar","Youssef","Karim","Anas","Assem","Ehab","Makram","Hamza" },
            new[] { "Hassan","Mahmoud","Ibrahim","Ben Romdhane","Abdelfattah","Mohsen","Lahyani","El Amrani","Bennani","Haddadi" },
            new[] { (Ethnicity.MENA, 0.85), (Ethnicity.African, 0.15) }),
        new("africa", 0, new[] { "Angola", "Rwanda", "South Africa", "Ivory Coast" },
            new[] { "Childe","Yanick","Olimpio","Kenny","Axel","Dieudonne","Thabo","Sipho","Souleymane","Mathieu" },
            new[] { "Dundao","Moreira","Cipriano","Gasana","Mukama","Nkosi","Dlamini","Kone","Coulibaly","Kouassi","Ndayisaba","Gomes" },
            new[] { (Ethnicity.African, 1.0) }),
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
    /// Brings an older save's player in line with the current heritage table: when his stored ethnicity is one his
    /// nationality can no longer have (e.g. a Spanish player saved as Italmed before SpanMed existed) and he has no
    /// portrait face yet, he moves to his heritage's main group. Players who already have a face keep everything.
    /// Returns true if the player changed.
    /// </summary>
    public static bool Reconcile(Player p)
    {
        if (p.FaceFile != null) return false;
        var h = All.FirstOrDefault(x => x.Nationalities.Contains(p.Nationality));
        if (h == null || h.Ethnicities.Any(e => e.Ethnicity == p.Ethnicity)) return false;
        p.Ethnicity = h.Ethnicities[0].Ethnicity;
        return true;
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
