using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;

namespace Hardwood.Core.Persistence;

/// <summary>Session state on top of the league tables: calendar, schedule with results, human box scores, inbox, bracket.</summary>
public static class SessionStore
{
    public static void SaveSession(this SaveFile file, GameSession s)
    {
        using var conn = file.OpenConnection();
        CreateSchema(conn);
        using var tx = conn.BeginTransaction();
        SaveFile.Exec(conn, "DELETE FROM session; DELETE FROM schedule; DELETE FROM box_line; DELETE FROM inbox; DELETE FROM postseason_series; DELETE FROM depth_chart; DELETE FROM offseason; DELETE FROM offseason_expiring; DELETE FROM offseason_pick;");

        SaveFile.Exec(conn, "INSERT INTO session(id, human_team_id, today, phase, seed, rng_seed, next_message_id, postseason_stage, midseason_json) VALUES(1, $t, $d, $p, $s, $r, $m, $ps, $mid)",
            ("$t", s.HumanTeam.Id), ("$d", s.Today.ToString("yyyy-MM-dd")), ("$p", (int)s.Phase), ("$s", (long)s.Seed), ("$r", (long)s.NextRngSeed),
            ("$m", s.Inbox.Count == 0 ? 1 : s.Inbox.Max(m => m.Id) + 1), ("$ps", (object?)s.Postseason?.Stage ?? DBNull.Value),
            ("$mid", s.Midseason != null ? SaveFile.ToJson(s.Midseason) : DBNull.Value));

        foreach (var g in s.Schedule)
        {
            s.Results.TryGetValue(g.Index, out var r);
            SaveFile.Exec(conn, "INSERT INTO schedule(idx, date, home_id, away_id, home_pts, away_pts, overtimes) VALUES($i, $d, $h, $a, $hp, $ap, $ot)",
                ("$i", g.Index), ("$d", g.Date.ToString("yyyy-MM-dd")), ("$h", g.Home.Id), ("$a", g.Away.Id),
                ("$hp", r is null ? DBNull.Value : r.Home.Points), ("$ap", r is null ? DBNull.Value : r.Away.Points), ("$ot", r?.Overtimes ?? 0));
            if (r != null && (g.Home == s.HumanTeam || g.Away == s.HumanTeam) && r.Home.FixedPoints == null)
                SaveBox(conn, g.Index, r);
        }

        foreach (var m in s.Inbox)
            SaveFile.Exec(conn, "INSERT INTO inbox(id, date, title, body, category, read) VALUES($i, $d, $t, $b, $c, $r)",
                ("$i", m.Id), ("$d", m.Date.ToString("yyyy-MM-dd")), ("$t", m.Title), ("$b", m.Body), ("$c", m.Category), ("$r", m.Read ? 1 : 0));

        if (s.Postseason != null)
        {
            int order = 0;
            foreach (var se in s.Postseason.Series)
                SaveFile.Exec(conn, "INSERT INTO postseason_series(ord, round, higher_id, lower_id, higher_wins, lower_wins, wins_needed) VALUES($o, $r, $h, $l, $hw, $lw, $n)",
                    ("$o", order++), ("$r", se.Round), ("$h", se.Higher.Id), ("$l", se.Lower.Id), ("$hw", se.HigherWins), ("$lw", se.LowerWins), ("$n", se.WinsNeeded));
        }

        if (s.Offseason is { } o && s.Phase == SessionPhase.Offseason)
        {
            SaveFile.Exec(conn, "INSERT INTO offseason(id, phase, season, draft_order, pick_owners, next_pick) VALUES(1, $p, $s, $d, $o, $n)",
                ("$p", (int)o.Phase), ("$s", o.Season), ("$d", string.Join(",", o.DraftOrder.Select(t => t.Id))), ("$o", string.Join(",", o.PickOwners.Select(t => t.Id))), ("$n", o.NextPickIndex));
            foreach (var kv in o.Expiring)
            foreach (var p in kv.Value)
                SaveFile.Exec(conn, "INSERT INTO offseason_expiring(team_id, player_id) VALUES($t, $p)", ("$t", kv.Key), ("$p", p.Id));
            foreach (var d in o.Report.Draft)
                SaveFile.Exec(conn, "INSERT INTO offseason_pick(pick, round, team_id, player_id, original_team_id) VALUES($k, $r, $t, $p, $o)",
                    ("$k", d.Pick), ("$r", d.Round), ("$t", d.Team.Id), ("$p", d.Player.Id), ("$o", (object?)d.OriginalTeam?.Id ?? DBNull.Value));
        }

        if (s.HumanTeam.DepthChart is { } chart)
        {
            SaveFile.Exec(conn, "INSERT INTO depth_chart(team_id, starters, minutes) VALUES($t, $s, $m)",
                ("$t", s.HumanTeam.Id), ("$s", string.Join(",", chart.Starters)), ("$m", string.Join(",", chart.MinutesTargets.Select(kv => $"{kv.Key}:{kv.Value:F1}"))));
        }
        tx.Commit();
    }

