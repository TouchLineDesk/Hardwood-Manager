using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.International;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 212 (H219): an env-gated probe of national-team scoring under candidate rules. `HW_FIBA_PROBE=1`.</summary>
public class Run212FibaProbe
{
    private readonly ITestOutputHelper _out;
    public Run212FibaProbe(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Probe()
    {
        if (Environment.GetEnvironmentVariable("HW_FIBA_PROBE") != "1") return;
        foreach (var seed in new ulong[] { 31, 2026 })
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
            var me = world.College!.Programs.First(t => t.FullName.StartsWith("Virginia Tech"));
            var s = GameSession.NewGame(seed, me.Id, null);
            var day = new DateOnly(2027, 8, 27);
            var field = NationalTeams.Build(WorldCup.Entries(s, day), day).Where(t => t.Rank > 0).OrderBy(t => t.Rank).Take(32).ToList();
            var teams = field.Select((n, i) => { var t = new Team { Id = -800_000 - i, City = n.Name, Name = "", Abbreviation = n.Nation.Code, Division = "FIBA" }; foreach (var x in n.Squad) t.Roster.Add(x.Player); return t; }).ToList();
            var candidates = new (string Name, Rules R)[]
            {
                ("Fiba", Rules.Fiba with { NeutralSite = true }),
                ("Nba48", Rules.Nba with { NeutralSite = true }),
                ("FibaNational", Rules.FibaNational),
            };
            foreach (var (name, rules) in candidates)
            {
                var rng = new Rng(seed * 7919UL);
                var sim = new GameSimulator();
                double pts = 0, poss = 0, fga = 0, fgm = 0, tpa = 0, tpm = 0, fta = 0, tov = 0, orb = 0; int n = 0;
                for (int i = 0; i < teams.Count; i++)
                    for (int j = i + 1; j < teams.Count; j += 3)
                    {
                        var r = sim.Simulate(teams[i], teams[j], rules, rng.NextULong());
                        foreach (var b in new[] { r.Home, r.Away })
                        {
                            pts += b.Points; poss += b.Possessions; fga += b.FGA; fgm += b.FGM; tpa += b.ThreePA; tpm += b.Lines.Sum(l => l.ThreePM);
                            fta += b.FTA; tov += b.Turnovers; orb += b.Lines.Sum(l => l.OReb); n++;
                        }
                    }
                _out.WriteLine($"seed {seed} {name,-13} sides {n}: pts {pts / n:F1} poss {poss / n:F1} FG% {100 * fgm / fga:F1} 3PA {tpa / n:F1} 3P% {100 * tpm / tpa:F1} FTA {fta / n:F1} TOV {tov / n:F1} ORB {orb / n:F1}");
            }
        }
    }
}
