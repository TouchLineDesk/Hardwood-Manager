using Hardwood.Core.Model;

namespace Hardwood.Core.World;

public enum CompetitionKind
{
    /// <summary>A domestic league that owns its clubs and plays a full season.</summary>
    Domestic,
    /// <summary>A club competition across countries; its clubs belong to domestic leagues (or the guest pool).</summary>
    Continental,
    /// <summary>Clubs that only play in a continental competition (their domestic leagues are not simulated).</summary>
    GuestPool,
}

/// <summary>A fictional club: city, nickname, abbreviation and (for guest clubs) country.</summary>
public sealed record ClubSeed(string City, string Name, string Abbr, string? Country = null);

/// <summary>One playoff round: name, series length and the weekdays games are played on.</summary>
public sealed record PlayoffRound(string Name, int BestOf, int Teams);

/// <summary>
/// Everything that defines a competition outside the NABL: clubs, strength, who counts as local, money,
/// rules and calendar. These are data, so new leagues are added here without new code.
/// </summary>
public sealed class CompetitionProfile
{
    public required string Key { get; init; }
    /// <summary>Display names: settable so a name pack can rename the competition (Names.NamePack).</summary>
    public required string Name { get; set; }
    public required string Short { get; set; }
    public CompetitionKind Kind { get; init; } = CompetitionKind.Domestic;
    /// <summary>Country of a domestic league; players of these nationalities are local.</summary>
    public string Country { get; init; } = "";
    public string[] LocalNationalities { get; init; } = Array.Empty<string>();
    public ClubSeed[] Clubs { get; init; } = Array.Empty<ClubSeed>();

    /// <summary>Shift (in CA) of this league's rosters against the European base table.</summary>
    public int StrengthOffset { get; init; }
    /// <summary>Most non-local players a club may carry (domestic rule; continental competitions have none).</summary>
    public int ForeignLimit { get; init; } = 99;
    /// <summary>Imports fill the top roster slots (China, Australia) rather than being spread through the roster.</summary>
    public bool ImportsAreStars { get; init; }
    /// <summary>Salaries as a share of the NABL market for the same player.</summary>
    public double SalaryScale { get; init; } = 0.12;
    /// <summary>Youngest age a player may turn professional here (23 in the USA, decision #75); 0 = no rule.</summary>
    public int MinAge { get; init; }
    /// <summary>How hard this league's jobs are to get compared with the NABL's (added to a club's prestige).</summary>
    public double JobPrestige { get; init; } = -10;

    public Rules Rules { get; init; } = Rules.Fiba;
    /// <summary>Points per team per game in quick results (0: the rules' default, ~80 FIBA / ~101 NBA). Each league its own style (critic review #2).</summary>
    public double ScoringLevel { get; init; }

    /// <summary>The scoring level quick results aim for.</summary>
    public double PointsPerTeam => ScoringLevel > 0 ? ScoringLevel
        : Styles.TryGetValue(Key, out var v) ? v
        : UsaPyramid.IsPyramid(Key) ? UsaPyramid.Level(Key) switch { 2 => 110, 3 => 105, 4 => 100, _ => 96 }
        : Rules.GameMinutes >= 48 ? 101 : 80;

    /// <summary>Real-world-ish scoring by league: China and the Philippines run, Greece grinds.</summary>
    private static readonly Dictionary<string, double> Styles = new()
    {
        ["esp"] = 82, ["tur"] = 80, ["aus"] = 89, ["chn"] = 101, ["eel"] = 79,
        ["ltu"] = 81, ["aba"] = 80, ["ger"] = 84, ["fra"] = 82, ["ita"] = 80, ["gre"] = 76, ["isr"] = 85, ["vtb"] = 82,
        ["pol"] = 81, ["bal"] = 83, ["cze"] = 80, ["bnx"] = 81, ["arg"] = 79, ["bra"] = 81, ["pur"] = 86, ["mex"] = 85,
        ["jpn"] = 81, ["kor"] = 83, ["phi"] = 96, ["afr"] = 77, ["ecc"] = 80, ["amc"] = 82,
    };

