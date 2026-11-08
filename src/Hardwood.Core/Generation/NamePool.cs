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

    public static readonly string[] EastDivisions = { "Atlantic", "Central", "Southeast" };
    public static readonly string[] WestDivisions = { "Northwest", "Pacific", "Southwest" };
}
