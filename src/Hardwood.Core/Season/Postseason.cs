using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

public sealed class Series
{
    public required string Round { get; init; }
    public required Team Higher { get; init; }
    public required Team Lower { get; init; }
    public int HigherWins { get; set; }
    public int LowerWins { get; set; }
    public int WinsNeeded { get; init; } = 4;
    public List<GameResult> Games { get; } = new();

    public bool Complete => HigherWins >= WinsNeeded || LowerWins >= WinsNeeded;
    public Team? Winner => !Complete ? null : HigherWins >= WinsNeeded ? Higher : Lower;
    public Team? Loser => !Complete ? null : HigherWins >= WinsNeeded ? Lower : Higher;
    public int GameNumber => HigherWins + LowerWins + 1;

    /// <summary>2-2-1-1-1 format; single games are at the higher seed; a best of three is 1-1-1 (games 1 and 3 at the higher seed).</summary>
    public bool HigherHostsNext => WinsNeeded == 1 || (WinsNeeded == 2 ? GameNumber is 1 or 3 : GameNumber is 1 or 2 or 5 or 7);
    public string Score => $"{HigherWins}-{LowerWins}";
}

/// <summary>
/// The top league's postseason, advancing one game per series per day so a human team's run can be followed.
/// <para>In a league of two conferences of four divisions of four (Danny's D33 design, #344, D36; run 30) the top two in
/// each division make a 16-team bracket, seeded 1-8 by record within the conference, and the eight third-placed teams play
/// the <see cref="ThirdPlaceName"/> alongside the first three rounds (best of three). Any other league (an older save's 30
/// before its summer, a test league) keeps the NBA's play-in and 16-team bracket.</para>
/// </summary>
public sealed class Postseason
{
    public const string ThirdPlaceName = "Third-place tournament";
    public const string ThirdQuarters = "Third-place quarter-finals", ThirdSemis = "Third-place semi-finals", ThirdFinal = "Third-place final";
    /// <summary>The share of a season's league revenue paid to the third-place tournament's winner (runner-up half that).</summary>
    public const double ThirdPlacePrize = 0.02;

    private readonly League _league;
    private readonly Standings _standings;
    private readonly List<Team> _eastSeeds = new(), _westSeeds = new();
    private readonly Dictionary<Conference, List<Team>> _top6 = new();
    private readonly List<Team> _thirdSeeds = new();

    public List<Series> Series { get; } = new();
    public string Stage { get; private set; } = "Play-In";
    public bool Complete { get; private set; }
    public Team? Champion { get; private set; }
    public Team? RunnerUp { get; private set; }
    /// <summary>True when the league plays the division format (no play-in; a third-place tournament).</summary>
    public bool DivisionFormat { get; }
    public Team? ThirdPlaceWinner => Series.FirstOrDefault(s => s.Round == ThirdFinal)?.Winner;
    public Team? ThirdPlaceRunnerUp => Series.FirstOrDefault(s => s.Round == ThirdFinal)?.Loser;

    public IEnumerable<Series> Active => Series.Where(s => !s.Complete);

    /// <summary>Is a round part of the third-place tournament (not the title bracket)?</summary>
    public static bool IsThirdPlace(string round) => round.StartsWith("Third-place");

    /// <summary>
    /// Does this league play the division format: 32 clubs, 16 a conference, each conference in four divisions of four?
    /// </summary>
    public static bool UsesDivisions(League league) => UsesDivisions(league.Teams);

    public static bool UsesDivisions(IReadOnlyCollection<Team> teams)
    {
        if (teams.Count != 32) return false;
        foreach (var conf in new[] { Conference.East, Conference.West })
        {
            var divs = teams.Where(t => t.Conference == conf).GroupBy(t => t.Division).ToList();
            if (divs.Count != 4 || divs.Any(d => d.Count() != 4)) return false;
        }
        return true;
    }

    /// <summary>A conference's eight playoff seeds in the division format: the top two in each division, ordered by record.</summary>
    public static List<Team> DivisionSeeds(Standings standings, Conference conf)
    {
        var table = standings.Conference(conf).Select(r => r.Team).ToList();
        var inDivision = table.GroupBy(t => t.Division).SelectMany(g => g.Take(2)).ToHashSet();
        return table.Where(inDivision.Contains).ToList();
    }

    /// <summary>The eight third-placed teams (one a division), best record first.</summary>
    public static List<Team> ThirdPlaced(Standings standings)
    {
        var thirds = new[] { Conference.East, Conference.West }
            .SelectMany(c => standings.Conference(c).Select(r => r.Team).GroupBy(t => t.Division).Select(g => g.Skip(2).FirstOrDefault()))
            .Where(t => t != null).Select(t => t!).ToHashSet();
        return standings.All().Select(r => r.Team).Where(thirds.Contains).ToList();
    }

