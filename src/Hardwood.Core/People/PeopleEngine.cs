using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.People;

/// <summary>Something a player or team did this week that the human should hear about.</summary>
public sealed record PeopleEvent(Player Player, Team Team, PeopleEventKind Kind, string Text);
/// <summary>A week's reading of a team's mood: chemistry and average morale.</summary>
public sealed record MoodSnapshot(DateOnly Date, double Chemistry, double Morale);

public enum PeopleEventKind { WantsTalk, TradeRequest, PromiseKept, PromiseBroken, Settled }

/// <summary>A promise the coach made a player (decision #64): more minutes over his next ten games.</summary>
public sealed class Promise
{
    public int PlayerId { get; set; }
    /// <summary>The team that made the promise (a traded player's minutes promise lapses; a trade promise is kept).</summary>
    public int TeamId { get; set; }
    public double TargetMpg { get; set; }
    public int GamesAtStart { get; set; }
    public double MinutesAtStart { get; set; }
    public DateOnly Made { get; set; }
    public int Games { get; set; } = 10;
    /// <summary>"minutes" or "trade" (a promise to trade him within 30 days).</summary>
    public string Kind { get; set; } = "minutes";
}

/// <summary>
/// Players as people (decision #64). Each week every player's morale drifts toward what his situation warrants:
/// minutes against what his standing on the team leads him to expect, winning, his contract, how well his role suits
/// him, and the team's chemistry, all weighted by personality (ego and ambition care about minutes and money,
/// temperament sets how fast moods swing). Chemistry grows with continuity and happy, team-first players and drops
/// with roster churn, unhappy players and clashing egos. Both feed back into games (EngineConfig.MoraleLogit,
/// ChemistryLogit) and into whether a player re-signs.
/// </summary>
public static class PeopleEngine
{
    public const double Neutral = 60;

    /// <summary>Minutes a player expects from where he ranks on the roster by ability.</summary>
    public static double ExpectedMinutes(Player p, Team team)
    {
        int rank = team.Roster.OrderByDescending(x => x.CurrentAbility).ToList().IndexOf(p) + 1;
        double baseMins = rank switch { <= 0 => 0, <= 2 => 34, <= 5 => 29, <= 7 => 22, <= 9 => 15, <= 11 => 8, _ => 3 };
        // Young players on cheap deals accept a smaller role.
        return baseMins;
    }

    /// <summary>What's driving a player's mood this week, each with its effect on his target morale.</summary>
    public static List<(string reason, double effect)> Factors(Player p, Team team, double winPct, PlayerSeasonStats? stats, Finance finance, DateOnly today,
                                                               double salaryScale = 1)
    {
        var f = new List<(string, double)>();
        var h = p.Hidden;
        if (stats is { Games: >= 3 })
        {
            double expected = ExpectedMinutes(p, team);
            double gap = stats.Mpg - expected;
            double weight = 0.6 + (h.Ego + h.Ambition) / 40.0;
            double e = Math.Clamp(gap * 1.7, -32, 4) * weight;
            if (Math.Abs(e) >= 2)
                f.Add((gap < 0 ? $"Wants more minutes ({stats.Mpg:0} a game; expects about {expected:0})" : $"Happy with his minutes ({stats.Mpg:0} a game)", e));
        }
        if (stats is { Games: >= 5 } || winPct > 0)
        {
            double e = (winPct - 0.5) * 44 * (0.5 + h.Ambition / 20.0);
            if (Math.Abs(e) >= 2) f.Add((winPct >= 0.5 ? "Enjoying winning" : "Frustrated by losing", e));
        }
        if (p.Contract is { } c && c.Salary > 0)
        {
            double market = ContractMarket.MarketSalary(p.CurrentAbility, p.Age(today), finance) * salaryScale;
            double ratio = c.Salary / Math.Max(1.0, market);
            if (ratio < 0.65) f.Add(("Feels underpaid", -8 * (0.5 + h.Ambition / 20.0)));
            else if (ratio > 1.3) f.Add(("Well paid", 3));
        }
        double stars = Roles.Stars(p, team.Tactic.RoleOf(p));
        if (stars >= 3.5) f.Add(($"Suits his role ({Roles.Info(team.Tactic.RoleOf(p)).Name})", 3));
        else if (stars < 2) f.Add(($"Doesn't suit his role ({Roles.Info(team.Tactic.RoleOf(p)).Name})", -6));
        double chem = (team.Chemistry - Neutral) * 0.15;
        if (Math.Abs(chem) >= 2) f.Add((team.Chemistry >= Neutral ? "Likes the team spirit" : "Unsettled in the locker room", chem));
        if (p.Injury != null && !p.IsAvailable(today)) f.Add(("Frustrated by his injury", -3));
        if (p.TradeRequested) f.Add(("Wants out", -8));
        if (team.Coach is { MoraleBonus: var mb } && Math.Abs(mb) >= 1) f.Add((mb > 0 ? "Plays for his coach" : "Not inspired by the coach", mb));
        return f;
    }

