using System.Security.Cryptography;
using System.Text;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 205 (the release candidate's gate, and H201's fresh-process check). Not a test: it runs only with
/// <c>HW_RC_PHASE</c> set, so each phase can run in its own <c>dotnet test</c> process:
/// <list type="bullet">
/// <item><c>save</c>: a new college career, played to <c>HW_RC_SAVE</c> (default Mar 16 2027), saved and the file copied,
/// then played on to <c>HW_RC_END</c> (default Feb 1 2028).</item>
/// <item><c>load</c>: the copied save loaded in this (fresh) process and played to the same end.</item>
/// <item><c>nosave</c>: the same new career played to the end without ever saving.</item>
/// </list>
/// Each phase writes one line per day (the date and a hash of run 198's full print) to <c>HW_RC_DIR/&lt;phase&gt;.txt</c>,
/// and the full print of every 30th step to <c>&lt;phase&gt;-full/</c>, so the files can be compared with <c>diff</c>.
/// <c>HW_RC_SEED</c> (default 31) and <c>HW_RC_TEAM</c> (default "UTEP") pick the career.
/// </summary>
public class Run205FreshProcessReloadProbe
{
    private static string Env(string k, string d) => Environment.GetEnvironmentVariable(k) is { Length: > 0 } v ? v : d;

    [Fact]
    public void Play_one_phase_when_asked()
    {
        var phase = Environment.GetEnvironmentVariable("HW_RC_PHASE");
        if (string.IsNullOrEmpty(phase)) return;
        var dir = Env("HW_RC_DIR", Path.Combine(Path.GetTempPath(), "hw-rc"));
        Directory.CreateDirectory(dir);
        ulong seed = ulong.Parse(Env("HW_RC_SEED", "31"));
        var team = Env("HW_RC_TEAM", "UTEP");
        var saveDay = DateOnly.Parse(Env("HW_RC_SAVE", "2027-03-16"));
        var end = DateOnly.Parse(Env("HW_RC_END", "2028-02-01"));
        var live = Path.Combine(dir, $"live-{phase}.hwsave");
        var copy = Path.Combine(dir, "reload.hwsave");

        GameSession s;
        if (phase == "load")
        {
            s = GameSession.Load(copy);
        }
        else
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
            var id = world.College!.Programs.First(t => (t.City + " " + t.Name).Contains(team) || (team == "UTEP" && t.City.Contains("El Paso"))).Id;
            s = GameSession.NewGame(seed, id, live);
            Assert.True(s.InCollege);
            while (s.Today < saveDay) s.AdvanceDay();
            if (phase == "save")
            {
                s.SaveNow();
                SqliteConnection.ClearAllPools();
                File.Copy(live, copy, true);
            }
        }

        var log = new StringBuilder();
        var full = Path.Combine(dir, $"{phase}-full");
        Directory.CreateDirectory(full);
        int steps = 0;
        void Note()
        {
            var p = Run198ReloadRolloverTests.Print(s);
            var h = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p)))[..16];
            log.Append($"{steps} {s.Today:yyyy-MM-dd} {s.Phase} {s.Offseason?.Phase} {h}\n");
            if (steps % 30 == 0) File.WriteAllText(Path.Combine(full, $"{steps:D4}.txt"), p);
        }
        Note();
        while (s.Today < end && steps++ < 600)
        {
            if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay();
            Note();
        }
        File.WriteAllText(Path.Combine(dir, $"{phase}.txt"), log.ToString());
        File.WriteAllText(Path.Combine(dir, $"{phase}-inbox.txt"),
            string.Join("\n", s.Inbox.Select(m => $"{m.Date:yyyy-MM-dd} {m.Title}")));
        SqliteConnection.ClearAllPools();
        Assert.True(s.Today >= end, $"stopped at {s.Today}");
    }
}
