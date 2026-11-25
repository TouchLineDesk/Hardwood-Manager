using System.Text.Json;
using Hardwood.Core;
using Hardwood.Core.College;
using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// CS11, slice 5 of the revised launch plan (run 195): the College All-American Game for seniors and the Hardwood
/// All-American Classic for high-school seniors, East against West, each on its own random stream.
/// </summary>
public class Run195AllAmericanTests
{
    private readonly ITestOutputHelper _out;
    public Run195AllAmericanTests(ITestOutputHelper output) => _out = output;

    private static (CollegeWorld World, GameSimulator Sim, Rng Rng) NewWorld(ulong seed)
    {
        var rng = new Rng(seed);
        var gen = new PlayerGenerator(new Rng(seed ^ 0x51), 2_000_000);
        return (CollegeWorld.Generate(gen, rng, new DateOnly(2026, 10, 1)), new GameSimulator(), rng);
    }

    [Fact]
    public void East_and_West_follow_home_and_the_overflow_goes_to_the_short_side()
    {
        // Seven East players, three West, five a side: the two weakest East players (ranked 6th and 7th East) go West.
        var best = new[] { (1, true), (2, true), (3, false), (4, true), (5, true), (6, true), (7, false), (8, true), (9, true), (10, false) };
        var (east, west) = CollegeWorld.Split(best, 5);
        Assert.Equal(new[] { 1, 2, 4, 5, 6 }, east);
        Assert.Equal(new[] { 3, 7, 8, 9, 10 }, west);
        Assert.True(CollegeWorld.IsEast("NY")); Assert.True(CollegeWorld.IsEast("NC"));
        Assert.False(CollegeWorld.IsEast("CA")); Assert.False(CollegeWorld.IsEast("IL")); Assert.False(CollegeWorld.IsEast("TX"));
    }

    [Fact]
    public void Both_games_are_named_and_played_on_their_days_with_honours_and_news()
    {
        var (c, sim, rng) = NewWorld(41);
        c.PlayThrough(new DateOnly(2027, 1, 31), sim, rng);
        Assert.Empty(c.AllAmerican);

        // The Classic: named February 1 (24 of the class that enrolls next summer, by rank), played March 31.
        c.PlayThrough(new DateOnly(2027, 2, 1), sim, rng);
        var hs = c.AllAmericanOf(CollegeWorld.HighSchoolGame);
        Assert.NotNull(hs);
        Assert.Equal(CollegeWorld.RecruitsASide, hs!.East.Count);
        Assert.Equal(CollegeWorld.RecruitsASide, hs.West.Count);
        Assert.False(hs.Played);
        var classOf = c.Recruits.Where(kv => kv.Value.ClassOf == 2027 && kv.Value.Rank > 0).OrderBy(kv => kv.Value.Rank).ToList();
        Assert.All(hs.East.Concat(hs.West), id => Assert.Equal(2027, c.Recruits[id].ClassOf));
        Assert.Contains(classOf[0].Key, hs.East.Concat(hs.West)); // the No. 1 recruit plays (healthy in a fresh world)
        Assert.Contains(c.News, n => n.Item1 == "The Hardwood All-American Classic rosters are named");

        for (var d = new DateOnly(2027, 2, 2); d <= new DateOnly(2027, 4, 6); d = d.AddDays(1))
        {
            c.PlayThrough(d, sim, rng);
            // K39 (run 204): the Classic on Tuesday Mar 30, the seniors' game on Friday Apr 2 (the Final Four is Saturday Apr 3).
            if (d == new DateOnly(2027, 3, 29)) Assert.False(hs.Played);
            if (d == new DateOnly(2027, 4, 1)) Assert.False(c.AllAmericanOf(CollegeWorld.SeniorGame)!.Played);
        }
        Assert.True(hs.Played);
        Assert.NotEqual(hs.EastPts, hs.WestPts);
        Assert.Equal(2 * CollegeWorld.RecruitsASide, hs.Box.Count);
        Assert.Equal(hs.EastPts, hs.Box.Where(b => b.East).Sum(b => b.Pts));
        Assert.Contains(hs.MvpId!.Value, hs.EastPts > hs.WestPts ? hs.East : hs.West); // the MVP is a winner

        // The seniors: named March 30, nobody from the Final Four or the NIT's last four, played April 2.
        var sr = c.AllAmericanOf(CollegeWorld.SeniorGame)!;
        Assert.True(sr.Played);
        Assert.Equal(CollegeWorld.SeniorsASide, sr.East.Count);
        Assert.Equal(CollegeWorld.SeniorsASide, sr.West.Count);
        var busy = c.Season.Games.Where(g => (g.Event == CollegeWorld.Madness && g.Round >= 5) || (g.Event == CollegeWorld.Nit && g.Round >= 3))
            .SelectMany(g => new[] { g.HomeId, g.AwayId }).ToHashSet();
        Assert.Equal(4, c.Season.Games.Where(g => g.Event == CollegeWorld.Madness && g.Round == 5).SelectMany(g => new[] { g.HomeId, g.AwayId }).Distinct().Count());
        var players = c.Players.ToDictionary(p => p.Id);
        foreach (var id in sr.East.Concat(sr.West))
        {
            Assert.DoesNotContain(sr.ProgramOf[id], busy);
            Assert.True(CollegeWorld.ClassYearOf(players[id], 2026)!.Final, $"{sr.NameOf(id)} is a senior");
        }

        Assert.Contains($"College All-American Game{(sr.MvpId == sr.East[0] ? " MVP" : "")} 2026-27", c.HonoursOf(sr.East[0]));
        Assert.Contains("Hardwood All-American Classic MVP 2026-27", c.HonoursOf(hs.MvpId.Value));
        Assert.Contains(c.News, n => n.Item1.Contains("win the College All-American Game"));
        Assert.Contains(c.News, n => n.Item1.Contains("win the Hardwood All-American Classic"));
        _out.WriteLine($"Classic: {hs.ScoreLine}, MVP {hs.NameOf(hs.MvpId.Value)} ({hs.LineOf(hs.MvpId.Value)})");
        _out.WriteLine($"Seniors: {sr.ScoreLine}, MVP {sr.NameOf(sr.MvpId!.Value)} ({sr.LineOf(sr.MvpId.Value)})");
        foreach (var n in c.News.Where(n => n.Item1.Contains("All-American"))) _out.WriteLine($"{n.Item1}\n  {n.Item2}");
    }

