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

/// <summary>A club: city, nickname, abbreviation and (for guest clubs) country. A real club abroad (<see cref="RealClubs"/>, run 217)
/// also carries its name as written, its arena and capacity, its place in the real hierarchy and the made-up name it replaced.</summary>
public sealed record ClubSeed(string City, string Name, string Abbr, string? Country = null)
{
    /// <summary>The name as written when "City Name" doesn't read right ("FC Barcelona", "Zalgiris Kaunas").</summary>
    public string? Full { get; init; }
    public string? Arena { get; init; }
    /// <summary>The arena's real capacity (0: drawn at random, as for the made-up clubs).</summary>
    public int Capacity { get; init; }
    /// <summary>Added to the club's roster quality in a new world (in CA): the giants are strong, the newcomers weak.</summary>
    public int Standing { get; init; }
    /// <summary>The made-up club this one replaced, "City|Name" (renamed on load in older saves).</summary>
    public string? Was { get; init; }
}

/// <summary>One playoff round: name, series length and the weekdays games are played on.</summary>
public sealed record PlayoffRound(string Name, int BestOf, int Teams);

/// <summary>
/// Everything that defines a competition outside the NABL: clubs, strength, who counts as local, money,
/// rules and calendar. These are data, so new leagues are added here without new code.
/// </summary>
public sealed class CompetitionProfile
{
    public required string Key { get; init; }
    /// <summary>Display names: settable so a name pack can rename the competition (Names.NamePack). In a real-world career a
    /// league with real-world names (<see cref="RealName"/>, Danny's D44, #411) shows those unless a pack has renamed it.</summary>
    public required string Name { get => Names.Brand.Real && RealName != null && !_renamed ? RealName : _name; set { _name = value; _renamed = _init; _init = true; } }
    public required string Short { get => Names.Brand.Real && RealShort != null && !_renamedShort ? RealShort : _short; set { _short = value; _renamedShort = _initShort; _initShort = true; } }
    private string _name = "", _short = "";
    private bool _init, _initShort, _renamed, _renamedShort;
    /// <summary>The name in real-world careers (D44): tier 2 is the NBA G League, and so on.</summary>
    public string? RealName { get; init; }
    public string? RealShort { get; init; }
    /// <summary>The name as built, whatever the career (for the made-up world and tests).</summary>
    public string BaseName => _name;
    public string BaseShort => _short;
    /// <summary>Back to the built names, as if no pack had renamed it (run 108: a reset used to count as a rename, which hid
    /// the real-world names, D44).</summary>
    public void ResetNames(string name, string shortName) { _name = name; _short = shortName; _renamed = _renamedShort = false; }
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
    /// <summary>Youngest age a player may turn professional here (22 in the USA, decision #75; D80 A); 0 = no rule.</summary>
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

