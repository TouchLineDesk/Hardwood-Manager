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
    public void A_short_audit_runs_and_hard_limits_hold()
    {
        var r = BalanceAudit.Run(7, 2);
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
