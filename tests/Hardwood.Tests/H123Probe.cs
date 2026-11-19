using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 121, H123's measurement: how good the made-up players abroad are, by league and age, on day one and after each summer,
/// and who tops the summer's free-agent pool. Not part of CI (set HW_H123_PROBE=seed:team[:seasons] to run it).
/// </summary>
public class H123Probe
{
    private readonly ITestOutputHelper _out;
    public H123Probe(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Generated_players_abroad()
    {
        var spec = Environment.GetEnvironmentVariable("HW_H123_PROBE");
        if (string.IsNullOrEmpty(spec)) return;
        var parts = spec.Split(':');
        ulong seed = ulong.Parse(parts[0]); int team = int.Parse(parts[1]), seasons = parts.Length > 2 ? int.Parse(parts[2]) : 1;
        if (team == -2)
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed);
            team = world[Hardwood.Core.World.UsaPyramid.Tier2].Clubs.OrderBy(t => Hardwood.Core.World.QuickSim.Strength(t)).ElementAt(15).Id;
        }
        var s = GameSession.NewGame(seed, team, null);
        foreach (var line in Snapshot(s, "day one")) _out.WriteLine(line);
        for (int k = 0; k < seasons; k++)
        {
            int season = s.League.Season; int guard = 0; bool sawFa = false;
            while (s.League.Season == season && guard++ < 3000)
            {
                if (s.Phase == SessionPhase.Offseason)
                {
                    if (!sawFa && s.Offseason?.Phase == OffseasonPhase.FreeAgency)
                    {
                        sawFa = true;
                        var real = Hardwood.Core.RealWorld.RealPlayers.Lines.Select(l => l.Name).ToHashSet();
                        var top = s.League.FreeAgents.OrderByDescending(p => p.CurrentAbility).Take(24).ToList();
                        _out.WriteLine($"summer {season}: FA pool {s.League.FreeAgents.Count}; top 24 generated {top.Count(p => !real.Contains(p.Name))}, generated 145+ {top.Count(p => !real.Contains(p.Name) && p.CurrentAbility >= 145)}");
                        _out.WriteLine("   " + string.Join(", ", top.Take(12).Select(p => $"{p.Name}{(real.Contains(p.Name) ? "*" : "")} {p.Age(s.Today)} {p.CurrentAbility}")));
                    }
                    s.AdvanceOffseason();
                }
                else s.AdvanceDay();
            }
            foreach (var line in Snapshot(s, $"after summer {season}")) _out.WriteLine(line);
        }
    }

    internal static List<string> Snapshot(GameSession s, string label)
    {
        var lines = new List<string>();
        var real = Hardwood.Core.RealWorld.RealPlayers.Lines.Select(l => l.Name).ToHashSet();
        var nba = s.League.Teams.ToHashSet();
        var abroad = s.World.Competitions.Where(c => !Hardwood.Core.World.UsaPyramid.IsPyramid(c.Profile.Key) && c.Clubs.Count > 0).ToList();
        var all = abroad.SelectMany(c => c.Clubs.SelectMany(t => t.Roster).Where(p => !real.Contains(p.Name))).ToList();
        lines.Add($"{label}: generated abroad {all.Count}; CA 135+ {all.Count(p => p.CurrentAbility >= 135)}, 140+ {all.Count(p => p.CurrentAbility >= 140)}, 145+ {all.Count(p => p.CurrentAbility >= 145)}, 150+ {all.Count(p => p.CurrentAbility >= 150)}; under 23 at 140+ {all.Count(p => p.CurrentAbility >= 140 && p.Age(s.Today) < 23)}");
        foreach (var c in abroad.OrderByDescending(c => c.Clubs.SelectMany(t => t.Roster).Max(p => p.CurrentAbility)))
        {
            var r = c.Clubs.SelectMany(t => t.Roster).ToList();
            lines.Add($"   {c.Profile.Short,-6} max {r.Max(p => p.CurrentAbility)} 140+ {r.Count(p => p.CurrentAbility >= 140)} 145+ {r.Count(p => p.CurrentAbility >= 145)} avg {r.Average(p => p.CurrentAbility):F1}");
        }
        var nbaGen = s.League.Teams.SelectMany(t => t.Roster).Where(p => !real.Contains(p.Name)).ToList();
        var top13 = s.League.Teams.Select(t => t.Roster.OrderByDescending(p => p.CurrentAbility).Take(13).Average(p => p.CurrentAbility)).Average();
        lines.Add($"   NBA: real {s.League.Teams.Sum(t => t.Roster.Count(p => real.Contains(p.Name)))}, generated {nbaGen.Count} (140+ {nbaGen.Count(p => p.CurrentAbility >= 140)}), top-13 CA {top13:F1}");
        return lines;
    }
}