    public static double Target(IEnumerable<(string reason, double effect)> factors) => Math.Clamp(Neutral + factors.Sum(x => x.effect), 5, 98);

    public static string Mood(double morale) => morale switch
    {
        >= 82 => "Delighted",
        >= 68 => "Happy",
        >= 50 => "Content",
        >= 35 => "Unhappy",
        _ => "Very unhappy",
    };

    /// <summary>
    /// Chemistry target for a team: continuity of the rotation, average morale, leaders and team-first players, and
    /// big egos (several egos together clash).
    /// </summary>
    public static List<(string reason, double effect)> ChemistryFactors(Team team)
    {
        var rot = team.Roster.OrderByDescending(p => p.CurrentAbility).Take(9).ToList();
        var f = new List<(string, double)>();
        if (rot.Count == 0) return f;
        double weeks = rot.Average(p => Math.Min(52, team.WeeksTogether.GetValueOrDefault(p.Id)));
        f.Add((weeks >= 30 ? "A settled group that knows each other" : weeks >= 12 ? "Still getting used to each other" : "Lots of new faces", (weeks - 28) * 0.45));
        double morale = rot.Average(p => p.Morale);
        if (Math.Abs(morale - Neutral) >= 3) f.Add((morale >= Neutral ? "Happy players" : "Unhappy players", (morale - Neutral) * 0.5));
        double leaders = rot.Max(p => p.Attributes[Attr.Leadership]);
        if (leaders >= 15) f.Add(("A strong leader in the locker room", (leaders - 14) * 1.5));
        double team1 = rot.Average(p => p.Attributes[Attr.Teamwork]);
        f.Add((team1 >= 11 ? "Team-first players" : "Too many individuals", (team1 - 11) * 2));
        int egos = rot.Count(p => p.Hidden.Ego >= 15);
        if (egos >= 2) f.Add(($"{egos} big egos competing for shots", -(egos - 1) * 4));
        if (team.Coach is { ChemistryBonus: var cb } && Math.Abs(cb) >= 1) f.Add((cb > 0 ? "A disciplined locker room" : "A loose locker room", cb));
        int requests = team.Roster.Count(p => p.TradeRequested);
        if (requests > 0) f.Add(("A player wants out", -5 * requests));
        return f;
    }

    /// <summary>
    /// The weekly update for every team in a league: chemistry, then each player's morale, trade requests and
    /// promises. Events concern the human team only (AI teams just live with it).
    /// </summary>
    public static List<PeopleEvent> Weekly(League league, DateOnly today, Func<Team, double> winPct,
        IReadOnlyDictionary<int, PlayerSeasonStats> stats, int? humanTeamId, List<Promise>? promises = null) =>
        Weekly(league.Teams, league.Finance, 1, id => league.Teams.FirstOrDefault(t => t.Id == id), today, winPct, stats, humanTeamId, promises);

