using Hardwood.Core;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// H78 (bug hunt, Nov 17): NBA teams played three and four nights in a row. The date pass only capped 4 games in 7 days, so a
/// team had ~37 back-to-backs, ~14 three-in-threes and ~3–4 four-in-fours a season (the real NBA: ~14, none and none).
/// </summary>
public class Run101ScheduleRestTests
{
    private static League Shape(int[] east, int[] west)
    {
        var league = new League { Name = "Shape" };
        int id = 1;
        foreach (var (conf, sizes) in new[] { (Conference.East, east), (Conference.West, west) })
            for (int d = 0; d < sizes.Length; d++)
                for (int i = 0; i < sizes[d]; i++, id++)
                    league.Teams.Add(new Team { Id = id, City = $"C{id}", Name = $"N{id}", Abbreviation = $"T{id}", Conference = conf, Division = $"{conf}{d}" });
        return league;
    }

    private static IEnumerable<(string label, League league, ulong seed)> Leagues()
    {
        foreach (ulong seed in new ulong[] { 2026, 31, 77 })
        {
            yield return ("NBA layout", Shape(new[] { 5, 5, 5 }, new[] { 5, 5, 5 }), seed);
            yield return ("D33's 32", Shape(new[] { 4, 4, 4, 4 }, new[] { 4, 4, 4, 4 }), seed);
            yield return ("real 30", RealCheck.Build(seed), seed);
        }
    }

    private static List<int> Days(List<ScheduledGame> s, Team t) =>
        s.Where(g => g.Home == t || g.Away == t).Select(g => g.Date.DayNumber).OrderBy(x => x).ToList();

    [Fact]
    public void No_team_plays_three_days_running_or_four_in_five()
    {
        foreach (var (label, league, seed) in Leagues())
        {
            var s = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), new Rng(seed));
            foreach (var t in league.Teams)
            {
                var d = Days(s, t);
                for (int i = 2; i < d.Count; i++)
                    Assert.False(d[i] - d[i - 2] == 2, $"{label} seed {seed}: {t.Abbreviation} plays three days running ending {DateOnly.FromDayNumber(d[i])}");
                for (int i = 3; i < d.Count; i++)
                    Assert.True(d[i] - d[i - 3] >= 5, $"{label} seed {seed}: {t.Abbreviation} plays 4 games in 5 days ending {DateOnly.FromDayNumber(d[i])}");
            }
        }
    }

    [Fact]
    public void Back_to_backs_are_near_the_real_leagues_count()
    {
        foreach (var (label, league, seed) in Leagues())
        {
            var s = ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), new Rng(seed));
            var counts = league.Teams.Select(t => { var d = Days(s, t); return Enumerable.Range(1, d.Count - 1).Count(i => d[i] - d[i - 1] == 1); }).ToList();
            Assert.InRange(counts.Average(), 11.0, 16.0);
            Assert.True(counts.Max() <= ScheduleBuilder.BackToBackCap, $"{label} seed {seed}: a team has {counts.Max()} back-to-backs");
        }
    }

    [Fact]
    public void The_season_runs_its_full_length_and_skips_the_All_Star_break()
    {
        foreach (var (label, league, seed) in Leagues())
        {
            var opening = new DateOnly(2026, 10, 21);
            var s = ScheduleBuilder.Build(league, opening, new Rng(seed));
            Assert.Equal(new DateOnly(2027, 4, 12), s.Max(g => g.Date));   // the real NBA's last day
            Assert.DoesNotContain(s, g => g.Date.Month == 2 && g.Date.Day is >= 13 and <= 17);
            // Every team plays in the season's last week (it ends together, not with a straggler's tail).
            var lastWeek = s.Where(g => g.Date > new DateOnly(2027, 4, 5)).SelectMany(g => new[] { g.Home, g.Away }).ToHashSet();
            Assert.Equal(league.Teams.Count, lastWeek.Count);
        }
    }

    [Fact]
    public void Dates_draw_no_random_numbers()
    {
        // The pairings and home/away are drawn first; the date pass must not touch the stream, so everything drawn after the
        // schedule (the season itself) starts from the same RNG state as before H78.
        var a = new Rng(5); var b = new Rng(5);
        ScheduleBuilder.Build(Shape(new[] { 5, 5, 5 }, new[] { 5, 5, 5 }), new DateOnly(2026, 10, 21), a);
        var league = Shape(new[] { 5, 5, 5 }, new[] { 5, 5, 5 });
        ScheduleBuilder.Build(league, new DateOnly(2027, 10, 20), b);   // a different opening day: different dates, same draws
        Assert.Equal(a.NextDouble(), b.NextDouble());
    }
}
