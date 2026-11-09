namespace Hardwood.Core.Generation;

/// <summary>Name pools for the default fictional world. All names are generic; no real players.</summary>
public static class NamePool
{
    public static readonly string[] FirstNames =
    {
        "Marcus","Jalen","Darius","Tyrese","Andre","Devin","Cameron","Isaiah","Malik","Jordan",
        "Trey","Xavier","Elijah","Kobe","Jaylen","Zion","Desmond","Terrence","Reggie","Donte",
        "Kendrick","Brandon","Anthony","Michael","Chris","Kevin","Jason","Derrick","Tyler","Caleb",
        "Luka","Nikola","Bogdan","Goran","Dario","Jusuf","Ante","Marko","Ivan","Dragan",
        "Evan","Rudy","Nicolas","Theo","Killian","Victor","Mathias","Lauri","Jonas","Deni",
        "Franz","Moritz","Dennis","Daniel","Santi","Ricky","Sergio","Juan","Pau","Alex",
        "Giannis","Kostas","Thanasis","Yuta","Rui","Josh","Patty","Ben","Dyson","Jock",
        "Shai","Jamal","Andrew","RJ","Dillon","Kelly","Lu","Bennedict","Chet","Paolo",
        "Ousmane","Joel","Pascal","Serge","Bismack","Precious","Ibou","Amadou","Hassan","Victor",
        "Leandro","Bruno","Raul","Facundo","Gabriel","Nicolas","Tomas","Luis","Mateo","Diego",
    };

    public static readonly string[] LastNames =
    {
        "Johnson","Williams","Brown","Jones","Davis","Miller","Wilson","Moore","Taylor","Anderson",
        "Thomas","Jackson","White","Harris","Martin","Thompson","Robinson","Clark","Lewis","Walker",
        "Hall","Allen","Young","King","Wright","Scott","Green","Baker","Adams","Nelson",
        "Carter","Mitchell","Roberts","Turner","Phillips","Campbell","Parker","Evans","Edwards","Collins",
        "Stewart","Morris","Murphy","Cook","Rogers","Morgan","Cooper","Reed","Bailey","Bell",
        "Howard","Ward","Cox","Richardson","Wood","Watson","Brooks","Bennett","Gray","James",
        "Hughes","Price","Sanders","Myers","Long","Ross","Foster","Powell","Jenkins","Perry",
        "Petrovic","Jokovic","Markovic","Nikolic","Stojanovic","Bogdanovic","Saric","Hezonja","Zubac","Vucevic",
        "Fournier","Batum","Gobert","Yabusele","Maledon","Hayes","Okobo","Lessort","Poirier","Cordinier",
        "Schroder","Wagner","Theis","Bonga","Kleber","Voigtmann","Hartenstein","Obst","Thiemann","Lo",
        "Hernangomez","Rubio","Llull","Abalde","Brizuela","Garuba","Aldama","Fernandez","Claver","Sastre",
        "Antetokounmpo","Papanikolaou","Sloukas","Calathes","Dorsey","Mitoglou","Larentzakis","Papagiannis","Bochoridis","Walkup",
        "Watanabe","Hachimura","Togashi","Hiejima","Kawamura","Baba","Yoshii","Tominaga","Hawkinson","Josh",
        "Giddey","Mills","Exum","Ingles","Dellavedova","Baynes","Landale","Bogut","Kay","Thybulle",
        "Dieng","Fall","Ndour","Diop","Sarr","Sow","Ba","Gueye","Seck","Mbaye",
        "Bolmaro","Campazzo","Deck","Laprovittola","Scola","Garino","Delia","Brussino","Vildoza","Redivo",
    };

    public static readonly (string City, string Name, string Abbr)[] Franchises =
    {
        // East
        ("Boston","Harbor","BOS"),("New York","Empire","NYK"),("Brooklyn","Bridges","BKN"),("Philadelphia","Liberty","PHI"),("Toronto","Huskies","TOR"),
        ("Chicago","Wind","CHI"),("Cleveland","Rockers","CLE"),("Detroit","Motors","DET"),("Indiana","Racers","IND"),("Milwaukee","Brewers","MIL"),
        ("Atlanta","Phoenix","ATL"),("Charlotte","Flight","CHA"),("Miami","Tide","MIA"),("Orlando","Rockets","ORL"),("Washington","Monuments","WAS"),
        // West
        ("Denver","Peaks","DEN"),("Minnesota","Lakes","MIN"),("Oklahoma City","Storm","OKC"),("Portland","Pioneers","POR"),("Utah","Canyons","UTA"),
        ("Golden State","Gold Rush","GSW"),("Los Angeles","Stars","LAS"),("Los Angeles","Sails","LAC"),("Phoenix","Heat","PHX"),("Sacramento","Capitals","SAC"),
        ("Dallas","Rangers","DAL"),("Houston","Comets","HOU"),("Memphis","Blues","MEM"),("New Orleans","Jazz Kings","NOP"),("San Antonio","Missions","SAS"),
    };

    /// <summary>Primary, secondary colour and logo style per franchise (same order as Franchises). Styles: 0 shield, 1 circle, 2 hexagon, 3 diamond, 4 banner.</summary>
    public static readonly (string Primary, string Secondary, int Style)[] Identity =
    {
        ("#0B6E4F","#F4D35E",0),("#1D3FA3","#F26419",1),("#111111","#E8E8E8",3),("#C8102E","#1D4ED8",0),("#B91C1C","#F5F5F5",2),
        ("#D81E05","#111111",1),("#7A1D2B","#F0B323",0),("#1E3A8A","#DC2626",2),("#F2B705","#0B2545",4),("#0F5132","#E6D3A3",0),
        ("#D11F2F","#F5F5F5",3),("#1BA3A3","#4B2A7B",2),("#9B1D2A","#F9A825",4),("#0369A1","#111111",1),("#0A2A5C","#D22630",0),
        ("#0E2A47","#FDB927",3),("#0C4A6E","#78BE20",2),("#F97316","#0F4C81",1),("#111111","#E03A3E",0),("#002B5C","#F9A01B",4),
        ("#1D428A","#FFC72C",1),("#552583","#FDB927",0),("#C8102E","#1D428A",2),("#E56020","#1D1160",3),("#5A2D81","#63727A",1),
        ("#00538C","#002B5E",4),("#CE1141","#111111",0),("#12173F","#5D76A9",2),("#0C2340","#85714D",0),("#C4CED4","#111111",3),
    };

    public static readonly string[] EastDivisions = { "Atlantic", "Central", "Southeast" };
    public static readonly string[] WestDivisions = { "Northwest", "Pacific", "Southwest" };
}