    // ---- calendar (relative to the season's start year Y) ----
    /// <summary>Round-robin passes: 2 = home and away against everyone, 4 = twice each way.</summary>
    public int RoundRobins { get; init; } = 2;
    /// <summary>First possible game date (month, day) in year Y.</summary>
    public (int Month, int Day) Start { get; init; } = (10, 1);
    /// <summary>Weekdays rounds are played on (one round per listed day). Domestic: weekends; continental: midweek.</summary>
    public DayOfWeek[] RoundDays { get; init; } = { DayOfWeek.Saturday };
    /// <summary>Spread a round's games over the round day and the next day (Saturday + Sunday).</summary>
    public bool TwoDayRounds { get; init; }
    /// <summary>Weekdays playoff games are played on.</summary>
    public DayOfWeek[] PlayoffDays { get; init; } = { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Saturday };
    public PlayoffRound[] Playoffs { get; init; } = Array.Empty<PlayoffRound>();
    /// <summary>Final Four: the last two rounds are single games at one venue on fixed days in late May of Y+1.</summary>
    public bool FinalFour { get; init; }
    /// <summary>Danny's division format (D36, run 30): with two conferences of four divisions of four, the top two in each division
    /// play a 16-team bracket by conference (<see cref="Playoffs"/>: first round, conference semi-finals, conference finals, final)
    /// and the third-placed teams play their own tournament alongside. Any other layout crowns the table's leader.</summary>
    public bool DivisionPlayoffs { get; init; }

    /// <summary>Continental competition: how many clubs each domestic league sends (by key), plus the guest pool.</summary>
    public (string LeagueKey, int Clubs)[] Entrants { get; init; } = Array.Empty<(string, int)>();
    /// <summary>Entrants in careers made before the leagues in <see cref="Entrants"/> existed (used when any of them is missing).</summary>
    public (string LeagueKey, int Clubs)[] LegacyEntrants { get; init; } = Array.Empty<(string, int)>();
    /// <summary>Only kept so older careers load: new careers don't create it.</summary>
    public bool Legacy { get; init; }

    public override string ToString() => Name;
}

/// <summary>The first world (decision #39): a pan-European elite competition, Spain, Turkey, Australia and China.</summary>
public static class Competitions
{
    public const string Elite = "eel", Spain = "esp", Turkey = "tur", Australia = "aus", China = "chn", Guests = "eur-guests";

    /// <summary>Every competition outside the NABL: the first world (decision #39), the USA pyramid (decision #78) and the wider world (decision #111).</summary>
    public static readonly CompetitionProfile[] All;

    static Competitions() => All = Base.Concat(UsaPyramid.Profiles).Concat(WorldLeagues.Profiles).ToArray();

    /// <summary>Competitions a new career creates (everything but the legacy guest pool).</summary>
    public static IEnumerable<CompetitionProfile> Current => All.Where(p => !p.Legacy);

