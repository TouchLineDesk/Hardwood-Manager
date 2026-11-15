using System.Diagnostics;
using System.Runtime.InteropServices;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Season;

namespace Hardwood.Cli;

/// <summary>
/// Phase 0 of Danny's roadmap (#596): a repeatable baseline. Measures the game as it plays, without changing it:
///   hardwood bench [--seeds 2026,77] [--games 200] [--seasons 5] [--repeat 3] [--out baseline.md] [--skip-career]
/// It times one game with and without the play log, a regular season and playoffs, building a new real world,
/// a managed career's days and offseasons (with and without its daily saves), and the save's size, save time and
/// load time after 1, 5 and (with --seasons 25) 25 seasons. Each line records the seed, the machine and the command.
/// A fingerprint of the results (points scored) is printed with each measure, so a later run can tell
/// a behaviour change (different fingerprint) from a speed change (same fingerprint, different time).
/// Nothing here is used by the game itself.
/// </summary>
public static class Bench
{
    public static int Run(string[] args, Func<string[], string, string?> arg)
    {
        var seeds = (arg(args, "--seeds") ?? "2026,77").Split(',').Select(ulong.Parse).ToArray();
        int games = int.Parse(arg(args, "--games") ?? "200");
        int seasons = int.Parse(arg(args, "--seasons") ?? "5");
        int repeat = int.Parse(arg(args, "--repeat") ?? "3");
        bool skipCareer = args.Contains("--skip-career");
        var md = new List<string>();
        void W(string line) { md.Add(line); Console.WriteLine(line); }

        W($"# Hardwood bench, {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        W("");
        W($"- command: `hardwood {string.Join(' ', args)}`");
        W($"- machine: {Environment.ProcessorCount} cores, {RuntimeInformation.OSDescription.Trim()}, {RuntimeInformation.ProcessArchitecture}; .NET {Environment.Version}; server GC {System.Runtime.GCSettings.IsServerGC}; build {(IsDebug() ? "Debug" : "Release")}");
        W($"- seeds: {string.Join(", ", seeds)}; games {games}; repeats {repeat}; career seasons {(skipCareer ? 0 : seasons)}");
        W("");

        // 1. Building a new real world (the main cost of starting a new career).
        W("## New real world (`RealWorldData.NewWorld`)");
        W("| seed | run | ms | allocated MB | players |");
        W("|---|---|---|---|---|");
        foreach (var seed in seeds)
            for (int r = 0; r < repeat; r++)
            {
                var (ms, mb, (lg, gw)) = Time(() => Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026));
                W($"| {seed} | {r + 1} | {ms:F0} | {mb:F0} | {lg.AllActivePlayers.Count() + gw.Players.Count()} |");
            }
        W("");

        // 2. One game, with and without the play log. A fresh world per seed; pairs rotate through the league.
        W("## One NBA game (`GameSimulator.Simulate`)");
        W("| seed | play log | games | mean ms | median ms | p95 ms | allocated KB per game | fingerprint (points) |");
        W("|---|---|---|---|---|---|---|---|");
        foreach (var seed in seeds)
            foreach (var log in new[] { false, true })
            {
                var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026);
                var sim = new GameSimulator();
                var t = league.Teams;
                // Warm up the JIT on a separate pair so the timed games start from the same state each time.
                sim.Simulate(t[0], t[1], league.Rules, seed ^ 0xBEEF, keepLog: log);
                var times = new List<double>(); long points = 0, alloc = 0;
                for (int i = 0; i < games; i++)
                {
                    var home = t[(i * 7 + 2) % t.Count]; var away = t[(i * 11 + 5) % t.Count];
                    if (home == away) away = t[(i * 11 + 6) % t.Count];
                    long a0 = GC.GetAllocatedBytesForCurrentThread();
                    var sw = Stopwatch.StartNew();
                    var res = sim.Simulate(home, away, league.Rules, seed + (ulong)i, keepLog: log);
                    sw.Stop();
                    alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                    times.Add(sw.Elapsed.TotalMilliseconds);
                    points += res.Home.Points * 1000 + res.Away.Points;
                }
                times.Sort();
                W($"| {seed} | {(log ? "on" : "off")} | {games} | {times.Average():F2} | {times[times.Count / 2]:F2} | {times[(int)(times.Count * 0.95)]:F2} | {alloc / 1024.0 / games:F0} | {points} |");
            }
        W("");

