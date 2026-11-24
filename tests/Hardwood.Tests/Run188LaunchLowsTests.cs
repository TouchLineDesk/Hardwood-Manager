using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 188 (before the launch freeze; text only, no engine, balance or RNG change):
/// - Run 182's review, finding 3: at low AD confidence a 7–10 seed's Round of 32 read "accepts the result, but there is
///   ground to make up" while the same season's Round of 64 exit read "pleased with the regular season". The ladder now
///   holds at every starting confidence.
/// - The mood number and word agree: a morale of 49.6 showed "50/100" beside "Unhappy".
/// </summary>
public class Run188LaunchLowsTests
{
    private readonly ITestOutputHelper _out;
    public Run188LaunchLowsTests(ITestOutputHelper o) => _out = o;
    private static readonly Dictionary<int, PlayerSeasonStats> None = new();

    private static int Rank(string verdict) =>
        verdict.Contains(" is delighted") ? 6
        : verdict.Contains(" is pleased") ? 5
        : verdict.Contains(" is satisfied") || verdict.Contains(" accepts") ? 4
        : verdict.Contains(" hoped for more") ? 3
        : verdict.Contains(" is disappointed") ? 2
        : verdict.Contains(" expected more") || verdict.Contains(" wanted to see progress") ? 1
        : 0;

    private static SeasonGoal Reached(int round) => round switch
    {
        7 => SeasonGoal.Title,
        >= 5 => SeasonGoal.Finals,
        4 => SeasonGoal.ConferenceFinals,
        3 => SeasonGoal.SecondRound,
        _ => SeasonGoal.Playoffs,
    };

    [Fact]
    public void The_ADs_ladder_holds_at_low_confidence()
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        var ctx = new LeagueContext("College basketball", league.Teams, 31, 68, false, null, -10, 0, true);
        var team = league.Teams[5];
        team.Owner.Patience = 20; team.Owner.Ambition = 10; // a patient AD: low confidence, but nobody is sacked
        int cases = 0;
        foreach (double start in new[] { 22.0, 30.0, 36.0, 45.0, 70.0 })
        foreach (var goal in new[] { SeasonGoal.Title, SeasonGoal.Finals, SeasonGoal.ConferenceFinals, SeasonGoal.SecondRound, SeasonGoal.Playoffs })
        foreach (int seed in new[] { 1, 2, 4, 6, 7, 8, 9, 10, 11, 12, 16 })
        foreach (int wins in new[] { 29, 24, 19 })
        {
            int last = -1, lastRound = 0; string lastText = "";
            for (int round = 1; round <= 7; round++)
            {
                var achieved = Reached(round);
                var job = new JobState
                {
                    Confidence = start, HiredSeason = 2023,
                    Current = new Expectations { Season = 2026, Goal = goal, TargetWins = 24, TeamId = team.Id, League = "college" },
                };
                var (_, body, sacked) = Board.SeasonReview(job, new Coach(), ctx, team, achieved, wins, 31 - wins, None, new DateOnly(2027, 4, 7), 2026,
                    madnessRound: round, seed: seed, overall: (wins + round, 31 - wins + 1));
                if (sacked) { last = -1; continue; }
                string verdict = body.Split('\n').Last();
                int rank = Rank(verdict);
                Assert.True(rank > 0, $"unranked verdict: {verdict}");
                Assert.True(rank >= last, $"start {start}, goal {goal}, seed {seed}, {wins} wins: round {round} \"{verdict}\" reads worse than round {lastRound} \"{lastText}\"");
                last = rank; lastRound = round; lastText = verdict;
                cases++;
            }
        }
        _out.WriteLine($"{cases} cases");
        Assert.True(cases > 1500);
    }

    [Fact]
    public void A_nine_seeds_Round_of_32_at_low_confidence_credits_the_season()
    {
        var league = new WorldGenerator(new Rng(3)).Generate("NABL", 2026, new DateOnly(2026, 10, 1));
        Board.EnsureOwnersAndCoaches(league, 3);
        var ctx = new LeagueContext("College basketball", league.Teams, 31, 68, false, null, -10, 0, true);
        var team = league.Teams[5];
        team.Owner.Patience = 20; team.Owner.Ambition = 10;
        string Review(int round)
        {
            var job = new JobState { Confidence = 12, HiredSeason = 2023, Current = new Expectations { Season = 2026, Goal = SeasonGoal.SecondRound, TargetWins = 20, TeamId = team.Id, League = "college" } };
            var v = Board.SeasonReview(job, new Coach(), ctx, team, Reached(round), 24, 7, None, new DateOnly(2027, 4, 7), 2026, madnessRound: round, seed: 9).body.Split('\n').Last();
            _out.WriteLine(v);
            return v;
        }
        string r64 = Review(1), r32 = Review(2);
        Assert.Contains("is pleased with the regular season", r32);
        Assert.Contains("and winning a game as a 9-seed beat expectations", r32);
        Assert.True(Rank(r32) >= Rank(r64), $"{r32} / {r64}");
    }

    [Fact]
    public void The_mood_number_never_reads_above_its_word()
    {
        foreach (double m in new[] { 34.6, 49.5, 49.99, 67.7, 81.9 })
        {
            int shown = PeopleEngine.MoraleShown(m);
            Assert.Equal(PeopleEngine.Mood(m), PeopleEngine.Mood(shown));
        }
    }
}
