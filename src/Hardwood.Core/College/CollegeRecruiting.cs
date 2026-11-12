using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>A high-school prospect's recruiting profile (decision #162): his school, his class, the recruiting services'
/// view of him and where he's committed.</summary>
public sealed class Recruit
{
    public string HighSchool { get; set; } = "";
    /// <summary>A real US state (two-letter code).</summary>
    public string State { get; set; } = "";
    /// <summary>The summer he enrolls in college (at 18). "Class of 2028".</summary>
    public int ClassOf { get; set; }
    /// <summary>5 to 2 (decision #162: about 25 five-stars, 80 four-stars and 300 three-stars a class; the rest 2-star).</summary>
    public int Stars { get; set; }
    /// <summary>National rank within his class.</summary>
    public int Rank { get; set; }
    /// <summary>The services' projection of his ability at 23 (their opinion, not the truth: busts and sleepers happen).</summary>
    public int Projection { get; set; }
    /// <summary>How the services misjudge him, fixed for life (they keep the same blind spot as he grows).</summary>
    public int Bias { get; set; }
    public int? CommittedTo { get; set; }
    public DateOnly? CommittedOn { get; set; }

    // The recruiting trail (C3, decision #173): the coached program's own courting. Only one program is courting in
    // the player's name; when the coach changes jobs the old program's effort no longer counts (see CourtedBy).
    /// <summary>The program whose effort the fields below record.</summary>
    public int? CourtedBy { get; set; }
    /// <summary>Goodwill toward that program, 0-100: calls, visits and an offer. Fades 1% a week.</summary>
    public double Interest { get; set; }
    public bool OnBoard { get; set; }
    public bool Scouted { get; set; }
    public int Calls { get; set; }
    /// <summary>The Monday of the week he was last called (one call a week counts).</summary>
    public DateOnly? CalledWeek { get; set; }
    public bool HomeVisit { get; set; }
    public bool OfficialVisit { get; set; }
    public bool Offered { get; set; }
}

/// <summary>
/// The classic-era roster model (Danny, Nov 11, decisions #159-#167): American players spend ages 14-17 in high school,
/// in a national recruiting pool with star ratings and class rankings; seniors commit to a college during the season and
/// enroll at 18; each program carries 13 scholarships and 2 walk-ons; everyone turns pro at 23 (no early entry, no portal,
/// no NIL). Unsigned graduates leave the game world. Careers from before it keep 30-man rosters from 14 (<see cref="Recruiting"/> false).
/// </summary>
public sealed partial class CollegeWorld
{
    public const int Scholarships = 13, WalkOns = 2, Squad = Scholarships + WalkOns, EnrollAge = 18;
    /// <summary>High-school prospects tracked per class (the services rank about a thousand; the rest aren't followed).</summary>
    public const int ClassTracked = 1000;
    public const int FiveStars = 25, FourStars = 80, ThreeStars = 300;
    /// <summary>The spring signing day: every senior still uncommitted signs where there's room, or doesn't.</summary>
    public static DateOnly SigningDay(int classOf) => new(classOf, 4, 15);
    /// <summary>Commitments open on September 1 of a senior's last high-school year.</summary>
    public static DateOnly CommitmentsOpen(int classOf) => new(classOf - 1, 9, 1);

    /// <summary>True once this world uses high-school recruiting and 15-man rosters (decision #162).</summary>
    public bool Recruiting { get; set; }
    /// <summary>High-school prospects aged 14-17 (no team).</summary>
    public List<Player> Prospects { get; } = new();
    public Dictionary<int, Recruit> Recruits { get; } = new();
    /// <summary>Roster players without a scholarship.</summary>
    public HashSet<int> WalkOnIds { get; } = new();
    /// <summary>The last day the weekly recruiting step has seen.</summary>
    public DateOnly? RecruitingDay { get; set; }

