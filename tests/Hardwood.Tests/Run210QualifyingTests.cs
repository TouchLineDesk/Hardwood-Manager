using Hardwood.Core.International;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>International plan I3 (run 210): the qualifying windows (quick results on their own stream), the FIBA world ranking
/// from results, and the World Cup field taken from the qualifiers.</summary>
public class Run210QualifyingTests
{
    private readonly ITestOutputHelper _out;
    public Run210QualifyingTests(ITestOutputHelper output) => _out = output;

    private static GameSession NewCareer(string? path = null)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("Virginia Tech"));
        return GameSession.NewGame(31, me.Id, path);
    }

    private static FibaEvent Window(int year, int month) =>
        FibaCalendar.InYear(year).Single(e => e.Kind == FibaEventKind.Window && e.Start.Month == month);

    [Fact]
    public void A_round_robin_meets_everyone_home_and_away()
    {
        foreach (int n in new[] { 3, 4, 5 })
        {
            var codes = Enumerable.Range(0, n).Select(i => $"T{i}").ToList();
            var rounds = Qualifying.Rounds(codes);
            var games = rounds.SelectMany(r => r).ToList();
            Assert.Equal(n * (n - 1), games.Count);
            Assert.Equal(games.Count, games.Distinct().Count());
            foreach (var r in rounds) Assert.Equal(r.Count * 2, r.SelectMany(g => new[] { g.Home, g.Away }).Distinct().Count());
        }
    }

    [Fact]
    public void Windows_are_pure_and_the_world_cup_field_comes_from_the_qualifiers()
    {
        var s = NewCareer();
        var before = Run198ReloadRolloverTests.Print(s);
        var h = new InternationalHistory();
        var nov = Window(2026, 11);
        var teams = NationalTeams.Build(WorldCup.Entries(s, nov.Start), nov.Start);
        var w1 = Qualifying.Play(h, nov, teams, s.Seed);
        var feb = Window(2027, 2);
        var w2 = Qualifying.Play(h, feb, NationalTeams.Build(WorldCup.Entries(s, feb.Start), feb.Start), s.Seed);
        Assert.Equal(before, Run198ReloadRolloverTests.Print(s)); // nothing shared moved

        // Same seed, same results.
        var h2 = new InternationalHistory();
        Qualifying.Play(h2, nov, teams, s.Seed);
        Assert.Equal(w1.Games, h2.Windows[0].Games);

        Assert.Equal("2027 World Cup", w1.Target);
        Assert.Equal(new[] { 0, 1 }, new[] { w1.Index, w2.Index });
        var cycle = Assert.Single(h.Cycles);
        Assert.All(cycle.Groups.Values, g => Assert.InRange(g.Count, 2, 6));
        Assert.Equal(teams.Count(t => t.Rank > 0), cycle.Groups.Values.Sum(g => g.Count));
        // Every team plays two games a window (a team in an odd group sits one out).
        foreach (var w in new[] { w1, w2 })
            foreach (var (name, codes) in cycle.Groups)
                foreach (var c in codes)
                    Assert.InRange(w.Games.Count(g => g.Round == name && (g.Home == c || g.Away == c)), codes.Count % 2 == 1 ? 1 : 2, 2);
        Assert.DoesNotContain(h.Windows.SelectMany(w => w.Games), g => g.HomePts == g.AwayPts);
        double avg = h.Windows.SelectMany(w => w.Games).Average(g => (g.HomePts + g.AwayPts) / 2.0);
        double homeWins = h.Windows.SelectMany(w => w.Games).Average(g => g.HomePts > g.AwayPts ? 1.0 : 0.0);
        _out.WriteLine($"{h.Windows.Sum(w => w.Games.Count)} games, {avg:F1} a side, home wins {homeWins:P0}");
        Assert.InRange(avg, 70, 88);

        foreach (var (name, codes) in cycle.Groups.OrderBy(g => g.Key))
            _out.WriteLine($"{name}: " + string.Join(", ", Qualifying.Table(h, w1.Target, name, codes)));

        // The World Cup field: every zone's group winners are in it.
        s.World.International = h;
        var cup = WorldCup.Play(s, 2027)!;
        var field = cup.Teams.Select(t => t.Code).ToHashSet();
        foreach (var (name, codes) in cycle.Groups)
            Assert.Contains(Qualifying.Table(h, w1.Target, name, codes)[0], field);
        _out.WriteLine($"Field: {string.Join(" ", cup.Teams.Select(t => t.Code))}; gold {cup.Gold}");
        Assert.Equal(32, cup.Teams.Count);
    }

    [Fact]
    public void The_ranking_moves_with_results_but_the_top_ten_holds_over_three_cycles()
    {
        var s = NewCareer();
        var day = new DateOnly(2026, 11, 23);
        var teams = NationalTeams.Build(WorldCup.Entries(s, day), day);
        var h = new InternationalHistory();
        var seedTop = FibaRanking.Apply(teams, FibaRanking.Seed(teams)).Take(10).Select(t => t.Nation.Code).ToList();
        int windows = 0;
        for (int y = 2026; y <= 2033; y++)
            foreach (var w in FibaCalendar.InYear(y).Where(e => e.Kind == FibaEventKind.Window && (y > 2026 || e.Start.Month == 11) && (y < 2033 || e.Start.Month == 2)))
            { Qualifying.Play(h, w, teams, s.Seed); windows++; }
        var ranked = FibaRanking.Apply(teams, h.Ranking);
        var top = ranked.Take(10).Select(t => t.Nation.Code).ToList();
        _out.WriteLine($"{windows} windows. Seed top ten: {string.Join(" ", seedTop)}");
        _out.WriteLine($"Now:            {string.Join(" ", ranked.Take(10).Select(t => $"{t.Nation.Code} {t.Points:F0}"))}");
        _out.WriteLine("Cycles: " + string.Join(", ", h.Cycles.Select(c => $"{c.Target} ({h.Windows.Count(w => w.Target == c.Target)})")));
        int kept = top.Intersect(seedTop).Count();
        Assert.InRange(kept, 5, 9); // stable, not frozen
        Assert.NotEqual(seedTop, top);
        Assert.Equal(4, h.Cycles.Count); // 2027 WC (2 windows), 2029 continentals (4), 2031 WC (4), 2033 continentals (4)
        Assert.Equal(new[] { 2, 4, 4, 4 }, h.Cycles.Select(c => h.Windows.Count(w => w.Target == c.Target)));
        Assert.DoesNotContain(ranked, t => t.Nation.Suspended && t.Rank > 0);
    }

    [Fact]
    public void A_career_plays_the_november_window_once_and_saves_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run210-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = NewCareer(path);
            var end = new DateOnly(2026, 12, 4);
            int steps = 0;
            while (s.Today < end && steps++ < 200) s.AdvanceDay();
            var w = Assert.Single(s.World.International.Windows);
            Assert.Equal("2026-11", w.Key);
            Assert.NotEmpty(s.World.International.Ranking);
            var msg = Assert.Single(s.Inbox, m => m.Title.StartsWith("FIBA window"));
            _out.WriteLine($"{msg.Date}: {msg.Title}\n{msg.Body}");
            Assert.Contains("ranking", msg.Body);

            s.SaveNow();
            SqliteConnection.ClearAllPools();
            var back = GameSession.Load(path).World.International;
            Assert.Equal(w.Games, Assert.Single(back.Windows).Games);
            Assert.Equal(s.World.International.Ranking, back.Ranking);
            Assert.Equal(s.World.International.Cycles.Single().Groups.Keys, back.Cycles.Single().Groups.Keys);
            Assert.Equal(s.World.International.LastRanking, back.LastRanking);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
