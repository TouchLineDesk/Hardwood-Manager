using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.International;

namespace Hardwood.Core.Season;

public sealed partial class GameSession
{
    /// <summary>I6a (run 214): your national-team job and the open offers (never null; older saves get an empty one).</summary>
    public NationalCoaching NationalJobState => World.International.Coaching ??= new NationalCoaching();

    /// <summary>The nation you coach, or null.</summary>
    public NationalTeam? MyNation => NationalJobState.Code is { } code ? NationalTeams.Ranked(this).FirstOrDefault(t => t.Nation.Code == code) : null;

    /// <summary>I6a: once a day. Offers past their day lapse; from Oct 15 each year, a coach with no national team hears from up to
    /// two federations (by reputation, smaller nations first). Its own stream: nothing shared is drawn.</summary>
    private void NationalJobDay()
    {
        var job = NationalJobState;
        if (job.Offers.RemoveAll(o => o.Expires <= Today) > 0 && job.Offers.Count == 0 && job.Code == null)
            Post("National team offers lapsed", "The federations that called have moved on. Others may call next October.", "World");
        if (Today < NationalJob.OfferDate(Today.Year) || job.LastOfferYear >= Today.Year) return;
        job.LastOfferYear = Today.Year;
        if (job.Code != null) return;
        var offers = NationalJob.OffersFor(NationalTeams.Ranked(this), Coach.Reputation, Seed, Today);
        if (offers.Count == 0) return;
        job.Offers = offers;
        string list = string.Join("\n", offers.Select(o => $"• {o.Name}, No. {o.Rank} in the FIBA world ranking"));
        Post(offers.Count == 1 ? $"{offers[0].Name} want you as head coach" : "National teams want you as head coach",
            $"{(offers.Count == 1 ? "A federation has" : $"{offers.Count} federations have")} asked whether you'll coach their national team alongside "
            + (Employed ? $"your job at {HumanTeam.TheFullName}" : "whatever club job comes next") + $":\n\n{list}\n\n"
            + $"Club seasons don't stop: you pick the twelve for the qualifying windows and the summer tournaments, and the club keeps its own schedule. "
            + $"Federations call by reputation (yours is {Coach.Reputation:0}/100), so the smaller nations come first. "
            + $"Answer on the International page by {offers[0].Expires:MMM d}.", "World");
    }

    /// <summary>I6a: take a federation's offer. You start with the federation's own twelve as your picks.</summary>
    public bool AcceptNationalOffer(string code)
    {
        var job = NationalJobState;
        var offer = job.Offers.FirstOrDefault(o => o.Code == code);
        if (offer == null || offer.Expires < Today || job.Code != null) return false;
        var team = NationalTeams.Ranked(this).FirstOrDefault(t => t.Nation.Code == code);
        if (team == null) return false;
        job.Code = code;
        job.Name = team.Name;
        job.Since = Today;
        job.Picks = team.Squad.Select(x => x.Player.Id).ToList();
        job.Offers.Clear();
        Post($"You're the head coach of {team.Name}",
            $"{team.Name} (No. {team.Rank} in the world) are yours, alongside your club job. The federation's twelve are your picks for now: "
            + "change them on the International page before the next window or tournament. A pick who is hurt on the day, or no longer eligible, "
            + "is replaced by the next best player for that event.", "World");
        return true;
    }

    public bool DeclineNationalOffer(string code) => NationalJobState.Offers.RemoveAll(o => o.Code == code) > 0;

    /// <summary>I6a: step down from the national team.</summary>
    public bool ResignNationalJob()
    {
        var job = NationalJobState;
        if (job.Code == null) return false;
        job.Past.Add(new NationalSpell(job.Code, job.Name, job.Since ?? Today, Today));
        Post($"You've stepped down as head coach of {job.Name}", "The federation thanked you and will name a successor. Other nations may call next October.", "World");
        job.Code = null; job.Name = ""; job.Since = null; job.Picks.Clear(); job.Games.Clear();
        return true;
    }

    /// <summary>I6a: add a player to (or drop him from) your twelve. Only players in your nation's pool; at most twelve.</summary>
    public bool ToggleNationalPick(int playerId)
    {
        var job = NationalJobState;
        if (job.Code == null) return false;
        if (job.Picks.Remove(playerId)) return true;
        var team = MyNation;
        if (team == null || team.Pool.All(x => x.Player.Id != playerId)) return false;
        // Review (run 214): a pick who has left the pool (retired, gone from the world) no longer holds a place.
        var pool = team.Pool.Select(x => x.Player.Id).ToHashSet();
        job.Picks.RemoveAll(id => !pool.Contains(id));
        if (job.Picks.Count >= NationalTeams.SquadSize) return false;
        job.Picks.Add(playerId);
        return true;
    }

