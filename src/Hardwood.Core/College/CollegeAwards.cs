using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;

namespace Hardwood.Core.College;

/// <summary>
/// CS5 / K12a (run 135, critic review #18 item 6): one college season's honours, voted the day the national champion is
/// crowned. Names and programs are kept with the ids, so a winner who has since turned pro (or left the game) is still named.
/// </summary>
public sealed class CollegeAwards
{
    public int Year { get; init; }
    public List<VoteLine> PlayerOfYear { get; init; } = new();
    public List<VoteLine> Defensive { get; init; } = new();
    public List<VoteLine> Freshman { get; init; } = new();
    public int? CoachProgramId { get; init; }
    public string CoachName { get; init; } = "";
    public string CoachNote { get; init; } = "";
    /// <summary>The Final Four's Most Outstanding Player (the champion's best in the semifinal and the final).</summary>
    public int? OutstandingId { get; init; }
    public string OutstandingLine { get; init; } = "";
    /// <summary>Three All-America teams of five player ids.</summary>
    public List<List<int>> AllAmerica { get; init; } = new();
    /// <summary>Each conference's first team (conference key → five player ids).</summary>
    public Dictionary<string, List<int>> AllConference { get; init; } = new();
    /// <summary>Each conference's player of the year (conference key → player id).</summary>
    public Dictionary<string, int> ConferencePlayer { get; init; } = new();
    /// <summary>Every honoured player's name and program then (id → name, id → program id).</summary>
    public Dictionary<int, string> Names { get; init; } = new();
    public Dictionary<int, int> ProgramOf { get; init; } = new();
    /// <summary>Every program's season: overall wins, losses and the March Madness round reached (-1 not in the field ... 7 champion),
    /// so History can show "You: 24-8 · a Sweet 16" for any past season.</summary>
    public Dictionary<int, int[]> Records { get; init; } = new();

    public int? PlayerOfYearId => PlayerOfYear.FirstOrDefault()?.PlayerId;
    public int? DefensiveId => Defensive.FirstOrDefault()?.PlayerId;
    public int? FreshmanId => Freshman.FirstOrDefault()?.PlayerId;
    public string NameOf(int id) => Names.TryGetValue(id, out var n) ? n : "-";

    public static string SeasonText(int year) => $"{year}-{(year + 1) % 100:00}";
}

public sealed partial class CollegeWorld
{
    /// <summary>CS5 (run 135): every season's college honours, oldest first.</summary>
    public List<CollegeAwards> Awards { get; } = new();

    /// <summary>A season needs this many games for a player to be on a ballot (about two thirds of a regular season).</summary>
    public const int AwardGames = 18;

