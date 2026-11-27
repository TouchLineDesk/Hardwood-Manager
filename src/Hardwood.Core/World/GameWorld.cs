using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.World;

/// <summary>A competition outside the NABL, with its clubs (if it owns any), simulation depth, current season and history.</summary>
public sealed class WorldCompetition
{
    public required CompetitionProfile Profile { get; init; }
    /// <summary>Clubs this competition owns (domestic leagues and the guest pool). A continental competition owns none.</summary>
    public List<Team> Clubs { get; } = new();
    /// <summary>Run every game through the full match engine (decision #40); otherwise games get a quick result.</summary>
    public bool FullSim { get; set; }
    public CompetitionSeason Season { get; set; } = new();
    public List<ChampionRecord> History { get; } = new();

    public string Key => Profile.Key;
    public bool Plays => Profile.Kind != CompetitionKind.GuestPool;
    public override string ToString() => Profile.Name;
}

/// <summary>
/// Everything in the world beyond the NABL (decision #38–40): five competitions with their own clubs, players,
/// rules, money and calendar. The NABL itself is still run by <see cref="GameSession"/>; the world plays its games
/// on the same dates as the session advances, through the full engine or a quick result model.
/// </summary>
public sealed class GameWorld
{
    /// <summary>Overseas team ids start here (the NABL uses 1–30).</summary>
    public const int FirstTeamId = 1001;
    /// <summary>Roster size for every club outside the NABL.</summary>
    public const int RosterSize = 13;
    /// <summary>K13 (run 133): how far below a club's need an unsigned college graduate (with the potential to reach it) can be.</summary>
    public const int GraduateReach = 15;
    /// <summary>H29: the fewest players a club outside the NBA may carry (tiers 2-5 and abroad). One number everywhere: the
    /// sporting director fills your club to it, the assistant warns below it, minimum deals and the roster-fill reserve use it.</summary>
    public const int RosterMinimum = RosterSize - 2;
    /// <summary>The most players a club outside the NBA may carry.</summary>
    public const int RosterMaximum = RosterSize + 2;
    /// <summary>The fewest players your club abroad may start a season with (the sporting director fills to this): the league minimum (H29; was 10, one under it).</summary>
    public const int ManagedMinimum = RosterMinimum;

    private readonly Rng _rng;
    private readonly Dictionary<int, Team> _teams = new();

    public League Nabl { get; }
    /// <summary>College basketball (decisions #76, #82-85, #95-98); null in careers from before it existed.</summary>
    public College.CollegeWorld? College { get; set; }
    public List<WorldCompetition> Competitions { get; } = new();
    /// <summary>International plan I2 (run 209): the tournaments played (the World Cup). Saved as an optional world_meta row.</summary>
    public International.InternationalHistory International { get; set; } = new();
    /// <summary>Headlines (champions, Final Four) since the session last read them.</summary>
    public List<(string Title, string Body)> News { get; } = new();

    /// <summary>K16 (run 155): the titles in <see cref="News"/> that came from the college world, so a college coach's digest can keep only those. Not saved; cleared with <see cref="News"/>.</summary>
    public HashSet<string> CollegeTitles { get; } = new();

    public GameWorld(League nabl, ulong rngSeed)
    {
        Nabl = nabl;
        _rng = new Rng(rngSeed);
    }

    /// <summary>A seed to restore the world's random stream from (saved with the game).</summary>
    public ulong NextRngSeed() => _rng.NextULong();
    /// <summary>The seed a save writes; the stream restarts from it (see <see cref="Hardwood.Core.Season.GameSession.SaveRngSeed"/>).</summary>
    internal ulong SaveRngSeed() { var x = _rng.NextULong(); _savedSeed = x; return x; }
    private ulong? _savedSeed;
    /// <summary>Restart the stream from the seed the last save wrote, once that save has gone through.</summary>
    internal void RestartFromSave() { if (_savedSeed is { } x) _rng.Reseed(x); _savedSeed = null; }
    /// <summary>Back to the start of the world's stream (its seed as loaded or last saved).</summary>
    internal void RestartStream() => _rng.Reseed(_rng.Seed);

    public WorldCompetition this[string key] => Competitions.First(c => c.Key == key);
    public IEnumerable<WorldCompetition> Playing => Competitions.Where(c => c.Plays);
    public IEnumerable<Team> Clubs => Competitions.SelectMany(c => c.Clubs);
    public IEnumerable<Player> Players => Clubs.SelectMany(t => t.Roster);
    public Team? TeamById(int id) => _teams.GetValueOrDefault(id);
    /// <summary>A club in any fixture: the world's, or a top-league club playing a promotion playoff (run 102). Top-league
    /// clubs stay out of <see cref="TeamById"/>, which callers read as "a club abroad".</summary>
    public Team? FixtureTeam(int id) => _teams.GetValueOrDefault(id) ?? Nabl.Teams.FirstOrDefault(t => t.Id == id);
    /// <summary>The domestic league (or guest pool) a club belongs to.</summary>
    public WorldCompetition HomeOf(Team t) => Competitions.First(c => c.Clubs.Contains(t));

    /// <summary>The domestic league (not a continental competition or the guest pool) a club plays in, if any.</summary>
    public WorldCompetition? LeagueOf(Team t) => Competitions.FirstOrDefault(c => c.Profile.Kind == CompetitionKind.Domestic && c.Clubs.Contains(t));

    /// <summary>Every game a club has in any competition this season (league and continental), in date order.</summary>
    public IEnumerable<(WorldCompetition comp, Fixture fixture)> FixturesOf(int teamId) =>
        Playing.SelectMany(c => c.Season.Fixtures.Where(f => f.HomeId == teamId || f.AwayId == teamId).Select(f => (c, f)))
               .OrderBy(x => x.Item2.Date).ThenBy(x => x.Item2.Index);

    /// <summary>A club's unplayed game on a date (games are scheduled at the end of the previous day, playoffs included).</summary>
    public (WorldCompetition comp, Fixture fixture)? GameOn(int teamId, DateOnly date)
    {
        foreach (var x in FixturesOf(teamId)) if (!x.fixture.Played && x.fixture.Date == date) return x;
        return null;
    }

    /// <summary>
    /// Record a game played elsewhere (the human's game, through the full engine or live): score, stat lines and series.
    /// </summary>
    public void Record(WorldCompetition comp, Fixture f, GameResult r)
    {
        f.HomePts = r.Home.Points; f.AwayPts = r.Away.Points;
        // A promotion playoff's lines stay out of tier 2's stats (a top-league club's players aren't tier 2's).
        if (!UsaPyramid.IsPromotion(f.Stage)) AddLines(comp.Season.Stats, r);
        AfterResult(comp, f);
    }

    public void Index()
    {
        _teams.Clear();
        foreach (var t in Clubs) _teams[t.Id] = t;
        if (College != null) foreach (var t in College.Programs) _teams[t.Id] = t;
    }

    // ------------------------------------------------------------------------------------------
    // Generation

    /// <summary>
    /// Build the world around an NABL league: clubs, players (ids continue after the NABL's), and the first season's
    /// fixtures. Uses its own random stream, so the NABL built from a seed is unchanged.
    /// </summary>
    /// <param name="expandTop">Grow the top league to 32 now (a new career). False when a world is built around a league that is
    /// already playing (a save from before the world, a test's session): its schedule stands, and it grows at the next summer.</param>
    /// <param name="legacyPyramid">Build the pyramid as careers before D39 A had it (six regional and sixteen local leagues
    /// below tier 3), to test how an older career converts to five tiers of 32 at its next summer.</param>
    public static GameWorld Generate(League nabl, ulong seed, IEnumerable<string> fullSim, DateOnly today, bool expandTop = true, bool legacyPyramid = false)
    {
        var world = new GameWorld(nabl, seed ^ 0x574F524C44UL);
        var rng = world._rng;
        var gen = new PlayerGenerator(new Rng(seed ^ 0x504C415945525321UL), nabl.NextPlayerId) { Names = NameBook.Of(nabl.AllActivePlayers, nabl.FreeAgents) };
        var full = new HashSet<string>(fullSim);
        int block = 0;
        var profiles = legacyPyramid
            ? Hardwood.Core.World.Competitions.Current.Where(p => p.Key is not (UsaPyramid.Tier4 or UsaPyramid.Tier5))
                .Concat(Hardwood.Core.World.Competitions.All.Where(p => p.Legacy && UsaPyramid.IsPyramid(p.Key)))
            : Hardwood.Core.World.Competitions.Current;
        foreach (var profile in profiles)
        {
            var comp = new WorldCompetition { Profile = profile, FullSim = full.Contains(profile.Key) };
            world.Competitions.Add(comp);
            if (profile.Clubs.Length == 0) continue;
            int id = FirstTeamId + 1000 * block++;
            foreach (var seedClub in profile.Clubs)
            {
                var team = NewClub(id++, seedClub, profile, rng);
                // Run 217 (K5): a real club's place in its league's hierarchy shifts its roster (the draw is the same).
                double quality = rng.NextGaussian(0, 7) + seedClub.Standing;
                for (int s = 0; s < RosterSize; s++) team.Roster.Add(NewPlayer(team, profile, seedClub, s, quality, today, gen, rng, foreignSlot: false));
                ApplyForeignMix(team, profile, seedClub, quality, today, gen, rng);
                comp.Clubs.Add(team);
            }
        }
        // College: every American player from 14 to 22 (decisions #82-85; D80 A).
        // R3: a real-world career (its top league is the NBA) plays with the real Division I programs.
        world.College = Hardwood.Core.College.CollegeWorld.Generate(gen, new Rng(seed ^ 0x434F4C4C45474555UL), today,
            real: Hardwood.Core.RealWorld.RealWorldData.IsReal(nabl));
        nabl.CollegeActive = true;
        nabl.NextPlayerId = gen.NextId;
        // The top league grows to 32 with the two strongest tier-2 clubs (Danny's D33 design, D36a A, #355).
        if (expandTop) UsaPyramid.ExpandTop(world, nabl, new StaffGenerator(new Rng(seed ^ 0x3332544F50UL)), today);
        world.Index();
        foreach (var c in world.Competitions.Where(c => c.Clubs.Count > 0 && c.Profile.Kind == CompetitionKind.Domestic))
            world.Levels[c.Key] = Math.Round(c.Clubs.SelectMany(t => t.Roster).Average(p => p.CurrentAbility), 1);
        world.StartSeason(nabl.Season);
        return world;
    }

