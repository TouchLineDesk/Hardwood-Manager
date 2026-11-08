using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

public sealed record VoteLine(int PlayerId, string Name, string Team, double Points, double Share, string StatLine);

/// <summary>End-of-season awards for one season. Player ids only; names are resolved from the league when shown.</summary>
public sealed class SeasonAwards
{
    public int Season { get; init; }
    public List<VoteLine> MvpVoting { get; init; } = new();
    public List<VoteLine> RoyVoting { get; init; } = new();
    public List<VoteLine> DpoyVoting { get; init; } = new();
    public List<VoteLine> SixthManVoting { get; init; } = new();
    public int? CoachOfYearTeamId { get; init; }
    public string CoachOfYearNote { get; init; } = "";
    /// <summary>Three teams of five player ids.</summary>
    public List<List<int>> AllLeague { get; init; } = new();
    public List<int> AllStarsEast { get; init; } = new();
    public List<int> AllStarsWest { get; init; } = new();
    public string AllStarGame { get; init; } = "";
    public int? AllStarMvpId { get; init; }

    public int? Mvp => MvpVoting.FirstOrDefault()?.PlayerId;
    public int? Roy => RoyVoting.FirstOrDefault()?.PlayerId;
    public int? Dpoy => DpoyVoting.FirstOrDefault()?.PlayerId;
    public int? SixthMan => SixthManVoting.FirstOrDefault()?.PlayerId;
}

/// <summary>Career accumulation used for the Hall of Fame and player pages.</summary>
public sealed class CareerTotals
{
    public int PlayerId { get; init; }
    public int Seasons, Games, Points, Rebounds, Assists, Steals, Blocks;
    public double Minutes;
    public int Mvps, AllLeague, AllStars, Titles, Dpoys, Roys, SixthMen, FinalsMvps;
    public Dictionary<int, int> GamesByTeam { get; set; } = new();

    public double Ppg => Games == 0 ? 0 : (double)Points / Games;
    public double Rpg => Games == 0 ? 0 : (double)Rebounds / Games;
    public double Apg => Games == 0 ? 0 : (double)Assists / Games;

    /// <summary>Hall of Fame score: longevity, production, hardware, rings.</summary>
    public double HallScore =>
        Points / 1000.0 * 1.0 + Rebounds / 1000.0 * 0.6 + Assists / 1000.0 * 0.8
        + Mvps * 12 + AllLeague * 4 + AllStars * 2.5 + Titles * 5 + Dpoys * 4 + FinalsMvps * 6 + Roys * 1.5 + SixthMen * 1.5
        + Math.Max(0, Seasons - 8) * 1.0;
}

public sealed record HallOfFamer(int PlayerId, int Year, double Score);

/// <summary>Voting-model awards. Each voter sees the same facts through slightly different eyes; the noise produces realistic vote splits.</summary>
public sealed class AwardsEngine
{
    private readonly League _league;
    private readonly Standings _standings;
    private readonly Dictionary<int, PlayerSeasonStats> _stats;
    private readonly Rng _rng;
    private readonly Dictionary<int, Player> _players;

    public AwardsEngine(League league, Standings standings, Dictionary<int, PlayerSeasonStats> stats, Rng rng)
    {
        _league = league; _standings = standings; _stats = stats; _rng = rng;
        _players = league.AllActivePlayers.Concat(league.FreeAgents).ToDictionary(p => p.Id);
    }

    private double TeamWinPct(int teamId) => _standings[_league.TeamById(teamId)].WinPct;
    private int TeamWins(int teamId) => _standings[_league.TeamById(teamId)].Wins;

    private static double TrueShooting(PlayerSeasonStats s) =>
        s.FGA + 0.44 * s.FTA <= 0 ? 0 : s.Points / (2.0 * (s.FGA + 0.44 * s.FTA));

    /// <summary>Production per game adjusted for efficiency.</summary>
    private static double Production(PlayerSeasonStats s)
    {
        if (s.Games == 0) return 0;
        double per = (s.Points + 1.2 * (s.OReb + s.DReb) + 1.5 * s.Assists + 1.8 * s.Steals + 1.8 * s.Blocks - 1.2 * s.Turnovers) / (double)s.Games;
        double eff = Math.Clamp((TrueShooting(s) - 0.56) * 2.0, -0.2, 0.2); // ±20% for efficiency
        return per * (1 + eff);
    }

