using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 95. C5 part 2 (college-sim-design #165): listed rivals in different conferences meet every season, and a
/// college home court is stronger than the pros' and varies by arena (tradition and size), on the full engine, the quick
/// sim and the ratings alike. Neutral-court events keep no edge.</summary>
public class Run95HomeCourtTests
{
    private readonly ITestOutputHelper _out;
    public Run95HomeCourtTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Home_court_varies_by_arena_and_neutral_events_have_none()
    {
        var (_, world) = RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        int Id(string school) => cw.Programs.Single(t => t.City == school).Id;
        var edges = cw.Programs.Select(t => cw.HomeCourtPoints(t.Id)).ToList();
        Assert.All(edges, e => Assert.InRange(e, CollegeWorld.HomeCourtMin, CollegeWorld.HomeCourtMax));
        double mean = edges.Average();
        _out.WriteLine($"home court: mean {mean:0.00}, min {edges.Min():0.0}, max {edges.Max():0.0}; Duke {cw.HomeCourtPoints(Id("Duke"))}, Kansas {cw.HomeCourtPoints(Id("Kansas"))}, Albany {cw.HomeCourtPoints(Id("Albany"))}");
        Assert.InRange(mean, 3.0, 3.6); // KenPom: about 3 to 3.5 points nationally
        Assert.True(cw.HomeCourtPoints(Id("Duke")) >= 4.0);
        Assert.True(cw.HomeCourtPoints(Id("Kansas")) >= 4.0);
        Assert.True(cw.HomeCourtPoints(Id("Albany")) < cw.HomeCourtPoints(Id("Duke")) - 0.6);
        Assert.StartsWith("Cameron Indoor Stadium: one of the loudest in the country (+", cw.HomeCourtLine(Id("Duke")));

        // The rules each game is played under: the home arena's own edge; none at an event.
        var conf = cw.Season.Games.First(g => g.Stage == "Conference" && g.HomeId == Id("Duke"));
        var rules = cw.GameRules(conf);
        Assert.False(rules.NeutralSite);
        Assert.Equal(cw.HomeCourtPoints(Id("Duke")), rules.HomePoints, 6);
        Assert.Equal(Rules.CollegeNeutral, cw.GameRules(new CollegeGame { Event = CollegeWorld.Madness, HomeId = Id("Duke") }));
        Assert.Equal(0, Rules.CollegeNeutral.HomePoints);
        Assert.Equal(2.5, Rules.Nba.HomePoints);
        // The engine's home logit scales with it; the pros are untouched.
        var cfg = new EngineConfig();
        Assert.Equal(cfg.HomeLogit * rules.HomeScale, cfg.For(rules).HomeLogit, 9);
        Assert.Same(cfg, cfg.For(Rules.Nba));
        Assert.Equal(0, cfg.For(Rules.CollegeNeutral).HomeLogit);
    }

    [Fact]
    public void Non_conference_rivals_meet_every_season_and_home_alternates()
    {
        var (_, world) = RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        int Id(string school) => cw.Programs.Single(t => t.City == school).Id;
        var rivals = cw.NonConferenceRivalries();
        Assert.Contains(rivals, r => r.Name == "Battle for the Bluegrass");
        Assert.DoesNotContain(rivals, r => r.InState || cw.ConferenceOf[r.A] == cw.ConferenceOf[r.B]);
        Assert.Equal("Battle for the Bluegrass", rivals.First(r => r.A == Id("Kentucky") || r.B == Id("Kentucky")).Name);

        Dictionary<string, int> HomeOf(CollegeSeason s) => rivals.ToDictionary(r => CollegeWorld.PairKey(r.A, r.B), r =>
        {
            var games = s.Games.Where(g => g.Stage == "Non-conference" && CollegeWorld.PairKey(g.HomeId, g.AwayId) == CollegeWorld.PairKey(r.A, r.B)).ToList();
            Assert.True(games.Count >= 1, $"{cw.Program(r.A)!.City} v {cw.Program(r.B)!.City} not scheduled");
            return games[0].HomeId;
        });
        void NoDoubleBooking(CollegeSeason s)
        {
            foreach (var day in s.Games.GroupBy(g => g.Date))
            {
                var ids = day.SelectMany(g => new[] { g.HomeId, g.AwayId }).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
            }
        }
        var first = HomeOf(cw.Season);
        NoDoubleBooking(cw.Season);
        _out.WriteLine($"{rivals.Count} non-conference rivalries scheduled; most for one program: {rivals.SelectMany(r => new[] { r.A, r.B }).GroupBy(x => x).Max(g => g.Count())}");

        cw.StartSeason(2027, new Rng(5));
        var second = HomeOf(cw.Season);
        NoDoubleBooking(cw.Season);
        Assert.All(first, kv => Assert.NotEqual(kv.Value, second[kv.Key]));
    }

    [Fact]
    public void Home_sides_win_about_as_often_as_in_real_conference_play()
    {
        var (_, world) = RealWorldData.NewWorld(2026, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        cw.PlayThrough(new DateOnly(2027, 3, 1), new GameSimulator(), new Rng(2026));
        var conf = cw.Season.Games.Where(g => g.Played && g.Stage == "Conference").ToList();
        double home = conf.Count(g => g.HomePts > g.AwayPts) / (double)conf.Count;
        double margin = conf.Average(g => g.HomePts!.Value - g.AwayPts!.Value);
        _out.WriteLine($"conference games {conf.Count}: home sides won {home:P1}, average home margin {margin:+0.0;-0.0}");
        Assert.InRange(home, 0.56, 0.70); // real Division I conference play: roughly 60-65%
        Assert.InRange(margin, 2.0, 5.0);
    }
}
