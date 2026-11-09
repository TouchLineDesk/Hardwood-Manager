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
        Assert.Equal(0, r["Teams over the apron at season start"].PerSeason.Max());
        Assert.InRange(r["Points per team per game"].Mean, 100, 130);
        Assert.All(r.Metrics.Where(m => m.Name.EndsWith("points per team per game")), m => Assert.InRange(m.Mean, 65, 110));
        var md = r.ToMarkdown();
        Assert.Contains("# Balance audit", md);
        Assert.Contains("| Points per team per game |", md);
    }
}