        // 3. A full NBA regular season and playoffs, headless (`SeasonSimulator`), fresh world each time.
        W("## NBA regular season and playoffs (`SeasonSimulator.Run`)");
        W("| seed | run | games | s | games/s | allocated MB | champion | fingerprint (points) |");
        W("|---|---|---|---|---|---|---|---|");
        foreach (var seed in seeds)
            for (int r = 0; r < repeat; r++)
            {
                var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026);
                var rng = new Rng(seed ^ 0xABCDEF);
                var schedule = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), rng);
                var (ms, mb, outcome) = Time(() => new SeasonSimulator(rng).Run(league, schedule));
                long pts = outcome.RegularSeason.Sum(g => (long)g.Home.Points + g.Away.Points);
                int n = outcome.RegularSeason.Count + outcome.Playoffs.Sum(p => p.Item2.WinnerGames + p.Item2.LoserGames);
                W($"| {seed} | {r + 1} | {n} | {ms / 1000:F1} | {n / (ms / 1000):F0} | {mb:F0} | {outcome.Champion!.Abbreviation} | {pts} |");
            }
        W("");

        if (!skipCareer)
        {
            // 4. A managed career (the player's own game loop): days, offseasons and the save.
            foreach (var seed in seeds) Career(seed, seasons, W);
        }

        if (arg(args, "--out") is { } outFile) { File.WriteAllLines(outFile, md); Console.WriteLine($"Wrote {outFile}"); }
        return 0;
    }

    /// <summary>A managed real career at a mid-table NBA club, advanced day by day with no decisions (as if the player
    /// only pressed Continue). Times the first season without saving, then the same career saving as in play (every 7 days and at phase changes, D74 B), and
    /// after 1, 5 and 25 seasons: the save's size, one explicit save and a full load.</summary>
    private static void Career(ulong seed, int seasons, Action<string> W)
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026);
        int teamId = probe.Teams.OrderBy(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(14).Id;
        string club = probe.Teams.First(t => t.Id == teamId).Abbreviation;

        W($"## Managed career, seed {seed} ({club}, the 15th strongest NBA club; Continue only)");
        // Without saves: the simulation's own cost of a season of days.
        {
            var s0 = GameSession.NewGame(seed, teamId, savePath: null);
            var (ms, mb, days) = Time(() => AdvanceSeason(s0));
            W($"- first season **without saving**: {days.days} days in {ms / 1000:F1} s ({ms / Math.Max(1, days.days):F0} ms a day; slowest day {days.slowest:F0} ms on {days.slowestOn}); allocated {mb:F0} MB; record {s0.MyRecord.Wins}-{s0.MyRecord.Losses}");
        }
        var path = Path.Combine(Path.GetTempPath(), $"hw-bench-{seed}-{Guid.NewGuid():N}.hwsave");
        try
        {
            var (newMs, _, s) = Time(() => GameSession.NewGame(seed, teamId, path));
            W($"- new career with a save file: {newMs / 1000:F1} s; first save {new FileInfo(path).Length / 1e6:F1} MB");
            W("");
            W("| after season | days | advance s (saves as in play) | ms a day | slowest day ms | save MB | one save ms | load ms | record |");
            W("|---|---|---|---|---|---|---|---|---|");
            var checkpoints = new HashSet<int> { 1, 5, 10, 25, seasons };
            for (int n = 1; n <= seasons; n++)
            {
                var (ms, _, d) = Time(() => AdvanceSeason(s));
                if (!checkpoints.Contains(n)) { Console.Error.WriteLine($"season {n}: {ms / 1000:F0} s"); continue; }
                var rec = s.MyRecord;
                var (saveMs, _, _) = Time(() => { s.SaveNow(); return 0; });
                long size = new FileInfo(path).Length;
                var (loadMs, _, back) = Time(() => GameSession.Load(path));
                string same = back.Today == s.Today && back.MyRecord.Wins == rec.Wins ? "" : " (load differs!)";
                W($"| {n} | {d.days} | {ms / 1000:F1} | {ms / Math.Max(1, d.days):F0} | {d.slowest:F0} | {size / 1e6:F1} | {saveMs:F0} | {loadMs:F0} | {rec.Wins}-{rec.Losses}{same} |");
            }
            W("");
        }
        finally { foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(path) + "*")) File.Delete(f); }
    }

    /// <summary>Advance from wherever the career is to the same point a season later (the first day back in the preseason).</summary>
    private static (int days, double slowest, DateOnly slowestOn) AdvanceSeason(GameSession s)
    {
        int startSeason = s.League.Season, days = 0, guard = 0;
        double slowest = 0; DateOnly slowestOn = s.Today;
        bool sawOffseason = false;
        while (guard++ < 2000)
        {
            if (sawOffseason && s.Phase != SessionPhase.Offseason) break;
            var sw = Stopwatch.StartNew(); var on = s.Today;
            if (s.Phase == SessionPhase.Offseason) { sawOffseason = true; s.AdvanceOffseason(); }
            else s.AdvanceDay();
            sw.Stop(); days++;
            if (sw.Elapsed.TotalMilliseconds > slowest) { slowest = sw.Elapsed.TotalMilliseconds; slowestOn = on; }
        }
        if (s.League.Season == startSeason) Console.Error.WriteLine($"warning: the season didn't turn over in {guard} steps");
        return (days, slowest, slowestOn);
    }

    private static (double ms, double allocatedMb, T value) Time<T>(Func<T> f)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        long a0 = GC.GetTotalAllocatedBytes(precise: true);
        var sw = Stopwatch.StartNew();
        var v = f();
        sw.Stop();
        return (sw.Elapsed.TotalMilliseconds, (GC.GetTotalAllocatedBytes(precise: true) - a0) / 1e6, v);
    }

    private static bool IsDebug()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }
}