    /// <summary>The college honours for this season, voted now (call once the champion is known). The votes use their own
    /// random stream seeded from the season, so the season's games and the world's other draws are untouched.</summary>
    public CollegeAwards VoteAwards()
    {
        var s = Season;
        var rng = new Rng(0xC5A3D5UL ^ ((ulong)s.Year * 0x9E3779B97F4A7C15UL));
        var players = Programs.SelectMany(t => t.Roster).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var field = s.Stats.Where(kv => kv.Value.G >= AwardGames && players.ContainsKey(kv.Key) && Program(kv.Value.TeamId) != null)
            .Select(kv => (Id: kv.Key, St: kv.Value)).ToList();
        var regular = Programs.ToDictionary(t => t.Id, t => RegularSeasonRecord(t.Id));
        double WinPct(int team) => regular[team] is var (w, l) && w + l > 0 ? w / (double)(w + l) : 0;
        int Wins(int team) => regular[team].wins;

        double Production(StatLine st)
        {
            if (st.G == 0) return 0;
            double per = (st.Pts + 1.2 * st.Reb + 1.5 * st.Ast + 1.8 * st.Stl + 1.8 * st.Blk) / st.G;
            double fg = st.Fga == 0 ? 0.45 : (st.Fgm + 0.5 * st.Tpm) / (double)st.Fga; // effective field-goal %
            return per * (1 + Math.Clamp((fg - 0.50) * 1.5, -0.15, 0.15));
        }
        double PlayerScore(StatLine st)
        {
            int rank = PollPosition(st.TeamId);
            double team = (WinPct(st.TeamId) - 0.5) * 14 + (rank <= 25 ? (26 - rank) * 0.18 : 0);
            return (Production(st) + team) * Math.Min(1.0, st.G / 25.0);
        }
        double DefenseScore(int id, StatLine st)
        {
            var p = players[id];
            double rating = (p.Attributes[Attr.PerimeterDefense] + p.Attributes[Attr.InteriorDefense] + p.Attributes[Attr.HelpDefense]) / 3.0;
            double box = (st.Stl * 2.0 + st.Blk * 2.5 + st.Reb * 0.3) / Math.Max(1, st.G);
            return (rating * 0.9 + box * 1.4 + (WinPct(st.TeamId) - 0.5) * 6) * Math.Min(1.0, st.Mpg / 26.0);
        }
        string Line(int id, StatLine st) => $"{st.Ppg:F1} pts, {st.Rpg:F1} reb, {st.Apg:F1} ast, {Wins(st.TeamId)} wins";

        List<VoteLine> Vote(IEnumerable<(int Id, StatLine St)> pool, Func<int, StatLine, double> score, Func<int, StatLine, string> line)
        {
            var cands = pool.Select(x => (x.Id, x.St, Score: score(x.Id, x.St))).Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score).ThenBy(x => x.Id).Take(10).ToList();
            if (cands.Count == 0) return new();
            int[] ballot = { 10, 7, 5, 3, 1 };
            var points = cands.ToDictionary(x => x.Id, _ => 0.0);
            for (int v = 0; v < 100; v++)
            {
                var seen = cands.Select(x => (x.Id, Noisy: x.Score * (1 + rng.NextGaussian(0, 0.08)))).OrderByDescending(x => x.Noisy).Take(5).ToList();
                for (int i = 0; i < seen.Count; i++) points[seen[i].Id] += ballot[i];
            }
            double total = points.Values.Sum();
            return cands.OrderByDescending(x => points[x.Id]).ThenBy(x => x.Id)
                .Where(x => points[x.Id] > 0)
                .Select(x => new VoteLine(x.Id, players[x.Id].Name, Program(x.St.TeamId)!.Abbreviation, points[x.Id], points[x.Id] / total, line(x.Id, x.St)))
                .ToList();
        }

        var poy = Vote(field, (_, st) => PlayerScore(st), Line);
        var dpoy = Vote(field, DefenseScore, (id, st) => $"{(double)st.Stl / st.G:F1} stl, {(double)st.Blk / st.G:F1} blk, {st.Rpg:F1} reb, {st.Mpg:F0} min");
        var freshmen = field.Where(x => ClassYearOf(players[x.Id], s.Year)?.Year == 1);
        var fresh = Vote(freshmen, (_, st) => PlayerScore(st), Line);

        // Coach of the Year: the biggest climb from where the roster said the program would finish, with a nod to March.
        var expected = Programs.Select(t => (t.Id, Core: t.Roster.OrderByDescending(p => p.CurrentAbility).Take(8).Select(p => (double)p.CurrentAbility).DefaultIfEmpty(0).Average()))
            .OrderByDescending(x => x.Core).ThenBy(x => x.Id).Select((x, i) => (x.Id, Rank: i + 1)).ToDictionary(x => x.Id, x => x.Rank);
        var coy = Programs.Select(t =>
            {
                int actual = PollPosition(t.Id);
                int reached = MadnessReached(t.Id);
                double score = Math.Min(60, expected[t.Id] - actual) * 1.0 + Math.Max(0, reached) * 3 + (WinPct(t.Id) - 0.5) * 20;
                return (t, actual, reached, score: actual <= 30 ? score : double.MinValue);
            })
            .OrderByDescending(x => x.score).ThenBy(x => x.t.Id).First();
        string coyNote = $"Picked {CollegeAwardsText.Ordinal(expected[coy.t.Id])} on talent, finished No. {coy.actual} at {Wins(coy.t.Id)}-{regular[coy.t.Id].losses}"
            + (coy.reached >= 1 ? $", {CollegeAwardsText.Reached(coy.reached)}" : "") + ".";

