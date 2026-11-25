using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.College;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;
using Xunit;

namespace Hardwood.Tests;

/// <summary>The college Top 25 (Danny, #528): released every Monday (K35, run 193), the rank at tip-off kept on each result, and the
/// rank travelling with the team (logos, links, schedules).</summary>
public class CollegePollTests
{
    private static GameSession CollegeCareer(ulong seed, string? path = null)
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, 2026, GameSession.DefaultFullSim);
        var cw = world.College!;
        var id = cw.Programs.OrderByDescending(p => cw.Prestige[p.Id]).First().Id;
        return GameSession.NewGame(seed, id, path);
    }

    [Fact]
    public void The_poll_is_re_ranked_after_every_game_and_each_result_keeps_the_ranks_at_tip_off()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-poll-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = CollegeCareer(11, path);
            var cw = s.World.College!;
            // Preseason: 25 teams on the rating, no arrows yet.
            var pre = cw.Poll();
            Assert.Equal(Enumerable.Range(1, 25), pre.Select(e => e.Rank));
            Assert.All(pre, e => Assert.Equal("–", e.MoveText));

            for (int i = 0; i < 24; i++) s.AdvanceDay();
            // K35 (run 193): on to the first Monday release after the preseason poll.
            for (int i = 0; i < 40 && cw.Season.PollDay?.DayOfWeek != DayOfWeek.Monday; i++) s.AdvanceDay();
            var monday = cw.Season.PollDay;
            for (int i = 0; i < 5 && !cw.Season.Games.Any(g => g.Played && g.Date >= monday); i++) s.AdvanceDay();
            Assert.Equal(monday, cw.Season.PollDay);
            var poll = cw.Poll();
            Assert.Equal(25, poll.Count);
            Assert.Equal(25, poll.Select(e => e.Team.Id).Distinct().Count());
            // K35 (run 193): the poll on show is the Monday release (ranked then on the rating and the record), and Rank() agrees with the grid.
            Assert.Equal(poll.Select(e => e.Team.Id), cw.Season.PollRelease.Take(25));
            Assert.Equal(DayOfWeek.Monday, cw.Season.PollDay!.Value.DayOfWeek);
            Assert.All(poll, e => Assert.Equal(e.Rank, cw.Rank(e.Team.Id)));
            Assert.Null(cw.Rank(cw.Programs.First(t => !poll.Any(e => e.Team == t)).Id));
            Assert.Null(cw.Rank(s.League.Teams[0].Id));

            // The arrows compare with last week's release.
            Assert.NotNull(cw.Season.PollDay);
            Assert.Equal(25, cw.Season.PollBefore.Count);
            Assert.All(poll, e => Assert.Equal(cw.Season.PollBefore.TryGetValue(e.Team.Id, out var b) ? b : null, e.Before));
            Assert.Equal(cw.Season.PollBefore.Keys.Count(id => cw.Rank(id) == null), cw.DroppedOut().Count);
            var moved = new PollEntry(3, poll[0].Team, 5, 1, 6);
            Assert.Equal("▲3", moved.MoveText);
            Assert.Equal("▼2", new PollEntry(8, poll[0].Team, 5, 1, 6).MoveText);
            Assert.Equal("NEW", new PollEntry(8, poll[0].Team, 5, 1, null).MoveText);

            // Every game played since this week's release tipped off against it.
            var day = cw.Season.Games.Where(g => g.Played && g.Date >= cw.Season.PollDay).ToList();
            Assert.NotEmpty(day);
            foreach (var g in day.Where(g => g.HomeId != s.HumanTeam.Id && g.AwayId != s.HumanTeam.Id))
            {
                Assert.True(cw.Rank(g.HomeId) == g.HomeRank, $"{g.Date} {g.Stage} {g.Event} r{g.Round} {g.HomeId} v {g.AwayId} rank {g.HomeRank} now {cw.Rank(g.HomeId)}");
                Assert.Equal(cw.Rank(g.AwayId), g.AwayRank);
            }
            // Ranked teams played, so some results carry a rank.
            Assert.Contains(cw.Season.Games, g => g.Played && (g.HomeRank != null || g.AwayRank != null));

            // Your schedule: a result shows the opponent's rank at tip-off, a game to come its rank now.
            var mine = s.MyGames();
            Assert.Contains(mine, g => g.Played);
            foreach (var g in mine)
            {
                var cg = cw.GamesOf(s.HumanTeam.Id).First(x => x.Date == g.Date && (x.HomeId == g.Opponent.Id || x.AwayId == g.Opponent.Id));
                if (g.Played) Assert.Equal(cg.HomeId == g.Opponent.Id ? cg.HomeRank : cg.AwayRank, g.OpponentRank);
                else Assert.Equal(cw.Rank(g.Opponent.Id), g.OpponentRank);
            }
            Assert.Equal("#4 Duke", CollegeWorld.WithRank(4, "Duke"));
            Assert.Equal("Duke", CollegeWorld.WithRank(null, "Duke"));

            // The next Monday: a new release, and the arrows compare with the one before it.
            var before = cw.Season.PollDay;
            var lastWeek = cw.PollRanks();
            int guard = 0;
            while (cw.Season.PollDay == before && guard++ < 10) s.AdvanceDay();
            Assert.NotEqual(before, cw.Season.PollDay);
            Assert.Equal(before!.Value.AddDays(7), cw.Season.PollDay);
            Assert.Equal(lastWeek, cw.Season.PollBefore);

            // It all saves and loads.
            s.SaveNow();
            var back = GameSession.Load(path);
            var bw = back.World.College!;
            Assert.Equal(cw.Poll().Select(e => (e.Rank, e.Team.Id, e.Before)), bw.Poll().Select(e => (e.Rank, e.Team.Id, e.Before)));
            Assert.Equal(cw.Season.PollDay, bw.Season.PollDay);
            Assert.Equal(cw.Season.Games.Select(g => (g.HomeRank, g.AwayRank)), bw.Season.Games.Select(g => (g.HomeRank, g.AwayRank)));
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void A_season_from_an_older_save_has_no_arrows_and_no_tip_off_ranks()
    {
        var season = System.Text.Json.JsonSerializer.Deserialize<CollegeSeason>("{\"Year\":2026,\"Games\":[{\"HomeId\":90001,\"AwayId\":90002,\"HomePts\":70,\"AwayPts\":60}]}")!;
        Assert.Empty(season.PollBefore);
        Assert.Null(season.PollDay);
        Assert.Null(season.Games[0].HomeRank);
        Assert.Null(season.Games[0].AwayRank);
    }

    [AvaloniaFact]
    public void The_rank_travels_with_the_team_on_links_and_logos()
    {
        var s = CollegeCareer(12);
        var cw = s.World.College!;
        var old = CollegeRank.Of;
        try
        {
            CollegeRank.Of = t => cw.Rank(t.Id);
            var top = cw.Poll()[0].Team;
            var unranked = cw.Programs.First(t => cw.Rank(t.Id) == null);
            Assert.Equal($"#1 {top.City}", CollegeRank.Prefix(top, top.City));
            Assert.Equal(unranked.City, CollegeRank.Prefix(unranked, unranked.City));
            Assert.Equal($"#1 {top.FullName}", new TeamLink { Team = top }.Content);
            Assert.Equal(top.FullName, new TeamLink { Team = top, ShowRank = false }.Content);
            Assert.Equal(s.League.Teams[0].FullName, new TeamLink { Team = s.League.Teams[0] }.Content);

            // The logo draws its badge at 36 px and up (a 24 px table logo has the rank in its row instead).
            var window = new Window { Width = 300, Height = 120 };
            var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
            panel.Children.Add(new TeamLogo { Team = top, Width = 60, Height = 60 });
            panel.Children.Add(new TeamLogo { Team = top, Width = 24, Height = 24 });
            panel.Children.Add(new TeamLogo { Team = unranked, Width = 60, Height = 60 });
            window.Content = panel;
            window.Show();
            CollegeRank.RaiseChanged();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } dir)
            {
                Directory.CreateDirectory(dir);
                frame!.Save(Path.Combine(dir, "college-rank-logos.png"));
            }
            window.Close();
        }
        finally { CollegeRank.Of = old; }
    }
}
