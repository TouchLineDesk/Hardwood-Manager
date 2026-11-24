using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Hardwood.Core.College;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 190. The March bracket redesign (Danny, Nov 24, `claude/bracket-redesign.md`): the whole field of 64 on one
/// screen as the TV graphic draws it, team-colour bars, region tabs, our own emblem. Display only. Checked on Selection Sunday
/// (the empty field), after the first weekend and after the title game, at 1480 and 1280 px and at 130%. Set HW_LAYOUT_SHOTS
/// to a folder to keep the screenshots.</summary>
[Collection(SharedUiState.Name)]
public class Run190BracketTests
{
    private static void Settle(MainWindow window)
    {
        for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
    }

    [AvaloniaFact]
    public void The_full_field_draws_every_team_on_one_screen_without_cutting_a_name()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-bracket-{Guid.NewGuid():N}.save");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false });
        var problems = new List<string>();
        MainWindow? window = null;
        var shots = Environment.GetEnvironmentVariable("HW_LAYOUT_SHOTS");
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            start.PlayLeague = "College basketball";
            start.SelectedTeam = start.Teams[1];
            start.NewGame();
            var session = vm.Session!;
            var c = session.World.College!;
            var s = c.Season;

            void Check(string stage, int expectNames, bool champion)
            {
                foreach (var (width, scale) in new[] { (1480, 1.0), (1280, 1.0), (1480, 1.3) })
                {
                    window.Width = width;
                    window.ApplyScale(scale);
                    vm.Navigate("Home");
                    vm.Navigate("College");
                    var college = (CollegeViewModel)vm.CurrentPage!;
                    college.Tab = 2;
                    college.BracketView = CollegeViewModel.FullView;
                    Settle(window);
                    Assert.True(college.ShowField);
                    var view = window.GetVisualDescendants().OfType<MadnessBracketView>().Single();
                    Assert.True(view.IsEffectivelyVisible);
                    // Every team in the field so far is drawn once: 60 first-round lines plus the First Four's eight on Selection
                    // Sunday; later rounds add the winners.
                    Assert.Equal(expectNames, view.Names.Count);
                    Assert.Equal(champion, view.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == "NATIONAL CHAMPION")
                                           && s.ChampionId != null);
                    // No name is cut: each fits the room its bar gives it.
                    foreach (var link in view.GetVisualDescendants().OfType<TeamLink>())
                    {
                        var probe = new TextBlock { Text = link.Content as string, FontFamily = link.FontFamily, FontSize = link.FontSize, FontWeight = link.FontWeight };
                        probe.Measure(Size.Infinity);
                        if (probe.DesiredSize.Width > link.Bounds.Width - link.Padding.Left - link.Padding.Right + 0.75)
                            problems.Add($"{stage} {width}px {scale:P0}: \"{link.Content}\" needs {probe.DesiredSize.Width:0.0} px, has {link.Bounds.Width:0.0}");
                    }
                    // Nothing borrowed: no NCAA mark or broadcaster on the graphic.
                    Assert.DoesNotContain(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text?.Contains("NCAA") == true);
                    // The field fits the page: nothing past the window's right edge.
                    var right = view.TranslatePoint(new Point(view.Bounds.Width, 0), window);
                    Assert.True(right != null && right.Value.X <= window.Bounds.Width + 1, $"{stage} {width}px: the bracket runs to {right?.X:0}");
                    if (width == 1280 && scale == 1.0)
                    {
                        // At 1280 px with the assistant open, the field shrinks to fit (the spec's 120-130 px holds only without the
                        // assistant); a first-round bar stays wide enough for its name, and a region tab draws it bigger.
                        var bar = view.Children.OfType<Border>().First(b => b.Child is Grid g && g.Children.OfType<TeamLink>().Any());
                        var a = bar.TranslatePoint(new Point(0, 0), window)!.Value; var z = bar.TranslatePoint(new Point(bar.Bounds.Width, 0), window)!.Value;
                        Assert.True(z.X - a.X >= 75, $"a first-round bar is {z.X - a.X:0} px at 1280");
                    }
                    foreach (var p in LayoutCheck.Scan(window)) problems.Add($"{stage} {width}px {scale:P0} | {p}");
                    if (shots is { Length: > 0 })
                    {
                        Directory.CreateDirectory(shots);
                        using var frame = window.CaptureRenderedFrame();
                        frame?.Save(Path.Combine(shots, $"190-{stage}-{width}-{scale * 100:0}.png"));
                    }
                }
                window.ApplyScale(1.0);
            }

            // Selection Sunday: the empty field (the reveal is its own view; the full field is one tab away).
            while (!s.Selected) session.AdvanceDay();
            vm.AfterTick();
            Check("selection", 68, false);

            // After the first weekend: the Round of 32 is played, so the Sweet 16 is filled.
            while (s.Games.Count(g => g.Event == CollegeWorld.Madness && g.Round == 2 && g.Played) < 16) session.AdvanceDay();
            vm.AfterTick();
            Check("weekend", 64 + 32 + 16 + 8, false);

            // One region, bigger: its sixteen, eight and four, and its First Four.
            vm.Navigate("College");
            var cv = (CollegeViewModel)vm.CurrentPage!;
            cv.Tab = 2;
            cv.BracketView = "South";
            Settle(window);
            Assert.True(cv.ShowField);
            Assert.Equal(2, cv.MadnessField!.Region);
            var region = window.GetVisualDescendants().OfType<MadnessBracketView>().Single();
            int ff = s.Games.Count(g => g.Event == CollegeWorld.Madness && g.Round == 0 && g.Slot / 16 == 2);
            Assert.Equal(16 + 8 + 4 + 2 * ff, region.Names.Count);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"region | {p}");
            if (shots is { Length: > 0 }) { using var frame = window.CaptureRenderedFrame(); frame?.Save(Path.Combine(shots, "190-region-south-1480.png")); }
            // The weekend page is still its own view.
            cv.BracketView = CollegeViewModel.FinalFourView;
            Settle(window);
            Assert.False(cv.ShowField);

            // After the title game: every round filled, the champion on the plate.
            while (s.ChampionId == null) session.AdvanceDay();
            vm.AfterTick();
            Check("champion", 64 + 32 + 16 + 8 + 4 + 2 + 1 + 8, true);
        }
        finally
        {
            window?.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(40)));
    }
}