        // All-America: the fifteen best on the Player of the Year ballot, two guards, two forwards and a center a team.
        List<List<int>> Teams(IEnumerable<(int Id, StatLine St)> pool, int count)
        {
            var ranked = pool.OrderByDescending(x => PlayerScore(x.St)).ThenBy(x => x.Id).Take(count * 5 + 10).Select(x => players[x.Id]).ToList();
            var teams = new List<List<int>>();
            for (int k = 0; k < count && ranked.Count > 0; k++)
            {
                var five = new List<Player>();
                void Take(Func<Player, bool> pred, int n) { foreach (var p in ranked.Where(pred).Take(n).ToList()) { five.Add(p); ranked.Remove(p); } }
                Take(p => p.PrimaryPosition is Position.PG or Position.SG, 2);
                Take(p => p.PrimaryPosition is Position.SF or Position.PF, 2);
                Take(p => p.PrimaryPosition == Position.C, 1);
                Take(_ => true, 5 - five.Count);
                teams.Add(five.Select(p => p.Id).ToList());
            }
            return teams;
        }
        var allAmerica = Teams(field, 3);
        var allConference = new Dictionary<string, List<int>>();
        var confPlayer = new Dictionary<string, int>();
        foreach (var conf in Active)
        {
            var mine = field.Where(x => ConferenceOf.GetValueOrDefault(x.St.TeamId) == conf.Key).ToList();
            if (mine.Count == 0) continue;
            allConference[conf.Key] = Teams(mine, 1).FirstOrDefault() ?? new();
            confPlayer[conf.Key] = mine.OrderByDescending(x => PlayerScore(x.St)).ThenBy(x => x.Id).First().Id;
        }

        // The Final Four's Most Outstanding Player: the champion's leading scorer in both games if one man led both, else the
        // man who led them in the title game.
        int? mop = null; string mopLine = "";
        if (s.ChampionId is { } champ)
        {
            var games = s.Games.Where(g => g.Event == Madness && g.Round >= 5 && g.Played && (g.HomeId == champ || g.AwayId == champ))
                .OrderBy(g => g.Round).ToList();
            var stars = games.Select(g => g.HomeId == champ ? g.HomeStar : g.AwayStar).ToList();
            if (stars.Count > 0 && stars[^1] is { } last)
            {
                mop = last.PlayerId;
                var his = stars.Where(x => x?.PlayerId == last.PlayerId).Select(x => x!).ToList();
                mopLine = his.Count >= 2
                    ? $"Led the champions in both games: {string.Join(" and ", his.Select(x => x.Pts))} points"
                    : $"Led the champions with {last.Pts} points in the title game";
            }
        }

        var names = new Dictionary<int, string>(); var programOf = new Dictionary<int, int>();
        void Note(int id)
        {
            if (players.TryGetValue(id, out var p)) { names[id] = p.Name; if (p.TeamId is { } t) programOf[id] = t; }
        }
        foreach (var id in poy.Concat(dpoy).Concat(fresh).Select(v => v.PlayerId)
                     .Concat(allAmerica.SelectMany(x => x)).Concat(allConference.Values.SelectMany(x => x)).Concat(confPlayer.Values)
                     .Concat(mop is { } m ? new[] { m } : Array.Empty<int>()).Distinct())
            Note(id);

