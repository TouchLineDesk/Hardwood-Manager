using Avalonia.Headless.XUnit;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Danny's #529: Schedule &amp; Results filters by competition, and the head-to-head record since the career began.</summary>
public class ScheduleFilterTests
{
    private static Team T(int id, string city) => new() { Id = id, City = city, Name = "Club", Abbreviation = city[..3].ToUpperInvariant(), Division = "" };

    private static MyGame G(int month, int day, Team opp, bool home, int? us, int? them, string group = ScheduleFilter.Regular, int year = 2026) =>
        new(new DateOnly(year, month, day), opp, home, "NBA", "Regular season", us, them, null) { Group = group };

    [Fact]
    public void Pro_and_college_games_fall_into_the_right_filters_in_order()
    {
        Assert.Equal(ScheduleFilter.Regular, ScheduleFilter.ProGroup("NBA", "Regular season", "NBA"));
        Assert.Equal(ScheduleFilter.Playoffs, ScheduleFilter.ProGroup("NBA", "Conference finals", "NBA"));
        Assert.Equal(Postseason.ThirdPlaceName, ScheduleFilter.ProGroup("NBA", "Third-place tournament: final", "NBA"));
        Assert.Equal("EuroLeague", ScheduleFilter.ProGroup("EuroLeague", "Regular season", "Liga ACB"));
        Assert.Equal(ScheduleFilter.Conference, ScheduleFilter.CollegeGroup(null, "Conference"));
        Assert.Equal(ScheduleFilter.NonConference, ScheduleFilter.CollegeGroup(null, "Regular season"));
        Assert.Equal("March Madness", ScheduleFilter.CollegeGroup("March Madness", "March Madness"));

        var a = T(1, "Boston");
        var games = new[]
        {
            G(3, 20, a, true, null, null, "March Madness", 2027), G(11, 10, a, true, 80, 70, ScheduleFilter.NonConference),
            G(11, 25, a, false, 70, 72, "Maui Invitational"), G(1, 5, a, true, 60, 50, ScheduleFilter.Conference, 2027),
            G(3, 8, a, true, 60, 50, "Big 12 tournament", 2027),
        };
        Assert.Equal(new[] { ScheduleFilter.All, ScheduleFilter.NonConference, ScheduleFilter.Conference, "Maui Invitational", "Big 12 tournament", "March Madness" },
            ScheduleFilter.For(games));
        var pro = new[] { G(4, 20, a, true, 100, 90, ScheduleFilter.Playoffs, 2027), G(4, 22, a, true, 100, 90, Postseason.ThirdPlaceName, 2027), G(10, 22, a, true, 100, 90) };
        Assert.Equal(new[] { ScheduleFilter.All, ScheduleFilter.Regular, Postseason.ThirdPlaceName, ScheduleFilter.Playoffs }, ScheduleFilter.For(pro));
        Assert.True(ScheduleFilter.Matches(pro[0], ScheduleFilter.All));
        Assert.False(ScheduleFilter.Matches(pro[0], ScheduleFilter.Regular));
    }

    [Fact]
    public void The_book_counts_from_the_careers_start_and_folds_each_season_in_when_the_schedule_changes()
    {
        var book = new HeadToHeadBook();
        var bos = T(2, "Boston"); var mia = T(3, "Miami");
        // The career's first day: nothing played, so the book starts with the career.
        var season1 = new List<MyGame> { G(10, 22, bos, true, null, null), G(11, 2, mia, false, null, null), G(12, 1, bos, false, null, null) };
        book.Sync(1, season1, new DateOnly(2026, 10, 1));
        Assert.True(book.FromCareerStart);
        Assert.Equal(new DateOnly(2026, 10, 1), book.Since);
        Assert.Equal(0, book.Of(1, 2).Games);

        season1 = new List<MyGame> { G(10, 22, bos, true, 110, 100), G(11, 2, mia, false, 90, 99), G(12, 1, bos, false, 101, 104) };
        book.Sync(1, season1, new DateOnly(2026, 12, 2));
        var vsBos = book.Of(1, 2);
        Assert.Equal((1, 1), (vsBos.Wins, vsBos.Losses));
        Assert.Equal(211, vsBos.PointsFor);
        Assert.Equal(new DateOnly(2026, 12, 1), vsBos.LastDate);
        Assert.Equal((101, 104, false), (vsBos.LastUs, vsBos.LastThem, vsBos.LastHome));
        Assert.Equal("1-1", vsBos.Line);
        // Syncing again counts nothing twice.
        book.Sync(1, season1, new DateOnly(2026, 12, 3));
        Assert.Equal(2, book.Of(1, 2).Games);

        // A new season's schedule: last season is folded in and this one counted on top.
        var season2 = new List<MyGame> { G(10, 21, bos, true, 120, 100, year: 2027), G(11, 5, mia, true, null, null, year: 2027) };
        book.Sync(1, season2, new DateOnly(2027, 10, 22));
        Assert.Equal((2, 1), (book.Of(1, 2).Wins, book.Of(1, 2).Losses));
        Assert.Equal((0, 1), (book.Of(1, 3).Wins, book.Of(1, 3).Losses));
        Assert.Equal(new DateOnly(2027, 10, 21), book.Of(1, 2).LastDate);
        Assert.Equal(new DateOnly(2026, 10, 1), book.Since);

        // A new club mid-season: its record against Boston starts at nothing; the old club's is kept.
        book.Sync(9, new List<MyGame> { G(12, 1, bos, true, 99, 98, year: 2027) }, new DateOnly(2027, 12, 2));
        Assert.Equal(1, book.Of(9, 2).Games);
        Assert.Equal(3, book.Of(1, 2).Games);

        // An older save: the book starts at the season being played, and says so.
        var old = new HeadToHeadBook();
        old.Sync(1, season1, new DateOnly(2026, 12, 2));
        Assert.False(old.FromCareerStart);
        Assert.Equal(new DateOnly(2026, 10, 22), old.Since);
        Assert.Equal(2, old.Of(1, 2).Games);
    }

