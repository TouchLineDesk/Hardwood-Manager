using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.International;

/// <summary>One World Cup game, kept in the saved history (codes, not team objects).</summary>
public sealed record CupGame(string Round, string Home, string Away, int HomePts, int AwayPts, string Day)
{
    public string Winner => HomePts > AwayPts ? Home : Away;
    public string Loser => HomePts > AwayPts ? Away : Home;
}

/// <summary>A player's tournament, kept by name (he may retire before the history is read).</summary>
public sealed record CupPlayer(int PlayerId, string Name, string Code, int Games, int Pts, int Reb, int Ast)
{
    public double Ppg => Games == 0 ? 0 : Math.Round((double)Pts / Games, 1);
    public double Rpg => Games == 0 ? 0 : Math.Round((double)Reb / Games, 1);
    public double Apg => Games == 0 ? 0 : Math.Round((double)Ast / Games, 1);
    /// <summary>The all-star five's measure: points, rebounds and assists a game.</summary>
    public double Impact => Games == 0 ? 0 : (double)(Pts + Reb + Ast) / Games;
}

/// <summary>A national team at the World Cup: its code, name, zone and twelve (player ids, frozen on the first day).</summary>
public sealed record CupTeam(string Code, string Name, string Zone, double Strength, int Rank, List<int> Squad);

/// <summary>One played World Cup (international plan I2, run 209). Saved as an optional field: older saves have none.</summary>
public sealed class WorldCupRecord
{
    public int Year { get; set; }
    /// <summary>I4 (run 211): the tournament's name ("Olympic basketball tournament 2028", "EuroBasket 2029"); empty on a World Cup.</summary>
    public string Name { get; set; } = "";
    /// <summary>I4: "Olympics", "Olympic qualifying" or "Continental"; empty on a World Cup.</summary>
    public string Kind { get; set; } = "";
    /// <summary>I4: a continental championship's zone ("Europe"); empty otherwise.</summary>
    public string Zone { get; set; } = "";
    public string Host { get; set; } = "";
    public string Dates { get; set; } = "";
    public List<CupTeam> Teams { get; set; } = new();
    /// <summary>Groups A–H, four codes each, in draw order.</summary>
    public List<List<string>> Groups { get; set; } = new();
    public List<CupGame> Games { get; set; } = new();
    public string Gold { get; set; } = "";
    public string Silver { get; set; } = "";
    public string Bronze { get; set; } = "";
    public string Fourth { get; set; } = "";
    public List<CupPlayer> TopScorers { get; set; } = new();
    public List<CupPlayer> AllStarFive { get; set; } = new();
    public CupPlayer? Mvp { get; set; }
    /// <summary>I5 (run 213): injuries picked up in its full-engine games (carried to the clubs at the summer's jump).</summary>
    public List<CupInjury> Injuries { get; set; } = new();
    /// <summary>I5: players in their nation's previous senior twelve who were fit and in the pool but not picked (id to code).</summary>
    public Dictionary<int, string> LeftOut { get; set; } = new();

    public string NameOf(string code) => Teams.FirstOrDefault(t => t.Code == code)?.Name ?? code;
    public CupGame? Final => Games.LastOrDefault(g => g.Round == WorldCup.FinalRound);
    /// <summary>"FIBA Basketball World Cup 2027", or the I4 tournament's own name.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string Title => Name != "" ? Name : $"FIBA Basketball World Cup {Year}";
    /// <summary>A short name for lists: "World Cup 2027", "Olympics 2028", "EuroBasket 2029".</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string Short => Kind switch
    {
        "" => $"World Cup {Year}",
        "Olympics" => $"Olympics {Year}",
        U23Cup.Kind => $"U23 Nations Cup {Year}",
        _ => Name.Replace("FIBA ", ""),
    };
}

