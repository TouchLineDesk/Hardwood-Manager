using Hardwood.Core.Jobs;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 54: review #14's bugs #3 (box scores of other clubs' games), #4 (the playoff games after a reload) and
/// #16 (the board's season review).</summary>
public class Run54FixTests
{
    private static GameSession NewCareer(string abbr, out string path)
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        path = Path.Combine(Path.GetTempPath(), $"hw-run54-{Guid.NewGuid():N}.hwsave");
        return GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == abbr).Id, path);
    }

    [Fact]
    public void Every_games_box_score_survives_a_save_and_reload()
    {
        var s = NewCareer("BOS", out var path);
        try
        {
            while (s.Phase != SessionPhase.RegularSeason) s.AdvanceDay();
            for (int i = 0; i < 12; i++) s.AdvanceDay();
            var others = s.Schedule.Where(g => s.Results.ContainsKey(g.Index) && g.Home != s.HumanTeam && g.Away != s.HumanTeam).ToList();
            Assert.NotEmpty(others);
            s.SaveNow();
            // Saved twice: the second save keeps the boxes it wrote the first time.
            var loaded = GameSession.Load(path);
            loaded.SaveNow();
            loaded = GameSession.Load(path);
            foreach (var g in others)
            {
                var r = loaded.Results[g.Index];
                Assert.True(r.Home.Lines.Count > 0 && r.Away.Lines.Count > 0, $"game {g.Index} lost its box score");
                Assert.Equal(s.Results[g.Index].Home.Points, r.Home.Points);
                Assert.Equal(s.Results[g.Index].Away.Points, r.Away.Points);
            }
            // Your own games keep theirs too, and play on after the load.
            Assert.All(loaded.HumanResults, x => Assert.True(x.result.Home.Lines.Count > 0));
            for (int i = 0; i < 3; i++) loaded.AdvanceDay();
            loaded.SaveNow();
            var again = GameSession.Load(path);
            Assert.All(again.Schedule.Where(g => again.Results.ContainsKey(g.Index)), g => Assert.True(again.Results[g.Index].Home.Lines.Count > 0));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Playoff_games_survive_a_reload_in_the_playoffs_and_the_summer()
    {
        var s = NewCareer("OKC", out var path);
        try
        {
            int guard = 0;
            while (s.Phase != SessionPhase.Postseason && guard++ < 400) s.AdvanceDay();
            Assert.Equal(SessionPhase.Postseason, s.Phase);
            // A week into the playoffs: a reload keeps the games already played.
            for (int i = 0; i < 8; i++) s.AdvanceDay();
            s.SaveNow();
            int before = s.MyGames().Count, playedBefore = s.Postseason!.Series.Sum(x => x.Games.Count);
            Assert.True(playedBefore > 0);
            var mid = GameSession.Load(path);
            Assert.Equal(before, mid.MyGames().Count);
            Assert.Equal(playedBefore, mid.Postseason!.Series.Sum(x => x.Games.Count));

            // On to the summer, then reload (twice: the second save is made from a loaded summer).
            guard = 0;
            while (s.Phase == SessionPhase.Postseason && guard++ < 120) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            s.SaveNow();
            var games = s.MyGames();
            int playoffGames = games.Count(g => g.Group != ScheduleFilter.Regular);
            Assert.True(playoffGames > 0, "OKC should play a postseason series");
            var summer = GameSession.Load(path);
            summer.SaveNow();
            summer = GameSession.Load(path);
            var after = summer.MyGames();
            Assert.Equal(games.Count, after.Count);
            Assert.Equal(playoffGames, after.Count(g => g.Group != ScheduleFilter.Regular));
            // The playoff games open their box scores, with the right scores.
            var first = games.First(g => g.Group != ScheduleFilter.Regular);
            var back = after.First(g => g.Group != ScheduleFilter.Regular);
            Assert.Equal((first.Date, first.Us, first.Them), (back.Date, back.Us, back.Them));
            Assert.True(back.Result!.Home.Lines.Count > 0);
            Assert.Equal(s.PlayoffSeries.Count, summer.PlayoffSeries.Count);
            // The relegation check runs on a finished season without trouble.
            _ = summer.GoingDown();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Season_review_reads_what_happened_against_the_goal()
    {
        var s = NewCareer("BOS", out var path);
        try
        {
            var ctx = s.ContextOf(s.HumanTeam);
            var stats = new Dictionary<int, PlayerSeasonStats>();
            string Review(Expectations e, SeasonGoal got, int w, int l, bool down = false)
            {
                var job = new JobState { Current = e, Confidence = 50, HiredSeason = 0 };
                return Board.SeasonReview(job, new Coach(), ctx, s.HumanTeam, got, w, l, stats, s.Today, s.League.Season, relegated: down).body;
            }

            // A promoted club told to stay up that goes straight back down is not "satisfied".
            var down = Review(new Expectations { Survive = true, Goal = SeasonGoal.Develop, TargetWins = 33 }, SeasonGoal.Develop, 25, 57, down: true);
            Assert.Contains("Result: relegated", down);
            Assert.DoesNotContain("satisfied", down);
            Assert.DoesNotContain("delighted", down);

            // Staying up in third place is the goal met, said as such.
            var up = Review(new Expectations { Survive = true, Goal = SeasonGoal.Develop, TargetWins = 33 }, SeasonGoal.PlayIn, 32, 50);
            Assert.Contains("Goal: stay up. Result: stayed up", up);
            Assert.Contains("satisfied", up);

            // The result is what happened, not a goal's wording.
            var missed = Review(new Expectations { Goal = SeasonGoal.SecondRound, TargetWins = 48 }, SeasonGoal.Develop, 38, 44);
            Assert.Contains("Result: missed the playoffs", missed);
            Assert.DoesNotContain("Result: develop", missed);
            Assert.DoesNotContain("satisfied", missed);

            // A relegation is never a goal met, whatever the goal was.
            var rebuild = Review(new Expectations { Goal = SeasonGoal.Develop, TargetWins = 30 }, SeasonGoal.Develop, 28, 54, down: true);
            Assert.DoesNotContain("satisfied", rebuild);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

/// <summary>Review #14 bug #2: the college career's second season crashed Schedule &amp; Results with "Infinite layout loop
/// detected": its side panel is 300 wide beside the table but only as wide as its content underneath, and with nothing in
/// it (the first day of a season: no scores yesterday, no head-to-head) the stacked width said "fits", so it flipped back.</summary>
public class ReflowLoopTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_side_panel_that_narrows_when_stacked_does_not_flip_for_ever()
    {
        var left = new Avalonia.Controls.Border { Width = 600 };
        var side = new Avalonia.Controls.StackPanel { Width = 300, Margin = new Avalonia.Thickness(16, 0, 0, 0) };
        side.Children.Add(new Avalonia.Controls.Border { Width = 230, Height = 20 });
        var grid = new Avalonia.Controls.Grid { ColumnDefinitions = new("*,Auto"), RowDefinitions = new("*,Auto") };
        Avalonia.Controls.Grid.SetColumn(side, 1);
        grid.Children.Add(left); grid.Children.Add(side);
        int flips = 0;
        var reflow = Hardwood.Desktop.Controls.Reflow.Watch(grid, left, side, leftNeed: () => 600);
        reflow.Stacking += stacked => { flips++; side.Width = stacked ? double.NaN : 300; };
        // 600 + 316 + 24 = 940 doesn't fit in 920; stacked, the panel's content (230 + 16) would have fitted.
        var window = new Avalonia.Controls.Window { Width = 920, Height = 600, Content = grid };
        window.Show();
        for (int i = 0; i < 20; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        Assert.True(reflow.Stacked);
        Assert.Equal(1, flips);
        // Wide enough again: it goes back side by side, once.
        window.Width = 1100;
        for (int i = 0; i < 20; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        Assert.False(reflow.Stacked);
        Assert.Equal(2, flips);
        window.Close();
    }
}
