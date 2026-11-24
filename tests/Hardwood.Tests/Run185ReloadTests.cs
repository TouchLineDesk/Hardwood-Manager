using System.IO.Compression;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 185 (left from run 178's review; the launch plan's "same future after reload"): K30 proved that a college career
/// saved in January plays on the same after a reload. Danny will also save in March and in the summer, so this does the
/// same at those two points: the college fixture is played on to the NCAA tournament, saved there, and the career that
/// saved and the career that reloaded are compared day by day; then both play on into the summer, save again, and are
/// compared step by step through the summer's steps.
/// </summary>
public class Run185ReloadTests
{
    private readonly ITestOutputHelper _out;
    public Run185ReloadTests(ITestOutputHelper output) => _out = output;

    private static string Unpack(out string dir)
    {
        dir = Path.Combine(Path.GetTempPath(), $"hw-run185-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        using var src = new GZipStream(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SaveFixtures", CollegeSaveCompatibilityTests.FixtureName)), CompressionMode.Decompress);
        using var dst = File.Create(path);
        src.CopyTo(dst);
        return path;
    }

    /// <summary>The college fingerprint plus what a coach reads first: the inbox's newest titles, the summer's step and
    /// your roster.</summary>
    private static string Print(GameSession s) =>
        CollegeSaveCompatibilityTests.Fingerprint(s)
        + $"offseason {s.Offseason?.Phase}\n"
        + "inbox " + string.Join(" | ", s.Inbox.Take(12).Select(m => $"{m.Date:MMdd} {m.Title}")) + "\n"
        + "newest " + s.Inbox.FirstOrDefault()?.Body?.Replace("\n", " / ") + "\n"
        + "mine " + string.Join(",", s.HumanTeam.Roster.OrderBy(p => p.Id).Select(p => $"{p.Id}:{p.CurrentAbility}:{p.Morale:R}:{p.UnhappyWeeks}:{p.Injury?.ReturnDate}:{p.InjuryHistory.Count}")) + "\n"
        + $"chemistry {s.HumanTeam.Chemistry:R}\n";

    private static string Diff(string a, string b)
    {
        var x = a.Split('\n'); var y = b.Split('\n');
        for (int i = 0; i < Math.Min(x.Length, y.Length); i++) if (x[i] != y[i]) return $"line {i}: '{x[i]}' vs '{y[i]}'";
        return $"lengths {x.Length} vs {y.Length}";
    }

    /// <summary>Save <paramref name="a"/> now, load the save beside it, and play both on with <paramref name="step"/>,
    /// asserting they stay the same career at every step.</summary>
    private void SaveReloadAndCompare(GameSession a, string path, string where, int steps, Action<GameSession> step)
    {
        a.SaveNow();
        SqliteConnection.ClearAllPools();
        var copy = Path.Combine(Path.GetDirectoryName(path)!, $"reload-{where}.hwsave");
        File.Copy(path, copy, true);
        var b = GameSession.Load(copy);
        Assert.True(Print(a) == Print(b), $"{where}, on reload: {Diff(Print(a), Print(b))}");
        for (int i = 0; i < steps; i++)
        {
            step(a); step(b);
            var pa = Print(a); var pb = Print(b);
            Assert.True(pa == pb, $"{where}, step {i + 1} ({a.Today:yyyy-MM-dd} {a.Phase} {a.Offseason?.Phase}): {Diff(pa, pb)}");
        }
        _out.WriteLine($"{where}: {steps} steps the same, to {a.Today:yyyy-MM-dd} {a.Phase} {a.Offseason?.Phase}");
    }

    [Fact]
    public void A_college_players_injury_and_mood_survive_a_save_and_reload()
    {
        // Run 185 (High): the program you coach lost every injury (and the injury history) and every mood (morale, unhappy
        // weeks, a mood boost) on each load: college players weren't among those the injuries and moods were read back for.
        var path = Unpack(out var dir);
        try
        {
            var a = GameSession.Load(path);
            var hurt = a.HumanTeam.Roster.OrderBy(p => p.Id).First();
            var sulky = a.HumanTeam.Roster.OrderBy(p => p.Id).Last();
            var inj = new Hardwood.Core.Model.Injury { Name = "Sprained ankle", Severity = Hardwood.Core.Model.InjurySeverity.Moderate,
                Occurred = a.Today, ReturnDate = a.Today.AddDays(20), AttributeLoss = 0 };
            hurt.InjuryHistory.Add(inj); hurt.Injury = inj;
            sulky.Morale = 23.456789; sulky.UnhappyWeeks = 4; sulky.MoodBoost = 7.5; sulky.BoostUntil = a.Today.AddDays(30); sulky.BoostReason = "Run 185";
            a.HumanTeam.Chemistry = 44.4444;
            a.SaveNow();
            SqliteConnection.ClearAllPools();
            var b = GameSession.Load(path);
            var hurt2 = b.HumanTeam.Roster.Single(p => p.Id == hurt.Id);
            Assert.NotNull(hurt2.Injury);
            Assert.Equal("Sprained ankle", hurt2.Injury!.Name);
            Assert.Equal(a.Today.AddDays(20), hurt2.Injury.ReturnDate);
            Assert.False(hurt2.IsAvailable(b.Today));
            Assert.Equal(hurt.InjuryHistory.Count, hurt2.InjuryHistory.Count);
            var sulky2 = b.HumanTeam.Roster.Single(p => p.Id == sulky.Id);
            Assert.Equal(23.456789, sulky2.Morale);
            Assert.Equal(4, sulky2.UnhappyWeeks);
            Assert.Equal(7.5, sulky2.MoodBoost);
            Assert.Equal("Run 185", sulky2.BoostReason);
            Assert.Equal(44.4444, b.HumanTeam.Chemistry);
            // Everyone on the roster, not just the two touched.
            foreach (var p in a.HumanTeam.Roster)
                Assert.Equal(p.Morale, b.HumanTeam.Roster.Single(q => q.Id == p.Id).Morale);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void A_college_career_saved_in_March_and_in_the_summer_plays_on_the_same_after_a_reload()
    {
        var path = Unpack(out var dir);
        try
        {
            var a = GameSession.Load(path);
            Assert.True(a.InCollege);

            // To the NCAA tournament's first weekend (the First Four is about Mar 16-17; the Round of 64 Mar 18-19).
            var march = new DateOnly(2027, 3, 17);
            int guard = 0;
            while (a.Today < march && a.Phase != SessionPhase.Offseason && guard++ < 200) a.AdvanceDay();
            Assert.Equal(march, a.Today);
            var cw = a.World.College!;
            Assert.Contains(cw.Season.Games, g => g.Date >= march && g.Date <= march.AddDays(10)); // tournament games ahead
            SaveReloadAndCompare(a, path, "March", 10, s => s.AdvanceDay());

            // On into the summer: the first summer day, then every summer step.
            guard = 0;
            while (a.Phase != SessionPhase.Offseason && guard++ < 200) a.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, a.Phase);
            SaveReloadAndCompare(a, path, "summer", 8, s => { if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay(); });
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
