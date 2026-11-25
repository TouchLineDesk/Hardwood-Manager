using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.People;

/// <summary>Something a player or team did this week that the human should hear about.</summary>
public sealed record PeopleEvent(Player Player, Team Team, PeopleEventKind Kind, string Text);
/// <summary>A week's reading of a team's mood: chemistry and average morale.</summary>
public sealed record MoodSnapshot(DateOnly Date, double Chemistry, double Morale);

public enum PeopleEventKind { WantsTalk, TradeRequest, PromiseKept, PromiseBroken, Settled, DressingRoom }

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
    /// <summary>K9 (run 165): below this morale a player counts as unhappy in the room (the weekly unhappy spell's line too).</summary>
    public const double UnhappyLine = 38;
    /// <summary>K9 (run 165): what each unhappy college player costs the chemistry target.</summary>
    public const double SulkCost = 4;

    /// <summary>K9 (run 165): the team's unhappy players, unhappiest first.</summary>
    public static List<Player> Sulking(Team team) => team.Roster.Where(p => p.Morale < UnhappyLine).OrderBy(p => p.Morale).ThenBy(p => p.Id).ToList();

    /// <summary>Minutes a player expects from where he ranks on the roster by ability.</summary>
    public static double ExpectedMinutes(Player p, Team team)
    {
        int rank = team.Roster.OrderByDescending(x => x.CurrentAbility).ToList().IndexOf(p) + 1;
        double baseMins = rank switch { <= 0 => 0, <= 2 => 34, <= 5 => 29, <= 7 => 22, <= 9 => 15, <= 11 => 8, _ => 3 };
        // Young players on cheap deals accept a smaller role.
        return baseMins;
    }

    /// <summary>
    /// K9 (run 165, the dressing room): how much a college player's class year weighs on his wish for minutes. A freshman
    /// accepts waiting his turn; a senior in his last season doesn't (Fr 0.8, So 1.0, Jr 1.2, Sr 1.4).
    /// </summary>
    public static double ClassWeight(Player p, DateOnly today) =>
        // An incoming freshman (enrolled before July 1) counts in next year, as the program's own count does.
        (College.CollegeWorld.ClassYearOf(p, College.CollegeWorld.SchoolYear(today))
         ?? College.CollegeWorld.ClassYearOf(p, College.CollegeWorld.SchoolYear(today) + 1))?.Year switch { 1 => 0.8, 3 => 1.2, 4 => 1.4, _ => 1.0 };

    /// <summary>
    /// K9 (run 165): how much of the team's winning a college player feels part of (1 = all of it). A player getting well
    /// under the minutes he expects (8+, the top eleven) enjoys the wins less: at a third of them, a third of the lift. The pros keep 1.
    /// </summary>
    public static double Involvement(Player p, Team team, PlayerSeasonStats? stats)
    {
        if (stats is not { Games: >= 3 }) return 1;
        double expected = ExpectedMinutes(p, team);
        return expected >= 8 && stats.Mpg < expected ? Math.Clamp(stats.Mpg / expected, 0.25, 1) : 1;
    }

    /// <summary>What's driving a player's mood this week, each with its effect on his target morale.</summary>
    /// <param name="college">K9 (run 165): a college player weighs minutes by his class year, and winning lifts him only as
    /// far as he's part of it (<see cref="ClassWeight"/>, <see cref="Involvement"/>).</param>
    public static List<(string reason, double effect)> Factors(Player p, Team team, double winPct, PlayerSeasonStats? stats, Finance finance, DateOnly today,
                                                               double salaryScale = 1, bool college = false)
    {
        var f = new List<(string, double)>();
        var h = p.Hidden;
        double involvement = college ? Involvement(p, team, stats) : 1;
        if (stats is { Games: >= 3 })
        {
            double expected = ExpectedMinutes(p, team);
            double gap = stats.Mpg - expected;
            double cw = college ? ClassWeight(p, today) : 1;
            double weight = (0.6 + (h.Ego + h.Ambition) / 40.0) * (gap < 0 ? cw : 1);
            double e = Math.Clamp(gap * 1.7, -32, 4) * weight;
            if (Math.Abs(e) >= 2)
                f.Add((gap < 0
                    ? (cw >= 1.4 ? $"A senior who wants his turn ({stats.Mpg:0} a game; expects about {expected:0})" : $"Wants more minutes ({stats.Mpg:0} a game; expects about {expected:0})")
                    : $"Happy with his minutes ({stats.Mpg:0} a game)", e));
        }
        if (stats is { Games: >= 5 } || winPct > 0)
        {
            double e = (winPct - 0.5) * 44 * (0.5 + h.Ambition / 20.0);
            if (e > 0) e *= involvement;
            if (Math.Abs(e) >= 2) f.Add((winPct >= 0.5 ? (involvement < 0.6 ? "Winning, but he isn't part of it" : "Enjoying winning") : "Frustrated by losing", e));
        }
        if (p.Contract is { } c && c.Salary > 0)
        {
            double market = ContractMarket.MarketSalary(p.CurrentAbility, p.Age(today), finance) * salaryScale;
            double ratio = c.Salary / Math.Max(1.0, market);
            if (ratio < 0.65) f.Add(("Feels underpaid", -8 * (0.5 + h.Ambition / 20.0)));
            else if (ratio > 1.3) f.Add(("Well paid", 3));
        }
        // H204 (run 203): in college the fit is judged against his own best role, not the absolute star scale.
        int suits = Roles.Suitability(p, team.Tactic.RoleOf(p), college);
        if (suits > 0) f.Add(($"Suits his role ({Roles.Info(team.Tactic.RoleOf(p)).Name})", 3));
        else if (suits < 0) f.Add(($"Doesn't suit his role ({Roles.Info(team.Tactic.RoleOf(p)).Name})", -6));
        double chem = (team.Chemistry - Neutral) * 0.15;
        if (Math.Abs(chem) >= 2) f.Add((team.Chemistry >= Neutral ? "Likes the team spirit" : "Unsettled in the locker room", chem));
        if (p.IsHurt(today)) f.Add(("Frustrated by his injury", -3));
        if (p.IsIneligible(today)) f.Add(("Ineligible: his grades", -4)); // CS7 part 2c (run 175)
        if (p.TradeRequested) f.Add(("Wants out", -8));
        if (team.Coach is { MoraleBonus: var mb } && Math.Abs(mb) >= 1) f.Add((mb > 0 ? "Plays for his coach" : "Not inspired by the coach", mb));
        if (p.BoostUntil is { } until && until > today && p.MoodBoost != 0) f.Add((p.BoostReason ?? "Settled", p.MoodBoost));
        if (college) // run 196: captains, a lost captaincy and Senior Night, each its own line
        {
            if (team.CaptainIds.Contains(p.Id)) f.Add((College.Captaincy.CaptainReason, College.Captaincy.CaptainMorale));
            else if (team.StrippedUntil.TryGetValue(p.Id, out var su) && su > today) f.Add((College.Captaincy.LostReason, College.Captaincy.LostCaptaincy));
            if (team.HonouredIds.Contains(p.Id)) f.Add((College.Captaincy.HonouredReason, College.Captaincy.SeniorNightLift));
        }
        return f;
    }

    /// <summary>Give a player a lasting lift (replaces a smaller one).</summary>
    public static void Boost(Player p, double amount, DateOnly until, string reason, DateOnly today)
    {
        bool active = p.BoostUntil is { } u && u > today;
        if (active && p.MoodBoost >= amount && p.BoostUntil >= until) return;
        p.MoodBoost = active ? Math.Max(amount, p.MoodBoost) : amount;
        p.BoostUntil = active && p.BoostUntil > until ? p.BoostUntil : until;
        p.BoostReason = reason;
    }

    /// <summary>Weeks of unhappiness before a player either asks out or makes his peace (critic review #3: the same players were unhappy all season).</summary>
    public const int DecideWeeks = 4;
    /// <summary>Weeks a trade request can stand before it resolves one way or the other.</summary>
    public const int RequestWeeks = 8;

    /// <summary>A boost that lifts his target to about "content" (48), whatever is still bothering him.</summary>
    private static double SettleBoost(Player p, Team team, double wp, PlayerSeasonStats? st, Finance finance, DateOnly today, double salaryScale, bool college = false)
    {
        var without = Factors(p, team, wp, st, finance, today, salaryScale, college).Where(x => x.reason != p.BoostReason || p.BoostUntil is not { } u || u <= today);
        return Math.Clamp(50 - Target(without) + (p.TradeRequested ? 8 : 0), 10, 34);
    }

    public static double Target(IEnumerable<(string reason, double effect)> factors) => Math.Clamp(Neutral + factors.Sum(x => x.effect), 5, 98);

    /// <summary>Run 188: the morale number shown beside <see cref="Mood"/>, rounded down so it never crosses a word's line
    /// (49.6 read "50/100" beside "Unhappy").</summary>
    public static int MoraleShown(double morale) => (int)Math.Floor(morale);

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
    /// <param name="college">K9 (run 165): in college, every unhappy player (morale under <see cref="UnhappyLine"/>) costs the
    /// locker room <see cref="SulkCost"/>, named, up to four: nobody can leave, so the mood stays in the room.</param>
    public static List<(string reason, double effect)> ChemistryFactors(Team team, bool college = false)
    {
        var rot = team.Roster.OrderByDescending(p => p.CurrentAbility).Take(9).ToList();
        var f = new List<(string, double)>();
        if (rot.Count == 0) return f;
        double weeks = rot.Average(p => Math.Min(52, team.WeeksTogether.GetValueOrDefault(p.Id)));
        f.Add((weeks >= 30 ? "A settled group that knows each other" : weeks >= 12 ? "Still getting used to each other" : "Lots of new faces", (weeks - 28) * 0.45));
        double morale = rot.Average(p => p.Morale);
        if (Math.Abs(morale - Neutral) >= 3) f.Add((morale >= Neutral ? "Happy players" : "Unhappy players", (morale - Neutral) * 0.5));
        // Run 196: in college, the captains lead the room (good or bad); the pros (and a college summer) keep the strongest leader.
        if (college && College.Captaincy.ChemistryLine(team) is { } caps)
        {
            f.Add(caps);
            if (team.Roster.Where(p => team.CaptainIds.Contains(p.Id)).FirstOrDefault(p => p.Morale < UnhappyLine) is { } sour)
                f.Add(($"Captain {sour.Name} is unhappy", -College.Captaincy.UnhappyCaptainCost));
        }
        else
        {
            double leaders = rot.Max(p => p.Attributes[Attr.Leadership]);
            if (leaders >= 15) f.Add(("A strong leader in the locker room", (leaders - 14) * 1.5));
        }
        double team1 = rot.Average(p => p.Attributes[Attr.Teamwork]);
        f.Add((team1 >= 11 ? "Team-first players" : "Too many individuals", (team1 - 11) * 2));
        int egos = rot.Count(p => p.Hidden.Ego >= 15);
        if (egos >= 2) f.Add(($"{egos} big egos competing for shots", -(egos - 1) * 4));
        if (team.Coach is { ChemistryBonus: var cb } && Math.Abs(cb) >= 1) f.Add((cb > 0 ? "A disciplined locker room" : "A loose locker room", cb));
        int requests = team.Roster.Count(p => p.TradeRequested);
        if (requests > 0) f.Add(("A player wants out", -5 * requests));
        if (college && Sulking(team) is { Count: > 0 } sulk)
            f.Add((sulk.Count == 1 ? $"{sulk[0].Name} is sulking" : $"{sulk.Count} unhappy players in the locker room", -SulkCost * Math.Min(4, sulk.Count)));
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
        Func<Team, double> winPct, IReadOnlyDictionary<int, PlayerSeasonStats> stats, int? humanTeamId, List<Promise>? promises = null,
        bool allowRequests = true, bool college = false)
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

            double chemTarget = Math.Clamp(Neutral + ChemistryFactors(team, college).Sum(x => x.effect), 10, 98);
            team.Chemistry += (chemTarget - team.Chemistry) * 0.25;

            double wp = winPct(team);
            bool human = team.Id == humanTeamId;
            foreach (var p in team.Roster)
            {
                stats.TryGetValue(p.Id, out var st);
                if (st != null && st.TeamId != team.Id) st = null; // stats from another team don't count here
                double target = Target(Factors(p, team, wp, st, finance, today, salaryScale, college));
                double rate = 0.2 + (21 - p.Hidden.Temperament) / 100.0;
                p.Morale = Math.Clamp(p.Morale + (target - p.Morale) * rate, 0, 100);

                bool settled = p.BoostUntil is { } bu && bu > today && p.BoostReason is "Has accepted his situation" or "Withdrew his trade request";
                p.UnhappyWeeks = p.TradeRequested || (p.Morale < 38 && !settled) ? p.UnhappyWeeks + 1 : 0;
                var season = new DateOnly(today.Month >= 8 ? today.Year + 1 : today.Year, 6, 30); // the boost lasts the season
                // Every unhappy spell resolves (critic review #3): after about a month he either asks out (egos, ambition,
                // or real misery) or makes his peace with it; a trade request stands about two months before it resolves.
                // Danny, Nov 22 (danny-no-college-transfers-nov22): a college player plays where he starts. With requests off
                // (college), nobody asks out: a proud or miserable player simply stays unhappy (and says so) until the cause is fixed.
                if (!allowRequests && p.TradeRequested) { p.TradeRequested = false; }
                bool wantsOut = !p.TradeRequested && p.UnhappyWeeks >= DecideWeeks && (p.Hidden.Ego + p.Hidden.Ambition >= 22 || p.Morale < 26);
                if (wantsOut && !allowRequests)
                {
                    if (human && (p.LastTalk == null || today.DayNumber - p.LastTalk.Value.DayNumber >= 21) && today.DayNumber % 14 < 7)
                    {
                        var worst = Factors(p, team, wp, st, finance, today, salaryScale, college).OrderBy(x => x.effect).FirstOrDefault();
                        events.Add(new(p, team, PeopleEventKind.WantsTalk, $"{p.Name} is unhappy and wants to talk. {(worst.reason != null ? worst.reason + "." : "")}"));
                    }
                }
                else if (wantsOut)
                {
                    p.TradeRequested = true;
                    if (human) events.Add(new(p, team, PeopleEventKind.TradeRequest,
                        $"{p.Name} has asked to be traded. Talk to him on the Squad page (promise to move him, refuse, or convince him to stay), "
                        + $"or sell him on the Transactions page. If nothing changes he'll decide within about {RequestWeeks} weeks whether to knuckle down."));
                }
                else if (!p.TradeRequested && p.UnhappyWeeks >= DecideWeeks)
                {
                    Boost(p, SettleBoost(p, team, wp, st, finance, today, salaryScale, college), season, "Has accepted his situation", today);
                    p.UnhappyWeeks = 0; p.Morale = Math.Max(p.Morale, 40);
                    if (human) events.Add(new(p, team, PeopleEventKind.Settled,
                        $"{p.Name} has accepted his situation for now and will keep working. It still matters: fix the cause (minutes, role, contract) and he'll be happier still."));
                }
                else if (p.TradeRequested && p.UnhappyWeeks >= DecideWeeks + RequestWeeks && !(promises?.Any(x => x.PlayerId == p.Id && x.Kind == "trade") ?? false))
                {
                    p.TradeRequested = false; p.UnhappyWeeks = 0; p.Morale = Math.Max(p.Morale, 40);
                    Boost(p, SettleBoost(p, team, wp, st, finance, today, salaryScale, college), season, "Withdrew his trade request", today);
                    if (human) events.Add(new(p, team, PeopleEventKind.Settled,
                        $"{p.Name} has withdrawn his trade request and will see out the season. He hasn't forgotten; expect the question again in the summer."));
                }
                else if (human && p.Morale < 38 && !p.TradeRequested && (p.LastTalk == null || today.DayNumber - p.LastTalk.Value.DayNumber >= 21)
                         && today.DayNumber % 14 < 7 /* not every week */)
                {
                    var worst = Factors(p, team, wp, st, finance, today, salaryScale, college).OrderBy(x => x.effect).FirstOrDefault();
                    events.Add(new(p, team, PeopleEventKind.WantsTalk, $"{p.Name} is unhappy and wants to talk. {(worst.reason != null ? worst.reason + "." : "")}"));
                }
            }
            // K9 (run 165): in college the mood stays in the room, so a restless dressing room is news (about once in four weeks).
            if (human && college && today.DayNumber % 28 < 7
                && DressingRoomNote(team, wp, pl => stats.TryGetValue(pl.Id, out var x) && x.TeamId == team.Id ? x : null, finance, today, salaryScale) is { } note)
                events.Add(new(note.first, team, PeopleEventKind.DressingRoom, note.text));
        }
        if (promises != null) events.AddRange(CheckPromises(teamList.SelectMany(t => t.Roster), teamById, today, stats, promises));
        return events;
    }

    /// <summary>
    /// K9 (run 165): the dressing room's word when two or more of the rotation (the nine best) are unhappy: who, why, and
    /// what it costs (chemistry, and unhappy players play below themselves). Null when the room is calm.
    /// </summary>
    public static (Player first, string text)? DressingRoomNote(Team team, double winPct, Func<Player, PlayerSeasonStats?> statsOf, Finance finance, DateOnly today, double salaryScale = 1)
    {
        var rotation = team.Roster.OrderByDescending(x => x.CurrentAbility).ThenBy(x => x.Id).Take(9).ToHashSet();
        var sulk = Sulking(team).Where(rotation.Contains).ToList();
        if (sulk.Count < 2) return null;
        string Why(Player p)
        {
            var worst = Factors(p, team, winPct, statsOf(p), finance, today, salaryScale, college: true).OrderBy(x => x.effect).FirstOrDefault();
            if (worst.reason == null || worst.effect >= 0) return "";
            string r = worst.reason.Split(" (")[0]; // "A senior who wants his turn (6 a game; ...)" -> "a senior who wants his turn"
            return $" ({char.ToLowerInvariant(r[0])}{r[1..]})";
        }
        var named = sulk.Take(3).Select(p => p.Name + Why(p)).ToList();
        string who = named.Count == 2 ? $"{named[0]} and {named[1]}" : $"{named[0]}, {named[1]} and {named[2]}";
        string record = winPct >= 0.6 ? "Winning hasn't fixed it. " : "";
        return (sulk[0], $"The dressing room is restless: {who} {(sulk.Count <= 3 ? "are unhappy" : $"are among {sulk.Count} unhappy players")}. {record}"
            + $"It costs the team: chemistry is {team.Chemistry:0} and each unhappy player pulls it down, and an unhappy player plays below himself on the floor. "
            + "Talk to them on the Squad page, or change the rotation.");
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
                Boost(p, 10, today.AddDays(56), "Trusts you after a kept promise", today);
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
            p.MoodBoost = 0; p.BoostUntil = null; p.BoostReason = null;
        }
    }

    /// <summary>A player joining a new team gets a fresh start.</summary>
    public static void Joined(Player p) { p.Morale = Math.Max(p.Morale, 58); p.TradeRequested = false; p.UnhappyWeeks = 0; p.MoodBoost = 0; p.BoostUntil = null; p.BoostReason = null; }
}
