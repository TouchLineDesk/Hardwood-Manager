using Hardwood.Core.Engine;
using Hardwood.Core.RealWorld;
using Hardwood.Core.World;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 222 (the real formats, `danny-world-leagues-nov26`): the EuroLeague is 20 clubs (its licensed clubs plus the best of the
/// rest), home and away over 38 rounds from late September to mid-April with double-round weeks, a play-in for 7th-10th, best-of-five
/// quarter-finals and a Final Four in late May. The EuroCup is 20 clubs, once each, single-game knockouts for 16 and a
/// best-of-three final.
/// </summary>
public class Run222RealFormatsTests
{
    [Fact]
    public void Every_licensed_club_is_a_real_club_in_a_source_league()
    {
        var p = Competitions.Get(Competitions.Elite);
        Assert.Equal(19, p.Members.Length);
        var sources = p.Entrants.Select(e => Competitions.Get(e.LeagueKey)).ToList();
        foreach (var m in p.Members)
            Assert.True(sources.Any(s => s.Clubs.Any(c => c.City + "|" + c.Name == m)), $"{m} is in no source league");
    }

    [Theory]
    [InlineData(31UL)]
    [InlineData(2026UL)]
    public void The_cups_have_their_real_fields_and_calendars(ulong seed)
    {
        var (_, world) = RealWorldData.NewWorld(seed, players: false);
        var el = world.Competitions.First(c => c.Key == Competitions.Elite);
        var ec = world.Competitions.First(c => c.Key == WorldLeagues.EuroCup);
        Assert.Equal(20, el.Season.Participants.Count);
        Assert.Equal(20, ec.Season.Participants.Count);
        Assert.Empty(el.Season.Participants.Intersect(ec.Season.Participants));
        var names = el.Season.Participants.Select(id => world.TeamById(id)!).Select(t => t.City + "|" + t.Name).ToHashSet();
        Assert.All(Competitions.Get(Competitions.Elite).Members, m => Assert.Contains(m, names));

        // 38 rounds of 10 games, Sep 29 to Apr 17, nobody twice on a day, and some weeks with two rounds.
        var f = el.Season.Fixtures;
        Assert.Equal(380, f.Count);
        Assert.Equal(new DateOnly(2026, 9, 29), f.Min(x => x.Date));   // a Tuesday
        Assert.Equal(new DateOnly(2027, 4, 15), f.Max(x => x.Date));   // the last Thursday on or before Apr 17
        Assert.Equal(38, f.Select(x => x.Date).Distinct().Count());
        foreach (var day in f.GroupBy(x => x.Date))
            Assert.Equal(20, day.SelectMany(x => new[] { x.HomeId, x.AwayId }).Distinct().Count());
        var weeks = f.Select(x => x.Date).Distinct().GroupBy(d => d.DayNumber / 7).ToList();
        Assert.Contains(weeks, w => w.Count() == 2);
        Assert.Contains(weeks, w => w.Count() == 1);

        // The EuroCup: 18 Wednesdays (two groups of ten, home and away since run 227), Oct 7 to early March.
        Assert.Equal(180, ec.Season.Fixtures.Count);
        Assert.All(ec.Season.Fixtures, x => Assert.Equal(DayOfWeek.Wednesday, x.Date.DayOfWeek));
        Assert.True(ec.Season.Fixtures.Max(x => x.Date) <= new DateOnly(2027, 3, 4));
    }

    [Fact]
    public void The_EuroLeague_plays_a_play_in_then_the_quarter_finals_and_the_Final_Four()
    {
        var (_, world) = RealWorldData.NewWorld(31, players: false);
        world.FinishSeasons(new GameSimulator());
        var el = world.Competitions.First(c => c.Key == Competitions.Elite);
        var s = el.Season;
        Assert.True(s.IsComplete);
        var table = s.Table().Select(r => r.TeamId).ToList();

        var a = s.Series.Single(x => x.Round == GameWorld.PlayInHigh);
        var b = s.Series.Single(x => x.Round == GameWorld.PlayInLow);
        var fin = s.Series.Single(x => x.Round == GameWorld.PlayInFinal);
        Assert.Equal((table[6], table[7]), (a.HighId, a.LowId));
        Assert.Equal((table[8], table[9]), (b.HighId, b.LowId));
        Assert.Equal((a.LoserId, b.WinnerId), (fin.HighId, fin.LowId));
        Assert.All(new[] { a, b, fin }, x => Assert.Equal(1, x.BestOf));

        // The play-in is in late April, not at the Final Four.
        var playInDays = s.Fixtures.Where(x => x.Stage.StartsWith("Play-In")).Select(x => x.Date).ToList();
        Assert.Equal(3, playInDays.Count);
        Assert.All(playInDays, d => Assert.Equal(4, d.Month));
        Assert.True(playInDays.Max() > s.Fixtures.Where(x => x.Stage == "Regular season").Max(x => x.Date));

        // Quarter-finals: 1v8, 4v5, 3v6, 2v7 with the play-in's winners as 7 and 8, best of five.
        var qf = s.Series.Where(x => x.Round == "Quarter-finals").ToList();
        Assert.Equal(4, qf.Count);
        Assert.All(qf, x => Assert.Equal(5, x.BestOf));
        Assert.Contains(qf, x => x.HighId == table[0] && x.LowId == fin.WinnerId);
        Assert.Contains(qf, x => x.HighId == table[1] && x.LowId == a.WinnerId);
        Assert.DoesNotContain(qf, x => x.HighId == a.LoserId && a.LoserId != fin.WinnerId);

        // The Final Four: a Friday and a Sunday in late May.
        var ff = s.Fixtures.Where(x => x.Stage is "Final Four semi-finals" or "Final").ToList();
        Assert.Equal(3, ff.Count);
        Assert.All(ff, x => Assert.Equal(5, x.Date.Month));
        Assert.All(ff.Where(x => x.Stage == "Final Four semi-finals"), x => Assert.Equal(DayOfWeek.Friday, x.Date.DayOfWeek));
        Assert.Equal(DayOfWeek.Sunday, ff.Single(x => x.Stage == "Final").Date.DayOfWeek);
        Assert.NotNull(s.ChampionId);
        Assert.Contains(world.News, n => n.Item1.Contains("play-in"));

        // The EuroCup: single-game knockouts (3rd-6th in each group in the eighth-finals since run 227), a best-of-three final.
        var ec = world.Competitions.First(c => c.Key == WorldLeagues.EuroCup).Season;
        Assert.True(ec.IsComplete);
        Assert.Equal(4, ec.Series.Count(x => x.Round == "Eighth-finals"));
        Assert.All(ec.Series.Where(x => x.Round != "Final"), x => Assert.Equal(1, x.BestOf));
        Assert.Equal(3, ec.Series.Single(x => x.Round == "Final").BestOf);
    }

    [Fact]
    public void An_older_save_in_the_EuroCup_quarter_finals_plays_on_to_one_final()
    {
        // Review (run 222): a v0.209 save holds its EuroCup quarter-finals at round index 0 (the eighth-finals' place now).
        var (_, world) = RealWorldData.NewWorld(31, players: false);
        var ec = world.Competitions.First(c => c.Key == WorldLeagues.EuroCup);
        var sim = new GameSimulator();
        var day = new DateOnly(2027, 1, 1);
        while (ec.Season.Stage != "Quarter-finals" && day < new DateOnly(2027, 6, 1)) { world.PlayThrough(day, sim); day = day.AddDays(1); }
        Assert.Equal("Quarter-finals", ec.Season.Stage);
        foreach (var x in ec.Season.Series.Where(x => x.Round == "Quarter-finals")) x.RoundIndex = 0;
        world.FinishSeasons(sim);
        Assert.True(ec.Season.IsComplete);
        Assert.Equal(4, ec.Season.Series.Count(x => x.Round == "Quarter-finals"));
        Assert.Equal(2, ec.Season.Series.Count(x => x.Round == "Semi-finals"));
        Assert.Single(ec.Season.Series, x => x.Round == "Final");
    }
}