    private static void SaveBox(SqliteConnection conn, int gameIndex, GameResult r)
    {
        foreach (var box in new[] { r.Home, r.Away })
        foreach (var l in box.Lines.Where(l => l.Seconds > 0))
        {
            SaveFile.Exec(conn, """
                INSERT INTO box_line(game_idx, team_id, player_id, started, seconds, pts, fgm, fga, tpm, tpa, ftm, fta, oreb, dreb, ast, stl, blk, tov, pf, pm)
                VALUES($g, $t, $p, $st, $sec, $pts, $fgm, $fga, $tpm, $tpa, $ftm, $fta, $oreb, $dreb, $ast, $stl, $blk, $tov, $pf, $pm)
                """,
                ("$g", gameIndex), ("$t", box.Team.Id), ("$p", l.Player.Id), ("$st", l.Started ? 1 : 0), ("$sec", l.Seconds), ("$pts", l.Points),
                ("$fgm", l.FGM), ("$fga", l.FGA), ("$tpm", l.ThreePM), ("$tpa", l.ThreePA), ("$ftm", l.FTM), ("$fta", l.FTA), ("$oreb", l.OReb), ("$dreb", l.DReb),
                ("$ast", l.Assists), ("$stl", l.Steals), ("$blk", l.Blocks), ("$tov", l.Turnovers), ("$pf", l.Fouls), ("$pm", l.PlusMinus));
        }
    }

    public static GameSession LoadSession(this SaveFile file, League league, World.GameWorld? world = null)
    {
        using var conn = file.OpenConnection();
        CreateSchema(conn);

        int humanId; DateOnly today; SessionPhase phase; ulong seed, rngSeed; int nextMsg; string? stage; SeasonAwards? midseason = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT human_team_id, today, phase, seed, rng_seed, next_message_id, postseason_stage, midseason_json FROM session WHERE id = 1";
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException("Save has no session (it was made by the headless simulator).");
            humanId = r.GetInt32(0); today = DateOnly.Parse(r.GetString(1)); phase = (SessionPhase)r.GetInt32(2);
            seed = (ulong)r.GetInt64(3); rngSeed = (ulong)r.GetInt64(4); nextMsg = r.GetInt32(5); stage = r.IsDBNull(6) ? null : r.GetString(6);
            if (!r.IsDBNull(7)) midseason = SaveFile.FromJson<SeasonAwards>(r.GetString(7));
        }

        var session = new GameSession(league, league.TeamById(humanId), seed, file, rngState: rngSeed, world: world) { Midseason = midseason };
        var players = league.AllActivePlayers.Concat(league.FreeAgents).Concat(league.Retired).Concat(file.LoadedProspects).ToDictionary(p => p.Id);

