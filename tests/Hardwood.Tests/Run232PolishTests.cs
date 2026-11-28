using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 232, a polish run: H229 (rookie-scale extensions paid at the rookie wage for six years), H230 (a coach
/// sacked at a bottom Lithuanian club hired by an NBA club), H231 (the Board's confidence chart vs its number), H232 and
/// H235 (summer and Board wording).</summary>
public class Run232PolishTests
{
    private readonly ITestOutputHelper _out;
    public Run232PolishTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void H229_only_the_real_rookie_extensions_are_marked()
    {
        var marked = RealPlayers.Bios.Keys.Where(RealPlayers.IsRookieExtension).OrderBy(n => n).ToList();
        _out.WriteLine(string.Join(", ", marked));
        Assert.Equal(new[] { "Amen Thompson", "Ausar Thompson", "Brandin Podziemski", "Brandon Miller", "Cason Wallace", "Keyonte George", "Victor Wembanyama" }, marked);
        // Five-year deals already in their extension, cheap veterans' long deals and stars are left alone.
        foreach (var n in new[] { "Tari Eason", "Neemias Queta", "Ayo Dosunmu", "Jabari Smith Jr." }) Assert.False(RealPlayers.IsRookieExtension(n), n);
    }

    [Fact]
    public void H229_the_extension_is_paid_from_the_summer_the_rookie_year_ends()
    {
        var s = GameSession.NewGame(31, 1, null);
        var all = s.League.Teams.SelectMany(t => t.Roster).ToList();
        var wemby = all.Single(p => p.Name == "Victor Wembanyama");
        var eason = all.Single(p => p.Name == "Tari Eason");
        long rookie = wemby.Contract!.Salary, easonPay = eason.Contract!.Salary;
        Assert.Equal(6, wemby.Contract.Years);
        Assert.True(rookie < 17_000_000);

        // A changed deal is never touched (a re-signing or a renegotiation): only the source's untouched deal steps up.
        var original = wemby.Contract;
        Assert.NotNull(RealPlayers.RookieExtensionSalary(wemby, s.League.Finance, s.Today));
        wemby.Contract = new Contract { Years = 6, Salary = rookie + 10_000, StartSeason = 2026 };
        Assert.Null(RealPlayers.RookieExtensionSalary(wemby, s.League.Finance, s.Today));
        wemby.Contract = new Contract { Years = 5, Salary = rookie, StartSeason = 2026 };
        Assert.Null(RealPlayers.RookieExtensionSalary(wemby, s.League.Finance, s.Today));
        wemby.Contract = original;

        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 700) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        var w = s.League.Teams.SelectMany(t => t.Roster).Single(p => p.Name == "Victor Wembanyama");
        long max = (long)(s.League.Finance.SalaryCap * RealPlayers.RookieMaxShareOfCap) + 10_000;
        _out.WriteLine($"Wembanyama CA {w.CurrentAbility}: ${rookie / 1e6:F2}M → ${w.Contract!.Salary / 1e6:F2}M × {w.Contract.Years} (cap ${s.League.Finance.SalaryCap / 1e6:F1}M)");
        Assert.Equal(5, w.Contract.Years);
        Assert.True(w.Contract.Salary > rookie * 2, "the best player in the game is paid like one from his extension");
        Assert.True(w.Contract.Salary <= max);
        // The summer after, the deal no longer matches the source, so it never steps again.
        Assert.Null(RealPlayers.RookieExtensionSalary(w, s.League.Finance, s.Today));
        var e = s.League.Teams.SelectMany(t => t.Roster).Concat(s.League.FreeAgents).FirstOrDefault(p => p.Name == "Tari Eason");
        if (e?.Contract is { } ec && ec.StartSeason == 2026) Assert.Equal(easonPay, ec.Salary);
    }

    [Fact]
    public void H230_a_coach_sacked_abroad_is_a_long_shot_for_the_nba()
    {
        var (_, world) = RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var club = world.Clubs.First(t => t.FullName.Contains("Lietkabelis"));
        var s = GameSession.NewGame(31, club.Id, null);
        s.Coach.Reputation = 30;
        var nba = s.League.Teams.OrderBy(t => Board.Prestige(s.ContextOf(t), t)).First();
        var gLeague = s.World.Clubs.Where(t => s.PyramidLevelOf(t) == 2).OrderBy(t => Board.Prestige(s.ContextOf(t), t)).First();
        var sameLevel = s.World.Clubs.First(t => t.Id != club.Id && s.World.LeagueOf(t)?.Key == s.World.LeagueOf(s.HumanTeam)?.Key);
        s.Fire();
        Assert.Equal(3, s.SackedFromLevel);
        double Base(Team t) => Board.HireChance(s.Coach, s.ContextOf(t), t);
        _out.WriteLine($"{nba.FullName}: {Base(nba):P0} → {s.HireChanceFor(nba):P1}; {gLeague.FullName}: {Base(gLeague):P0} → {s.HireChanceFor(gLeague):P1}; {sameLevel.FullName}: {s.HireChanceFor(sameLevel):P0}");
        Assert.Equal(Base(nba) * GameSession.SackedProTwoUpFactor, s.HireChanceFor(nba), 9);
        Assert.Equal(Base(gLeague) * GameSession.SackedProOneUpFactor, s.HireChanceFor(gLeague), 9);
        Assert.Equal(Base(sameLevel), s.HireChanceFor(sameLevel), 9);
        // A high reputation gets an NBA club's ear again.
        s.Coach.Reputation = GameSession.SackedProReputation;
        Assert.Equal(Base(nba), s.HireChanceFor(nba), 9);
    }

    [Fact]
    public void H231_the_chart_ends_at_todays_number_and_says_which_weeks()
    {
        var job = new JobState { Confidence = 45 };
        job.History.Add(new ConfidencePoint(new DateOnly(2026, 10, 27), 60));
        job.History.Add(new ConfidencePoint(new DateOnly(2027, 4, 13), 64));
        var (values, caption) = BoardViewModel.TrendFor(job);
        Assert.Equal(new[] { 60.0, 64.0, 45.0 }, values);
        Assert.Equal("Weekly, Oct 27, 2026 to Apr 13, 2027; the last point is today's 45.", caption);
        job.Confidence = 64.2;
        (values, caption) = BoardViewModel.TrendFor(job);
        Assert.Equal(new[] { 60.0, 64.0 }, values);
        Assert.EndsWith("to Apr 13, 2027.", caption);
    }
}