    public int RosterTarget => Recruiting ? Squad : RosterSize;
    public Recruit? RecruitOf(Player p) => Recruits.GetValueOrDefault(p.Id);
    public IEnumerable<Player> ClassOf(int year) => Prospects.Where(p => Recruits.TryGetValue(p.Id, out var r) && r.ClassOf == year);
    /// <summary>The class of high-school seniors being recruited now (it enrolls the following summer). The pros' summer can
    /// start before July (the NABL finishes in May), and once it has, last year's seniors are already in college.</summary>
    public int SeniorClass => Prospects.Count > 0 ? Prospects.Min(p => Recruits.TryGetValue(p.Id, out var r) ? r.ClassOf : int.MaxValue) : Season.Year + 1;

    public static string StarText(int stars) => stars <= 0 ? "unrated" : $"{stars}-star";

    // ------------------------------------------------------------------------------------------
    // Geography: high schools in real states; each program has a home state, so distance from home matters.

    private static readonly (string Code, string Name, string Region, double Weight)[] StateTable =
    {
        ("CA", "California", "West", 10), ("TX", "Texas", "Southwest", 9), ("FL", "Florida", "Southeast", 7), ("GA", "Georgia", "Southeast", 6),
        ("NY", "New York", "Northeast", 6), ("NC", "North Carolina", "Southeast", 5), ("IL", "Illinois", "Midwest", 5), ("OH", "Ohio", "Midwest", 4),
        ("PA", "Pennsylvania", "Northeast", 4), ("MD", "Maryland", "Northeast", 4), ("VA", "Virginia", "Southeast", 4), ("MI", "Michigan", "Midwest", 4),
        ("NJ", "New Jersey", "Northeast", 4), ("IN", "Indiana", "Midwest", 3), ("TN", "Tennessee", "Southeast", 3), ("LA", "Louisiana", "Southeast", 3),
        ("AL", "Alabama", "Southeast", 3), ("WA", "Washington", "West", 3), ("AZ", "Arizona", "Southwest", 3), ("KY", "Kentucky", "Southeast", 2),
        ("MO", "Missouri", "Midwest", 2), ("SC", "South Carolina", "Southeast", 2), ("MS", "Mississippi", "Southeast", 2), ("MN", "Minnesota", "Midwest", 2),
        ("WI", "Wisconsin", "Midwest", 2), ("MA", "Massachusetts", "Northeast", 2), ("CO", "Colorado", "West", 2), ("CT", "Connecticut", "Northeast", 1),
        ("KS", "Kansas", "Midwest", 1), ("OK", "Oklahoma", "Southwest", 1), ("AR", "Arkansas", "Southeast", 1), ("OR", "Oregon", "West", 1),
        ("UT", "Utah", "West", 1), ("NV", "Nevada", "West", 1), ("IA", "Iowa", "Midwest", 1), ("NE", "Nebraska", "Midwest", 1),
        ("NM", "New Mexico", "Southwest", 1), ("DE", "Delaware", "Northeast", 0.5), ("NH", "New Hampshire", "Northeast", 0.3), ("VT", "Vermont", "Northeast", 0.2),
        ("MT", "Montana", "West", 0.3), ("ID", "Idaho", "West", 0.3), ("WY", "Wyoming", "West", 0.2), ("SD", "South Dakota", "Midwest", 0.3),
        ("ND", "North Dakota", "Midwest", 0.2), ("WV", "West Virginia", "Southeast", 0.5),
    };
    private static readonly double StateWeightTotal = StateTable.Sum(s => s.Weight);

