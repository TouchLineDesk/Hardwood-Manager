using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 122, H122's measurement: at each summer's contract decisions, the human club's expiring players (CA, age, ask)
/// against its wage room, and how many young stars (CA 150+, under 26) every NBA club lets go.
/// Not part of CI (set HW_H122_PROBE=seed:team[:seasons]; team -1 is the strongest club, the nba-contender career).
/// </summary>
public class H122Probe
{
    private readonly ITestOutputHelper _out;
    public H122Probe(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Young_stars_at_contract_time()
    {
        var spec = Environment.GetEnvironmentVariable("HW_H122_PROBE");
        if (string.IsNullOrEmpty(spec)) return;
        var parts = spec.Split(':');
        ulong seed = ulong.Parse(parts[0]); int team = int.Parse(parts[1]), seasons = parts.Length > 2 ? int.Parse(parts[2]) : 2;
        if (team < 0)
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed);
            team = probe.Teams.OrderByDescending(t => QuickSim.Strength(t)).First().Id;
        }
        var s = GameSession.NewGame(seed, team, null);
        int start = s.League.Season;
        int guard = 0;
        while (s.League.Season < start + seasons && guard++ < 5000)
        {
            if (s.Phase == SessionPhase.Offseason && s.Offseason?.Phase == OffseasonPhase.ReSigning)
            {
                var t = s.HumanTeam;
                _out.WriteLine($"summer {s.League.Season + 1} {t.Abbreviation}: budget {t.Money.WageBudget / 1e6:F1}M, committed {s.MyCommittedWages / 1e6:F1}M, room {s.MyWageRoom / 1e6:F1}M, allowance {s.KeepOwnAllowance() / 1e6:F1}M");
                foreach (var p in s.MyExpiring.OrderByDescending(p => p.CurrentAbility))
                {
                    var d = s.PeekDemand(p);
                    _out.WriteLine($"   {p.Name} CA {p.CurrentAbility} PA {p.PotentialAbility} age {p.Age(s.Today)} asks {d.Wage / 1e6:F2}M x{d.Years}  young star {ContractRules.IsYoungStar(p, s.Today)}  keepable {s.CanKeep(p, d.Wage)}");
                }
                var young = s.League.Teams.Where(x => x != t).SelectMany(x => s.Offseason.Expiring[x.Id].Select(p => (x, p)))
                    .Where(y => ContractRules.IsYoungStar(y.p, s.Today)).ToList();
                s.AdvanceOffseason();
                int kept = young.Count(y => y.p.TeamId == y.x.Id && y.p.Contract != null);
                _out.WriteLine($"   AI young stars expiring {young.Count}, kept {kept}: " + string.Join(", ", young.Select(y => $"{y.p.Name} {y.p.CurrentAbility} {y.x.Abbreviation} {(y.p.TeamId == y.x.Id ? "kept" : "let go")}")));
                continue;
            }
            if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay();
        }
    }
}