    /// <summary>I6a: go back to the federation's best twelve.</summary>
    public void ResetNationalPicks()
    {
        var job = NationalJobState;
        if (job.Code == null) return;
        job.Picks.Clear();
        if (MyNation is { } team) job.Picks = team.Squad.Select(x => x.Player.Id).ToList();
    }
}

public sealed partial class GameSession
{
    /// <summary>I6b (run 215): a window game is home and away (the quick results' home court), so not a neutral site.</summary>
    public static readonly Rules WindowRules = Rules.FibaNational with { NeutralSite = false };

    private NationalFixture? _liveNational;
    /// <summary>I6b: the national game being played live today, or null.</summary>
    public NationalFixture? LiveNational => _liveNational;

    private (DateOnly Day, string Code, NationalFixture? Fixture)? _natCache;

    /// <summary>I6b: the FIBA window running today (from its first day to the day before it is played), or null.</summary>
    private FibaEvent? WindowToday() =>
        FibaCalendar.InYear(Today.Year).FirstOrDefault(e => e.Kind == FibaEventKind.Window && e.Start <= Today && Today < e.End);

    /// <summary>I6b (run 215): your nation's qualifier today, still to be played, or null (no job, no window, no game today).
    /// Reads only: the group draw it may need isn't kept until the game is played.</summary>
    public NationalFixture? MyNationalGameToday
    {
        get
        {
            var job = NationalJobState;
            if (job.Code is not { } code || WindowToday() is not { } w) return null;
            var h = World.International;
            if (h.Windows.Any(x => x.Key == Qualifying.KeyOf(w))) return null;
            NationalFixture? f;
            if (_natCache is { } c && c.Day == Today && c.Code == code) f = c.Fixture;
            else
            {
                var teams = NationalTeams.For(this, w.Start);
                var ranked = FibaRanking.Apply(teams, FibaRanking.Current(h, teams));
                if (ranked.All(t => t.Nation.Code != code || t.Rank <= 0)) f = null;
                else f = Qualifying.FixturesOf(Qualifying.CycleFor(h, w, ranked, keep: false), w)
                    .FirstOrDefault(x => x.Day == Today && (x.Home == code || x.Away == code));
                _natCache = (Today, code, f);
            }
            if (f != null && job.Games.Any(g => g.Key == f.Key && g.Home == f.Home && g.Away == f.Away)) return null;
            return f;
        }
    }

    /// <summary>I6b: your club's game today (any competition), which the national game gives way to.</summary>
    public bool ClubPlaysToday => ClubGameToday;

    /// <summary>I6b: start (or play) today's national game. Live only when your club is idle today; otherwise your assistant
    /// takes the national game in the full engine and you're told the score. Returns the live game, or null.</summary>
    private GameSimulator.LiveGame? BeginNationalGame(bool live)
    {
        _liveNational = null;
        if (MyNationalGameToday is not { } f || WindowToday() is not { } w) return null;
        var h = World.International;
        var teams = NationalTeams.For(this, w.Start);
        Qualifying.CycleFor(h, w, FibaRanking.Apply(teams, FibaRanking.Current(h, teams)), keep: true); // the draw holds from today
        var today = NationalTeams.For(this, Today);
        var home = today.FirstOrDefault(t => t.Nation.Code == f.Home);
        var away = today.FirstOrDefault(t => t.Nation.Code == f.Away);
        if (home == null || away == null) return null; // a side short of twelve today: the window's quick result stands
        var ht = NationalGames.TeamOf(home, -910_001, home: true);
        var at = NationalGames.TeamOf(away, -910_002, home: false);
        ulong seed = NationalGames.GameSeed(Seed, f);
        if (live && !ClubGameToday)
        {
            _liveNational = f;
            return _sim.StartLive(ht, at, WindowRules, seed);
        }
        var r = _sim.Simulate(ht, at, WindowRules, seed, keepLog: true);
        RecordNational(f, r, live: false);
        return null;
    }