    public Postseason(League league, Standings standings)
    {
        _league = league; _standings = standings;
        DivisionFormat = UsesDivisions(league);
        if (DivisionFormat)
        {
            _thirdSeeds.AddRange(ThirdPlaced(standings));
            foreach (var conf in new[] { Conference.East, Conference.West })
            {
                var seeds = DivisionSeeds(standings, conf);
                (conf == Conference.East ? _eastSeeds : _westSeeds).AddRange(seeds);
                AddFirstRound(conf, seeds);
            }
            // 1v8, 4v5, 3v6, 2v7 by record across the league, so winners pair in the semi-finals as in the title bracket.
            var t = _thirdSeeds;
            foreach (var (a, b) in new[] { (0, 7), (3, 4), (2, 5), (1, 6) })
                Series.Add(new Series { Round = ThirdQuarters, Higher = t[a], Lower = t[b], WinsNeeded = 2 });
            Stage = "First Round";
            return;
        }
        foreach (var conf in new[] { Conference.East, Conference.West })
        {
            var table = standings.Conference(conf).Select(r => r.Team).ToList();
            _top6[conf] = table.Take(6).ToList();
            // Play-in: 7v8 and 9v10
            Series.Add(new Series { Round = $"{conf} Play-In 7/8", Higher = table[6], Lower = table[7], WinsNeeded = 1 });
            Series.Add(new Series { Round = $"{conf} Play-In 9/10", Higher = table[8], Lower = table[9], WinsNeeded = 1 });
        }
    }

    private void AddFirstRound(Conference conf, List<Team> seeds)
    {
        Series.Add(new Series { Round = $"{conf} First Round", Higher = seeds[0], Lower = seeds[7] });
        Series.Add(new Series { Round = $"{conf} First Round", Higher = seeds[3], Lower = seeds[4] });
        Series.Add(new Series { Round = $"{conf} First Round", Higher = seeds[2], Lower = seeds[5] });
        Series.Add(new Series { Round = $"{conf} First Round", Higher = seeds[1], Lower = seeds[6] });
    }

    /// <summary>The prize for the third-place tournament (D36c A): a share of a season's league revenue to the winner, half
    /// that to the runner-up, paid as the final ends.</summary>
    public static long ThirdPlacePrizeFor(bool winner) => (long)(Budgets.NablRevenue * ThirdPlacePrize * (winner ? 1 : 0.5));

    private void PayThirdPlacePrize()
    {
        if (ThirdPlaceWinner is { } w) w.Money.Balance += ThirdPlacePrizeFor(true);
        if (ThirdPlaceRunnerUp is { } r) r.Money.Balance += ThirdPlacePrizeFor(false);
    }

    /// <summary>The next third-place round, played alongside the title bracket's semi-finals and conference finals.</summary>
    private void AdvanceThirdPlace(string from, string to)
    {
        if (!DivisionFormat) return;
        var done = Series.Where(s => s.Round == from).ToList();
        for (int i = 0; i + 1 < done.Count; i += 2)
            Series.Add(Pair(done[i].Winner!, done[i + 1].Winner!, _thirdSeeds, to, 2));
    }

    /// <summary>Play one day: one game in every active series; build the next stage when a stage finishes.</summary>
    public List<GameResult> PlayDay(GameSimulator sim, Rng rng, DateOnly date) => PlayDayExcept(sim, rng, date, null);

    /// <summary>Play every active series except the one involving <paramref name="skip"/> (played live by the caller).</summary>
    public List<GameResult> PlayDayExcept(GameSimulator sim, Rng rng, DateOnly date, Team? skip)
    {
        var results = new List<GameResult>();
        if (Complete) return results;

        foreach (var s in Active.ToList())
        {
            if (skip != null && (s.Higher == skip || s.Lower == skip)) continue;
            var (home, away) = NextMatchup(s);
            var r = sim.Simulate(home, away, _league.Rules, rng.NextULong(), date: date);
            RecordGame(s, r);
            results.Add(r);
        }

        if (skip == null) AfterDay();
        return results;
    }

    public (Team home, Team away) NextMatchup(Series s) => s.HigherHostsNext ? (s.Higher, s.Lower) : (s.Lower, s.Higher);

    public void RecordGame(Series s, GameResult r)
    {
        s.Games.Add(r);
        if (r.Winner.Team == s.Higher) s.HigherWins++; else s.LowerWins++;
    }

    /// <summary>Once every series has played today, advance the stage if the round is finished.</summary>
    public void AfterDay()
    {
        if (!Active.Any() && !Complete) AdvanceStage();
    }

