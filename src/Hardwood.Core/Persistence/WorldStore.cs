using Hardwood.Core.Model;
using Hardwood.Core.World;
using Microsoft.Data.Sqlite;

namespace Hardwood.Core.Persistence;

/// <summary>
/// The world beyond the NABL: clubs (world_team), each competition's settings, season and history (world_comp, JSON),
/// and the world's random stream (world_meta). Players are in the shared player table with status 'overseas'.
/// </summary>
public static class WorldStore
{
    public static void SaveWorld(this SaveFile file, GameWorld world)
    {
        using var conn = file.OpenConnection();
        CreateSchema(conn);
        using var tx = conn.BeginTransaction();
        SaveFile.Exec(conn, "DELETE FROM world_meta; DELETE FROM world_comp; DELETE FROM world_team;");
        SaveFile.Exec(conn, "INSERT INTO world_meta(key, value) VALUES('rng_seed', $v)", ("$v", ((long)world.NextRngSeed()).ToString()));
        SaveFile.Exec(conn, "INSERT INTO world_meta(key, value) VALUES('summer_started', $v)", ("$v", world.SummerStarted.ToString()));
        SaveFile.Exec(conn, "INSERT INTO world_meta(key, value) VALUES('summer_ended', $v)", ("$v", world.SummerEnded.ToString()));
        SaveFile.Exec(conn, "INSERT INTO world_meta(key, value) VALUES('departed', $v)", ("$v", SaveFile.ToJson(world.Departed)));
        SaveFile.Exec(conn, "INSERT INTO world_meta(key, value) VALUES('awaiting', $v)",
            ("$v", SaveFile.ToJson(world.AwaitingDecision.ToDictionary(kv => kv.Key, kv => kv.Value.Select(p => p.Id).ToList()))));
        int order = 0;
        foreach (var c in world.Competitions)
        {
            SaveFile.Exec(conn, "INSERT INTO world_comp(key, ord, full_sim, season_json, history_json) VALUES($k, $o, $f, $s, $h)",
                ("$k", c.Key), ("$o", order++), ("$f", c.FullSim ? 1 : 0), ("$s", SaveFile.ToJson(c.Season)), ("$h", SaveFile.ToJson(c.History)));
            foreach (var t in c.Clubs)
                SaveFile.Exec(conn, """
                    INSERT INTO world_team(id, comp_key, city, name, abbr, capacity, primary_color, secondary_color, logo_style, tactic_json)
                    VALUES($id, $k, $city, $name, $abbr, $cap, $pc, $sc, $ls, $tac)
                    """,
                    ("$id", t.Id), ("$k", c.Key), ("$city", t.City), ("$name", t.Name), ("$abbr", t.Abbreviation), ("$cap", t.ArenaCapacity),
                    ("$pc", t.PrimaryColor), ("$sc", t.SecondaryColor), ("$ls", t.LogoStyle), ("$tac", SaveFile.ToJson(t.Tactic)));
        }
        tx.Commit();
    }

    /// <summary>Rebuild the world from a save, or null if the save predates it (the session then generates one).</summary>
    public static GameWorld? LoadWorld(this SaveFile file, League nabl, IEnumerable<Player> overseasPlayers)
    {
        using var conn = file.OpenConnection();
        CreateSchema(conn);
        ulong seed;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM world_meta WHERE key = 'rng_seed'";
            if (cmd.ExecuteScalar() is not string v) return null;
            seed = (ulong)long.Parse(v);
        }

        var world = new GameWorld(nabl, seed);
        var comps = new Dictionary<string, WorldCompetition>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT key, full_sim, season_json, history_json FROM world_comp ORDER BY ord";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var key = r.GetString(0);
                var profile = Competitions.All.FirstOrDefault(p => p.Key == key);
                if (profile == null) continue;
                var c = new WorldCompetition { Profile = profile, FullSim = r.GetInt32(1) == 1, Season = SaveFile.FromJson<CompetitionSeason>(r.GetString(2)) ?? new() };
                c.History.AddRange(SaveFile.FromJson<List<ChampionRecord>>(r.GetString(3)) ?? new());
                comps[key] = c;
                world.Competitions.Add(c);
            }
        }

        var teams = new Dictionary<int, Team>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, comp_key, city, name, abbr, capacity, primary_color, secondary_color, logo_style, tactic_json FROM world_team ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!comps.TryGetValue(r.GetString(1), out var c)) continue;
                var t = new Team
                {
                    Id = r.GetInt32(0), City = r.GetString(2), Name = r.GetString(3), Abbreviation = r.GetString(4), Conference = Conference.East,
                    Division = c.Profile.Short, ArenaCapacity = r.GetInt32(5), PrimaryColor = r.GetString(6), SecondaryColor = r.GetString(7),
                    LogoStyle = r.GetInt32(8), Tactic = SaveFile.FromJson<Tactic>(r.GetString(9)) ?? new(),
                };
                c.Clubs.Add(t);
                teams[t.Id] = t;
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM world_meta WHERE key = 'summer_started'";
            if (cmd.ExecuteScalar() is string v && int.TryParse(v, out var y)) world.SummerStarted = y;
            cmd.CommandText = "SELECT value FROM world_meta WHERE key = 'summer_ended'";
            if (cmd.ExecuteScalar() is string e && int.TryParse(e, out var ye)) world.SummerEnded = ye;
            cmd.CommandText = "SELECT value FROM world_meta WHERE key = 'departed'";
            if (cmd.ExecuteScalar() is string dj && SaveFile.FromJson<Dictionary<int, List<GameWorld.Departure>>>(dj) is { } departed)
                foreach (var (k, list) in departed) world.Departed[k] = list;
        }
        var asOf = new DateOnly(nabl.Season, 10, 1);
        foreach (var p in overseasPlayers)
            if (p.TeamId is { } id && teams.TryGetValue(id, out var t))
            {
                t.Roster.Add(p);
                // Saves from before format 6 have no buyout clauses: write them in now.
                if (p.Contract is { Buyout: 0 } c) c.Buyout = GameWorld.BuyoutFor(p, c.Salary, c.Years, asOf);
            }
        world.Index();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM world_meta WHERE key = 'awaiting'";
            if (cmd.ExecuteScalar() is string aj && SaveFile.FromJson<Dictionary<int, List<int>>>(aj) is { } awaiting)
                foreach (var (teamId, ids) in awaiting)
                    if (world.TeamById(teamId) is { } club)
                        world.AwaitingDecision[teamId] = club.Roster.Where(p => ids.Contains(p.Id)).ToList();
        }
        return world;
    }

    private static void CreateSchema(SqliteConnection conn) => SaveFile.Exec(conn, """
        CREATE TABLE IF NOT EXISTS world_meta(key TEXT PRIMARY KEY, value TEXT);
        CREATE TABLE IF NOT EXISTS world_comp(key TEXT PRIMARY KEY, ord INTEGER, full_sim INTEGER, season_json TEXT, history_json TEXT);
        CREATE TABLE IF NOT EXISTS world_team(id INTEGER PRIMARY KEY, comp_key TEXT, city TEXT, name TEXT, abbr TEXT, capacity INTEGER,
            primary_color TEXT, secondary_color TEXT, logo_style INTEGER, tactic_json TEXT);
        """);
}