        return new CollegeAwards
        {
            Year = s.Year,
            PlayerOfYear = poy, Defensive = dpoy, Freshman = fresh,
            CoachProgramId = coy.t.Id,
            CoachName = coy.t.Coach?.Name is { Length: > 0 } cn ? cn : coy.t.CoachName,
            CoachNote = coyNote,
            OutstandingId = mop, OutstandingLine = mopLine,
            AllAmerica = allAmerica, AllConference = allConference, ConferencePlayer = confPlayer,
            Names = names, ProgramOf = programOf,
            Records = Programs.ToDictionary(t => t.Id, t => Record(t.Id) is var (w, l) ? new[] { w, l, MadnessReached(t.Id) } : new int[3]),
        };
    }

    /// <summary>A player's college honours, newest first ("National Player of the Year 2027-28", "All-America first team 2026-27").</summary>
    public List<string> HonoursOf(int playerId)
    {
        var list = new List<string>();
        foreach (var a in Awards.OrderByDescending(a => a.Year))
        {
            string y = CollegeAwards.SeasonText(a.Year);
            if (a.PlayerOfYearId == playerId) list.Add($"National Player of the Year {y}");
            if (a.OutstandingId == playerId) list.Add($"Most Outstanding Player {y}");
            if (a.DefensiveId == playerId) list.Add($"Defensive Player of the Year {y}");
            if (a.FreshmanId == playerId) list.Add($"Freshman of the Year {y}");
            int team = a.AllAmerica.FindIndex(t => t.Contains(playerId));
            if (team >= 0) list.Add($"All-America {CollegeAwardsText.TeamName(team)} {y}");
            foreach (var (key, id) in a.ConferencePlayer)
                if (id == playerId) list.Add($"{ConferenceShortName(key)} Player of the Year {y}");
            foreach (var (key, five) in a.AllConference)
                if (five.Contains(playerId) && a.ConferencePlayer.GetValueOrDefault(key) != playerId) list.Add($"All-{ConferenceShortName(key)} first team {y}");
        }
        return list;
    }

    /// <summary>A conference's short name by key ("ACC"), or the key itself.</summary>
    public static string ConferenceShortName(string key) => Conferences.FirstOrDefault(c => c.Key == key)?.Short ?? key;

    /// <summary>The awards' news story: the national winners in one line each.</summary>
    private (string, string) AwardsNews(CollegeAwards a)
    {
        string Who(int? id) => id is { } i ? $"{a.NameOf(i)} ({Program(a.ProgramOf.GetValueOrDefault(i))?.FullName ?? "-"})" : "-";
        var lines = new List<string>
        {
            $"National Player of the Year: {Who(a.PlayerOfYearId)}",
            $"Defensive Player of the Year: {Who(a.DefensiveId)}",
            $"Freshman of the Year: {Who(a.FreshmanId)}",
            $"Coach of the Year: {(a.CoachName.Length > 0 ? a.CoachName + ", " : "")}{Program(a.CoachProgramId ?? 0)?.FullName ?? "-"}",
        };
        if (a.OutstandingId != null) lines.Insert(1, $"Most Outstanding Player: {Who(a.OutstandingId)}");
        if (a.AllAmerica.Count > 0) lines.Add("All-America first team: " + string.Join(", ", a.AllAmerica[0].Select(i => a.NameOf(i))));
        return ($"{a.NameOf(a.PlayerOfYearId ?? 0)} is the National Player of the Year", string.Join("\n", lines) + ".");
    }
}

/// <summary>Wording shared by the college awards and their pages.</summary>
public static class CollegeAwardsText
{
    public static string TeamName(int i) => i switch { 0 => "first team", 1 => "second team", 2 => "third team", _ => $"team {i + 1}" };
    public static string Ordinal(int n) => AwardsEngine.Ordinal(n);
    public static string Reached(int round) => round switch
    {
        7 => "national champions",
        6 => "national runners-up",
        5 => "a Final Four",
        4 => "an Elite Eight",
        3 => "a Sweet 16",
        2 => "the Round of 32",
        _ => "a tournament bid",
    };
}
