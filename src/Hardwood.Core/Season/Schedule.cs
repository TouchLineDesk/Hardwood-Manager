using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

public sealed record ScheduledGame(int Index, DateOnly Date, Team Home, Team Away);

/// <summary>
/// Builds an 82-game schedule for a two-conference league: 30 teams in six divisions of five (4 games vs each division rival,
/// 3 or 4 vs the rest of the conference, 2 vs each other-conference team), or 32 in eight divisions of four (#355:
/// 4 × 3 division rivals, 3 or 4 vs the other 12 in the conference, 2 × 16 against the other conference).
/// Dates run from opening night to the 174th day (Apr 12) with no team twice on one day, never three days running, and about
/// 14 back-to-backs a team (H78, <see cref="AssignDates"/>).
/// </summary>
public static class ScheduleBuilder
{
    public static List<ScheduledGame> Build(League league, DateOnly opening, Rng rng)
    {
        var teams = league.Teams;
        var pairs = new List<(Team home, Team away)>();

        foreach (var a in teams)
        foreach (var b in teams)
        {
            if (a.Id >= b.Id) continue;
            int games;
            if (a.Conference != b.Conference) games = 2;
            else if (a.Division == b.Division) games = 4;
            else games = 3; // 10 non-division conference opponents × 3 = 30, + 4 of them get a 4th game below
            for (int g = 0; g < games; g++)
                pairs.Add(g % 2 == 0 ? (a, b) : (b, a));
        }

        // Top up to 82 with extra conference games against teams outside the division.
        foreach (var conf in new[] { Conference.East, Conference.West })
        {
            var divisions = teams.Where(t => t.Conference == conf)
                .GroupBy(t => t.Division).OrderBy(g => g.Key)
                .Select(g => g.OrderBy(t => t.Id).ToList()).ToList();
            int size = divisions.Count > 0 ? divisions[0].Count : 0;
            if (divisions.Count % 2 == 0 && divisions.Count > 0 && divisions.All(d => d.Count == size))
            {
                // Four divisions of four (Danny's D33 design, #344, #355): 12 division games, 36 against the other 12 conference
                // teams, 32 against the other conference, so 2 more: perfect matchings between division pairs (round r of a
                // round robin of the divisions; offset o within the pair), one extra game each.
                int confSize = divisions.Count * size, others = teams.Count - confSize;
                int extra = 82 - (4 * (size - 1) + 3 * (confSize - size) + 2 * others);
                int n = divisions.Count, rounds = n - 1;
                for (int m = 0; m < extra && m < rounds * size; m++)
                {
                    int r = m % rounds, o = m / rounds;
                    var order = Enumerable.Range(0, n).ToList();
                    for (int i = 0; i < r; i++) { order.Insert(1, order[^1]); order.RemoveAt(order.Count - 1); }
                    for (int i = 0; i < n / 2; i++)
                    {
                        var A = divisions[order[i]]; var B = divisions[order[n - 1 - i]];
                        for (int j = 0; j < size; j++)
                        {
                            var a = A[j]; var b = B[(j + o) % size];
                            pairs.Add(rng.Chance(0.5) ? (a, b) : (b, a));
                        }
                    }
                }
                continue;
            }
            // Three divisions of five (the NBA's real layout): each team needs 6 extra conference games (a 4th game vs 6 of its
            // 10 non-division rivals). For each pair of divisions (A,B), team i of A plays team (i+k) mod 5 of B for k = 0..2.
            // Any other shape (uneven divisions, e.g. the real 30 in D33's divisions of 3 and 4) is topped up below (K4).
            if (divisions.Count != 3 || divisions.Any(d => d.Count != 5)) continue;
            for (int dA = 0; dA < divisions.Count; dA++)
            for (int dB = dA + 1; dB < divisions.Count; dB++)
            {
                var A = divisions[dA]; var B = divisions[dB];
                for (int i = 0; i < A.Count; i++)
                for (int k = 0; k < 3; k++)
                {
                    var a = A[i]; var b = B[(i + k) % B.Count];
                    pairs.Add(rng.Chance(0.5) ? (a, b) : (b, a));
                }
            }
        }

        TopUp(pairs, teams, rng);
        BalanceHomeGames(pairs, teams);
        rng.Shuffle(pairs);

        return AssignDates(pairs, teams, opening);
    }

