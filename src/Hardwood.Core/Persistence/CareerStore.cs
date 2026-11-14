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
        var clubs = s.League.Teams.Concat(s.World.Clubs).DistinctBy(t => t.Id).ToList();
        Put("owners", SaveFile.ToJson(clubs.ToDictionary(t => t.Id, t => t.Owner)));
        Put("coaches", SaveFile.ToJson(clubs.ToDictionary(t => t.Id, t => t.CoachName)));
        Put("money", SaveFile.ToJson(clubs.ToDictionary(t => t.Id, t => t.Money)));
        Put("transfers", SaveFile.ToJson(new TransferState(s.TransferLog, s.IncomingBids, s.AgreedFees, s.MyBids)));
        Put("digest", SaveFile.ToJson(new DigestState(s.WorldDigest, s.LastDigest, s.WeekResults, s.WeekKnocks)));
        Put("inbox", SaveFile.ToJson(s.InboxSettings));
        Put("head_to_head", SaveFile.ToJson(s.HeadToHeadBook));
        if (s.NamePackApplied is { } pack) Put("name_pack", pack);
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
        var clubs = s.League.Teams.Concat(s.World.Clubs).DistinctBy(t => t.Id).ToList();
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
        foreach (var t in clubs) t.Coach = null;
        if (s.Employed) { s.HumanTeam.Coach = s.Coach; s.HumanTeam.CoachName = s.Coach.Name; }
        s.World.ManagedClubId = s.Employed && s.Abroad ? s.HumanTeam.Id : null;
    }

    private sealed record TransferState(List<TransferRecord>? Log, List<IncomingBid>? Incoming, Dictionary<int, long>? Agreed, List<MyBid>? Mine = null);

    private static void CreateSchema(SqliteConnection conn) =>
        SaveFile.Exec(conn, "CREATE TABLE IF NOT EXISTS career_meta(key TEXT PRIMARY KEY, value TEXT);");
}

/// <summary>World headlines waiting for the weekly digest.</summary>
public sealed record DigestState(List<DigestItem>? Items, DateOnly Last, List<string>? Results = null, List<string>? Knocks = null);