/// <summary>Every international tournament played in this career (I2: the World Cup; later slices add more).</summary>
public sealed class InternationalHistory
{
    public List<WorldCupRecord> WorldCups { get; set; } = new();
    /// <summary>I3 (run 210): the qualifying windows played, oldest first.</summary>
    public List<WindowRecord> Windows { get; set; } = new();
    /// <summary>I3: each qualifying cycle's groups.</summary>
    public List<QualifyingCycle> Cycles { get; set; } = new();
    /// <summary>I3: FIBA ranking points by code. Empty until the first window or World Cup is played (the page shows the seed).</summary>
    public Dictionary<string, double> Ranking { get; set; } = new();
    /// <summary>I3: the world rank by code before the latest window or tournament, for the page's movement arrows.</summary>
    public Dictionary<string, int> LastRanking { get; set; } = new();
    /// <summary>I4 (run 211): the Olympic tournaments played.</summary>
    public List<WorldCupRecord> Olympics { get; set; } = new();
    /// <summary>I4: the Olympic qualifying tournaments (quick results).</summary>
    public List<WorldCupRecord> OlympicQualifiers { get; set; } = new();
    /// <summary>I4: the continental championships (EuroBasket, the AmeriCup, AfroBasket, the Asia Cup).</summary>
    public List<WorldCupRecord> Continentals { get; set; } = new();
    /// <summary>I5 (run 213): the U23 Nations Cups played (a game event; FIBA has none).</summary>
    public List<WorldCupRecord> U23Cups { get; set; } = new();
    /// <summary>I6a (run 214): your national-team job and the federations' offers.</summary>
    public NationalCoaching Coaching { get; set; } = new();

    /// <summary>I4: the World Cups and Olympics played, oldest first: the medal table's tournaments.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public IEnumerable<WorldCupRecord> Majors => WorldCups.Concat(Olympics).OrderBy(r => r.Year);

    public bool IsEmpty => WorldCups.Count == 0 && Windows.Count == 0 && Ranking.Count == 0 && Olympics.Count == 0 && OlympicQualifiers.Count == 0
                           && Continentals.Count == 0
                           && U23Cups.Count == 0
                           && (Coaching?.IsEmpty ?? true);
}

/// <summary>
/// International plan I2 (run 209): the FIBA Basketball World Cup, played. 32 teams by zone quota (Europe 12, Americas 7,
/// Africa 5, Asia-Pacific 8 with the host), by I3's qualifiers (run 210) and the FIBA ranking; eight groups of four; a second round of four groups
/// (each team carries its group games against the other qualifier and plays the two from the paired group); quarter-finals,
/// semi-finals, a bronze game and the final. Every game is the full engine on FIBA rules at a neutral site (#109). Squads are the
/// federation's twelve on the first day, from players fit that day. It draws from its own stream (the session seed and the year),
/// never the shared ones, and writes nothing to players or clubs (I5, run 213: its injuries are kept in the record and carried by the session, #110).
/// </summary>
public static class WorldCup
{
    public const string FinalRound = "Final", BronzeRound = "Third place", SemiRound = "Semi-final", QuarterRound = "Quarter-final";
    public const string SecondRound = "Second round";

    /// <summary>The zone quotas (32 in all). A zone without enough teams passes its places to the best of the rest.</summary>
    public static readonly IReadOnlyDictionary<FibaZone, int> Quota = new Dictionary<FibaZone, int>
    {
        [FibaZone.Europe] = 12, [FibaZone.Americas] = 7, [FibaZone.Africa] = 5, [FibaZone.AsiaPacific] = 8,
    };

    public const int Teams = 32;

    /// <summary>The World Cup event in a year, or null when there is none.</summary>
    public static FibaEvent? EventIn(int year) =>
        FibaCalendar.IsWorldCupYear(year) ? FibaCalendar.InYear(year).First(e => e.Kind == FibaEventKind.WorldCup) : null;

