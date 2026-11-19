using Hardwood.Core.Season;
using Hardwood.Core.World;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 123, H70's measurement: board confidence through each season (the start, each month, the end of the regular season and
/// after the review), with the goal, the target and the record.
/// Not part of CI (set HW_H70_PROBE=seed:team[:seasons]; team -1 is the strongest club, -15 the 15th weakest (nba-real-mid)).
/// </summary>
public class H70Probe
{
    private readonly ITestOutputHelper _out;
    public H70Probe(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Confidence_through_the_seasons()
    {
        var spec = Environment.GetEnvironmentVariable("HW_H70_PROBE");
        if (string.IsNullOrEmpty(spec)) return;
        var parts = spec.Split(':');
        ulong seed = ulong.Parse(parts[0]); int team = int.Parse(parts[1]), seasons = parts.Length > 2 ? int.Parse(parts[2]) : 3;
        if (team < 0)
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed);
            team = team == -1 ? probe.Teams.OrderByDescending(t => QuickSim.Strength(t)).First().Id
                : probe.Teams.OrderBy(t => QuickSim.Strength(t)).ElementAt(-team - 1).Id;
        }
        var s = GameSession.NewGame(seed, team, null);
        int start = s.League.Season, guard = 0, month = -1;
        var phase = s.Phase;
        _out.WriteLine($"{s.HumanTeam.FullName}, owner patience {s.HumanTeam.Owner.Patience}, ambition {s.HumanTeam.Owner.Ambition}");
        while (s.League.Season < start + seasons && guard++ < 6000)
        {
            if (s.Phase != phase)
            {
                var r = s.MyRecord;
                _out.WriteLine($"  {s.Today:yyyy-MM-dd} {phase} → {s.Phase}: {r.Wins}-{r.Losses}, confidence {s.Job.Confidence:0.0}, goal {s.Job.Current?.Goal} target {s.Job.Current?.TargetWins}");
                phase = s.Phase;
            }
            if (s.Phase == SessionPhase.RegularSeason && s.Today.Month != month)
            {
                month = s.Today.Month;
                var r = s.MyRecord;
                _out.WriteLine($"  {s.Today:yyyy-MM-dd} {r.Wins}-{r.Losses} confidence {s.Job.Confidence:0.0} (goal {s.Job.Current?.Goal}, target {s.Job.Current?.TargetWins})");
            }
            if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay();
        }
        foreach (var m in s.Inbox.Where(m => m.Title.StartsWith("Season review") || m.Title.StartsWith("Expectations")))
            _out.WriteLine($"{m.Date:yyyy-MM-dd} {m.Title}: {m.Body}");
    }
}
