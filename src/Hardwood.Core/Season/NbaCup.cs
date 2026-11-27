using System.Text.Json.Serialization;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>A cup group game fixed to its night before the schedule is dated (run 223).</summary>
public sealed record CupFixture(DateOnly Date, Team Home, Team Away);

/// <summary>What the schedule builder needs from the cup: the group games on their nights and the knockout nights kept free.</summary>
public sealed record CupPlan(IReadOnlyList<CupFixture> Games, IReadOnlyCollection<DateOnly> Blocked);

/// <summary>One of the groups (six of five in a 30-club league, eight of four in the game's 32).</summary>
public sealed class CupGroup
{
    public string Name { get; set; } = "";
    public List<int> TeamIds { get; set; } = new();
}

/// <summary>A knockout game: quarter-finals at the better seed's arena, the semi-finals and final in Las Vegas.</summary>
public sealed class CupTie
{
    public string Round { get; set; } = "";
    public DateOnly Date { get; set; }
    public int HomeId { get; set; }
    public int AwayId { get; set; }
    public bool Neutral { get; set; }
    public int? HomePoints { get; set; }
    public int? AwayPoints { get; set; }
    public int Overtimes { get; set; }
    /// <summary>The winner's top scorer in the final: the cup's MVP.</summary>
    public string? TopScorer { get; set; }
    /// <summary>The game itself, while the session that played it runs (not saved: the score is).</summary>
    [JsonIgnore] public Engine.GameResult? Result { get; set; }
    [JsonIgnore] public bool Played => HomePoints != null;
    [JsonIgnore] public int? WinnerId => Played ? (HomePoints > AwayPoints ? HomeId : AwayId) : null;
    [JsonIgnore] public int? LoserId => Played ? (HomePoints > AwayPoints ? AwayId : HomeId) : null;
}

/// <summary>A team's group line.</summary>
public sealed record CupRow(Team Team, int Wins, int Losses, int PointDiff);

/// <summary>This season's cup.</summary>
public sealed class CupSeason
{
    public int Season { get; set; }
    public List<CupGroup> Groups { get; set; } = new();
    /// <summary>The schedule indices of the group games (they count in the regular season, as in the real NBA Cup).</summary>
    public List<int> GroupGames { get; set; } = new();
    public List<CupTie> Ties { get; set; } = new();
    public bool GroupsPosted { get; set; }
    public int? WinnerId { get; set; }
}

/// <summary>A past final.</summary>
public sealed record CupWinner(int Season, int WinnerId, int RunnerUpId, string Score, string? Mvp);

/// <summary>The league's in-season cup across seasons. Saved as JSON in meta (`nba_cup`); older saves have none.</summary>
public sealed class CupBook
{
    public CupSeason? Current { get; set; }
    public List<CupWinner> Winners { get; set; } = new();
}

/// <summary>
/// The in-season cup (run 223, Danny's `danny-world-leagues-nov26`), on the real NBA Cup's format since 2023: six groups of
/// five, drawn within each conference from pots by last season's record (here, without a draw: snake order by last season's
/// win share, or by strength in a new world); four group games each on five cup nights in November (Fridays and Tuesdays),
/// and they count in the 82; the three group winners and the best second-placed team in each conference go to the
/// quarter-finals, hosted by the better seed, the second Tuesday and Wednesday of December; the semi-finals (Saturday) and
/// the final (the Tuesday after) at a neutral Las Vegas arena. The knockout games don't count in the standings or the season's
/// statistics (the real QFs and SFs do; here they are extra games, so every team still plays 82).
/// The game's league has 32 clubs (#355), so it plays the same cup with four groups of four in each conference: three group
/// games each on three cup nights, and the four group winners go through. A league with two conferences of 15 plays the real
/// shape. No random numbers are drawn for the draw or the dates.
/// </summary>
public static class NbaCup
{
    public const string QuarterFinal = "Quarter-final", SemiFinal = "Semi-final", Final = "Final";
    public const string Venue = "Las Vegas";

    public static string Name => $"{Names.Brand.TopShort} Cup";

    /// <summary>The league has the cup's shape: two conferences of fifteen (groups of five) or of sixteen (groups of four).</summary>
    public static bool Fits(League league)
    {
        int e = league.Teams.Count(t => t.Conference == Conference.East), w = league.Teams.Count(t => t.Conference == Conference.West);
        return e == w && e is 15 or 16;
    }

    /// <summary>Clubs in a group: five, or four in a league of 32.</summary>
    public static int GroupSize(League league) => league.Teams.Count(t => t.Conference == Conference.East) == 16 ? 4 : 5;
    /// <summary>Group games a club plays: four in groups of five, three in groups of four.</summary>
    public static int GroupGamesEach(int groupSize) => groupSize - 1;

