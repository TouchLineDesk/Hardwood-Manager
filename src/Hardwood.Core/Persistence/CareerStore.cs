using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Jobs;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;

namespace Hardwood.Core.Persistence;

/// <summary>
/// Save format 9: your coach, every club's owner and computer coach, and your job (board confidence, expectations,
/// open jobs). Saves from before format 9 get a default coach, generated owners and a fresh job at their club.
/// </summary>
public static class CareerStore
{
    public static void SaveCareer(this SaveFile file, GameSession s)
    {
        using var conn = file.OpenConnection();
        CreateSchema(conn);
        using var tx = conn.BeginTransaction();
        SaveFile.Exec(conn, "DELETE FROM career_meta;");
        void Put(string k, string v) => SaveFile.Exec(conn, "INSERT INTO career_meta(key, value) VALUES($k, $v)", ("$k", k), ("$v", v));
        Put("coach", SaveFile.ToJson(s.Coach));
        Put("job", SaveFile.ToJson(s.Job));
        var clubs = AllClubs(s);
        Put("owners", SaveFile.ToJson(clubs.ToDictionary(t => t.Id, t => t.Owner)));
        Put("coaches", SaveFile.ToJson(clubs.ToDictionary(t => t.Id, t => t.CoachName)));
        Put("money", SaveFile.ToJson(clubs.ToDictionary(t => t.Id, t => t.Money)));
        Put("transfers", SaveFile.ToJson(new TransferState(s.TransferLog, s.IncomingBids, s.AgreedFees, s.MyBids)));
        Put("digest", SaveFile.ToJson(new DigestState(s.WorldDigest, s.LastDigest, s.WeekResults, s.WeekKnocks)));
        Put("inbox", SaveFile.ToJson(s.InboxSettings));
        Put("head_to_head", SaveFile.ToJson(s.HeadToHeadBook));
        if (s.NamePackApplied is { } pack) Put("name_pack", pack);
        // Bugs #18 and #19 (run 55): the box scores of your college games and your games below the NBA lived only in the
        // session, so after a reload their schedule rows had a score and no box. They are kept here now (at most 200 each).
        Put("side_boxes", SaveFile.ToJson(
            s.CollegeResults.Select(kv => SideBox.From("college", kv.Key, kv.Value))
             .Concat(s.AbroadResults.Select(kv => SideBox.From("abroad", kv.Key, kv.Value))).ToList()));
        tx.Commit();
    }

    public static void LoadCareer(this SaveFile file, GameSession s)
    {
        using var conn = file.OpenConnection();
        CreateSchema(conn);
        string? Get(string key)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM career_meta WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
        var clubs = AllClubs(s);
        if (Get("owners") is { } oj && SaveFile.FromJson<Dictionary<int, Owner>>(oj) is { } owners)
            foreach (var t in clubs) if (owners.TryGetValue(t.Id, out var o)) t.Owner = o;
        if (Get("coaches") is { } cj && SaveFile.FromJson<Dictionary<int, string>>(cj) is { } names)
            foreach (var t in clubs) if (names.TryGetValue(t.Id, out var n)) t.CoachName = n;
        if (Get("money") is { } mj && SaveFile.FromJson<Dictionary<int, ClubFinance>>(mj) is { } money)
            foreach (var t in clubs) if (money.TryGetValue(t.Id, out var m) && m != null) t.Money = m;
        if (Get("transfers") is { } tj && SaveFile.FromJson<TransferState>(tj) is { } ts)
        {
            s.TransferLog.Clear(); s.TransferLog.AddRange(ts.Log ?? new());
            s.IncomingBids.Clear(); s.IncomingBids.AddRange(ts.Incoming ?? new());
            s.AgreedFees.Clear(); foreach (var (k, v) in ts.Agreed ?? new()) s.AgreedFees[k] = v;
            s.MyBids.Clear(); s.MyBids.AddRange(ts.Mine ?? new());
        }
        if (Get("digest") is { } dj && SaveFile.FromJson<DigestState>(dj) is { } ds)
        {
            s.WorldDigest.Clear(); s.WorldDigest.AddRange(ds.Items ?? new()); s.LastDigest = ds.Last;
            s.WeekResults.Clear(); s.WeekResults.AddRange(ds.Results ?? new()); s.WeekKnocks.Clear(); s.WeekKnocks.AddRange(ds.Knocks ?? new());
        }
                if (Get("coach") is { } coj && SaveFile.FromJson<Coach>(coj) is { } coach) s.Coach = coach;
        if (Get("job") is { } jj && SaveFile.FromJson<JobState>(jj) is { } job) s.Job = job;
        s.NamePackApplied = Get("name_pack");
        // #529: a save from before v0.79.0 has no book; it starts counting from this season.
        s.HeadToHeadBook = Get("head_to_head") is { } hj && SaveFile.FromJson<HeadToHeadBook>(hj) is { } book ? book : new HeadToHeadBook();
        s.InboxSettings = Get("inbox") is { } ij && SaveFile.FromJson<InboxSettings>(ij) is { } inbox ? inbox : new InboxSettings();
        if (Get("side_boxes") is { } sb && SaveFile.FromJson<List<SideBox>>(sb) is { } sides)
        {
            var byId = clubs.ToDictionary(t => t.Id);
            var players = new Dictionary<int, Player>();
            foreach (var p in clubs.SelectMany(t => t.Roster).Concat(s.League.FreeAgents).Concat(s.League.Retired)
                         .Concat(s.World.College?.Prospects ?? Enumerable.Empty<Player>()))
                players.TryAdd(p.Id, p);
            s.CollegeResults.Clear(); s.AbroadResults.Clear();
            foreach (var box in sides)
            {
                // v0.79.3 keyed a game below the NBA by competition and fixture number only ("usa5:12"), which repeats
                // every season (run 56). Such a box is re-keyed only if it is this season's game with the same clubs
                // and score; otherwise it may belong to an earlier season, and is dropped rather than shown for the wrong game.
                string key = box.Key;
                if (box.Kind == "abroad" && key.Split(':').Length == 2)
                {
                    var hit = s.World.FixturesOf(box.HomeId).FirstOrDefault(x => $"{x.comp.Key}:{x.fixture.Index}" == key
                        && x.fixture.HomeId == box.HomeId && x.fixture.AwayId == box.AwayId
                        && x.fixture.HomePts == box.HomePts && x.fixture.AwayPts == box.AwayPts);
                    if (hit.fixture == null) continue;
                    key = GameSession.AbroadKey(hit.comp.Key, hit.fixture);
                }
                if (box.ToResult(byId, players) is { } r) (box.Kind == "college" ? s.CollegeResults : s.AbroadResults)[key] = r;
            }
            GameSession.KeepNewest(s.CollegeResults); GameSession.KeepNewest(s.AbroadResults);
        }
        foreach (var t in clubs) t.Coach = null;
        if (s.Employed) { s.HumanTeam.Coach = s.Coach; s.HumanTeam.CoachName = s.Coach.Name; }
        s.World.ManagedClubId = s.Employed && s.Abroad ? s.HumanTeam.Id : null;
    }

