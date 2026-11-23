using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 163, K21 (with H155 and H173): March was too chalky (1-seeds won 73-75% of titles; 1 v 4 went 85% to the 1). March
/// Madness now plays with a team-wide shooting night per side, so a single game on a strange court can run hot or cold, and
/// talent gaps count a little less than in the season (1.6, not 1.9). Nothing else draws the night, so every other game
/// keeps its stream. Also H171 (an invited program books one fewer regular-season
/// game, so champions no longer play 42) and Florida's sixth Final Four in the programs pack.
/// </summary>
public class Run163MarchTests
{
    private readonly ITestOutputHelper _out;
    public Run163MarchTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void Only_March_Madness_has_a_shooting_night()
    {
        Assert.True(Rules.CollegeMadness.HasTeamForm);
        Assert.Equal(Rules.MadnessTeamFormSd, Rules.CollegeMadness.TeamFormSd);
        Assert.True(Rules.CollegeMadness.NeutralSite);
        Assert.Equal(Rules.MadnessTalentScale, Rules.CollegeMadness.TalentScale);
        Assert.True(Rules.MadnessTalentScale < Rules.College.TalentScale);
        foreach (var r in new[] { Rules.Nba, Rules.Fiba, Rules.College, Rules.CollegeNeutral })
        {
            Assert.False(r.HasTeamForm, r.Name);
            Assert.Equal(0, EngineConfig.Default.For(r).TeamFormSd);
        }
        Assert.Equal(Rules.MadnessTeamFormSd, EngineConfig.Default.For(Rules.CollegeMadness).TeamFormSd);

        // The college world hands the tournament (and only the tournament) the March rules, with the conference style on top.
        var (_, world) = RealWorldData.NewWorld(31, 2026, Hardwood.Core.Season.GameSession.DefaultFullSim);
        var cw = world.College!;
        int a = cw.Programs[0].Id, b = cw.Programs[1].Id;
        Assert.True(cw.GameRules(new CollegeGame { HomeId = a, AwayId = b, Stage = CollegeWorld.Madness, Event = CollegeWorld.Madness }).HasTeamForm);
        Assert.False(cw.GameRules(new CollegeGame { HomeId = a, AwayId = b, Stage = "Invitational", Event = "Island Classic" }).HasTeamForm);
        Assert.False(cw.GameRules(new CollegeGame { HomeId = a, AwayId = b, Stage = "Conference tournament", Event = "ACC tournament" }).HasTeamForm);
        Assert.False(cw.GameRules(new CollegeGame { HomeId = a, AwayId = b, Stage = "Conference" }).HasTeamForm);
    }

    [Fact]
    public void A_shooting_night_widens_the_game_to_game_spread()
    {
        var (_, world) = RealWorldData.NewWorld(2026, 2026, Hardwood.Core.Season.GameSession.DefaultFullSim);
        var cw = world.College!;
        var ordered = cw.Programs.OrderByDescending(t => Hardwood.Core.World.QuickSim.Strength(t)).ToList();
        var fav = ordered[10]; var dog = ordered[60];
        var sim = new GameSimulator();
        (double mean, double sd, double favWins) Run(Rules rules)
        {
            var m = Enumerable.Range(0, 300).Select(i =>
            {
                var r = sim.Simulate(fav, dog, rules, 1000UL + (ulong)i, false, new DateOnly(2027, 3, 20));
                return (double)(r.Home.Points - r.Away.Points);
            }).ToList();
            double mu = m.Average();
            return (mu, Math.Sqrt(m.Sum(x => (x - mu) * (x - mu)) / (m.Count - 1)), m.Count(x => x > 0) / (double)m.Count);
        }
        var plain = Run(Rules.CollegeNeutral);
        var march = Run(Rules.CollegeMadness);
        _out.WriteLine($"{fav.FullName} v {dog.FullName}: neutral {plain.mean:0.0} ± {plain.sd:0.0} ({plain.favWins:P0}), March {march.mean:0.0} ± {march.sd:0.0} ({march.favWins:P0})");
        Assert.True(march.sd > plain.sd + 1.0, $"{march.sd:0.0} vs {plain.sd:0.0}");
        Assert.True(march.favWins < plain.favWins, $"{march.favWins:P0} vs {plain.favWins:P0}");
        Assert.True(Math.Abs(march.mean - plain.mean) < 3.5, $"the night is centred: {march.mean:0.0} vs {plain.mean:0.0}");
    }

    [Fact]
    public void Champions_play_a_real_length_season()
    {
        var (_, world) = RealWorldData.NewWorld(31, 2026, Hardwood.Core.Season.GameSession.DefaultFullSim);
        var cw = world.College!;
        var rng = new Rng(31);
        cw.PlayThrough(new DateOnly(2027, 4, 30), new GameSimulator(), rng);
        var s = cw.Season;
        Assert.NotNull(s.ChampionId);
        int champ = s.ChampionId!.Value;
        int Games(int id, Func<CollegeGame, bool> f) => s.Games.Count(g => g.Played && (g.HomeId == id || g.AwayId == id) && f(g));
        int total = Games(champ, _ => true);
        _out.WriteLine($"champion {cw.Program(champ)!.FullName}: {total} games, {Games(champ, CollegeWorld.IsRegularSeason)} regular season");
        Assert.InRange(total, 36, 40); // real champions play 37-40 (H171: was 41-42)
        var regular = cw.Programs.Select(t => (t, n: Games(t.Id, CollegeWorld.IsRegularSeason))).ToList();
        foreach (var x in regular.Where(x => x.n > 32)) _out.WriteLine($"  {x.t.FullName} {x.n}");
        Assert.All(regular, x => Assert.InRange(x.n, 26, 32));
    }

    [Fact]
    public void Florida_has_six_Final_Fours()
    {
        var uf = RealColleges.Programs.Single(p => p.School == "Florida");
        Assert.Equal(6, uf.FinalFours); // 1994, 2000, 2006, 2007, 2014, 2025
        Assert.Equal(3, uf.Titles);
    }
}
