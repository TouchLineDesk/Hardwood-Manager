using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 156, H159: a schedule that reads true. Every program's regular season is about 30 games (real D-I plays 29-32; a
/// small conference's season was 24-28), and a power program hosts a low-major (Duke opened at Queens).
/// </summary>
public class Run156CollegeScheduleTests
{
    private readonly ITestOutputHelper _out;
    public Run156CollegeScheduleTests(ITestOutputHelper o) => _out = o;

    [Theory]
    [InlineData(9UL, 2026)]
    [InlineData(31UL, 2026)]
    [InlineData(77UL, 2026)]
    [InlineData(9UL, 2027)]  // run 156 review: the weekdays move, and 2027-2039 had one day fewer
    [InlineData(31UL, 2028)]
    public void Every_program_plays_a_full_season(ulong seed, int year)
    {
        var (_, world) = RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        var c = world.College!;
        c.StartSeason(year, new Rng(seed));
        var games = c.Season.Games;
        // The regular season as the AD counts it, before the brackets are played: non-conference, conference, and the
        // invitational allowance for an invited program (one to three games).
        var counts = c.Programs.Select(t =>
        {
            var mine = games.Where(g => g.HomeId == t.Id || g.AwayId == t.Id).ToList();
            int n = mine.Count(g => g.Stage is "Non-conference" or "Conference");
            bool invited = mine.Any(g => g.Stage == "Invitational");
            return (t, n, invited, total: n + (invited ? CollegeWorld.InvitationalAllowance : 0));
        }).ToList();
        var hist = counts.GroupBy(x => x.total).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}");
        _out.WriteLine($"seed {seed} {year}: " + string.Join(" ", hist));
        foreach (var x in counts.Where(x => x.total < 29 || x.total > 32))
            _out.WriteLine($"  {x.t.FullName} {x.total} (invited {x.invited})");
        Assert.All(counts, x => Assert.InRange(x.total, 29, 32));

        // One game a day at most; non-conference games stay out of the showcase days and before conference play.
        var byDay = games.SelectMany(g => new[] { (g.Date, g.HomeId), (g.Date, g.AwayId) }).GroupBy(x => x).Where(g => g.Count() > 1).ToList();
        Assert.Empty(byDay);
        Assert.All(games.Where(g => g.Stage == "Non-conference"), g =>
        {
            Assert.True(g.Date < new DateOnly(year + 1, 1, 1), g.Date.ToString());
            Assert.False(g.Date.Month == 12 && g.Date.Day is >= 19 and <= 21, g.Date.ToString());
            Assert.NotEqual(c.ConferenceOf[g.HomeId], c.ConferenceOf[g.AwayId]);
        });

        // Power programs host the low-majors: of games between a 75+ prestige program and a sub-45 one, nearly all are at the big one.
        var mismatches = games.Where(g => g.Stage == "Non-conference")
            .Select(g => (h: c.Prestige[g.HomeId], a: c.Prestige[g.AwayId]))
            .Where(x => Math.Max(x.h, x.a) >= 75 && Math.Min(x.h, x.a) < 45).ToList();
        double atBig = mismatches.Count(x => x.h > x.a) / (double)Math.Max(1, mismatches.Count);
        _out.WriteLine($"power v low-major: {mismatches.Count} games, {atBig:P0} at the power program");
        Assert.True(atBig >= 0.85, $"{atBig:P0}");
    }

    [Fact]
    public void The_target_fills_a_small_conference()
    {
        Assert.Equal(16, CollegeWorld.NonConferenceTarget(14, false)); // an eight-team league: 14 + 16 = 30
        Assert.Equal(10, CollegeWorld.NonConferenceTarget(20, false));
        Assert.Equal(7, CollegeWorld.NonConferenceTarget(20, true)); // H171 (run 163): an invited program books 3 for its bracket
        for (int y = 2026; y <= 2060; y++) Assert.True(CollegeWorld.NonConferenceDates(y).Count >= 18, $"{y}");
    }
}
