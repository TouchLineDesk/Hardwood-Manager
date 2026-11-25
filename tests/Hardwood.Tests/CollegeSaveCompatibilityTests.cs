using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hardwood.Core.Persistence;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// K30 (run 178; ADR-1 in <c>claude/tech-debt-2026-11-23.md</c>): a college career saved by an older build still loads,
/// keeps everything it had (prestige and its memory, the recruiting board, pledges, the books, the season), plays on
/// the same way from two separate loads, and survives a save and reload unchanged. The fixture is
/// <c>SaveFixtures/college-v0165.hwsave.gz</c>, made by v0.165.0 (see the README there). Danny's first real career is a
/// college one, so every later build has to pass this.
/// </summary>
public class CollegeSaveCompatibilityTests
{
    public const string FixtureName = "college-v0165.hwsave.gz";

    /// <summary>The fixture's day and program, as made (README).</summary>
    private static readonly DateOnly FixtureDay = new(2027, 1, 8);

    public static IEnumerable<object[]> Fixtures() =>
        Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "SaveFixtures"), "college-v*.hwsave.gz").OrderBy(f => f).Select(f => new object[] { Path.GetFileName(f) });

    /// <summary>A fresh, writable copy of a fixture in its own folder.</summary>
    private static string Unpack(string fixture, out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), $"hw-college-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        using var src = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SaveFixtures", fixture)), CompressionMode.Decompress);
        using var dst = File.Create(path);
        src.CopyTo(dst);
        return path;
    }

    private static void Clean(string dir)
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    private static string F(double d) => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// What a college career is made of, as text: the date and phase, every program's prestige, its five-season memory,
    /// tradition, conference and roster, the season's games and Elo, the poll, every recruit's board state, pledges,
    /// rivalry series, alumni, awards and champions, your program's books, your job and the AD's confidence. Two careers
    /// with the same fingerprint are the same career as far as a player can tell.
    /// </summary>
    public static string Fingerprint(GameSession s)
    {
        var cw = s.World.College!;
        var sb = new StringBuilder();
        sb.Append($"day {s.Today:yyyy-MM-dd} phase {s.Phase} team {s.HumanTeam.Id} season {cw.Season.Year}\n");
        foreach (var t in cw.Programs.OrderBy(t => t.Id))
            sb.Append($"P{t.Id} {cw.ConferenceOf[t.Id]} {F(cw.Prestige[t.Id])} {F(cw.Tradition.GetValueOrDefault(t.Id))} "
                + $"[{WorldStore.FormText(cw.Form.GetValueOrDefault(t.Id))}] r{string.Join(",", t.Roster.Select(p => p.Id).OrderBy(i => i))}\n");
        foreach (var g in cw.Season.Games)
            sb.Append($"G{g.Date:MMdd} {g.HomeId}-{g.AwayId} {g.HomePts}-{g.AwayPts}\n");
        foreach (var (k, v) in cw.Season.Elo.OrderBy(kv => kv.Key)) sb.Append($"E{k} {F(Math.Round(v, 6))}\n");
        sb.Append("poll ").Append(string.Join(",", cw.Season.PollBefore.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"))).Append('\n');
        foreach (var (id, r) in cw.Recruits.OrderBy(kv => kv.Key))
            sb.Append($"R{id} {r.ClassOf} {r.Stars} {r.Rank} {r.CommittedTo} {r.CommittedOn} {r.CourtedBy} {F(Math.Round(r.Interest, 6))} "
                + $"{r.OnBoard} {r.Scouted} {r.Calls} {r.HomeVisit} {r.OfficialVisit} {r.Offered} {r.Pitch} {r.Promised} {r.FlipOffer}\n");
        sb.Append("walkons ").Append(string.Join(",", cw.WalkOnIds.OrderBy(i => i))).Append('\n');
        sb.Append("trail ").Append($"{cw.TrailHours} {cw.TrailWeek} {cw.RecruitingDay} {cw.Recruiting} {cw.RealAlumniSeeded} {cw.BrokenWordUntil}\n");
        sb.Append("pledges ").Append(JsonSerializer.Serialize(cw.Pledges)).Append('\n');
        foreach (var (k, v) in cw.Series.OrderBy(kv => kv.Key, StringComparer.Ordinal)) sb.Append($"S{k} {JsonSerializer.Serialize(v)}\n");
        foreach (var (k, v) in cw.Alumni.OrderBy(kv => kv.Key)) sb.Append($"A{k} {v.ProgramId} {v.Year}\n");
        sb.Append("awards ").Append(JsonSerializer.Serialize(cw.Awards)).Append('\n');
        sb.Append("history ").Append(JsonSerializer.Serialize(cw.History)).Append('\n');
        sb.Append("allamerican ").Append(JsonSerializer.Serialize(cw.AllAmerican)).Append('\n'); // CS11 (run 195)
        sb.Append("captains ").Append(JsonSerializer.Serialize(cw.Captains)).Append(' ').Append(string.Join(",", s.HumanTeam.CaptainIds.OrderBy(x => x))).Append('\n'); // run 196
        sb.Append("books ").Append(JsonSerializer.Serialize(s.HumanTeam.Money.Program, new JsonSerializerOptions { IncludeFields = true })).Append('\n');
        var job = s.Job.Current!;
        sb.Append($"job {job.League} {job.TeamId} {job.Season} {job.Goal} {job.TargetWins} {F(Math.Round(s.Job.Confidence, 6))} {s.Job.History.Count}\n");
        sb.Append($"record {s.MyBoardRecord.Wins}-{s.MyBoardRecord.Losses}\n");
        return sb.ToString();
    }

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    [Fact]
    public void A_reseeded_stream_is_the_same_as_a_new_one_from_that_seed()
    {
        var r = new Hardwood.Core.Rng(31);
        for (int i = 0; i < 1000; i++) r.NextULong();
        r.Reseed(77);
        var fresh = new Hardwood.Core.Rng(77);
        Assert.Equal(77UL, r.Seed);
        for (int i = 0; i < 100; i++) Assert.Equal(fresh.NextULong(), r.NextULong());
    }

    [Fact]
    public void There_is_a_college_fixture()
    {
        Assert.Contains(Fixtures(), f => (string)f[0] == FixtureName);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_field_an_old_college_save_wrote_is_still_read(string fixture)
    {
        // The college record is read by name into a positional record: a renamed or dropped parameter would silently
        // zero what an old save holds (the tech-debt pass's "silent loss"). Every key the old save wrote must still be read.
        var path = Unpack(fixture, out var dir);
        try
        {
            string json;
            using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT value FROM world_meta WHERE key = 'college'";
                json = (string)cmd.ExecuteScalar()!;
            }
            var root = JsonNode.Parse(json)!.AsObject();
            var college = typeof(WorldStore).GetNestedType("CollegeState", BindingFlags.NonPublic)!;
            var program = typeof(WorldStore).GetNestedType("ProgramState", BindingFlags.NonPublic)!;
            static HashSet<string> Params(Type t) => t.GetConstructors().Single(c => c.GetParameters().Length > 1).GetParameters().Select(p => p.Name!).ToHashSet();
            var cp = Params(college);
            foreach (var (key, _) in root) Assert.True(cp.Contains(key), $"the college record no longer reads '{key}' (add a CollegeMigrations step)");
            var pp = Params(program);
            foreach (var (key, _) in root["Programs"]!.AsArray()[0]!.AsObject()) Assert.True(pp.Contains(key), $"a program no longer reads '{key}'");

            // All the way down (recruits, the season, pledges, series, alumni, awards): read the old JSON with today's
            // record and write it back. Every key the old save wrote must come back, and nothing it held may come back null.
            var opts = new JsonSerializerOptions { IncludeFields = true };
            var again = JsonNode.Parse(JsonSerializer.Serialize(JsonSerializer.Deserialize(CollegeMigrations.Upgrade(json, 0), college, opts), college, opts))!;
            var lost = new List<string>();
            static void Walk(JsonNode? old, JsonNode? now, string at, List<string> lost)
            {
                if (old is null) return;
                if (now is null) { lost.Add(at); return; }
                if (old is JsonObject o)
                {
                    if (now is not JsonObject n) { lost.Add(at); return; }
                    foreach (var (k, v) in o)
                        if (!n.ContainsKey(k)) lost.Add($"{at}.{k}");
                        else Walk(v, n[k], $"{at}.{k}", lost);
                }
                else if (old is JsonArray a)
                {
                    if (now is not JsonArray n || n.Count != a.Count) { lost.Add(at); return; }
                    for (int i = 0; i < a.Count; i++) Walk(a[i], n[i], $"{at}[{i}]", lost);
                }
            }
            Walk(root, again, "college", lost);
            Assert.True(lost.Count == 0, $"{lost.Count} values an old save holds are no longer read, e.g. {string.Join(", ", lost.Take(5))} (add a CollegeMigrations step)");
        }
        finally { Clean(dir); }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void An_old_college_save_loads_with_everything_it_had(string fixture)
    {
        var path = Unpack(fixture, out var dir);
        try
        {
            var s = GameSession.Load(path);
            Assert.True(s.InCollege);
            Assert.Equal(FixtureDay, s.Today);
            Assert.Contains("Virginia Tech", s.HumanTeam.City + " " + s.HumanTeam.Name);
            var cw = s.World.College!;
            Assert.InRange(cw.Programs.Count, 360, 370);
            // Nothing came back zeroed: prestige and its memory, tradition, the board, the season, the books.
            Assert.All(cw.Programs, t => Assert.InRange(cw.Prestige[t.Id], 1, 100));
            // (Form marks start at the first summer in a new career; K27 seeds them. The fixture's are read and fingerprinted.)
            Assert.True(cw.Tradition.Count > 300);
            Assert.True(cw.Recruits.Count > 1000, $"{cw.Recruits.Count} recruits");
            Assert.Contains(cw.Recruits.Values, r => r.CommittedTo != null);
            Assert.Contains(cw.Recruits.Values, r => r.OnBoard || r.Offered);
            Assert.NotEmpty(cw.Series);
            Assert.NotEmpty(cw.Alumni);
            Assert.True(cw.Season.Games.Count(g => g.HomePts != null) > 1000);
            Assert.True(s.MyBoardRecord.Games >= 10, $"{s.MyBoardRecord.Games} games");
            Assert.InRange(s.HumanTeam.Roster.Count, 13, 15);
            Assert.Contains(s.Inbox, m => m.Title.StartsWith("Expectations"));
            var ids = s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.World.Players).Concat(cw.Players).Select(p => p.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }
        finally { Clean(dir); }
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void An_old_college_save_saves_and_reloads_unchanged_and_plays_on_the_same_after_a_reload(string fixture)
    {
        var pathA = Unpack(fixture, out var dirA);
        var pathB = Unpack(fixture, out var dirB);
        try
        {
            // Load and save at once: the reloaded career is the same career.
            var a = GameSession.Load(pathA);
            var before = Fingerprint(a);
            a.SaveNow();
            var a2 = GameSession.Load(pathA);
            Assert.Equal(before, Fingerprint(a2));

            // Playing on after the save and playing on after reloading it are the same week, day by day (the save
            // restarts the random streams from the seeds it writes).
            for (int i = 0; i < 7; i++)
            {
                a.AdvanceDay(); a2.AdvanceDay();
                Assert.Equal(Fingerprint(a), Fingerprint(a2));
            }
            Assert.NotEqual(before, Fingerprint(a2)); // the week was played (games, recruiting)
            Assert.Equal(FixtureDay.AddDays(7), a2.Today);

            // The untouched old save plays a week too, and that week survives its own save and reload.
            var b = GameSession.Load(pathB);
            Assert.Equal(before, Fingerprint(b));
            for (int i = 0; i < 7; i++) b.AdvanceDay();
            var week = Fingerprint(b);
            // And the week survives its own save and reload.
            b.SaveNow();
            Assert.Equal(week, Fingerprint(GameSession.Load(pathB)));
            using var conn = new SqliteConnection($"Data Source={pathB};Pooling=False");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT value FROM world_meta WHERE key = 'college_format'";
            Assert.Equal(CollegeMigrations.Current.ToString(), cmd.ExecuteScalar());
        }
        finally { Clean(dirA); Clean(dirB); }
    }

    [Fact]
    public void A_college_save_from_a_newer_build_is_refused_loudly_and_left_untouched()
    {
        var path = Unpack(FixtureName, out var dir);
        try
        {
            using (var conn = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "INSERT OR REPLACE INTO world_meta(key, value) VALUES('college_format', $v)";
                cmd.Parameters.AddWithValue("$v", (CollegeMigrations.Current + 1).ToString());
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();
            var original = File.ReadAllBytes(path);
            var ex = Assert.Throws<InvalidOperationException>(() => GameSession.Load(path));
            Assert.Equal(CollegeMigrations.NewerMessage(CollegeMigrations.Current + 1), ex.Message);
            SqliteConnection.ClearAllPools();
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally { Clean(dir); }
    }

    [Fact]
    public void The_migration_steps_run_in_order_and_cover_every_format()
    {
        Assert.Equal(Enumerable.Range(0, CollegeMigrations.Current), CollegeMigrations.Steps.Select(s => s.From));
        Assert.All(CollegeMigrations.Steps, s => Assert.False(string.IsNullOrWhiteSpace(s.What)));
        const string json = "{\"Recruiting\":true}";
        Assert.Equal(json, CollegeMigrations.Upgrade(json, 0)); // format 0 is read as it is
        Assert.Equal(json, CollegeMigrations.Upgrade(json, CollegeMigrations.Current));
        Assert.Throws<InvalidOperationException>(() => CollegeMigrations.Upgrade(json, CollegeMigrations.Current + 1));
    }

    /// <summary>
    /// Makes a college fixture with today's code (not a test; it runs only with <c>HW_MAKE_COLLEGE_FIXTURE=&lt;file.hwsave.gz&gt;</c>):
    /// Virginia Tech (ACC) on seed 31, a career started for 2026-27 and played to Jan 8, 2027 with the session's own
    /// recruiting (no presses), so it holds a part-played season, a recruiting board with commitments and offers, the
    /// five-season memory and the program's books.
    /// </summary>
    [Fact]
    public void Make_a_college_fixture_when_asked()
    {
        var target = Environment.GetEnvironmentVariable("HW_MAKE_COLLEGE_FIXTURE");
        if (string.IsNullOrEmpty(target)) return;
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var id = world.College!.Programs.Single(t => (t.City + " " + t.Name).Contains("Virginia Tech")).Id;
        var dir = Path.Combine(Path.GetTempPath(), $"hw-make-college-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = GameSession.NewGame(31, id, path);
            // A new college career's assistant works the recruiting trail (Delegated.Recruiting), so the board fills by itself.
            while (s.Today < FixtureDay) s.AdvanceDay();
            s.SaveNow();
            SqliteConnection.ClearAllPools();
            using (var src = File.OpenRead(path))
            using (var dst = new GZipStream(File.Create(target), CompressionLevel.SmallestSize))
                src.CopyTo(dst);
        }
        finally { Clean(dir); }
    }
}