    /// <summary>The world's players as national-team entries, fit on <paramref name="day"/>.</summary>
    /// <param name="window">K42 (run 216): a qualifying window. NBA clubs don't release their players for the windows
    /// (they play on through November and February), so NBA rosters are left out; summer events take everyone.</param>
    public static List<(Player P, Team? Club, bool College)> Entries(GameSession s, DateOnly day, bool window = false)
    {
        var entries = new List<(Player, Team?, bool)>();
        var seen = new HashSet<int>();
        void Add(Player p, Team? t, bool college) { if (p.IsAvailable(day) && seen.Add(p.Id)) entries.Add((p, t, college)); }
        if (!window) foreach (var t in s.League.Teams) foreach (var p in t.Roster) Add(p, t, false);
        else foreach (var t in s.League.Teams) foreach (var p in t.Roster) seen.Add(p.Id); // held by their NBA club
        foreach (var t in s.World.Clubs) foreach (var p in t.Roster) Add(p, t, false);
        if (s.World.College?.Programs is { } programs) foreach (var t in programs) foreach (var p in t.Roster) Add(p, t, true);
        foreach (var p in s.League.FreeAgents) Add(p, null, false);
        return entries;
    }

    /// <summary>The 32 teams: the host if it has a team, then each zone's places up to its quota (I3, run 210: in qualifying
    /// order when the zone's qualifiers were played, else by rank), then the best of the rest by rank.</summary>
    public static List<NationalTeam> Field(IReadOnlyList<NationalTeam> ranked, string host, IReadOnlyDictionary<FibaZone, List<string>>? qualifying = null)
    {
        var eligible = ranked.Where(t => t.Rank > 0).OrderBy(t => t.Rank).ToList();
        var field = new List<NationalTeam>();
        if (eligible.FirstOrDefault(t => t.Name == host) is { } h) field.Add(h);
        foreach (var (zone, quota) in Quota)
        {
            var order = qualifying?.GetValueOrDefault(zone) ?? new List<string>();
            var inZone = eligible.Where(t => t.Nation.Zone == zone && !field.Contains(t))
                .OrderBy(t => order.IndexOf(t.Nation.Code) is var i && i >= 0 ? i : int.MaxValue).ThenBy(t => t.Rank).ToList();
            foreach (var t in inZone.Take(quota - field.Count(f => f.Nation.Zone == zone)))
                field.Add(t);
        }
        foreach (var t in eligible.Where(t => !field.Contains(t)))
        {
            if (field.Count >= Teams) break;
            field.Add(t);
        }
        return field.OrderBy(t => t.Rank).Take(Teams).ToList();
    }

    /// <summary>The draw: four pots of eight by rank, each group one team from each pot; no two teams from one zone in a group
    /// except Europe (at most two), as in FIBA's draw, when a shuffle allows it.</summary>
    public static List<List<NationalTeam>> Draw(List<NationalTeam> field, Rng rng)
    {
        int groups = field.Count / 4;
        var best = (List<List<NationalTeam>>?)null;
        int bestClash = int.MaxValue;
        for (int attempt = 0; attempt < 200 && bestClash > 0; attempt++)
        {
            var g = Enumerable.Range(0, groups).Select(_ => new List<NationalTeam>()).ToList();
            for (int pot = 0; pot < 4; pot++)
            {
                var p = field.Skip(pot * groups).Take(groups).ToList();
                for (int i = p.Count - 1; i > 0; i--) { int j = rng.Next(0, i + 1); (p[i], p[j]) = (p[j], p[i]); }
                for (int i = 0; i < groups; i++) g[i].Add(p[i]);
            }
            int clash = g.Sum(Clashes);
            if (clash < bestClash) { bestClash = clash; best = g; }
        }
        return best!;
    }

    private static int Clashes(List<NationalTeam> group) =>
        group.GroupBy(t => t.Nation.Zone).Sum(z => Math.Max(0, z.Count() - (z.Key == FibaZone.Europe ? 2 : 1)));

    /// <summary>Play the World Cup of <paramref name="year"/> with the session's world as it stands. Pure towards the session: its
    /// streams, players and clubs are untouched (the engine only reads them; no injuries are rolled).</summary>
    public static WorldCupRecord? Play(GameSession s, int year, GameSimulator? sim = null)
    {
        if (EventIn(year) is not { } ev) return null;
        var built = NationalTeams.For(s, ev.Start);
        var points = FibaRanking.Current(s.World.International, built);
        var ranked = FibaRanking.Apply(built, points); // I3 (run 210): ranked by results, the field by the qualifiers
        var field = Field(ranked, ev.Where, Qualifying.ZoneOrder(s.World.International, Qualifying.WorldCupTarget(year), points));
        if (field.Count < Teams) return null; // review (run 209): the bracket needs the full 32; a smaller world has no World Cup
        var rng = new Rng(s.Seed ^ 0xF1BA_0C0B_2027_0001UL ^ ((ulong)(uint)year * 0x9E3779B97F4A7C15UL));
        return Play(field, ev, rng, sim ?? new GameSimulator());
    }

