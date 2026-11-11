using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;

namespace Hardwood.Core.College;

/// <summary>A college conference: five power conferences of 14, three mid-majors of 16 (decision #82).</summary>
public sealed record ConferenceInfo(string Key, string Name, string Short, bool Power, int Size);

/// <summary>One college game. Knockout games (invitationals, conference tournaments, March Madness) carry an event, a round and a slot.</summary>
public sealed class CollegeGame
{
    public DateOnly Date { get; set; }
    public int HomeId { get; set; }
    public int AwayId { get; set; }
    public int? HomePts { get; set; }
    public int? AwayPts { get; set; }
    /// <summary>Non-conference · Invitational · Showcase · Conference · Conference tournament · March Madness</summary>
    public string Stage { get; set; } = "";
    /// <summary>The tournament or showcase name (knockouts are grouped by it).</summary>
    public string? Event { get; set; }
    public int Round { get; set; }
    public int Slot { get; set; }
    public bool Played => HomePts != null;
    public int? WinnerId => !Played ? null : HomePts > AwayPts ? HomeId : AwayId;
}

/// <summary>A college season (decision #96) and everything it decides. Saved as JSON with the world.</summary>
public sealed class CollegeSeason
{
    public int Year { get; set; }
    public List<CollegeGame> Games { get; set; } = new();
    /// <summary>Dates of each knockout round by event (round 0 = first round; March Madness round 0 = the First Four).</summary>
    public Dictionary<string, List<DateOnly>> RoundDates { get; set; } = new();
    /// <summary>Elo ratings (rankings and seeding).</summary>
    public Dictionary<int, double> Elo { get; set; } = new();
    public Dictionary<int, StatLine> Stats { get; set; } = new();
    /// <summary>March Madness: the 64 bracket slots (0 = a First Four winner still to come) and each team's seed.</summary>
    public List<int> Bracket { get; set; } = new();
    public Dictionary<int, int> Seeds { get; set; } = new();
    public Dictionary<string, int> ConferenceChampions { get; set; } = new();
    public Dictionary<string, int> EventWinners { get; set; } = new();
    public int? ChampionId { get; set; }
    public int? RunnerUpId { get; set; }
    public bool Selected { get; set; }
    /// <summary>Pro clubs courting this season's seniors (roadmap 11, college depth). Older saves start with an empty board.</summary>
    public RecruitingBoard Recruiting { get; set; } = new();
}

public sealed record CollegeChampion(int Year, int ChampionId, string Champion, int RunnerUpId, string RunnerUp);

/// <summary>
/// College basketball (decisions #76, #82-85, #95-98): about 120 computer-run programs whose rosters hold every
/// American player from 14 to 23. Programs recruit 14-year-olds by prestige; players grow toward the ability they'll
/// have at 23; at 23 they graduate into free agency and the whole pyramid can sign them. The season runs from
/// November invitationals and December showcases through conference play and tournaments to a 68-team March Madness,
/// whose games use the full match engine.
/// </summary>
public sealed class CollegeWorld
{
    public const int RosterSize = 30, FirstId = 90_001, YoungestAge = 14, GraduateAge = 23;
    public const string Madness = "March Madness";

    public static readonly ConferenceInfo[] Conferences =
    {
        new("aca", "Atlantic Coast Alliance", "ACA", true, 14), new("bmw", "Big Midwest", "BMW", true, 14),
        new("sac", "Southern Athletic Conference", "SAC", true, 14), new("pcu", "Pacific Union", "PCU", true, 14),
        new("htl", "Heartland Conference", "HTL", true, 14),
        new("coa", "Coastal Conference", "COA", false, 16), new("fro", "Frontier Conference", "FRO", false, 16),
        new("grc", "Great Rivers Conference", "GRC", false, 16),
        // Danny (Nov 10, decision #121): more big programs. Older careers keep their eight conferences (see Active).
        new("mpc", "Mountain Plains Conference", "MPC", true, 14),
        new("lvc", "Lakes & Valleys Conference", "LVC", false, 16),
    };

    /// <summary>The programs in the original eight conferences: the elite share of each recruiting class is scaled to keep
    /// the number of NABL-ready graduates as it was, so extra programs add depth for the pyramid rather than more stars.</summary>
    public const int BasePrograms = 118;

    /// <summary>Conferences this world actually has (a career started before #121 has eight).</summary>
    public IEnumerable<ConferenceInfo> Active => Conferences.Where(c => ConferenceOf.ContainsValue(c.Key));

