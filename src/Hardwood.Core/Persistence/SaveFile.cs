using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;

namespace Hardwood.Core.Persistence;

/// <summary>
/// One SQLite file per save. Holds the league, every team and player (active, free agent, retired),
/// contracts, season history and per-season player stats. The schema is plain so the editor and
/// external tools can read it.
/// </summary>
public sealed class SaveFile
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new() { IncludeFields = true };
    internal static string ToJson<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, JsonOpts);
    internal static T? FromJson<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json, JsonOpts);

    /// <summary>
    /// Save format. 2: phase 5 · 3: ethnicity, draft record · 4: portrait face · 5: the world beyond the NABL ·
    /// 6: player movement (overseas buyout clauses, draft rights held while a player is stashed abroad) ·
    /// 7: full team tactics (system, scheme, roles, matchups, presets) as JSON ·
    /// 8: players as people (morale, chemistry, promises; people_meta table) ·
    /// 9: your coach, club owners and your job (career_meta table).
    /// Bump it whenever the format changes, add a sample save of the old format to tests/Hardwood.Tests/SaveFixtures,
    /// and make sure Load upgrades it (SaveCompatibilityTests loads every sample).
    /// </summary>
    public const int SchemaVersion = 9;
    private readonly string _path;
    /// <summary>Draft prospects found in the last Load (an offseason in progress).</summary>
    public List<Player> LoadedProspects { get; } = new();
    /// <summary>Players of clubs outside the NABL found in the last Load; <see cref="WorldStore"/> puts them on their clubs.</summary>
    public List<Player> LoadedOverseas { get; } = new();

    public SaveFile(string path) => _path = path;

    /// <summary>Format version of the save on disk before this load upgraded it (null if it was already current or new).</summary>
    public int? UpgradedFrom { get; private set; }
    /// <summary>Where the untouched copy of an upgraded save was put.</summary>
    public string? BackupPath { get; private set; }

    /// <summary>
    /// Before opening a save made by an older version (which upgrades it in place), copy it next to itself as
    /// <c>&lt;name&gt;.v&lt;version&gt;-backup&lt;ext&gt;</c>, once per version. Older versions of the game can still open that copy.
    /// </summary>
    public void BackupBeforeUpgrade()
    {
        if (!File.Exists(_path)) return;
        int? version = null;
        var cs = new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        using (var conn = new SqliteConnection(cs))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key = 'schema_version'";
            try { if (cmd.ExecuteScalar() is string v && int.TryParse(v, out var n)) version = n; }
            catch (SqliteException) { return; } // not a save we understand; Load will report it
        }
        if (version is not { } old || old >= SchemaVersion) return;
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(_path))!;
        var backup = System.IO.Path.Combine(dir, $"{System.IO.Path.GetFileNameWithoutExtension(_path)}.v{old}-backup{System.IO.Path.GetExtension(_path)}");
        if (!File.Exists(backup)) File.Copy(_path, backup);
        UpgradedFrom = old;
        BackupPath = backup;
    }

    /// <summary>Location of the save on disk.</summary>
    public string Path => _path;

    /// <summary>Open a connection for extension stores (sessions). Caller disposes.</summary>
    public SqliteConnection OpenConnection() => Open();

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_path}");
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=DELETE; PRAGMA synchronous=NORMAL;";
        pragma.ExecuteNonQuery();
        Migrate(conn);
        return conn;
    }

    // ------------------------------------------------------------------------------------------
    public void Save(League league, IEnumerable<PlayerSeasonStats>? seasonStats = null, IEnumerable<Player>? prospects = null, IEnumerable<Player>? overseas = null)
    {
        using var conn = Open();
        CreateSchema(conn);
        using var tx = conn.BeginTransaction();

        Exec(conn, "DELETE FROM meta; DELETE FROM player_stats WHERE 0; DELETE FROM league; DELETE FROM team; DELETE FROM player; DELETE FROM contract; DELETE FROM season_history; DELETE FROM season_record; DELETE FROM injury; DELETE FROM draft_pick; DELETE FROM career; DELETE FROM hall_of_fame; DELETE FROM retired_number; DELETE FROM staff; DELETE FROM training;");

        Exec(conn, "INSERT INTO meta(key, value) VALUES('schema_version', $v), ('saved_at', $t)",
            ("$v", SchemaVersion.ToString()), ("$t", DateTime.UtcNow.ToString("O")));

        Exec(conn, """
            INSERT INTO league(id, name, season, rules_name, periods, minutes_per_period, shot_clock, fouls_out, bonus_threshold, three_distance,
                               hard_cap, min_salary, roster_max, roster_min, rookie_scale_first, next_player_id, next_pick_id)
            VALUES(1, $name, $season, $rn, $per, $mpp, $sc, $fo, $bt, $td, $cap, $min, $rmax, $rmin, $rs, $npid, $npk)
            """,
            ("$name", league.Name), ("$season", league.Season), ("$rn", league.Rules.Name), ("$per", league.Rules.Periods),
            ("$mpp", league.Rules.MinutesPerPeriod), ("$sc", league.Rules.ShotClockSeconds), ("$fo", league.Rules.FoulsToFoulOut),
            ("$bt", league.Rules.TeamFoulBonusThreshold), ("$td", league.Rules.ThreePointDistanceM),
            ("$cap", league.Finance.HardCap), ("$min", league.Finance.MinSalary), ("$rmax", league.Finance.RosterMax),
            ("$rmin", league.Finance.RosterMin), ("$rs", league.Finance.RookieScaleFirstPick), ("$npid", league.NextPlayerId), ("$npk", league.NextPickId));
        Exec(conn, "INSERT INTO meta(key, value) VALUES('next_staff_id', $v)", ("$v", league.NextStaffId.ToString()));

        foreach (var pk in league.Picks)
            Exec(conn, "INSERT INTO draft_pick(id, year, round, original_team_id, owner_team_id, protected_top) VALUES($id, $y, $r, $o, $w, $p)",
                ("$id", pk.Id), ("$y", pk.Year), ("$r", pk.Round), ("$o", pk.OriginalTeamId), ("$w", pk.OwnerTeamId), ("$p", pk.ProtectedTop));

        foreach (var t in league.Teams)
        {
            Exec(conn, """
                INSERT INTO team(id, city, name, abbr, conference, division, arena_capacity, pace, three_emphasis, crash_glass, def_aggression, star_usage, primary_color, secondary_color, logo_style, tactic_json)
                VALUES($id, $city, $name, $abbr, $conf, $div, $cap, $pace, $three, $crash, $agg, $star, $pc, $sc, $ls, $tac)
                """,
                ("$id", t.Id), ("$city", t.City), ("$name", t.Name), ("$abbr", t.Abbreviation), ("$conf", (int)t.Conference),
                ("$div", t.Division), ("$cap", t.ArenaCapacity), ("$pace", t.Tactic.Pace), ("$three", t.Tactic.ThreePointEmphasis),
                ("$crash", t.Tactic.CrashGlass), ("$agg", t.Tactic.DefensiveAggression), ("$star", t.Tactic.StarUsage),
                ("$pc", t.PrimaryColor), ("$sc", t.SecondaryColor), ("$ls", t.LogoStyle), ("$tac", ToJson(t.Tactic)));
        }

        foreach (var t in league.Teams) foreach (var p in t.Roster) InsertPlayer(conn, p, t.Id, "active");
        foreach (var p in league.FreeAgents) InsertPlayer(conn, p, null, "free_agent");
        foreach (var p in league.Retired) InsertPlayer(conn, p, null, "retired");
        if (prospects != null) foreach (var p in prospects) InsertPlayer(conn, p, null, "prospect");
        if (overseas != null) foreach (var p in overseas) InsertPlayer(conn, p, p.TeamId, "overseas");

        foreach (var h in league.History)
        {
            Exec(conn, "INSERT INTO season_history(season, champion_team_id, runner_up_team_id, mvp_player_id, scoring_leader_player_id, finals_mvp_player_id, awards_json) VALUES($s, $c, $r, $m, $l, $f, $a)",
                ("$s", h.Season), ("$c", h.ChampionTeamId), ("$r", h.RunnerUpTeamId), ("$m", (object?)h.MvpPlayerId ?? DBNull.Value), ("$l", (object?)h.ScoringLeaderPlayerId ?? DBNull.Value),
                ("$f", (object?)h.FinalsMvpPlayerId ?? DBNull.Value), ("$a", h.Awards != null ? ToJson(h.Awards) : DBNull.Value));
            foreach (var (teamId, w, l) in h.Records)
                Exec(conn, "INSERT INTO season_record(season, team_id, wins, losses) VALUES($s, $t, $w, $l)", ("$s", h.Season), ("$t", teamId), ("$w", w), ("$l", l));
        }

        foreach (var st in league.Teams.SelectMany(t => t.Staff).Concat(league.FreeStaff))
            Exec(conn, """
                INSERT INTO staff(id, first_name, last_name, birth_date, role, coaching, development, fitness, medical, scouting, reputation, team_id, salary, years)
                VALUES($id, $fn, $ln, $bd, $role, $c, $d, $f, $m, $s, $r, $t, $sal, $y)
                """,
                ("$id", st.Id), ("$fn", st.FirstName), ("$ln", st.LastName), ("$bd", st.BirthDate.ToString("yyyy-MM-dd")), ("$role", (int)st.Role),
                ("$c", st.Coaching), ("$d", st.Development), ("$f", st.Fitness), ("$m", st.Medical), ("$s", st.Scouting), ("$r", st.Reputation),
                ("$t", (object?)st.TeamId ?? DBNull.Value), ("$sal", st.Salary), ("$y", st.ContractYears));
        foreach (var t in league.Teams)
            Exec(conn, "INSERT INTO training(team_id, intensity, focus) VALUES($t, $i, $f)",
                ("$t", t.Id), ("$i", t.Training.Intensity), ("$f", string.Join(",", t.Training.Focus.Select(kv => $"{kv.Key}:{(int)kv.Value}"))));

        foreach (var c in league.Careers.Values)
            Exec(conn, "INSERT INTO career(player_id, json) VALUES($p, $j)", ("$p", c.PlayerId), ("$j", ToJson(c)));
        foreach (var h in league.HallOfFame)
            Exec(conn, "INSERT INTO hall_of_fame(player_id, year, score) VALUES($p, $y, $s)", ("$p", h.PlayerId), ("$y", h.Year), ("$s", h.Score));
        foreach (var t in league.Teams)
        foreach (var pid in t.RetiredNumbers)
            Exec(conn, "INSERT INTO retired_number(team_id, player_id) VALUES($t, $p)", ("$t", t.Id), ("$p", pid));

        if (seasonStats != null)
        {
            foreach (var s in seasonStats)
            {
                Exec(conn, """
                    INSERT OR REPLACE INTO player_stats(player_id, season, team_id, games, starts, minutes, points, fgm, fga, tpm, tpa, ftm, fta, oreb, dreb, ast, stl, blk, tov, pf, plus_minus, zones)
                    VALUES($pid, $season, $tid, $g, $gs, $min, $pts, $fgm, $fga, $tpm, $tpa, $ftm, $fta, $oreb, $dreb, $ast, $stl, $blk, $tov, $pf, $pm, $z)
                    """,
                    ("$pid", s.PlayerId), ("$season", s.Season), ("$tid", s.TeamId), ("$g", s.Games), ("$gs", s.Starts), ("$min", s.Minutes),
                    ("$pts", s.Points), ("$fgm", s.FGM), ("$fga", s.FGA), ("$tpm", s.ThreePM), ("$tpa", s.ThreePA), ("$ftm", s.FTM), ("$fta", s.FTA),
                    ("$oreb", s.OReb), ("$dreb", s.DReb), ("$ast", s.Assists), ("$stl", s.Steals), ("$blk", s.Blocks), ("$tov", s.Turnovers), ("$pf", s.Fouls), ("$pm", s.PlusMinus),
                    ("$z", string.Join(",", s.ZoneAttempts) + "|" + string.Join(",", s.ZoneMakes)));
            }
        }

        tx.Commit();
    }

    private static void InsertPlayer(SqliteConnection conn, Player p, int? teamId, string status)
    {
        var attrs = string.Join(",", AttrInfo.All.Select(a => p.Attributes[a]));
        var h = p.Hidden; var t = p.Tendencies;
        Exec(conn, """
            INSERT INTO player(id, first_name, last_name, birth_date, nationality, height_cm, wingspan_cm, weight_kg, primary_pos, secondary_pos,
                               ca, pa, team_id, status, attributes,
                               professionalism, ambition, loyalty, temperament, pressure, adaptability, consistency, big_games, injury_proneness, ego,
                               t_three, t_drive, t_post, t_iso, t_pass_first, t_crash, t_gamble, rookie_season,
                               ethnicity, draft_year, draft_round, draft_pick, face_file, draft_rights_team_id)
            VALUES($id, $fn, $ln, $bd, $nat, $h, $ws, $w, $pp, $sp, $ca, $pa, $tid, $status, $attrs,
                   $prof, $amb, $loy, $temp, $pres, $adapt, $cons, $big, $inj, $ego,
                   $t3, $tdr, $tpo, $tiso, $tpf, $tcr, $tga, $rook,
                   $eth, $dy, $dr, $dp, $face, $rights)
            """,
            ("$id", p.Id), ("$fn", p.FirstName), ("$ln", p.LastName), ("$bd", p.BirthDate.ToString("yyyy-MM-dd")), ("$nat", p.Nationality),
            ("$h", p.HeightCm), ("$ws", p.WingspanCm), ("$w", p.WeightKg), ("$pp", (int)p.PrimaryPosition), ("$sp", p.SecondaryPosition is { } s ? (int)s : DBNull.Value),
            ("$ca", p.CurrentAbility), ("$pa", p.PotentialAbility), ("$tid", (object?)teamId ?? DBNull.Value), ("$status", status), ("$attrs", attrs),
            ("$prof", h.Professionalism), ("$amb", h.Ambition), ("$loy", h.Loyalty), ("$temp", h.Temperament), ("$pres", h.Pressure), ("$adapt", h.Adaptability),
            ("$cons", h.Consistency), ("$big", h.BigGames), ("$inj", h.InjuryProneness), ("$ego", h.Ego),
            ("$t3", t.ThreePointRate), ("$tdr", t.DriveRate), ("$tpo", t.PostUpRate), ("$tiso", t.IsolationRate), ("$tpf", t.PassFirst), ("$tcr", t.CrashGlass), ("$tga", t.GambleSteals),
            ("$rook", (object?)p.RookieSeason ?? DBNull.Value),
            ("$eth", EthnicityInfo.FolderName(p.Ethnicity)),
            ("$dy", (object?)p.Draft?.Year ?? DBNull.Value), ("$dr", (object?)p.Draft?.Round ?? DBNull.Value), ("$dp", (object?)p.Draft?.Pick ?? DBNull.Value),
            ("$face", (object?)p.FaceFile ?? DBNull.Value), ("$rights", (object?)p.DraftRightsTeamId ?? DBNull.Value));

        foreach (var inj in p.InjuryHistory)
            Exec(conn, "INSERT INTO injury(player_id, name, severity, occurred, return_date, attr_loss, current) VALUES($pid, $n, $sev, $occ, $ret, $loss, $cur)",
                ("$pid", p.Id), ("$n", inj.Name), ("$sev", (int)inj.Severity), ("$occ", inj.Occurred.ToString("yyyy-MM-dd")), ("$ret", inj.ReturnDate.ToString("yyyy-MM-dd")),
                ("$loss", inj.AttributeLoss), ("$cur", ReferenceEquals(inj, p.Injury) ? 1 : 0));

        if (p.Contract is { } c)
            Exec(conn, "INSERT INTO contract(player_id, years, salary, start_season, team_option, player_option, buyout) VALUES($pid, $y, $s, $ss, $to, $po, $bo)",
                ("$pid", p.Id), ("$y", c.Years), ("$s", c.Salary), ("$ss", c.StartSeason), ("$to", c.TeamOption ? 1 : 0), ("$po", c.PlayerOption ? 1 : 0), ("$bo", c.Buyout));
    }

    // ------------------------------------------------------------------------------------------
    public League Load()
    {
        BackupBeforeUpgrade();
        using var conn = Open();
        League league;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name, season, rules_name, periods, minutes_per_period, shot_clock, fouls_out, bonus_threshold, three_distance, hard_cap, min_salary, roster_max, roster_min, rookie_scale_first, next_player_id, next_pick_id FROM league WHERE id = 1";
            using var r = cmd.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException("Save file has no league");
            league = new League
            {
                Name = r.GetString(0),
                Season = r.GetInt32(1),
                Rules = new Rules(r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7), r.GetDouble(8)),
                Finance = new Finance(r.GetInt64(9), r.GetInt64(10), r.GetInt32(11), r.GetInt32(12), r.GetInt64(13)),
                NextPlayerId = r.GetInt32(14),
                NextPickId = r.IsDBNull(15) ? 1 : r.GetInt32(15),
            };
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM meta WHERE key = 'next_staff_id'";
            var v = cmd.ExecuteScalar() as string;
            if (v != null) league.NextStaffId = int.Parse(v);
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, year, round, original_team_id, owner_team_id, protected_top FROM draft_pick";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                league.Picks.Add(new DraftPickAsset { Id = r.GetInt32(0), Year = r.GetInt32(1), Round = r.GetInt32(2), OriginalTeamId = r.GetInt32(3), OwnerTeamId = r.GetInt32(4), ProtectedTop = r.GetInt32(5) });
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, city, name, abbr, conference, division, arena_capacity, pace, three_emphasis, crash_glass, def_aggression, star_usage, primary_color, secondary_color, logo_style, tactic_json FROM team ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                league.Teams.Add(new Team
                {
                    Id = r.GetInt32(0), City = r.GetString(1), Name = r.GetString(2), Abbreviation = r.GetString(3),
                    Conference = (Conference)r.GetInt32(4), Division = r.GetString(5), ArenaCapacity = r.GetInt32(6),
                    Tactic = (r.IsDBNull(15) ? null : FromJson<Tactic>(r.GetString(15)))
                             ?? new Tactic { Pace = r.GetDouble(7), ThreePointEmphasis = r.GetInt32(8), CrashGlass = r.GetInt32(9), DefensiveAggression = r.GetInt32(10), StarUsage = r.GetInt32(11) },
                    PrimaryColor = r.IsDBNull(12) ? "#1D3FA3" : r.GetString(12), SecondaryColor = r.IsDBNull(13) ? "#F0A33A" : r.GetString(13), LogoStyle = r.IsDBNull(14) ? 0 : r.GetInt32(14),
                });
            }
        }

        var contracts = new Dictionary<int, Contract>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT player_id, years, salary, start_season, team_option, player_option, buyout FROM contract";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                contracts[r.GetInt32(0)] = new Contract
                {
                    Years = r.GetInt32(1), Salary = r.GetInt64(2), StartSeason = r.GetInt32(3), TeamOption = r.GetInt32(4) == 1, PlayerOption = r.GetInt32(5) == 1,
                    Buyout = r.IsDBNull(6) ? 0 : r.GetInt64(6),
                };
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT id, first_name, last_name, birth_date, nationality, height_cm, wingspan_cm, weight_kg, primary_pos, secondary_pos, ca, pa, team_id, status, attributes,
                       professionalism, ambition, loyalty, temperament, pressure, adaptability, consistency, big_games, injury_proneness, ego,
                       t_three, t_drive, t_post, t_iso, t_pass_first, t_crash, t_gamble, rookie_season,
                       ethnicity, draft_year, draft_round, draft_pick, face_file, draft_rights_team_id
                FROM player ORDER BY id
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var p = new Player
                {
                    Id = r.GetInt32(0), FirstName = r.GetString(1), LastName = r.GetString(2),
                    BirthDate = DateOnly.Parse(r.GetString(3)), Nationality = r.GetString(4),
                    HeightCm = r.GetInt32(5), WingspanCm = r.GetInt32(6), WeightKg = r.GetInt32(7),
                    PrimaryPosition = (Position)r.GetInt32(8),
                    SecondaryPosition = r.IsDBNull(9) ? null : (Position)r.GetInt32(9),
                    CurrentAbility = r.GetInt32(10), PotentialAbility = r.GetInt32(11),
                    TeamId = r.IsDBNull(12) ? null : r.GetInt32(12),
                };
                var values = r.GetString(14).Split(',');
                int i = 0;
                foreach (var a in AttrInfo.All) p.Attributes[a] = int.Parse(values[i++]);
                var h = p.Hidden;
                h.Professionalism = r.GetInt32(15); h.Ambition = r.GetInt32(16); h.Loyalty = r.GetInt32(17); h.Temperament = r.GetInt32(18);
                h.Pressure = r.GetInt32(19); h.Adaptability = r.GetInt32(20); h.Consistency = r.GetInt32(21); h.BigGames = r.GetInt32(22);
                h.InjuryProneness = r.GetInt32(23); h.Ego = r.GetInt32(24);
                var t = p.Tendencies;
                t.ThreePointRate = r.GetInt32(25); t.DriveRate = r.GetInt32(26); t.PostUpRate = r.GetInt32(27); t.IsolationRate = r.GetInt32(28);
                t.PassFirst = r.GetInt32(29); t.CrashGlass = r.GetInt32(30); t.GambleSteals = r.GetInt32(31);
                p.RookieSeason = r.IsDBNull(32) ? null : r.GetInt32(32);
                p.Ethnicity = (r.IsDBNull(33) ? null : EthnicityInfo.FromFolderName(r.GetString(33)))
                              ?? Generation.Heritages.Backfill(p.Id, p.Nationality);
                p.FaceFile = r.IsDBNull(37) ? null : r.GetString(37);
                p.DraftRightsTeamId = r.IsDBNull(38) ? null : r.GetInt32(38);
                Generation.Heritages.Reconcile(p);
                if (!r.IsDBNull(34)) p.Draft = new DraftRecord(r.GetInt32(34), r.GetInt32(35), r.GetInt32(36));
                if (contracts.TryGetValue(p.Id, out var c)) p.Contract = c;

                switch (r.GetString(13))
                {
                    case "active": league.TeamById(p.TeamId!.Value).Roster.Add(p); break;
                    case "free_agent": league.FreeAgents.Add(p); break;
                    case "prospect": LoadedProspects.Add(p); break;
                    case "overseas": LoadedOverseas.Add(p); break;
                    default: league.Retired.Add(p); break;
                }
            }
        }

        var allPlayers = league.AllActivePlayers.Concat(league.FreeAgents).Concat(league.Retired).Concat(LoadedProspects).Concat(LoadedOverseas).ToDictionary(p => p.Id);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT player_id, name, severity, occurred, return_date, attr_loss, current FROM injury ORDER BY occurred";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!allPlayers.TryGetValue(r.GetInt32(0), out var p)) continue;
                var inj = new Injury
                {
                    Name = r.GetString(1), Severity = (InjurySeverity)r.GetInt32(2),
                    Occurred = DateOnly.Parse(r.GetString(3)), ReturnDate = DateOnly.Parse(r.GetString(4)), AttributeLoss = r.GetInt32(5),
                };
                p.InjuryHistory.Add(inj);
                if (r.GetInt32(6) == 1) p.Injury = inj;
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT season, champion_team_id, runner_up_team_id, mvp_player_id, scoring_leader_player_id, finals_mvp_player_id, awards_json FROM season_history ORDER BY season";
            using var r = cmd.ExecuteReader();
            var summaries = new List<SeasonSummary>();
            while (r.Read())
                summaries.Add(new SeasonSummary
                {
                    Season = r.GetInt32(0), ChampionTeamId = r.GetInt32(1), RunnerUpTeamId = r.GetInt32(2),
                    MvpPlayerId = r.IsDBNull(3) ? null : r.GetInt32(3), ScoringLeaderPlayerId = r.IsDBNull(4) ? null : r.GetInt32(4),
                    FinalsMvpPlayerId = r.IsDBNull(5) ? null : r.GetInt32(5),
                    Awards = r.IsDBNull(6) ? null : FromJson<SeasonAwards>(r.GetString(6)),
                });
            league.History.AddRange(summaries);
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, first_name, last_name, birth_date, role, coaching, development, fitness, medical, scouting, reputation, team_id, salary, years FROM staff";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var st = new Staff
                {
                    Id = r.GetInt32(0), FirstName = r.GetString(1), LastName = r.GetString(2), BirthDate = DateOnly.Parse(r.GetString(3)), Role = (StaffRole)r.GetInt32(4),
                    Coaching = r.GetInt32(5), Development = r.GetInt32(6), Fitness = r.GetInt32(7), Medical = r.GetInt32(8), Scouting = r.GetInt32(9), Reputation = r.GetInt32(10),
                    TeamId = r.IsDBNull(11) ? null : r.GetInt32(11), Salary = r.GetInt64(12), ContractYears = r.GetInt32(13),
                };
                if (st.TeamId is { } tid) league.TeamById(tid).Staff.Add(st); else league.FreeStaff.Add(st);
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT team_id, intensity, focus FROM training";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var t = league.TeamById(r.GetInt32(0));
                t.Training = new TrainingPlan { Intensity = r.GetInt32(1) };
                var f = r.GetString(2);
                if (f.Length > 0) foreach (var kv in f.Split(',')) { var parts = kv.Split(':'); t.Training.Focus[int.Parse(parts[0])] = (AttrGroup)int.Parse(parts[1]); }
            }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT player_id, json FROM career";
            using var r = cmd.ExecuteReader();
            while (r.Read()) { var c = FromJson<CareerTotals>(r.GetString(1)); if (c != null) league.Careers[r.GetInt32(0)] = c; }
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT player_id, year, score FROM hall_of_fame ORDER BY year";
            using var r = cmd.ExecuteReader();
            while (r.Read()) league.HallOfFame.Add(new HallOfFamer(r.GetInt32(0), r.GetInt32(1), r.GetDouble(2)));
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT team_id, player_id FROM retired_number";
            using var r = cmd.ExecuteReader();
            while (r.Read()) league.TeamById(r.GetInt32(0)).RetiredNumbers.Add(r.GetInt32(1));
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT season, team_id, wins, losses FROM season_record";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                league.History.First(h => h.Season == r.GetInt32(0)).Records.Add((r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)));
        }
        return league;
    }

    public List<PlayerSeasonStats> LoadStats(int? season = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT player_id, season, team_id, games, starts, minutes, points, fgm, fga, tpm, tpa, ftm, fta, oreb, dreb, ast, stl, blk, tov, pf, plus_minus, zones FROM player_stats" + (season is null ? "" : " WHERE season = $s");
        if (season is { } s) cmd.Parameters.AddWithValue("$s", s);
        using var r = cmd.ExecuteReader();
        var list = new List<PlayerSeasonStats>();
        while (r.Read())
        {
            var st = new PlayerSeasonStats
            {
                PlayerId = r.GetInt32(0), Season = r.GetInt32(1), TeamId = r.GetInt32(2), Games = r.GetInt32(3), Starts = r.GetInt32(4), Minutes = r.GetDouble(5),
                Points = r.GetInt32(6), FGM = r.GetInt32(7), FGA = r.GetInt32(8), ThreePM = r.GetInt32(9), ThreePA = r.GetInt32(10), FTM = r.GetInt32(11), FTA = r.GetInt32(12),
                OReb = r.GetInt32(13), DReb = r.GetInt32(14), Assists = r.GetInt32(15), Steals = r.GetInt32(16), Blocks = r.GetInt32(17), Turnovers = r.GetInt32(18), Fouls = r.GetInt32(19), PlusMinus = r.GetInt32(20),
            };
            if (!r.IsDBNull(21))
            {
                var parts = r.GetString(21).Split('|');
                if (parts.Length == 2)
                {
                    st.ZoneAttempts = parts[0].Split(',').Select(int.Parse).ToArray();
                    st.ZoneMakes = parts[1].Split(',').Select(int.Parse).ToArray();
                }
            }
            list.Add(st);
        }
        return list;
    }

    // ------------------------------------------------------------------------------------------
    private static void CreateSchema(SqliteConnection conn)
    {
        Exec(conn, """
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS league(id INTEGER PRIMARY KEY, name TEXT, season INTEGER, rules_name TEXT, periods INTEGER, minutes_per_period INTEGER,
                shot_clock INTEGER, fouls_out INTEGER, bonus_threshold INTEGER, three_distance REAL,
                hard_cap INTEGER, min_salary INTEGER, roster_max INTEGER, roster_min INTEGER, rookie_scale_first INTEGER, next_player_id INTEGER, next_pick_id INTEGER);
            CREATE TABLE IF NOT EXISTS draft_pick(id INTEGER PRIMARY KEY, year INTEGER, round INTEGER, original_team_id INTEGER, owner_team_id INTEGER, protected_top INTEGER);
            CREATE TABLE IF NOT EXISTS team(id INTEGER PRIMARY KEY, city TEXT, name TEXT, abbr TEXT, conference INTEGER, division TEXT, arena_capacity INTEGER,
                pace REAL, three_emphasis INTEGER, crash_glass INTEGER, def_aggression INTEGER, star_usage INTEGER, primary_color TEXT, secondary_color TEXT, logo_style INTEGER, tactic_json TEXT);
            CREATE TABLE IF NOT EXISTS player(id INTEGER PRIMARY KEY, first_name TEXT, last_name TEXT, birth_date TEXT, nationality TEXT,
                height_cm INTEGER, wingspan_cm INTEGER, weight_kg INTEGER, primary_pos INTEGER, secondary_pos INTEGER,
                ca INTEGER, pa INTEGER, team_id INTEGER, status TEXT, attributes TEXT,
                professionalism INTEGER, ambition INTEGER, loyalty INTEGER, temperament INTEGER, pressure INTEGER, adaptability INTEGER,
                consistency INTEGER, big_games INTEGER, injury_proneness INTEGER, ego INTEGER,
                t_three INTEGER, t_drive INTEGER, t_post INTEGER, t_iso INTEGER, t_pass_first INTEGER, t_crash INTEGER, t_gamble INTEGER, rookie_season INTEGER,
                ethnicity TEXT, draft_year INTEGER, draft_round INTEGER, draft_pick INTEGER, face_file TEXT, draft_rights_team_id INTEGER);
            CREATE INDEX IF NOT EXISTS ix_player_team ON player(team_id);
            CREATE TABLE IF NOT EXISTS injury(player_id INTEGER, name TEXT, severity INTEGER, occurred TEXT, return_date TEXT, attr_loss INTEGER, current INTEGER);
            CREATE INDEX IF NOT EXISTS ix_injury_player ON injury(player_id);
            CREATE TABLE IF NOT EXISTS contract(player_id INTEGER PRIMARY KEY, years INTEGER, salary INTEGER, start_season INTEGER, team_option INTEGER, player_option INTEGER, buyout INTEGER);
            CREATE TABLE IF NOT EXISTS season_history(season INTEGER PRIMARY KEY, champion_team_id INTEGER, runner_up_team_id INTEGER, mvp_player_id INTEGER, scoring_leader_player_id INTEGER, finals_mvp_player_id INTEGER, awards_json TEXT);
            CREATE TABLE IF NOT EXISTS career(player_id INTEGER PRIMARY KEY, json TEXT);
            CREATE TABLE IF NOT EXISTS staff(id INTEGER PRIMARY KEY, first_name TEXT, last_name TEXT, birth_date TEXT, role INTEGER, coaching INTEGER, development INTEGER, fitness INTEGER, medical INTEGER, scouting INTEGER, reputation INTEGER, team_id INTEGER, salary INTEGER, years INTEGER);
            CREATE TABLE IF NOT EXISTS training(team_id INTEGER PRIMARY KEY, intensity INTEGER, focus TEXT);
            CREATE TABLE IF NOT EXISTS hall_of_fame(player_id INTEGER PRIMARY KEY, year INTEGER, score REAL);
            CREATE TABLE IF NOT EXISTS retired_number(team_id INTEGER, player_id INTEGER, PRIMARY KEY(team_id, player_id));
            CREATE TABLE IF NOT EXISTS season_record(season INTEGER, team_id INTEGER, wins INTEGER, losses INTEGER, PRIMARY KEY(season, team_id));
            CREATE TABLE IF NOT EXISTS player_stats(player_id INTEGER, season INTEGER, team_id INTEGER, games INTEGER, starts INTEGER, minutes REAL,
                points INTEGER, fgm INTEGER, fga INTEGER, tpm INTEGER, tpa INTEGER, ftm INTEGER, fta INTEGER, oreb INTEGER, dreb INTEGER,
                ast INTEGER, stl INTEGER, blk INTEGER, tov INTEGER, pf INTEGER, plus_minus INTEGER, zones TEXT, PRIMARY KEY(player_id, season));
            """);
    }

    /// <summary>
    /// Brings an older save's player table up to date (v3 added ethnicity and draft columns, v4 the portrait face).
    /// Missing ethnicity is backfilled on load from nationality and player id.
    /// </summary>
    private static void Migrate(SqliteConnection conn)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(player)";
            using var r = cmd.ExecuteReader();
            while (r.Read()) cols.Add(r.GetString(1));
        }
        if (cols.Count == 0) return; // new file; CreateSchema makes the table
        foreach (var (name, type) in new[] { ("ethnicity", "TEXT"), ("draft_year", "INTEGER"), ("draft_round", "INTEGER"), ("draft_pick", "INTEGER"), ("face_file", "TEXT"), ("draft_rights_team_id", "INTEGER") })
            if (!cols.Contains(name)) Exec(conn, $"ALTER TABLE player ADD COLUMN {name} {type}");
        // v6: overseas buyout clauses on contracts.
        var contractCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(contract)";
            using var r = cmd.ExecuteReader();
            while (r.Read()) contractCols.Add(r.GetString(1));
        }
        if (contractCols.Count > 0 && !contractCols.Contains("buyout")) Exec(conn, "ALTER TABLE contract ADD COLUMN buyout INTEGER");
        // v7: full tactics as JSON on each team.
        var teamCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(team)";
            using var r = cmd.ExecuteReader();
            while (r.Read()) teamCols.Add(r.GetString(1));
        }
        if (teamCols.Count > 0 && !teamCols.Contains("tactic_json")) Exec(conn, "ALTER TABLE team ADD COLUMN tactic_json TEXT");
    }

    internal static void Exec(SqliteConnection conn, string sql, params (string name, object value)[] args)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}
