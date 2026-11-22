using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hardwood.Core.Engine;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 158, K23 (critic review #21): the live court was covered by its own panels: the Game flow drawer over the
/// right half of the floor, the ribbon board's text under the score bar, and the drawer tabs gone while a drawer was open
/// (the finder's four dead ends: "On the floor", "Apply lineup", "Double their best scorer", the Box score tab). Now the
/// page tells the court the room its panels take and the court fits into the rest, at 1480 px and at 130% (1138 px).</summary>
[Collection(SharedUiState.Name)]
public class Run158LiveCourtTests
{
    private readonly ITestOutputHelper _out;
    public Run158LiveCourtTests(ITestOutputHelper output) => _out = output;

    [AvaloniaFact]
    public void The_court_fits_inside_the_room_its_reserve_leaves()
    {
        foreach (var size in new[] { new Size(1480, 860), new Size(1138, 660), new Size(700, 500) })
        foreach (var reserve in new[] { new Thickness(256, 110, 136, 70), new Thickness(12, 110, 584, 70), new Thickness(44, 124, 44, 66) })
        {
            var (s, o) = MatchCourt.Fit(size, reserve);
            var e = MatchCourt.Extent(s, o);
            var room = new Rect(reserve.Left, reserve.Top, size.Width - reserve.Left - reserve.Right, size.Height - reserve.Top - reserve.Bottom);
            Assert.True(e.Left >= room.Left - 0.5 && e.Right <= room.Right + 0.5 && e.Top >= room.Top - 0.5 && e.Bottom <= room.Bottom + 0.5,
                $"{size} {reserve}: extent {e} outside {room}");
            // Centred in the room on the axis with slack.
            Assert.True(Math.Abs((e.Left - room.Left) - (room.Right - e.Right)) < 1);
            Assert.True(Math.Abs((e.Top - room.Top) - (room.Bottom - e.Bottom)) < 1);
        }
        // Only a narrow page steps the controls aside for an open drawer (the drawer keeps its width for the box score).
        Assert.False(LiveGameView.StepAside(1480));
        Assert.False(LiveGameView.StepAside(1210));   // 1480 with the assistant open
        Assert.True(LiveGameView.StepAside(868));     // 130% with the assistant open
    }

    [AvaloniaFact]
    public void No_panel_covers_the_floor_and_the_tabs_stay_beside_an_open_drawer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run158-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false, StudioShow = false });
        string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } sd ? sd : null;
        MainWindow? window = null;
        try
        {
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            start.SelectedTeam = start.Teams.First(t => t.Team.Abbreviation == "BOS");
            start.NewGame();
            var s = vm.Session!;
            for (int i = 0; i < 30; i++) s.AdvanceDay();
            vm.AfterTick();
            var w = window;
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }
            void Shot(string name) { if (dir == null) return; Directory.CreateDirectory(dir); using var f = w.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, $"run158-{name}.png")); }

            var home = s.HumanTeam; var away = s.League.Teams.First(t => t != home);
            var game = new GameSimulator().StartLive(home, away, s.League.Rules, 158, s.Today);
            var page = new LiveGameViewModel(vm, game, new());
            vm.InLiveGame = true;
            vm.CurrentPage = page; Settle();
            page.PlayPause();
            for (int i = 0; i < 60 * 30; i++) page.Play(1.0 / 60);
            page.PlayPause();
            Settle();
            var view = w.GetVisualDescendants().OfType<LiveGameView>().First(v => v.IsEffectivelyVisible);
            var court = view.GetVisualDescendants().OfType<MatchCourt>().Single();
            var tabs = view.FindControl<Border>("DrawerTabs")!;

            Rect Of(Control c) => c.TransformToVisual(court) is { } m ? new Rect(c.Bounds.Size).TransformToAABB(m) : default;
            void NothingCovers(string label)
            {
                var (sc, o) = MatchCourt.Fit(court.Bounds.Size, court.Reserve);
                var floor = MatchCourt.Extent(sc, o);
                _out.WriteLine($"{label}: page {court.Bounds.Size}, floor {94 * sc:0} px wide, extent {floor}, reserve {court.Reserve}");
                foreach (var name in new[] { "ScoreBar", "LeftStack", "DrawerTabs", "Drawer", "CommentaryBar" })
                {
                    var c = view.FindControl<Control>(name)!;
                    if (!c.IsEffectivelyVisible) continue;
                    var r = Of(c);
                    Assert.False(r.Intersects(floor.Deflate(1)), $"{label}: {name} {r} covers the floor {floor}");
                }
                Assert.True(94 * sc > 220, $"{label}: the floor keeps a readable size ({94 * sc:0} px)");
            }

            void TabsOnScreen(string label)
            {
                // Every tab sits on the page, above the commentary bar (at 130% they used to run off the bottom).
                double floorOfPage = Of(view.FindControl<Control>("CommentaryBar")!).Top;
                foreach (var b in tabs.GetVisualDescendants().OfType<Button>())
                {
                    var r = Of(b);
                    Assert.True(r.Top >= 0 && r.Bottom <= floorOfPage + 0.5, $"{label}: tab {b.Content} at {r}, page ends at {floorOfPage}");
                }
            }

            foreach (var (width, height, tag) in new[] { (1480.0, 900.0, "1480"), (1138.0, 692.0, "130pct") })
            {
                w.Width = width; w.Height = height; Settle(); Settle();
                NothingCovers($"{tag} closed");
                TabsOnScreen($"{tag} closed");
                Shot($"{tag}-closed");
                for (int d = 0; d < 8; d++)
                {
                    page.OpenDrawerCommand.Execute(d.ToString()); Settle(); Settle();
                    Assert.True(view.FindControl<Border>("Drawer")!.IsVisible);
                    // The tabs stay up and the open one is lit, so every other drawer is one press away.
                    Assert.True(tabs.IsEffectivelyVisible, $"{tag}: the tabs stay beside drawer {d}");
                    var lit = tabs.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primary")).ToList();
                    Assert.Single(lit);
                    NothingCovers($"{tag} drawer {d}");
                    TabsOnScreen($"{tag} drawer {d}");
                    if (d is 0 or 6 or 7) Shot($"{tag}-drawer-{d}");
                }
                // The finder's dead ends: each is reachable with a drawer open.
                page.OpenDrawerCommand.Execute("6"); Settle();
                Assert.Contains(tabs.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "On the floor" && b.IsEffectivelyVisible);
                page.OpenDrawerCommand.Execute("3"); Settle();
                Assert.Contains(view.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Apply lineup" && b.IsEffectivelyVisible);
                page.OpenDrawerCommand.Execute("2"); Settle();
                Assert.Contains(view.GetVisualDescendants().OfType<CheckBox>(), b => b.Content as string == "Double their best scorer" && b.IsEffectivelyVisible);
                Assert.Contains(tabs.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Box score" && b.IsEffectivelyVisible);
                // Pressing the open drawer's tab again closes it, and the court takes the room back.
                double open = MatchCourt.Fit(court.Bounds.Size, court.Reserve).Scale;
                page.OpenDrawerCommand.Execute("2"); Settle(); Settle();
                Assert.False(page.DrawerOpen);
                Assert.True(MatchCourt.Fit(court.Bounds.Size, court.Reserve).Scale > open);
                Assert.Empty(LayoutCheck.Scan(w).Select(x => x.ToString()));
            }
            Assert.Null(MatchCourt.LastRenderError);
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
        }
    }
}