    public List<Team> Programs { get; } = new();
    public Dictionary<int, string> ConferenceOf { get; } = new();
    public Dictionary<int, double> Prestige { get; } = new();
    /// <summary>The ability each player is heading for at 23 (his development path).</summary>
    public Dictionary<int, int> Destined { get; } = new();
    public CollegeSeason Season { get; set; } = new();
    public List<CollegeChampion> History { get; } = new();
    public List<(string Title, string Body)> News { get; } = new();

    public IEnumerable<Player> Players => Programs.SelectMany(t => t.Roster);
    public Team? Program(int id) => Programs.FirstOrDefault(t => t.Id == id);
    public IEnumerable<Team> InConference(string key) => Programs.Where(t => ConferenceOf[t.Id] == key);

    // ------------------------------------------------------------------------------------------
    // Players: ability at each age on the way to 23.

    private static readonly double[] Path = { 0.45, 0.52, 0.58, 0.64, 0.70, 0.76, 0.82, 0.88, 0.94, 1.0 }; // ages 14..23
    public static double Share(int age) => Path[Math.Clamp(age, YoungestAge, GraduateAge) - YoungestAge];

    /// <summary>
    /// How good a recruit will be at 23. Calibrated so each year's graduates look like the old draft classes at the top
    /// (a handful ready for the NABL, a few dozen for the tiers below) with most heading for the lower leagues or out.
    /// </summary>
    private static int DestinedAbility(Rng rng, double eliteScale = 1)
    {
        double u = rng.NextDouble(), elite = 0.012 * eliteScale;
        return u < elite ? rng.Next(110, 131) : u < 0.065 ? rng.Next(90, 110) : u < 0.35 ? rng.Next(75, 90) : rng.Next(50, 75);
    }

    private Player NewRecruit(PlayerGenerator gen, Rng rng, int age, DateOnly on)
    {
        int g = DestinedAbility(rng, Math.Min(1, BasePrograms / (double)Math.Max(1, Programs.Count)));
        int ca = Math.Max(20, (int)Math.Round(g * Share(age) + rng.NextGaussian(0, 2)));
        var p = gen.Generate((Position)rng.Next(0, 5), ca, age, on, Heritages.All.First(h => h.Key == "us"), "USA");
        p.PotentialAbility = Math.Max(ca, g + (int)Math.Round(Math.Max(0, rng.NextGaussian(g >= 100 ? 10 : 5, 6))));
        Destined[p.Id] = g;
        return p;
    }

    /// <summary>Recruits go to programs by prestige (decision #84): the best players to the biggest names, with some luck.</summary>
    private void Recruit(List<Player> recruits, Rng rng)
    {
        foreach (var p in recruits.OrderByDescending(p => Destined[p.Id]))
        {
            var open = Programs.Where(t => t.Roster.Count < RosterSize).ToList();
            if (open.Count == 0) break;
            var pick = open.OrderByDescending(t => Prestige[t.Id] + rng.NextGaussian(0, 18)).First();
            pick.Roster.Add(p);
            p.TeamId = pick.Id;
        }
    }

    // ------------------------------------------------------------------------------------------
    // Generation

    private static readonly string[] States =
    {
        "Ohio", "Kentucky", "Carolina", "Texas", "Michigan", "Indiana", "Kansas", "Virginia", "Georgia", "Tennessee", "Oregon", "Arizona",
        "Florida", "Iowa", "Wisconsin", "Illinois", "Missouri", "Alabama", "Louisiana", "Oklahoma", "Nebraska", "Colorado", "Utah", "Nevada",
        "Washington", "Maryland", "Connecticut", "Massachusetts", "Pennsylvania", "Minnesota", "Arkansas", "Mississippi", "Montana", "Idaho",
        "Wyoming", "Dakota", "Vermont", "Delaware", "Jersey", "Carolina Coastal", "Gulf", "Lakeshore", "Bluegrass", "Ozark", "Sierra", "Cascade",
        "Prairie", "Piedmont", "Tidewater", "Big Sky", "Red River", "Rio Grande", "Smoky Mountain", "Great Plains", "Bayou", "Palmetto",
        "Keystone", "Granite", "Golden Valley", "High Desert",
    };
    private static readonly string[] Forms = { "{0} State", "University of {0}", "{0} Tech", "{0} A&M", "Northern {0}", "Southern {0}", "{0} Christian", "Central {0}", "Western {0}", "Eastern {0}" };
    private static readonly string[] Mascots =
    {
        "Wildcats", "Bulldogs", "Tigers", "Eagles", "Cougars", "Bears", "Panthers", "Hornets", "Owls", "Rams", "Cardinals", "Huskies", "Aggies",
        "Lions", "Spartans", "Mustangs", "Hawkeyes", "Wolverines", "Seminoles", "Gophers", "Badgers", "Bobcats", "Mountaineers", "Rebels",
        "Volunteers", "Hoosiers", "Sooners", "Longhorns", "Razorbacks", "Gamecocks", "Terrapins", "Buckeyes", "Bruins", "Trojans", "Ducks",
        "Beavers", "Sun Devils", "Utes", "Buffaloes", "Cyclones", "Jayhawks", "Wolfpack", "Blue Devils", "Tar Heels", "Cavaliers", "Hokies",
    };

