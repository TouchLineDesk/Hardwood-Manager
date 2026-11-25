using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 198 (H201, High): a new college career (UTEP, seed 31) saved on Mar 16 2027 and reloaded played the same spring
/// and summer, then a different next season: the schedule and the recruits' commitments on Oct 14, the games by January.
/// The cause: a load reads each competition's clubs abroad in id order, while the live career kept them in the order
/// they joined (a new world's pyramid, promotion and relegation). The world steps its clubs in list order, drawing as it
/// goes, so the clubs abroad developed differently from the summer's first step, and the session's stream followed.
/// The run-185 test starts from a loaded fixture, whose clubs were already in id order, so it couldn't see this.
/// </summary>
public class Run198ReloadRolloverTests
{
    private readonly ITestOutputHelper _out;
    public Run198ReloadRolloverTests(ITestOutputHelper output) => _out = output;

    /// <summary>The college fingerprint, the inbox's newest titles, your roster, and the pro and abroad worlds in list order
    /// (the order the world draws in).</summary>
    private static string Print(GameSession s)
    {
        var sb = new System.Text.StringBuilder(CollegeSaveCompatibilityTests.Fingerprint(s));
        sb.Append("inbox ").Append(string.Join(" | ", s.Inbox.Take(12).Select(m => $"{m.Date:MMdd} {m.Title}"))).Append('\n');
        sb.Append("mine ").Append(string.Join(",", s.HumanTeam.Roster.OrderBy(p => p.Id).Select(p => $"{p.Id}:{p.CurrentAbility}:{p.Morale:R}"))).Append('\n');
        if (s.Offseason is { } o) sb.Append("prospects ").Append(string.Join(",", o.Prospects.Select(p => $"{p.Id}:{p.CurrentAbility}"))).Append('\n');
        sb.Append("fa ").Append(string.Join(",", s.League.FreeAgents.Select(p => $"{p.Id}:{p.CurrentAbility}"))).Append('\n');
        sb.Append("retired ").Append(s.League.Retired.Count).Append('\n');
        foreach (var t in s.League.Teams)
            sb.Append($"T{t.Id} ").Append(string.Join(",", t.Roster.Select(p => $"{p.Id}:{p.CurrentAbility}:{p.PotentialAbility}"))).Append('\n');
        foreach (var t in s.World.Clubs)
            sb.Append($"C{t.Id} ").Append(string.Join(",", t.Roster.Select(p => $"{p.Id}:{p.CurrentAbility}"))).Append('\n');
        // Staff in list order (run 198's review): the summer's staff step draws in that order.
        sb.Append("freestaff ").Append(string.Join(",", s.League.FreeStaff.Select(x => x.Id))).Append('\n');
        foreach (var t in s.League.Teams.Concat(s.World.Clubs).Where(t => t.Staff.Count > 0))
            sb.Append($"S{t.Id} ").Append(string.Join(",", t.Staff.Select(x => x.Id))).Append('\n');
        return sb.ToString();
    }

    private static string Diff(string a, string b)
    {
        var x = a.Split('\n'); var y = b.Split('\n');
        int n = 0; var sb = new System.Text.StringBuilder();
        for (int i = 0; i < Math.Min(x.Length, y.Length) && n < 4; i++)
            if (x[i] != y[i]) { sb.Append($"\n line {i}: '{Cut(x[i])}'\n   vs '{Cut(y[i])}'"); n++; }
        return n == 0 ? $"lengths {x.Length} vs {y.Length}" : sb.ToString();
        static string Cut(string s) => s.Length > 160 ? s[..160] + "…" : s;
    }

    private static void Step(GameSession s)
    {
        if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay();
    }

    [Fact]
    public void A_new_college_career_saved_in_March_plays_the_same_next_season_after_a_reload()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var id = world.College!.Programs.Single(t => t.City.Contains("El Paso") || (t.City + " " + t.Name).Contains("UTEP")).Id;
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run198-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var a = GameSession.NewGame(31, id, path);
            Assert.True(a.InCollege);
            var march = new DateOnly(2027, 3, 16);
            while (a.Today < march) a.AdvanceDay();
            a.SaveNow();
            // After a save the clubs abroad are in the order a load reads them.
            foreach (var comp in a.World.Competitions)
                Assert.True(comp.Clubs.Select(t => t.Id).SequenceEqual(comp.Clubs.Select(t => t.Id).Order()), $"{comp.Key}'s clubs out of id order after a save");
            Assert.True(a.League.Teams.Select(t => t.Id).SequenceEqual(a.League.Teams.Select(t => t.Id).Order()), "the NBA's teams out of id order after a save");
            foreach (var t in a.League.Teams.Concat(a.World.Clubs).Prepend(a.HumanTeam))
                Assert.True(t.Staff.Select(x => x.Id).SequenceEqual(t.Staff.Select(x => x.Id).Order()), $"{t.FullName}'s staff out of id order after a save");
            Assert.True(a.League.FreeStaff.Select(x => x.Id).SequenceEqual(a.League.FreeStaff.Select(x => x.Id).Order()), "free staff out of id order after a save");
            SqliteConnection.ClearAllPools();
            var copy = Path.Combine(dir, "reload.hwsave");
            File.Copy(path, copy, true);
            var b = GameSession.Load(copy);
            Assert.True(Print(a) == Print(b), $"on reload: {Diff(Print(a), Print(b))}");
            // Through the summer, the rollover (Oct 14) and the next season's Monday recruiting steps to February.
            var end = new DateOnly(2028, 2, 1);
            int steps = 0;
            GameSession? c = null;
            while (a.Today < end && steps++ < 500)
            {
                Step(a); Step(b); if (c != null) Step(c);
                var pa = Print(a); var pb = Print(b);
                Assert.True(pa == pb, $"step {steps} ({a.Today:yyyy-MM-dd} {a.Phase} {a.Offseason?.Phase}): {Diff(pa, pb)}");
                if (c != null) Assert.True(pa == Print(c), $"Oct 14 save, step {steps} ({a.Today:yyyy-MM-dd}): {Diff(pa, Print(c))}");
                if (c == null && a.Phase == SessionPhase.Preseason)
                {
                    // The hunter's second case: a save on the new season's first day, reloaded, to the Monday recruiting steps.
                    a.SaveNow(); b.SaveNow();
                    SqliteConnection.ClearAllPools();
                    var oct = Path.Combine(dir, "reload-oct.hwsave");
                    File.Copy(path, oct, true);
                    c = GameSession.Load(oct);
                    Assert.True(Print(a) == Print(c), $"Oct 14 save, on reload: {Diff(Print(a), Print(c))}");
                }
            }
            Assert.NotNull(c);
            Assert.True(a.Today >= end, $"stopped at {a.Today}");
            _out.WriteLine($"{steps} steps the same, to {a.Today:yyyy-MM-dd}");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