    /// <summary>Every club whose owner, coach and money the career keeps: the top league, the world's leagues and,
    /// since v0.79.3 (bug #17), the college programs (a college career's balance used to reset on every reload).</summary>
    internal static List<Team> AllClubs(GameSession s) =>
        s.League.Teams.Concat(s.World.Clubs).Concat(s.World.College?.Programs ?? Enumerable.Empty<Team>()).DistinctBy(t => t.Id).ToList();

    /// <summary>A saved box score of one of your games outside the NBA's schedule table (college, or tiers 2-5 and abroad).</summary>
    internal sealed record SideBox(string Kind, string Key, string? Date, int HomeId, int AwayId, int HomePts, int AwayPts, int Overtimes,
                                   int[]? HomePeriods, int[]? AwayPeriods, List<int[]>? Lines)
    {
        // One line: team, player, started, seconds x10, pts, fgm, fga, tpm, tpa, ftm, fta, oreb, dreb, ast, stl, blk, tov, pf, pm.
        public static SideBox From(string kind, string key, GameResult r) => new(kind, key, r.Date?.ToString("yyyy-MM-dd"),
            r.Home.Team.Id, r.Away.Team.Id, r.Home.Points, r.Away.Points, r.Overtimes, r.Home.PointsByPeriod, r.Away.PointsByPeriod,
            new[] { r.Home, r.Away }.SelectMany(b => b.Lines.Where(l => l.Seconds > 0).Select(l => new[]
            {
                b.Team.Id, l.Player.Id, l.Started ? 1 : 0, (int)Math.Round(l.Seconds * 10), l.Points, l.FGM, l.FGA, l.ThreePM, l.ThreePA, l.FTM, l.FTA,
                l.OReb, l.DReb, l.Assists, l.Steals, l.Blocks, l.Turnovers, l.Fouls, l.PlusMinus,
            })).ToList());

        public GameResult? ToResult(Dictionary<int, Team> teams, Dictionary<int, Player> players)
        {
            if (!teams.TryGetValue(HomeId, out var h) || !teams.TryGetValue(AwayId, out var a)) return null;
            var home = new TeamBox { Team = h, PointsByPeriod = HomePeriods ?? Array.Empty<int>() };
            var away = new TeamBox { Team = a, PointsByPeriod = AwayPeriods ?? Array.Empty<int>() };
            foreach (var v in Lines ?? new())
            {
                if (v.Length < 19 || !players.TryGetValue(v[1], out var p)) continue;
                var line = new PlayerLine
                {
                    Player = p, Started = v[2] == 1, Seconds = v[3] / 10.0, Points = v[4], FGM = v[5], FGA = v[6], ThreePM = v[7], ThreePA = v[8],
                    FTM = v[9], FTA = v[10], OReb = v[11], DReb = v[12], Assists = v[13], Steals = v[14], Blocks = v[15], Turnovers = v[16], Fouls = v[17], PlusMinus = v[18],
                };
                (v[0] == HomeId ? home : away).Lines.Add(line);
            }
            // The score stands as played even if a player has since left the game world (a graduate, say).
            if (home.Points != HomePts) home.FixedPoints = HomePts;
            if (away.Points != AwayPts) away.FixedPoints = AwayPts;
            return new GameResult { Home = home, Away = away, Overtimes = Overtimes, Date = Date is { } d ? DateOnly.Parse(d) : null };
        }
    }

    private sealed record TransferState(List<TransferRecord>? Log, List<IncomingBid>? Incoming, Dictionary<int, long>? Agreed, List<MyBid>? Mine = null);

    private static void CreateSchema(SqliteConnection conn) =>
        SaveFile.Exec(conn, "CREATE TABLE IF NOT EXISTS career_meta(key TEXT PRIMARY KEY, value TEXT);");
}

/// <summary>World headlines waiting for the weekly digest.</summary>
public sealed record DigestState(List<DigestItem>? Items, DateOnly Last, List<string>? Results = null, List<string>? Knocks = null);