    private void AdvanceStage()
    {
        switch (Stage)
        {
            case "Play-In":
                foreach (var conf in new[] { Conference.East, Conference.West })
                {
                    var s78 = Series.First(s => s.Round == $"{conf} Play-In 7/8");
                    var s910 = Series.First(s => s.Round == $"{conf} Play-In 9/10");
                    Series.Add(new Series { Round = $"{conf} Play-In 8th seed", Higher = s78.Loser!, Lower = s910.Winner!, WinsNeeded = 1 });
                }
                Stage = "Play-In Final";
                break;

            case "Play-In Final":
                foreach (var conf in new[] { Conference.East, Conference.West })
                {
                    var seeds = new List<Team>(_top6[conf]);
                    seeds.Add(Series.First(s => s.Round == $"{conf} Play-In 7/8").Winner!);
                    seeds.Add(Series.First(s => s.Round == $"{conf} Play-In 8th seed").Winner!);
                    (conf == Conference.East ? _eastSeeds : _westSeeds).AddRange(seeds);
                    AddFirstRound(conf, seeds);
                }
                Stage = "First Round";
                break;

            case "First Round":
                foreach (var conf in new[] { Conference.East, Conference.West })
                {
                    var r1 = Series.Where(s => s.Round == $"{conf} First Round").ToList();
                    var seeds = conf == Conference.East ? _eastSeeds : _westSeeds;
                    Series.Add(Pair(r1[0].Winner!, r1[1].Winner!, seeds, $"{conf} Semifinals"));
                    Series.Add(Pair(r1[2].Winner!, r1[3].Winner!, seeds, $"{conf} Semifinals"));
                }
                AdvanceThirdPlace(ThirdQuarters, ThirdSemis);
                Stage = "Semifinals";
                break;

            case "Semifinals":
                foreach (var conf in new[] { Conference.East, Conference.West })
                {
                    var sf = Series.Where(s => s.Round == $"{conf} Semifinals").ToList();
                    var seeds = conf == Conference.East ? _eastSeeds : _westSeeds;
                    Series.Add(Pair(sf[0].Winner!, sf[1].Winner!, seeds, $"{conf} Finals"));
                }
                AdvanceThirdPlace(ThirdSemis, ThirdFinal);
                Stage = "Conference Finals";
                break;

            case "Conference Finals":
                PayThirdPlacePrize();
                var east = Series.First(s => s.Round == "East Finals").Winner!;
                var west = Series.First(s => s.Round == "West Finals").Winner!;
                bool eastHome = _standings[east].WinPct >= _standings[west].WinPct;
                Series.Add(new Series { Round = "Finals", Higher = eastHome ? east : west, Lower = eastHome ? west : east });
                Stage = "Finals";
                break;

            case "Finals":
                var f = Series.First(s => s.Round == "Finals");
                Champion = f.Winner; RunnerUp = f.Loser;
                Complete = true;
                Stage = "Complete";
                break;
        }
    }

    private static Series Pair(Team a, Team b, List<Team> seeds, string round, int winsNeeded = 4)
    {
        bool aHigher = seeds.IndexOf(a) < seeds.IndexOf(b);
        return new Series { Round = round, Higher = aHigher ? a : b, Lower = aHigher ? b : a, WinsNeeded = winsNeeded };
    }

    /// <summary>Rebuild from saved series rows (round, higher id, lower id, wins, wins needed) and the saved stage.</summary>
    public void Restore(string stage, IEnumerable<(string round, int higherId, int lowerId, int higherWins, int lowerWins, int winsNeeded)> rows)
    {
        Series.Clear();
        foreach (var r in rows)
            Series.Add(new Series { Round = r.round, Higher = _league.TeamById(r.higherId), Lower = _league.TeamById(r.lowerId), HigherWins = r.higherWins, LowerWins = r.lowerWins, WinsNeeded = r.winsNeeded });
        Stage = stage;
        Complete = stage == "Complete";
        if (Complete)
        {
            var f = Series.First(s => s.Round == "Finals");
            Champion = f.Winner; RunnerUp = f.Loser;
        }
        // The division format's seeds come from the final table (the constructor); its rounds are restored as saved.
        if (DivisionFormat) return;
        // Seeds are needed to pair later rounds: top six plus the two play-in survivors.
        foreach (var conf in new[] { Conference.East, Conference.West })
        {
            var s78 = Series.FirstOrDefault(s => s.Round == $"{conf} Play-In 7/8");
            var s8 = Series.FirstOrDefault(s => s.Round == $"{conf} Play-In 8th seed");
            if (s78?.Winner == null || s8?.Winner == null) continue;
            var seeds = new List<Team>(_top6[conf]) { s78.Winner, s8.Winner };
            (conf == Conference.East ? _eastSeeds : _westSeeds).AddRange(seeds);
        }
    }

    /// <summary>Convert to the SeasonOutcome shape the offseason expects.</summary>
    public void FillOutcome(SeasonOutcome outcome)
    {
        foreach (var s in Series.Where(s => s.Complete))
            outcome.Playoffs.Add((s.Round, new SeriesResult(s.Winner!, s.Loser!, Math.Max(s.HigherWins, s.LowerWins), Math.Min(s.HigherWins, s.LowerWins), s.Games)));
        outcome.Champion = Champion;
        outcome.ThirdPlaceWinner = ThirdPlaceWinner;
        outcome.ThirdPlaceRunnerUp = ThirdPlaceRunnerUp;
    }
}