    /// <summary>Run 222 (the real formats): the last regular-season date (month, day) in year Y+1. When set, the rounds are spread
    /// evenly over the <see cref="RoundDays"/> between <see cref="Start"/> and this date (the EuroLeague's double-round weeks),
    /// rather than played on consecutive round days.</summary>
    public (int Month, int Day)? RegularEnd { get; init; }
    /// <summary>Run 222: a play-in before the first playoff round (the EuroLeague's): the top <c>Playoffs[0].Teams − 2</c> go
    /// straight in; the next four play 7v8 (the winner is the 7th seed) and 9v10, then the 7v8 loser meets the 9v10 winner for
    /// the 8th seed. Single games at the higher seed's arena.</summary>
    public bool PlayIn { get; init; }
    /// <summary>Run 222: a continental competition's licensed clubs, as "City|Name" (the EuroLeague's long-term licences and this
    /// season's wild cards). They enter whatever their domestic finish; the rest of <see cref="Size"/> is filled from the
    /// <see cref="Entrants"/> leagues' tables, one club from each league in turn. A member a name pack renamed is just a club.</summary>
    public string[] Members { get; init; } = Array.Empty<string>();
    /// <summary>Run 227 (the EuroCup's real format): the regular season is played in this many groups (snake-drawn by strength),
    /// each its own round robin of <see cref="RoundRobins"/> passes. With two groups and an "Eighth-finals" first round, the top
    /// two in each group go straight to the quarter-finals and 3rd-6th play the eighth-finals across the groups (A3 v B6, B3 v A6,
    /// A4 v B5, B4 v A5). 1: one table, as before.</summary>
    public int Groups { get; init; } = 1;
    /// <summary>Run 222: the number of clubs when <see cref="Members"/> are set (0: the sum of <see cref="Entrants"/>).</summary>
    public int Size { get; init; }

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
            // Run 222 (the real format): 20 clubs, home and away (38 rounds, from the last days of September to mid-April with double-round weeks),
            // a play-in for 7th-10th, best-of-five quarter-finals and the Final Four in late May.
            Key = Elite, Name = "Euro Elite League", Short = "EEL", RealName = "EuroLeague", Kind = CompetitionKind.Continental,
            Start = (9, 29), RegularEnd = (4, 17), RoundDays = new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday },
            PlayoffDays = new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday },
            Playoffs = new[] { new PlayoffRound("Quarter-finals", 5, 8), new PlayoffRound("Final Four semi-finals", 1, 4), new PlayoffRound("Final", 1, 2) },
            FinalFour = true, PlayIn = true,
            Members = WorldLeagues.EliteMembers, Size = 20,
            Entrants = WorldLeagues.EliteEntrants,
            LegacyEntrants = new[] { (Spain, 5), (Turkey, 4), (Guests, 9) },
        },
        new()
        {
            Key = Spain, Name = "Spanish League", Short = "ESP", RealName = "Liga ACB", Country = "Spain", LocalNationalities = new[] { "Spain" },
            StrengthOffset = 0, ForeignLimit = 5, SalaryScale = 0.11, JobPrestige = -8,
            Start = (9, 26), RoundDays = new[] { DayOfWeek.Saturday }, TwoDayRounds = true,
            Playoffs = new[] { new PlayoffRound("Quarter-finals", 3, 8), new PlayoffRound("Semi-finals", 5, 4), new PlayoffRound("Final", 5, 2) },
            Clubs = RealClubs.Spain,
        },
        new()
        {
            Key = Turkey, Name = "Turkish Super League", Short = "TUR", RealName = "Basketbol Süper Ligi", Country = "Turkey", LocalNationalities = new[] { "Turkey" },
            StrengthOffset = -3, ForeignLimit = 6, SalaryScale = 0.12, JobPrestige = -10,
            Start = (10, 3), RoundDays = new[] { DayOfWeek.Saturday }, TwoDayRounds = true,
            Playoffs = new[] { new PlayoffRound("Quarter-finals", 3, 8), new PlayoffRound("Semi-finals", 5, 4), new PlayoffRound("Final", 7, 2) },
            Clubs = RealClubs.Turkey,
        },
        new()
        {
            Key = Australia, Name = "Australian League", Short = "AUS", RealName = "National Basketball League", Country = "Australia", LocalNationalities = new[] { "Australia", "New Zealand" },
            StrengthOffset = -10, ForeignLimit = 3, ImportsAreStars = true, SalaryScale = 0.07, JobPrestige = -20,
            RoundRobins = 4, Start = (9, 18), RoundDays = new[] { DayOfWeek.Thursday, DayOfWeek.Sunday },
            PlayoffDays = new[] { DayOfWeek.Thursday, DayOfWeek.Sunday },
            Playoffs = new[] { new PlayoffRound("Semi-finals", 3, 4), new PlayoffRound("Grand Final", 5, 2) },
            Clubs = RealClubs.Australia,
        },
        new()
        {
            Key = China, Name = "China Pro League", Short = "CPL", RealName = "Chinese Basketball Association", Country = "China", LocalNationalities = new[] { "China" },
            StrengthOffset = -14, ForeignLimit = 2, ImportsAreStars = true, SalaryScale = 0.09, JobPrestige = -16,
            Start = (10, 14), RoundDays = new[] { DayOfWeek.Wednesday, DayOfWeek.Saturday },
            PlayoffDays = new[] { DayOfWeek.Tuesday, DayOfWeek.Friday, DayOfWeek.Sunday },
            Playoffs = new[] { new PlayoffRound("Quarter-finals", 5, 8), new PlayoffRound("Semi-finals", 5, 4), new PlayoffRound("Finals", 7, 2) },
            Clubs = RealClubs.China,
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