    private static readonly int[] SlotCa = { 138, 130, 124, 118, 113, 108, 103, 98, 93, 88, 83, 78, 72 };

    private static Team NewClub(int id, ClubSeed c, CompetitionProfile profile, Rng rng)
    {
        var (primary, secondary) = Colours(rng);
        int capacity = rng.Next(5000, 16000);
        return new Team
        {
            Id = id, City = c.City, Name = c.Name, Abbreviation = c.Abbr, Conference = Conference.East, Division = profile.Short,
            ArenaCapacity = c.Capacity > 0 ? c.Capacity : capacity, PrimaryColor = primary, SecondaryColor = secondary, LogoStyle = rng.Next(0, 5),
            // European and Asian basketball is slower than the NABL: ~72 possessions in 40 minutes.
            Tactic = new Tactic { Pace = Math.Round(rng.NextGaussian(86, 3), 1) },
        };
    }

    /// <summary>Local players of a club's country; imports get a heritage drawn from the worldwide mix (mostly American).</summary>
    private static Player NewPlayer(Team team, CompetitionProfile profile, ClubSeed club, int slot, double quality, DateOnly today,
                                    PlayerGenerator gen, Rng rng, bool foreignSlot, int? ageOverride = null, int? caOverride = null)
    {
        var pos = (Position)(slot % 5);
        int ca = caOverride ?? (int)Math.Round(SlotCa[Math.Min(slot, SlotCa.Length - 1)] + profile.StrengthOffset + quality + rng.NextGaussian(0, 5));
        // The best players outside the NABL are NABL starters, not NABL stars: compress the top end (a 165 became a 150
        // and, once free to move, walked into the NABL as a star; movement audit, Nov 9).
        if (ca > 140) ca = 140 + (int)Math.Round((ca - 140) * 0.4);
        // H123 (run 121): abroad, rate made-up pros against the real NBA. Each league has a ceiling (Spain's best are NBA
        // rotation players, the Philippines' are not), and the best of them sit just above it.
        int? ceiling = UsaPyramid.IsPyramid(profile.Key) ? null : Ceiling(profile, foreignSlot);
        if (ceiling is int cap && ca > cap) ca = Math.Min(cap + CeilingReach, cap + (int)Math.Round((ca - cap) * 0.35));
        ca = Math.Clamp(ca, 45, 160);
        int age = ageOverride ?? (int)Math.Round(Math.Clamp(rng.NextGaussian(slot < 9 ? 27 : 23.5, 3.5), 18, 36));
        if (age < profile.MinAge) age = profile.MinAge; // pro at 22+ in the USA (decision #75; D80 A, run 128)
        string country = club.Country ?? profile.Country;
        Player p;
        if (foreignSlot)
        {
            Heritage h;
            do h = Heritages.Pick(rng); while (LocalHeritage(country) is { } local && h.Key == local.Key);
            p = gen.Generate(pos, ca, age, today, h, null);
        }
        else
        {
            var local = LocalHeritage(country) ?? Heritages.All[0];
            string nat = country == "Australia" && rng.Chance(0.12) ? "New Zealand" : country;
            p = gen.Generate(pos, ca, age, today, local, nat);
        }
        // Same for potential: players who'll become NABL stars arrive through the NABL draft, not as overseas free
        // agents. (Overseas players grew to 175-184 and then walked in as free agents; issue #9.)
        if (p.PotentialAbility > 150) p.PotentialAbility = Math.Max(p.CurrentAbility, 150 + (int)Math.Round((p.PotentialAbility - 150) * 0.4));
        // H123: and they don't grow past the league's reach either (a 19-year-old at 154 / 161 in the summer pool).
        if (ceiling is int top && p.PotentialAbility > top + PotentialReach)
            p.PotentialAbility = Math.Max(p.CurrentAbility, Math.Min(top + PotentialReach + 4, top + PotentialReach + (int)Math.Round((p.PotentialAbility - top - PotentialReach) * 0.3)));
        p.TeamId = team.Id;
        p.Contract = NewContract(p, profile, rng.Next(1, 5), today, today.Year);
        return p;
    }

    /// <summary>How far above its league's ceiling a made-up pro abroad can be rated (H123).</summary>
    public const int CeilingReach = 6;
    /// <summary>How far above the ceiling his potential may reach before it is compressed (H123).</summary>
    public const int PotentialReach = 8;

    /// <summary>
    /// H123 (run 121): the rating a made-up pro abroad is compressed above: 138 plus half the league's strength offset
    /// (Spain 138, Greece 136, Lithuania 134, the Philippines 128), plus 4 for an import in a league whose imports are its stars.
    /// The real NBA's top-13 average is about 110 and its stars 160+, so the best of these are NBA rotation players.
    /// </summary>
    public static int Ceiling(CompetitionProfile profile, bool import) =>
        (int)Math.Round(138 + profile.StrengthOffset / 2.0) + (import && profile.ImportsAreStars ? 4 : 0);

    /// <summary>Swap some local players for imports, up to the league's limit (imports take the top slots in China and Australia).</summary>
    private static void ApplyForeignMix(Team team, CompetitionProfile profile, ClubSeed club, double quality, DateOnly today, PlayerGenerator gen, Rng rng)
    {
        int imports = profile.ImportsAreStars ? profile.ForeignLimit : Math.Max(0, profile.ForeignLimit - rng.Next(0, 2));
        var slots = profile.ImportsAreStars
            ? Enumerable.Range(0, imports).ToList()
            : Enumerable.Range(0, 10).OrderBy(_ => rng.NextDouble()).Take(imports).ToList();
        foreach (var s in slots)
        {
            var old = team.Roster[s];
            // Stars imported into weaker leagues are good players by that league's standard.
            int? ca = profile.ImportsAreStars ? old.CurrentAbility + 12 : null;
            team.Roster[s] = NewPlayer(team, profile, club, s, quality, today, gen, rng, foreignSlot: true, caOverride: ca);
        }
    }

    /// <summary>The heritage (names and looks) of a country's own players; null for the USA and unknown countries.</summary>
    private static Heritage? LocalHeritage(string country) =>
        country is "" or "USA" ? null
            // A country's own name list (weight 0) beats a shared regional one.
            : Heritages.All.Where(h => h.Nationalities.Contains(country)).OrderBy(h => h.Weight > 0).FirstOrDefault();

    /// <summary>An overseas contract. Every one carries a buyout clause (decision #53).</summary>
    public static Contract NewContract(Player p, CompetitionProfile profile, int years, DateOnly on, int startSeason)
    {
        long salary = Salary(p, profile, on);
        return new Contract { Years = years, Salary = salary, StartSeason = startSeason, Buyout = BuyoutFor(p, salary, years, on) };
    }

    /// <summary>
    /// Buyout clause: what it costs to take a player out of his contract early. Scales with salary and years left,
    /// and is steeper for young players clubs want to keep. Real EuroLeague-level buyouts run from a few hundred
    /// thousand to several million dollars.
    /// </summary>
    public static long BuyoutFor(Player p, long salary, int yearsLeft, DateOnly on)
    {
        int age = p.Age(on);
        double youth = age <= 21 ? 2.0 : age <= 24 ? 1.5 : 1.0;
        return Math.Max(250_000, (long)Math.Round(salary * Math.Max(1, yearsLeft) * 0.8 * youth / 10_000) * 10_000);
    }

    public static long Salary(Player p, CompetitionProfile profile, DateOnly on)
    {
        var nabl = ContractMarket.MarketSalary(p.CurrentAbility, p.Age(on), Finance.HardCapDefault);
        return Math.Max(60_000, (long)Math.Round(nabl * profile.SalaryScale / 10_000) * 10_000);
    }

    /// <summary>Non-local players at a club (the domestic foreign-player limit counts these).</summary>
    public static int Imports(Team t, CompetitionProfile home, string? clubCountry = null)
    {
        var locals = home.LocalNationalities.Length > 0 ? home.LocalNationalities : new[] { clubCountry ?? "" };
        return t.Roster.Count(p => !locals.Contains(p.Nationality));
    }

    private static (string, string) Colours(Rng rng)
    {
        double hue = rng.NextDouble() * 360, sat = 0.55 + rng.NextDouble() * 0.35, val = 0.45 + rng.NextDouble() * 0.35;
        string primary = Hsv(hue, sat, val);
        string secondary = rng.Next(0, 3) switch
        {
            0 => "#F4F4F4",
            1 => "#E8B530",
            _ => Hsv((hue + 150 + rng.NextDouble() * 60) % 360, 0.6, 0.85),
        };
        return (primary, secondary);
    }