    /// <summary>Build every program with a full roster (new careers).</summary>
    public static CollegeWorld Generate(PlayerGenerator gen, Rng rng, DateOnly today)
    {
        var c = new CollegeWorld();
        var used = new HashSet<string>();
        int id = FirstId, n = 0;
        foreach (var conf in Conferences)
        {
            for (int i = 0; i < conf.Size; i++, n++)
            {
                string school;
                do school = string.Format(Forms[rng.Next(Forms.Length)], States[rng.Next(States.Length)]);
                while (!used.Add(school));
                string abbr = new string(school.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w != "of" && w != "University").Select(w => char.ToUpperInvariant(w[0])).ToArray());
                abbr = abbr.Length >= 2 ? abbr : school[..3].ToUpperInvariant();
                while (!used.Add("abbr:" + abbr)) abbr += (char)('A' + rng.Next(26));
                double hue = rng.NextDouble() * 360;
                var team = new Team
                {
                    Id = id++, City = school, Name = rng.Pick(Mascots), Abbreviation = abbr, Conference = Conference.East, Division = conf.Short,
                    ArenaCapacity = rng.Next(conf.Power ? 9000 : 4000, conf.Power ? 22000 : 12000),
                    PrimaryColor = Hsv(hue, 0.7, 0.6), SecondaryColor = rng.Chance(0.5) ? "#F4F4F4" : Hsv((hue + 180) % 360, 0.6, 0.85),
                    LogoStyle = rng.Next(0, 5), Tactic = new Tactic { Pace = Math.Round(rng.NextGaussian(90, 3), 1) },
                };
                c.Programs.Add(team);
                c.ConferenceOf[team.Id] = conf.Key;
                c.Prestige[team.Id] = Math.Clamp(rng.NextGaussian(conf.Power ? 68 : 42, 11), 15, 95);
            }
        }
        // Rosters: each age group recruited in turn, so the best of every year go to the biggest programs.
        // Ages 14-22 today: everyone who turns 23 before next summer graduates then, so every graduate is 23
        // (a 23-year-old cohort here would graduate at 24 and double the first class). The spare places go to the oldest.
        int groups = GraduateAge - YoungestAge, perAge = RosterSize / groups, spare = RosterSize - perAge * groups;
        for (int age = GraduateAge - 1; age >= YoungestAge; age--)
        {
            int each = perAge + (GraduateAge - 1 - age < spare ? 1 : 0);
            var cohort = Enumerable.Range(0, c.Programs.Count * each).Select(_ => c.NewRecruit(gen, rng, age, today)).ToList();
            c.Recruit(cohort, rng);
        }
        c.StartSeason(today.Month >= 7 ? today.Year : today.Year - 1, rng);
        return c;
    }

    private static string Hsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return $"#{(int)((r + m) * 255):X2}{(int)((g + m) * 255):X2}{(int)((b + m) * 255):X2}";
    }

    // ------------------------------------------------------------------------------------------
    // The summer: graduation, growth, recruiting, prestige.

    /// <summary>
    /// Players who are 23 graduate into free agency (decision #98); everyone else grows along his path; programs
    /// recruit 14-year-olds to refill their rosters; prestige follows results. Returns the graduates.
    /// </summary>
    public List<Player> Summer(DateOnly on, List<Player> freeAgents, PlayerGenerator gen, DevelopmentEngine dev, Rng rng)
    {
        var grads = new List<Player>();
        foreach (var t in Programs)
            foreach (var p in t.Roster.ToList())
            {
                int age = p.Age(on);
                if (age >= GraduateAge)
                {
                    t.Roster.Remove(p);
                    p.TeamId = null; p.Contract = null; p.RookieSeason = on.Year;
                    Destined.Remove(p.Id);
                    freeAgents.Add(p);
                    grads.Add(p);
                    continue;
                }
                int g = Destined.TryGetValue(p.Id, out var d) ? d : p.CurrentAbility;
                int target = (int)Math.Round(g * Share(age));
                int delta = target - p.CurrentAbility + (int)Math.Round(rng.NextGaussian(0, 2));
                if (delta > 0) dev.Nudge(p, delta, age);
            }
        // Prestige: winning and tournament runs raise it; it drifts back toward the conference's level.
        var s = Season;
        foreach (var t in Programs)
        {
            var conf = Conferences.First(x => x.Key == ConferenceOf[t.Id]);
            var (w, l) = Record(t.Id);
            double runs = s.Games.Count(g => g.Event == Madness && g.Round >= 1 && g.WinnerId == t.Id);
            double change = (w + l > 0 ? (w / (double)(w + l) - 0.5) * 10 : 0) + runs * 1.5 + (s.ChampionId == t.Id ? 4 : 0);
            Prestige[t.Id] = Math.Clamp(Prestige[t.Id] + change + ((conf.Power ? 68 : 42) - Prestige[t.Id]) * 0.08, 10, 99);
        }
        // Recruit the new class of 14-year-olds.
        int needed = Programs.Sum(t => RosterSize - t.Roster.Count);
        Recruit(Enumerable.Range(0, needed).Select(_ => NewRecruit(gen, rng, YoungestAge, on)).ToList(), rng);
        if (grads.Count > 0)
        {
            var top = grads.OrderByDescending(p => p.CurrentAbility).Take(5).ToList();
            News.Add(($"{grads.Count} college graduates turn pro", "The best of the class: " + string.Join(", ", top.Select(p => $"{p.Name} ({p.PrimaryPosition}, {p.CurrentAbility})")) + ". They're free agents now."));
        }
        return grads;
    }

    public (int wins, int losses) Record(int id)
    {
        int w = 0, l = 0;
        foreach (var g in Season.Games)
        {
            if (!g.Played || (g.HomeId != id && g.AwayId != id)) continue;
            if (g.WinnerId == id) w++; else l++;
        }
        return (w, l);
    }

    // ------------------------------------------------------------------------------------------
    // The season (decision #96).

    private static readonly string[] Invitationals = { "Island Classic", "Empire Invitational", "Bayou Shootout", "Desert Showdown", "Pacific Rim Classic", "Lakeshore Invitational", "Smoky Mountain Classic", "Sunshine Showcase" };
    private static readonly string[] Showcases = { "Liberty Bowl Showcase", "Gateway Classic", "Peach Showcase", "Lone Star Classic", "Rose Garden Showcase", "Motor City Classic", "Crescent City Showcase", "Bluegrass Classic", "Golden Gate Showcase", "Windy City Classic", "Mile High Showcase", "Music City Classic" };

    public void StartSeason(int year, Rng rng)
    {
        var s = new CollegeSeason { Year = year };
        foreach (var t in Programs)
            s.Elo[t.Id] = 1500 + (QuickSim.Strength(t) - 85) * 8 + (Prestige[t.Id] - 50) * 2;
        var ids = Programs.Select(t => t.Id).ToList();
        // Non-conference: ten rounds, Tuesdays and Fridays from early November, against teams from other conferences.
        var day = new DateOnly(year, 11, 6);
        for (int round = 0; round < 10; round++)
        {
            while (day.DayOfWeek is not (DayOfWeek.Tuesday or DayOfWeek.Friday)) day = day.AddDays(1);
            if (day.Month == 11 && day.Day >= 24 && day.Day <= 28) { day = day.AddDays(1); round--; continue; } // invitational week
            var pool = ids.OrderBy(_ => rng.NextDouble()).ToList();
            while (pool.Count >= 2)
            {
                int a = pool[0]; pool.RemoveAt(0);
                int j = pool.FindIndex(b => ConferenceOf[b] != ConferenceOf[a]);
                if (j < 0) j = 0;
                int b = pool[j]; pool.RemoveAt(j);
                s.Games.Add(new CollegeGame { Date = day, HomeId = rng.Chance(0.5) ? a : b, AwayId = 0, Stage = "Non-conference" });
                s.Games[^1].AwayId = s.Games[^1].HomeId == a ? b : a;
            }
            day = day.AddDays(1);
        }
        // Invitationals (Thanksgiving week): eight brackets of eight, invited by prestige with some luck.
        var invited = ids.OrderByDescending(i => Prestige[i] + rng.NextGaussian(0, 15)).Take(64).OrderBy(_ => rng.NextDouble()).ToList();
        for (int e = 0; e < Invitationals.Length; e++)
        {
            var field = invited.Skip(e * 8).Take(8).OrderByDescending(i => s.Elo[i]).ToList();
            var dates = new List<DateOnly> { new(year, 11, 25), new(year, 11, 26), new(year, 11, 27) };
            AddKnockout(s, Invitationals[e], "Invitational", Seeded(field), dates);
        }
        // Conference play: a single round robin, Tuesdays and Saturdays from early January.
        foreach (var conf in Active)
        {
            var members = InConference(conf.Key).Select(t => t.Id).ToList();
            var rounds = RoundRobin(members);
            var d = new DateOnly(year + 1, 1, 3);
            foreach (var r in rounds)
            {
                while (d.DayOfWeek is not (DayOfWeek.Tuesday or DayOfWeek.Saturday)) d = d.AddDays(1);
                foreach (var (h, a) in r) s.Games.Add(new CollegeGame { Date = d, HomeId = h, AwayId = a, Stage = "Conference" });
                d = d.AddDays(1);
            }
        }
        Season = s;
    }

    private static List<List<(int, int)>> RoundRobin(List<int> teams)
    {
        var list = teams.ToList();
        if (list.Count % 2 == 1) list.Add(-1);
        int n = list.Count;
        var rounds = new List<List<(int, int)>>();
        for (int r = 0; r < n - 1; r++)
        {
            var round = new List<(int, int)>();
            for (int i = 0; i < n / 2; i++)
            {
                int a = list[i], b = list[n - 1 - i];
                if (a >= 0 && b >= 0) round.Add((r + i) % 2 == 0 ? (a, b) : (b, a));
            }
            rounds.Add(round);
            list.Insert(1, list[^1]); list.RemoveAt(list.Count - 1);
        }
        return rounds;
    }

    /// <summary>Bracket order for a seeded field (1 v 8, 4 v 5, 3 v 6, 2 v 7 ...).</summary>
    private static List<int> Seeded(List<int> bySeed)
    {
        var order = new List<int> { 0 };
        while (order.Count < bySeed.Count) { int m = order.Count * 2; order = order.SelectMany(x => new[] { x, m - 1 - x }).ToList(); }
        return order.Select(i => bySeed[i]).ToList();
    }

    private static void AddKnockout(CollegeSeason s, string name, string stage, List<int> bracket, List<DateOnly> dates)
    {
        s.RoundDates[name] = dates;
        for (int i = 0; i < bracket.Count; i += 2)
            s.Games.Add(new CollegeGame { Date = dates[0], HomeId = bracket[i], AwayId = bracket[i + 1], Stage = stage, Event = name, Round = 0, Slot = i / 2 });
    }

    // ------------------------------------------------------------------------------------------
    // Playing

    /// <summary>Play every game up to and including <paramref name="date"/>, adding knockout rounds and stages as they come.</summary>
    public void PlayThrough(DateOnly date, GameSimulator sim, Rng rng)
    {
        var s = Season;
        while (true)
        {
            bool added = Advance(rng);
            var next = s.Games.Where(g => !g.Played).OrderBy(g => g.Date).FirstOrDefault();
            if (next == null || next.Date > date)
            {
                if (!added) return;
                continue;
            }
            foreach (var g in s.Games.Where(g => !g.Played && g.Date == next.Date).ToList()) Play(g, sim, rng);
        }
    }

    private void Play(CollegeGame g, GameSimulator sim, Rng rng)
    {
        var home = Program(g.HomeId)!; var away = Program(g.AwayId)!;
        if (g.Event == Madness)
        {
            // March Madness gets the full match engine (decision #95).
            var r = sim.Simulate(home, away, Rules.College, rng.NextULong(), false, g.Date);
            g.HomePts = r.Home.Points; g.AwayPts = r.Away.Points;
            foreach (var box in new[] { r.Home, r.Away })
            foreach (var l in box.Lines.Where(l => l.Seconds > 0))
            {
                var st = Season.Stats.TryGetValue(l.Player.Id, out var x) ? x : Season.Stats[l.Player.Id] = new StatLine();
                st.TeamId = box.Team.Id; st.G++; st.Min += l.Minutes; st.Pts += l.Points; st.Reb += l.Rebounds; st.Ast += l.Assists;
                st.Stl += l.Steals; st.Blk += l.Blocks; st.Fgm += l.FGM; st.Fga += l.FGA; st.Tpm += l.ThreePM; st.Tpa += l.ThreePA;
            }
        }
        else
        {
            var (hp, ap) = QuickSim.Play(home, away, g.Date, rng, Season.Stats, Rules.College, 72); // NCAA ~72 a side
            g.HomePts = hp; g.AwayPts = ap;
        }
        UpdateElo(g);
    }

    private void UpdateElo(CollegeGame g)
    {
        var elo = Season.Elo;
        double rh = elo.GetValueOrDefault(g.HomeId, 1500), ra = elo.GetValueOrDefault(g.AwayId, 1500);
        bool neutral = g.Event != null;
        double expected = 1 / (1 + Math.Pow(10, (ra - rh - (neutral ? 0 : 60)) / 400));
        double result = g.HomePts > g.AwayPts ? 1 : 0;
        double margin = Math.Log(Math.Abs(g.HomePts!.Value - g.AwayPts!.Value) + 1);
        double k = 24 * margin * 0.6;
        elo[g.HomeId] = rh + k * (result - expected);
        elo[g.AwayId] = ra - k * (result - expected);
    }

    /// <summary>Add whatever is now due: next knockout rounds, showcases, conference tournaments, selection, the champion. True if anything was added.</summary>
    private bool Advance(Rng rng)
    {
        var s = Season;
        bool changed = false;
        // Next rounds of every knockout whose current round is finished.
        foreach (var (name, dates) in s.RoundDates)
        {
            var games = s.Games.Where(g => g.Event == name).ToList();
            if (games.Count == 0) continue;
            int round = games.Max(g => g.Round);
            var current = games.Where(g => g.Round == round).OrderBy(g => g.Slot).ToList();
            if (current.Any(g => !g.Played)) continue;
            if (name == Madness && round == 0 && !games.Any(g => g.Round == 1))
            {
                // First Four winners take their slots in the bracket; the Round of 64 begins.
                foreach (var g in current) s.Bracket[g.Slot] = g.WinnerId!.Value;
                for (int i = 0; i < 64; i += 2)
                    s.Games.Add(new CollegeGame { Date = dates[1].AddDays(i < 32 ? 0 : 1), HomeId = s.Bracket[i], AwayId = s.Bracket[i + 1], Stage = Madness, Event = Madness, Round = 1, Slot = i / 2 });
                changed = true;
                continue;
            }
            if (current.Count == 1)
            {
                if (!s.EventWinners.ContainsKey(name)) { s.EventWinners[name] = current[0].WinnerId!.Value; changed = true; Crowned(name, current[0]); }
                continue;
            }
            int next = round + 1;
            int dateIndex = name == Madness ? next : next;
            var date = dateIndex < dates.Count ? dates[dateIndex] : current.Max(g => g.Date).AddDays(1);
            for (int i = 0; i < current.Count; i += 2)
                s.Games.Add(new CollegeGame { Date = date, HomeId = current[i].WinnerId!.Value, AwayId = current[i + 1].WinnerId!.Value, Stage = current[i].Stage, Event = name, Round = next, Slot = i / 2 });
            changed = true;
        }
        if (changed) return true;

        // December showcases once the non-conference season and invitationals are done.
        if (!s.Games.Any(g => g.Stage == "Showcase") && s.Games.Where(g => g.Stage is "Non-conference" or "Invitational").All(g => g.Played))
        {
            var top = Programs.Select(t => t.Id).OrderByDescending(i => s.Elo[i]).Take(30).ToList();
            var day = new DateOnly(s.Year, 12, 19);
            int n = 0;
            while (top.Count >= 2 && n < Showcases.Length)
            {
                int a = top[0]; top.RemoveAt(0);
                int j = top.FindIndex(b => ConferenceOf[b] != ConferenceOf[a]);
                if (j < 0) j = 0;
                int b = top[j]; top.RemoveAt(j);
                s.Games.Add(new CollegeGame { Date = day.AddDays(n % 3), HomeId = a, AwayId = b, Stage = "Showcase", Event = Showcases[n], Round = 0 });
                n++;
            }
            return true;
        }
        // Conference tournaments: the top eight of each conference, after the regular season.
        if (!s.Games.Any(g => g.Stage == "Conference tournament") && s.Games.Where(g => g.Stage == "Conference").All(g => g.Played) && s.Games.Any(g => g.Stage == "Conference"))
        {
            var dates = new List<DateOnly> { new(s.Year + 1, 3, 10), new(s.Year + 1, 3, 11), new(s.Year + 1, 3, 12) };
            foreach (var conf in Active)
            {
                var table = ConferenceTable(conf.Key).Take(8).Select(r => r.TeamId).ToList();
                AddKnockout(s, $"{conf.Short} tournament", "Conference tournament", Seeded(table), dates);
            }
            return true;
        }
        // Selection Sunday: 68 teams, the First Four, then the bracket.
        if (!s.Selected && s.Games.Any(g => g.Stage == "Conference tournament") && Active.All(c => s.EventWinners.ContainsKey($"{c.Short} tournament")))
        {
            foreach (var conf in Active)
                if (s.EventWinners.TryGetValue($"{conf.Short} tournament", out var w)) s.ConferenceChampions[conf.Key] = w;
            Select(rng);
            return true;
        }
        if (s.Selected && s.ChampionId == null && s.EventWinners.TryGetValue(Madness, out var champ))
        {
            var final = s.Games.Where(g => g.Event == Madness).OrderBy(g => g.Round).Last();
            s.ChampionId = champ; s.RunnerUpId = final.HomeId == champ ? final.AwayId : final.HomeId;
            var c = Program(champ)!; var r = Program(s.RunnerUpId.Value)!;
            History.Add(new CollegeChampion(s.Year, champ, c.FullName, r.Id, r.FullName));
            News.Add(($"{c.FullName} win March Madness", $"{c.FullName} beat {r.FullName} in the final to win the {s.Year + 1} college championship."));
            return true;
        }
        return false;
    }

    private void Crowned(string name, CollegeGame final)
    {
        if (name == Madness) return; // the champion is announced with the final
        var w = Program(final.WinnerId!.Value)!;
        if (name.EndsWith("tournament")) News.Add(($"{w.FullName} win the {name}", $"{w.FullName} take the {name} and the automatic bid to March Madness."));
    }

    /// <summary>Selection: eight automatic bids (conference tournament champions) and 60 at-large by rating; the bottom eight play the First Four.</summary>
    private void Select(Rng rng)
    {
        var s = Season;
        var auto = s.ConferenceChampions.Values.Distinct().ToList();
        var atLarge = Programs.Select(t => t.Id).Where(i => !auto.Contains(i)).OrderByDescending(i => s.Elo[i]).Take(68 - auto.Count);
        var field = auto.Concat(atLarge).OrderByDescending(i => s.Elo[i]).ToList();
        // Seed lines 1-15: four teams each, snaked across four regions; line 16: the First Four winners.
        var regions = new List<int>[4];
        for (int r = 0; r < 4; r++) regions[r] = new List<int>();
        for (int line = 0; line < 15; line++)
        {
            var four = field.Skip(line * 4).Take(4).ToList();
            if (line % 2 == 1) four.Reverse();
            for (int r = 0; r < 4; r++) { regions[r].Add(four[r]); s.Seeds[four[r]] = line + 1; }
        }
        var firstFour = field.Skip(60).ToList();
        foreach (var t in firstFour) s.Seeds[t] = 16;
        int[] order = { 1, 16, 8, 9, 5, 12, 4, 13, 6, 11, 3, 14, 7, 10, 2, 15 };
        s.Bracket = new List<int>();
        for (int r = 0; r < 4; r++)
            foreach (int seed in order) s.Bracket.Add(seed == 16 ? 0 : regions[r][seed - 1]);
        int y = s.Year + 1;
        var dates = new List<DateOnly> { new(y, 3, 17), new(y, 3, 19), new(y, 3, 21), new(y, 3, 26), new(y, 3, 28), new(y, 4, 4), new(y, 4, 6) };
        s.RoundDates[Madness] = dates;
        var slots = Enumerable.Range(0, 64).Where(i => s.Bracket[i] == 0).ToList();
        for (int i = 0; i < 4; i++)
            s.Games.Add(new CollegeGame { Date = dates[0], HomeId = firstFour[i * 2], AwayId = firstFour[i * 2 + 1], Stage = Madness, Event = Madness, Round = 0, Slot = slots[i] });
        s.Selected = true;
        var ones = field.Take(4).Select(i => Program(i)!.FullName);
        News.Add(("March Madness: the field is set", $"68 teams are in. The No. 1 seeds: {string.Join(", ", ones)}."));
    }

    /// <summary>A conference's regular-season table.</summary>
    public List<(int TeamId, int Wins, int Losses)> ConferenceTable(string key)
    {
        var members = InConference(key).Select(t => t.Id).ToHashSet();
        var rows = members.ToDictionary(i => i, _ => (w: 0, l: 0));
        foreach (var g in Season.Games.Where(g => g.Stage == "Conference" && g.Played && members.Contains(g.HomeId) && members.Contains(g.AwayId)))
        {
            var w = g.WinnerId!.Value; var l = w == g.HomeId ? g.AwayId : g.HomeId;
            rows[w] = (rows[w].w + 1, rows[w].l); rows[l] = (rows[l].w, rows[l].l + 1);
        }
        return rows.Select(kv => (kv.Key, kv.Value.w, kv.Value.l)).OrderByDescending(r => r.Item2 - r.Item3).ThenByDescending(r => Season.Elo[r.Key]).ToList();
    }

    /// <summary>The Top 25 by rating.</summary>
    public List<Team> Top25() => Programs.OrderByDescending(t => Season.Elo.GetValueOrDefault(t.Id, 1500)).Take(25).ToList();

    public string StageText
    {
        get
        {
            var s = Season;
            if (s.ChampionId != null) return "Season complete";
            var next = s.Games.Where(g => !g.Played).OrderBy(g => g.Date).FirstOrDefault();
            if (next == null) return "Between stages";
            return next.Event == Madness ? $"March Madness: {RoundName(next.Round)}" : next.Stage;
        }
    }

    public static string RoundName(int round) => round switch
    {
        0 => "First Four", 1 => "Round of 64", 2 => "Round of 32", 3 => "Sweet 16", 4 => "Elite Eight", 5 => "Final Four", _ => "Championship",
    };
}

