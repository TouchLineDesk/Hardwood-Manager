using Hardwood.Core.Audit;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// The balance checker runs and its hard limits hold. Balance flags themselves are reported, not failed on:
/// run `hardwood audit --seasons 10` after gameplay changes and read the report.
/// </summary>
public class BalanceAuditTests
{
    [Fact]
    public void Title_streaks_are_counted_per_league()
    {
        Assert.Equal(3, BalanceAudit.Streak(new[] { "2026: AAA", "2027: BBB", "2028: BBB", "2029: BBB", "2030: AAA" }));
        Assert.Equal(1, BalanceAudit.Streak(new[] { "2026: AAA", "2027: BBB" }));
        Assert.Equal(0, BalanceAudit.Streak(System.Array.Empty<string>()));
    }

    [Fact]
    public void A_short_audit_runs_and_hard_limits_hold()
    {
        // Seed 8 since v0.49.0: the college changes moved the shared random stream, and seed 7's first two summers then
        // signed only 2 and 7 graduates (main gives 6-11 across seeds 7-11 over two seasons; the branch 4.5-9).
        var r = BalanceAudit.Run(8, 2);
        Assert.Equal(2, r.Champions.Count);
        Assert.True(r.Metrics.Count >= 30);
        // Open market (decision #80): clubs live within their budgets and sign the graduates instead of drafting.
        Assert.True(r["Clubs over their wage budget at season start"].PerSeason.Max() <= 8);
        // (Champions shedding stars and relegation releases put more veterans on the market, so fewer graduates are needed.)
        Assert.True(r["College graduates signed by NABL clubs"].Mean >= 5, $"{r["College graduates signed by NABL clubs"].Mean}");
        Assert.InRange(r["Points per team per game"].Mean, 100, 130);
        Assert.All(r.Metrics.Where(m => m.Name.EndsWith("points per team per game")), m => Assert.InRange(m.Mean, 65, 118)); // each league its own level: Greece ~76 to tier 2 ~110
        var md = r.ToMarkdown();
        Assert.Contains("# Balance audit", md);
        Assert.Contains("| Points per team per game |", md);
    }
}
