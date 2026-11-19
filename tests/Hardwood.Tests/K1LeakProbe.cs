using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 119, K1's measurement: where the real players who are in the NBA on day one are after summer one, and why.
/// Not part of CI (set HW_K1_PROBE=seed:team[:seasons] to run it).
/// </summary>
public class K1LeakProbe
{
    private readonly ITestOutputHelper _out;
    public K1LeakProbe(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Where_summer_ones_real_players_go()
    {
        var spec = Environment.GetEnvironmentVariable("HW_K1_PROBE");
        if (string.IsNullOrEmpty(spec)) return;
        var parts = spec.Split(':');
        ulong seed = ulong.Parse(parts[0]); int team = int.Parse(parts[1]), seasons = parts.Length > 2 ? int.Parse(parts[2]) : 1;
        var s = GameSession.NewGame(seed, team, null);
        foreach (var line in Probe(s, seasons)) _out.WriteLine(line);
    }

    internal static List<string> Probe(GameSession s, int seasons)
    {
        var lines = new List<string>();
        var real = Hardwood.Core.RealWorld.RealPlayers.Lines.Select(l => l.Name).ToHashSet();
        int start = s.League.Season;
        for (int k = 0; k < seasons; k++)
        {
            var nba = s.League.Teams.ToHashSet();
            var snap = s.League.Teams.SelectMany(t => t.Roster.Where(p => real.Contains(p.Name)).Select(p => (p, t, end: p.Contract == null ? 0 : p.Contract.StartSeason + p.Contract.Years - 1, ca: p.CurrentAbility))).ToList();
            int season = s.League.Season;
            var before = s.League.Teams.SelectMany(t => t.Roster).Select(p => p.Id).ToHashSet();
            var origin = new Dictionary<int, string>();
            foreach (var c in s.World.Clubs) foreach (var p in c.Roster) origin[p.Id] = s.World.LeagueOf(c)?.Profile.Name ?? "?";
            foreach (var p in s.League.FreeAgents) origin.TryAdd(p.Id, "NBA free agent");
            int guard = 0;
            while (s.League.Season == season && guard++ < 2000)
            {
                if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay();
            }
            var nowNba = s.League.Teams.ToHashSet();
            HashSet<int> snapIds(HashSet<Team> ts) => before;
            var where = new Dictionary<int, string>();
            foreach (var c in s.World.Clubs.Concat(s.League.Teams).Distinct())
                foreach (var p in c.Roster) where[p.Id] = nowNba.Contains(c) ? "NBA" : s.World.LeagueOf(c)?.Profile.Name ?? "?";
            foreach (var p in s.League.FreeAgents) where.TryAdd(p.Id, "free agent");
            var rows = snap.Select(x =>
            {
                string now = where.TryGetValue(x.p.Id, out var w) ? w : "retired/gone";
                bool relegated = !nowNba.Contains(x.t);
                bool expired = x.end < season + 1;
                bool joinedDown = x.p.TeamId is { } tid && tid != x.t.Id && nba.Any(t => t.Id == tid) && !nowNba.Any(t => t.Id == tid);
                string cause = now == "NBA" ? "stayed" : now == "retired/gone" ? "retired" : relegated && x.p.TeamId == x.t.Id ? "went down with his club"
                    : joinedDown ? "signed with a club that went down" : relegated ? "club went down, left it" : expired ? "contract ran out" : "under contract, moved";
                return (x, now, cause);
            }).ToList();
            lines.Add($"season {season}: {snap.Count} real players in the NBA; after the summer {rows.Count(r => r.now == "NBA")} (clubs relegated: {string.Join(", ", nba.Except(nowNba).Select(t => t.Abbreviation))})");
            var stayers = nowNba.Where(nba.Contains).ToList();
            var gen = stayers.SelectMany(t => t.Roster.Where(p => !real.Contains(p.Name))).ToList();
            var newGen = gen.Where(p => !snapIds(nba).Contains(p.Id)).ToList();
            lines.Add($"  clubs that stayed up: {stayers.Count}, roster {stayers.Sum(t => t.Roster.Count)}, generated {gen.Count} (new this summer {newGen.Count}, CA avg {(newGen.Count > 0 ? newGen.Average(p => p.CurrentAbility) : 0):F0}, ages avg {(newGen.Count > 0 ? newGen.Average(p => p.Age(s.Today)) : 0):F1}); promoted clubs' rosters {nowNba.Except(nba).Sum(t => t.Roster.Count)} with real {nowNba.Except(nba).Sum(t => t.Roster.Count(p => real.Contains(p.Name)))}");
            lines.Add("     came from: " + string.Join(", ", newGen.GroupBy(p => origin.TryGetValue(p.Id, out var o) ? o : "college/new").OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()} (CA {g.Average(p => p.CurrentAbility):F0})")));
            foreach (var p in newGen.OrderByDescending(p => p.CurrentAbility).Take(5)) lines.Add($"     new: {p.Name} CA {p.CurrentAbility} age {p.Age(s.Today)} {(p.Contract is { } c ? $"${c.Salary / 1e6:F2}M x{c.Years}" : "")}");
            foreach (var g in rows.GroupBy(r => r.cause).OrderByDescending(g => g.Count()))
            {
                lines.Add($"  {g.Key}: {g.Count()} (CA 130+: {g.Count(r => r.x.ca >= 130)}, 115-129: {g.Count(r => r.x.ca is >= 115 and < 130)}, <115: {g.Count(r => r.x.ca < 115)}) → " +
                          string.Join(", ", g.GroupBy(r => r.now).OrderByDescending(h => h.Count()).Select(h => $"{h.Key} {h.Count()}")));
                foreach (var r in g.Where(r => r.cause != "stayed").OrderByDescending(r => r.x.ca).Take(6))
                    lines.Add($"     {r.x.p.Name} CA {r.x.ca} age {r.x.p.Age(s.Today)} was {r.x.t.Abbreviation} (deal to {r.x.end}) now {r.now} {(r.x.p.Contract is { } c ? $"${c.Salary / 1e6:F2}M x{c.Years}" : "")}");
            }
        }
        lines.Add($"after {s.League.Season - start} seasons: real players in the NBA {s.League.Teams.Sum(t => t.Roster.Count(p => real.Contains(p.Name)))}; real clubs in the NBA {s.League.Teams.Count(t => t.Id <= 30)}");
        return lines;
    }
}
