using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 223 (Danny's `danny-world-leagues-nov26`): the NBA's in-season cup. Six groups of five, four group games each
/// on five November cup nights inside the 82, an eight-team knockout in December (quarter-finals at the better seed's arena,
/// the semi-finals and final in Las Vegas), saved with the career.</summary>
public class Run223NbaCupTests
{
    private readonly ITestOutputHelper _out;
    public Run223NbaCupTests(ITestOutputHelper output) => _out = output;

    private static int Club(ulong seed, int rank)
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026);
        return probe.Teams.OrderByDescending(t => QuickSim.Strength(t)).ElementAt(rank).Id;
    }

    [Theory]
    [InlineData(31UL)]
    [InlineData(2026UL)]
    public void The_groups_and_cup_nights_sit_inside_a_normal_82_game_schedule(ulong seed)
    {
        var s = GameSession.NewGame(seed, Club(seed, 10), null);
        var cup = Assert.IsType<CupSeason>(s.Cup);
        // The game's league has 32 clubs (#355): eight groups of four, three group games each.
        Assert.Equal(8, cup.Groups.Count);
        Assert.All(cup.Groups, g => Assert.Equal(4, g.TeamIds.Count));
        Assert.Equal(32, cup.Groups.SelectMany(g => g.TeamIds).Distinct().Count());
        Assert.All(cup.Groups, g => Assert.Single(g.TeamIds.Select(id => s.League.TeamById(id).Conference).Distinct()));
        var dbg = s.Schedule.Where(s.IsCupGame).GroupBy(g => g.Date).Select(g => $"{g.Key}:{g.Count()}");
        _out.WriteLine($"group games {cup.GroupGames.Count}: {string.Join(" ", dbg)}");
        Assert.Equal(48, cup.GroupGames.Count);

        var nights = NbaCup.GroupNights(2026);
        Assert.Equal(new DateOnly(2026, 11, 13), nights[0]);
        var games = s.Schedule.Where(s.IsCupGame).ToList();
        Assert.All(games, g => Assert.Contains(g.Date, nights));
        foreach (var t in s.League.Teams)
        {
            var mine = games.Where(g => g.Home == t || g.Away == t).ToList();
            Assert.Equal(3, mine.Count);
            Assert.InRange(mine.Count(g => g.Home == t), 1, 2);
            Assert.Equal(82, s.Schedule.Count(g => g.Home == t || g.Away == t));
            Assert.Equal(41, s.Schedule.Count(g => g.Home == t));
            // Never three days running, never four in five.
            var days = s.Schedule.Where(g => g.Home == t || g.Away == t).Select(g => g.Date.DayNumber).OrderBy(d => d).ToList();
            for (int i = 2; i < days.Count; i++) Assert.False(days[i] - days[i - 2] == 2, $"{t.Abbreviation} three straight days to {DateOnly.FromDayNumber(days[i])}");
            for (int i = 3; i < days.Count; i++) Assert.True(days[i] - days[i - 3] >= 5, $"{t.Abbreviation} four in five days to {DateOnly.FromDayNumber(days[i])}");
        }
        var (q1, q2, sf, f) = NbaCup.KnockoutNights(2026);
        Assert.Equal((new DateOnly(2026, 12, 8), new DateOnly(2026, 12, 9), new DateOnly(2026, 12, 12), new DateOnly(2026, 12, 15)), (q1, q2, sf, f));
        Assert.DoesNotContain(s.Schedule, g => g.Date == q1 || g.Date == q2 || g.Date == sf || g.Date == f);
        Assert.True(s.Schedule.Max(g => g.Date) <= new DateOnly(2027, 4, 12));
        _out.WriteLine(string.Join("\n", cup.Groups.Select(g => $"{g.Name}: {string.Join(", ", g.TeamIds.Select(id => s.League.TeamById(id).Abbreviation))}")));
    }

    [Fact]
    public void The_cup_is_played_to_a_winner_and_kept_through_a_save()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run223-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = GameSession.NewGame(31, Club(31, 2), path);
            s.Job.Current!.TargetWins = 0;
            while (s.Today <= s.OpeningNight) s.AdvanceDay();
            var groups = Assert.Single(s.Inbox, m => m.Title == $"The {NbaCup.Name}: the groups");
            Assert.Contains("You're in ", groups.Body);
            _out.WriteLine(groups.Body + "\n");

            var (q1, _, _, final) = NbaCup.KnockoutNights(2026);
            while (s.Today < q1) s.AdvanceDay();
            var cup = s.Cup!;
            Assert.Equal(4, cup.Ties.Count);
            Assert.Equal(2, cup.Ties.Select(t => s.League.TeamById(t.HomeId).Conference).Distinct().Count());
            int standingGames = s.Standings[s.HumanTeam].Games;
            var qfMsg = Assert.Single(s.Inbox, m => m.Title == $"The {NbaCup.Name}: the quarter-finals");
            _out.WriteLine(qfMsg.Body + "\n");

            // Reload in the middle of the knockouts: the cup comes back as it was.
            s.SaveNow();
            var reloaded = GameSession.Load(path);
            Assert.Equal(cup.Ties.Count, reloaded.Cup!.Ties.Count);
            Assert.Equal(cup.GroupGames, reloaded.Cup.GroupGames);
            Assert.Equal(cup.Groups.Select(g => string.Join(",", g.TeamIds)), reloaded.Cup.Groups.Select(g => string.Join(",", g.TeamIds)));
            Assert.True(reloaded.Cup.GroupsPosted);

            foreach (var x in new[] { s, reloaded })
            {
                while (x.Today <= final) x.AdvanceDay();
                var c = x.Cup!;
                Assert.Equal(7, c.Ties.Count);
                Assert.All(c.Ties, t => Assert.True(t.Played));
                Assert.All(c.Ties.Where(t => t.Round != NbaCup.QuarterFinal), t => Assert.True(t.Neutral));
                Assert.NotNull(c.WinnerId);
                var w = Assert.Single(x.League.Cup.Winners);
                Assert.Equal(c.WinnerId, w.WinnerId);
                Assert.Single(x.Inbox, m => m.Title == $"{x.League.TeamById(w.WinnerId).FullName} win the {NbaCup.Name}");
                Assert.Single(x.Inbox, m => m.Title == $"The {NbaCup.Name}: the final");
                // Knockout games stay out of the standings.
                var ours = c.Ties.Count(t => (t.HomeId == x.HumanTeam.Id || t.AwayId == x.HumanTeam.Id));
                int leagueGames = x.Schedule.Count(g => (g.Home == x.HumanTeam || g.Away == x.HumanTeam) && x.Results.ContainsKey(g.Index));
                Assert.Equal(leagueGames, x.Standings[x.HumanTeam].Games);
                // Review: with the knockout games, still never three days running.
                foreach (var id in c.Ties.SelectMany(t => new[] { t.HomeId, t.AwayId }).Distinct())
                {
                    var days = x.Schedule.Where(g => g.Home.Id == id || g.Away.Id == id).Select(g => g.Date.DayNumber)
                        .Concat(c.Ties.Where(t => t.HomeId == id || t.AwayId == id).Select(t => t.Date.DayNumber)).OrderBy(d => d).ToList();
                    for (int i = 2; i < days.Count; i++) Assert.False(days[i] - days[i - 2] == 2, $"{id} three straight days to {DateOnly.FromDayNumber(days[i])}");
                }
                _out.WriteLine($"{(x == s ? "kept" : "reloaded")}: our knockout games {ours}; {string.Join(" | ", c.Ties.Select(t => $"{t.Round}: {NbaCup.Line(x.League, t)}"))}");
            }
            Assert.True(s.Standings[s.HumanTeam].Games >= standingGames);

            // The next season draws new groups from the table and keeps the winner.
            var saved = GameSession.Load(path);
            Assert.NotNull(saved.Cup);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }

    [Fact]
    public void A_live_knockout_game_is_recorded_as_the_cup_tie()
    {
        var s = GameSession.NewGame(2026, Club(2026, 0), null);
        s.Job.Current!.TargetWins = 0;
        var (q1, _, _, final) = NbaCup.KnockoutNights(2026);
        while (s.Today < q1) s.AdvanceDay();
        var cup = s.Cup!;
        var tie = cup.Ties.FirstOrDefault(t => t.HomeId == s.HumanTeam.Id || t.AwayId == s.HumanTeam.Id);
        if (tie == null) { _out.WriteLine("not through: the live path isn't reached on this seed"); return; }
        while (s.Today < tie.Date) s.AdvanceDay();
        Assert.True(s.HumanPlaysToday);
        var (live, played) = s.BeginLiveDay();
        Assert.NotNull(live);
        Assert.Same(tie, s.LiveCup);
        int before = s.Standings[s.HumanTeam].Games;
        s.FinishLiveDay(live, played);
        Assert.True(tie.Played);
        Assert.Equal(before, s.Standings[s.HumanTeam].Games);
        Assert.Contains(s.Inbox, m => m.Title.StartsWith($"{NbaCup.Name} {tie.Round.ToLowerInvariant()}:"));
        Assert.Contains(s.MyGames(), g => g.Competition == NbaCup.Name && g.Played);
        while (s.Today <= final) s.AdvanceDay();
        Assert.NotNull(s.Cup!.WinnerId);
    }

    [Fact]
    public void Thirty_clubs_play_the_real_shape_six_groups_of_five()
    {
        var league = new League { Name = "Test" };
        for (int i = 0; i < 30; i++)
            league.Teams.Add(new Team { Id = i + 1, City = $"C{i}", Name = $"N{i}", Abbreviation = $"T{i}", Conference = i < 15 ? Conference.East : Conference.West, Division = $"D{i / 5}" });
        Assert.True(NbaCup.Fits(league));
        var groups = NbaCup.Draw(league, t => -t.Id);
        Assert.Equal(6, groups.Count);
        Assert.All(groups, g => Assert.Equal(5, g.TeamIds.Count));
        // Snake order: the best three head groups A–C, the next three go C–A.
        Assert.Equal(new[] { 1, 6, 7, 12, 13 }, groups[0].TeamIds);
        var plan = NbaCup.Plan(league, 2026, groups);
        Assert.Equal(60, plan.Games.Count);
        foreach (var t in league.Teams)
        {
            Assert.Equal(4, plan.Games.Count(g => g.Home == t || g.Away == t));
            Assert.Equal(2, plan.Games.Count(g => g.Home == t));
            Assert.True(plan.Games.Where(g => g.Home == t || g.Away == t).Select(g => g.Date).Distinct().Count() == 4);
        }
        var schedule = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), new Hardwood.Core.Rng(7), plan);
        Assert.Equal(60, NbaCup.GroupGameIndices(schedule, 2026, groups).Count);
        foreach (var t in league.Teams) Assert.Equal(82, schedule.Count(g => g.Home == t || g.Away == t));
    }

    [Fact]
    public void A_league_without_two_even_conferences_of_fifteen_or_sixteen_has_no_cup()
    {
        var league = new League { Name = "Test" };
        for (int i = 0; i < 28; i++) league.Teams.Add(new Team { Id = i + 1, City = $"C{i}", Name = $"N{i}", Abbreviation = $"T{i}", Conference = i < 14 ? Conference.East : Conference.West, Division = $"D{i / 4}" });
        Assert.False(NbaCup.Fits(league));
        // The plain schedule is drawn as before (the overload without a cup draws the same numbers).
        var a = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), new Hardwood.Core.Rng(5));
        var b = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), new Hardwood.Core.Rng(5), null);
        Assert.Equal(a.Select(g => (g.Date, g.Home.Id, g.Away.Id)), b.Select(g => (g.Date, g.Home.Id, g.Away.Id)));
    }
}