    private double MvpScore(PlayerSeasonStats s)
    {
        double availability = Math.Min(1.0, s.Games / 65.0); // voters punish absence
        double teamSuccess = (TeamWins(s.TeamId) - 41) * 0.45;
        double impact = s.PlusMinus / (double)Math.Max(1, s.Games) * 0.6;
        return (Production(s) * 1.0 + teamSuccess + impact) * availability;
    }

    private double DefenseScore(PlayerSeasonStats s)
    {
        var p = _players[s.PlayerId];
        double rating = (p.Attributes[Attr.PerimeterDefense] + p.Attributes[Attr.InteriorDefense] + p.Attributes[Attr.HelpDefense]) / 3.0;
        double box = (s.Steals * 2.0 + s.Blocks * 2.5 + s.DReb * 0.35) / Math.Max(1, s.Games);
        double teamDef = _standings.All().OrderBy(r => r.PointsAgainst / Math.Max(1.0, r.Games)).ToList().FindIndex(r => r.Team.Id == s.TeamId);
        double teamBonus = (30 - teamDef) * 0.25;
        double availability = Math.Min(1.0, s.Games / 60.0);
        return (rating * 1.6 + box + teamBonus) * availability * Math.Min(1.0, s.Mpg / 28.0);
    }

    private List<VoteLine> Vote(IEnumerable<PlayerSeasonStats> field, Func<PlayerSeasonStats, double> score, int voters = 100, double noise = 0.08)
    {
        var cands = field.Select(s => (s, score: score(s))).Where(x => x.score > 0).OrderByDescending(x => x.score).Take(10).ToList();
        if (cands.Count == 0) return new List<VoteLine>();
        int[] ballot = { 10, 7, 5, 3, 1 };
        var points = cands.ToDictionary(x => x.s.PlayerId, _ => 0.0);
        for (int v = 0; v < voters; v++)
        {
            var seen = cands.Select(x => (stats: x.s, noisy: x.score * (1 + _rng.NextGaussian(0, noise)))).OrderByDescending(x => x.noisy).Take(5).ToList();
            for (int i = 0; i < seen.Count; i++) points[seen[i].stats.PlayerId] += ballot[i];
        }
        double total = points.Values.Sum();
        return cands.OrderByDescending(x => points[x.s.PlayerId]).Select(x =>
        {
            var p = _players[x.s.PlayerId];
            return new VoteLine(p.Id, p.Name, _league.TeamById(x.s.TeamId).Abbreviation, points[p.Id], points[p.Id] / total,
                $"{x.s.Ppg:F1} pts, {x.s.Rpg:F1} reb, {x.s.Apg:F1} ast, {TeamWins(x.s.TeamId)} wins");
        }).Where(v => v.Points > 0).ToList();
    }

