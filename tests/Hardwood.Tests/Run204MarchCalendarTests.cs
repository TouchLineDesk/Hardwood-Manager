using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// K39 (run 204): March on the real weekdays. Selection Sunday is a Sunday, the Final Four a Saturday and the final a Monday;
/// the early rounds are split over two days; the conference tournaments end on Selection Sunday; the NIT and the All-American
/// games sit in Final Four week. A season started before v0.192.0 keeps its fixed day numbers.
/// </summary>
public class Run204MarchCalendarTests
{
    private readonly ITestOutputHelper _out;
    public Run204MarchCalendarTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Every_year_from_2027_to_2035_falls_on_the_right_weekdays()
    {
        for (int season = 2026; season <= 2034; season++)
        {
            var sel = MarchCalendar.SelectionSunday(season);
            var m = MarchCalendar.Madness(season, weekdays: true);
            var conf = MarchCalendar.ConferenceTournament(season, weekdays: true);
            var nit = MarchCalendar.Nit(season, weekdays: true);
            Assert.Equal(DayOfWeek.Sunday, sel.DayOfWeek);
            Assert.InRange(sel.Day, 12, 18);
            Assert.Equal(3, sel.Month);
            Assert.Equal(new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday, DayOfWeek.Saturday, DayOfWeek.Thursday, DayOfWeek.Saturday, DayOfWeek.Saturday, DayOfWeek.Monday },
                m.Select(d => d.DayOfWeek));
            Assert.Equal(4, m[5].Month);
            Assert.InRange(m[5].Day, 1, 7);
            Assert.Equal(new[] { DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday }, conf.Select(d => d.DayOfWeek));
            Assert.Equal(sel, conf[2]);
            // The regular season's last day is Mar 9 (CollegeWorld's conference schedule), so the tournaments never overlap it.
            Assert.True(conf[0] > new DateOnly(season + 1, 3, 9));
            Assert.True(nit[0] > sel && nit[4] < m[6] && nit[3] < m[5]);
            var (hsNamed, hs) = MarchCalendar.AllAmerican(true, season, true);
            var (srNamed, sr) = MarchCalendar.AllAmerican(false, season, true);
            Assert.Equal(DayOfWeek.Tuesday, hs.DayOfWeek);
            Assert.Equal(DayOfWeek.Friday, sr.DayOfWeek);
            Assert.True(srNamed > m[4].AddDays(1) && sr < m[5], "the seniors are named after the Elite Eight and play before the semi-finals");
            Assert.True(hsNamed < hs);
            _out.WriteLine($"{season + 1}: Selection Sunday {sel:ddd MMM d}, Final Four {m[5]:ddd MMM d}, final {m[6]:ddd MMM d}");
        }
        // The real 2027 dates (critic #24).
        Assert.Equal(new DateOnly(2027, 3, 14), MarchCalendar.SelectionSunday(2026));
        Assert.Equal(new DateOnly(2027, 4, 3), MarchCalendar.FinalFour(2026));
    }

    [Fact]
    public void An_old_season_keeps_its_fixed_dates()
    {
        Assert.Equal(new[] { new DateOnly(2027, 3, 10), new DateOnly(2027, 3, 11), new DateOnly(2027, 3, 12) }, MarchCalendar.ConferenceTournament(2026, false));
        Assert.Equal(new DateOnly(2027, 3, 17), MarchCalendar.Madness(2026, false)[0]);
        Assert.Equal(new DateOnly(2027, 4, 6), MarchCalendar.Madness(2026, false)[6]);
        Assert.Equal(new DateOnly(2027, 3, 18), MarchCalendar.Nit(2026, false)[0]);
        Assert.Equal((new DateOnly(2027, 3, 30), new DateOnly(2027, 4, 3)), MarchCalendar.AllAmerican(false, 2026, false));
        Assert.False(new CollegeSeason().Weekdays); // an older save's season deserializes without the flag
    }

    [Fact]
    public void A_played_March_uses_the_weekdays_and_splits_the_early_rounds()
    {
        var rng = new Rng(41);
        var gen = new PlayerGenerator(new Rng(41 ^ 0x51), 2_000_000);
        var c = CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1));
        Assert.True(c.Season.Weekdays);
        c.PlayThrough(new DateOnly(2027, 4, 10), new GameSimulator(), rng);
        var s = c.Season;
        Assert.NotNull(s.ChampionId);
        var sel = MarchCalendar.SelectionSunday(2026);
        var conf = s.Games.Where(g => g.Stage == "Conference tournament").ToList();
        Assert.Equal(sel, conf.Max(g => g.Date));
        Assert.All(conf, g => Assert.InRange(g.Date, sel.AddDays(-2), sel));
        var madness = s.Games.Where(g => g.Event == CollegeWorld.Madness).ToList();
        var expect = new Dictionary<int, DayOfWeek[]>
        {
            [0] = new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday },
            [1] = new[] { DayOfWeek.Thursday, DayOfWeek.Friday },
            [2] = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday },
            [3] = new[] { DayOfWeek.Thursday, DayOfWeek.Friday },
            [4] = new[] { DayOfWeek.Saturday, DayOfWeek.Sunday },
            [5] = new[] { DayOfWeek.Saturday },
            [6] = new[] { DayOfWeek.Monday },
        };
        foreach (var (round, days) in expect)
        {
            var games = madness.Where(g => g.Round == round).ToList();
            if (games.Count == 0) continue; // no First Four in a year without one
            var played = games.Select(g => g.Date.DayOfWeek).Distinct().OrderBy(d => ((int)d + 6) % 7).ToArray();
            if (round == 0) { Assert.All(played, d => Assert.Contains(d, days)); continue; } // by the winner's half of the bracket
            Assert.Equal(days, played);
            if (days.Length == 2) Assert.Equal(games.Count / 2, games.Count(g => g.Date == games.Min(x => x.Date)));
        }
        // A team never plays two days running in March Madness.
        foreach (var id in madness.SelectMany(g => new[] { g.HomeId, g.AwayId }).Distinct())
        {
            var days = madness.Where(g => g.HomeId == id || g.AwayId == id).Select(g => g.Date).OrderBy(d => d).ToList();
            for (int i = 1; i < days.Count; i++) Assert.True(days[i].DayNumber - days[i - 1].DayNumber >= 2, $"{c.Program(id)!.FullName} plays {days[i - 1]} and {days[i]}");
        }
        Assert.Equal(new DateOnly(2027, 4, 5), madness.Single(g => g.Round == 6).Date);
        var nit = s.Games.Where(g => g.Event == CollegeWorld.Nit).ToList();
        Assert.Equal(MarchCalendar.Nit(2026, true)[4], nit.Single(g => g.Round == 4).Date);
        var sr = c.AllAmericanOf(CollegeWorld.SeniorGame);
        Assert.NotNull(sr);
        Assert.Equal(DayOfWeek.Friday, sr!.Date.DayOfWeek);
    }
}