    private static readonly CompetitionProfile[] Base =
    {
        new()
        {
            Key = Elite, Name = "Euro Elite League", Short = "EEL", Kind = CompetitionKind.Continental,
            Start = (10, 6), RoundDays = new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday },
            PlayoffDays = new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday },
            Playoffs = new[] { new PlayoffRound("Quarter-finals", 5, 8), new PlayoffRound("Final Four semi-finals", 1, 4), new PlayoffRound("Final", 1, 2) },
            FinalFour = true,
            Entrants = WorldLeagues.EliteEntrants,
            LegacyEntrants = new[] { (Spain, 5), (Turkey, 4), (Guests, 9) },
        },
        new()
        {
            Key = Spain, Name = "Spanish League", Short = "ESP", Country = "Spain", LocalNationalities = new[] { "Spain" },
            StrengthOffset = 0, ForeignLimit = 5, SalaryScale = 0.11, JobPrestige = -8,
            Start = (9, 26), RoundDays = new[] { DayOfWeek.Saturday }, TwoDayRounds = true,
            Playoffs = new[] { new PlayoffRound("Quarter-finals", 3, 8), new PlayoffRound("Semi-finals", 5, 4), new PlayoffRound("Final", 5, 2) },
            Clubs = new ClubSeed[]
            {
                new("Madrid", "Capitales", "MAD"), new("Barcelona", "Navegantes", "BCN"), new("Valencia", "Naranjeros", "VLC"),
                new("Vitoria", "Alaveses", "VIT"), new("Malaga", "Boquerones", "MLG"), new("Murcia", "Huertanos", "MUR"),
                new("Tenerife", "Volcanes", "TFE"), new("Gran Canaria", "Alisios", "GCA"), new("Badalona", "Verdinegros", "BDN"),
                new("Zaragoza", "Cierzo", "ZAR"), new("Manresa", "Tigres", "MNR"), new("Bilbao", "Leones", "BIO"),
                new("Granada", "Nazaries", "GRX"), new("Andorra", "Pirineus", "AND"), new("Girona", "Muralles", "GRO"),
                new("Burgos", "Castellanos", "BUR"), new("Lugo", "Celtas", "LUG"), new("Sevilla", "Giraldas", "SVQ"),
            },
        },
        new()
        {
            Key = Turkey, Name = "Turkish Super League", Short = "TUR", Country = "Turkey", LocalNationalities = new[] { "Turkey" },
            StrengthOffset = -3, ForeignLimit = 6, SalaryScale = 0.12, JobPrestige = -10,
            Start = (10, 3), RoundDays = new[] { DayOfWeek.Saturday }, TwoDayRounds = true,
            Playoffs = new[] { new PlayoffRound("Quarter-finals", 3, 8), new PlayoffRound("Semi-finals", 5, 4), new PlayoffRound("Final", 7, 2) },
            Clubs = new ClubSeed[]
            {
                new("Istanbul", "Kartallar", "IKA"), new("Istanbul", "Sarilar", "ISA"), new("Istanbul", "Bogazici", "IBO"),
                new("Istanbul", "Halic", "IHA"), new("Ankara", "Baskent", "ANK"), new("Izmir", "Korfez", "IZM"),
                new("Bursa", "Uludag", "BRS"), new("Antalya", "Akdeniz", "AYT"), new("Gaziantep", "Sahinler", "GZT"),
                new("Konya", "Selcuklu", "KYA"), new("Mersin", "Limanlar", "MER"), new("Samsun", "Karadeniz", "SSX"),
                new("Trabzon", "Firtina", "TZX"), new("Eskisehir", "Porsuk", "ESK"), new("Kayseri", "Erciyes", "ASR"),
                new("Manisa", "Spil", "MNS"),
            },
        },
        new()
        {
            Key = Australia, Name = "Australian League", Short = "AUS", Country = "Australia", LocalNationalities = new[] { "Australia", "New Zealand" },
            StrengthOffset = -10, ForeignLimit = 3, ImportsAreStars = true, SalaryScale = 0.07, JobPrestige = -20,
            RoundRobins = 4, Start = (9, 18), RoundDays = new[] { DayOfWeek.Thursday, DayOfWeek.Sunday },
            PlayoffDays = new[] { DayOfWeek.Thursday, DayOfWeek.Sunday },
            Playoffs = new[] { new PlayoffRound("Semi-finals", 3, 4), new PlayoffRound("Grand Final", 5, 2) },
            Clubs = new ClubSeed[]
            {
                new("Sydney", "Harbourmen", "SYD"), new("Melbourne", "Laneways", "MEL"), new("Melbourne", "Southern Cross", "MSC"),
                new("Perth", "Black Swans", "PER"), new("Adelaide", "Vignerons", "ADL"), new("Brisbane", "Rivermen", "BNE"),
                new("Cairns", "Reef", "CNS"), new("Tasmania", "Tigers", "TAS"), new("Illawarra", "Steelers", "ILW"),
                new("Auckland", "Harbour Hawks", "AKL"),
            },
        },
        new()
        {
            Key = China, Name = "China Pro League", Short = "CPL", Country = "China", LocalNationalities = new[] { "China" },
            StrengthOffset = -14, ForeignLimit = 2, ImportsAreStars = true, SalaryScale = 0.09, JobPrestige = -16,
            Start = (10, 14), RoundDays = new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday },
            PlayoffDays = new[] { DayOfWeek.Tuesday, DayOfWeek.Friday, DayOfWeek.Sunday },
            Playoffs = new[] { new PlayoffRound("Quarter-finals", 5, 8), new PlayoffRound("Semi-finals", 5, 4), new PlayoffRound("Finals", 7, 2) },
            Clubs = new ClubSeed[]
            {
                new("Beijing", "Dragons", "BJD"), new("Beijing", "Imperials", "BJI"), new("Shanghai", "Tide", "SHA"),
                new("Guangdong", "Pearl River", "GDP"), new("Shenzhen", "Aviators", "SZN"), new("Liaoning", "Ironmen", "LIA"),
                new("Xinjiang", "Snow Leopards", "XJS"), new("Zhejiang", "Silk", "ZJS"), new("Shandong", "Mountaineers", "SDM"),
                new("Shanxi", "Loong", "SXL"), new("Jiangsu", "Mariners", "JSU"), new("Sichuan", "Pandas", "SCP"),
                new("Tianjin", "Pioneers", "TJN"), new("Qingdao", "Eagles", "QDE"), new("Fujian", "Seafarers", "FJS"),
                new("Guangzhou", "Rams", "GZR"), new("Nanjing", "Plum Blossoms", "NJG"), new("Ningbo", "Harbour", "NGB"),
                new("Wuhan", "Cranes", "WUH"), new("Chongqing", "Mist", "CQM"),
            },
        },
        new()
        {
            // The first world's stand-ins for Europe's other leagues (decision #39); replaced by real leagues (decision #111).
            Key = Guests, Name = "Other European clubs", Short = "EUR", Kind = CompetitionKind.GuestPool, Legacy = true,
            StrengthOffset = 4, ForeignLimit = 6, SalaryScale = 0.14,
            Clubs = new ClubSeed[]
            {
                new("Athens", "Hoplites", "ATH", "Greece"), new("Piraeus", "Triremes", "PIR", "Greece"),
                new("Belgrade", "White Eagles", "BEG", "Serbia"), new("Milan", "Meneghini", "MIL", "Italy"),
                new("Bologna", "Torri", "BLQ", "Italy"), new("Paris", "Lutetia", "PAR", "France"),
                new("Munich", "Bavarians", "MUC", "Germany"), new("Kaunas", "Knights", "KUN", "Lithuania"),
                new("Tel Aviv", "Coast", "TLV", "Israel"),
            },
        },
    };

    public static CompetitionProfile Get(string key) => All.First(p => p.Key == key);

    /// <summary>Region groups for pickers: club cups, the USA, Europe, the Americas, Asia-Pacific and Africa.</summary>
    public static readonly string[] Regions = { "Club cups", "USA", "Europe", "Americas", "Asia-Pacific", "Africa" };

    public static string RegionOf(CompetitionProfile p)
    {
        if (p.Kind == CompetitionKind.Continental) return "Club cups";
        if (UsaPyramid.IsPyramid(p.Key)) return "USA";
        return p.Key switch
        {
            Australia or China or WorldLeagues.Japan or WorldLeagues.Korea or WorldLeagues.Philippines => "Asia-Pacific",
            WorldLeagues.Argentina or WorldLeagues.Brazil or WorldLeagues.PuertoRico or WorldLeagues.Mexico => "Americas",
            WorldLeagues.Africa => "Africa",
            _ => "Europe",
        };
    }
}
