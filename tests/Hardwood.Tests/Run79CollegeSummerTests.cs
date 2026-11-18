using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 79. H37 (bug hunt): a college coach's Offseason page was the pro one ($0.0M of a $0.0M budget, pro free
/// agents he couldn't sign, contract talks); now it's the college summer. H22: the trail's "Offers out 8 of 6" says why.
/// H36: a real veteran's Hall of Fame line no longer claims "0 titles, 0 All-Star selections". H38/H40 ride along.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run79CollegeSummerTests
{
    [Fact]
    public void Offers_over_the_limit_say_why()
    {
        var ok = CollegeViewModel.OffersChip(4, 6);
        Assert.Equal("4 of 6", ok.Value);
        Assert.Equal("", ok.Tone);
        Assert.Equal("", CollegeViewModel.OverOffersNote(4, 6, 3, 2028));

        var over = CollegeViewModel.OffersChip(8, 5);
        Assert.Equal("8 of 5", over.Value);
        Assert.Equal("warn", over.Tone);
        Assert.Contains("no new offers", over.Sub);
        var note = CollegeViewModel.OverOffersNote(8, 5, 2, 2028);
        Assert.Contains("8 offers out to the Class of 2028", note);
        Assert.Contains("only 5 (2 scholarships open + 3)", note);
        Assert.Contains("fewer than 5", note);

        // The last place filled: the limit drops to 0 (code review).
        var none = CollegeViewModel.OffersChip(3, 0);
        Assert.Equal("no scholarships open", none.Sub);
        Assert.Equal("3 out", none.Value);   // H83 (run 106): not "3 of 0"
        var noneNote = CollegeViewModel.OverOffersNote(3, 0, 0, 2028);
        Assert.Contains("unless a scholarship opens up", noneNote);
        Assert.DoesNotContain("fewer than 0", noneNote);
    }

    [Fact]
    public void A_real_veterans_Hall_of_Fame_line_does_not_claim_zero_titles()
    {
        var real = Hardwood.Core.RealWorld.RealCareers.Players.Keys.First();
        var parts = real.Split(' ', 2);
        var p = new Player { FirstName = parts[0], LastName = parts[1] };
        var c = new CareerTotals { Seasons = 24, Points = 44_868, Mvps = 4, FinalsMvps = 4 };
        var line = GameSession.HallOfFameLine(p, c);
        Assert.StartsWith($"Hall of Fame: {real}, inducted after a 24-season career: 44,868 points, 4 MVPs, 4 Finals MVPs.", line);
        Assert.DoesNotContain("0 titles", line);
        Assert.DoesNotContain("All-Star", line);

        c.Titles = 1; c.AllStars = 2;
        Assert.EndsWith("In the seasons played here: 1 title, 2 All-Star selections.", GameSession.HallOfFameLine(p, c));

        // A player whose whole career was played here keeps the full line.
        var own = new Player { FirstName = "Zzyzx", LastName = "Run79-Testman" };
        Assert.EndsWith("1 MVP, 0 titles, 3 All-Star selections.", GameSession.HallOfFameLine(own, new CareerTotals { Seasons = 12, Points = 20_000, Mvps = 1, AllStars = 3 }));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_college_coachs_summer_shows_the_college_summer_not_pro_free_agency()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run79-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            start.PlayLeague = StartViewModel.CollegeChoice;
            start.SelectedTeam = start.Teams[1];
            start.NewGame();
            var s = vm.Session!;
            Assert.True(s.InCollege);
            int guard = 0;
            while (s.Phase != SessionPhase.Offseason && guard++ < 500) s.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            vm.AfterTick();
            var w = window;
            void Settle() { for (int i = 0; i < 4; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

            vm.Navigate("Offseason"); Settle();
            var page = Assert.IsType<OffseasonViewModel>(vm.CurrentPage);
            page.Refresh(); Settle();
            Assert.True(page.CollegeSummer);
            Assert.False(page.ShowTalk);
            Assert.False(page.CanReleaseFromRoster);
            Assert.False(page.ShowContracts);
            Assert.False(page.ShowFreeAgency);
            Assert.Equal(3, page.Tab);   // Your roster
            Assert.DoesNotContain("$", page.CapText);
            Assert.DoesNotContain("budget", page.CapText);
            Assert.StartsWith($"Roster {s.HumanTeam.Roster.Count}", page.CapText);
            Assert.DoesNotContain("owner", page.Instructions);
            Assert.Contains("no contracts", page.Instructions);
            Assert.Contains("Class of", page.CollegeSummerText);
            Assert.Contains("College page", page.CollegeSummerText);
            Assert.Contains("Graduation and enrollment", page.CollegeSummerText);   // he coached here when the summer began
            Assert.Equal(s.HumanTeam.Roster.Count, page.Roster.Count);
            Assert.Empty(page.FreeAgents);
            Assert.Empty(page.Expiring);

            var view = window.GetVisualDescendants().OfType<OffseasonView>().First(v => v.DataContext == page && v.Bounds.Width > 0);
            var tabs = view.GetVisualDescendants().OfType<TabItem>().Where(t => t.IsEffectivelyVisible).Select(t => t.Header as string).ToList();
            Assert.DoesNotContain("Contracts up", tabs);
            Assert.DoesNotContain("Free agents", tabs);
            Assert.Contains("Your roster", tabs);
            var panel = view.GetVisualDescendants().OfType<Border>().First(b => b.Name == "CollegeSummerPanel");
            Assert.True(panel.IsEffectivelyVisible);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && (b.Content as string) == "Release selected");
            var problems = LayoutCheck.Scan(window);
            Assert.True(problems.Count == 0, "College summer:\n" + string.Join("\n", problems));
            if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, "run79-college-summer.png")); }

            // On to the pros' draft: the tab is there to watch, with no Draft button for a program.
            guard = 0;
            while (s.Phase == SessionPhase.Offseason && s.Offseason!.Phase != OffseasonPhase.Draft && guard++ < 50) s.AdvanceOffseason();
            if (s.Phase == SessionPhase.Offseason && s.Offseason!.Phase == OffseasonPhase.Draft)
            {
                vm.AfterTick(); page.Refresh(); Settle();
                Assert.True(page.CollegeSummer);
                if (page.ShowDraft)
                {
                    Assert.Contains("College programs don't pick", page.DraftText);
                    Assert.DoesNotContain("your board", page.DraftNote);
                    Assert.DoesNotContain("Press Draft", page.DraftHint);
                    if (page.DraftCards.Count > 1)
                    {
                        page.PickProspect(page.DraftCards[1]);
                        Assert.Contains("on the pros' board", page.DraftHint);   // a press says something (bug finder)
                    }
                }
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