    /// <summary>The regular season's length: opening night to the last day (Oct 21 → Apr 12, as in the real NBA).</summary>
    public const int SeasonDays = 174;
    /// <summary>Back-to-backs a team may be given while the season has room (the real NBA averages about 14–15).</summary>
    public const int BackToBackCap = 16;
    /// <summary>The pace of back-to-backs a team is given without the day needing it (the real NBA's 2024-25 average).</summary>
    public const double BackToBackPace = 14;

    /// <summary>
    /// H78: dates for the games, in order. The real NBA never plays a team three days running and gives each about 14
    /// back-to-backs; the old greedy pass only capped 4 games in 7 days, so teams played ~37 back-to-backs, ~14 three-in-threes
    /// and ~3–4 four-in-fours a season. Rules here:
    /// <list type="bullet">
    /// <item>never on a third straight day, never 4 games in 5 days, and at most 4 in 7 (hard);</item>
    /// <item>a back-to-back only once rested games are used up for the day, and at most <see cref="BackToBackCap"/> a team
    /// while the season has room;</item>
    /// <item>each day's slate is the games left spread over the days left to the season's end (weekends carry more), with the
    /// teams furthest behind picked first, so the season fills its full length and ends together;</item>
    /// <item>no games in the All-Star break (Feb 13–17).</item>
    /// </list>
    /// No random numbers are drawn here, so the RNG stream is unchanged; only the dates differ. If the days run out, the cap on
    /// back-to-backs lifts (the hard rules stay) until every game is placed.
    /// </summary>
    internal static List<ScheduledGame> AssignDates(List<(Team home, Team away)> pairs, List<Team> teams, DateOnly opening)
    {
        var schedule = new List<ScheduledGame>();
        var remaining = new List<(Team home, Team away)>(pairs);
        // Apr 12 of the next year (the 174th day from Oct 21, or the 175th across a Feb 29); an unusual opening keeps 174 days.
        var last = new DateOnly(opening.Year + 1, 4, 12);
        if (last.DayNumber - opening.DayNumber < 150) last = opening.AddDays(SeasonDays - 1);
        var left = teams.ToDictionary(t => t.Id, _ => 0);
        foreach (var (h, a) in pairs) { left[h.Id]++; left[a.Id]++; }
        var played = teams.ToDictionary(t => t.Id, _ => new List<int>());   // day numbers, ascending
        var backToBacks = teams.ToDictionary(t => t.Id, _ => 0);
        int idx = 0;
        var day = opening;

        while (remaining.Count > 0)
        {
            if (AllStarBreak(day)) { day = day.AddDays(1); continue; }
            bool overtime = day > last;
            int target;
            if (overtime) target = teams.Count / 2;
            else
            {
                double weightLeft = 0;
                for (var d = day; d <= last; d = d.AddDays(1)) if (!AllStarBreak(d)) weightLeft += Weight(d);
                target = (int)Math.Ceiling(remaining.Count * Weight(day) / weightLeft);
            }

            int dn = day.DayNumber;
            var playedToday = new HashSet<int>();
            int gamesToday = 0;
            // Teams furthest behind first; the stable sort keeps the shuffled order between equals. (Indices, not pairs: the
            // same home-and-away pairing can appear twice.)
            var order = Enumerable.Range(0, remaining.Count)
                .OrderByDescending(i => left[remaining[i].home.Id] + left[remaining[i].away.Id]).ThenBy(i => i).ToList();
            var placed = new HashSet<int>();
            for (int pass = 0; pass < 2 && gamesToday < target; pass++)
                foreach (int i in order)
                {
                    if (gamesToday >= target) break;
                    var (h, a) = remaining[i];
                    if (placed.Contains(i) || playedToday.Contains(h.Id) || playedToday.Contains(a.Id)) continue;
                    if (!CanPlay(h.Id, pass) || !CanPlay(a.Id, pass)) continue;
                    foreach (var id in new[] { h.Id, a.Id })
                    {
                        if (Yesterday(id)) backToBacks[id]++;
                        played[id].Add(dn); left[id]--;
                        playedToday.Add(id);
                    }
                    schedule.Add(new ScheduledGame(idx++, day, h, a));
                    placed.Add(i);
                    gamesToday++;
                }
            if (placed.Count > 0)
            {
                int k = 0;
                remaining = remaining.Where(_ => !placed.Contains(k++)).ToList();
            }
            day = day.AddDays(1);

            bool Yesterday(int id) => played[id].Count > 0 && played[id][^1] == dn - 1;
            bool CanPlay(int id, int pass)
            {
                var p = played[id];
                int n = p.Count;
                if (n >= 2 && p[^1] == dn - 1 && p[^2] == dn - 2) return false;     // a third straight day
                if (n >= 3 && dn - p[^3] < 5) return false;                         // 4 games in 5 days
                if (n >= 4 && dn - p[^4] < 7) return false;                         // 5 games in 7 days
                if (!Yesterday(id)) return true;
                if (overtime) return pass == 1;
                if (backToBacks[id] >= BackToBackCap) return false;
                // Pass 0 takes a back-to-back while the team is behind the real league's pace (about 14 in 82 games), so
                // they are spread through the season instead of piling up where the slate is full; pass 1 fills the day.
                return pass == 1 || backToBacks[id] < BackToBackPace * n / (double)(n + left[id]);
            }
        }
        return schedule;

        // The real NBA's week: Wednesday and Friday are the big nights, Thursday is light (TV doubleheaders), and the
        // weekend is busy. Heavy nights next to each other are where back-to-backs come from (about 14 a team here).
        static double Weight(DateOnly d) => d.DayOfWeek switch
        {
            DayOfWeek.Monday => 0.9, DayOfWeek.Tuesday => 0.9, DayOfWeek.Wednesday => 1.4, DayOfWeek.Thursday => 0.5,
            DayOfWeek.Friday => 1.4, DayOfWeek.Saturday => 1.2, _ => 1.0,
        };
        static bool AllStarBreak(DateOnly d) => d.Month == 2 && d.Day >= 13 && d.Day <= 17;
    }

