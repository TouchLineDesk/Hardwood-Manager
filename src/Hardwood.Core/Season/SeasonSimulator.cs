using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

public sealed record SeriesResult(Team Winner, Team Loser, int WinnerGames, int LoserGames, List<GameResult> Games);

public sealed class SeasonOutcome
{
    public required Standings Standings { get; init; }
    public List<GameResult> RegularSeason { get; } = new();
    public List<(string Round, SeriesResult Series)> Playoffs { get; } = new();
    public Team? Champion { get; set; }
    /// <summary>Mid-season All-Star selections and result, if the season produced them.</summary>
    public SeasonAwards? Midseason { get; set; }
}

/// <summary>Runs a regular season, a play-in, and a 16-team best-of-seven playoff bracket.</summary>
public sealed class SeasonSimulator
{
    private readonly GameSimulator _sim;
    private readonly Rng _rng;
    private DateOnly? _date;   // running date through the postseason

    private GameResult Play(Team home, Team away, Rules rules)
    {
        var r = _sim.Simulate(home, away, rules, _rng.NextULong(), date: _date);
        if (_date is { } d) _date = d.AddDays(2);
        return r;
    }

    public SeasonSimulator(Rng rng, EngineConfig? cfg = null)
    {
        _rng = rng;
        _sim = new GameSimulator(cfg);
    }

    public SeasonOutcome Run(League league, List<ScheduledGame> schedule, Action<int, int>? progress = null)
    {
        var standings = new Standings(league);
        var outcome = new SeasonOutcome { Standings = standings };

        int i = 0;
        var allStarDate = new DateOnly(league.Season + 1, 2, 14);
        bool allStarDone = false;
        DateOnly? nextWeekly = null;
        var running = new Dictionary<int, PlayerSeasonStats>();
        foreach (var g in schedule)
        {
            if (!allStarDone && g.Date >= allStarDate)
            {
                var stats = PlayerSeasonStats.Aggregate(outcome.RegularSeason, league.Season);
                outcome.Midseason = new AwardsEngine(league, standings, stats, _rng).AllStar(league.Season, _sim, allStarDate);
                allStarDone = true;
            }
            // Weekly: players' moods and team chemistry move (decision #64).
            if (nextWeekly == null) nextWeekly = g.Date.AddDays(7);
            else if (g.Date >= nextWeekly)
            {
                People.PeopleEngine.Weekly(league, g.Date, t => standings[t].Games > 0 ? standings[t].WinPct : 0.5, running, null);
                nextWeekly = nextWeekly.Value.AddDays(7);
            }
            var r = _sim.Simulate(g.Home, g.Away, league.Rules, _rng.NextULong(), date: g.Date);
            standings.Record(r);
            outcome.RegularSeason.Add(r);
            foreach (var box in new[] { r.Home, r.Away })
            foreach (var l in box.Lines)
            {
                if (l.Seconds <= 0) continue;
                if (!running.TryGetValue(l.Player.Id, out var ps))
                    running[l.Player.Id] = ps = new PlayerSeasonStats { PlayerId = l.Player.Id, TeamId = box.Team.Id, Season = league.Season };
                ps.Add(l);
            }
            if (++i % 100 == 0) progress?.Invoke(i, schedule.Count);
        }

        _date = schedule.Count > 0 ? schedule.Last().Date.AddDays(2) : null;
        var east = Seed(standings, Conference.East, league.Rules, outcome);
        var west = Seed(standings, Conference.West, league.Rules, outcome);

        var eastChamp = Bracket(east, "East", league.Rules, outcome);
        var westChamp = Bracket(west, "West", league.Rules, outcome);

        // Finals: better record gets home court.
        bool eastHome = standings[eastChamp].WinPct >= standings[westChamp].WinPct;
        var finals = Series(eastHome ? eastChamp : westChamp, eastHome ? westChamp : eastChamp, league.Rules);
        outcome.Playoffs.Add(("Finals", finals));
        outcome.Champion = finals.Winner;
        return outcome;
    }

    /// <summary>Top 6 direct; 7–10 play-in for seeds 7 and 8.</summary>
    private List<Team> Seed(Standings standings, Conference conf, Rules rules, SeasonOutcome outcome)
    {
        var table = standings.Conference(conf).Select(r => r.Team).ToList();
        var seeds = table.Take(6).ToList();
        var (t7, t8, t9, t10) = (table[6], table[7], table[8], table[9]);

        var g78 = Play(t7, t8, rules);
        var g910 = Play(t9, t10, rules);
        var seed7 = g78.Winner.Team;
        var loser78 = g78.Loser.Team;
        var winner910 = g910.Winner.Team;
        var g8 = Play(loser78, winner910, rules);
        var seed8 = g8.Winner.Team;

        outcome.Playoffs.Add(($"{conf} Play-In 7/8", new SeriesResult(seed7, loser78, 1, 0, new() { g78 })));
        outcome.Playoffs.Add(($"{conf} Play-In 9/10", new SeriesResult(winner910, g910.Loser.Team, 1, 0, new() { g910 })));
        outcome.Playoffs.Add(($"{conf} Play-In 8th seed", new SeriesResult(seed8, g8.Loser.Team, 1, 0, new() { g8 })));

        seeds.Add(seed7);
        seeds.Add(seed8);
        return seeds; // index 0 = 1 seed ... 7 = 8 seed
    }

    private Team Bracket(List<Team> seeds, string conf, Rules rules, SeasonOutcome outcome)
    {
        // 1v8, 4v5, 3v6, 2v7
        var r1 = new[]
        {
            Series(seeds[0], seeds[7], rules), Series(seeds[3], seeds[4], rules),
            Series(seeds[2], seeds[5], rules), Series(seeds[1], seeds[6], rules),
        };
        foreach (var s in r1) outcome.Playoffs.Add(($"{conf} First Round", s));

        var sf1 = Series(Higher(r1[0].Winner, r1[1].Winner, seeds), Lower(r1[0].Winner, r1[1].Winner, seeds), rules);
        var sf2 = Series(Higher(r1[2].Winner, r1[3].Winner, seeds), Lower(r1[2].Winner, r1[3].Winner, seeds), rules);
        outcome.Playoffs.Add(($"{conf} Semifinals", sf1));
        outcome.Playoffs.Add(($"{conf} Semifinals", sf2));

        var final = Series(Higher(sf1.Winner, sf2.Winner, seeds), Lower(sf1.Winner, sf2.Winner, seeds), rules);
        outcome.Playoffs.Add(($"{conf} Finals", final));
        return final.Winner;

        static Team Higher(Team a, Team b, List<Team> seeds) => seeds.IndexOf(a) < seeds.IndexOf(b) ? a : b;
        static Team Lower(Team a, Team b, List<Team> seeds) => seeds.IndexOf(a) < seeds.IndexOf(b) ? b : a;
    }

    /// <summary>Best of seven, 2-2-1-1-1 home-court format.</summary>
    public SeriesResult Series(Team higher, Team lower, Rules rules)
    {
        int hw = 0, lw = 0;
        var games = new List<GameResult>();
        bool[] higherHome = { true, true, false, false, true, false, true };
        for (int g = 0; g < 7 && hw < 4 && lw < 4; g++)
        {
            var home = higherHome[g] ? higher : lower;
            var away = higherHome[g] ? lower : higher;
            var r = Play(home, away, rules);
            games.Add(r);
            if (r.Winner.Team == higher) hw++; else lw++;
        }
        return hw == 4
            ? new SeriesResult(higher, lower, hw, lw, games)
            : new SeriesResult(lower, higher, lw, hw, games);
    }
}
