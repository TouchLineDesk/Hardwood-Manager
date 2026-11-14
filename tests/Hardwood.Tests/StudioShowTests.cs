using Hardwood.Core.Model;
using Hardwood.Core.People;
using Hardwood.Core.RealWorld;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Danny's pre-game studio show (#446, D53 B; part 1, run 45): the HSN desk before your games, its sets, and the
/// panel's picks graded at the final.
/// </summary>
public class StudioShowTests
{
    private static GameSession RealCareerOnGameDay(int days)
    {
        var s = GameSession.NewGame(31, 5, null, realWorld: true);
        for (int i = 0; i < days; i++) s.AdvanceDay();
        int guard = 0;
        while (!s.HumanPlaysToday && guard++ < 30) s.AdvanceDay();
        Assert.True(s.HumanPlaysToday);
        return s;
    }

    [Fact]
    public void A_real_game_day_has_the_whole_show_and_the_picks_are_graded_at_the_final()
    {
        var s = RealCareerOnGameDay(40);
        var (live, played) = s.BeginLiveDay();
        Assert.NotNull(live);
        var show = s.BuildStudioShow(live!.Result.Home.Team, live.Result.Away.Team);

        Assert.Equal(StudioShow.Network, "HSN");
        Assert.Contains(show.Segments, x => x.Kicker == "THE OPEN");
        Assert.Contains(show.Segments, x => x.Kicker == "KEYS TO THE GAME");
        Assert.All(show.Segments, x => Assert.NotEmpty(x.Lines));
        Assert.Contains(show.Tape, t => t.Label == "Record");
        Assert.Contains(show.Tape, t => t.Label == "Last 10");
        // A real NBA career plays the division format: the division table and what tonight means.
        Assert.NotEmpty(show.Impact);
        Assert.Equal(4, show.Impact[0].Rows.Count);
        Assert.Contains(show.Impact[0].Rows, r => r.Team == s.HumanTeam);
        Assert.NotEmpty(show.ImpactLines);
        Assert.Equal("MVP ladder", show.RaceTitle == "Rookie of the Year ladder" ? "MVP ladder" : show.RaceTitle);
        Assert.InRange(show.Race.Count, 1, 5);
        Assert.Equal(3, show.Picks.Count);
        Assert.All(show.Picks, p => Assert.True(p.Pick == show.Home || p.Pick == show.Away));
        Assert.InRange(show.HomeChance, 0.01, 0.99);
        Assert.NotEqual("", show.Venue); // a real career names the real arena
        // Every line is a full sentence with no unfilled template text.
        foreach (var line in show.Segments.SelectMany(x => x.Lines).Concat(show.ImpactLines).Concat(show.RaceLines))
        {
            Assert.DoesNotContain("{", line.Text);
            Assert.False(string.IsNullOrWhiteSpace(line.Text));
        }

        // The same game's show tells the same story twice (no reroll on reopening).
        var again = s.BuildStudioShow(show.Home, show.Away);
        Assert.Equal(show.Picks.Select(p => (p.Pick.Id, p.Margin)), again.Picks.Select(p => (p.Pick.Id, p.Margin)));

        s.LockStudioPicks(show);
        s.LockStudioPicks(show); // once per game
        Assert.Equal(3, s.Media.Studio.Picks.Count);
        s.FinishLiveDay(live, played);
        Assert.All(s.Media.Studio.Picks, p => Assert.NotNull(p.Correct));
        int homeWon = live.Result.Home.Points > live.Result.Away.Points ? show.Home.Id : show.Away.Id;
        Assert.All(s.Media.Studio.Picks, p => Assert.Equal(p.PickId == homeWon, p.Correct));
        var (w, l) = s.Media.Studio.Record("numbers", s.League.Season);
        Assert.Equal(1, w + l);
        Assert.NotEmpty(s.StudioLastNight());
    }

    [Fact]
    public void The_set_changes_with_the_night()
    {
        var a = new Team { Id = 1, City = "A", Name = "Aces", Abbreviation = "AAA", Division = "X" };
        var b = new Team { Id = 2, City = "B", Name = "Bees", Abbreviation = "BBB", Division = "X" };
        Assert.Equal(StudioSet.Regular, StudioShow.SetFor(new DateOnly(2026, 11, 17), false, false, null, false).set);
        Assert.Equal(StudioSet.OpeningNight, StudioShow.SetFor(new DateOnly(2026, 10, 21), false, false, null, true).set);
        Assert.Equal(StudioSet.Thanksgiving, StudioShow.SetFor(new DateOnly(2026, 11, 26), false, false, null, false).set);
        Assert.Equal(StudioSet.Christmas, StudioShow.SetFor(new DateOnly(2026, 12, 25), false, false, null, false).set);
        Assert.Equal(StudioSet.NewYear, StudioShow.SetFor(new DateOnly(2026, 12, 31), false, false, null, false).set);
        Assert.Equal(StudioSet.MlkDay, StudioShow.SetFor(new DateOnly(2027, 1, 18), false, false, null, false).set);
        Assert.Equal(StudioSet.International, StudioShow.SetFor(new DateOnly(2026, 11, 17), false, true, null, false).set);
        Assert.Equal(StudioSet.College, StudioShow.SetFor(new DateOnly(2026, 11, 17), true, false, null, false).set);
        var round = new Series { Round = "East First Round", Higher = a, Lower = b };
        Assert.Equal(StudioSet.Playoffs, StudioShow.SetFor(new DateOnly(2027, 4, 20), false, false, round, false).set);
        Assert.Equal(StudioSet.Finals, StudioShow.SetFor(new DateOnly(2027, 6, 5), false, false, new Series { Round = "Finals", Higher = a, Lower = b }, false).set);
        Assert.Equal(StudioSet.ThirdPlace, StudioShow.SetFor(new DateOnly(2027, 4, 22), false, false, new Series { Round = Postseason.ThirdQuarters, Higher = a, Lower = b, WinsNeeded = 2 }, false).set);
        Assert.Contains("Game 1", StudioShow.SetFor(new DateOnly(2027, 4, 20), false, false, round, false).line);
    }

    [Fact]
    public void The_cast_is_invented()
    {
        foreach (var a in StudioShow.Cast)
        {
            Assert.False(RealNames.IsReal(a.Name.Replace(" 'Showtime'", "")), a.Name);
            Assert.False(string.IsNullOrEmpty(a.Initials));
        }
        Assert.Equal("ML", StudioShow.Guard.Initials);
    }

    [Fact]
    public void Zones_follow_the_division_format()
    {
        Assert.Equal("Playoffs", GameSession.Zone(1, 1));
        Assert.Equal("Third place", GameSession.Zone(1, 3));
        Assert.Equal("Relegation", GameSession.Zone(1, 4));
        Assert.Equal("Promotion", GameSession.Zone(2, 1));
        Assert.Equal("", GameSession.Zone(5, 4));
    }

    [Fact]
    public void An_older_media_record_loads_without_picks()
    {
        var desk = System.Text.Json.JsonSerializer.Deserialize<MediaDesk>("{\"NextId\":4,\"Answered\":2}")!;
        Assert.NotNull(desk.Studio);
        Assert.Empty(desk.Studio.Picks);
        Assert.Equal((0, 0), desk.Studio.Record("big", 2026));
    }
}