    /// <summary>
    /// Any league shape the fixed patterns above don't cover (K4: uneven divisions, an odd-sized conference) gets its extra games
    /// here: the team furthest short of 82 plays the conference rival (outside its division first) it has had the fewest extra
    /// games against and who is also short, then the other conference, then anyone short. A standard shape has no one short, so
    /// nothing is drawn from <paramref name="rng"/> and existing schedules are unchanged. If the shortfall is odd, one team plays 81.
    /// </summary>
    internal static void TopUp(List<(Team home, Team away)> pairs, List<Team> teams, Rng rng, int games = 82)
    {
        var count = teams.ToDictionary(t => t.Id, _ => 0);
        foreach (var (h, a) in pairs) { count[h.Id]++; count[a.Id]++; }
        if (teams.All(t => count[t.Id] >= games)) return;
        var extra = new Dictionary<(int, int), int>();
        int Extra(Team a, Team b) => extra.GetValueOrDefault(a.Id < b.Id ? (a.Id, b.Id) : (b.Id, a.Id));
        while (true)
        {
            var t = teams.Where(x => count[x.Id] < games).OrderBy(x => count[x.Id]).ThenBy(x => x.Id).FirstOrDefault();
            if (t == null) break;
            var short_ = teams.Where(x => x != t && count[x.Id] < games).ToList();
            var opp = short_.Where(x => x.Conference == t.Conference && x.Division != t.Division)
                          .OrderBy(x => Extra(t, x)).ThenBy(x => count[x.Id]).ThenBy(x => x.Id).FirstOrDefault()
                   ?? short_.Where(x => x.Conference != t.Conference)
                          .OrderBy(x => Extra(t, x)).ThenBy(x => count[x.Id]).ThenBy(x => x.Id).FirstOrDefault()
                   ?? short_.OrderBy(x => Extra(t, x)).ThenBy(x => count[x.Id]).ThenBy(x => x.Id).FirstOrDefault();
            if (opp == null) break; // an odd total: this team plays one game fewer
            pairs.Add(rng.Chance(0.5) ? (t, opp) : (opp, t));
            count[t.Id]++; count[opp.Id]++;
            var key = t.Id < opp.Id ? (t.Id, opp.Id) : (opp.Id, t.Id);
            extra[key] = extra.GetValueOrDefault(key) + 1;
        }
    }