    /// <summary>The five group nights: the first Friday on or after Nov 7, then Tuesday, Friday, Tuesday, Friday.</summary>
    public static List<DateOnly> GroupNights(int season)
    {
        var d = new DateOnly(season, 11, 7);
        while (d.DayOfWeek != DayOfWeek.Friday) d = d.AddDays(1);
        return new() { d, d.AddDays(4), d.AddDays(7), d.AddDays(11), d.AddDays(14) };
    }

    /// <summary>The knockout nights: two quarter-final nights (the first Tuesday on or after Dec 8, and the Wednesday), the
    /// semi-finals that Saturday, the final the Tuesday after.</summary>
    public static (DateOnly qf1, DateOnly qf2, DateOnly sf, DateOnly final) KnockoutNights(int season)
    {
        var d = new DateOnly(season, 12, 8);
        while (d.DayOfWeek != DayOfWeek.Tuesday) d = d.AddDays(1);
        return (d, d.AddDays(1), d.AddDays(4), d.AddDays(7));
    }

    /// <summary>The groups: each conference's fifteen ranked by <paramref name="rank"/> (best first) go into groups A–C in
    /// snake order (1-2-3, 3-2-1, …), so each group has one team from each pot of three.</summary>
    public static List<CupGroup> Draw(League league, Func<Team, double> rank)
    {
        var groups = new List<CupGroup>();
        foreach (var conf in new[] { Conference.East, Conference.West })
        {
            var ranked = league.Teams.Where(t => t.Conference == conf).OrderByDescending(rank).ThenBy(t => t.Id).ToList();
            int n = ranked.Count / GroupSize(league);
            var these = Enumerable.Range(0, n).Select(i => new CupGroup { Name = $"{conf} {(char)('A' + i)}" }).ToList();
            for (int i = 0; i < ranked.Count; i++)
            {
                int pot = i / n, slot = i % n;
                these[pot % 2 == 0 ? slot : n - 1 - slot].TeamIds.Add(ranked[i].Id);
            }
            groups.AddRange(these);
        }
        return groups;
    }

    /// <summary>The group games on their nights. Five: a round robin with one team resting each night; each team hosts the
    /// two that follow it in the group's order and visits the two before it. Four: three nights, everyone playing.</summary>
    public static CupPlan Plan(League league, int season, List<CupGroup> groups)
    {
        var nights = GroupNights(season);
        var games = new List<CupFixture>();
        foreach (var g in groups)
        {
            var t = g.TeamIds.Select(league.TeamById).ToList();
            if (t.Count == 4)
            {
                // (host, guest) by night: everyone hosts once or twice.
                var rounds = new[] { new[] { (0, 1), (2, 3) }, new[] { (2, 0), (3, 1) }, new[] { (0, 3), (1, 2) } };
                for (int r = 0; r < 3; r++)
                    foreach (var (h, a) in rounds[r]) games.Add(new CupFixture(nights[r], t[h], t[a]));
                continue;
            }
            for (int r = 0; r < 5; r++)
            for (int k = 1; k <= 2; k++)
            {
                int a = (r + k) % 5, b = (r - k + 5) % 5;
                // a hosts b when b follows a by one or two places.
                bool aHosts = (b - a + 5) % 5 is 1 or 2;
                games.Add(new CupFixture(nights[r], aHosts ? t[a] : t[b], aHosts ? t[b] : t[a]));
            }
        }
        var (q1, q2, sf, f) = KnockoutNights(season);
        return new CupPlan(games, new[] { q1, q2, sf, f });
    }

    /// <summary>The group games in the schedule: on a group night, between two of the same group.</summary>
    public static List<int> GroupGameIndices(IEnumerable<ScheduledGame> schedule, int season, List<CupGroup> groups)
    {
        var nights = GroupNights(season).Take(groups.Count > 0 && groups[0].TeamIds.Count == 4 ? 3 : 5).ToHashSet();
        var groupOf = groups.SelectMany(g => g.TeamIds.Select(id => (id, g.Name))).ToDictionary(x => x.id, x => x.Name);
        return schedule.Where(s => nights.Contains(s.Date) && groupOf.TryGetValue(s.Home.Id, out var gh) && groupOf.TryGetValue(s.Away.Id, out var ga) && gh == ga)
            .Select(s => s.Index).ToList();
    }