    /// <summary>I6b: keep a national game's score for the window, and tell the coach.</summary>
    private void RecordNational(NationalFixture f, GameResult r, bool live)
    {
        var job = NationalJobState;
        if (job.Code is not { } code) return;
        var g = new CoachedGame(f.Key, f.Group, f.Home, f.Away, r.Home.Points, r.Away.Points, f.Day, live);
        job.Games.Add(g);
        if (live) return; // you watched it: the box score follows
        bool won = g.Won(code);
        var box = f.Home == code ? r.Home : r.Away;
        var top = box.Lines.Where(l => l.Seconds > 0).OrderByDescending(l => l.Points).FirstOrDefault();
        string opp = (f.Home == code ? r.Away : r.Home).Team.City;
        Post($"{job.Name} {(won ? "beat" : "lost to")} {opp} {Math.Max(g.HomePts, g.AwayPts)}-{Math.Min(g.HomePts, g.AwayPts)}",
            (ClubGameToday ? $"Your assistant took the {f.Group} qualifier ({(f.Home == code ? "at home" : "away")}) while you were with the club today. "
                : $"The {f.Group} qualifier ({(f.Home == code ? "at home" : "away")}) was played without you watching: your assistant ran the bench. ")
            + (top != null ? $"{top.Player.Name} led {job.Name} with {top.Points} points. " : "")
            + "With Watch games on, you coach your nation's game yourself on a day your club doesn't play.", "World");
    }

    /// <summary>I6b: after a window is played, your results move your reputation (wins above what the ranking expected).</summary>
    private void ReviewWindow(WindowRecord rec, IReadOnlyDictionary<string, double> pointsBefore)
    {
        var job = NationalJobState;
        if (job.Code is not { } code) return;
        var mine = rec.Games.Where(g => g.Home == code || g.Away == code)
            .Select(g => new CoachedGame(rec.Key, g.Round, g.Home, g.Away, g.HomePts, g.AwayPts, Today, false)).ToList();
        job.Games.RemoveAll(g => g.Key == rec.Key);
        if (mine.Count == 0) return;
        double rep = NationalGames.WindowReputation(mine, code, pointsBefore);
        Coach.Reputation = Math.Clamp(Coach.Reputation + rep, 0, 100);
        job.ReputationEarned = Math.Round(job.ReputationEarned + rep, 1);
        int w = mine.Count(g => g.Won(code));
        Post($"{job.Name}: {w}-{mine.Count - w} in the {rec.Target} qualifiers' window",
            $"Your {job.Name} went {w}-{mine.Count - w} in the window ({rec.Dates}). "
            + (rep > 0 ? $"Beating the ranking's expectations lifts your reputation by {rep:0.0}." : rep < 0 ? $"Falling short of the ranking's expectations costs you {-rep:0.0} reputation." : "About what the ranking expected: your reputation holds.")
            + $" Reputation now {Coach.Reputation:0}/100.", "World");
    }

    /// <summary>I6b: after a summer tournament your nation played, the federation's verdict: reputation, and perhaps the sack.</summary>
    private void ReviewTournaments(IReadOnlyList<WorldCupRecord> played)
    {
        var job = NationalJobState;
        foreach (var rec in played)
        {
            if (job.Code is not { } code || rec.Kind == "U23" || rec.Teams.All(t => t.Code != code)) continue;
            bool major = rec.Kind is "" or "WorldCup" or "Olympics";
            var (rep, sacked, verdict) = NationalGames.Review(rec, code, major);
            Coach.Reputation = Math.Clamp(Coach.Reputation + rep, 0, 100);
            job.ReputationEarned = Math.Round(job.ReputationEarned + rep, 1);
            int seed = NationalGames.SeedPlace(rec, code);
            string placing = Placing(rec, code);
            job.Verdicts.Add($"{rec.Short}: {placing}, seeded {Ordinal(seed)} of {rec.Teams.Count} · {verdict}");
            string repLine = rep > 0 ? $"Your reputation rises {rep:0.0}" : rep < 0 ? $"Your reputation falls {-rep:0.0}" : "Your reputation holds";
            if (!sacked)
            {
                Post($"{job.Name} at {rec.Short}: {placing}",
                    $"{job.Name} went in seeded {Ordinal(seed)} of {rec.Teams.Count} and finished {placing}. The federation is {verdict}. {repLine} (now {Coach.Reputation:0}/100).", "World");
                continue;
            }
            job.Past.Add(new NationalSpell(code, job.Name, job.Since ?? Today, Today, Sacked: true));
            Post($"{job.Name} have sacked you after {rec.Short}",
                $"{job.Name} went in seeded {Ordinal(seed)} of {rec.Teams.Count} and went {placing}. The federation has seen enough and has ended your time as head coach. "
                + $"{repLine} (now {Coach.Reputation:0}/100). Your club job isn't affected; other nations may call next October.", "World");
            job.Code = null; job.Name = ""; job.Since = null; job.Picks.Clear(); job.Games.Clear();
        }
    }
}
