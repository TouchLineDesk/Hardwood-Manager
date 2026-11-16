using Avalonia.Headless;
using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Jobs;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 65, H8: the bug finder's "lost" G League box score was a job move. Its gleague career accepted an
/// offer at the season review and became Memphis's coach, whose games had been played without him. Those games keep
/// their score; the schedule now says why there's no box score, and every game you coached keeps its box score,
/// across a reload.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run65BoxTests
{
    private static (GameSession s, string path) GleagueCareer()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        int teamId = world[UsaPyramid.Tier2].Clubs.OrderBy(t => QuickSim.Strength(t)).ElementAt(15).Id;
        var path = Path.Combine(Path.GetTempPath(), $"hw-run65-{Guid.NewGuid():N}.hwsave");
        return (GameSession.NewGame(31, teamId, path), path);
    }

    private static void PlayUntil(GameSession s, Func<bool> done, int max = 120)
    {
        for (int i = 0; i < max && !done(); i++) s.AdvanceDay();
    }

    [Fact]
    public void Games_from_before_a_job_move_keep_the_score_and_say_why_there_is_no_box()
    {
        var (s, path) = GleagueCareer();
        try
        {
            PlayUntil(s, () => s.MyGames().Count(g => g.Played) >= 8);
            var mine = s.MyGames().Where(g => g.Played).ToList();
            Assert.All(mine, g => { Assert.NotNull(g.Result); Assert.False(g.NotCoached); });

            // Take another G League club's job while in work, as an accepted offer does.
            var other = s.World[UsaPyramid.Tier2].Clubs.First(t => t != s.HumanTeam && s.World.FixturesOf(t.Id).Count(x => x.fixture.Played) >= 5);
            s.Job.Vacancies.Add(new Vacancy { TeamId = other.Id, Opened = s.Today, Status = "offered", DecideOn = s.Today.AddDays(7) });
            Assert.Null(s.AcceptOffer(other.Id));
            Assert.Equal(other, s.HumanTeam);
            int before = s.MyGames().Count(g => g.Played);
            Assert.True(before >= 5);
            Assert.All(s.MyGames().Where(g => g.Played), g => { Assert.True(g.NotCoached); Assert.Null(g.Result); Assert.NotNull(g.Us); });

            // The new club's next games are yours: they keep their box scores, through a reload.
            PlayUntil(s, () => s.MyGames().Count(g => g.Played) >= before + 3);
            var loaded = GameSession.Load(path);
            foreach (var x in new[] { s, loaded })
            {
                var played = x.MyGames().Where(g => g.Played).ToList();
                Assert.Equal(before, played.Count(g => g.NotCoached));
                Assert.All(played.Where(g => !g.NotCoached), g => Assert.True(g.Result is { } r && r.Home.Lines.Count > 0));
                Assert.True(played.Count(g => !g.NotCoached) >= 3);
            }
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_career_never_marks_its_own_games_as_before_you_arrived()
    {
        var (s, path) = GleagueCareer();
        try
        {
            PlayUntil(s, () => s.MyGames().Count(g => g.Played) >= 4);
            s.SaveNow();
            var loaded = GameSession.Load(path);
            Assert.All(loaded.MyGames().Where(g => g.Played), g => { Assert.False(g.NotCoached); Assert.NotNull(g.Result); });
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Games_played_while_you_are_out_of_work_are_marked_too()
    {
        var (s, path) = GleagueCareer();
        try
        {
            PlayUntil(s, () => s.MyGames().Count(g => g.Played) >= 4);
            int coached = s.MyGames().Count(g => g.Played);
            Assert.Null(s.Resign());
            PlayUntil(s, () => s.MyGames().Count(g => g.Played) >= coached + 3);
            var played = s.MyGames().Where(g => g.Played).ToList();
            Assert.Equal(coached, played.Count(g => !g.NotCoached && g.Result != null));
            Assert.All(played.Skip(coached), g => { Assert.True(g.NotCoached); Assert.Null(g.Result); });
        }
        finally { File.Delete(path); }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_schedule_says_a_game_was_before_you_took_over()
    {
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        var (s, path) = GleagueCareer();
        try
        {
            PlayUntil(s, () => s.MyGames().Count(g => g.Played) >= 3);
            var other = s.World[UsaPyramid.Tier2].Clubs.First(t => t != s.HumanTeam && s.World.FixturesOf(t.Id).Count(x => x.fixture.Played) >= 2);
            s.Job.Vacancies.Add(new Vacancy { TeamId = other.Id, Opened = s.Today, Status = "offered", DecideOn = s.Today.AddDays(7) });
            Assert.Null(s.AcceptOffer(other.Id));
            PlayUntil(s, () => s.MyGames().Any(g => g.Played && !g.NotCoached));

            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            vm.Navigate("Schedule & Results");
            var sched = Assert.IsType<ScheduleViewModel>(vm.CurrentPage);
            var old = sched.Games.First(g => g.Played && g.NotCoached);
            Assert.False(old.HasBox);
            Assert.Equal(ScheduleViewModel.NotCoachedText, old.TopPerformer);
            Assert.NotEmpty(old.Score);
            sched.Selected = old;
            Assert.False(sched.CanOpenBox);
            Assert.Equal(ScheduleViewModel.NotCoachedTip, sched.OpenBoxTip);
            if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } dir)
            {
                window.UpdateLayout();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(dir, "schedule-before-you-arrived.png"));
            }

            var ours = sched.Games.First(g => g.Played && !g.NotCoached);
            Assert.True(ours.HasBox);
            Assert.NotEqual(ScheduleViewModel.NotCoachedText, ours.TopPerformer);
            sched.Selected = ours;
            Assert.True(sched.CanOpenBox);
            Assert.DoesNotContain("before you took", sched.OpenBoxTip);
            window.Close();
        }
        finally
        {
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            File.Delete(path);
        }
    }
}
