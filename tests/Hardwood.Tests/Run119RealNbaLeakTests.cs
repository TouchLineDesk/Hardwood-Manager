using Hardwood.Core;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 119, K1's measurement and the leak this run fixes (stars retiring with seasons left on their deals). Summer one on seed 31 (v0.110.3): 450 real players in the NBA →
/// 284. Of the 166 who left, 61 retired (Kawhi Leonard at CA 160 and Stephen Curry at 156, both signed to 2028), 53 went
/// down with the five relegated clubs (the pyramid's design), 44 whose deals ran out signed abroad for scraps (Miles Bridges,
/// CA 135, to Korea for $1.33M) because no club over its budget could pay the full ask, and 8 left relegated clubs.
/// </summary>
public class Run119RealNbaLeakTests
{
    private readonly ITestOutputHelper _out;
    public Run119RealNbaLeakTests(ITestOutputHelper o) => _out = o;

    private static readonly DateOnly Summer = new(2027, 7, 1);

    private static Player Make(int ca, int age, Contract? c = null) =>
        new() { Id = 1, FirstName = "A", LastName = "B", CurrentAbility = ca, PotentialAbility = ca, BirthDate = Summer.AddYears(-age).AddDays(-30),
                Hidden = new HiddenTraits { Ambition = 10 }, Contract = c };

    private static double RetireRate(Player p, bool seasonsLeft = false, int rolls = 4000)
    {
        var dev = new DevelopmentEngine(new Rng(119));
        int n = 0;
        for (int i = 0; i < rolls; i++) if (dev.ShouldRetire(p, Summer, 30, seasonsLeft)) n++;
        return n / (double)rolls;
    }

    [Fact]
    public void A_star_with_seasons_left_on_his_deal_plays_on()
    {
        double star = RetireRate(Make(160, 36), seasonsLeft: true), starFree = RetireRate(Make(160, 36)), role = RetireRate(Make(100, 36));
        _out.WriteLine($"at 36: CA 160 under contract {star:P1}, CA 160 out of contract {starFree:P1}, CA 100 {role:P1}");
        Assert.InRange(star, 0.0, 0.06);           // was about 30%
        Assert.InRange(starFree, 0.04, 0.12);
        Assert.InRange(role, 0.5, 0.75);           // ordinary players retire as before
        Assert.InRange(RetireRate(Make(120, 35), seasonsLeft: true), 0.03, 0.12);   // a role player with years left: half of 20%
        Assert.InRange(RetireRate(Make(160, 40), seasonsLeft: true), 0.85, 1.0);   // 40 is still the end
    }

    [Fact]
    public void Summer_one_keeps_its_stars_under_contract()
    {
        var s = GameSession.NewGame(31, 1, null);
        var lines = K1LeakProbe.Probe(s, 1);
        foreach (var l in lines) _out.WriteLine(l);
        int Count(string line, string band) => int.Parse(System.Text.RegularExpressions.Regex.Match(line, band + @": (\d+)").Groups[1].Value);
        // Stars under contract don't retire in summer one (8 of CA 130+ did on v0.110.3: Leonard, Curry, George...).
        var ret = lines.Single(l => l.TrimStart().StartsWith("retired or left the game:"));
        Assert.InRange(Count(ret, @"CA 130\+"), 0, 4);
        // Run 128 (D80 A): the world is drawn differently, and Curry (39, CA 156, one season left) now retires on seed 31. At 39
        // under contract his chance is about 15% by design (`ShouldRetire`; the rates are checked above), so only Leonard (36,
        // about 4%) is named here; the CA 130+ count above still holds (3: Durant 39, Curry 39, LeBron 42).
        Assert.DoesNotContain(lines, l => l.Contains("Kawhi Leonard") && l.Contains("retired"));
    }
}