    [AvaloniaFact]
    public void A_real_career_filters_its_schedule_and_keeps_the_head_to_head_through_a_save()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-h2h-{Guid.NewGuid():N}.hwsave");
        try
        {
            var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(7, 2026, GameSession.DefaultFullSim);
            var s = GameSession.NewGame(7, league.Teams.First(t => t.City == "Boston").Id, path);
            Assert.True(s.HeadToHeadBook.FromCareerStart);
            for (int i = 0; i < 40; i++) s.AdvanceDay();
            var games = s.MyGames();
            var played = games.Where(g => g.Played).ToList();
            Assert.NotEmpty(played);
            Assert.All(games, g => Assert.Equal(ScheduleFilter.Regular, g.Group));
            foreach (var opp in played.Select(g => g.Opponent).Distinct())
            {
                var r = s.HeadToHead(opp);
                Assert.Equal(played.Count(g => g.Opponent == opp && g.Won), r.Wins);
                Assert.Equal(played.Count(g => g.Opponent == opp && !g.Won), r.Losses);
            }
            Assert.StartsWith("since your career began", s.HeadToHeadSince);

            // Kept in the save.
            s.SaveNow();
            var loaded = GameSession.Load(path);
            var first = played[0].Opponent;
            var again = loaded.HeadToHead(loaded.FindTeam(first.Id)!);
            Assert.Equal(s.HeadToHead(first).Line, again.Line);
            Assert.Equal(s.HeadToHeadBook.Since, loaded.HeadToHeadBook.Since);
            Assert.True(loaded.HeadToHeadBook.FromCareerStart);

            // The page: a chip per filter with its record; the H2H column; the card for the selected opponent.
            var vm = new MainViewModel();
            vm.AttachSession(loaded);
            vm.Navigate("Schedule & Results");
            var sched = (ScheduleViewModel)vm.CurrentPage!;
            Assert.Equal(new[] { ScheduleFilter.All, ScheduleFilter.Regular }, sched.Filters.Select(f => f.Name));
            var rec = loaded.MyRecord;
            Assert.Equal($"{rec.Wins}-{rec.Losses}", sched.Filters[1].Record);
            Assert.True(sched.Filters[0].Selected);
            // The H2H column reads as of each game: the last played meeting with an opponent carries the whole record.
            var row = sched.Games.Last(g => g.Played);
            Assert.Equal(loaded.HeadToHead(row.OpponentTeam!).Line, row.HeadToHead);
            var firstMeeting = sched.Games.First(g => g.Played);
            Assert.Equal(firstMeeting.Game.Won ? "1-0" : "0-1", firstMeeting.HeadToHead);
            sched.Selected = row;
            Assert.True(sched.HasRival);
            Assert.Equal(row.OpponentTeam, sched.RivalTeam);
            Assert.Contains("since your career began", sched.RivalSince);
            Assert.NotEmpty(sched.RivalMeetings);
            sched.SetFilter(ScheduleFilter.Regular);
            Assert.True(sched.Filters[1].Selected);
            Assert.Equal(games.Count, sched.Games.Count);
            Assert.StartsWith("Regular season: ", sched.FilterSummary);
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