/// <summary>One box in the March Madness bracket: two teams (or "to be decided"), seeds, the score once played.</summary>
public sealed record BracketBox(int Round, int Slot, int? TeamA, int SeedA, int? TeamB, int SeedB, int? ScoreA, int? ScoreB, DateOnly? Date)
{
    public bool Played => ScoreA != null;
    public int? Winner => !Played ? null : ScoreA > ScoreB ? TeamA : TeamB;
    /// <summary>A win by a seed at least four lines worse.</summary>
    public bool Upset => Played && (Winner == TeamA ? SeedA - SeedB : SeedB - SeedA) >= 4;
}

public static class MadnessBracket
{
    public static readonly string[] Regions = { "East", "West", "South", "Midwest" };

    /// <summary>Every box of rounds 1-6 (Round of 64 to the final), filled from the games so far; slots not yet reached are empty.</summary>
    public static List<BracketBox> Boxes(CollegeSeason s)
    {
        var boxes = new List<BracketBox>();
        if (!s.Selected || s.Bracket.Count < 64) return boxes;
        var games = s.Games.Where(g => g.Event == CollegeWorld.Madness).ToList();
        int Seed(int? id) => id is { } x ? s.Seeds.GetValueOrDefault(x) : 0;
        for (int round = 1; round <= 6; round++)
        {
            int n = 64 >> round;
            for (int slot = 0; slot < n; slot++)
            {
                var g = games.FirstOrDefault(x => x.Round == round && x.Slot == slot);
                if (g != null) { boxes.Add(new BracketBox(round, slot, g.HomeId, Seed(g.HomeId), g.AwayId, Seed(g.AwayId), g.HomePts, g.AwayPts, g.Date)); continue; }
                int? a = null, b = null;
                if (round == 1) { a = s.Bracket[slot * 2] is var x and > 0 ? x : null; b = s.Bracket[slot * 2 + 1] is var y and > 0 ? y : null; }
                else
                {
                    a = games.FirstOrDefault(x => x.Round == round - 1 && x.Slot == slot * 2)?.WinnerId;
                    b = games.FirstOrDefault(x => x.Round == round - 1 && x.Slot == slot * 2 + 1)?.WinnerId;
                }
                var date = s.RoundDates.TryGetValue(CollegeWorld.Madness, out var d) && round < d.Count ? d[round] : (DateOnly?)null;
                boxes.Add(new BracketBox(round, slot, a, Seed(a), b, Seed(b), null, null, date));
            }
        }
        return boxes;
    }

    /// <summary>Which region a box belongs to (rounds 1-4), or -1 for the Final Four and the final.</summary>
    public static int RegionOf(BracketBox b) => b.Round >= 5 ? -1 : b.Slot / (8 >> (b.Round - 1));
}
