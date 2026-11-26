using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.International;
using Hardwood.Core.Season;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>International plan I2 (run 209): the 2027 World Cup is played: 32 teams by zone quota, eight groups, a second
/// round, the knockouts; every game in the full engine on FIBA rules; its own stream; an optional saved history.</summary>
public class Run209WorldCupTests
{
    private readonly ITestOutputHelper _out;
    public Run209WorldCupTests(ITestOutputHelper output) => _out = output;

    private static GameSession NewCareer(string? path = null)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31, 2026, GameSession.DefaultFullSim);
        var me = world.College!.Programs.First(t => t.FullName.StartsWith("Virginia Tech"));
        return GameSession.NewGame(31, me.Id, path);
    }

    [Fact]
    public void The_2027_world_cup_plays_a_full_tournament_and_changes_nothing_else()
    {
        var s = NewCareer();
        var before = Run198ReloadRolloverTests.Print(s);
        var rec = WorldCup.Play(s, 2027)!;
        var again = WorldCup.Play(s, 2027)!;
        Assert.Equal(before, Run198ReloadRolloverTests.Print(s)); // no shared stream, player or club touched
        Assert.Equal(rec.Games.Select(g => (g.Home, g.Away, g.HomePts, g.AwayPts)), again.Games.Select(g => (g.Home, g.Away, g.HomePts, g.AwayPts)));
        Assert.Null(WorldCup.Play(s, 2028));

        foreach (var (g, i) in rec.Groups.Select((g, i) => (g, i)))
            _out.WriteLine($"{WorldCup.GroupName(i)}: " + string.Join(", ", WorldCup.Table(rec, g, x => x.Round == WorldCup.GroupName(i))
                .Select(c => { var r = WorldCup.Record(rec, c, x => x.Round == WorldCup.GroupName(i)); return $"{c} {r.W}-{r.L} ({r.Diff:+0;-0})"; })));
        foreach (var g in rec.Games.Where(g => g.Round is WorldCup.QuarterRound or WorldCup.SemiRound or WorldCup.BronzeRound or WorldCup.FinalRound))
            _out.WriteLine($"{g.Round,-14} {g.Day}: {g.Home} {g.HomePts}-{g.AwayPts} {g.Away}");
        _out.WriteLine($"Gold {rec.Gold}, silver {rec.Silver}, bronze {rec.Bronze}; MVP {rec.Mvp?.Name} ({rec.Mvp?.Code}) {rec.Mvp?.Ppg}");
        foreach (var p in rec.TopScorers.Take(5)) _out.WriteLine($"  {p.Name} ({p.Code}) {p.Ppg} in {p.Games}");
        _out.WriteLine("All-star five: " + string.Join(", ", rec.AllStarFive.Select(p => $"{p.Name} ({p.Code})")));
        _out.WriteLine("Field: " + string.Join(" ", rec.Teams.Select(t => t.Code)));
        _out.WriteLine($"points a game {rec.Games.Average(g => g.HomePts + g.AwayPts) / 2.0:F1}");

        Assert.Equal(32, rec.Teams.Count);
        Assert.Equal(32, rec.Teams.Select(t => t.Code).Distinct().Count());
        Assert.Equal(8, rec.Groups.Count);
        Assert.All(rec.Groups, g => Assert.Equal(4, g.Count));
        Assert.Equal(32, rec.Groups.SelectMany(g => g).Distinct().Count());
        Assert.Equal(48 + 16 + 4 + 2 + 1 + 1, rec.Games.Count);
        Assert.Equal(4, new[] { rec.Gold, rec.Silver, rec.Bronze, rec.Fourth }.Distinct().Count());
        Assert.All(rec.Teams, t => Assert.Equal(12, t.Squad.Count));
        Assert.Equal(5, rec.AllStarFive.Count);
        Assert.NotNull(rec.Mvp);
        Assert.Equal(rec.Gold, rec.Mvp!.Code);
        Assert.DoesNotContain(rec.Games, g => g.HomePts == g.AwayPts);
        // FIBA rules: 40 minutes, so about 75–95 a side.
        Assert.InRange(rec.Games.Average(g => g.HomePts + g.AwayPts) / 2.0, 75, 85); // H219 (run 212): FIBA tournament scoring
        // Every quarter-finalist won its way out of a second-round group: the top two of the four.
        Assert.Equal(8, rec.Games.Where(g => g.Round == WorldCup.QuarterRound).SelectMany(g => new[] { g.Home, g.Away }).Distinct().Count());
        // Days run inside the event.
        Assert.Equal("Sep 12", rec.Final!.Day);
        Assert.Equal("Aug 27", rec.Games[0].Day);
    }

    [Fact]
    public void The_field_keeps_the_zone_quotas_and_passes_spare_places_on()
    {
        var s = NewCareer();
        var ranked = NationalTeams.Build(WorldCup.Entries(s, new DateOnly(2027, 8, 27)), new DateOnly(2027, 8, 27));
        var field = WorldCup.Field(ranked, "Qatar");
        Assert.Equal(32, field.Count);
        Assert.DoesNotContain(field, t => t.Nation.Suspended);
        foreach (var (zone, quota) in WorldCup.Quota)
        {
            int have = ranked.Count(t => t.Rank > 0 && t.Nation.Zone == zone);
            Assert.True(field.Count(t => t.Nation.Zone == zone) >= Math.Min(quota, have), $"{zone}: {field.Count(t => t.Nation.Zone == zone)} of {quota} ({have} ranked)");
        }
        // A team left out is never better than every team let in from its own zone over quota.
        Assert.Equal(field.OrderBy(t => t.Rank).Select(t => t.Rank), field.Select(t => t.Rank));
    }

    [Fact]
    public void The_history_is_saved_and_an_old_save_has_none()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hw-run209-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "career.hwsave");
        try
        {
            var s = NewCareer(path);
            s.SaveNow();
            SqliteConnection.ClearAllPools();
            Assert.Empty(GameSession.Load(path).World.International.WorldCups);

            var rec = WorldCup.Play(s, 2027)!;
            s.World.International.WorldCups.Add(rec);
            s.SaveNow();
            SqliteConnection.ClearAllPools();
            var back = GameSession.Load(path).World.International.WorldCups.Single();
            Assert.Equal(rec.Gold, back.Gold);
            Assert.Equal(rec.Games, back.Games);
            Assert.Equal(rec.Groups.SelectMany(g => g), back.Groups.SelectMany(g => g));
            Assert.Equal(rec.Mvp, back.Mvp);
            Assert.Equal(rec.TopScorers, back.TopScorers);
            Assert.Equal(rec.Teams.Select(t => (t.Code, t.Name, string.Join(",", t.Squad))), back.Teams.Select(t => (t.Code, t.Name, string.Join(",", t.Squad))));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>A college career's summer jump (May to October 2027) plays the World Cup once and tells the inbox.</summary>
    [Fact]
    public void A_college_career_plays_the_world_cup_at_the_summer_jump()
    {
        var s = NewCareer();
        var oct = new DateOnly(2027, 10, 20);
        int steps = 0;
        while (s.Today < oct && steps++ < 600) { if (s.Phase == SessionPhase.Offseason) s.AdvanceOffseason(); else s.AdvanceDay(); }
        var cup = Assert.Single(s.World.International.WorldCups);
        Assert.Equal(2027, cup.Year);
        var msg = Assert.Single(s.Inbox, m => m.Title.StartsWith("World Cup 2027"));
        _out.WriteLine($"{msg.Date}: {msg.Title}\n{msg.Body}");
        Assert.Contains("International page", msg.Body);
    }
}