    /// <summary>The weekly update for any set of clubs (a club abroad judges wages by its league's market: <paramref name="salaryScale"/>).</summary>
    public static List<PeopleEvent> Weekly(IEnumerable<Team> teams, Finance finance, double salaryScale, Func<int, Team?> teamById, DateOnly today,
        Func<Team, double> winPct, IReadOnlyDictionary<int, PlayerSeasonStats> stats, int? humanTeamId, List<Promise>? promises = null)
    {
        var events = new List<PeopleEvent>();
        var teamList = teams.ToList();
        foreach (var team in teamList)
        {
            // Continuity: count the weeks each player has been here.
            var ids = team.Roster.Select(p => p.Id).ToHashSet();
            foreach (var gone in team.WeeksTogether.Keys.Where(k => !ids.Contains(k)).ToList()) team.WeeksTogether.Remove(gone);
            foreach (var p in team.Roster)
            {
                int w = team.WeeksTogether.GetValueOrDefault(p.Id) + 1;
                team.WeeksTogether[p.Id] = w;
                if (w == 1 && team.WeeksTogether.Count > 1 && team.WeeksTogether.Values.Any(x => x > 1)) Joined(p); // a new arrival
            }

            double chemTarget = Math.Clamp(Neutral + ChemistryFactors(team).Sum(x => x.effect), 10, 98);
            team.Chemistry += (chemTarget - team.Chemistry) * 0.25;

            double wp = winPct(team);
            bool human = team.Id == humanTeamId;
            foreach (var p in team.Roster)
            {
                stats.TryGetValue(p.Id, out var st);
                if (st != null && st.TeamId != team.Id) st = null; // stats from another team don't count here
                double target = Target(Factors(p, team, wp, st, finance, today, salaryScale));
                double rate = 0.2 + (21 - p.Hidden.Temperament) / 100.0;
                p.Morale = Math.Clamp(p.Morale + (target - p.Morale) * rate, 0, 100);

                p.UnhappyWeeks = p.Morale < 38 ? p.UnhappyWeeks + 1 : 0;
                if (!p.TradeRequested && p.UnhappyWeeks >= 3 && (p.Hidden.Ego + p.Hidden.Ambition >= 23 || p.Morale < 28))
                {
                    p.TradeRequested = true;
                    if (human) events.Add(new(p, team, PeopleEventKind.TradeRequest,
                        $"{p.Name} has asked to be traded. Talk to him on the Squad page: promise to move him, refuse, or try to convince him to stay."));
                }
                else if (human && p.Morale < 38 && !p.TradeRequested && (p.LastTalk == null || today.DayNumber - p.LastTalk.Value.DayNumber >= 21)
                         && today.DayNumber % 14 < 7 /* not every week */)
                {
                    var worst = Factors(p, team, wp, st, finance, today, salaryScale).OrderBy(x => x.effect).FirstOrDefault();
                    events.Add(new(p, team, PeopleEventKind.WantsTalk, $"{p.Name} is unhappy and wants to talk. {(worst.reason != null ? worst.reason + "." : "")}"));
                }
            }
        }
        if (promises != null) events.AddRange(CheckPromises(teamList.SelectMany(t => t.Roster), teamById, today, stats, promises));
        return events;
    }

    private static IEnumerable<PeopleEvent> CheckPromises(IEnumerable<Player> players, Func<int, Team?> teamById, DateOnly today, IReadOnlyDictionary<int, PlayerSeasonStats> stats, List<Promise> promises)
    {
        var byId = players.ToDictionary(p => p.Id);
        foreach (var pr in promises.ToList())
        {
            var p = byId.GetValueOrDefault(pr.PlayerId);
            var team = p?.TeamId is { } tid ? teamById(tid) : null;
            if (p == null || team == null) { promises.Remove(pr); continue; }
            if (team.Id != pr.TeamId)
            {
                promises.Remove(pr); // he's moved on: a trade promise is kept, a minutes promise lapses
                continue;
            }
            if (pr.Kind == "trade")
            {
                if (today.DayNumber - pr.Made.DayNumber < 30) continue;
                promises.Remove(pr);
                p.Morale = Math.Max(0, p.Morale - 25); p.DistrustUntil = today.AddDays(90);
                yield return new(p, team, PeopleEventKind.PromiseBroken, $"You promised to trade {p.Name} and didn't. He's furious, and won't believe your promises for a while.");
                continue;
            }
            stats.TryGetValue(p.Id, out var st);
            int games = (st?.Games ?? 0) - pr.GamesAtStart;
            if (games < pr.Games) continue;
            promises.Remove(pr);
            double mpg = ((st?.Minutes ?? 0) - pr.MinutesAtStart) / Math.Max(1, games);
            if (mpg >= pr.TargetMpg - 3)
            {
                p.Morale = Math.Min(100, p.Morale + 8);
                yield return new(p, team, PeopleEventKind.PromiseKept, $"You kept your word to {p.Name}: {mpg:0} minutes a game over his last {games}. He appreciates it.");
            }
            else
            {
                p.Morale = Math.Max(0, p.Morale - 20); p.DistrustUntil = today.AddDays(90);
                yield return new(p, team, PeopleEventKind.PromiseBroken, $"You promised {p.Name} about {pr.TargetMpg:0} minutes; he got {mpg:0}. He feels lied to.");
            }
        }
    }

    /// <summary>Summer: moods settle back toward neutral and trade requests are wiped (new season, new start).</summary>
    public static void Summer(League league)
    {
        foreach (var p in league.AllActivePlayers.Concat(league.FreeAgents))
        {
            p.Morale += (Neutral - p.Morale) * 0.5;
            p.UnhappyWeeks = 0;
            p.TradeRequested = false;
        }
    }

    /// <summary>A player joining a new team gets a fresh start.</summary>
    public static void Joined(Player p) { p.Morale = Math.Max(p.Morale, 58); p.TradeRequested = false; p.UnhappyWeeks = 0; }
}
