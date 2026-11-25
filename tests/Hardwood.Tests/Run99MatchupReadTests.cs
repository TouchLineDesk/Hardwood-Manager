using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Desktop.ViewModels;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Critic K2 (run 99): the tactics advice contradicted itself. The assistant's pick said "poor matchup", the board said
/// "Big disadvantage" and every card, the game plan included, said "Plays into their hands". One matchup model now feeds
/// the option lines, the board, the cards and the assistant.
/// </summary>
public class Run99MatchupReadTests
{
    private static IEnumerable<(Team us, Team them)> Pairs(int seed)
    {
        var lg = RealWorldData.NewWorld((ulong)seed).League;
        var nba = lg.Teams.Where(t => t.Roster.Count >= 8).Take(30).ToList();
        for (int i = 0; i < nba.Count; i++)
            yield return (nba[i], nba[(i * 7 + 3) % nba.Count]);
    }

    [Theory]
    [InlineData(31)]
    [InlineData(77)]
    public void The_game_plan_scores_at_least_as_well_as_every_alternative_and_is_the_assistants_pick(int seed)
    {
        int checkedPairs = 0;
        foreach (var (us, them) in Pairs(seed))
        {
            if (us == them) continue;
            var setups = TacticSetups.Suggest(us, them, null);
            var plan = setups.Single(s => s.Key == "plan");
            foreach (var s in setups) Assert.True(plan.Value >= s.Value - 1e-12, $"{us.Abbreviation} v {them.Abbreviation}: {s.Key} {s.Value:0.000} > plan {plan.Value:0.000}");
            var pick = setups.Single(s => s.Recommended);
            Assert.Equal(setups.Max(s => s.Value), pick.Value, 12);
            // The plan's value is the best over every system and scheme, not just the cards.
            var read = MatchupRead.Of(us, them, null);
            var rot = CoachAi.Rotation(us, null);
            foreach (var sys in Enum.GetValues<OffensiveSystem>())
                foreach (var sch in Tactics.Schemes(college: false))   // run 194: the college zones aren't the pros' to pick
                    Assert.True(plan.Value >= MatchupRead.Value(Systems.Fit(sys, rot), Systems.Fit(sch, rot), read.Edge(sys, sch), sys, sch) - 1e-12);
            checkedPairs++;
        }
        Assert.True(checkedPairs >= 25);
    }

    [Fact]
    public void A_card_never_says_it_plays_into_their_hands_unless_its_worse_than_the_plan_in_use()
    {
        foreach (var (us, them) in Pairs(31))
        {
            if (us == them) continue;
            var read = MatchupRead.Of(us, them, null);
            var setups = TacticSetups.Suggest(us, them, null);
            foreach (var current in setups)
            {
                double cur = current.Edge;
                foreach (var s in setups)
                {
                    var text = SetupCard.EdgeOf(s, cur);
                    if (text == "Plays into their hands") Assert.True(s.Edge < cur && !s.Recommended);
                    if (s.Recommended) Assert.NotEqual("Plays into their hands", text);
                    if (s.Edge >= MatchupRead.PlanNotable) Assert.Equal("Beats their plan", text);
                }
                // The card in use never calls itself worse than itself.
                Assert.NotEqual("Plays into their hands", SetupCard.EdgeOf(current, cur));
            }
            // "Counters both" only when it does.
            var plan = setups.Single(s => s.Key == "plan");
            if (plan.Pitch.Contains("counters both"))
                Assert.True(read.Attack(plan.System) >= MatchupRead.Notable && read.Defence(plan.Scheme) >= MatchupRead.Notable);
            // "Least-bad" only when nothing matches up better on that side.
            if (read.WhyScheme(plan.Scheme).Contains("least-bad"))
                Assert.All(Enum.GetValues<DefensiveScheme>(), d => Assert.True(read.Defence(d) <= read.Defence(plan.Scheme) + 1e-9));
            if (read.WhySystem(plan.System).Contains("least-bad"))
                Assert.All(Enum.GetValues<OffensiveSystem>(), x => Assert.True(read.Attack(x) <= read.Attack(plan.System) + 1e-9));
        }
    }

    [Fact]
    public void The_board_and_the_option_lines_use_the_same_words_and_the_assistant_explains_a_poor_pick()
    {
        // A scheme the board calls a big disadvantage: their post-up against our switching.
        var read = new MatchupRead(OffensiveSystem.PostCentric, DefensiveScheme.Man);
        double sw = read.Defence(DefensiveScheme.SwitchEverything);
        Assert.Equal("Big disadvantage", MatchupRead.Words(sw));
        Assert.Equal("poor matchup", MatchupRead.Tag(sw));
        Assert.Contains("fit is worth more", read.WhyScheme(DefensiveScheme.SwitchEverything)); // the 2-3 zone would stop it
        Assert.Equal("", read.WhyScheme(DefensiveScheme.Zone23));
        // The reviewer's case: against motion, man and drop are even, so the zone is picked for fit, not as the least-bad.
        var read2 = new MatchupRead(OffensiveSystem.Motion, DefensiveScheme.Man);
        Assert.Contains("fit is worth more", read2.WhyScheme(DefensiveScheme.Zone23));
        Assert.DoesNotContain("least-bad", read2.WhyScheme(DefensiveScheme.Zone23));
        // Against their man-to-man every system is even: a plan that only stops their offense doesn't "counter both".
        var read3 = new MatchupRead(OffensiveSystem.PostCentric, DefensiveScheme.Man);
        Assert.DoesNotContain("counters both", read3.PlanPitch(OffensiveSystem.Motion, DefensiveScheme.Zone23));
        Assert.Contains("holds even on offense", read3.PlanPitch(OffensiveSystem.Motion, DefensiveScheme.Zone23));

        // The board note: a disadvantage on a side the assistant picked explains it; on a side he didn't, names his pick.
        foreach (var (us, them) in Pairs(31))
        {
            if (us == them) continue;
            var r = MatchupRead.Of(us, them, null);
            var rot = CoachAi.Rotation(us, null);
            foreach (var sys in Enum.GetValues<OffensiveSystem>())
                foreach (var sch in Enum.GetValues<DefensiveScheme>())
                {
                    var note = TacticsViewModel.BoardNoteFor(r, rot, sys, sch);
                    bool poor = r.Attack(sys) <= -MatchupRead.Notable || r.Defence(sch) <= -MatchupRead.Notable;
                    Assert.Equal(poor, note.Length > 0);
                    if (poor) Assert.StartsWith("Assistant: ", note);
                }
        }
    }

    [Fact]
    public void The_scouting_report_never_calls_a_poor_scheme_a_good_answer()
    {
        foreach (var (us, them) in Pairs(77))
        {
            if (us == them) continue;
            var report = CoachAi.ScoutingReport(us, them, null);
            var read = MatchupRead.Of(us, them, null);
            var pickD = CoachAi.BestScheme(CoachAi.Rotation(us, null), read.TheirSystem);
            if (read.Defence(pickD) <= -MatchupRead.Notable)
            {
                Assert.DoesNotContain("good answer", report);
                Assert.True(report.Contains("least-bad") || report.Contains("fit is worth more"), report);
            }
        }
    }
}
