using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Desktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 116: the scouting report, the stopper and the double team key on the go-to man the engine feeds the
/// ball to (<see cref="GameSimulator.GoToWeight"/>), not simply the best-rated player.</summary>
public class Run116TheirStarTests
{
    private readonly ITestOutputHelper _out;
    public Run116TheirStarTests(ITestOutputHelper o) => _out = o;

    private static Player Make(int id, string last, Position pos, int ca, int all, params (Attr a, int v)[] set)
    {
        var p = new Player { Id = id, FirstName = "T", LastName = last, PrimaryPosition = pos, CurrentAbility = ca, PotentialAbility = ca };
        foreach (Attr a in Enum.GetValues<Attr>()) p.Attributes[a] = all;
        foreach (var (a, v) in set) p.Attributes[a] = v;
        return p;
    }

    [Fact]
    public void The_go_to_scorer_is_named_even_when_a_defensive_big_rates_higher()
    {
        var team = new Team { Id = 1, City = "Test", Name = "Testers", Abbreviation = "TST", Division = "Test" };
        // A rim protector rated 152 who barely shoots, and a 148 scorer who creates his own shot.
        var big = Make(1, "Anchor", Position.C, 152, 8, (Attr.Block, 20), (Attr.DefensiveRebounding, 20), (Attr.InteriorDefense, 20), (Attr.ShotCreation, 3), (Attr.ThreePoint, 3), (Attr.BallHandling, 3));
        var scorer = Make(2, "Bucket", Position.SG, 148, 10, (Attr.ShotCreation, 20), (Attr.ThreePoint, 19), (Attr.CloseShot, 18), (Attr.BallHandling, 18));
        team.Roster.Add(big); team.Roster.Add(scorer);
        for (int i = 3; i <= 9; i++) team.Roster.Add(Make(i, $"Role{i}", Position.SF, 110, 9));
        var (star, dbl) = CoachAi.TheirStar(team, null);
        Assert.Same(scorer, star);
        Assert.True(GameSimulator.GoToWeight(scorer, team.Tactic) > GameSimulator.GoToWeight(big, team.Tactic));
        Assert.False(dbl);   // 148 isn't a star, and he isn't 22 points clear of the 152 big
        // A dominant star who is also the go-to man is still doubled.
        scorer.CurrentAbility = 180;
        var (star2, dbl2) = CoachAi.TheirStar(team, null);
        Assert.Same(scorer, star2);
        Assert.True(dbl2);
    }

    [Fact]
    public void Every_real_NBA_team_names_its_shot_taker()
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        int differ = 0, doubled = 0;
        foreach (var t in league.Teams)
        {
            var (star, dbl) = CoachAi.TheirStar(t, null);
            var rot = CoachAi.Rotation(t, null);
            Assert.NotNull(star);
            Assert.Contains(star, rot);
            Assert.Equal(rot.Max(p => GameSimulator.GoToWeight(p, t.Tactic)), GameSimulator.GoToWeight(star!, t.Tactic));
            if (star != rot[0]) { differ++; _out.WriteLine($"{t.Abbreviation}: go-to {star!.Name} ({star.CurrentAbility}) over {rot[0].Name} ({rot[0].CurrentAbility})"); }
            if (dbl) doubled++;
        }
        _out.WriteLine($"go-to man differs from the top-rated player on {differ} of {league.Teams.Count}; worth doubling on {doubled}");
        Assert.True(differ < league.Teams.Count / 2, "on most teams the best player is also the go-to man");
    }

    [Theory]
    [InlineData(1.3, true)]
    [InlineData(1.2, true)]
    [InlineData(1.0, false)]
    public void The_assistant_folds_in_a_live_game_at_big_text(double scale, bool folds) =>
        Assert.Equal(folds, GuideViewModel.FoldsInLiveGame(scale));
}
