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
            "Aaron","Adrian","Aidan","Alan","Alonzo","Amari","Antoine","Antonio","Ashton","Avery","Barrett","Beau","Blake","Brady","Braden","Brent","Brett","Brian","Brock","Bryce","Byron","Calvin","Carl","Carlos","Carson","Cedric","Chance","Charles","Clarence","Clay","Clifton","Colby","Collin","Connor","Corey","Craig","Curtis","Dalton","Damon","Dane","Dante","Darnell","Darren","Darryl","David","Dawson","Dean","DeAndre","Demetrius","Derek","DeShawn","Devante","Dominic","Donovan","Douglas","Drew","Duane","Dwayne","Dylan","Eddie","Edward","Eli","Emmanuel","Eric","Ernest","Ethan","Everett","Felix","Frank","Frederick","Garrett","Gavin","George","Gerald","Gordon","Graham","Greg","Harold","Harrison","Hayden","Henry","Ian","Isaac","Jabari","Jace","Jackson","Jacob","Jake","Jamar","Jamison","Jared","Javon","Jay","Jeff","Jeremiah","Jeremy","Jerome","Jesse","Joel","John","Jonah","Jonathan","Joseph","Joshua","Julian","Julius","Justin","Kameron","Keith","Kendall","Kenny","Kenyon","Khalil","Kieran","Kyle","Lamar","Lance","Landon","Larry","Lawrence","Lee","Leon","Levi","Lincoln","Logan","Lorenzo","Lucas","Luke","Marcellus","Mario","Mark","Marquis","Martin","Mason","Matthew","Maurice","Max","Micah","Mitchell","Montrez","Nathan","Nathaniel","Neal","Nolan","Noah","Omar","Oscar","Owen","Patrick","Paul","Percy","Peyton","Preston","Quincy","Rashad","Ray","Raymond","Reed","Rickey","Robert","Rodney","Roman","Ronald","Ross","Russell","Ryan","Samuel","Sean","Seth","Shane","Shawn","Simon","Solomon","Spencer","Stephen","Steven","Terrell","Theodore","Thomas","Tim","Tony","Travis","Tristan","Troy","Tyson","Vernon","Vince","Wade","Walker","Warren","Wesley","Will","Willie","Zach","Zeke" };

    private static readonly string[] AngloLast =
    {
        "Johnson","Williams","Brown","Jones","Davis","Miller","Wilson","Moore","Taylor","Anderson",
        "Thomas","Jackson","White","Harris","Martin","Thompson","Robinson","Clark","Lewis","Walker",
        "Hall","Allen","Young","King","Wright","Scott","Green","Baker","Adams","Nelson",
        "Carter","Mitchell","Roberts","Turner","Phillips","Campbell","Parker","Evans","Edwards","Collins",
        "Stewart","Morris","Murphy","Cook","Rogers","Morgan","Cooper","Reed","Bailey","Bell",
        "Howard","Ward","Cox","Richardson","Wood","Watson","Brooks","Bennett","Gray","James",
        "Hughes","Price","Sanders","Myers","Long","Ross","Foster","Powell","Jenkins","Perry",
            "Abbott","Acker","Alexander","Allison","Andrews","Armstrong","Arnold","Atkins","Austin","Avery","Ball","Banks","Barber","Barker","Barnes","Barnett","Barrett","Barton","Bates","Battle","Beasley","Beck","Benson","Berry","Bishop","Black","Blackwell","Blair","Blake","Boone","Booth","Bowen","Bowman","Boyd","Bradley","Bradford","Brady","Branch","Bridges","Briggs","Brock","Bryan","Buchanan","Buckley","Bullock","Burgess","Burke","Burns","Burton","Bush","Byrd","Caldwell","Calhoun","Cannon","Carpenter","Carr","Carroll","Carson","Case","Cash","Chambers","Chandler","Chapman","Chase","Christian","Clayton","Clements","Cobb","Coleman","Combs","Conley","Conner","Conrad","Copeland","Craig","Crawford","Cross","Cummings","Cunningham","Curtis","Dalton","Daniel","Daniels","Dawson","Day","Dean","Dennis","Dixon","Dodson","Douglas","Doyle","Drake","Dudley","Duke","Dunn","Eaton","Elliott","Ellis","Emerson","English","Erickson","Farmer","Ferguson","Fields","Finch","Fleming","Fletcher","Flowers","Floyd","Ford","Fowler","Fox","Francis","Franklin","Freeman","Fuller","Gaines","Gamble","Gardner","Garner","Garrett","Gates","Gibbs","Gibson","Gilbert","Gill","Glover","Golden","Goodman","Goodwin","Gordon","Graham","Grant","Graves","Greer","Griffin","Grimes","Guy","Hale","Haley","Hamilton","Hammond","Hampton","Hancock","Hardy","Harmon","Harper","Harrington","Hart","Harvey","Hawkins","Haynes","Henderson","Henry","Hicks","Higgins","Hill","Hines","Hodge","Hodges","Holland","Holloway","Holmes","Holt","Hopkins","Horn","Horton","House","Houston","Howell","Hubbard","Hudson","Huff","Humphrey","Hunt","Hutchinson","Ingram","Irwin","Jacobs","Jefferson","Jennings","Jensen","Joyner","Keller","Kemp","Kennedy","Kent","Kerr","Kirby","Kirk","Knight","Knox","Lamb","Lane","Lawrence","Lawson","Leach","Lee","Lindsey","Little","Lloyd","Logan","Lowe","Lucas","Lyons","Mack","Maddox","Mann","Manning","Marsh","Marshall","Mason","Massey","Matthews","Maxwell","May","McBride","McCall","McCoy","McDaniel","McDonald","McGee","McKinney","McLean","Meadows","Mercer","Merritt","Middleton","Miles","Monroe","Montgomery","Moody","Morrow","Moss","Mullins","Neal","Newman","Newton","Nichols","Noble","Norman","Norris","Norton","Oliver","Osborne","Owens","Page","Palmer","Parks","Parsons","Patrick","Patterson","Payne","Pearson","Peck","Pennington","Peters","Peterson","Pierce","Pittman","Pope","Porter","Potter","Pratt","Preston","Pruitt","Quinn","Ramsey","Randall","Randolph","Ray","Reese","Reeves","Reid","Reynolds","Rhodes","Rice","Riley","Rivers","Robbins","Robertson","Rodgers","Rollins","Rowe","Russell","Rutledge","Sampson","Sawyer","Schultz","Sellers","Shaw","Shelton","Shepherd","Sherman","Simmons","Simpson","Sims","Singleton","Skinner","Slater","Small","Smith","Snow","Spencer","Stanley","Steele","Stephens","Stevens","Stokes","Stone","Strickland","Sullivan","Summers","Sutton","Swanson","Tate","Terry","Tillman","Townsend","Tucker","Tyler","Underwood","Vaughn","Wade","Walls","Walton","Warner","Warren","Washington","Waters","Watkins","Weaver","Webb","Webster","Welch","Wells","West","Wheeler","Whitaker","Whitfield","Wiggins","Wilcox","Wilkerson","Wilkins","Willis","Winston","Wise","Woodard","Woods","Wooten","Wyatt","York" };

    /// <summary>Weights are relative shares of a North American league's intake.</summary>
    public static readonly Heritage[] All =
    {
        new("us", 70, new[] { "USA" }, UsFirst, AngloLast,
            new[] { (Ethnicity.African, 0.70), (Ethnicity.Caucasian, 0.22), (Ethnicity.SAMed, 0.03), (Ethnicity.SouthAmerican, 0.02), (Ethnicity.Asian, 0.02), (Ethnicity.MESA, 0.01) }),
        new("canada", 5, new[] { "Canada" },
            new[] { "Shai","Jamal","Andrew","RJ","Dillon","Kelly","Bennedict","Dwight","Trey","Zach","Kyle","Cory","Tristan","Nickeil","Lu",
            "Brandon","Lucas","Owen","Liam","Connor","Mitchell","Andre","Dylan","Ethan","Noah","Cameron","Jacob","Leonard","Caleb","Ryan","Thomas","Kevin","Isaiah","Aaron" },
            AngloLast,
            new[] { (Ethnicity.African, 0.60), (Ethnicity.Caucasian, 0.33), (Ethnicity.Asian, 0.04), (Ethnicity.MESA, 0.03) }),
        new("balkan", 3, new[] { "Serbia", "Croatia", "Slovenia", "Bosnia and Herzegovina", "Montenegro" },
            new[] { "Luka","Nikola","Bogdan","Goran","Dario","Jusuf","Ante","Marko","Ivan","Dragan","Vasilije","Filip","Stefan","Aleksa","Nemanja",
            "Mihailo","Dusan","Uros","Ognjen","Milos","Mario","Josip","Tomislav","Matej","Nik" },
            new[] { "Petrovic","Jovanovic","Markovic","Nikolic","Stojanovic","Pavlovic","Popovic","Vukovic","Djordjevic","Ilic","Jovic","Micic","Ilic","Stankovic","Radovic",
            "Kovacevic","Simic","Milic","Horvat","Babic","Knezevic","Novak","Zupan","Kos","Tomic" },
            new[] { (Ethnicity.YugoGreek, 1.0) }),
        new("france", 4, new[] { "France" },
            new[] { "Evan","Rudy","Nicolas","Theo","Killian","Victor","Mathias","Bilal","Moussa","Sekou","Frank","Timothe","Elie","Guerschon","Yakuba",
            "Adam","Arthur","Louis","Hugo","Lucas","Nathan","Enzo","Mathis","Axel","Gabriel","Rayan","Mamadou","Ibrahima","Jordan","Kevin","Thomas","Maxime","Jules","Paul","Bastien","Yohan","Ludovic" },
            new[] { "Fournier","Moreau","Girard","Diallo","Traore","Hayes","Lambert","Bonnet","Rousseau","Fontaine","Diallo","Traore","Lefevre","Moreau","Girard",
            "Martin","Bernard","Dubois","Laurent","Simon","Michel","Leroy","Roux","David","Bertrand","Morel","Fournel","Mercier","Blanc","Guerin","Boyer","Garnier","Chevalier","Francois","Legrand","Gauthier","Perrin","Robin","Clement","Morin","Nicolas","Henry","Roussel","Mathieu","Gautier","Masson","Marchand","Duval","Denis","Lemaire","Kone","Camara","Cisse","Sylla","Keita" },
            new[] { (Ethnicity.Caucasian, 0.50), (Ethnicity.African, 0.42), (Ethnicity.MENA, 0.08) }),
        new("germany", 2, new[] { "Germany", "Austria" },
            new[] { "Franz","Moritz","Dennis","Daniel","Maximilian","Johannes","Andreas","Jonas","Tim","Lukas",
            "Felix","Paul","Leon","Niklas","Jan","Philipp","Florian","Sebastian","Tobias","Fabian","Benedikt","Kilian","Robin","Johann","Malte","Henrik","Erik","Lennard","Till","Ole" },
            new[] { "Schroder","Wagner","Becker","Hoffmann","Krause","Schmidt","Weber","Fischer","Meyer","Becker","Hoffmann","Krause",
            "Schneider","Schulz","Richter","Klein","Wolf","Neumann","Schwarz","Zimmermann","Braun","Hartmann","Lange","Schmitt","Werner","Kruger","Meier","Lehmann","Schmid","Schulze","Maier","Kohler","Herrmann","Konig","Walter","Mayer","Huber","Kaiser","Fuchs","Peters","Lang","Scholz","Moller","Weiss","Jung","Hahn","Vogel","Friedrich","Keller","Gunther","Frank","Berger" },
            new[] { (Ethnicity.CentralEuropean, 0.85), (Ethnicity.African, 0.15) }),
        new("spain", 2, new[] { "Spain" },
            new[] { "Santi","Ricky","Sergio","Juan","Pau","Alex","Willy","Dario","Jaime","Usman",
            "Pablo","Alvaro","Diego","Javier","Carlos","Mario","Adrian","Marc","Pol","Hugo","Lucas","Daniel","Ivan","Ruben","Victor","Oscar","Guillermo","Xavier","Jordi","Albert","Nacho","Fernando" },
            new[] { "Garcia","Ortega","Romero","Torres","Ruiz","Moreno","Jimenez","Fernandez","Alonso","Serrano","Navarro","Rodriguez",
            "Lopez","Martinez","Sanchez","Perez","Gomez","Martin","Hernandez","Diaz","Munoz","Alvarez","Gutierrez","Castillo","Ramos","Gil","Vazquez","Blanco","Molina","Morales","Suarez","Ortiz","Delgado","Castro","Marin","Rubio","Sanz","Iglesias","Medina","Garrido","Cortes","Santos","Lozano","Guerrero","Cano","Prieto","Mendez","Cruz","Calvo","Gallego","Vidal","Leon" },
            new[] { (Ethnicity.SpanMed, 0.88), (Ethnicity.African, 0.12) }),
        new("italy", 1, new[] { "Italy" },
            new[] { "Danilo","Marco","Nicolo","Simone","Achille","Matteo","Giampaolo","Alessandro","Luigi","Stefano",
            "Andrea","Francesco","Lorenzo","Davide","Federico","Riccardo","Gabriele","Tommaso","Pietro","Giovanni","Edoardo","Leonardo","Filippo","Emanuele","Giacomo","Michele","Paolo","Daniele","Fabio","Antonio" },
            new[] { "Rossi","Russo","Ferrari","Esposito","Bianchi","Romano","Ricci","Colombo","Bruno","Greco",
            "Gallo","Conti","De Luca","Mancini","Costa","Giordano","Rizzo","Lombardi","Moretti","Barbieri","Fontana","Santoro","Mariani","Rinaldi","Caruso","Ferrara","Galli","Martini","Leone","Longo","Gentile","Martinelli","Vitale","Lombardo","Serra","Coppola","De Santis","D'Angelo","Marchetti","Parisi","Villa","Conte","Ferraro","Ferri","Fabbri","Bianco","Marini","Grasso","Valentini","Messina" },
            new[] { (Ethnicity.Italmed, 0.92), (Ethnicity.African, 0.08) }),
        new("greece", 1.5, new[] { "Greece" },
            new[] { "Giannis","Kostas","Thanasis","Nikos","Vassilis","Georgios","Dimitris","Panagiotis","Ioannis","Kostis",
            "Christos","Michalis","Antonis","Alexandros","Stavros","Spyros","Evangelos","Lefteris","Thodoris","Andreas","Manolis","Stelios","Pavlos","Sotiris","Yannis","Aris","Nektarios","Fotis","Lazaros","Marios" },
            new[] { "Papadopoulos","Georgiou","Nikolaidis","Konstantinou","Alexiou","Dimitriou","Karras","Vlachos","Papadopoulos","Kalaitzakis",
            "Papadakis","Pappas","Oikonomou","Makris","Antoniou","Ioannou","Christodoulou","Vasileiou","Athanasiou","Petropoulos","Michailidis","Papageorgiou","Angelopoulos","Stavropoulos","Karagiannis","Nikolaou","Papanikolaou","Galanis","Mavridis","Theodorou","Kyriakou","Panagiotou","Raptis","Sotiriou","Lazaridis","Economou","Tsakalos","Kouris","Spanos","Fotiou" },
            new[] { (Ethnicity.YugoGreek, 0.85), (Ethnicity.African, 0.15) }),
        new("australia", 2.5, new[] { "Australia", "New Zealand" },
            new[] { "Josh","Patty","Ben","Dyson","Jock","Matisse","Joe","Xavier","Will","Jack",
            "Liam","Noah","Oliver","Lachlan","Cooper","Mitch","Brodie","Harrison","Hamish","Angus","Riley","Kyle","Sam","Toby","Declan","Blake","Tyson","Nathan","Dante","Jordan" },
            new[] { "Murray","Mills","Kelly","Ryan","O'Brien","Walsh","Hunter","Fraser","Kay","Byrne","Daniels","Green",
            "Smith","Jones","Brown","Wilson","Taylor","Johnson","White","Martin","Anderson","Thompson","Nguyen","Thomas","Walker","Harris","Lee","Robinson","King","Campbell","Clarke","Wright","Mitchell","Hughes","Edwards","Cook","Morris","Bell","Kennedy","Gibson","Stewart","Turner","Collins","Ward","Reid","Graham","Burke","Sullivan","Chapman","Bennett","Russell" },
            new[] { (Ethnicity.Caucasian, 0.78), (Ethnicity.African, 0.15), (Ethnicity.Seasian, 0.04), (Ethnicity.Asian, 0.03) }),
        new("westafrica", 3, new[] { "Senegal", "Cameroon", "Nigeria", "Mali", "Congo DR" },
            new[] { "Ousmane","Joel","Pascal","Serge","Bismack","Precious","Ibou","Amadou","Hassan","Mamadou","Chimezie","Udoka","Moussa","Cheikh","Ekpe",
            "Abdoulaye","Babacar","Cheick","Seydou","Ismaila","Boubacar","Emeka","Chukwuemeka","Ifeanyi","Tobi","Femi","Olumide","Kelechi","Landry","Christian","Aboubakar","Youssouf","Lamine","Pape","Khadim" },
            new[] { "Dieng","Fall","Ndour","Diop","Sarr","Sow","Ba","Gueye","Seck","Mbaye","Okafor","Eze","Okoro","Obi","Nwankwo",
            "Ndiaye","Faye","Diouf","Cisse","Kane","Toure","Kone","Camara","Keita","Coulibaly","Adebayo","Okeke","Nwosu","Adeyemi","Okonkwo","Mbah","Fofana","Ngando","Tchoupo","Sangare","Nkounkou","Bamba","Dembele","Sissoko" },
            new[] { (Ethnicity.African, 1.0) }),
        new("southamerica", 2, new[] { "Argentina", "Brazil", "Uruguay" },
            new[] { "Leandro","Bruno","Raul","Facundo","Gabriel","Nicolas","Tomas","Luis","Mateo","Diego",
            "Santiago","Joaquin","Agustin","Matias","Federico","Lucas","Franco","Ignacio","Martin","Juan","Pablo","Marcelo","Rafael","Thiago","Felipe","Rodrigo","Andres","Sebastian","Leonardo","Caio","Vitor","Guilherme","Henrique","Lautaro","Emiliano","Nahuel" },
            new[] { "Gomez","Diaz","Alvarez","Acosta","Benitez","Medina","Herrera","Sosa","Silva","Pereira","Souza","Costa",
            "Fernandez","Lopez","Martinez","Rodriguez","Gonzalez","Romero","Sanchez","Ramirez","Torres","Flores","Rojas","Ruiz","Castro","Vega","Molina","Morales","Suarez","Ortiz","Paz","Rios","Santos","Oliveira","Lima","Ferreira","Almeida","Ribeiro","Carvalho","Rocha","Barbosa","Melo","Cardoso","Teixeira","Nunes","Moreira","Campos","Ramos","Correa","Vidal","Cabrera","Paredes" },
            new[] { (Ethnicity.SAMed, 0.72), (Ethnicity.SouthAmerican, 0.20), (Ethnicity.African, 0.08) }),
        new("caribbean", 1, new[] { "Dominican Republic", "Puerto Rico" },
            new[] { "Karl","Al","Francisco","Angel","Jose","Edgar","Victor","Eloy","Andres","Rafael",
            "Carlos","Luis","Miguel","Juan","Pedro","Javier","Alexis","Gian","Ismael","Yohan","Wilfredo","Kevin","Jonathan","Christian","Gabriel","Emmanuel","Ramon","Felix","Hector","Manny" },
            new[] { "Vargas","Perez","Garcia","Delgado","Feliz","Liz","Rosario","Mejia","Santana","Cruz",
            "Rodriguez","Martinez","Hernandez","Lopez","Gonzalez","Torres","Rivera","Ramos","Diaz","Reyes","Morales","Ortiz","Castillo","Jimenez","Pena","Guzman","Medina","Batista","Polanco","Nunez","Tavarez","Encarnacion","Soto","Colon","Figueroa","Velez","Maldonado","Acevedo","Santiago","Ayala" },
            new[] { (Ethnicity.SouthAmerican, 0.55), (Ethnicity.African, 0.35), (Ethnicity.SAMed, 0.10) }),
        new("japan", 1, new[] { "Japan" },
            new[] { "Yuta","Rui","Yuki","Makoto","Kai","Keisei","Daiki","Ren","Shota","Haruki",
            "Takumi","Ryota","Kenta","Sota","Yuto","Hiroto","Daichi","Kosuke","Naoki","Takeshi","Tatsuya","Shun","Riku","Kazuki","Yusuke","Koki","Tomoya","Hayato","Ryusei","Kenji" },
            new[] { "Watanabe","Suzuki","Takahashi","Ito","Yamamoto","Nakamura","Kobayashi","Kato","Tanaka","Saito",
            "Sato","Yamaguchi","Matsumoto","Inoue","Kimura","Hayashi","Shimizu","Yamazaki","Mori","Abe","Ikeda","Hashimoto","Ishikawa","Ogawa","Fujita","Okada","Goto","Hasegawa","Murakami","Kondo","Ishii","Sakamoto","Endo","Aoki","Fujii","Nishimura","Fukuda","Ota","Miura","Fujiwara","Okamoto","Matsuda","Nakagawa","Harada","Ono","Takeda" },
            new[] { (Ethnicity.Asian, 0.92), (Ethnicity.African, 0.05), (Ethnicity.Caucasian, 0.03) }),
        new("china", 0.5, new[] { "China" },
            new[] { "Wei","Jun","Zhou","Hao","Lei","Ming","Yi","Qi","Tao","Bo",
            "Zhen","Yong","Rui","Jian","Xin","Feng","Kai","Long","Peng","Chao","Bin","Yuan","Gang","Lin","Hui","Jie","Wen","Zihao","Haoran","Yuhang","Junjie","Zhiwei","Dawei","Xiaolong","Tianyu","Boyang","Mingze","Yifan" },
            new[] { "Wang","Li","Zhang","Liu","Chen","Yang","Zhao","Huang","Zhou","Wu",
            "Xu","Sun","Ma","Zhu","Hu","Guo","He","Gao","Lin","Luo","Zheng","Liang","Xie","Song","Tang","Han","Feng","Deng","Cao","Peng","Zeng","Xiao","Tian","Dong","Pan","Yuan","Cai","Jiang","Yu","Du","Ye","Cheng","Wei","Su","Lu","Ding","Shen" },
            new[] { (Ethnicity.Asian, 1.0) }),
        new("philippines", 0.5, new[] { "Philippines" },
            new[] { "Kai","Dwight","Kiefer","Thirdy","Ricci","Rhenz","Carl","Juan","AJ","Scottie",
            "Jericho","Jayson","Mark","Paolo","Jun","Gabe","Matthew","Calvin","Arwind","Japeth","Jeron","Marcio","Chris","Ray","Jamie","June","Dave","Bobby","Kevin","Mike" },
            new[] { "Santos","Reyes","Aquino","Ramos","Bautista","Villanueva","Mendoza","Flores","Navarro","Rivero",
            "Cruz","Garcia","Lim","Tan","Castro","Dela Cruz","Fernandez","Lopez","Gonzales","Torres","Ramirez","Rivera","Pascual","Salazar","Domingo","Lacsamana","Delos Santos","Dizon","Pangilinan","Gatchalian","Soriano","Valdez","Macaraeg","Aguilar","Mercado","Sison","Velasco","Javier","Lacson","Dimaculangan" },
            new[] { (Ethnicity.Seasian, 1.0) }),
        new("turkey", 1, new[] { "Turkey", "Israel", "Lebanon" },
            new[] { "Alperen","Cedi","Ersan","Furkan","Sertac","Omer","Deni","Yam","Wael","Ali",
            "Mert","Emre","Burak","Kerem","Can","Baris","Onur","Kaan","Arda","Ege","Yusuf","Tamir","Itay","Lior","Noam","Gal","Omri","Elie","Fadi","Karim" },
            new[] { "Yilmaz","Kaya","Demir","Sahin","Celik","Aydin","Cohen","Levi","Khoury","Haddad",
            "Ozturk","Yildiz","Arslan","Dogan","Kilic","Aslan","Cetin","Kara","Koc","Kurt","Ozdemir","Simsek","Polat","Erdem","Korkmaz","Mizrahi","Peretz","Biton","Friedman","Avraham","Katz","Azoulay","Shapiro","Ben-David","Saade","Nassar","Karam","Aoun","Abboud","Tannous" },
            new[] { (Ethnicity.MENA, 0.85), (Ethnicity.Caucasian, 0.15) }),
        new("southasia", 0.5, new[] { "India", "Pakistan" },
            new[] { "Amjyot","Satnam","Vishesh","Arvind","Princepal","Muin","Sahaj","Rohan","Arjun","Imran",
            "Rahul","Vikram","Karan","Aditya","Siddharth","Harpreet","Gurpreet","Jaspreet","Ali","Bilal" },
            new[] { "Singh","Verma","Sharma","Kumar","Patel","Bhullar","Sekhon","Khan","Gill","Rana",
            "Gupta","Reddy","Nair","Mehta","Joshi","Malhotra","Chopra","Sandhu","Grewal","Dhillon" },
            new[] { (Ethnicity.MESA, 1.0) }),
        new("nordic", 1, new[] { "Finland", "Sweden", "Norway", "Denmark" },
            new[] { "Lauri","Mikael","Erik","Anton","Oskar","Elias","Sasu","Viktor","Gustav","Magnus",
            "Oliver","William","Emil","Lucas","Noah","Henrik","Johan","Axel","Ville","Juho" },
            new[] { "Lindqvist","Johansson","Eriksson","Nilsson","Virtanen","Korhonen","Hansen","Makinen","Heikkinen","Larsen",
            "Andersson","Karlsson","Larsson","Olsen","Pedersen","Nielsen","Jensen","Laine","Lehtonen","Salo" },
            new[] { (Ethnicity.Scandinavian, 0.90), (Ethnicity.African, 0.10) }),
        new("easterneurope", 1.5, new[] { "Lithuania", "Latvia", "Ukraine", "Russia", "Georgia" },
            new[] { "Jonas","Domantas","Mindaugas","Arturas","Kristaps","Davis","Sviatoslav","Alexey","Dmitri","Tornike",
            "Rokas","Marius","Tadas","Ignas","Janis","Rolands","Andrei","Nikita","Giorgi","Levan","Oleksandr","Bohdan" },
            new[] { "Petrauskas","Kazlauskas","Jankauskas","Berzins","Kalnins","Shevchenko","Ivanov","Petrov","Beridze","Kovalenko",
            "Stankevicius","Urbonas","Butkus","Ozolins","Krumins","Smirnov","Kuznetsov","Melnyk","Bondarenko","Tkachenko","Kapanadze","Gelashvili" },
            new[] { (Ethnicity.EECA, 1.0) }),
        // The wider world's home players (decision #111). Weight 0: they come through their own leagues, not the NABL's intake.
        // Country-specific names where a shared group above would mix them (a Lithuanian league full of Georgian names).
        new("lithuania", 0, new[] { "Lithuania" },
            new[] { "Jonas","Mindaugas","Arturas","Domantas","Rokas","Marius","Tadas","Lukas","Arnas","Gytis","Paulius","Ignas",
            "Mantas","Edgaras","Eimantas","Deividas","Justas","Martynas","Vytautas","Kestutis","Simas","Laurynas","Dovydas","Tomas","Ovidijus","Nojus","Augustas","Karolis" },
            new[] { "Kazlauskas","Jankauskas","Petrauskas","Stankevicius","Vasiliauskas","Zukauskas","Butkus","Paulauskas","Urbonas","Kavaliauskas","Navickas","Rimkus",
            "Petraitis","Kazlauskis","Jonaitis","Gudaitis","Zukas","Andriulis","Rimkevicius","Sakalauskas","Vaiciulis","Adomaitis","Simkus","Lukosevicius","Tamosiunas","Bagdonas","Mazeika","Grigas","Baranauskas","Stasiulis","Kairys","Sinkevicius","Jurkus","Zemaitis","Balciunas","Vaitkus","Mockus","Rutkauskas","Savickas","Vainauskas" },
            new[] { (Ethnicity.EECA, 1.0) }),
        new("latvia", 0, new[] { "Latvia" },
            new[] { "Kristaps","Davis","Janis","Rolands","Arturs","Rihards","Andrejs","Martins","Karlis","Edgars",
            "Aigars","Reinis","Kristers","Toms","Ronalds","Valters","Klavs","Kaspars","Emils","Ugis","Armands" },
            new[] { "Berzins","Kalnins","Ozolins","Jansons","Liepins","Krumins","Ozols","Smits","Vitols","Grinbergs",
            "Klavins","Lacis","Zarins","Purins","Eglitis","Balodis","Rozitis","Upitis","Abolins","Rudzitis","Zvirbulis","Kalejs","Petersons","Saulitis","Strazds" },
            new[] { (Ethnicity.EECA, 1.0) }),
        new("russia", 0, new[] { "Russia", "Ukraine" },
            new[] { "Alexey","Dmitri","Sergei","Andrei","Nikita","Ivan","Mikhail","Artem","Vitaly","Egor",
            "Pavel","Roman","Maxim","Anton","Kirill","Denis","Yegor","Timofey","Vladislav","Ilya" },
            new[] { "Ivanov","Petrov","Smirnov","Kuznetsov","Popov","Sokolov","Volkov","Fedorov","Morozov","Orlov","Lebedev","Kovalenko",
            "Novikov","Mikhailov","Pavlov","Semenov","Golubev","Vinogradov","Bogdanov","Vorobyov","Zaitsev","Egorov" },
            new[] { (Ethnicity.EECA, 1.0) }),
        new("serbia", 0, new[] { "Serbia", "Montenegro", "Bosnia and Herzegovina" },
            new[] { "Nikola","Bogdan","Vasilije","Stefan","Aleksa","Nemanja","Filip","Marko","Ognjen","Uros","Dusan","Milos",
            "Luka","Vladimir","Ivan","Mihajlo","Lazar","Petar","Rade","Dejan","Zoran","Vuk" },
            new[] { "Petrovic","Markovic","Nikolic","Stojanovic","Jovanovic","Ilic","Djordjevic","Pavlovic","Stankovic","Radovic","Popovic","Vukovic",
            "Milosevic","Jokovic","Simic","Lazic","Kostic","Bogdanovic","Todorovic","Ristic","Milic","Savic" },
            new[] { (Ethnicity.YugoGreek, 1.0) }),
        new("croatia", 0, new[] { "Croatia", "Slovenia" },
            new[] { "Ante","Dario","Ivan","Mario","Luka","Josip","Zoran","Jaka","Klemen","Ziga",
            "Matej","Tomislav","Kresimir","Domagoj","Filip","Roko","Nik","Rok","Tilen","Miha" },
            new[] { "Horvat","Kovacic","Babic","Maric","Juric","Novak","Kranjc","Zupan","Hrovat","Simunovic","Popovic","Kos",
            "Pavlovic","Knezevic","Vukovic","Matic","Tomic","Kovac","Golob","Mlakar","Bizjak","Vidmar" },
            new[] { (Ethnicity.YugoGreek, 1.0) }),
        new("korea", 0, new[] { "Korea" },
            new[] { "Min-jun","Seo-jun","Ji-hoon","Hyun-woo","Jae-won","Dong-hyun","Sung-min","Tae-yang","Woo-jin","Joon-ho",
            "Seung-hyun","Jun-young","Min-ho","Do-yoon","Si-woo","Ha-jun","Ye-jun","Jin-woo","Sang-woo","Kyung-min","Jae-hyun","Dong-wook" },
            new[] { "Kim","Lee","Park","Choi","Jung","Kang","Cho","Yoon","Jang","Lim","Han","Oh",
            "Shin","Kwon","Hwang","Ahn","Song","Yoo","Hong","Jeon","Ko","Moon","Yang","Son","Bae","Baek","Heo","Nam" },
            new[] { (Ethnicity.Asian, 1.0) }),
        new("benelux", 0, new[] { "Belgium", "Netherlands" },
            new[] { "Thijs","Bram","Lars","Sem","Ruben","Wout","Jelle","Kevin","Maxime","Arnaud",
            "Daan","Luuk","Milan","Stijn","Tim","Niels","Jens","Bas","Pieter","Joris","Elias","Mathis","Jonas","Robbe" },
            new[] { "de Jong","Jansen","Peeters","Janssens","Maes","Visser","Smits","Mulder","de Vries","Claes","Willems","Bakker",
            "Bos","Vos","Meijer","van Dijk","de Boer","van den Berg","Dekker","Brouwer","Hendriks","Dubois","Lambert","Jacobs","Mertens","Wouters","Goossens","Vermeulen","Desmet","De Smet","Hermans","Michiels" },
            new[] { (Ethnicity.CentralEuropean, 0.80), (Ethnicity.African, 0.20) }),
        new("centraleurope", 0, new[] { "Poland", "Czechia" },
            new[] { "Mateusz","Jakub","Michal","Tomasz","Kamil","Ondrej","Jan","Lukas","Petr","Adam",
            "Piotr","Krzysztof","Pawel","Marcin","Bartosz","Dawid","Szymon","Filip","Wojciech","Lukasz","Martin","David","Tomas","Vojtech","Matej","Jiri","Pavel","Patrik","Dominik","Radek" },
            new[] { "Nowak","Kowalski","Wisniewski","Wojcik","Kaminski","Novak","Svoboda","Dvorak","Cerny","Prochazka","Zielinski","Lewandowski",
            "Wozniak","Kozlowski","Jankowski","Mazur","Kwiatkowski","Krawczyk","Piotrowski","Grabowski","Nowakowski","Pawlowski","Michalski","Adamczyk","Dudek","Zajac","Wieczorek","Jablonski","Majewski","Olszewski","Jaworski","Malinowski","Novotny","Vesely","Horak","Nemec","Marek","Pospisil","Pokorny","Hajek","Kral","Jelinek" },
            new[] { (Ethnicity.CentralEuropean, 0.75), (Ethnicity.EECA, 0.25) }),
        new("baltic", 0, new[] { "Estonia", "Belarus", "Kazakhstan" },
            new[] { "Kristjan","Martin","Sander","Rait","Artem","Maksim","Yuri","Nurlan","Alibek","Pavel",
            "Kaspar","Marten","Siim","Henri","Kaarel","Andrei","Dmitry","Arman","Daniyar","Yerlan" },
            new[] { "Tamm","Saar","Sepp","Kask","Novik","Kovalchuk","Petrenko","Abenov","Nurpeisov","Volkov","Ilves","Lepik",
            "Kuusk","Mets","Koppel","Vaher","Rebane","Kozlov","Zhukov","Seitkali","Omarov","Zhumabekov" },
            new[] { (Ethnicity.EECA, 0.85), (Ethnicity.Asian, 0.15) }),
        new("macedonia", 0, new[] { "North Macedonia" },
            new[] { "Bojan","Darko","Aleksandar","Kiril","Vlatko","Damjan","Dimitar","Andrej","Pero","Toni",
            "Marko","Stefan","Nikola","Filip","Goran" },
            new[] { "Stojanovski","Petrovski","Nikolovski","Dimitrovski","Trajkovski","Angelov","Ristovski","Jovanovski","Georgiev","Kostov",
            "Spasovski","Velkovski","Mitrevski","Stefanovski","Ilievski" },
            new[] { (Ethnicity.YugoGreek, 1.0) }),
        new("mexico", 0, new[] { "Mexico" },
            new[] { "Jorge","Gustavo","Paul","Francisco","Alejandro","Emilio","Fernando","Ricardo","Hector","Ivan",
            "Carlos","Luis","Miguel","Juan","Jose","Daniel","Diego","Eduardo","Roberto","Sergio","Arturo","Javier","Manuel","Ruben","Omar","Adrian","Rodrigo","Oscar","Pablo","Cesar" },
            new[] { "Hernandez","Lopez","Ramirez","Gonzalez","Martinez","Sanchez","Ayala","Gutierrez","Reyes","Flores","Morales","Ortiz",
            "Torres","Rivera","Perez","Garcia","Cruz","Ramos","Vazquez","Mendoza","Castillo","Jimenez","Moreno","Romero","Herrera","Medina","Aguilar","Vargas","Castro","Guzman","Juarez","Dominguez","Rojas","Salazar","Contreras","Luna","Estrada","Rios","Navarro","Delgado","Cervantes","Fuentes" },
            new[] { (Ethnicity.SouthAmerican, 0.70), (Ethnicity.SAMed, 0.25), (Ethnicity.African, 0.05) }),
        new("northafrica", 0, new[] { "Egypt", "Tunisia", "Morocco" },
            new[] { "Ahmed","Mohamed","Omar","Youssef","Karim","Anas","Assem","Ehab","Makram","Hamza",
            "Mahmoud","Ali","Hassan","Mostafa","Tarek","Amine","Mehdi","Ayoub","Bilel","Walid" },
            new[] { "Hassan","Mahmoud","Ibrahim","Ben Romdhane","Abdelfattah","Mohsen","Lahyani","El Amrani","Bennani","Haddadi",
            "Saleh","Farouk","Abdallah","Kamel","Rahmani","Mansouri","Trabelsi","Gharbi","Alaoui","Idrissi" },
            new[] { (Ethnicity.MENA, 0.85), (Ethnicity.African, 0.15) }),
        new("africa", 0, new[] { "Angola", "Rwanda", "South Africa", "Ivory Coast" },
            new[] { "Childe","Yanick","Olimpio","Kenny","Axel","Dieudonne","Thabo","Sipho","Souleymane","Mathieu",
            "Bruno","Gerson","Leonel","Jean","Claude","Emmanuel","Kagiso","Lebo","Ibrahim","Drissa" },
            new[] { "Dundao","Moreira","Cipriano","Gasana","Mukama","Nkosi","Dlamini","Kone","Coulibaly","Kouassi","Ndayisaba","Gomes",
            "Dos Santos","Mendes","Lopes","Silva","Hakizimana","Mokoena","Khumalo","Traore","Diabate","Yao" },
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
