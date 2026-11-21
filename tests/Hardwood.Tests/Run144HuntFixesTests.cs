using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Jobs;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 144 (hunt 21): H166, the athletic director counts the program's real season (the Big East's 31 games, not 23), and
/// K20's verdict wording follows the margin ("15-17 is well short of the 15-win target" read for a target met).
/// </summary>
public class Run144HuntFixesTests
{
    private readonly ITestOutputHelper _out;
    public Run144HuntFixesTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData(9, 16)]
    [InlineData(10, 18)]
    [InlineData(11, 20)]
    [InlineData(12, 19)]
    [InlineData(14, 19)]
    [InlineData(15, 19)]
    [InlineData(18, 19)]
    public void Conference_games_follow_the_schedule_rule(int members, int games) =>
        Assert.Equal(games, CollegeWorld.ConferenceGames(members));

    [Fact]
    public void The_AD_counts_the_regular_season_the_program_actually_plays()
    {
        var rng = new Rng(77);
        var gen = new PlayerGenerator(new Rng(77 ^ 0x51), 2_000_000);
        var cw = CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1));
        // Opening day: from the schedule (or the rule before it exists), never the old 10 + n − 1 + 3.
        var before = cw.Programs.ToDictionary(p => p.Id, p => LeagueContext.College(cw, p).SeasonGames);
        Assert.All(before.Values, g => Assert.InRange(g, 26, 34));
        cw.PlayThrough(new DateOnly(2027, 3, 9), new GameSimulator(), rng); // the day before the conference tournaments
        int off = 0, worst = 0;
        foreach (var p in cw.Programs)
        {
            var (w, l) = cw.RegularSeasonRecord(p.Id);
            int counted = LeagueContext.College(cw, p).SeasonGames;
            Assert.Equal(w + l, counted); // after the regular season the AD's season is the record's
            int diff = Math.Abs(before[p.Id] - (w + l));
            if (diff > 0) off++;
            worst = Math.Max(worst, diff);
        }
        _out.WriteLine($"opening-day count off for {off} of {cw.Programs.Count} programs, at most by {worst}");
        Assert.True(worst <= 3, $"opening-day count off by {worst}");
    }

    [Fact]
    public void A_sacked_college_coach_is_a_long_shot_for_a_pro_job_that_summer()
    {
        // H167: Providence (seed 77) was sacked at reputation 24 and coached Riga the same week.
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run144-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(77, 2026, Hardwood.Core.Season.GameSession.DefaultFullSim);
            int id = world.College!.Programs.First(p => p.City == "Providence").Id;
            var s = Hardwood.Core.Season.GameSession.NewGame(77, id, Path.Combine(dir, "prov.hwsave"));
            s.Coach.Reputation = 24;
            var pro = s.League.Teams.OrderBy(t => Board.Prestige(s.ContextOf(t), t)).First();
            var college = s.World.College!.Programs.Where(p => p.Id != id).OrderBy(p => Board.Prestige(s.ContextOf(p), p)).First();
            Assert.Equal(Board.HireChance(s.Coach, s.ContextOf(pro), pro), s.HireChanceFor(pro)); // in work: unchanged
            s.Fire();
            Assert.True(s.RecentlySackedFromCollege);
            double baseChance = Board.HireChance(s.Coach, s.ContextOf(pro), pro);
            Assert.Equal(baseChance * Hardwood.Core.Season.GameSession.SackedCollegeCoachProFactor, s.HireChanceFor(pro), 6);
            Assert.Equal(Board.HireChance(s.Coach, s.ContextOf(college), college), s.HireChanceFor(college)); // college jobs: unchanged
            _out.WriteLine($"{pro.FullName}: {baseChance:P0} → {s.HireChanceFor(pro):P0}; {college.FullName}: {s.HireChanceFor(college):P0}");
            // A high reputation still gets a pro club's ear.
            s.Coach.Reputation = 60;
            Assert.Equal(Board.HireChance(s.Coach, s.ContextOf(pro), pro), s.HireChanceFor(pro));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Theory]
    [InlineData(15, 17, 15, "15-17 reached the 15-win target")]
    [InlineData(14, 17, 15, "14-17 is just short of the 15-win target")]
    [InlineData(13, 17, 15, "13-17 is just short of the 15-win target")]
    [InlineData(12, 19, 15, "12-19 is well short of the 15-win target")]
    public void The_verdict_is_worded_by_the_margin(int wins, int losses, int target, string expected) =>
        Assert.Equal(expected, Board.TargetPhrase(wins, losses, target));
}