        var boxes = new Dictionary<int, Dictionary<int, List<PlayerLine>>>(); // game -> team -> lines
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT game_idx, team_id, player_id, started, seconds, pts, fgm, fga, tpm, tpa, ftm, fta, oreb, dreb, ast, stl, blk, tov, pf, pm FROM box_line";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!players.TryGetValue(r.GetInt32(2), out var p)) continue;
                var line = new PlayerLine
                {
                    Player = p, Started = r.GetInt32(3) == 1, Seconds = r.GetDouble(4), Points = r.GetInt32(5), FGM = r.GetInt32(6), FGA = r.GetInt32(7),
                    ThreePM = r.GetInt32(8), ThreePA = r.GetInt32(9), FTM = r.GetInt32(10), FTA = r.GetInt32(11), OReb = r.GetInt32(12), DReb = r.GetInt32(13),
                    Assists = r.GetInt32(14), Steals = r.GetInt32(15), Blocks = r.GetInt32(16), Turnovers = r.GetInt32(17), Fouls = r.GetInt32(18), PlusMinus = r.GetInt32(19),
                };
                boxes.TryAdd(r.GetInt32(0), new());
                boxes[r.GetInt32(0)].TryAdd(r.GetInt32(1), new());
                boxes[r.GetInt32(0)][r.GetInt32(1)].Add(line);
            }
        }

        var schedule = new List<ScheduledGame>();
        var results = new Dictionary<int, GameResult>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT idx, date, home_id, away_id, home_pts, away_pts, overtimes FROM schedule ORDER BY idx";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var g = new ScheduledGame(r.GetInt32(0), DateOnly.Parse(r.GetString(1)), league.TeamById(r.GetInt32(2)), league.TeamById(r.GetInt32(3)));
                schedule.Add(g);
                if (r.IsDBNull(4)) continue;
                var home = new TeamBox { Team = g.Home };
                var away = new TeamBox { Team = g.Away };
                if (boxes.TryGetValue(g.Index, out var byTeam))
                {
                    if (byTeam.TryGetValue(g.Home.Id, out var hl)) home.Lines.AddRange(hl);
                    if (byTeam.TryGetValue(g.Away.Id, out var al)) away.Lines.AddRange(al);
                }
                if (home.Lines.Count == 0) home.FixedPoints = r.GetInt32(4);
                if (away.Lines.Count == 0) away.FixedPoints = r.GetInt32(5);
                results[g.Index] = new GameResult { Home = home, Away = away, Overtimes = r.GetInt32(6), Date = g.Date };
            }
        }

        var inbox = new List<InboxMessage>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, date, title, body, category, read FROM inbox ORDER BY id DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                inbox.Add(new InboxMessage { Id = r.GetInt32(0), Date = DateOnly.Parse(r.GetString(1)), Title = r.GetString(2), Body = r.GetString(3), Category = r.GetString(4), Read = r.GetInt32(5) == 1 });
        }

        var stats = file.LoadStats(league.Season).ToDictionary(x => x.PlayerId);
        session.Restore(today, phase, schedule, results, stats, inbox, nextMsg);

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT starters, minutes FROM depth_chart WHERE team_id = $t";
            cmd.Parameters.AddWithValue("$t", humanId);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var chart = new DepthChart();
                var starters = r.GetString(0);
                if (starters.Length > 0) chart.Starters = starters.Split(',').Select(int.Parse).ToList();
                var mins = r.GetString(1);
                if (mins.Length > 0)
                    foreach (var kv in mins.Split(',')) { var parts = kv.Split(':'); chart.MinutesTargets[int.Parse(parts[0])] = double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture); }
                session.HumanTeam.DepthChart = chart;
            }
        }

        if (phase == SessionPhase.Offseason)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT phase, season, draft_order, pick_owners, next_pick FROM offseason WHERE id = 1";
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var oPhase = (OffseasonPhase)r.GetInt32(0);
                var order = r.GetString(2).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
                var owners = r.GetString(3).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
                int nextPick = r.GetInt32(4);
                r.Close();

                var expiring = new Dictionary<int, List<Player>>();
                using (var c2 = conn.CreateCommand())
                {
                    c2.CommandText = "SELECT team_id, player_id FROM offseason_expiring";
                    using var r2 = c2.ExecuteReader();
                    while (r2.Read())
                    {
                        if (!players.TryGetValue(r2.GetInt32(1), out var p)) continue;
                        expiring.TryAdd(r2.GetInt32(0), new());
                        expiring[r2.GetInt32(0)].Add(p);
                    }
                }
                var picksMade = new List<DraftPick>();
                using (var c3 = conn.CreateCommand())
                {
                    c3.CommandText = "SELECT pick, round, team_id, player_id, original_team_id FROM offseason_pick ORDER BY pick";
                    using var r3 = c3.ExecuteReader();
                    while (r3.Read())
                        if (players.TryGetValue(r3.GetInt32(3), out var p))
                            picksMade.Add(new DraftPick(r3.GetInt32(1), r3.GetInt32(0), league.TeamById(r3.GetInt32(2)), p, r3.IsDBNull(4) ? null : league.TeamById(r3.GetInt32(4))));
                }
                session.RestoreOffseason(oPhase, order, owners, file.LoadedProspects, nextPick, expiring, picksMade);
            }
        }

        if (phase == SessionPhase.Postseason && stage != null)
        {
            var rows = new List<(string, int, int, int, int, int)>();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT round, higher_id, lower_id, higher_wins, lower_wins, wins_needed FROM postseason_series ORDER BY ord";
            using var r = cmd.ExecuteReader();
            while (r.Read()) rows.Add((r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5)));
            session.RestorePostseason(stage, rows);
        }
        return session;
    }

    private static void CreateSchema(SqliteConnection conn)
    {
        SaveFile.Exec(conn, """
            CREATE TABLE IF NOT EXISTS session(id INTEGER PRIMARY KEY, human_team_id INTEGER, today TEXT, phase INTEGER, seed INTEGER, rng_seed INTEGER, next_message_id INTEGER, postseason_stage TEXT, midseason_json TEXT);
            CREATE TABLE IF NOT EXISTS schedule(idx INTEGER PRIMARY KEY, date TEXT, home_id INTEGER, away_id INTEGER, home_pts INTEGER, away_pts INTEGER, overtimes INTEGER);
            CREATE TABLE IF NOT EXISTS box_line(game_idx INTEGER, team_id INTEGER, player_id INTEGER, started INTEGER, seconds REAL, pts INTEGER, fgm INTEGER, fga INTEGER, tpm INTEGER, tpa INTEGER,
                ftm INTEGER, fta INTEGER, oreb INTEGER, dreb INTEGER, ast INTEGER, stl INTEGER, blk INTEGER, tov INTEGER, pf INTEGER, pm INTEGER);
            CREATE INDEX IF NOT EXISTS ix_box_game ON box_line(game_idx);
            CREATE TABLE IF NOT EXISTS inbox(id INTEGER PRIMARY KEY, date TEXT, title TEXT, body TEXT, category TEXT, read INTEGER);
            CREATE TABLE IF NOT EXISTS postseason_series(ord INTEGER PRIMARY KEY, round TEXT, higher_id INTEGER, lower_id INTEGER, higher_wins INTEGER, lower_wins INTEGER, wins_needed INTEGER);
            CREATE TABLE IF NOT EXISTS depth_chart(team_id INTEGER PRIMARY KEY, starters TEXT, minutes TEXT);
            CREATE TABLE IF NOT EXISTS offseason(id INTEGER PRIMARY KEY, phase INTEGER, season INTEGER, draft_order TEXT, pick_owners TEXT, next_pick INTEGER);
            CREATE TABLE IF NOT EXISTS offseason_expiring(team_id INTEGER, player_id INTEGER);
            CREATE TABLE IF NOT EXISTS offseason_pick(pick INTEGER PRIMARY KEY, round INTEGER, team_id INTEGER, player_id INTEGER, original_team_id INTEGER);
            """);
    }
}
