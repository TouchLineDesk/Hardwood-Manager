using Hardwood.Core;
using Hardwood.Core.International;
using Microsoft.Data.Sqlite;
using Hardwood.Core.Season;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>International plan I4 (run 211): the 2028 Olympics (the host, the World Cup's best per zone, four qualifying-tournament
/// winners) and the 2029 continental championships (fields from the continental qualifying tables), each on its own stream.</summary>
public class Run211OlympicsTests
{
    private readonly ITestOutputHelper _out;
    public Run211OlympicsTests(ITestOutputHelper output) => _out = output;

    private static GameSession NewCareer(string? path = null)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("Virginia Tech"));
        return GameSession.NewGame(31, me.Id, path);
    }

    [Fact]
    public void A_bracket_is_seeded_and_avoids_group_rematches()
    {
        Assert.Equal(new[] { 1, 8, 4, 5, 2, 7, 3, 6 }, Tournament.SeedOrder(8));
        Assert.Equal(new[] { 1, 4, 2, 3 }, Tournament.SeedOrder(4));
        Assert.Equal(16, Tournament.SeedOrder(16).Distinct().Count());
        var slots = new List<string> { "A1", "A2", "B1", "C2", "C1", "B2", "D1", "D2" };
        var groups = slots.ToDictionary(c => c, c => (int)c[0]);
        Tournament.AvoidRematches(slots, groups);
        for (int p = 0; p < 4; p++) Assert.NotEqual(groups[slots[p * 2]], groups[slots[p * 2 + 1]]);
        Assert.Equal(new[] { 4, 8, 8, 16 }, new[] { 7, 12, 16, 24 }.Select(Tournament.KnockoutFor));
    }

    [Fact]
    public void The_2028_olympics_take_the_host_the_world_cup_places_and_four_qualifiers()
    {
        var s = NewCareer();
        var before = Run198ReloadRolloverTests.Print(s);
        var cup = WorldCup.Play(s, 2027)!;
        s.World.International.WorldCups.Add(cup);
        var r = Olympics.Play(s, 2028)!;
        var again = Olympics.Play(s, 2028)!;
        Assert.Equal(before, Run198ReloadRolloverTests.Print(s)); // nothing shared moved
        Assert.Equal(r.Olympics.Games, again.Olympics.Games);
        Assert.Null(Olympics.Play(s, 2029));

        var o = r.Olympics;
        foreach (var q in r.Qualifiers) _out.WriteLine($"{q.Name}: {string.Join(" ", q.Teams.Select(t => t.Code))} → {q.Gold}");
        for (int i = 0; i < o.Groups.Count; i++)
            _out.WriteLine($"{WorldCup.GroupName(i)}: " + string.Join(", ", WorldCup.Table(o, o.Groups[i], g => g.Round == WorldCup.GroupName(i))));
        foreach (var g in o.Games.Where(g => !g.Round.StartsWith("Group "))) _out.WriteLine($"{g.Round,-14} {g.Day}: {g.Home} {g.HomePts}-{g.AwayPts} {g.Away}");
        _out.WriteLine($"Gold {o.Gold}, silver {o.Silver}, bronze {o.Bronze}; MVP {o.Mvp?.Name}; {o.Games.Average(g => g.HomePts + g.AwayPts) / 2.0:F1} a side");

        Assert.Equal(12, o.Teams.Count);
        Assert.Equal(12, o.Teams.Select(t => t.Code).Distinct().Count());
        Assert.Contains("USA", o.Teams.Select(t => t.Code)); // the host
        Assert.Equal(3, o.Groups.Count);
        Assert.All(o.Groups, g => Assert.Equal(4, g.Count));
        Assert.Equal(18 + 4 + 2 + 1 + 1, o.Games.Count);
        Assert.Equal(4, new[] { o.Gold, o.Silver, o.Bronze, o.Fourth }.Distinct().Count());
        Assert.Equal("Jul 30", o.Final!.Day);
        Assert.Equal("Jul 15", o.Games[0].Day);
        Assert.InRange(o.Games.Average(g => g.HomePts + g.AwayPts) / 2.0, 73, 87); // H219 (run 212): 26 games, so a little wider
        Assert.NotNull(o.Mvp);

        // The World Cup's best per zone (after the host): Europe 2, the Americas 2, Africa 1, Asia-Pacific 2.
        var points = FibaRanking.Current(s.World.International, NationalTeams.Build(WorldCup.Entries(s, new DateOnly(2028, 7, 4)), new DateOnly(2028, 7, 4)));
        var finish = Tournament.FinishOrder(cup, points);
        foreach (var (zone, places) in Olympics.WorldCupPlaces)
        {
            string zn = NationalTeams.ZoneName(zone);
            var best = finish.Where(c => cup.Teams.First(t => t.Code == c).Zone == zn && c != "USA").Take(places);
            Assert.All(best, c => Assert.Contains(c, o.Teams.Select(t => t.Code)));
        }
        // Four qualifying tournaments of six, quick results, no bronze game; each winner is at the Olympics.
        Assert.Equal(4, r.Qualifiers.Count);
        Assert.All(r.Qualifiers, q => Assert.Equal(6, q.Teams.Count));
        Assert.All(r.Qualifiers, q => Assert.Equal(6 + 2 + 1, q.Games.Count));
        Assert.All(r.Qualifiers, q => Assert.Contains(q.Gold, o.Teams.Select(t => t.Code)));
        Assert.Empty(r.Qualifiers.SelectMany(q => q.Teams).Select(t => t.Code).Intersect(o.Teams.Select(t => t.Code)).Except(r.Qualifiers.Select(q => q.Gold)));
        Assert.Equal(24, r.Qualifiers.SelectMany(q => q.Teams).Select(t => t.Code).Distinct().Count());
    }

    [Fact]
    public void The_2029_continental_championships_play_each_zone_from_its_qualifiers()
    {
        var s = NewCareer();
        var before = Run198ReloadRolloverTests.Print(s);
        var played = Continentals.Play(s, 2029);
        Assert.Equal(before, Run198ReloadRolloverTests.Print(s));
        Assert.Empty(Continentals.Play(s, 2028));
        Assert.Equal(new[] { "FIBA Asia Cup 2029", "FIBA AfroBasket 2029", "FIBA AmeriCup 2029", "EuroBasket 2029" }, played.Select(c => c.Name));
        var ranked = NationalTeams.Ranked(s);
        foreach (var c in played)
        {
            _out.WriteLine($"{c.Name}: {c.Teams.Count} teams, {c.Games.Count} games; gold {c.Gold}, silver {c.Silver}, bronze {c.Bronze}; "
                           + $"{c.Games.Average(g => g.HomePts + g.AwayPts) / 2.0:F1} a side; MVP {c.Mvp?.Name}");
            var spec = Continentals.Events.First(e => c.Name.StartsWith(e.Prefix));
            int zoneTeams = ranked.Count(t => t.Rank > 0 && t.Nation.Zone == spec.Zone);
            Assert.Equal(Math.Min(spec.Size, zoneTeams), c.Teams.Count);
            Assert.All(c.Teams, t => Assert.Equal(NationalTeams.ZoneName(spec.Zone), t.Zone));
            Assert.Equal(4, new[] { c.Gold, c.Silver, c.Bronze, c.Fourth }.Distinct().Count());
            Assert.Equal(c.Teams.Select(t => t.Code).OrderBy(x => x), c.Groups.SelectMany(g => g).OrderBy(x => x));
            Assert.Equal(c.Dates.Split(" – ")[1][..6].TrimEnd(','), c.Final!.Day);
            Assert.DoesNotContain(c.Games, g => g.HomePts == g.AwayPts);
            int ko = Tournament.KnockoutFor(c.Teams.Count);
            Assert.Equal(ko - 1 + 1, c.Games.Count(g => !g.Round.StartsWith("Group ")));
        }
        var euro = played.Single(c => c.Name.StartsWith("EuroBasket"));
        Assert.Equal(8, euro.Games.Count(g => g.Round == Tournament.RoundOf16)); // seed 31 has 21 European teams
        Assert.Equal(played.Select(c => c.Games), Continentals.Play(s, 2029).Select(c => c.Games)); // same seed, same results
    }

    [Fact]
    public void The_summer_jump_plays_them_once_and_a_save_keeps_them()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run211-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = NewCareer(path);
            var h = s.World.International;
            s.PlayInternationalOverSummer(new DateOnly(2027, 5, 8), new DateOnly(2027, 10, 14));
            s.PlayInternationalOverSummer(new DateOnly(2028, 5, 8), new DateOnly(2028, 10, 14));
            s.PlayInternationalOverSummer(new DateOnly(2028, 5, 8), new DateOnly(2028, 10, 14)); // once only
            s.PlayInternationalOverSummer(new DateOnly(2029, 5, 8), new DateOnly(2029, 10, 14));
            Assert.Single(h.WorldCups);
            Assert.Single(h.Olympics);
            Assert.Equal(4, h.OlympicQualifiers.Count);
            Assert.Equal(4, h.Continentals.Count);
            Assert.Single(s.Inbox, m => m.Title.StartsWith("Olympics 2028: "));
            Assert.Single(s.Inbox, m => m.Title.StartsWith("Continental championships 2029: "));
            Assert.NotEmpty(h.LastRanking);
            foreach (var m in s.Inbox.Where(m => m.Title.StartsWith("Olympics") || m.Title.StartsWith("Continental"))) _out.WriteLine($"{m.Title}\n{m.Body}\n");
            Assert.Equal("Olympics 2028", h.Olympics[0].Short);
            Assert.Equal("EuroBasket 2029", h.Continentals[^1].Short);

            s.SaveNow();
            SqliteConnection.ClearAllPools();
            var back = GameSession.Load(path).World.International;
            Assert.Equal(h.Olympics[0].Games, back.Olympics[0].Games);
            Assert.Equal(h.Olympics[0].Gold, back.Olympics[0].Gold);
            Assert.Equal(h.OlympicQualifiers.Select(q => q.Gold), back.OlympicQualifiers.Select(q => q.Gold));
            Assert.Equal(h.Continentals.Select(c => (c.Name, c.Kind, c.Zone, c.Gold)), back.Continentals.Select(c => (c.Name, c.Kind, c.Zone, c.Gold)));
            Assert.Equal(h.Ranking, back.Ranking);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