    /// <summary>
    /// Flip home/away on individual games until every team hosts half its games (41 of 82; with an odd count, the half rounded
    /// either way). Uses augmenting paths: a surplus team hands a home game to an opponent, who may hand one on, until a team
    /// with room is reached; then any team still short takes one back the same way. Such an orientation always exists, so this
    /// never gives up; if it somehow did, the schedule keeps its imbalance rather than stopping the season (K4).
    /// </summary>
    internal static void BalanceHomeGames(List<(Team home, Team away)> pairs, List<Team> teams)
    {
        var games = teams.ToDictionary(t => t.Id, _ => 0);
        var homes = teams.ToDictionary(t => t.Id, _ => 0);
        foreach (var (h, a) in pairs) { homes[h.Id]++; games[h.Id]++; games[a.Id]++; }
        int Lo(int id) => games[id] / 2;
        int Hi(int id) => (games[id] + 1) / 2;

        // Too many home games: push them along games the team hosts until someone with room takes one.
        while (teams.FirstOrDefault(t => homes[t.Id] > Hi(t.Id)) is { } over)
        {
            int reached = Augment(over.Id, fromHome: true, v => homes[v] < Hi(v));
            if (reached < 0) return;
            homes[over.Id]--; homes[reached]++;
        }
        // Too few: pull one along games the team plays away, from someone who can spare one.
        while (teams.FirstOrDefault(t => homes[t.Id] < Lo(t.Id)) is { } under)
        {
            int reached = Augment(under.Id, fromHome: false, v => homes[v] > Lo(v));
            if (reached < 0) return;
            homes[under.Id]++; homes[reached]--;
        }

        // BFS from `start` along games it hosts (fromHome) or plays away; flips the path to the first team that `done`, and returns it.
        int Augment(int start, bool fromHome, Func<int, bool> done)
        {
            var prevGame = new Dictionary<int, int> { [start] = -1 };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            int reached = -1;
            while (queue.Count > 0 && reached < 0)
            {
                int u = queue.Dequeue();
                for (int i = 0; i < pairs.Count; i++)
                {
                    int me = fromHome ? pairs[i].home.Id : pairs[i].away.Id;
                    if (me != u) continue;
                    int v = fromHome ? pairs[i].away.Id : pairs[i].home.Id;
                    if (prevGame.ContainsKey(v)) continue;
                    prevGame[v] = i;
                    if (done(v)) { reached = v; break; }
                    queue.Enqueue(v);
                }
            }
            if (reached < 0) return -1;
            int cur = reached;
            while (prevGame[cur] >= 0)
            {
                int gi = prevGame[cur];
                var (h, a) = pairs[gi];
                pairs[gi] = (a, h);
                cur = fromHome ? h.Id : a.Id;
            }
            return reached;
        }
    }
}
