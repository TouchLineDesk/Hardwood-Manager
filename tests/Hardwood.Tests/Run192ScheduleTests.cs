using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Slice 2 of the revised launch plan (run 192): your own non-conference schedule. Buy games at home, two-year home-and-home
/// series with power programs, a holiday tournament of your choice and a December showcase on request; the AD's target
/// moves with the slate and the season's welcome shows its strength of schedule.
/// </summary>
public class Run192ScheduleTests
{
    private readonly ITestOutputHelper _out;
    public Run192ScheduleTests(ITestOutputHelper output) => _out = output;

    private static (CollegeWorld c, int me) World(string school)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        var c = world.College!;
        int me = c.Programs.First(p => p.FullName.StartsWith(school)).Id;
        c.ManagedProgramId = me;
        return (c, me);
    }

    private static List<CollegeGame> Mine(CollegeWorld c, int me, string stage) =>
        c.Season.Games.Where(g => (g.HomeId == me || g.AwayId == me) && g.Stage == stage).ToList();

    private static int Opp(CollegeGame g, int me) => g.HomeId == me ? g.AwayId : g.HomeId;

    [Fact]
    public void Buy_games_home_and_homes_and_the_holiday_choice_are_drawn_into_the_schedule()
    {
        var (c, me) = World("George Mason");
        var books = c.Program(me)!.Money.Program;

        // Buy games: five low-majors come to our gym; no holiday tournament.
        books.Schedule = new NonConferencePlan { Mix = ScheduleMix.BuyGames, Holiday = "" };
        c.StartSeason(2027, new Rng(31));
        var nc = Mine(c, me, "Non-conference");
        var buys = nc.Where(g => g.HomeId == me && c.Prestige[g.AwayId] < CollegeWorld.BuyGameMaxPrestige).ToList();
        _out.WriteLine(c.NonConferenceSummary(me));
        Assert.Equal(CollegeWorld.BuyGameCount, books.Schedule.BuyGamesBooked);
        Assert.True(buys.Count >= CollegeWorld.BuyGameCount, $"{buys.Count} buy games at home");
        Assert.Empty(Mine(c, me, "Invitational"));
        Assert.Equal(1, c.ScheduleTargetShift(me, 2027));
        Assert.Contains("one more win", c.ScheduleTargetNote(me, 2027));
        Assert.All(nc, g => Assert.NotEqual(c.ConferenceOf[me], c.ConferenceOf[Opp(g, me)]));
        Assert.Equal(nc.Count, nc.Select(g => g.Date).Distinct().Count()); // one game a day
        int total = c.Season.Games.Count(g => (g.HomeId == me || g.AwayId == me) && CollegeWorld.IsRegularSeason(g));
        Assert.InRange(total, 29, 32);
        var weak = c.ScheduleStrength(me)!.Value;

        // Home-and-homes with power programs, and a holiday tournament we pick.
        string holiday = CollegeWorld.HolidayEvents.First(h => !CollegeWorld.IsMajorHoliday(h));
        books.Schedule = new NonConferencePlan { Mix = ScheduleMix.Tough, Holiday = holiday };
        c.StartSeason(2028, new Rng(31));
        var series = books.Schedule.Series.Where(s => s.FirstYear == 2028).ToList();
        Assert.Equal(CollegeWorld.NewSeriesCount, series.Count);
        nc = Mine(c, me, "Non-conference");
        foreach (var s in series)
        {
            var g = nc.Single(x => Opp(x, me) == s.OpponentId);
            Assert.Equal(s.HomeFirst, g.HomeId == me);
            Assert.True(c.Prestige[s.OpponentId] >= CollegeWorld.PowerPrestige);
        }
        Assert.Contains(Mine(c, me, "Invitational"), g => g.Event == holiday);
        Assert.Equal(-1, c.ScheduleTargetShift(me, 2028));
        var tough = c.ScheduleStrength(me)!.Value;
        _out.WriteLine($"{c.NonConferenceSummary(me)} SOS {tough.Rank} (non-conf {tough.NonConfRank}) vs buy games {weak.Rank} (non-conf {weak.NonConfRank})");
        Assert.True(tough.NonConfRank < weak.NonConfRank, $"non-conference SOS {tough.NonConfRank} should beat {weak.NonConfRank}");

        // The returns: next season each series comes back with the other side at home, and the deal is done.
        books.Schedule.Mix = ScheduleMix.Balanced;
        c.StartSeason(2029, new Rng(31));
        nc = Mine(c, me, "Non-conference");
        foreach (var s in series)
        {
            var g = nc.Single(x => Opp(x, me) == s.OpponentId);
            Assert.Equal(!s.HomeFirst, g.HomeId == me);
        }
        Assert.Equal(0, c.ScheduleTargetShift(me, 2029));
        c.StartSeason(2030, new Rng(31));
        Assert.Empty(books.Schedule.Series);
    }

    [Fact]
    public void A_major_holiday_needs_prestige_and_no_plan_draws_exactly_as_before()
    {
        var (c, me) = World("George Mason");
        string major = CollegeWorld.HolidayEvents[0];
        Assert.True(CollegeWorld.IsMajorHoliday(major));
        if (c.Prestige[me] < CollegeWorld.MajorHolidayPrestige) Assert.NotNull(c.HolidayBlocked(me, major));
        var (big, duke) = World("Duke");
        Assert.Null(big.HolidayBlocked(duke, major));
        big.Program(duke)!.Money.Program.Schedule = new NonConferencePlan { Holiday = major };
        big.StartSeason(2027, new Rng(7));
        Assert.Contains(Mine(big, duke, "Invitational"), g => g.Event == major);

        // No plan: the draw is the one every program got before run 192.
        var (a, ma) = World("George Mason");
        var (b, _) = World("George Mason");
        b.ManagedProgramId = null;
        a.StartSeason(2027, new Rng(11));
        b.StartSeason(2027, new Rng(11));
        Assert.Equal(b.Season.Games.Select(g => (g.Date, g.HomeId, g.AwayId, g.Stage)), a.Season.Games.Select(g => (g.Date, g.HomeId, g.AwayId, g.Stage)));
    }

    [Fact]
    public void A_showcase_request_puts_us_in_December_and_the_welcome_shows_the_schedule()
    {
        var (_, world0) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(9, 2026, GameSession.DefaultFullSim);
        int id = world0.College!.Programs.First(p => p.FullName.StartsWith("George Mason")).Id;
        var s = GameSession.NewGame(9, id, null);
        var c = s.World.College!;
        Assert.DoesNotContain(c.Programs.Select(t => t.Id).OrderByDescending(i => c.Season.Elo[i]).Take(30), i => i == id);
        s.HumanTeam.Money.Program.Schedule = new NonConferencePlan { Showcase = true };
        int guard = 0;
        while (s.Today < new DateOnly(2026, 12, 22) && guard++ < 120) s.AdvanceDay();
        var show = c.Season.Games.Where(g => g.Stage == "Showcase" && (g.HomeId == id || g.AwayId == id)).ToList();
        Assert.Single(show);
        _out.WriteLine($"{show[0].Event}: {c.Program(show[0].HomeId)!.FullName} v {c.Program(show[0].AwayId)!.FullName}");
        string? line = s.ScheduleLine();
        _out.WriteLine(line);
        Assert.NotNull(line);
        Assert.Contains("Strength of schedule:", line);
        Assert.Contains("Board page", line);

        // Next season with buy games: the AD expects one more win and says so, and the welcome shows the new slate.
        s.HumanTeam.Money.Program.Schedule.Mix = ScheduleMix.BuyGames;
        s.Job.Current!.TargetWins = 0; // kept on, whatever the record
        guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 500) s.AdvanceDay();
        while (s.Phase == SessionPhase.Offseason && guard++ < 1100) s.AdvanceOffseason();
        _out.WriteLine($"{s.Today} {s.Phase} league {s.League.Season} college {c.Season.Year} employed {s.Employed}");
        Assert.Equal(2027, c.Season.Year);
        var plan = s.HumanTeam.Money.Program.Schedule;
        Assert.Equal(2027, plan.DrawnYear);
        Assert.Equal(CollegeWorld.BuyGameCount, plan.BuyGamesBooked);
        var goal = s.Inbox.Last(m => m.Title.StartsWith("Expectations for 2027"));
        _out.WriteLine(goal.Body);
        Assert.Contains("buy games at home, so the AD expects one more win", goal.Body);
        var welcome = s.Inbox.Last(m => m.Title.StartsWith("Welcome to 2027"));
        _out.WriteLine(welcome.Body);
        Assert.Contains("Your schedule:", welcome.Body);
    }
}
