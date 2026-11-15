using System.Text.RegularExpressions;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Phase 1 part 2 (#614, run 58): the commentary lives in <c>Engine/PlayByPlay.cs</c> and is handed each event by
/// the engine. These check the hand-off itself: every made basket gets exactly one call, every assist in the box is
/// named in a call, every period ends with its score line and recap, and the stamps follow the rules' periods.
/// The exact wording is pinned by <see cref="CharacterizationTests"/> (the play-by-play hash).
/// </summary>
public class PlayByPlayTests
{
    private static readonly League World = new WorldGenerator(new Rng(58)).Generate("L", 2026, new DateOnly(2026, 10, 1));

    private static readonly string[] Verbs =
    {
        "buries a three", "knocks down the three", "drills it from deep", "hits from downtown", "splashes the triple",
        "finishes at the rim", "lays it in", "attacks the basket and scores", "gets to the rim for two", "finishes inside",
        "hits the jumper", "pulls up and connects", "knocks down the mid-range J", "rises and hits", "sinks the jumper",
    };

    public static IEnumerable<object[]> Games() => new[]
    {
        new object[] { "nba", 1UL }, new object[] { "nba", 2UL }, new object[] { "nba", 3UL },
        new object[] { "fiba", 4UL }, new object[] { "college", 5UL }, new object[] { "college", 6UL },
    };

    private static Rules RulesFor(string name) => name switch { "fiba" => Rules.Fiba, "college" => Rules.College, _ => Rules.Nba };

    private static GameResult Play(string rules, ulong seed)
    {
        World.Teams[6].Tactic = new Tactic(); World.Teams[7].Tactic = new Tactic();
        return new GameSimulator().Simulate(World.Teams[6], World.Teams[7], RulesFor(rules), seed, keepLog: true);
    }

    [Theory, MemberData(nameof(Games))]
    public void Every_made_basket_gets_one_call_and_every_assist_is_named(string rules, ulong seed)
    {
        var r = Play(rules, seed);
        var text = r.PlayLog.Select(l => l.Split("  ", 2)[1]).ToList();
        var calls = text.Where(t => Verbs.Any(t.Contains)).ToList();
        int made = r.Home.Lines.Concat(r.Away.Lines).Sum(l => l.FGM);
        int assists = r.Home.Lines.Concat(r.Away.Lines).Sum(l => l.Assists);
        Assert.Equal(made, calls.Count);
        Assert.Equal(assists, calls.Count(c => c.Contains(" (assist ")));
        // The assister named is a team-mate of the scorer (never the scorer himself).
        foreach (var c in calls.Where(c => c.Contains(" (assist ")))
        {
            string scorer = c[..c.IndexOf(' ')];
            string helper = Regex.Match(c, @"\(assist ([^)]+)\)").Groups[1].Value;
            Assert.NotEqual(scorer, helper);
        }
        Assert.Equal(r.Home.Lines.Concat(r.Away.Lines).Sum(l => l.AndOnes), calls.Count(c => c.Contains("And one")));
    }

    [Theory, MemberData(nameof(Games))]
    public void Every_period_ends_with_its_score_line_then_its_recap(string rules, ulong seed)
    {
        var r = Play(rules, seed);
        int periods = r.Home.PointsByPeriod.Length;
        var ends = r.PlayLog.Select((l, i) => (l, i)).Where(x => x.l.Contains("  End of period ")).ToList();
        Assert.Equal(periods, ends.Count);
        int home = 0, away = 0;
        for (int p = 0; p < periods; p++)
        {
            home += r.Home.PointsByPeriod[p]; away += r.Away.PointsByPeriod[p];
            Assert.EndsWith($"End of period {p + 1}: {r.Away.Team.Abbreviation} {away} - {r.Home.Team.Abbreviation} {home}", ends[p].l);
            string recap = r.PlayLog[ends[p].i + 1];
            Assert.True(recap.Contains(" goes to ") || recap.Contains(" is even"), recap);
        }
    }

    [Theory, MemberData(nameof(Games))]
    public void Stamps_follow_the_rules_periods_and_the_clock_runs_down(string rules, ulong seed)
    {
        var r = Play(rules, seed);
        var rr = RulesFor(rules);
        string letter = rr.Periods == 2 ? "H" : "Q";
        var stamp = new Regex(@"^(?:(?<p>[HQ])(?<n>\d)|OT(?<ot>\d)) (?<m>\d\d):(?<s>\d\d)  \S");
        int lastPeriod = 0; int lastClock = int.MaxValue;
        foreach (var l in r.PlayLog)
        {
            var m = stamp.Match(l);
            Assert.True(m.Success, l);
            int period = m.Groups["ot"].Success ? rr.Periods + int.Parse(m.Groups["ot"].Value) : int.Parse(m.Groups["n"].Value);
            if (!m.Groups["ot"].Success) Assert.Equal(letter, m.Groups["p"].Value);
            int clock = int.Parse(m.Groups["m"].Value) * 60 + int.Parse(m.Groups["s"].Value);
            Assert.True(period >= lastPeriod, l);
            if (period == lastPeriod) Assert.True(clock <= lastClock, l);
            if (period > lastPeriod) { lastPeriod = period; }
            lastClock = clock;
            Assert.True(clock <= rr.MinutesPerPeriod * 60, l);
        }
        Assert.Equal(r.Home.PointsByPeriod.Length, lastPeriod);
    }

    [Fact]
    public void A_game_without_a_log_writes_no_commentary()
    {
        World.Teams[6].Tactic = new Tactic(); World.Teams[7].Tactic = new Tactic();
        var r = new GameSimulator().Simulate(World.Teams[6], World.Teams[7], Rules.Nba, 9, keepLog: false);
        Assert.Empty(r.PlayLog);
        Assert.Empty(r.Flow);
    }
}