    /// <summary>A program's home state, read from its name ("Northern Bluegrass" is in Kentucky, "Sierra State" in California).</summary>
    private static readonly Dictionary<string, string> PlaceToState = new()
    {
        ["Ohio"] = "OH", ["Kentucky"] = "KY", ["Carolina"] = "NC", ["Texas"] = "TX", ["Michigan"] = "MI", ["Indiana"] = "IN", ["Kansas"] = "KS",
        ["Virginia"] = "VA", ["Georgia"] = "GA", ["Tennessee"] = "TN", ["Oregon"] = "OR", ["Arizona"] = "AZ", ["Florida"] = "FL", ["Iowa"] = "IA",
        ["Wisconsin"] = "WI", ["Illinois"] = "IL", ["Missouri"] = "MO", ["Alabama"] = "AL", ["Louisiana"] = "LA", ["Oklahoma"] = "OK",
        ["Nebraska"] = "NE", ["Colorado"] = "CO", ["Utah"] = "UT", ["Nevada"] = "NV", ["Washington"] = "WA", ["Maryland"] = "MD",
        ["Connecticut"] = "CT", ["Massachusetts"] = "MA", ["Pennsylvania"] = "PA", ["Minnesota"] = "MN", ["Arkansas"] = "AR", ["Mississippi"] = "MS",
        ["Montana"] = "MT", ["Idaho"] = "ID", ["Wyoming"] = "WY", ["Dakota"] = "SD", ["Vermont"] = "VT", ["Delaware"] = "DE", ["Jersey"] = "NJ",
        ["Carolina Coastal"] = "SC", ["Gulf"] = "AL", ["Lakeshore"] = "MI", ["Bluegrass"] = "KY", ["Ozark"] = "AR", ["Sierra"] = "CA", ["Cascade"] = "WA",
        ["Prairie"] = "KS", ["Piedmont"] = "NC", ["Tidewater"] = "VA", ["Big Sky"] = "MT", ["Red River"] = "TX", ["Rio Grande"] = "NM",
        ["Smoky Mountain"] = "TN", ["Great Plains"] = "NE", ["Bayou"] = "LA", ["Palmetto"] = "SC", ["Keystone"] = "PA", ["Granite"] = "NH",
        ["Golden Valley"] = "CA", ["High Desert"] = "NV",
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> HomeCache = new();
    private static readonly string[] PlacesLongestFirst = PlaceToState.Keys.OrderByDescending(k => k.Length).ToArray();

    /// <summary>Cached by city name (a name pack can rename a program, so the name is the key, not the team).</summary>
    public static string HomeState(Team program) => HomeCache.GetOrAdd(program.City, FindHomeState);

    private static string FindHomeState(string city)
    {
        foreach (var place in PlacesLongestFirst)
            if (city.Contains(place, StringComparison.Ordinal)) return PlaceToState[place];
        return "TX";
    }

    public static string StateName(string code) => StateTable.FirstOrDefault(s => s.Code == code).Name ?? code;
    private static readonly Dictionary<string, string> RegionByCode = StateTable.ToDictionary(s => s.Code, s => s.Region);
    public static string RegionOf(string code) => RegionByCode.GetValueOrDefault(code, "");

    private static string PickState(Rng rng)
    {
        double u = rng.NextDouble() * StateWeightTotal;
        foreach (var s in StateTable) { if ((u -= s.Weight) <= 0) return s.Code; }
        return "TX";
    }

    private static readonly string[] Towns =
    {
        "Oak Hill", "Cedar Grove", "Riverside", "Lincoln", "Westwood", "Brookfield", "Fairview", "Pinecrest", "Northgate", "Lakeview", "Hillcrest",
        "Springdale", "Maplewood", "Kingston", "Franklin", "Madison", "Jefferson", "Roosevelt", "Sterling", "Ashford", "Briarwood", "Glenwood",
        "Highland", "Summit", "Clearwater", "Ridgeway", "Stonebridge", "Woodland", "Crestview", "Harbor View", "Eastside", "Southridge", "Millbrook",
        "Willow Creek", "Red Oak", "Bayside", "Parkview", "Fox Run", "Garfield", "Monroe", "Hamilton", "Union", "Liberty", "Heritage", "Grandview",
    };
    private static readonly string[] Saints = { "Anthony", "Vincent", "Patrick", "Benedict", "Augustine", "Ignatius", "Francis", "Joseph", "Thomas More", "John Bosco" };

    private static string HighSchoolName(Rng rng) => rng.Next(10) switch
    {
        0 => $"St. {rng.Pick(Saints)}",
        1 or 2 => $"{rng.Pick(Towns)} Academy",
        3 => $"{rng.Pick(Towns)} Prep",
        4 => $"{rng.Pick(Towns)} Christian",
        _ => $"{rng.Pick(Towns)} High School",
    };

    // ------------------------------------------------------------------------------------------
    // Classes

    /// <summary>
    /// The players of one age group as the services see them. The top of each class keeps the old calibration (#121:
    /// the same handful of NABL-ready players a year); the rest of the thousand tracked are depth below college level.
    /// </summary>
    private List<Player> NewClass(PlayerGenerator gen, Rng rng, int age, DateOnly on, int classOf, int size)
    {
        int core = Math.Min(size, (int)Math.Round(Programs.Count * RosterSize / (double)(GraduateAge - YoungestAge)));
        var list = new List<Player>(size);
        for (int i = 0; i < size; i++)
        {
            var p = NewRecruit(gen, rng, age, on, i < core ? null : rng.Next(35, 60));
            Recruits[p.Id] = new Recruit { HighSchool = HighSchoolName(rng), State = PickState(rng), ClassOf = classOf, Bias = (int)Math.Round(rng.NextGaussian(0, 4)) };
            list.Add(p);
        }
        return list;
    }

    /// <summary>The services re-rank a class: projections sharpen as players get older (wide at 14, close at 17).</summary>
    public void Rerank(int classOf, DateOnly on, Rng rng)
    {
        var cls = ClassOf(classOf).ToList();
        foreach (var p in cls)
        {
            var r = Recruits[p.Id];
            int age = Math.Clamp(p.Age(on), YoungestAge, 17);
            double sd = 9 - 1.5 * (age - YoungestAge);
            r.Projection = (int)Math.Round(Destined.GetValueOrDefault(p.Id, p.CurrentAbility) + r.Bias + rng.NextGaussian(0, sd));
        }
        int rank = 1;
        foreach (var p in cls.OrderByDescending(p => Recruits[p.Id].Projection).ThenBy(p => p.Id))
        {
            var r = Recruits[p.Id];
            r.Rank = rank;
            r.Stars = rank <= FiveStars ? 5 : rank <= FiveStars + FourStars ? 4 : rank <= FiveStars + FourStars + ThreeStars ? 3 : 2;
            rank++;
        }
    }

    // ------------------------------------------------------------------------------------------
    // Scholarships and commitments

    public int Commits(Team t, int classOf) => Recruits.Values.Count(r => r.ClassOf == classOf && r.CommittedTo == t.Id);

    /// <summary>
    /// Where a class stands with every program: scholarships still open and players at each position for that season
    /// (returning players plus commitments). Built once per recruiting step and kept up to date as prospects commit.
    /// </summary>
    public sealed class ClassBoard
    {
        public Dictionary<int, int> Open { get; } = new();
        public Dictionary<(int, Position), int> AtPosition { get; } = new();
    }

    public ClassBoard BoardFor(int classOf)
    {
        var b = new ClassBoard();
        var summer = new DateOnly(classOf, 7, 1);
        foreach (var t in Programs)
        {
            int returning = 0;
            foreach (var x in t.Roster)
            {
                if (x.Age(summer) >= GraduateAge || WalkOnIds.Contains(x.Id)) continue;
                returning++;
                b.AtPosition[(t.Id, x.PrimaryPosition)] = b.AtPosition.GetValueOrDefault((t.Id, x.PrimaryPosition)) + 1;
            }
            b.Open[t.Id] = Scholarships - returning;
        }
        foreach (var p in ClassOf(classOf))
            if (Recruits[p.Id].CommittedTo is { } to)
            {
                b.Open[to]--;
                b.AtPosition[(to, p.PrimaryPosition)] = b.AtPosition.GetValueOrDefault((to, p.PrimaryPosition)) + 1;
            }
        return b;
    }

    /// <summary>Scholarships a program can still offer to a class: 13, less the scholarship players still there next season, less commitments.</summary>
    public int OpenScholarships(Team t, int classOf) => Math.Max(0, BoardFor(classOf).Open[t.Id]);

    /// <summary>
    /// How much a program appeals to a prospect: prestige, distance from home, the minutes waiting at his position and how
    /// the program is winning, with the one he cares about most weighing extra (C3, #174: prestige +50% above average, home +50%, minutes and winning double); plus the coached program's
    /// courting (<see cref="Recruit.Interest"/>). Noise stands for everything else (a cousin on the team, a coach he liked).
    /// </summary>
    public double Appeal(Team t, Player p, Recruit r, ClassBoard board, Rng? rng = null)
    {
        var (prestige, near, minutes, winning) = AppealParts(t, p, r, board);
        double a = prestige + near + minutes + winning;
        a += PriorityOf(p) switch
        {
            RecruitPriority.Prestige => (prestige - 50) * 0.5,
            RecruitPriority.Home => near * 0.5,
            RecruitPriority.PlayingTime => minutes,
            _ => winning,
        };
        if (t.Id == ManagedProgramId) a += CourtingEdge(r, t.Id, InterestIn(r, t));
        return a + (rng?.NextGaussian(0, 14) ?? 0);
    }

    /// <summary>
    /// What the coached program's courting is worth against the computer schools, which court everyone in their reach
    /// all the time: interest above the prospect's baseline (what the rivals put in) adds appeal, below it costs appeal,
    /// so a coached program that never calls is behind every school recruiting him. The best prospects are courted
    /// hardest and are swayed least: a 5-star's baseline is 60 and each point counts 0.2 (100 interest is +8, so a
    /// mid-major still can't outbid a blue blood for him); a 3-star's is 30 at 0.45 (100 interest is +31.5).
    /// </summary>
    public static double CourtingEdge(Recruit r, int programId, double interest) => (interest - Baseline(r)) * WeightOf(r);

    /// <summary>How hard the computer schools court him, in interest points.</summary>
    public static double Baseline(Recruit r) => r.Stars switch { >= 5 => 60, 4 => 45, 3 => 30, _ => 20 };

    /// <summary>How much a point of interest sways him (the best hear from everyone and go where the program is).</summary>
    public static double WeightOf(Recruit r) => r.Stars switch { >= 5 => 0.2, 4 => 0.3, _ => InterestWeight };

    /// <summary>Each point of interest is worth this much appeal: 100 interest against a 3-star's baseline of 30 is about the gap between a mid-major and a power program.</summary>
    public const double InterestWeight = 0.45;

    /// <summary>The parts of a program's appeal before his priority and your courting: prestige, home, minutes, winning.</summary>
    public (double Prestige, double Near, double Minutes, double Winning) AppealParts(Team t, Player p, Recruit r, ClassBoard board)
    {
        string home = HomeState(t);
        double near = home == r.State ? 14 : RegionOf(home) == RegionOf(r.State) ? 6 : 0;
        int crowd = board.AtPosition.GetValueOrDefault((t.Id, p.PrimaryPosition));
        double minutes = -3 * Math.Max(0, crowd - 2);
        double winning = Season.Elo.TryGetValue(t.Id, out var elo) ? Math.Clamp((elo - 1500) / 12, -10, 10) : 0;
        return (Prestige[t.Id], near, minutes, winning);
    }

    /// <summary>What a prospect weighs most (fixed for life, from his id so older saves get one too).</summary>
    public static RecruitPriority PriorityOf(Player p) => (RecruitPriority)((uint)(p.Id * 2654435761u) >> 8 & 0x3);

    public static string PriorityText(RecruitPriority k) => k switch
    {
        RecruitPriority.Prestige => "a big-name program",
        RecruitPriority.Home => "staying close to home",
        RecruitPriority.PlayingTime => "early playing time",
        _ => "a winning team",
    };

    /// <summary>The coached program can only land a prospect it has offered (C3): no scholarship, no commitment.</summary>
    private bool CanTake(Team t, Recruit r) => t.Id != ManagedProgramId || (r.Offered && r.CourtedBy == t.Id);

    /// <summary>
    /// C4 step 0 (#184): a computer school only recruits prospects inside its reach: those ranked within 3 × its prestige
    /// rank + 20 (the best program chases the top 23, the 50th the top 170), twice that in March and April, and anyone on
    /// signing day. Without it the blue bloods' few scholarships filled with early-committing 3-stars before the 5-stars
    /// chose, and after four seasons the top programs were no stronger than the bubble (review #7). The coached program
    /// has no limit: who you chase is your call.
    /// </summary>
    public Dictionary<int, int> PrestigeRanks() =>
        Programs.OrderByDescending(t => Prestige[t.Id]).ThenBy(t => t.Id).Select((t, i) => (t.Id, i + 1)).ToDictionary(x => x.Id, x => x.Item2);

    /// <summary>The worst national rank a program of this prestige rank recruits (scale 2 in spring).</summary>
    public static int Reach(int prestigeRank, double scale = 1) => (int)Math.Round(scale * (3 * prestigeRank + 20));

    /// <summary>How far the computer schools reach today: double in March and April, when the leftovers are picked over.</summary>
    public double ReachScale => RecruitingDay is { Month: >= 3 and <= 4 } ? 2 : 1;

    private bool InReach(Team t, Recruit r, Dictionary<int, int>? ranks, double scale) =>
        ranks == null || t.Id == ManagedProgramId || r.Rank <= Reach(ranks.GetValueOrDefault(t.Id, Programs.Count), scale);

    /// <summary>Whether a program would recruit him today: a scholarship left, within its reach, and for the coached program an offer.</summary>
    public bool CouldTake(Team t, Recruit r, ClassBoard board, Dictionary<int, int>? ranks = null) =>
        board.Open[t.Id] > 0 && CanTake(t, r) && InReach(t, r, ranks ?? PrestigeRanks(), ReachScale);

    private Team? Choose(Player p, Recruit r, ClassBoard board, Rng rng, Dictionary<int, int>? ranks = null, double scale = 1)
    {
        Team? best = null; double top = double.MinValue;
        foreach (var t in Programs)
        {
            if (board.Open[t.Id] <= 0 || !CanTake(t, r) || !InReach(t, r, ranks, scale)) continue;
            double a = Appeal(t, p, r, board, rng);
            if (a > top) { top = a; best = t; }
        }
        return best;
    }

    private void Commit(Player p, Recruit r, Team t, DateOnly on, ClassBoard board)
    {
        r.CommittedTo = t.Id; r.CommittedOn = on;
        board.Open[t.Id]--;
        board.AtPosition[(t.Id, p.PrimaryPosition)] = board.AtPosition.GetValueOrDefault((t.Id, p.PrimaryPosition)) + 1;
        TellCoach(p, r, t);
        if (r.Rank <= 10)
            News.Add(($"{StarText(r.Stars)} {p.Name} commits to {t.FullName}",
                      $"The No. {r.Rank} player in the Class of {r.ClassOf} ({p.PrimaryPosition}, {r.HighSchool}, {StateName(r.State)}) has chosen {t.FullName}."));
    }

    /// <summary>The weekly recruiting step for the senior class: commitments through the season, then signing day.</summary>
    public void RecruitingThrough(DateOnly date, Rng rng)
    {
        if (!Recruiting) return;
        // "Play everything" passes a far-off date: recruiting only runs to the end of this college year.
        var end = new DateOnly(Season.Year + 1, 6, 30);
        if (date > end) date = end;
        var day = (RecruitingDay ?? date.AddDays(-1)).AddDays(1);
        for (; day <= date; day = day.AddDays(1))
        {
            int cls = day.Month >= 7 ? day.Year + 1 : day.Year; // the seniors this school year
            if (day == SigningDay(cls)) SignAll(cls, day, rng);
            if (day.DayOfWeek != DayOfWeek.Monday || day < CommitmentsOpen(cls) || day >= SigningDay(cls)) continue;
            // The chance a prospect commits this week: the best decide earlier, many sign in the November early period,
            // everyone hurries in spring. Prospects ranked below the scholarships still open wait to see who's left.
            bool spring = day.Month is >= 3 and <= 4, early = day.Month == 11 && day.Day >= 8 && day.Day <= 21;
            var board = BoardFor(cls);
            var ranks = PrestigeRanks();
            int open = board.Open.Values.Sum(v => Math.Max(0, v)), waiting = 0;
            foreach (var p in ClassOf(cls).OrderBy(p => Recruits[p.Id].Rank).ToList())
            {
                var r = Recruits[p.Id];
                if (r.CommittedTo != null) continue;
                if (++waiting > open * 1.2) break;
                double chance = spring ? 0.22 : (0.01 + 0.008 * r.Stars + (r.Stars >= 4 ? 0.02 : 0)) * (early ? 2.5 : 1); // C3: the best decide earlier still, so few top-50 recruits are left for spring
                if (!rng.Chance(chance)) continue;
                if (Choose(p, r, board, rng, ranks, spring ? 2 : 1) is { } pick) Commit(p, r, pick, day, board);
            }
        }
        if (RecruitingDay == null || date > RecruitingDay) RecruitingDay = date;
    }

    /// <summary>Signing day: every uncommitted senior signs with the program that wants him most, while scholarships last.</summary>
    private void SignAll(int cls, DateOnly day, Rng rng)
    {
        var board = BoardFor(cls);
        foreach (var p in ClassOf(cls).OrderBy(p => Recruits[p.Id].Rank).ToList())
        {
            var r = Recruits[p.Id];
            if (r.CommittedTo != null) continue;
            var pick = Choose(p, r, board, rng);
            if (pick == null) break;
            r.CommittedTo = pick.Id; r.CommittedOn = day;
            board.Open[pick.Id]--;
            board.AtPosition[(pick.Id, p.PrimaryPosition)] = board.AtPosition.GetValueOrDefault((pick.Id, p.PrimaryPosition)) + 1;
            TellCoach(p, r, pick, signingDay: true);
        }
        var top = ClassRanking(cls).Take(5).ToList();
        if (top.Count > 0)
            News.Add(($"Signing day: the top recruiting classes of {cls}",
                      string.Join(" · ", top.Select((x, i) => $"{i + 1}. {x.Program.FullName} ({x.Commits} signed, {x.FiveStars}× five-star)")) + "."));
    }

    /// <summary>Recruiting class rankings: five-stars count most (100, 40, 15, 5 points by stars).</summary>
    public List<(Team Program, int Points, int Commits, int FiveStars, int FourStars)> ClassRanking(int classOf)
    {
        var by = Recruits.Values.Where(r => r.ClassOf == classOf && r.CommittedTo != null).GroupBy(r => r.CommittedTo!.Value);
        return by.Select(g => (Program(g.Key)!, g.Sum(r => r.Stars switch { 5 => 100, 4 => 40, 3 => 15, _ => 5 }), g.Count(),
                               g.Count(r => r.Stars == 5), g.Count(r => r.Stars == 4)))
                 .OrderByDescending(x => x.Item2).ThenBy(x => x.Item1.Id).ToList();
    }

    // ------------------------------------------------------------------------------------------
    // New careers and the summer

    private void GenerateRecruiting(PlayerGenerator gen, Rng rng, DateOnly today)
    {
        Recruiting = true;
        int season = today.Month >= 7 ? today.Year : today.Year - 1;
        // Ages are as of the summer the school year began, the date classes enroll and graduate on, so each age group is
        // exactly one class (a 22-year-old then graduates next summer at 23; a 17-year-old enrolls next summer at 18).
        var summer = new DateOnly(season, 7, 1);
        // College rosters, ages 22 down to 18: three per program per age; each program's weakest 22- and 18-year-olds walk on.
        int perAge = Squad / (GraduateAge - EnrollAge);
        for (int age = GraduateAge - 1; age >= EnrollAge; age--)
        {
            int core = (int)Math.Round(Programs.Count * RosterSize / (double)(GraduateAge - YoungestAge));
            var cohort = Enumerable.Range(0, core).Select(_ => NewRecruit(gen, rng, age, summer, null)).ToList();
            var taken = cohort.OrderByDescending(p => Destined[p.Id] + rng.NextGaussian(0, 5)).Take(Programs.Count * perAge).ToList();
            foreach (var p in cohort.Except(taken)) Destined.Remove(p.Id);
            foreach (var p in taken.OrderByDescending(p => Destined[p.Id]))
            {
                var pick = Programs.Where(t => t.Roster.Count(x => x.Age(summer) == age) < perAge)
                                   .OrderByDescending(t => Prestige[t.Id] + rng.NextGaussian(0, 18)).First();
                pick.Roster.Add(p); p.TeamId = pick.Id;
            }
            if (age is GraduateAge - 1 or EnrollAge)
                foreach (var t in Programs)
                    WalkOnIds.Add(t.Roster.Where(x => x.Age(summer) == age).OrderBy(x => x.CurrentAbility).First().Id);
        }
        // High school: the classes enrolling in each of the next four summers.
        for (int age = 17; age >= YoungestAge; age--)
        {
            int classOf = season + 1 + (17 - age);
            Prospects.AddRange(NewClass(gen, rng, age, summer, classOf, ClassTracked));
            Rerank(classOf, summer, rng);
        }
        // The seniors have been committing since September.
        Season = new CollegeSeason { Year = season }; // StartSeason fills it in; recruiting needs the year now
        RecruitingDay = CommitmentsOpen(season + 1).AddDays(-1);
        RecruitingThrough(today, rng);
        News.Clear();
    }

    /// <summary>The summer in the recruiting model: the senior class enrolls, programs fill to 13 scholarships and 2
    /// walk-ons from who's left, the unsigned leave the game, and a new class of 14-year-olds appears.</summary>
    private void SummerRecruiting(DateOnly on, PlayerGenerator gen, Rng rng)
    {
        int cls = on.Year;
        RecruitingThrough(on.AddDays(-1), rng); // signing day is in April; make sure it has happened
        var seniors = ClassOf(cls).ToList();
        foreach (var p in seniors.Where(p => Recruits[p.Id].CommittedTo != null))
        {
            var t = Program(Recruits[p.Id].CommittedTo!.Value)!;
            t.Roster.Add(p); p.TeamId = t.Id;
        }
        var left = seniors.Where(p => Recruits[p.Id].CommittedTo == null).OrderBy(p => Recruits[p.Id].Rank).ToList();
        // Late scholarships first (a program short of 13), then walk-ons, the biggest names choosing first.
        foreach (var walkOn in new[] { false, true })
            foreach (var t in Programs.OrderByDescending(t => Prestige[t.Id]))
            {
                int room = walkOn ? WalkOns - t.Roster.Count(p => WalkOnIds.Contains(p.Id))
                                  : Scholarships - t.Roster.Count(p => !WalkOnIds.Contains(p.Id));
                for (int i = 0; i < room && left.Count > 0; i++)
                {
                    var p = left[0]; left.RemoveAt(0);
                    t.Roster.Add(p); p.TeamId = t.Id;
                    if (walkOn) WalkOnIds.Add(p.Id); else Recruits[p.Id].CommittedTo = t.Id;
                }
            }
        foreach (var p in left) Destined.Remove(p.Id); // unsigned: they leave the game world (Danny, #162)
        foreach (var p in seniors) { Prospects.Remove(p); Recruits.Remove(p.Id); }
        // A new class of 14-year-olds; every class is re-ranked as the services learn more.
        Prospects.AddRange(NewClass(gen, rng, YoungestAge, on, cls + 4, ClassTracked));
        for (int c = cls + 1; c <= cls + 4; c++) Rerank(c, on, rng);
    }
}