    public SeasonAwards EndOfSeason(int season, SeasonAwards? midseason)
    {
        var qualified = _stats.Values.Where(s => s.Games >= 50 && _players.ContainsKey(s.PlayerId)).ToList();

        var mvp = Vote(qualified, MvpScore);
        var rookies = _stats.Values.Where(s => s.Games >= 35 && _players.TryGetValue(s.PlayerId, out var p) && p.RookieSeason == season);
        var roy = Vote(rookies, s => Production(s) + (TeamWins(s.TeamId) - 41) * 0.1);
        var dpoy = Vote(qualified, DefenseScore);
        var sixth = Vote(qualified.Where(s => s.Starts < s.Games / 2), s => Production(s) + (TeamWins(s.TeamId) - 41) * 0.15);

        // Coach of the Year: biggest overachievement versus preseason roster strength, with a nod to wins.
        var expected = _league.Teams.Select(t => (t, core: t.Roster.OrderByDescending(p => p.CurrentAbility).Take(8).DefaultIfEmpty().Average(p => p?.CurrentAbility ?? 0)))
            .OrderByDescending(x => x.core).Select((x, i) => (x.t, rank: i + 1)).ToDictionary(x => x.t.Id, x => x.rank);
        var actual = _standings.All().Select((r, i) => (r.Team.Id, rank: i + 1)).ToDictionary(x => x.Id, x => x.rank);
        var coy = _league.Teams.Select(t => (t, score: (expected[t.Id] - actual[t.Id]) * 1.0 + (TeamWins(t.Id) - 41) * 0.12)).OrderByDescending(x => x.score).First();

        // All-League: 15 best by MVP score, slotted 2 guards / 2 forwards / 1 center per team where possible.
        var pool = qualified.OrderByDescending(MvpScore).Take(30).Select(s => _players[s.PlayerId]).ToList();
        var allLeague = new List<List<int>>();
        for (int team = 0; team < 3; team++)
        {
            var five = new List<Player>();
            void Take(Func<Player, bool> pred, int n) { foreach (var p in pool.Where(pred).Take(n).ToList()) { five.Add(p); pool.Remove(p); } }
            Take(p => p.PrimaryPosition is Position.PG or Position.SG, 2);
            Take(p => p.PrimaryPosition is Position.SF or Position.PF, 2);
            Take(p => p.PrimaryPosition == Position.C, 1);
            Take(_ => true, 5 - five.Count);
            allLeague.Add(five.Select(p => p.Id).ToList());
        }

        return new SeasonAwards
        {
            Season = season,
            MvpVoting = mvp, RoyVoting = roy, DpoyVoting = dpoy, SixthManVoting = sixth,
            CoachOfYearTeamId = coy.t.Id,
            CoachOfYearNote = $"Expected {Ordinal(expected[coy.t.Id])}, finished {Ordinal(actual[coy.t.Id])} with {TeamWins(coy.t.Id)} wins.",
            AllLeague = allLeague,
            AllStarsEast = midseason?.AllStarsEast ?? new(), AllStarsWest = midseason?.AllStarsWest ?? new(),
            AllStarGame = midseason?.AllStarGame ?? "", AllStarMvpId = midseason?.AllStarMvpId,
        };
    }

    /// <summary>Pick 12 All-Stars per conference from the first half of the season and play the exhibition.</summary>
    public SeasonAwards AllStar(int season, GameSimulator sim, DateOnly date)
    {
        var east = new List<int>(); var west = new List<int>();
        foreach (var (conf, list) in new[] { (Conference.East, east), (Conference.West, west) })
        {
            var field = _stats.Values.Where(s => s.Games >= 15 && _players.ContainsKey(s.PlayerId) && _league.TeamById(s.TeamId).Conference == conf)
                .Select(s => (s, score: Production(s) + TeamWinPct(s.TeamId) * 12))
                .OrderByDescending(x => x.score).ToList();
            // Starters: 2 guards, 3 frontcourt; then 7 reserves regardless of position.
            var guards = field.Where(x => _players[x.s.PlayerId].PrimaryPosition is Position.PG or Position.SG).Take(2).ToList();
            var front = field.Where(x => _players[x.s.PlayerId].PrimaryPosition is Position.SF or Position.PF or Position.C).Take(3).ToList();
            var chosen = guards.Concat(front).ToList();
            foreach (var x in field) { if (chosen.Count >= 12) break; if (!chosen.Contains(x)) chosen.Add(x); }
            list.AddRange(chosen.Select(x => x.s.PlayerId));
        }

        var eastTeam = new Team { Id = -1, City = "East", Name = "All-Stars", Abbreviation = "EAST", Conference = Conference.East, Division = "" };
        var westTeam = new Team { Id = -2, City = "West", Name = "All-Stars", Abbreviation = "WEST", Conference = Conference.West, Division = "" };
        eastTeam.Roster.AddRange(east.Select(id => _players[id]));
        westTeam.Roster.AddRange(west.Select(id => _players[id]));
        eastTeam.Tactic.Pace = 108; westTeam.Tactic.Pace = 108;
        eastTeam.Tactic.DefensiveAggression = 20; westTeam.Tactic.DefensiveAggression = 20;

        var r = sim.Simulate(eastTeam, westTeam, _league.Rules, _rng.NextULong());
        var winner = r.Winner;
        var mvpLine = winner.Lines.OrderByDescending(l => l.Points + l.Rebounds * 0.7 + l.Assists).First();
        return new SeasonAwards
        {
            Season = season,
            AllStarsEast = east, AllStarsWest = west,
            AllStarGame = $"{r.Away.Team.Abbreviation} {r.Away.Points}, {r.Home.Team.Abbreviation} {r.Home.Points}",
            AllStarMvpId = mvpLine.Player.Id,
        };
    }

    public static string Ordinal(int n) => n % 100 is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
}
