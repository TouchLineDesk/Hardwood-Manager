using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

public sealed record ScheduledGame(int Index, DateOnly Date, Team Home, Team Away);

/// <summary>
/// Builds an 82-game schedule for a two-conference league: 30 teams in six divisions of five (4 games vs each division rival,
/// 3 or 4 vs the rest of the conference, 2 vs each other-conference team), or 32 in eight divisions of four (#355:
/// 4 × 3 division rivals, 3 or 4 vs the other 12 in the conference, 2 × 16 against the other conference).
/// Dates are spread over ~170 days with no team playing twice on one day.
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

        // Assign dates: greedy, ~7 games per day, no team twice on a day, soft cap of 4 games per 7 days.
        var schedule = new List<ScheduledGame>();
        var remaining = new LinkedList<(Team home, Team away)>(pairs);
        var day = opening;
        var recent = teams.ToDictionary(t => t.Id, _ => new Queue<DateOnly>());
        int idx = 0;

        while (remaining.Count > 0)
        {
            var playedToday = new HashSet<int>();
            int gamesToday = 0;
            int target = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? 10 : 7;
            var node = remaining.First;
            while (node != null && gamesToday < target)
            {
                var next = node.Next;
                var (h, a) = node.Value;
                if (!playedToday.Contains(h.Id) && !playedToday.Contains(a.Id) && Rested(recent[h.Id], day) && Rested(recent[a.Id], day))
                {
                    schedule.Add(new ScheduledGame(idx++, day, h, a));
                    playedToday.Add(h.Id); playedToday.Add(a.Id);
                    Push(recent[h.Id], day); Push(recent[a.Id], day);
                    remaining.Remove(node);
                    gamesToday++;
                }
                node = next;
            }
            day = day.AddDays(1);
        }
        return schedule;

        static bool Rested(Queue<DateOnly> q, DateOnly d) => q.Count(x => d.DayNumber - x.DayNumber < 7) < 4;
        static void Push(Queue<DateOnly> q, DateOnly d) { q.Enqueue(d); while (q.Count > 6) q.Dequeue(); }
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