    /// <summary>Play a field (best first) through the whole tournament.</summary>
    public static WorldCupRecord Play(List<NationalTeam> field, FibaEvent ev, Rng rng, GameSimulator sim)
    {
        var rec = new WorldCupRecord { Year = ev.Start.Year, Host = ev.Where, Dates = ev.Dates };
        var teams = new Dictionary<string, Team>();
        int id = -900_000;
        foreach (var n in field)
        {
            rec.Teams.Add(new CupTeam(n.Nation.Code, n.Name, NationalTeams.ZoneName(n.Nation.Zone), n.Strength, n.Rank, n.Squad.Select(x => x.Player.Id).ToList()));
            var t = new Team { Id = id--, City = n.Name, Name = "", Abbreviation = n.Nation.Code, Division = "FIBA" };
            foreach (var x in n.Squad) t.Roster.Add(x.Player);
            teams[n.Nation.Code] = t;
        }
        var rules = Rules.FibaNational; // H219 (run 212): national-team scoring
        var lines = new Dictionary<int, (string Code, string Name, int G, int Pts, int Reb, int Ast)>();
        var apps = new List<Callups.Appearance>();

        CupGame Game(string round, string home, string away, DateOnly day)
        {
            var r = sim.Simulate(teams[home], teams[away], rules, rng.NextULong());
            foreach (var (box, code) in new[] { (r.Home, home), (r.Away, away) })
                foreach (var l in box.Lines.Where(l => l.Seconds > 0))
                {
                    apps.Add(new Callups.Appearance(l.Player, code, day, l.Minutes));
                    var c = lines.TryGetValue(l.Player.Id, out var was) ? was : (Code: code, Name: l.Player.Name, G: 0, Pts: 0, Reb: 0, Ast: 0);
                    lines[l.Player.Id] = (code, l.Player.Name, c.G + 1, c.Pts + l.Points, c.Reb + l.Rebounds, c.Ast + l.Assists);
                }
            var g = new CupGame(round, home, away, r.Home.Points, r.Away.Points, day.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture));
            rec.Games.Add(g);
            return g;
        }

        // Groups A–H: three days, two games per group a day.
        var draw = Draw(field, rng);
        rec.Groups = draw.Select(g => g.Select(t => t.Nation.Code).ToList()).ToList();
        var day = ev.Start;
        int[][] pairs = { new[] { 0, 3, 1, 2 }, new[] { 0, 2, 3, 1 }, new[] { 0, 1, 2, 3 } };
        for (int d = 0; d < 3; d++, day = day.AddDays(2))
            for (int gi = 0; gi < rec.Groups.Count; gi++)
            {
                var g = rec.Groups[gi]; var p = pairs[d];
                Game(GroupName(gi), g[p[0]], g[p[1]], day);
                Game(GroupName(gi), g[p[2]], g[p[3]], day);
            }

        // The second round: groups paired (A+B, C+D, …); the top two of each carry their game against each other.
        var secondGroups = new List<List<string>>();
        day = day.AddDays(1);
        for (int gi = 0; gi + 1 < rec.Groups.Count; gi += 2)
        {
            var a = Table(rec, rec.Groups[gi], g => g.Round == GroupName(gi)).Take(2).ToList();
            var b = Table(rec, rec.Groups[gi + 1], g => g.Round == GroupName(gi + 1)).Take(2).ToList();
            Game(SecondRound, a[0], b[1], day); Game(SecondRound, b[0], a[1], day);
            Game(SecondRound, a[0], b[0], day.AddDays(2)); Game(SecondRound, a[1], b[1], day.AddDays(2));
            secondGroups.Add(new List<string> { a[0], a[1], b[0], b[1] });
        }
        day = day.AddDays(4);