    /// <summary>A group's table from the played group games: wins, then the head-to-head among the tied, then point
    /// difference, then the team's id.</summary>
    public static List<CupRow> Table(League league, CupSeason cup, CupGroup group, IReadOnlyList<ScheduledGame> schedule, IReadOnlyDictionary<int, Engine.GameResult> results)
    {
        var w = group.TeamIds.ToDictionary(id => id, _ => 0);
        var l = group.TeamIds.ToDictionary(id => id, _ => 0);
        var pd = group.TeamIds.ToDictionary(id => id, _ => 0);
        var beat = new HashSet<(int, int)>();
        var indices = cup.GroupGames.ToHashSet();
        foreach (var g in schedule)
        {
            if (!indices.Contains(g.Index) || !w.ContainsKey(g.Home.Id) || !results.TryGetValue(g.Index, out var r)) continue;
            int hp = r.Home.Points, ap = r.Away.Points;
            int win = hp > ap ? g.Home.Id : g.Away.Id, lose = hp > ap ? g.Away.Id : g.Home.Id;
            w[win]++; l[lose]++;
            pd[g.Home.Id] += hp - ap; pd[g.Away.Id] += ap - hp;
            beat.Add((win, lose));
        }
        return group.TeamIds
            .OrderByDescending(id => w[id])
            .ThenByDescending(id => group.TeamIds.Count(o => o != id && w[o] == w[id] && beat.Contains((id, o))))
            .ThenByDescending(id => pd[id]).ThenBy(id => id)
            .Select(id => new CupRow(league.TeamById(id), w[id], l[id], pd[id])).ToList();
    }

    /// <summary>The eight: per conference, the group winners and (with three groups) the best second-placed team, seeded 1–4
    /// by group record (wins, then point difference); 1 hosts 4 and 2 hosts 3 (East on the first night, West on the second).</summary>
    public static List<CupTie> QuarterFinals(League league, CupSeason cup, IReadOnlyList<ScheduledGame> schedule, IReadOnlyDictionary<int, Engine.GameResult> results)
    {
        var (q1, q2, _, _) = KnockoutNights(cup.Season);
        var ties = new List<CupTie>();
        foreach (var (conf, night) in new[] { (Conference.East, q1), (Conference.West, q2) })
        {
            var tables = cup.Groups.Where(g => g.Name.StartsWith(conf.ToString())).Select(g => Table(league, cup, g, schedule, results)).ToList();
            static IOrderedEnumerable<CupRow> Best(IEnumerable<CupRow> rows) => rows.OrderByDescending(r => r.Wins).ThenByDescending(r => r.PointDiff).ThenBy(r => r.Team.Id);
            var seeds = Best(tables.Select(t => t[0])).ToList();
            seeds.AddRange(Best(tables.Select(t => t[1])).Take(4 - seeds.Count));
            ties.Add(new CupTie { Round = QuarterFinal, Date = night, HomeId = seeds[0].Team.Id, AwayId = seeds[3].Team.Id });
            ties.Add(new CupTie { Round = QuarterFinal, Date = night, HomeId = seeds[1].Team.Id, AwayId = seeds[2].Team.Id });
        }
        return ties;
    }

    /// <summary>The next round once the last one is played (semi-finals: each conference's two winners, the 1-v-4 winner
    /// listed first; the final: East v West), or nothing.</summary>
    public static List<CupTie> NextRound(CupSeason cup)
    {
        var (_, _, sf, f) = KnockoutNights(cup.Season);
        var qf = cup.Ties.Where(t => t.Round == QuarterFinal).ToList();
        var semis = cup.Ties.Where(t => t.Round == SemiFinal).ToList();
        if (qf.Count == 4 && qf.All(t => t.Played) && semis.Count == 0)
            return new()
            {
                new CupTie { Round = SemiFinal, Date = sf, HomeId = qf[0].WinnerId!.Value, AwayId = qf[1].WinnerId!.Value, Neutral = true },
                new CupTie { Round = SemiFinal, Date = sf, HomeId = qf[2].WinnerId!.Value, AwayId = qf[3].WinnerId!.Value, Neutral = true },
            };
        if (semis.Count == 2 && semis.All(t => t.Played) && !cup.Ties.Any(t => t.Round == Final))
            return new() { new CupTie { Round = Final, Date = f, HomeId = semis[0].WinnerId!.Value, AwayId = semis[1].WinnerId!.Value, Neutral = true } };
        return new();
    }

    /// <summary>"Boston 112-104 New York" for a played tie.</summary>
    public static string Line(League league, CupTie t)
    {
        var h = league.TeamById(t.HomeId); var a = league.TeamById(t.AwayId);
        if (!t.Played) return $"{h.FullName} v {a.FullName} ({t.Date:ddd MMM d}{(t.Neutral ? $", {Venue}" : $", at {h.City}")})";
        bool hw = t.HomePoints > t.AwayPoints;
        var (wn, ln) = hw ? (h, a) : (a, h);
        int wp = Math.Max(t.HomePoints!.Value, t.AwayPoints!.Value), lp = Math.Min(t.HomePoints!.Value, t.AwayPoints!.Value);
        return $"{wn.FullName} {wp}-{lp} {ln.FullName}{(t.Overtimes > 0 ? $" ({(t.Overtimes == 1 ? "" : t.Overtimes)}OT)" : "")}";
    }
}
