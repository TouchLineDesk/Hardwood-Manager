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
    /// <summary>The fewest players your club abroad may start a season with (the sporting director fills to this).</summary>
    public const int ManagedMinimum = 10;

    private readonly Rng _rng;
    private readonly Dictionary<int, Team> _teams = new();

    public League Nabl { get; }
    /// <summary>College basketball (decisions #76, #82-85, #95-98); null in careers from before it existed.</summary>
    public College.CollegeWorld? College { get; set; }
    public List<WorldCompetition> Competitions { get; } = new();
    /// <summary>Headlines (champions, Final Four) since the session last read them.</summary>
    public List<(string Title, string Body)> News { get; } = new();

    public GameWorld(League nabl, ulong rngSeed)
    {
        Nabl = nabl;
        _rng = new Rng(rngSeed);
    }

    /// <summary>A seed to restore the world's random stream from (saved with the game).</summary>
    public ulong NextRngSeed() => _rng.NextULong();

    public WorldCompetition this[string key] => Competitions.First(c => c.Key == key);
    public IEnumerable<WorldCompetition> Playing => Competitions.Where(c => c.Plays);
    public IEnumerable<Team> Clubs => Competitions.SelectMany(c => c.Clubs);
    public IEnumerable<Player> Players => Clubs.SelectMany(t => t.Roster);
    public Team? TeamById(int id) => _teams.GetValueOrDefault(id);
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
        AddLines(comp.Season.Stats, r);
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
    public static GameWorld Generate(League nabl, ulong seed, IEnumerable<string> fullSim, DateOnly today)
    {
        var world = new GameWorld(nabl, seed ^ 0x574F524C44UL);
        var rng = world._rng;
        var gen = new PlayerGenerator(new Rng(seed ^ 0x504C415945525321UL), nabl.NextPlayerId);
        var full = new HashSet<string>(fullSim);
        int block = 0;
        foreach (var profile in Hardwood.Core.World.Competitions.All)
        {
            var comp = new WorldCompetition { Profile = profile, FullSim = full.Contains(profile.Key) };
            world.Competitions.Add(comp);
            if (profile.Clubs.Length == 0) continue;
            int id = FirstTeamId + 1000 * block++;
            foreach (var seedClub in profile.Clubs)
            {
                var team = NewClub(id++, seedClub, profile, rng);
                double quality = rng.NextGaussian(0, 7);
                for (int s = 0; s < RosterSize; s++) team.Roster.Add(NewPlayer(team, profile, seedClub, s, quality, today, gen, rng, foreignSlot: false));
                ApplyForeignMix(team, profile, seedClub, quality, today, gen, rng);
                comp.Clubs.Add(team);
            }
        }
        // College: every American player from 14 to 23 (decisions #82-85).
        world.College = Hardwood.Core.College.CollegeWorld.Generate(gen, new Rng(seed ^ 0x434F4C4C45474555UL), today);
        nabl.CollegeActive = true;
        nabl.NextPlayerId = gen.NextId;
        world.Index();
        world.StartSeason(nabl.Season);
        return world;
    }

    private static readonly int[] SlotCa = { 138, 130, 124, 118, 113, 108, 103, 98, 93, 88, 83, 78, 72 };

    private static Team NewClub(int id, ClubSeed c, CompetitionProfile profile, Rng rng)
    {
        var (primary, secondary) = Colours(rng);
        return new Team
        {
            Id = id, City = c.City, Name = c.Name, Abbreviation = c.Abbr, Conference = Conference.East, Division = profile.Short,
            ArenaCapacity = rng.Next(5000, 16000), PrimaryColor = primary, SecondaryColor = secondary, LogoStyle = rng.Next(0, 5),
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
        ca = Math.Clamp(ca, 45, 160);
        int age = ageOverride ?? (int)Math.Round(Math.Clamp(rng.NextGaussian(slot < 9 ? 27 : 23.5, 3.5), 18, 36));
        if (age < profile.MinAge) age = profile.MinAge; // pro at 23+ in the USA (decision #75)
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
        p.TeamId = team.Id;
        p.Contract = NewContract(p, profile, rng.Next(1, 5), today, today.Year);
        return p;
    }

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

    private static Heritage? LocalHeritage(string country) => country switch
    {
        "Spain" => Heritages.All.First(h => h.Key == "spain"),
        "Turkey" or "Israel" => Heritages.All.First(h => h.Key == "turkey"),
        "Australia" => Heritages.All.First(h => h.Key == "australia"),
        "China" => Heritages.All.First(h => h.Key == "china"),
        "Greece" => Heritages.All.First(h => h.Key == "greece"),
        "Serbia" => Heritages.All.First(h => h.Key == "balkan"),
        "Italy" => Heritages.All.First(h => h.Key == "italy"),
        "France" => Heritages.All.First(h => h.Key == "france"),
        "Germany" => Heritages.All.First(h => h.Key == "germany"),
        "Lithuania" => Heritages.All.First(h => h.Key == "easterneurope"),
        _ => null,
    };

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
        foreach (var comp in Playing)
        {
            var participants = comp.Profile.Kind == CompetitionKind.Continental ? ContinentalEntrants(comp) : comp.Clubs.Select(t => t.Id).ToList();
            comp.Season = new CompetitionSeason { Year = year, Participants = participants };
            comp.Season.Fixtures = RoundRobin(participants, comp.Profile, year);
        }
    }

    /// <summary>Clubs entering a continental competition: the top of each domestic league's last table (or the strongest clubs), plus the guest pool.</summary>
    private List<int> ContinentalEntrants(WorldCompetition comp)
    {
        var ids = new List<int>();
        foreach (var (key, n) in comp.Profile.Entrants)
        {
            var source = this[key];
            if (source.Profile.Kind == CompetitionKind.GuestPool) { ids.AddRange(source.Clubs.Take(n).Select(t => t.Id)); continue; }
            var table = source.Season.Fixtures.Any(f => f.Played) ? source.Season.Table().Select(r => r.TeamId).ToList()
                : source.Clubs.OrderByDescending(Strength).Select(t => t.Id).ToList();
            ids.AddRange(table.Take(n));
        }
        return ids;
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
        var day = new DateOnly(year, p.Start.Month, p.Start.Day);
        int idx = 0;
        foreach (var round in rounds)
        {
            while (!p.RoundDays.Contains(day.DayOfWeek)) day = day.AddDays(1);
            foreach (var (h, a) in round)
                fixtures.Add(new Fixture { Index = idx++, Date = p.TwoDayRounds && _rng.Chance(0.5) ? day.AddDays(1) : day, HomeId = h, AwayId = a });
            day = day.AddDays(1);
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
            News.AddRange(College.News); College.News.Clear();
        }
    }

    /// <summary>Play out every competition's season to the end (used before the yearly rollover).</summary>
    public void FinishSeasons(GameSimulator sim) => PlayThrough(DateOnly.MaxValue, sim);

    private void PlayCompetition(WorldCompetition comp, DateOnly until, GameSimulator sim)
    {
        var s = comp.Season;
        while (!s.IsComplete)
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
        var home = _teams[f.HomeId]; var away = _teams[f.AwayId];
        var stats = comp.Season.Stats;
        if (comp.FullSim)
        {
            var r = sim.Simulate(home, away, comp.Profile.Rules, _rng.NextULong(), false, f.Date);
            f.HomePts = r.Home.Points; f.AwayPts = r.Away.Points;
            AddLines(stats, r);
        }
        else
        {
            var (hp, ap) = QuickSim.Play(home, away, f.Date, _rng, stats, comp.Profile.Rules);
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
            st.TeamId = box.Team.Id; st.G++; st.Min += l.Minutes; st.Pts += l.Points; st.Reb += l.Rebounds; st.Ast += l.Assists;
            st.Stl += l.Steals; st.Blk += l.Blocks; st.Fgm += l.FGM; st.Fga += l.FGA; st.Tpm += l.ThreePM; st.Tpa += l.ThreePA;
        }
    }

    private static void AfterResult(WorldCompetition comp, Fixture f)
    {
        if (f.SeriesId is { } sid)
        {
            var series = comp.Season.Series.First(x => x.Id == sid);
            if (f.WinnerId == series.HighId) series.HighWins++; else series.LowWins++;
        }
    }

    /// <summary>Move the competition forward when nothing is left to play: seed the playoffs, add the next game of each series, or crown a champion.</summary>
    private bool Advance(WorldCompetition comp)
    {
        var s = comp.Season;
        var p = comp.Profile;
        if (s.Fixtures.Any(f => !f.Played)) return false;

        if (s.Stage == CompetitionSeason.RegularSeason)
        {
            if (p.Playoffs.Length == 0) { Crown(comp, s.Table()[0].TeamId, s.Table()[1].TeamId); return true; }
            var seeds = s.Table().Take(p.Playoffs[0].Teams).Select(r => r.TeamId).ToList();
            var order = BracketOrder(seeds.Count);
            for (int i = 0; i < order.Count; i += 2)
                s.Series.Add(new SeriesState { Id = s.Series.Count + 1, RoundIndex = 0, Round = p.Playoffs[0].Name, BestOf = p.Playoffs[0].BestOf, HighId = seeds[order[i]], LowId = seeds[order[i + 1]] });
            s.Stage = p.Playoffs[0].Name;
            ScheduleNextGames(comp);
            return true;
        }

        var active = s.Series.Where(x => x.Round == s.Stage).ToList();
        if (active.Any(x => !x.Complete)) { ScheduleNextGames(comp); return true; }

        int ri = active[0].RoundIndex;
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

    /// <summary>Add the next game for every series in the current round that needs one.</summary>
    private void ScheduleNextGames(WorldCompetition comp)
    {
        var s = comp.Season;
        var p = comp.Profile;
        var lastPlayed = s.Fixtures.Where(f => f.Played).Select(f => f.Date).DefaultIfEmpty(new DateOnly(s.Year, p.Start.Month, p.Start.Day)).Max();
        foreach (var series in s.Series.Where(x => x.Round == s.Stage && !x.Complete))
        {
            if (s.Fixtures.Any(f => f.SeriesId == series.Id && !f.Played)) continue;
            int gameNo = series.HighWins + series.LowWins + 1;
            var prev = s.Fixtures.Where(f => f.SeriesId == series.Id).Select(f => f.Date).DefaultIfEmpty(lastPlayed).Max();
            DateOnly date;
            if (p.FinalFour && series.BestOf == 1)
            {
                // Final Four weekend in late May: semi-finals on Friday, the final on Sunday.
                var friday = new DateOnly(s.Year + 1, 5, 20);
                while (friday.DayOfWeek != DayOfWeek.Friday) friday = friday.AddDays(1);
                date = series.RoundIndex == p.Playoffs.Length - 1 ? friday.AddDays(2) : friday;
                if (date <= prev) date = prev.AddDays(2);
            }
            else
            {
                date = prev.AddDays(1);
                while (!p.PlayoffDays.Contains(date.DayOfWeek)) date = date.AddDays(1);
            }
            bool highHome = series.BestOf switch { 3 => gameNo != 2, 5 => gameNo is 1 or 2 or 5, 7 => gameNo is 1 or 2 or 5 or 7, _ => true };
            s.Fixtures.Add(new Fixture
            {
                Index = s.Fixtures.Count, Date = date, Stage = series.Round, SeriesId = series.Id,
                HomeId = highHome ? series.HighId : series.LowId, AwayId = highHome ? series.LowId : series.HighId,
            });
        }
    }

    private void Crown(WorldCompetition comp, int champion, int runnerUp)
    {
        var s = comp.Season;
        s.ChampionId = champion; s.RunnerUpId = runnerUp; s.Stage = CompetitionSeason.Complete;
        var c = _teams[champion]; var r = _teams[runnerUp];
        comp.History.Add(new ChampionRecord(s.Year, champion, runnerUp, c.FullName, r.FullName));
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
            var cgen = new PlayerGenerator(new Rng(_rng.NextULong()), Nabl.NextPlayerId);
            College.Summer(date, freeAgents, cgen, new DevelopmentEngine(_rng), _rng);
            Nabl.NextPlayerId = cgen.NextId;
            News.AddRange(College.News); College.News.Clear();
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
                t => comp.Season.ChampionId == t.Id || Competitions.Any(c => c.Profile.Kind == CompetitionKind.Continental && c.Season.ChampionId == t.Id));
        }
        var dev = new DevelopmentEngine(_rng);
        foreach (var comp in Competitions.Where(c => c.Clubs.Count > 0))
        foreach (var team in comp.Clubs)
        {
            double clubLevel = team.Roster.Count == 0 ? 0 : team.Roster.OrderByDescending(p => p.CurrentAbility).Take(9).Average(p => p.CurrentAbility);
            foreach (var p in team.Roster.ToList())
            {
                double mpg = Mpg(p.Id);
                dev.Develop(p, date, mpg);
                bool expiring = p.Contract == null || --p.Contract.Years <= 0;
                if (p.Contract != null && !expiring) p.Contract.Buyout = BuyoutFor(p, p.Contract.Salary, p.Contract.Years, date);
                var locals = LocalsOf(comp.Profile, ClubOf(comp, team));
                var departure = new Departure(p.CurrentAbility, !locals.Contains(p.Nationality));
                if (dev.ShouldRetire(p, date, mpg) || (expiring && p.Age(date) >= 33 && _rng.Chance(0.5)))
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
                bool testsMarket = p.CurrentAbility >= NablCalibre && p.Age(date) <= 31 && _rng.Chance(0.2);
                bool clubWants = p.CurrentAbility >= clubLevel - 18 || p.Age(date) <= 23;
                if (!testsMarket && clubWants && _rng.Chance(0.95))
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
        var gen = new PlayerGenerator(new Rng(_rng.NextULong()), Nabl.NextPlayerId);
        // Each club replaces the players it lost like for like: an import for an import (within the foreign limit),
        // at about the same level. Clubs take turns, best first, one replacement at a time, so the pool spreads out.
        // The pool is tried first; if nobody there fits, the club finds a new player (sometimes a younger prospect).
        // (Refilling with weak teenagers drained every overseas league by 5-10 CA a decade; balance audit, Nov 9.)
        var clubs = Competitions.Where(c => c.Clubs.Count > 0).SelectMany(c => c.Clubs.Select(t => (comp: c, team: t))).ToList();
        var needs = clubs.ToDictionary(x => x.team.Id, x => new Queue<Departure>(
            (Departed.TryGetValue(x.team.Id, out var d) ? d : new List<Departure>()).OrderByDescending(d => d.Ca)));
        bool any = true;
        while (any)
        {
            any = false;
            foreach (var (comp, team) in clubs.OrderByDescending(x => Strength(x.team)))
            {
                // You build your own squad; the sporting director only makes up the numbers if you leave it short.
                if (team.Roster.Count >= (team.Id == ManagedClubId ? ManagedMinimum : RosterSize)) continue;
                if (team.Id == ManagedClubId) needs[team.Id].Clear();
                var club = ClubOf(comp, team);
                var locals = LocalsOf(comp.Profile, club);
                var need = needs[team.Id].Count > 0 ? needs[team.Id].Dequeue() : new Departure(ExpectedCa(comp.Profile, team, team.Roster.Count), false);
                any = true;
                int imports = team.Roster.Count(p => !locals.Contains(p.Nationality));
                bool importRoom = imports < comp.Profile.ForeignLimit;
                // US clubs look harder for a player in the pool (college graduates fill the pyramid, decision #98).
                int window = comp.Profile.MinAge > 0 ? 12 : 8;
                var pick = freeAgents
                    .Where(p => p.Age(date) <= 33 && p.CurrentAbility >= need.Ca - window && p.CurrentAbility <= need.Ca + window)
                    .Where(p => p.Age(date) >= comp.Profile.MinAge)
                    .Where(p => locals.Contains(p.Nationality) || (importRoom && need.Import))
                    .OrderBy(p => Math.Abs(p.CurrentAbility - need.Ca) + _rng.NextDouble() * 6)
                    .FirstOrDefault();
                if (pick != null && _rng.Chance(comp.Profile.MinAge > 0 ? 0.95 : 0.7))
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
        Nabl.NextPlayerId = gen.NextId;
        // Promotion and relegation through the USA pyramid, before next season's fixtures (decisions #78-79, #91).
        foreach (var n in UsaPyramid.Promote(this, Nabl, new StaffGenerator(new Rng(_rng.NextULong())), date)) News.Add(n);
        College?.StartSeason(newYear, _rng);
        StartSeason(newYear);
    }

    /// <summary>Whole summer in one go (no NABL market in between): used by tests and tools that only need the world to roll on.</summary>
    public void Rollover(int newYear, GameSimulator sim)
    {
        var pool = new List<Player>();
        BeginSummer(newYear, sim, pool);
        EndSummer(newYear, sim, pool);
        foreach (var p in pool) { p.TeamId = null; Nabl.FreeAgents.Add(p); }
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