        // Quarter-finals: 1st of one second-round group v 2nd of its pair.
        var qualified = secondGroups.Select(g => Table(rec, g, x => x.Round == SecondRound || x.Round.StartsWith("Group ")).Take(2).ToList()).ToList();
        var quarters = new List<string>();
        for (int i = 0; i + 1 < qualified.Count; i += 2)
        {
            quarters.Add(Game(QuarterRound, qualified[i][0], qualified[i + 1][1], day).Winner);
            quarters.Add(Game(QuarterRound, qualified[i + 1][0], qualified[i][1], day.AddDays(1)).Winner);
        }
        day = day.AddDays(3);
        var s1 = Game(SemiRound, quarters[0], quarters[1], day);
        var s2 = Game(SemiRound, quarters[2], quarters[3], day);
        var bronze = Game(BronzeRound, s1.Loser, s2.Loser, ev.End.AddDays(-1));
        var final = Game(FinalRound, s1.Winner, s2.Winner, ev.End);
        rec.Gold = final.Winner; rec.Silver = final.Loser; rec.Bronze = bronze.Winner; rec.Fourth = bronze.Loser;

        var all = lines.Select(kv => new CupPlayer(kv.Key, kv.Value.Name, kv.Value.Code, kv.Value.G, kv.Value.Pts, kv.Value.Reb, kv.Value.Ast)).ToList();
        rec.TopScorers = all.Where(p => p.Games >= 3).OrderByDescending(p => p.Ppg).ThenByDescending(p => p.Pts).ThenBy(p => p.PlayerId).Take(10).ToList();
        // The all-star five: the best by points, rebounds and assists a game among the last eight's players (5+ games).
        var lastEight = rec.Games.Where(g => g.Round == QuarterRound).SelectMany(g => new[] { g.Home, g.Away }).ToHashSet();
        rec.AllStarFive = all.Where(p => p.Games >= 5 && lastEight.Contains(p.Code))
            .OrderByDescending(p => p.Impact).ThenBy(p => p.PlayerId).Take(5).ToList();
        rec.Mvp = rec.AllStarFive.FirstOrDefault(p => p.Code == rec.Gold) ?? all.Where(p => p.Code == rec.Gold).OrderByDescending(p => p.Impact).FirstOrDefault();
        rec.Injuries = Callups.RollInjuries(rec, apps); // I5 (run 213): after the games, from its own seed
        return rec;
    }

    public static string GroupName(int i) => $"Group {(char)('A' + i)}";

    /// <summary>A group's table: wins, then the games between the tied teams' point difference, then point difference, then points.</summary>
    public static List<string> Table(WorldCupRecord rec, IReadOnlyList<string> codes, Func<CupGame, bool> counts)
    {
        var games = rec.Games.Where(g => counts(g) && codes.Contains(g.Home) && codes.Contains(g.Away)).ToList();
        int Wins(string c) => games.Count(g => g.Winner == c);
        int Diff(string c, IEnumerable<CupGame> gs) => gs.Sum(g => g.Home == c ? g.HomePts - g.AwayPts : g.Away == c ? g.AwayPts - g.HomePts : 0);
        int For(string c) => games.Sum(g => g.Home == c ? g.HomePts : g.Away == c ? g.AwayPts : 0);
        return codes.OrderByDescending(Wins)
            .ThenByDescending(c => Diff(c, games.Where(g => Wins(g.Home) == Wins(c) && Wins(g.Away) == Wins(c))))
            .ThenByDescending(c => Diff(c, games)).ThenByDescending(For).ThenBy(c => c, StringComparer.Ordinal).ToList();
    }

    /// <summary>"W-L" for a team in the games that count.</summary>
    public static (int W, int L, int Diff) Record(WorldCupRecord rec, string code, Func<CupGame, bool> counts)
    {
        var gs = rec.Games.Where(g => counts(g) && (g.Home == code || g.Away == code)).ToList();
        int w = gs.Count(g => g.Winner == code);
        return (w, gs.Count - w, gs.Sum(g => g.Home == code ? g.HomePts - g.AwayPts : g.AwayPts - g.HomePts));
    }
}