    [Fact]
    public void The_games_draw_nothing_from_the_world_and_hurt_nobody()
    {
        // Two copies of a world, played day by day through April 6. In b both games are marked played in advance, so b never
        // plays them: the seasons and the shared stream must still match, and nobody in a's games gets hurt on their days.
        var (a, simA, rngA) = NewWorld(77);
        var (b, simB, rngB) = NewWorld(77);
        a.PlayThrough(new DateOnly(2027, 1, 31), simA, rngA);
        b.PlayThrough(new DateOnly(2027, 1, 31), simB, rngB);
        foreach (var kind in new[] { CollegeWorld.HighSchoolGame, CollegeWorld.SeniorGame })
            b.AllAmerican.Add(new AllAmericanGame { Year = 2026, Kind = kind, EastPts = 0, WestPts = 0 });
        for (var d = new DateOnly(2027, 2, 1); d <= new DateOnly(2027, 4, 6); d = d.AddDays(1))
        {
            a.PlayThrough(d, simA, rngA);
            b.PlayThrough(d, simB, rngB);
        }
        Assert.Equal(a.Season.Games.Select(g => $"{g.HomeId}-{g.AwayId} {g.HomePts}-{g.AwayPts}"), b.Season.Games.Select(g => $"{g.HomeId}-{g.AwayId} {g.HomePts}-{g.AwayPts}"));
        Assert.Equal(rngA.NextULong(), rngB.NextULong());
        Assert.Equal(a.Season.Stats.Count, b.Season.Stats.Count);
        Assert.All(a.Season.Stats, kv => Assert.Equal(b.Season.Stats[kv.Key].Pts, kv.Value.Pts));
        foreach (var g in a.AllAmerican)
        {
            Assert.True(g.Played);
            Assert.All(g.East.Concat(g.West), id => Assert.NotEqual(g.Date, a.Players.First(p => p.Id == id).Injury?.Occurred));
        }
        Assert.Equal(2, a.AllAmerican.Count);
    }

    [Fact]
    public void The_games_survive_a_save_and_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run195-save-{Guid.NewGuid():N}.hwsave");
        try
        {
            var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026, 2026, Hardwood.Core.Season.GameSession.DefaultFullSim);
            int id = world.College!.Programs.First(p => p.FullName.StartsWith("George Mason")).Id;
            var s = Hardwood.Core.Season.GameSession.NewGame(2026, id, path);
            var cw = s.World.College!;
            cw.PlayThrough(new DateOnly(2027, 3, 31), new GameSimulator(), new Rng(5));
            cw.PlayThrough(new DateOnly(2027, 4, 6), new GameSimulator(), new Rng(6));
            Assert.Equal(2, cw.AllAmerican.Count(g => g.Played));
            s.SaveNow();
            var back = Hardwood.Core.Season.GameSession.Load(path).World.College!;
            Assert.Equal(JsonSerializer.Serialize(cw.AllAmerican), JsonSerializer.Serialize(back.AllAmerican));
            var mvp = cw.AllAmerican[0].MvpId!.Value;
            Assert.Equal(cw.HonoursOf(mvp), back.HonoursOf(mvp));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void A_coached_programs_signee_in_the_Classic_reaches_the_inbox_and_the_record_survives_json()
    {
        var (c, sim, rng) = NewWorld(41);
        c.PlayThrough(new DateOnly(2027, 1, 31), sim, rng);
        var top = c.Recruits.Where(kv => kv.Value.ClassOf == 2027 && kv.Value.CommittedTo != null).OrderBy(kv => kv.Value.Rank).First();
        c.ManagedProgramId = top.Value.CommittedTo;
        c.CoachNews.Clear();
        c.PlayThrough(new DateOnly(2027, 2, 1), sim, rng);
        Assert.Contains(c.CoachNews, n => n.Title.EndsWith("Hardwood All-Americans") && n.Body.Contains(c.AllAmericanOf(CollegeWorld.HighSchoolGame)!.NameOf(top.Key)));

        var g = c.AllAmericanOf(CollegeWorld.HighSchoolGame)!;
        var back = JsonSerializer.Deserialize<List<AllAmericanGame>>(JsonSerializer.Serialize(c.AllAmerican))!;
        Assert.Equal(JsonSerializer.Serialize(c.AllAmerican), JsonSerializer.Serialize(back));
        Assert.Equal(g.East, back[0].East);
        Assert.Equal(g.SchoolOf[top.Key], back[0].SchoolOf[top.Key]);
    }
}
