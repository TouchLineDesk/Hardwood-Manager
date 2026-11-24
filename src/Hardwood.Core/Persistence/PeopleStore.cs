using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;

namespace Hardwood.Core.Persistence;

/// <summary>
/// Players as people (save format 8): each NABL player's morale state, each team's chemistry and continuity, and the
/// coach's promises. Saves from before format 8 start everyone content (morale 60, chemistry 60).
/// </summary>
public static class PeopleStore
{
    private sealed record PlayerMood(double Morale, int UnhappyWeeks, bool TradeRequested, DateOnly? LastTalk, DateOnly? DistrustUntil,
        double MoodBoost = 0, DateOnly? BoostUntil = null, string? BoostReason = null);
    private sealed record TeamMood(double Chemistry, Dictionary<int, int> WeeksTogether);

    public static void SavePeople(this SaveFile file, GameSession s)
    {
        using var conn = file.OpenConnection();
        CreateSchema(conn);
        using var tx = conn.BeginTransaction();
        SaveFile.Exec(conn, "DELETE FROM people_meta;");
        // Clubs abroad: only the one you coach has moods worth keeping.
        var abroad = s.Abroad ? new[] { s.HumanTeam } : Array.Empty<Team>();
        // Run 185: kept to the last bit (they were rounded to 2 places), so a reloaded career makes the same mood-driven
        // choices as the career that saved it (the summer market drew differently after a reload).
        var players = s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(abroad.SelectMany(t => t.Roster)).DistinctBy(p => p.Id)
            .ToDictionary(p => p.Id, p => new PlayerMood(p.Morale, p.UnhappyWeeks, p.TradeRequested, p.LastTalk, p.DistrustUntil, p.MoodBoost, p.BoostUntil, p.BoostReason));
        var teams = s.League.Teams.Concat(abroad).DistinctBy(t => t.Id).ToDictionary(t => t.Id, t => new TeamMood(t.Chemistry, t.WeeksTogether));
        SaveFile.Exec(conn, "INSERT INTO people_meta(key, value) VALUES('players', $v)", ("$v", SaveFile.ToJson(players)));
        SaveFile.Exec(conn, "INSERT INTO people_meta(key, value) VALUES('teams', $v)", ("$v", SaveFile.ToJson(teams)));
        SaveFile.Exec(conn, "INSERT INTO people_meta(key, value) VALUES('promises', $v)", ("$v", SaveFile.ToJson(s.Promises)));
        SaveFile.Exec(conn, "INSERT INTO people_meta(key, value) VALUES('history', $v)", ("$v", SaveFile.ToJson(s.MoodHistory)));
        tx.Commit();
    }

    public static void LoadPeople(this SaveFile file, GameSession s)
    {
        using var conn = file.OpenConnection();
        CreateSchema(conn);
        string? Get(string key)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM people_meta WHERE key = $k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
        if (Get("players") is { } pj && SaveFile.FromJson<Dictionary<int, PlayerMood>>(pj) is { } players)
            // Run 185 (High): the program you coach is saved above (Abroad is true in college) but its players live in the
            // college world, not World.Players, so their morale, unhappy weeks and mood boosts were reset to 60 on every load.
            foreach (var p in s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.World.Players).Concat(s.World.College?.Players ?? Enumerable.Empty<Player>()))
                if (players.TryGetValue(p.Id, out var m))
                {
                    p.Morale = m.Morale; p.UnhappyWeeks = m.UnhappyWeeks; p.TradeRequested = m.TradeRequested;
                    p.LastTalk = m.LastTalk; p.DistrustUntil = m.DistrustUntil;
                    p.MoodBoost = m.MoodBoost; p.BoostUntil = m.BoostUntil; p.BoostReason = m.BoostReason;
                }
        if (Get("teams") is { } tj && SaveFile.FromJson<Dictionary<int, TeamMood>>(tj) is { } teams)
            foreach (var t in s.League.Teams.Concat(s.World.Clubs).Concat(s.World.College?.Programs ?? Enumerable.Empty<Team>()))
                if (teams.TryGetValue(t.Id, out var m)) { t.Chemistry = m.Chemistry; t.WeeksTogether = m.WeeksTogether ?? new(); }
        if (Get("promises") is { } prj && SaveFile.FromJson<List<Promise>>(prj) is { } promises)
        {
            s.Promises.Clear();
            s.Promises.AddRange(promises);
        }
        if (Get("history") is { } hj && SaveFile.FromJson<List<MoodSnapshot>>(hj) is { } history)
        {
            s.MoodHistory.Clear();
            s.MoodHistory.AddRange(history);
        }
    }

    private static void CreateSchema(SqliteConnection conn) =>
        SaveFile.Exec(conn, "CREATE TABLE IF NOT EXISTS people_meta(key TEXT PRIMARY KEY, value TEXT);");
}
