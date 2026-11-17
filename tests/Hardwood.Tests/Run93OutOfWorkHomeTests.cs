using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Jobs;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 93. Bug hunt H15b: out of work, Home still showed the club you'd left (crest, standings row, squad pulse,
/// payroll, next game and the pre-game show), Squad's talks and Offseason's tabs led nowhere, and the summer's Continue
/// offered a draft and free agency you had no part in. H19: the job history's seasons ("2026-2028" for a job that ended
/// in 2028-29).</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run93OutOfWorkHomeTests
{
    private static string? Dir => Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;

    [Fact]
    public void Job_history_uses_season_labels()
    {
        Assert.Equal("Anchorage Steelheads, 2026-27 to 2028-29: sacked (10-52 in the last season)",
            JobRecords.Line(new JobRecord(1, "Anchorage Steelheads", 2026, 2028, 10, 52, "Sacked")));
        Assert.Equal("Boston Celtics, 2026-27: resigned (3-4 in the last season)",
            JobRecords.Line(new JobRecord(1, "Boston Celtics", 2026, 2026, 3, 4, "Resigned")));
        Assert.Equal("2099-00", JobRecords.Season(2099));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Out_of_work_Home_points_to_the_open_jobs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run93-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            var w = window;
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }
            // The page host can hold a second, empty HomeView during a page change: take the one that's drawn.
            HomeView View(HomeViewModel h) => w.GetVisualDescendants().OfType<HomeView>().Where(v => v.DataContext == h)
                .OrderByDescending(v => v.GetVisualDescendants().Count()).First();
            bool Shown(HomeView v, string name) => v.FindControl<Control>(name)?.IsEffectivelyVisible == true;

            for (int d = 0; d < 9; d++) s.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Home"); Settle();
            var home = Assert.IsType<HomeViewModel>(vm.CurrentPage);
            Assert.False(home.OutOfWork);
            Assert.Same(s.HumanTeam, home.HeroTeam);
            Assert.Equal("NEXT GAME", home.CardTitle);
            Assert.False(Shown(View(home), "JobsPanel"));
            Assert.True(home.Table.Any(r => r.Ours));
            Assert.True(vm.Nav.First(n => n.Name == "Squad").IsVisible);

            // Sacked, with one open job, one applied for and one offer (the Board page's states).
            var club = s.HumanTeam;
            s.Fire();
            var others = s.League.Teams.Where(t => t != club).Take(3).ToList();
            s.Job.Vacancies.Add(new Vacancy { TeamId = others[0].Id, Opened = s.Today, Status = "open" });
            s.Job.Vacancies.Add(new Vacancy { TeamId = others[1].Id, Opened = s.Today, Status = "applied", DecideOn = s.Today.AddDays(4) });
            s.Job.Vacancies.Add(new Vacancy { TeamId = others[2].Id, Opened = s.Today, Status = "offered", DecideOn = s.Today.AddDays(6) });
            vm.AfterTick(); vm.Navigate("Home"); Settle();
            home = Assert.IsType<HomeViewModel>(vm.CurrentPage);
            var view = View(home);
            Assert.True(home.OutOfWork);
            Assert.Null(home.HeroTeam);                                        // not the old club's crest
            Assert.Equal("Out of work", home.Headline);
            Assert.Contains($"Last job: {club.FullName} (sacked, 2026-27)", home.Subline);
            Assert.Contains("reputation", home.Subline);
            Assert.Equal("OPEN JOBS", home.CardTitle);
            Assert.Equal("1 offer waiting", home.NextGame);
            Assert.False(home.HasOpponent);                                    // no pre-game show for the old club
            Assert.Equal(3, home.Jobs.Count);
            Assert.Equal(others[2].FullName, home.Jobs[0].Title);              // the offer first, then the application
            Assert.StartsWith("Offer!", home.Jobs[0].Detail);
            Assert.StartsWith("Applied · decision by", home.Jobs[1].Detail);
            Assert.DoesNotContain(home.Table, r => r.Ours);
            Assert.True(Shown(view, "JobsPanel"));
            var buttons = view.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible).ToList();
            Assert.DoesNotContain(buttons, b => b.CommandParameter is "Squad" or "Tactics" or "Rotation");
            Assert.DoesNotContain(buttons, b => b.Content is "Pre-game show");
            Assert.True(buttons.Any(b => b.Content is "See open jobs"), $"visible {buttons.Count} of {view.GetVisualDescendants().OfType<Button>().Count()}; views {w.GetVisualDescendants().OfType<HomeView>().Count()}; " + string.Join(",", view.GetVisualDescendants().OfType<Button>().Select(b => $"{b.Content}:{b.IsVisible}")));
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), t => t.IsEffectivelyVisible && t.Text is "Everyone is content" or "Payroll committed");
            Assert.True(LayoutCheck.Scan(window).Count == 0, string.Join("\n", LayoutCheck.Scan(window)));
            if (Dir is { } dir) { using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, "run93-home-out-of-work-1480.png")); }
            window.Width = 1280; Settle();
            Assert.True(LayoutCheck.Scan(window).Count == 0, string.Join("\n", LayoutCheck.Scan(window)));
            if (Dir is { } dir2) { using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir2, "run93-home-out-of-work-1280.png")); }
            window.Width = 1480; Settle();

            // See open jobs → Board; Squad and Offseason are closed like Rotation (H52).
            buttons.First(b => b.Content is "See open jobs").Command!.Execute("Board"); Settle();
            var board = Assert.IsType<BoardViewModel>(vm.CurrentPage);
            Assert.Contains($"{club.FullName}, 2026-27: sacked", board.JobHistory);  // H19
            foreach (var name in new[] { "Squad", "Offseason" })
            {
                Assert.False(vm.Nav.First(n => n.Name == name).IsVisible, name);
                vm.Navigate(name); Settle();
                Assert.IsType<BoardViewModel>(vm.CurrentPage);
            }

            // No jobs at all: the card and the panel say what to do.
            s.Job.Vacancies.Clear();
            vm.Navigate("Home"); Settle();
            home = Assert.IsType<HomeViewModel>(vm.CurrentPage);
            Assert.Equal("No open jobs yet", home.NextGame);
            Assert.Empty(home.Jobs);
            Assert.Contains("Keep pressing Continue", home.JobsNote);

            // The summer's Continue doesn't offer a draft or free agency of yours.
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 600) { s.AdvanceDay(); if (s.Employed) break; }
            if (!s.Employed)
            {
                Assert.Equal(SessionPhase.Offseason, s.Phase);
                vm.AfterTick(); Settle();
                Assert.Equal("Continue the summer", vm.ContinueText);
                Assert.False(vm.Nav.First(n => n.Name == "Offseason").IsVisible);
            }
        }
        finally
        {
            window?.Close();
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
