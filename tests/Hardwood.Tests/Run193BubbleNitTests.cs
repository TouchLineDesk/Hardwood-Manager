using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Slice 3 of the revised launch plan (run 193): the NET and the quadrants on the bubble, the NIT for the best teams left out,
/// and K35: the Top 25 is released on Mondays and stays put all week.
/// </summary>
public class Run193BubbleNitTests
{
    private readonly ITestOutputHelper _out;
    public Run193BubbleNitTests(ITestOutputHelper output) => _out = output;

    private static (CollegeWorld World, GameSimulator Sim, Rng Rng) NewWorld(ulong seed)
    {
        var rng = new Rng(seed);
        var gen = new PlayerGenerator(new Rng(seed ^ 0x51), 2_000_000);
        return (CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1)), new GameSimulator(), rng);
    }

    [Theory]
    [InlineData(30, 'H', 1)] [InlineData(31, 'H', 2)] [InlineData(75, 'H', 2)] [InlineData(160, 'H', 3)] [InlineData(161, 'H', 4)]
    [InlineData(50, 'N', 1)] [InlineData(100, 'N', 2)] [InlineData(200, 'N', 3)] [InlineData(201, 'N', 4)]
    [InlineData(75, 'A', 1)] [InlineData(135, 'A', 2)] [InlineData(240, 'A', 3)] [InlineData(241, 'A', 4)]
    public void The_quadrants_use_the_committees_lines(int oppNet, char venue, int quad) =>
        Assert.Equal(quad, CollegeWorld.Quad(oppNet, venue));

    [Fact]
    public void The_NET_ranks_every_program_and_the_quadrants_add_up_to_the_record()
    {
        var (c, sim, rng) = NewWorld(41);
        c.PlayThrough(new DateOnly(2027, 2, 10), sim, rng);
        var net = c.NetRanks();
        Assert.Equal(c.Programs.Count, net.Count);
        Assert.Equal(Enumerable.Range(1, c.Programs.Count), net.Values.OrderBy(v => v));
        // The bracketology field is ordered by the same rating: the best at-large team has the best NET of the at-large teams.
        var p = c.Bracketology();
        var atLarge = p.Field.Where(id => !p.AutoBids.ContainsKey(id)).ToList();
        Assert.Equal(atLarge.Min(id => net[id]), net[atLarge.OrderBy(id => net[id]).First()]);
        foreach (var t in c.Programs.Take(40))
        {
            var q = c.Quads(t.Id);
            var (w, l) = c.Record(t.Id);
            Assert.Equal(w, q.Wins.Sum());
            Assert.Equal(l, q.Losses.Sum());
        }
        int bubble = p.FirstFourOut[0];
        _out.WriteLine($"{c.Program(bubble)!.FullName}: {c.Resume(bubble)} · {c.Quads(bubble).Line}");
        Assert.StartsWith("NET ", c.Resume(bubble));
    }

    [Fact]
    public void The_poll_is_released_on_Mondays_and_holds_all_week()
    {
        var (c, sim, rng) = NewWorld(41);
        var day = new DateOnly(2026, 11, 1);
        Dictionary<int, int?>? week = null;
        DateOnly? weekOf = null;
        int releases = 0, checkedDays = 0;
        DateOnly? lastRelease = null;
        while (day < new DateOnly(2027, 2, 15))
        {
            c.PlayThrough(day, sim, rng);
            if (c.Season.PollDay != lastRelease) { releases++; lastRelease = c.Season.PollDay; }
            if (c.Season.PollRelease.Count > 0)
            {
                var ranks = c.Programs.ToDictionary(t => t.Id, t => c.Rank(t.Id));
                var monday = CollegeWorld.PollMondayOf(day);
                if (weekOf == monday && week != null)
                {
                    Assert.Equal(week, ranks);   // no change between Mondays, games or not
                    checkedDays++;
                }
                else if (c.Season.PollDay is { } released && released.DayOfWeek == DayOfWeek.Monday)
                    Assert.Equal(monday, released);
                week = ranks; weekOf = monday;
            }
            day = day.AddDays(1);
        }
        _out.WriteLine($"{releases} releases, {checkedDays} days checked against their Monday");
        Assert.InRange(releases, 12, 17);
        Assert.True(checkedDays > 50);
        // The arrows compare with last week's release, and the page's poll is the release.
        Assert.Equal(c.PollOrder(), c.Poll().Select(e => e.Team.Id).ToList());
        Assert.Equal(c.Season.PollRelease.Take(CollegeWorld.PollSize), c.PollOrder());
        Assert.NotEmpty(c.Season.PollBefore);
    }

    [Fact]
    public void The_NIT_takes_32_teams_left_out_hosts_three_rounds_and_crowns_a_champion()
    {
        var (c, sim, rng) = NewWorld(41);
        c.PlayThrough(new DateOnly(2027, 4, 10), sim, rng);
        var s = c.Season;
        Assert.NotNull(s.ChampionId);
        Assert.Equal(CollegeWorld.NitSize, s.NitField.Count);
        Assert.All(s.NitField, id => Assert.False(s.Seeds.ContainsKey(id)));
        var nit = s.Games.Where(g => g.Event == CollegeWorld.Nit).ToList();
        Assert.Equal(31, nit.Count);
        Assert.All(nit, g => Assert.True(g.Played));
        Assert.Equal(new[] { 16, 8, 4, 2, 1 }, Enumerable.Range(0, 5).Select(r => nit.Count(g => g.Round == r)));
        // The better seed hosts the first three rounds; the semi-finals and the final are neutral.
        foreach (var g in nit.Where(g => g.Round < 3))
        {
            Assert.False(g.IsNeutral());
            Assert.True(s.NitField.IndexOf(g.HomeId) < s.NitField.IndexOf(g.AwayId), $"round {g.Round}: the better seed is at home");
        }
        Assert.All(nit.Where(g => g.Round >= 3), g => Assert.True(g.IsNeutral()));
        // The NIT finishes before the national championship game, and its champion is crowned in the news.
        Assert.True(nit.Max(g => g.Date) < s.RoundDates[CollegeWorld.Madness][6]);
        int champ = s.EventWinners[CollegeWorld.Nit];
        Assert.Contains(c.News, n => n.Title == $"{c.Program(champ)!.FullName} win the NIT");
        Assert.Contains(c.News, n => n.Title == "The NIT field is set");
        Assert.False(CollegeWorld.IsRegularSeason(nit[0]));
        _out.WriteLine($"NIT champions: {c.Program(champ)!.FullName} (No. {c.NitSeedOf(champ)} seed)");
    }

    [Fact]
    public void The_NIT_does_not_move_March_Madness()
    {
        // The NIT plays on its own stream: re-playing its games with different results leaves every March Madness score
        // as it was (the two worlds differ only in the NIT's dice).
        var (a, simA, rngA) = NewWorld(77);
        var (b, simB, rngB) = NewWorld(77);
        a.PlayThrough(new DateOnly(2027, 3, 16), simA, rngA);
        a.PlayThrough(new DateOnly(2027, 4, 10), simA, rngA);
        b.PlayThrough(new DateOnly(2027, 3, 16), simB, rngB);
        Assert.True(b.Season.Selected);
        // Scramble the NIT's pairings in b: different games, different hashes, different results.
        foreach (var g in b.Season.Games.Where(g => g.Event == CollegeWorld.Nit && g.Round == 0)) (g.HomeId, g.AwayId) = (g.AwayId, g.HomeId);
        b.PlayThrough(new DateOnly(2027, 4, 10), simB, rngB);
        string Scores(CollegeWorld w) => string.Join(";", w.Season.Games.Where(g => g.Event == CollegeWorld.Madness).OrderBy(g => g.Round).ThenBy(g => g.Slot).Select(g => $"{g.HomeId}-{g.AwayId}:{g.HomePts}-{g.AwayPts}"));
        Assert.Equal(Scores(a), Scores(b));
        Assert.Equal(a.Season.ChampionId, b.Season.ChampionId);
        Assert.NotEqual(a.Season.EventWinners[CollegeWorld.Nit] + "" + a.Season.Games.Where(g => g.Event == CollegeWorld.Nit).Sum(g => g.HomePts),
                        b.Season.EventWinners[CollegeWorld.Nit] + "" + b.Season.Games.Where(g => g.Event == CollegeWorld.Nit).Sum(g => g.HomePts));
    }

    [Fact]
    public void A_coach_left_out_hears_his_NIT_seed_and_first_game()
    {
        var (c, sim, rng) = NewWorld(41);
        c.PlayThrough(new DateOnly(2027, 3, 1), sim, rng);
        // Coach the best team the projection leaves out, so it lands in the NIT.
        var p = c.ProjectNow();
        int me = p.FirstFourOut[0];
        c.ManagedProgramId = me;
        var day = new DateOnly(2027, 3, 2);
        while (!c.Season.Selected && day < new DateOnly(2027, 3, 20)) { c.PlayThrough(day, sim, rng); day = day.AddDays(1); }
        Assert.True(c.Season.Selected);
        if (c.Season.Seeds.ContainsKey(me)) return; // the conference tournament changed the bubble: nothing to check
        Assert.True(c.InNit(me));
        var news = c.CoachNews.Last(n => n.Title.StartsWith("Selection Sunday"));
        _out.WriteLine(news.Body);
        Assert.Contains($"The NIT has picked us instead: a No. {c.NitSeedOf(me)} seed.", news.Body);
    }
}
