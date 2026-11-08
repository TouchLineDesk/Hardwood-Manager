using System.Diagnostics;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Persistence;
using Hardwood.Core.Season;

namespace Hardwood.Cli;

/// <summary>
/// Console runner for the prototype:
///   hardwood season [--seed N]          generate a league, play 82 games + playoffs, print standings, leaders, averages
///   hardwood game [--seed N] [--log]     generate a league, play one game, print the box score (and play log)
///   hardwood roster [--seed N] [--team ABBR]
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var cmd = args.Length > 0 ? args[0] : "season";
        ulong seed = (ulong)(ArgValue(args, "--seed") is { } s ? long.Parse(s) : 2026);
        var today = new DateOnly(2026, 10, 1);

        var world = new WorldGenerator(new Rng(seed));
        var league = world.Generate("North American Basketball League", 2026, today);

        switch (cmd)
        {
            case "season": return RunSeason(league, seed, today);
            case "game": return RunGame(league, seed, args.Contains("--log"));
            case "roster": return ShowRoster(league, ArgValue(args, "--team") ?? "BOS");
            case "sim-years": return SimYears(league, seed, int.Parse(ArgValue(args, "--years") ?? "10"), ArgValue(args, "--save") ?? "hardwood.save");
            case "load": return LoadSave(ArgValue(args, "--save") ?? "hardwood.save");
            default:
                Console.Error.WriteLine("Usage: hardwood season|game|roster|sim-years|load [--seed N] [--log] [--team ABBR] [--years N] [--save FILE]");
                return 1;
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int RunSeason(League league, ulong seed, DateOnly today)
    {
        var rng = new Rng(seed ^ 0xABCDEF);
        var schedule = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), rng);
        Console.WriteLine($"{league.Name} {league.Season}-{league.Season + 1}: {league.Teams.Count} teams, {schedule.Count} games, seed {seed}");
        Console.WriteLine($"Season runs {schedule.First().Date} to {schedule.Last().Date}");

        var sw = Stopwatch.StartNew();
        var season = new SeasonSimulator(rng);
        var outcome = season.Run(league, schedule);
        sw.Stop();
        Console.WriteLine($"Simulated regular season + playoffs in {sw.Elapsed.TotalSeconds:F1}s ({schedule.Count / sw.Elapsed.TotalSeconds:F0} games/s)\n");

        foreach (var conf in new[] { Conference.East, Conference.West })
        {
            Console.WriteLine($"=== {conf} ===");
            Console.WriteLine($"{"#",2} {"Team",-24} {"W",3} {"L",3} {"Pct",5} {"Diff",6} {"Home",7}");
            int rank = 1;
            foreach (var r in outcome.Standings.Conference(conf))
                Console.WriteLine($"{rank++,2} {r.Team.FullName,-24} {r.Wins,3} {r.Losses,3} {r.WinPct,5:F3} {r.PointDiff,6:+0.0;-0.0} {r.HomeWins + "-" + r.HomeLosses,7}");
            Console.WriteLine();
        }

        Console.WriteLine("=== Playoffs ===");
        foreach (var (round, s) in outcome.Playoffs)
            Console.WriteLine($"{round,-22} {s.Winner.FullName,-24} def. {s.Loser.FullName,-24} {s.WinnerGames}-{s.LoserGames}");
        Console.WriteLine($"\nCHAMPION: {outcome.Champion!.FullName}\n");

        PrintLeaders(league, outcome.RegularSeason);

        var injuries = outcome.RegularSeason.SelectMany(g => g.Injuries).ToList();
        Console.WriteLine($"=== Injuries: {injuries.Count} in the regular season ({injuries.Count / 30.0:F1} per team), avg {injuries.Average(x => x.injury.ReturnDate.DayNumber - x.injury.Occurred.DayNumber):F1} days ===");
        foreach (var g in injuries.GroupBy(x => x.injury.Severity).OrderBy(g => g.Key))
            Console.WriteLine($"  {g.Key,-13} {g.Count(),4}   e.g. {g.First().player.Name}: {g.First().injury.Name}, {g.First().injury.ReturnDate.DayNumber - g.First().injury.Occurred.DayNumber} days");
        Console.WriteLine();

        Console.WriteLine("=== League averages vs. real-world targets ===");
        Console.WriteLine(LeagueAverages.From(outcome.RegularSeason).Report());
        return 0;
    }

    private static void PrintLeaders(League league, List<GameResult> games)
    {
        var totals = new Dictionary<Player, (int g, double min, int pts, int reb, int ast, int fgm, int fga, int tpm, int tpa)>();
        foreach (var g in games)
        foreach (var box in new[] { g.Home, g.Away })
        foreach (var l in box.Lines)
        {
            if (l.Seconds <= 0) continue;
            var t = totals.GetValueOrDefault(l.Player);
            totals[l.Player] = (t.g + 1, t.min + l.Minutes, t.pts + l.Points, t.reb + l.Rebounds, t.ast + l.Assists, t.fgm + l.FGM, t.fga + l.FGA, t.tpm + l.ThreePM, t.tpa + l.ThreePA);
        }

        var teamOf = league.Teams.SelectMany(t => t.Roster.Select(p => (p, t))).ToDictionary(x => x.p, x => x.t);
        Console.WriteLine("=== Scoring leaders (min 50 games) ===");
        Console.WriteLine($"{"Player",-24} {"Team",-4} {"Pos",-3} {"CA",3} {"GP",3} {"MPG",5} {"PPG",5} {"RPG",5} {"APG",5} {"FG%",5} {"3P%",5}");
        foreach (var (p, t) in totals.Where(kv => kv.Value.g >= 50).OrderByDescending(kv => (double)kv.Value.pts / kv.Value.g).Take(15))
        {
            Console.WriteLine($"{p.Name,-24} {teamOf[p].Abbreviation,-4} {p.PrimaryPosition,-3} {p.CurrentAbility,3} {t.g,3} {t.min / t.g,5:F1} {(double)t.pts / t.g,5:F1} {(double)t.reb / t.g,5:F1} {(double)t.ast / t.g,5:F1} {(t.fga > 0 ? 100.0 * t.fgm / t.fga : 0),5:F1} {(t.tpa > 0 ? 100.0 * t.tpm / t.tpa : 0),5:F1}");
        }
        Console.WriteLine();
    }

    private static int RunGame(League league, ulong seed, bool log)
    {
        var home = league.Teams[0];
        var away = league.Teams[15];
        var sim = new GameSimulator();
        var r = sim.Simulate(home, away, league.Rules, seed, keepLog: log);
        PrintBox(r);
        if (log) foreach (var line in r.PlayLog) Console.WriteLine(line);
        return 0;
    }

    public static void PrintBox(GameResult r)
    {
        Console.WriteLine(r.Summary());
        Console.WriteLine($"{"",-10} {string.Join(" ", r.Away.PointsByPeriod.Select(p => $"{p,3}"))} | {r.Away.Points,3}   ({r.Away.Team.Abbreviation})");
        Console.WriteLine($"{"",-10} {string.Join(" ", r.Home.PointsByPeriod.Select(p => $"{p,3}"))} | {r.Home.Points,3}   ({r.Home.Team.Abbreviation})\n");
        foreach (var box in new[] { r.Away, r.Home })
        {
            Console.WriteLine($"--- {box.Team.FullName} ({box.Possessions} poss) ---");
            Console.WriteLine($"{"Player",-24} {"MIN",5} {"PTS",3} {"FG",5} {"3P",5} {"FT",5} {"REB",3} {"AST",3} {"ST",2} {"BL",2} {"TO",2} {"PF",2} {"+/-",4}");
            foreach (var l in box.Lines.Where(l => l.Seconds > 0).OrderByDescending(l => l.Started).ThenByDescending(l => l.Seconds))
                Console.WriteLine(l);
            Console.WriteLine($"{"TOTAL",-24} {"",5} {box.Points,3} {box.FGM,2}-{box.FGA,-2} {box.ThreePM,2}-{box.ThreePA,-2} {box.FTM,2}-{box.FTA,-2} {box.OReb + box.DReb,3} {box.Assists,3} {box.Steals,2} {box.Blocks,2} {box.Turnovers,2} {box.Fouls,2}\n");
        }
    }

    private static int SimYears(League league, ulong seed, int years, string savePath)
    {
        if (File.Exists(savePath)) File.Delete(savePath);
        var save = new SaveFile(savePath);
        var career = new Career(new Rng(seed ^ 0x5EA50), save);
        Console.WriteLine($"Simulating {years} seasons of {league.Name} from {league.Season}-{league.Season + 1}, saving to {savePath}\n");
        Console.WriteLine($"{"Season",-9} {"Champion",-24} {"Runner-up",-24} {"Rec",-6} {"MVP",-26} {"Scoring leader",-30} {"Lg CA",5} {"Ret",3} {"Draft #1",-26}");

        var sw = Stopwatch.StartNew();
        for (int y = 0; y < years; y++)
        {
            var played = career.PlaySeason(league);
            var h = league.History.Last();
            var champ = league.TeamById(h.ChampionTeamId);
            var ru = league.TeamById(h.RunnerUpTeamId);
            var rec = played.Outcome.Standings[champ];
            var all = league.AllActivePlayers.Concat(league.FreeAgents).Concat(league.Retired).ToDictionary(p => p.Id);
            string Name(int? id, Dictionary<int, PlayerSeasonStats> st) => id is { } i && all.TryGetValue(i, out var p) ? $"{p.Name} ({st[i].Ppg:F1})" : "-";
            double lgCa = league.AllActivePlayers.Average(p => p.CurrentAbility);
            var first = played.Offseason.Draft.First();
            Console.WriteLine($"{played.Season}-{(played.Season + 1) % 100:00} {champ.FullName,-24} {ru.FullName,-24} {rec.Wins + "-" + rec.Losses,-6} {Name(h.MvpPlayerId, played.Stats),-26} {Name(h.ScoringLeaderPlayerId, played.Stats),-30} {lgCa,5:F1} {played.Offseason.Retired.Count,3} {first.Player.Name + " (" + first.Team.Abbreviation + ")",-26}");
        }
        sw.Stop();
        Console.WriteLine($"\n{years} seasons in {sw.Elapsed.TotalSeconds:F1}s. Save file: {new FileInfo(savePath).Length / 1024} KB");

        // Age and ability profile of the league after the run.
        var today = new DateOnly(league.Season, 10, 1);
        var active = league.AllActivePlayers.ToList();
        Console.WriteLine($"\nActive players: {active.Count}  avg age {active.Average(p => p.Age(today)):F1}  avg CA {active.Average(p => p.CurrentAbility):F1}  max CA {active.Max(p => p.CurrentAbility)}  free agents {league.FreeAgents.Count}  retired {league.Retired.Count}");
        Console.WriteLine("Age distribution: " + string.Join("  ", active.GroupBy(p => p.Age(today) / 3 * 3).OrderBy(g => g.Key).Select(g => $"{g.Key}-{g.Key + 2}:{g.Count()}")));
        var best = active.OrderByDescending(p => p.CurrentAbility).First();
        Console.WriteLine($"Best player: {best} age {best.Age(today)} on {league.TeamById(best.TeamId!.Value).FullName}");
        Console.WriteLine("Payrolls: " + string.Join(" ", league.Teams.OrderByDescending(ContractMarket.Payroll).Take(5).Select(t => $"{t.Abbreviation} ${ContractMarket.Payroll(t) / 1e6:F0}M")) + $"  (cap ${league.Finance.HardCap / 1e6:F0}M)");
        Console.WriteLine("Titles: " + string.Join(", ", league.History.GroupBy(x => x.ChampionTeamId).OrderByDescending(g => g.Count()).Select(g => $"{league.TeamById(g.Key).Abbreviation} {g.Count()}")));
        return 0;
    }

    private static int LoadSave(string savePath)
    {
        if (!File.Exists(savePath)) { Console.Error.WriteLine($"No save at {savePath}"); return 1; }
        var league = new SaveFile(savePath).Load();
        Console.WriteLine($"Loaded {league.Name}, season {league.Season}-{league.Season + 1}: {league.Teams.Count} teams, {league.AllActivePlayers.Count()} active players, {league.History.Count} seasons of history");
        foreach (var h in league.History)
            Console.WriteLine($"  {h.Season}-{(h.Season + 1) % 100:00}: {league.TeamById(h.ChampionTeamId).FullName} over {league.TeamById(h.RunnerUpTeamId).FullName}");
        return 0;
    }

    private static int ShowRoster(League league, string abbr)
    {
        var team = league.Teams.FirstOrDefault(t => t.Abbreviation.Equals(abbr, StringComparison.OrdinalIgnoreCase));
        if (team == null) { Console.Error.WriteLine($"No team {abbr}"); return 1; }
        var today = new DateOnly(2026, 10, 1);
        Console.WriteLine($"{team.FullName} ({team.Conference}, {team.Division})  payroll ${team.Roster.Sum(p => p.Contract!.Salary) / 1_000_000.0:F1}M");
        Console.WriteLine($"{"Player",-24} {"Pos",-5} {"Age",3} {"Ht",5} {"CA",4} {"PA",4} {"OVR",5} {"Sho",4} {"Ply",4} {"Def",4} {"Men",4} {"Phy",4} {"Salary",8}");
        foreach (var p in team.Roster.OrderByDescending(p => p.Overall))
        {
            var a = p.Attributes;
            string pos = p.PrimaryPosition + (p.SecondaryPosition is { } s2 ? "/" + s2 : "");
            Console.WriteLine($"{p.Name,-24} {pos,-5} {p.Age(today),3} {p.HeightCm,5} {p.CurrentAbility,4} {p.PotentialAbility,4} {p.Overall,5:F1} {a.Average(AttrGroup.Shooting),4:F1} {a.Average(AttrGroup.Playmaking),4:F1} {a.Average(AttrGroup.Defense),4:F1} {a.Average(AttrGroup.Mental),4:F1} {a.Average(AttrGroup.Physical),4:F1} {"$" + (p.Contract!.Salary / 1_000_000.0).ToString("F1") + "M",8}");
        }
        return 0;
    }
}