    private static string Hsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        var (r, g, b) = h switch { < 60 => (c, x, 0.0), < 120 => (x, c, 0.0), < 180 => (0.0, c, x), < 240 => (0.0, x, c), < 300 => (x, 0.0, c), _ => (c, 0.0, x) };
        return $"#{(int)((r + m) * 255):X2}{(int)((g + m) * 255):X2}{(int)((b + m) * 255):X2}";
    }

    // ------------------------------------------------------------------------------------------
    // Seasons

    /// <summary>Set up every competition's season starting in <paramref name="year"/>: participants and regular-season fixtures.</summary>
    public void StartSeason(int year)
    {
        var taken = new HashSet<int>();
        foreach (var comp in Playing)
        {
            var participants = comp.Profile.Kind == CompetitionKind.Continental ? ContinentalEntrants(comp, taken) : comp.Clubs.Select(t => t.Id).ToList();
            comp.Season = new CompetitionSeason { Year = year, Participants = participants };
            comp.Season.Fixtures = RoundRobin(participants, comp.Profile, year);
        }
    }

    /// <summary>
    /// Clubs entering a continental competition: the top of each domestic league's last table (or the strongest clubs),
    /// skipping clubs already in a bigger cup this season (<paramref name="taken"/>). Careers from before a source league
    /// existed use the profile's legacy entrants (the guest pool).
    /// </summary>
    private List<int> ContinentalEntrants(WorldCompetition comp, HashSet<int> taken)
    {
        var ids = new List<int>();
        bool current = comp.Profile.Entrants.All(e => Competitions.Any(c => c.Key == e.LeagueKey));
        var entrants = current ? comp.Profile.Entrants : comp.Profile.LegacyEntrants;
        if (current && comp.Profile.Members.Length > 0)
        {
            ids = LicensedEntrants(comp.Profile, taken);
            taken.UnionWith(ids);
            return ids;
        }
        foreach (var (key, n) in entrants)
        {
            if (Competitions.FirstOrDefault(c => c.Key == key) is not { } source) continue;
            if (source.Profile.Kind == CompetitionKind.GuestPool) { ids.AddRange(source.Clubs.Take(n).Select(t => t.Id)); continue; }
            var table = source.Season.Fixtures.Any(f => f.Played) ? source.Season.Table().Select(r => r.TeamId).ToList()
                : source.Clubs.OrderByDescending(Strength).Select(t => t.Id).ToList();
            ids.AddRange(table.Where(id => !taken.Contains(id)).Take(n));
        }
        taken.UnionWith(ids);
        return ids;
    }

    /// <summary>The order a domestic league's clubs are picked in for a cup: last season's table, or by strength before any game.</summary>
    private List<int> CupOrder(WorldCompetition source) =>
        source.Season.Fixtures.Any(f => f.Played) ? source.Season.Table().Select(r => r.TeamId).ToList()
            : source.Clubs.OrderByDescending(Strength).Select(t => t.Id).ToList();

    /// <summary>
    /// Run 222: a cup with licensed clubs (the EuroLeague): every member in its source leagues enters, wherever it finished;
    /// the rest of the field comes from those leagues' tables, one club from each league in the listed order, round after round.
    /// </summary>
    private List<int> LicensedEntrants(CompetitionProfile p, HashSet<int> taken)
    {
        var members = p.Members.ToHashSet();
        var sources = p.Entrants.Select(e => Competitions.FirstOrDefault(c => c.Key == e.LeagueKey))
            .OfType<WorldCompetition>().Where(c => c.Profile.Kind == CompetitionKind.Domestic).ToList();
        var orders = sources.Select(CupOrder).ToList();
        var ids = new List<int>();
        foreach (var order in orders)
            ids.AddRange(order.Where(id => !taken.Contains(id) && _teams.TryGetValue(id, out var t) && members.Contains(t.City + "|" + t.Name)));
        int size = p.Size > 0 ? p.Size : p.Entrants.Sum(e => e.Clubs);
        bool added = true;
        while (ids.Count < size && added)
        {
            added = false;
            foreach (var order in orders)
            {
                if (ids.Count >= size) break;
                int next = order.FirstOrDefault(id => !taken.Contains(id) && !ids.Contains(id), -1);
                if (next < 0) continue;
                ids.Add(next); added = true;
            }
        }
        return ids.Take(Math.Max(size, 0)).ToList();
    }

    /// <summary>Run 222: the round dates for <paramref name="rounds"/> rounds. With <see cref="CompetitionProfile.RegularEnd"/> they are
    /// spread evenly over the round days up to that date (so some weeks have two rounds and some one); otherwise one round
    /// on each round day in turn.</summary>
    internal static List<DateOnly> RoundDates(CompetitionProfile p, int year, int rounds)
    {
        var days = new List<DateOnly>();
        var day = new DateOnly(year, p.Start.Month, p.Start.Day);
        if (p.RegularEnd is { } end)
        {
            var last = new DateOnly(year + 1, end.Month, end.Day);
            var slots = new List<DateOnly>();
            for (var d = day; d <= last; d = d.AddDays(1)) if (p.RoundDays.Contains(d.DayOfWeek)) slots.Add(d);
            if (slots.Count >= rounds && rounds > 0)
            {
                for (int i = 0; i < rounds; i++)
                    days.Add(slots[rounds == 1 ? 0 : (int)Math.Round(i * (slots.Count - 1) / (double)(rounds - 1))]);
                return days;
            }
        }
        for (int i = 0; i < rounds; i++)
        {
            while (!p.RoundDays.Contains(day.DayOfWeek)) day = day.AddDays(1);
            days.Add(day);
            day = day.AddDays(1);
        }
        return days;
    }

    private List<Fixture> RoundRobin(List<int> ids, CompetitionProfile p, int year)
    {
        var teams = ids.ToList();
        if (teams.Count % 2 == 1) teams.Add(-1); // bye
        int n = teams.Count;
        var rounds = new List<List<(int h, int a)>>();
        for (int pass = 0; pass < p.RoundRobins; pass++)
        {
            var rot = teams.ToList();
            for (int r = 0; r < n - 1; r++)
            {
                var round = new List<(int, int)>();
                for (int i = 0; i < n / 2; i++)
                {
                    int a = rot[i], b = rot[n - 1 - i];
                    if (a < 0 || b < 0) continue;
                    bool flip = (r + i + pass) % 2 == 1;
                    round.Add(flip ? (b, a) : (a, b));
                }
                rounds.Add(round);
                rot.Insert(1, rot[^1]); rot.RemoveAt(rot.Count - 1);
            }
        }

        var fixtures = new List<Fixture>();
        var dates = RoundDates(p, year, rounds.Count);
        int idx = 0;
        for (int r = 0; r < rounds.Count; r++)
        {
            var day = dates[r];
            foreach (var (h, a) in rounds[r])
                fixtures.Add(new Fixture { Index = idx++, Date = p.TwoDayRounds && _rng.Chance(0.5) ? day.AddDays(1) : day, HomeId = h, AwayId = a });
        }
        return fixtures;
    }

    /// <summary>Play every game dated on or before <paramref name="date"/> in every competition, scheduling playoffs as they come.</summary>
    public void PlayThrough(DateOnly date, GameSimulator sim)
    {
        foreach (var comp in Playing) PlayCompetition(comp, date, sim);
        if (College != null)
        {
            College.PlayThrough(date, sim, _rng);
            News.AddRange(College.News); foreach (var (t, _) in College.News) CollegeTitles.Add(t); College.News.Clear();
        }
    }

    /// <summary>Play out every competition's season to the end (used before the yearly rollover).</summary>
    public void FinishSeasons(GameSimulator sim) => PlayThrough(DateOnly.MaxValue, sim);

    private void PlayCompetition(WorldCompetition comp, DateOnly until, GameSimulator sim)
    {
        var s = comp.Season;
        // A promotion playoff can outlast tier 2's own final (run 102): its games are still played.
        while (!s.IsComplete || PromotionPending(s))
        {
            var next = s.Fixtures.Where(f => !f.Played).OrderBy(f => f.Date).FirstOrDefault();
            if (next == null || next.Date > until)
            {
                if (!Advance(comp)) return;      // nothing new to schedule
                continue;
            }
            var day = next.Date;
            foreach (var f in s.Fixtures.Where(f => !f.Played && f.Date == day).ToList()) Play(comp, f, sim);
        }
    }

    private void Play(WorldCompetition comp, Fixture f, GameSimulator sim)
    {
        var home = FixtureTeam(f.HomeId)!; var away = FixtureTeam(f.AwayId)!;
        var stats = UsaPyramid.IsPromotion(f.Stage) ? new Dictionary<int, StatLine>() : comp.Season.Stats;
        if (comp.FullSim)
        {
            var r = sim.Simulate(home, away, comp.Profile.Rules, _rng.NextULong(), false, f.Date);
            f.HomePts = r.Home.Points; f.AwayPts = r.Away.Points;
            AddLines(stats, r);
        }
        else
        {
            var (hp, ap) = QuickSim.Play(home, away, f.Date, _rng, stats, comp.Profile.Rules, comp.Profile.PointsPerTeam);
            f.HomePts = hp; f.AwayPts = ap;
        }
        AfterResult(comp, f);
    }

    private static void AddLines(Dictionary<int, StatLine> stats, GameResult r)
    {
        foreach (var box in new[] { r.Home, r.Away })
        foreach (var l in box.Lines.Where(l => l.Seconds > 0))
        {
            var st = stats.TryGetValue(l.Player.Id, out var x) ? x : stats[l.Player.Id] = new StatLine();
            st.TeamId = box.Team.Id; st.G++; if (l.Started) st.Gs++; st.Min += l.Minutes; st.Pts += l.Points; st.Reb += l.Rebounds; st.Ast += l.Assists;
            st.Stl += l.Steals; st.Blk += l.Blocks; st.Fgm += l.FGM; st.Fga += l.FGA; st.Tpm += l.ThreePM; st.Tpa += l.ThreePA;
        }
    }

    private static bool PromotionPending(CompetitionSeason s) => s.Series.Any(x => UsaPyramid.IsPromotion(x.Round) && !x.Complete);

    private void AfterResult(WorldCompetition comp, Fixture f)
    {
        if (f.SeriesId is { } sid)
        {
            var series = comp.Season.Series.First(x => x.Id == sid);
            if (f.WinnerId == series.HighId) series.HighWins++; else series.LowWins++;
            if (UsaPyramid.IsPromotion(series.Round) && series.Complete && FixtureTeam(series.WinnerId!.Value) is { } w && FixtureTeam(series.LoserId!.Value) is { } l)
            {
                string top = Hardwood.Core.Names.Brand.Top;
                bool up = series.WinnerId == series.HighId;
                News.Add(up
                    ? ($"Promotion playoff: {w.FullName} beat {l.FullName}", $"{w.FullName} beat {l.FullName} {series.Score} and will play in the {top} next season; {l.FullName} drop into {comp.Profile.Name} at the end of the summer.")
                    : ($"Promotion playoff: {w.FullName} stay up", $"{w.FullName} beat {l.FullName} {series.Score} and keep their place in the {top}; {l.FullName} stay in {comp.Profile.Name}."));
            }
        }
    }

    /// <summary>
    /// Start the promotion playoffs (run 102, <see cref="UsaPyramid.PlaysPromotion"/>) once both regular seasons are over: in
    /// each division, the top league's last club against the paired tier-2 division's winner, best of three, the tier-2 club
    /// at home in Games 1 and 3. Game 1 is on the next playoff day after <paramref name="today"/>. Does nothing if they've
    /// started, don't apply, or tier 2 is still in its regular season. Returns the pairings (top-league club, challenger).
    /// </summary>
    public List<(Team Top, Team Challenger)> StartPromotionPlayoffs(Season.Standings topTable, DateOnly today)
    {
        var made = new List<(Team, Team)>();
        if (!UsaPyramid.PlaysPromotion(this, Nabl)) return made;
        var t2 = this[UsaPyramid.Tier2];
        var s = t2.Season;
        // Tier 2's regular season must be over (its own playoffs may be running, or even finished).
        if (s.Stage == CompetitionSeason.RegularSeason || s.Series.Any(x => UsaPyramid.IsPromotion(x.Round))) return made;
        // A bracket seeded the old way (a save from before run 102) already has its division winners in it: no playoffs.
        if (s.Series.Any(x => x.RoundIndex == 0 && !Postseason.IsThirdPlace(x.Round))) return made;
        var order = s.Table().Select((r, i) => (r.TeamId, i)).ToDictionary(x => x.TeamId, x => x.i);
        foreach (var (conf, div) in Nabl.Teams.Select(t => (t.Conference, t.Division)).Distinct().OrderBy(x => x.Conference)
                     .ThenBy(x => Array.IndexOf(UsaPyramid.DivisionNames(x.Conference, UsaPyramid.DivisionNames(x.Conference, true).Contains(x.Division)), x.Division)).ToList())
        {
            var last = topTable.Conference(conf).Where(r => r.Team.Division == div).Select(r => r.Team).LastOrDefault();
            var winner = t2.Clubs.Where(t => t.Conference == conf && t.Division == div).OrderBy(t => order.GetValueOrDefault(t.Id, int.MaxValue)).ThenBy(t => t.Id).FirstOrDefault();
            if (last == null || winner == null) continue;
            s.Series.Add(new SeriesState { Id = s.Series.Count + 1, RoundIndex = 0, Round = UsaPyramid.PromotionRound, BestOf = UsaPyramid.PromotionBestOf, HighId = winner.Id, LowId = last.Id });
            made.Add((last, winner));
        }
        ScheduleNextGames(t2, notBefore: today.AddDays(1));
        return made;
    }

    /// <summary>Move the competition forward when nothing is left to play: seed the playoffs, add the next game of each series, or crown a champion.</summary>
    private bool Advance(WorldCompetition comp)
    {
        var s = comp.Season;
        var p = comp.Profile;
        if (s.Fixtures.Any(f => !f.Played)) return false;
        if (s.IsComplete)
        {
            if (!PromotionPending(s)) return false;
            ScheduleNextGames(comp);
            return true;
        }
        // A league with fewer than two clubs has nothing to play and no champion (review #11: a drained local league crashed here).
        if (s.Stage == CompetitionSeason.RegularSeason && s.Participants.Count < 2) { s.Stage = CompetitionSeason.Complete; return true; }

        if (p.DivisionPlayoffs) return AdvanceDivisions(comp);
        void FirstRound(List<int> seeds)
        {
            var order = BracketOrder(seeds.Count);
            for (int i = 0; i < order.Count; i += 2)
                s.Series.Add(new SeriesState { Id = s.Series.Count + 1, RoundIndex = 0, Round = p.Playoffs[0].Name, BestOf = p.Playoffs[0].BestOf, HighId = seeds[order[i]], LowId = seeds[order[i + 1]] });
            s.Stage = p.Playoffs[0].Name;
            ScheduleNextGames(comp);
        }
        if (s.Stage == CompetitionSeason.RegularSeason)
        {
            if (p.Playoffs.Length == 0) { Crown(comp, s.Table()[0].TeamId, s.Table()[1].TeamId); return true; }
            var rows = s.Table();
            int direct = p.Playoffs[0].Teams - 2;
            if (p.PlayIn && direct >= 1 && rows.Count >= p.Playoffs[0].Teams + 2)
            {
                // Run 222: the play-in (7v8 and 9v10, single games at the higher seed).
                s.Series.Add(new SeriesState { Id = s.Series.Count + 1, RoundIndex = -1, Round = PlayInHigh, BestOf = 1, HighId = rows[direct].TeamId, LowId = rows[direct + 1].TeamId });
                s.Series.Add(new SeriesState { Id = s.Series.Count + 1, RoundIndex = -1, Round = PlayInLow, BestOf = 1, HighId = rows[direct + 2].TeamId, LowId = rows[direct + 3].TeamId });
                s.Stage = PlayInStage;
                ScheduleNextGames(comp);
                return true;
            }
            FirstRound(rows.Take(p.Playoffs[0].Teams).Select(r => r.TeamId).ToList());
            return true;
        }
        if (s.Stage is PlayInStage or PlayInFinal)
        {
            var playIn = s.Series.Where(x => x.Round is PlayInHigh or PlayInLow or PlayInFinal).ToList();
            if (playIn.Any(x => !x.Complete)) { ScheduleNextGames(comp); return true; }
            var high = playIn.First(x => x.Round == PlayInHigh);
            var low = playIn.First(x => x.Round == PlayInLow);
            if (s.Stage == PlayInStage)
            {
                // The 7v8 loser hosts the 9v10 winner for the 8th seed.
                s.Series.Add(new SeriesState { Id = s.Series.Count + 1, RoundIndex = -1, Round = PlayInFinal, BestOf = 1, HighId = high.LoserId!.Value, LowId = low.WinnerId!.Value });
                s.Stage = PlayInFinal;
                ScheduleNextGames(comp);
                return true;
            }
            var last = playIn.First(x => x.Round == PlayInFinal);
            var seeds = s.Table().Take(p.Playoffs[0].Teams - 2).Select(r => r.TeamId).ToList();
            seeds.Add(high.WinnerId!.Value); seeds.Add(last.WinnerId!.Value);
            News.Add(($"{p.Name} play-in: {_teams[high.WinnerId!.Value].FullName} and {_teams[last.WinnerId!.Value].FullName} go through",
                $"{_teams[high.WinnerId!.Value].FullName} beat {_teams[high.LoserId!.Value].FullName} for 7th place; {_teams[last.WinnerId!.Value].FullName} beat {_teams[last.LoserId!.Value].FullName} for 8th. The quarter-finals start next."));
            FirstRound(seeds);
            return true;
        }

        var active = s.Series.Where(x => x.Round == s.Stage).ToList();
        if (active.Any(x => !x.Complete)) { ScheduleNextGames(comp); return true; }

        // Review (run 222): the round comes from the stage's name, not the stored index: a save from before the EuroCup's
        // eighth-finals holds its quarter-finals at index 0, which is the eighth-finals' place now (it repeated the round).
        int named = Array.FindIndex(p.Playoffs, r => r.Name == s.Stage);
        int ri = named >= 0 ? named : active[0].RoundIndex;
        if (ri == p.Playoffs.Length - 1)
        {
            var final = active[0];
            Crown(comp, final.WinnerId!.Value, final.LoserId!.Value);
            return true;
        }
        var nextRound = p.Playoffs[ri + 1];
        // Bracket: winners of adjacent series meet; the better regular-season seed is "high".
        var table = s.Table().Select(r => r.TeamId).ToList();
        for (int i = 0; i < active.Count; i += 2)
        {
            int a = active[i].WinnerId!.Value, b = active[i + 1].WinnerId!.Value;
            bool aHigh = table.IndexOf(a) < table.IndexOf(b);
            s.Series.Add(new SeriesState { Id = s.Series.Count + 1, RoundIndex = ri + 1, Round = nextRound.Name, BestOf = nextRound.BestOf, HighId = aHigh ? a : b, LowId = aHigh ? b : a });
        }
        s.Stage = nextRound.Name;
        if (p.FinalFour && ri + 1 == p.Playoffs.Length - 2)
            News.Add(($"{p.Name} Final Four set", string.Join(", ", s.Series.Where(x => x.RoundIndex == ri + 1).SelectMany(x => new[] { x.HighId, x.LowId }).Select(id => _teams[id].FullName)) + " will meet for the title."));
        ScheduleNextGames(comp);
        return true;
    }

    /// <summary>Run 222: the play-in's rounds (the names hold "Play-In", which the bracket view leaves out, as it does the NBA's).</summary>
    public const string PlayInStage = "Play-In", PlayInHigh = "Play-In 7/8", PlayInLow = "Play-In 9/10", PlayInFinal = "Play-In Final";

    public const string ThirdQuarters = Postseason.ThirdQuarters, ThirdSemis = Postseason.ThirdSemis, ThirdFinal = Postseason.ThirdFinal;

    /// <summary>
    /// The division format (D36, #364): at the end of the regular season each conference's eight seeds play 1v8, 4v5, 3v6, 2v7,
    /// winners of adjacent series meet, the conference champions play the final; the third-placed teams play their own bracket
    /// (best of three) in step with the first three rounds. The tournament's winner takes a trophy and prize money.
    /// </summary>
    private bool AdvanceDivisions(WorldCompetition comp)
    {
        var s = comp.Season;
        var p = comp.Profile;
        if (s.Stage == CompetitionSeason.RegularSeason)
        {
            var table = s.Table();
            var clubs = s.Participants.Select(id => _teams[id]).ToList();
            // Run 102: tier 2's division winners play the promotion playoffs instead (Danny: the runners-up play for the title).
            bool winnersOut = p.Key == UsaPyramid.Tier2 && UsaPyramid.PlaysPromotion(this, Nabl);
            if (UsaPyramid.DivisionField(p.Key, clubs, table, winnersOut) is not { } field)
            {
                Crown(comp, table[0].TeamId, table[1].TeamId);   // not (yet) four divisions of four: the table decides
                return true;
            }
            // Four seeds a conference start a round later (1v4, 2v3), eight start at the first round.
            int startRi = winnersOut ? 1 : 0;
            var first = p.Playoffs[startRi];
            var pairs = winnersOut ? new[] { (0, 3), (1, 2) } : new[] { (0, 7), (3, 4), (2, 5), (1, 6) };
            foreach (var seeds in new[] { field.East, field.West })
                foreach (var (a, b) in pairs)
                    s.Series.Add(new SeriesState { Id = s.Series.Count + 1, RoundIndex = startRi, Round = first.Name, BestOf = first.BestOf, HighId = seeds[a], LowId = seeds[b] });
            foreach (var (a, b) in new[] { (0, 7), (3, 4), (2, 5), (1, 6) })
                s.Series.Add(new SeriesState { Id = s.Series.Count + 1, RoundIndex = 0, Round = ThirdQuarters, BestOf = 3, HighId = field.Thirds[a], LowId = field.Thirds[b] });
            s.Stage = first.Name;
            ScheduleNextGames(comp);
            return true;
        }
        // The promotion playoffs run alongside and don't hold up the title bracket (run 102).
        if (s.Series.Any(x => !x.Complete && !UsaPyramid.IsPromotion(x.Round))) { ScheduleNextGames(comp); return true; }

        var tableIds = s.Table().Select(r => r.TeamId).ToList();
        int Seed(int id) { int i = tableIds.IndexOf(id); return i < 0 ? int.MaxValue : i; }
        SeriesState Pair(SeriesState x, SeriesState y, int ri, string round, int bestOf)
        {
            int a = x.WinnerId!.Value, b = y.WinnerId!.Value;
            bool aHigh = Seed(a) <= Seed(b);
            return new SeriesState { Id = s.Series.Count + 1, RoundIndex = ri, Round = round, BestOf = bestOf, HighId = aHigh ? a : b, LowId = aHigh ? b : a };
        }

        var main = s.Series.Where(x => x.Round == s.Stage).ToList();
        int ri = main[0].RoundIndex;
        if (ri == p.Playoffs.Length - 1)
        {
            // A bracket of runners-up (run 102) plays its third-place final alongside the title final: crown it first, so
            // the trophy, the prize and the history row's third-place club all land (review, run 102).
            if (s.Series.FirstOrDefault(x => x.Round == ThirdFinal) is { Complete: true } tf3 && s.ThirdPlaceId == null)
                CrownThirdPlace(comp, tf3.WinnerId!.Value, tf3.LoserId!.Value);
            Crown(comp, main[0].WinnerId!.Value, main[0].LoserId!.Value);
            return true;
        }
        var next = p.Playoffs[ri + 1];
        for (int i = 0; i + 1 < main.Count; i += 2) s.Series.Add(Pair(main[i], main[i + 1], ri + 1, next.Name, next.BestOf));

        // The third-place bracket: quarter-finals, semi-finals, final, alongside the first three rounds the title bracket plays
        // (counted from its first: a bracket of runners-up starts at the conference semi-finals, run 102).
        int firstRi = s.Series.Where(x => !Postseason.IsThirdPlace(x.Round) && !UsaPyramid.IsPromotion(x.Round)).Min(x => x.RoundIndex);
        int step = ri - firstRi;
        string? thirdFrom = step == 0 ? ThirdQuarters : step == 1 ? ThirdSemis : null;
        if (thirdFrom != null)
        {
            var done = s.Series.Where(x => x.Round == thirdFrom).ToList();
            string to = step == 0 ? ThirdSemis : ThirdFinal;
            for (int i = 0; i + 1 < done.Count; i += 2) s.Series.Add(Pair(done[i], done[i + 1], ri + 1, to, 3));
        }
        else if (s.Series.FirstOrDefault(x => x.Round == ThirdFinal) is { Complete: true } tf && s.ThirdPlaceId == null)
            CrownThirdPlace(comp, tf.WinnerId!.Value, tf.LoserId!.Value);

        s.Stage = next.Name;
        ScheduleNextGames(comp);
        return true;
    }

    /// <summary>The third-place tournament's trophy and prize money (D36c A): a share of the league's revenue, half to the runner-up.</summary>
    private void CrownThirdPlace(WorldCompetition comp, int winner, int runnerUp)
    {
        var s = comp.Season;
        s.ThirdPlaceId = winner; s.ThirdPlaceRunnerUpId = runnerUp;
        long prize = (long)(Budgets.LeagueRevenue(comp.Profile.SalaryScale) * Postseason.ThirdPlacePrize);
        _teams[winner].Money.Balance += prize;
        _teams[runnerUp].Money.Balance += prize / 2;
        News.Add(($"{_teams[winner].FullName} win the {comp.Profile.Name}'s {Postseason.ThirdPlaceName}",
            $"{_teams[winner].FullName} beat {_teams[runnerUp].FullName} in the {comp.Profile.Name}'s third-place final and take the trophy and {Franchise.Money(prize)} in prize money."));
    }

    /// <summary>Add the next game for every series in the current round that needs one.</summary>
    private void ScheduleNextGames(WorldCompetition comp, DateOnly? notBefore = null)
    {
        var s = comp.Season;
        var p = comp.Profile;
        var lastPlayed = s.Fixtures.Where(f => f.Played).Select(f => f.Date).DefaultIfEmpty(new DateOnly(s.Year, p.Start.Month, p.Start.Day)).Max();
        if (notBefore is { } nb && lastPlayed < nb.AddDays(-1)) lastPlayed = nb.AddDays(-1);
        HashSet<(int, DateOnly)>? busy = null;
        foreach (var series in s.Series.Where(x => (x.Round == s.Stage || (s.Stage == PlayInStage && x.Round is PlayInHigh or PlayInLow)
                     || (p.DivisionPlayoffs && Postseason.IsThirdPlace(x.Round)) || UsaPyramid.IsPromotion(x.Round)) && !x.Complete).ToList())
        {
            if (s.Fixtures.Any(f => f.SeriesId == series.Id && !f.Played)) continue;
            int gameNo = series.HighWins + series.LowWins + 1;
            var prev = s.Fixtures.Where(f => f.SeriesId == series.Id).Select(f => f.Date).DefaultIfEmpty(lastPlayed).Max();
            DateOnly date;
            // Run 222: the play-in is single games too, but in the regular playoff weeks.
            if (p.FinalFour && series.BestOf == 1 && series.RoundIndex >= p.Playoffs.Length - 2)
            {
                // Final Four weekend in late May: semi-finals on Friday, the final on Sunday.
                var friday = new DateOnly(s.Year + 1, 5, 20);
                while (friday.DayOfWeek != DayOfWeek.Friday) friday = friday.AddDays(1);
                date = series.RoundIndex == p.Playoffs.Length - 1 ? friday.AddDays(2) : friday;
                if (date <= prev) date = prev.AddDays(2);
            }
            else
            {
                // Run 222: never on a day either club already plays in another competition (a domestic playoff game against
                // a EuroCup round, or a cup knockout against a domestic playoff game already set).
                date = prev.AddDays(1);
                busy ??= BusyElsewhere(comp);
                while (!p.PlayoffDays.Contains(date.DayOfWeek) || busy.Contains((series.HighId, date)) || busy.Contains((series.LowId, date)))
                    date = date.AddDays(1);
            }
            bool highHome = series.BestOf switch { 3 => gameNo != 2, 5 => gameNo is 1 or 2 or 5, 7 => gameNo is 1 or 2 or 5 or 7, _ => true };
            s.Fixtures.Add(new Fixture
            {
                Index = s.Fixtures.Count, Date = date, Stage = series.Round, SeriesId = series.Id,
                HomeId = highHome ? series.HighId : series.LowId, AwayId = highHome ? series.LowId : series.HighId,
            });
        }
    }

    /// <summary>Every (club, day) with a game in a competition other than <paramref name="comp"/>, for this competition's clubs
    /// (built once per scheduling pass; review, run 222).</summary>
    private HashSet<(int, DateOnly)> BusyElsewhere(WorldCompetition comp)
    {
        var mine = comp.Season.Participants.ToHashSet();
        var busy = new HashSet<(int, DateOnly)>();
        foreach (var c in Playing)
        {
            if (c == comp || !c.Season.Participants.Any(mine.Contains)) continue;
            foreach (var f in c.Season.Fixtures)
            {
                if (mine.Contains(f.HomeId)) busy.Add((f.HomeId, f.Date));
                if (mine.Contains(f.AwayId)) busy.Add((f.AwayId, f.Date));
            }
        }
        return busy;
    }

    private void Crown(WorldCompetition comp, int champion, int runnerUp)
    {
        var s = comp.Season;
        s.ChampionId = champion; s.RunnerUpId = runnerUp; s.Stage = CompetitionSeason.Complete;
        var c = _teams[champion]; var r = _teams[runnerUp];
        comp.History.Add(new ChampionRecord(s.Year, champion, runnerUp, c.FullName, r.FullName,
            s.ThirdPlaceId is { } tp && _teams.TryGetValue(tp, out var tpt) ? tpt.FullName : null));
        News.Add(($"{c.FullName} win the {comp.Profile.Name}", $"{c.FullName} beat {r.FullName} to win the {s.Year}-{(s.Year + 1) % 100:00} {comp.Profile.Name}."));
    }

    /// <summary>Seed order for a bracket of n (power of two) so that 1 and 2 can only meet in the final.</summary>
    private static List<int> BracketOrder(int n)
    {
        var order = new List<int> { 0 };
        while (order.Count < n)
        {
            int m = order.Count * 2;
            order = order.SelectMany(x => new[] { x, m - 1 - x }).ToList();
        }
        return order;
    }

    /// <summary>A club's strength: the weighted ability of its best eight players.</summary>
    public static double Strength(Team t) => QuickSim.Strength(t);

    // ------------------------------------------------------------------------------------------
    // Yearly turnover: the summer has a start and an end so players can move between leagues in between.

    /// <summary>Summer (calendar year) whose start has run; the save keeps it so a reload mid-summer is consistent.</summary>
    public int SummerStarted { get; set; }
    /// <summary>Summer whose end has run (overseas rosters filled, next season started).</summary>
    public int SummerEnded { get; set; }

    /// <summary>The club abroad you coach (set by the session): its contracts are yours to decide, not the computer's.</summary>
    public int? ManagedClubId { get; set; }
    /// <summary>Your club abroad's expiring players this summer, waiting for you to renew or release them (saved).</summary>
    public Dictionary<int, List<Player>> AwaitingDecision { get; } = new();

    /// <summary>A contract abroad at an agreed wage (with its buyout clause, decision #53).</summary>
    public static Contract ContractAt(Player p, long wage, int years, DateOnly on, int startSeason) =>
        new() { Years = years, Salary = wage, StartSeason = startSeason, Buyout = BuyoutFor(p, wage, years, on) };

    /// <summary>NABL-level ability: overseas players at least this good test the open market when their deals end.</summary>
    public const int NablCalibre = 135;

    /// <summary>
    /// Start of summer (the NABL offseason begins): finish every season, age and develop every player outside the NABL,
    /// retire the old, and settle expiring contracts. Clubs keep most of their players; players good enough for the
    /// NABL usually test the market, and players a club no longer wants are released. Everyone who leaves joins the
    /// shared free-agent pool, where NABL teams can sign them (decision #41).
    /// </summary>
    public void BeginSummer(int summerYear, GameSimulator sim, List<Player> freeAgents)
    {
        if (SummerStarted == summerYear) return;
        FinishSeasons(sim);
        var date = new DateOnly(summerYear, 7, 1);
        LeftClub.Clear(); SignedAbroad.Clear(); BuyoutMoves.Clear(); Stashes.Clear(); Departed.Clear();
        if (College != null)
        {
            // Graduation, growth and the new recruiting class (decisions #84, #98).
            var cgen = new PlayerGenerator(new Rng(_rng.NextULong()), Nabl.NextPlayerId) { Names = NameBook.Of(this, freeAgents) };
            College.Summer(date, freeAgents, cgen, new DevelopmentEngine(_rng), _rng);
            Nabl.NextPlayerId = cgen.NextId;
            News.AddRange(College.News); foreach (var (t, _) in College.News) CollegeTitles.Add(t); College.News.Clear();
        }
        // Draft rights lapse once a player is 27 (he's no longer a prospect the team is waiting on).
        foreach (var p in Players.Concat(Nabl.FreeAgents)) if (p.DraftRightsTeamId != null && p.Age(date) >= 27) p.DraftRightsTeamId = null;
        // The books close abroad too: budgets for next season from revenue, fans and results (decision #88).
        foreach (var comp in Competitions.Where(c => c.Clubs.Count > 0))
        {
            var table = comp.Plays && comp.Season.Fixtures.Any(f => f.Played) ? comp.Season.Table().ToDictionary(r => r.TeamId) : new();
            Budgets.Initialize(comp.Clubs, comp.Profile.SalaryScale, _rng);
            Budgets.Summer(comp.Clubs, comp.Profile.SalaryScale,
                t => table.TryGetValue(t.Id, out var r) && r.Games > 0 ? r.Pct : 0.5,
                t => comp.Season.ChampionId == t.Id || Competitions.Any(c => c.Profile.Kind == CompetitionKind.Continental && c.Season.ChampionId == t.Id),
                summerYear - 1);
        }
        var dev = new DevelopmentEngine(_rng);
        foreach (var comp in Competitions.Where(c => c.Clubs.Count > 0))
        foreach (var team in comp.Clubs)
        {
            double clubLevel = team.Roster.Count == 0 ? 0 : team.Roster.OrderByDescending(p => p.CurrentAbility).Take(9).Average(p => p.CurrentAbility);
            // Champions get raided: their best players are courted, and the bills for a winning squad come due.
            bool champion = comp.Profile.Kind == CompetitionKind.Domestic && comp.Season.ChampionId == team.Id;
            var stars = champion ? team.Roster.OrderByDescending(p => p.CurrentAbility).Take(3).ToHashSet() : new HashSet<Player>();
            foreach (var p in team.Roster.ToList())
            {
                double mpg = Mpg(p.Id);
                dev.Develop(p, date, mpg, 10 + StaffEffects.FacilityTraining(team, p, date)); // facilities (item M, #281)
                bool expiring = p.Contract == null || --p.Contract.Years <= 0;
                if (p.Contract != null && !expiring) p.Contract.Buyout = BuyoutFor(p, p.Contract.Salary, p.Contract.Years, date);
                var locals = LocalsOf(comp.Profile, ClubOf(comp, team));
                var departure = new Departure(p.CurrentAbility, !locals.Contains(p.Nationality));
                if (dev.ShouldRetire(p, date, mpg, seasonsLeft: !expiring) || (expiring && p.Age(date) >= 33 && _rng.Chance(0.5)))
                {
                    DepartedFrom(team).Add(departure);
                    team.Roster.Remove(p); p.TeamId = null; p.Contract = null; Nabl.Retired.Add(p);
                    continue;
                }
                if (!expiring) continue;
                if (team.Id == ManagedClubId)
                {
                    // Your club: the player waits for your decision (renew or release) during the summer.
                    if (p.Contract != null) p.Contract.Years = 0;
                    (AwaitingDecision.TryGetValue(team.Id, out var waiting) ? waiting : AwaitingDecision[team.Id] = new()).Add(p);
                    continue;
                }
                // 20% when the world had five leagues; the wider world (decision #111) has about 60% more players of NABL calibre.
                bool testsMarket = (p.CurrentAbility >= NablCalibre && p.Age(date) <= 31 && _rng.Chance(0.12))
                                   || (stars.Contains(p) && p.Age(date) <= 31 && _rng.Chance(0.45));
                bool clubWants = p.CurrentAbility >= clubLevel - 18 || p.Age(date) <= 23;
                // H61 (run 110): a player far too good for the league won't renew at its scale (Giannis re-signed in the
                // G League for $2.35M); he goes to the pool, where the top league's free agency comes first.
                // Checked after the club's draw, so the world reads the same random numbers as before.
                if (!testsMarket && clubWants && _rng.Chance(0.95) && !PricedOut(p, comp, date))
                    p.Contract = NewContract(p, comp.Profile, _rng.Next(1, 4), date, summerYear);
                else
                {
                    team.Roster.Remove(p); p.TeamId = null; p.Contract = null;
                    freeAgents.Add(p);
                    LeftClub.Add((p, team));
                    DepartedFrom(team).Add(departure);
                }
            }
        }
        SummerStarted = summerYear;
    }

    /// <summary>
    /// H61 (run 110): the leagues open to a player for his asking floor (<see cref="ContractMarket.AskingFloor"/>): the top
    /// league at its mean ability, every domestic league where he counts as a local (the USA pyramid for Americans), and the
    /// best-paying import league abroad (run 111).
    /// </summary>
    /// Worked out once per nationality and kept until the season or the summer moves on (the summer's refill asks it for
    /// every player in the pool at every pick).
    public IReadOnlyList<(double Level, double Scale)> Options(Player p)
    {
        // Run 218 (Run198's reload test, in the real-clubs world): the stamp now holds what the list is made of, the top
        // league's mean ability and the league levels. It held only counts, so a list cached weeks earlier (or by a page that
        // asked) was still used in the summer, while a reloaded career built a fresh one: the two priced players differently
        // and drew different numbers.
        var top = Nabl.Teams.SelectMany(t => t.Roster).Select(x => (double)x.CurrentAbility).DefaultIfEmpty(0).Average();
        double levels = 0; foreach (var v in Levels.Values) levels += v;
        var stamp = (Nabl.Season, SummerStarted, SummerEnded, Competitions.Count, Levels.Count, top, levels);
        if (stamp != _optionsStamp) { _options.Clear(); _optionsStamp = stamp; }
        if (_options.TryGetValue(p.Nationality, out var list)) return list;
        list = new List<(double, double)>();
        list.Add((top, 1.0));
        foreach (var c in Competitions)
            if (c.Profile.Kind == CompetitionKind.Domestic && c.Clubs.Count > 0 && Levels.TryGetValue(c.Key, out var level)
                && (c.Profile.LocalNationalities.Length > 0 ? c.Profile.LocalNationalities.Contains(p.Nationality) : c.Profile.Country == p.Nationality))
                list.Add((level, c.Profile.SalaryScale));
        // K1 part 2 (run 111): the best-paying league abroad that takes imports, so a foreign star (a Belgian CA 143) doesn't ask
        // only his home league's wage of a small club. Leagues outside the USA pyramid only: an import there is a real option.
        var abroad = Competitions.Where(c => c.Profile.Kind == CompetitionKind.Domestic && c.Clubs.Count > 0 && c.Profile.ForeignLimit > 0
                && !UsaPyramid.IsPyramid(c.Key) && c.Profile.Country != p.Nationality && Levels.ContainsKey(c.Key))
            .OrderByDescending(c => c.Profile.SalaryScale).ThenBy(c => c.Key).FirstOrDefault();
        if (abroad != null) list.Add((Levels[abroad.Key], abroad.Profile.SalaryScale));
        return _options[p.Nationality] = list;
    }
    private readonly Dictionary<string, List<(double Level, double Scale)>> _options = new();
    private (int, int, int, int, int, double, double) _optionsStamp;

    /// <summary>H61 (run 110): the least this player takes to play below the top league.</summary>
    public long AskingFloor(Player p, DateOnly on) => ContractMarket.AskingFloor(p, on, Options(p));

    /// <summary>
    /// H61 (run 226): the top league's summer market is open: the world's summer has started (contracts abroad counted down)
    /// and its end (clubs abroad filling their rosters from the pool) hasn't run.
    /// </summary>
    public bool TopMarketOpen => SummerStarted > SummerEnded;

    /// <summary>
    /// H61 (run 226): the least this player takes of a club below the top league while the top league's free agency is open:
    /// his usual floor, or, if he is good enough for the top league, most of his top-league market (he waits for its offers).
    /// Once its free agency is over and nobody there signed him, the usual floor applies (#932 "unless no better offer
    /// comes"). Only your own club's talks use it: the computer's clubs abroad sign from the pool at the end of the summer.
    /// </summary>
    public long SummerAskingFloor(Player p, DateOnly on)
    {
        long floor = AskingFloor(p, on);
        return TopMarketOpen ? Math.Max(floor, ContractMarket.TopLeagueFloor(p, on, Options(p)[0].Level)) : floor;
    }

    /// <summary>
    /// H61 (run 110): would this player turn down a deal in <paramref name="comp"/>? Yes when his asking floor (from the
    /// best league open to him) is more than <paramref name="comp"/> pays a player of his level.
    /// </summary>
    public bool PricedOut(Player p, WorldCompetition comp, DateOnly on) =>
        comp.Profile.Kind == CompetitionKind.Domestic && AskingFloor(p, on) > Salary(p, comp.Profile, on);

    /// <summary>A player a club lost this summer: his level and whether he filled an import slot. Clubs replace like for like.</summary>
    public sealed record Departure(int Ca, bool Import);
    /// <summary>Who each club lost at the start of this summer (saved, so a reload mid-summer replaces them properly).</summary>
    public Dictionary<int, List<Departure>> Departed { get; } = new();
    private List<Departure> DepartedFrom(Team t) => Departed.TryGetValue(t.Id, out var l) ? l : Departed[t.Id] = new();

    /// <summary>Players who left their overseas club at the start of this summer (for headlines and the audit).</summary>
    public List<(Player player, Team from)> LeftClub { get; } = new();
    /// <summary>NABL teams buying players out of overseas contracts this summer (player, club, NABL team, buyout).</summary>
    public List<(Player player, Team club, Team team, long buyout)> BuyoutMoves { get; } = new();
    /// <summary>NABL draft picks sent overseas this summer, rights kept (player, club, NABL team).</summary>
    public List<(Player player, Team club, Team team)> Stashes { get; } = new();
    /// <summary>Free agents overseas clubs signed at the end of this summer.</summary>
    public List<(Player player, Team to)> SignedAbroad { get; } = new();

    /// <summary>
    /// End of summer (after NABL free agency): overseas clubs fill their rosters from the shared free-agent pool,
    /// imports within each league's limit (decision #56) and at the club's level, then with new players if the pool
    /// runs dry; then the next season starts. Players nobody signs stay in the pool.
    /// </summary>
    public void EndSummer(int newYear, GameSimulator sim, List<Player> freeAgents)
    {
        if (SummerEnded == newYear) return;
        if (SummerStarted != newYear) BeginSummer(newYear, sim, freeAgents);
        SummerEnded = newYear;
        var date = new DateOnly(newYear, 8, 15);
        // Your club abroad: anyone you didn't renew leaves now.
        foreach (var (teamId, waiting) in AwaitingDecision)
            if (TeamById(teamId) is { } club)
                foreach (var p in waiting.Where(p => club.Roster.Contains(p)).ToList())
                {
                    club.Roster.Remove(p); p.TeamId = null; p.Contract = null; freeAgents.Add(p);
                    LeftClub.Add((p, club));
                }
        AwaitingDecision.Clear();
        var gen = new PlayerGenerator(new Rng(_rng.NextULong()), Nabl.NextPlayerId) { Names = NameBook.Of(this, freeAgents) };
        // Each club replaces the players it lost like for like: an import for an import (within the foreign limit),
        // at about the same level. Clubs take turns, best first, one replacement at a time, so the pool spreads out.
        // The pool is tried first; if nobody there fits, the club finds a new player (sometimes a younger prospect).
        // (Refilling with weak teenagers drained every overseas league by 5-10 CA a decade; balance audit, Nov 9.)
        var clubs = Competitions.Where(c => c.Clubs.Count > 0).SelectMany(c => c.Clubs.Select(t => (comp: c, team: t))).ToList();
        // Talent anchor (critic review, Nov 10): like-for-like replacement drifts down, because a 34-year-old who
        // retires at CA 80 was a 100 at his peak and gets replaced by an 80. Over 100 seasons every league outside the
        // NABL lost ~25 CA. Each league leans its signings back toward its own level.
        var lean = Competitions.Where(c => c.Clubs.Count > 0 && c.Profile.Kind == CompetitionKind.Domestic)
            .ToDictionary(c => c.Key, AnchorCorrection);
        var leagueStrength = Competitions.Where(c => c.Clubs.Count > 0 && c.Profile.Kind == CompetitionKind.Domestic)
            .ToDictionary(c => c.Key, c => c.Clubs.Average(t => Strength(t)));
        var needs = clubs.ToDictionary(x => x.team.Id, x => new Queue<Departure>(
            (Departed.TryGetValue(x.team.Id, out var d) ? d : new List<Departure>()).OrderByDescending(d => d.Ca)));
        bool any = true;
        while (any)
        {
            any = false;
            // Weakest clubs choose first, like a draft (critic review #2: best-first let dynasties keep the best of the pool).
            foreach (var (comp, team) in clubs.OrderBy(x => Strength(x.team)))
            {
                // You build your own squad; the sporting director only makes up the numbers if you leave it short.
                if (team.Roster.Count >= (team.Id == ManagedClubId ? ManagedMinimum : RosterSize)) continue;
                if (team.Id == ManagedClubId) needs[team.Id].Clear();
                var club = ClubOf(comp, team);
                var locals = LocalsOf(comp.Profile, club);
                var need = needs[team.Id].Count > 0 ? needs[team.Id].Dequeue() : new Departure(ExpectedCa(comp.Profile, team, team.Roster.Count), false);
                if (lean.TryGetValue(comp.Key, out var shift) && shift != 0) need = need with { Ca = need.Ca + shift };
                // Parity (critic review #2/#3: Sydney won 68 of 100 titles): a club far above its league's level can't
                // replace like for like; the market prices it out and its replacements regress toward the league.
                if (comp.Profile.Kind == CompetitionKind.Domestic && leagueStrength.TryGetValue(comp.Key, out var avg))
                {
                    double excess = Strength(team) - avg;
                    if (excess > 4) need = need with { Ca = need.Ca - (int)Math.Round(Math.Min(14, (excess - 4) * 0.6)) };
                }
                any = true;
                int imports = team.Roster.Count(p => !locals.Contains(p.Nationality));
                bool importRoom = imports < comp.Profile.ForeignLimit;
                // US clubs look harder for a player in the pool (college graduates fill the pyramid, decision #98).
                int window = comp.Profile.MinAge > 0 ? 12 : 8;
                var pick = freeAgents
                    .Where(p => p.Age(date) <= 33 && p.CurrentAbility >= need.Ca - window && p.CurrentAbility <= need.Ca + window)
                    .Where(p => p.Age(date) >= comp.Profile.MinAge)
                    .Where(p => locals.Contains(p.Nationality) || (importRoom && need.Import))
                    .Where(p => !PricedOut(p, comp, date)) // H61 (run 110): a star won't sign for the league's scale
                    .OrderBy(p => Math.Abs(p.CurrentAbility - need.Ca) + _rng.NextDouble() * 6)
                    .FirstOrDefault();
                // K13 (run 133, critic review #18): before a club makes up a new player, it looks at this summer's college
                // graduates nobody else signed: a 22-year-old a little below the level it needs, but with the upside to
                // reach it. Real college starters who miss the NBA play in the lower leagues or abroad; they used to
                // retire at 22 while the clubs generated strangers.
                // Run 136: also when the pool's pick falls through (the 70%/95% chance below), before a stranger is made up.
                bool takePick = pick != null && _rng.Chance(comp.Profile.MinAge > 0 ? 0.95 : 0.7);
                var graduate = !takePick ? freeAgents
                    .Where(p => p.RookieSeason == newYear && p.Age(date) <= 24 && p.Age(date) >= comp.Profile.MinAge)
                    .Where(p => p.CurrentAbility >= need.Ca - GraduateReach && p.CurrentAbility <= need.Ca + window && p.PotentialAbility >= need.Ca)
                    .Where(p => locals.Contains(p.Nationality) || (importRoom && need.Import))
                    .Where(p => !PricedOut(p, comp, date))
                    .OrderByDescending(p => p.PotentialAbility + p.CurrentAbility).ThenBy(p => p.Id)
                    .FirstOrDefault() : null;
                if (graduate != null)
                {
                    freeAgents.Remove(graduate);
                    graduate.TeamId = team.Id;
                    graduate.Contract = NewContract(graduate, comp.Profile, _rng.Next(1, 4), date, newYear);
                    team.Roster.Add(graduate);
                    SignedAbroad.Add((graduate, team));
                    continue;
                }
                if (takePick)
                {
                    freeAgents.Remove(pick);
                    pick.TeamId = team.Id;
                    pick.Contract = NewContract(pick, comp.Profile, _rng.Next(1, 4), date, newYear);
                    team.Roster.Add(pick);
                    SignedAbroad.Add((pick, team));
                    continue;
                }
                bool prospect = _rng.Chance(0.35);
                int age = prospect ? _rng.Next(18, 22) : _rng.Next(23, 31);
                int target = (int)Math.Round(need.Ca - (prospect ? 6 : 0) + _rng.NextGaussian(0, 5));
                bool asImport = need.Import && importRoom;
                var signing = NewPlayer(team, comp.Profile, club, team.Roster.Count, 0, date, gen, _rng, foreignSlot: asImport, ageOverride: age, caOverride: target);
                signing.Contract!.StartSeason = newYear;
                team.Roster.Add(signing);
            }
        }
        Departed.Clear();
        // H56 (run 87): the minimum deals your sporting director signed to make up your roster are the owner's to fund.
        if (ManagedClubId is int mid && clubs.FirstOrDefault(x => x.team.Id == mid).team is { } managed)
            Budgets.OwnerFunds(managed, ContractMarket.Payroll(managed));
        // Promotion and relegation through the USA pyramid, before next season's fixtures (decisions #78-79, #91).
        foreach (var n in UsaPyramid.Promote(this, Nabl, new StaffGenerator(new Rng(_rng.NextULong())), date)) News.Add(n);
        // Relegation releases leave gaps: those clubs sign again at their new level.
        foreach (var comp in Competitions.Where(c => c.Clubs.Count > 0 && c.Profile.Kind == CompetitionKind.Domestic))
        foreach (var team in comp.Clubs.Where(t => t.Id != ManagedClubId))
        {
            var club = ClubOf(comp, team);
            while (team.Roster.Count < RosterSize)
            {
                int target = ExpectedCa(comp.Profile, team, team.Roster.Count);
                var pick = freeAgents.Where(p => p.Age(date) <= 33 && p.Age(date) >= comp.Profile.MinAge && Math.Abs(p.CurrentAbility - target) <= 10
                        && LocalsOf(comp.Profile, club).Contains(p.Nationality) && !PricedOut(p, comp, date)).OrderBy(p => Math.Abs(p.CurrentAbility - target)).FirstOrDefault();
                if (pick != null)
                {
                    freeAgents.Remove(pick); pick.TeamId = team.Id;
                    pick.Contract = NewContract(pick, comp.Profile, _rng.Next(1, 4), date, newYear);
                    team.Roster.Add(pick); SignedAbroad.Add((pick, team));
                    continue;
                }
                var signing = NewPlayer(team, comp.Profile, club, team.Roster.Count, 0, date, gen, _rng, foreignSlot: false, ageOverride: _rng.Next(21, 30), caOverride: target);
                signing.Contract!.StartSeason = newYear;
                team.Roster.Add(signing);
            }
        }
        // H88 (run 104): your own club skips the refill above (you pick your squad), but relegation clauses run after your
        // sporting director's summer signings, so a relegated club could open the season under the minimum (Charlotte FC,
        // 10 players on Oct 22). He makes up the numbers to the minimum, and the inbox names who he signed.
        if (ManagedClubId is int mine && Competitions.FirstOrDefault(c => c.Clubs.Count > 0 && c.Profile.Kind == CompetitionKind.Domestic && c.Clubs.Any(t => t.Id == mine)) is { } myComp)
        {
            var team = myComp.Clubs.First(t => t.Id == mine);
            var club = ClubOf(myComp, team);
            var made = new List<Player>();
            while (team.Roster.Count < ManagedMinimum)
            {
                int target = ExpectedCa(myComp.Profile, team, team.Roster.Count);
                var pick = freeAgents.Where(p => p.Age(date) <= 33 && p.Age(date) >= myComp.Profile.MinAge && Math.Abs(p.CurrentAbility - target) <= 10
                        && LocalsOf(myComp.Profile, club).Contains(p.Nationality) && (p.DraftRightsTeamId == null || p.DraftRightsTeamId == mine)
                        && !PricedOut(p, myComp, date))
                    .OrderBy(p => Math.Abs(p.CurrentAbility - target)).ThenBy(p => p.Id).FirstOrDefault();
                Player signing;
                if (pick != null)
                {
                    freeAgents.Remove(pick); pick.TeamId = team.Id; pick.DraftRightsTeamId = null;
                    pick.Contract = NewContract(pick, myComp.Profile, 1, date, newYear);
                    team.Roster.Add(pick); SignedAbroad.Add((pick, team));
                    signing = pick;
                }
                else
                {
                    signing = NewPlayer(team, myComp.Profile, club, team.Roster.Count, 0, date, gen, _rng, foreignSlot: false, ageOverride: _rng.Next(21, 30), caOverride: target);
                    signing.Contract!.StartSeason = newYear;
                    signing.Contract.Years = 1;
                    signing.Contract.Buyout = BuyoutFor(signing, signing.Contract.Salary, 1, date); // priced for the one year (run 104 review)
                    team.Roster.Add(signing);
                }
                made.Add(signing);
            }
            if (made.Count > 0)
            {
                Budgets.OwnerFunds(team, ContractMarket.Payroll(team));
                News.Add(($"{team.FullName} make up the numbers",
                    $"{myComp.Profile.Name} needs at least {ManagedMinimum} players, and the squad was down to {team.Roster.Count - made.Count} after the summer's moves, so your sporting director signed "
                    + $"{string.Join(", ", made.Select(p => $"{p.Name} ({p.PrimaryPosition})"))} on one-year deals."));
            }
        }
        RetireFadingStars(freeAgents, date);
        Nabl.NextPlayerId = gen.NextId;
        College?.StartSeason(newYear, _rng);
        StartSeason(newYear);
    }

    /// <summary>
    /// H61 (run 226, #932 "some fading stars retire rather than drop that far"): at the end of the summer, a free agent of
    /// top-league ability aged <see cref="ContractMarket.FadingStarAge"/>+ whom no top-league club signed retires (LeBron
    /// James, 43, had signed for tier-5 Worcester at $0.43M). Reads no random numbers. The best of them make the news.
    /// </summary>
    private void RetireFadingStars(List<Player> freeAgents, DateOnly date)
    {
        if (Nabl.Teams.Count == 0) return;
        double top = Nabl.Teams.SelectMany(t => t.Roster).Select(x => (double)x.CurrentAbility).DefaultIfEmpty(0).Average();
        if (top <= 0) return;
        var gone = freeAgents.Where(p => p.Age(date) >= ContractMarket.FadingStarAge && p.CurrentAbility >= top + ContractMarket.TopLeagueMargin)
            .Distinct().OrderByDescending(p => p.CurrentAbility).ThenBy(p => p.Id).ToList();
        foreach (var p in gone)
        {
            freeAgents.RemoveAll(x => x == p); p.TeamId = null; p.Contract = null; p.DraftRightsTeamId = null;
            if (!Nabl.Retired.Contains(p)) Nabl.Retired.Add(p);
        }
        if (gone.Count == 0) return;
        string names = string.Join(", ", gone.Take(3).Select(p => $"{p.Name} ({p.Age(date)})")) + (gone.Count > 3 ? $" and {gone.Count - 3} more" : "");
        News.Add((gone.Count == 1 ? $"{gone[0].Name} retires" : $"{gone[0].Name} and {gone.Count - 1} more veterans retire",
            $"{names}: no {Hardwood.Core.Names.Brand.Top} club offered a deal this summer, and {(gone.Count == 1 ? "he" : "they")} would rather retire than play on for a smaller league's wage."));
    }

    /// <summary>Whole summer in one go (no NABL market in between): used by tests and tools that only need the world to roll on.</summary>
    public void Rollover(int newYear, GameSimulator sim)
    {
        var pool = new List<Player>();
        BeginSummer(newYear, sim, pool);
        EndSummer(newYear, sim, pool);
        foreach (var p in pool) { p.TeamId = null; Nabl.FreeAgents.Add(p); }
    }

    /// <summary>
    /// Each league's home level: its mean ability when the world was made (saved). Careers from before it was kept
    /// take the level the league has on first use, so nothing jumps.
    /// </summary>
    public Dictionary<string, double> Levels { get; } = new();

    /// <summary>How far to lean this summer's signings: a multiple of the gap between the league's home level and where it has drifted.</summary>
    private int AnchorCorrection(WorldCompetition c)
    {
        // The league as it stood before the summer's departures (counting them in): the players left behind are the
        // better ones, so measuring them alone would read a drained league as healthy.
        var cas = c.Clubs.SelectMany(t => t.Roster.Select(p => (double)p.CurrentAbility)
            .Concat(Departed.TryGetValue(t.Id, out var d) ? d.Select(x => (double)x.Ca) : Enumerable.Empty<double>())).ToList();
        if (cas.Count == 0) return 0;
        double now = cas.Average();
        if (!Levels.TryGetValue(c.Key, out var home)) Levels[c.Key] = home = Math.Round(now, 1);
        double gap = home - now;
        return (int)Math.Round(Math.Clamp(gap * 3.0, -12, 20));
    }

    /// <summary>The ability a club expects for its next roster spot: its league's slot table, shifted by how strong the club is.</summary>
    private static int ExpectedCa(CompetitionProfile profile, Team team, int slot)
    {
        double quality = team.Roster.Count >= 5 ? Strength(team) - (SlotCa.Take(8).Zip(new[] { .18, .16, .14, .13, .12, .10, .09, .08 }, (c, w) => c * w).Sum() + profile.StrengthOffset) : 0;
        return (int)Math.Round(SlotCa[Math.Min(slot, SlotCa.Length - 1)] + profile.StrengthOffset + Math.Clamp(quality, -15, 15));
    }

    private static ClubSeed ClubOf(WorldCompetition comp, Team team) =>
        comp.Profile.Clubs.FirstOrDefault(c => c.Abbr == team.Abbreviation) ?? new ClubSeed(team.City, team.Name, team.Abbreviation);

    private static string[] LocalsOf(CompetitionProfile profile, ClubSeed club) =>
        profile.LocalNationalities.Length > 0 ? profile.LocalNationalities : new[] { club.Country ?? "" };

    private double Mpg(int playerId)
    {
        int g = 0; double min = 0;
        foreach (var c in Playing)
            if (c.Season.Stats.TryGetValue(playerId, out var st)) { g += st.G; min += st.Min; }
        return g == 0 ? 0 : min / g;
    }
}
