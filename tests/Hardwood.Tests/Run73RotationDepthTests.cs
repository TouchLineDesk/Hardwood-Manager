using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Season;
using Hardwood.Core.Model;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 73 (review #13 rank 10): Rotation opens on a depth chart: the five starters' cards at their spots on a
/// half court, the bench as chips, minutes as bars with − and +. A starter's card then a bench chip swaps them (the new
/// starter takes the old one's minutes); two starters' cards swap spots. The table is one switch away.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run73RotationDepthTests
{
    [Fact]
    public void The_five_spots_sit_on_the_court_without_overlapping()
    {
        var rects = Enumerable.Range(0, 5).Select(i =>
        {
            var (l, t) = RotationViewModel.SpotFor(i);
            return new Rect(l, t, RotationViewModel.CardWidth, RotationViewModel.CardHeight + 16); // room for a wrapped name
        }).ToList();
        foreach (var r in rects)
        {
            Assert.True(r.X >= 0 && r.Right <= RotationViewModel.CourtWidth, $"{r} leaves the court's width");
            Assert.True(r.Y >= 0 && r.Bottom <= RotationViewModel.CourtHeight, $"{r} leaves the court's depth");
        }
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < i; j++)
                Assert.False(rects[i].Intersects(rects[j]), $"spot {i} overlaps spot {j}");
        // The point guard is up top, the bigs nearest the basket.
        Assert.True(RotationViewModel.SpotFor(0).top < RotationViewModel.SpotFor(1).top);
        Assert.True(RotationViewModel.SpotFor(3).top > RotationViewModel.SpotFor(1).top);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void An_NBA_rotation_as_a_depth_chart_swaps_and_moves_minutes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run73-{Guid.NewGuid():N}.hwsave");
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
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
            void Snap(string name) { if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, name)); } }

            vm.Navigate("Rotation"); Settle();
            var rot = (RotationViewModel)vm.CurrentPage!;
            // The live view (a page transition can leave the old one in the tree for a moment).
            var view = window.GetVisualDescendants().OfType<RotationView>()
                .First(v => v.DataContext == rot && v.GetVisualDescendants().OfType<Button>().Any(b => b.DataContext is StarterSlot));
            var chart = s.HumanTeam.DepthChart!;

            // Cards first: five cards on the court, each his own starter, the rest on the bench, everyone in the bars.
            Assert.True(rot.ShowCards);
            Assert.True(view.FindControl<ScrollViewer>("DepthChart")!.IsEffectivelyVisible);
            Assert.Equal(5, rot.Starters.Count(sl => sl.Row != null));
            Assert.Equal(chart.Starters, rot.Starters.Select(sl => sl.Row!.Player.Id).ToList());
            Assert.Equal(s.HumanTeam.Roster.Count - 5, rot.Bench.Count);
            Assert.All(rot.Bench, b => Assert.False(b.IsStarter));
            Assert.Equal(s.HumanTeam.Roster.Count, rot.MinuteRows.Count);
            Assert.Equal(rot.Starters.Select(sl => sl.Row!).ToList(), rot.MinuteRows.Take(5).ToList());
            Assert.Contains("Starters", rot.ShareText);
            var cards = view.FindControl<ItemsControl>("CourtSlots")!.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is StarterSlot).ToList();
            Assert.Equal(5, cards.Count);
            Assert.All(cards, c => Assert.True(c.IsEffectivelyVisible && c.Bounds.Width > 0));
            Snap("run73-depth-chart.png");

            // Pick the small forward's card, then a healthy bench player: he starts there and they swap minutes.
            var sf = rot.Starters[2];
            var outgoing = sf.Row!;
            var bench = rot.Bench.First(b => b.Healthy);
            double minOut = outgoing.Minutes, minIn = bench.Minutes;
            rot.PickSlot(sf);
            Assert.True(sf.Picked);
            Assert.Contains("SF", rot.Hint);
            rot.SwapIn(bench);
            Assert.False(sf.Picked);
            Assert.Equal(bench.Player.Id, chart.Starters[2]);
            Assert.True(bench.IsStarter); Assert.False(outgoing.IsStarter);
            Assert.Equal(minOut, bench.Minutes); Assert.Equal(minIn, outgoing.Minutes);
            Assert.Equal(minOut, chart.MinutesTargets[bench.Player.Id]);
            Assert.Contains(outgoing, rot.Bench);
            Assert.DoesNotContain(bench, rot.Bench);
            Assert.StartsWith($"{bench.Name} starts at SF in place of {outgoing.Name}", rot.Status);

            // Two starters' cards swap spots; minutes stay with the players.
            var pg = rot.Starters[0].Row!; var c5 = rot.Starters[4].Row!;
            double pgMin = pg.Minutes;
            rot.PickSlot(rot.Starters[0]);
            rot.PickSlot(rot.Starters[4]);
            Assert.Equal(c5.Player.Id, chart.Starters[0]);
            Assert.Equal(pg.Player.Id, chart.Starters[4]);
            Assert.Equal(pgMin, pg.Minutes);
            Assert.Contains("swap spots", rot.Status);
            Assert.All(rot.Starters, sl => Assert.False(sl.Picked));

            // Clicking a card twice lets it go.
            rot.PickSlot(rot.Starters[1]); rot.PickSlot(rot.Starters[1]);
            Assert.Null(rot.PickedSlot);

            // − and + move two minutes and the chart follows.
            var row = rot.MinuteRows[0];
            double before = row.Minutes;
            row.LessCommand.Execute(null);
            Assert.Equal(before - 2, row.Minutes);
            Assert.Equal(before - 2, chart.MinutesTargets[row.Player.Id]);
            row.MoreCommand.Execute(null); row.MoreCommand.Execute(null);
            Assert.Equal(Math.Min(40, before + 2), row.Minutes);

            // An injured bench player can't start.
            var hurt = rot.Bench.First(b => b.Healthy);
            hurt.Player.Injury = new Injury { Name = "Sprained ankle", Occurred = s.Today, ReturnDate = s.Today.AddDays(12) };
            rot.Refresh(); Settle();
            var hurtRow = rot.Bench.First(b => b.Player == hurt.Player);
            Assert.False(hurtRow.Healthy);
            Assert.Contains("Out 12d", hurtRow.ChipLine);
            var startersBefore = chart.Starters.ToList();
            rot.PickSlot(rot.Starters[1]);
            rot.SwapIn(hurtRow);
            Assert.Equal(startersBefore, chart.Starters);
            Assert.Contains("can't start", rot.Status);
            hurt.Player.Injury = null;

            // The table is one switch away; back on cards the court returns.
            rot.View = "Full table"; Settle();
            Assert.False(view.FindControl<ScrollViewer>("DepthChart")!.IsEffectivelyVisible);
            Assert.NotNull(view.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault(g => g.IsEffectivelyVisible));
            Snap("run73-table.png");
            rot.View = "Cards"; Settle();
            Assert.True(view.FindControl<ScrollViewer>("DepthChart")!.IsEffectivelyVisible);

            // Suggest rotation puts the assistant's five back on the court.
            rot.AutoRotation(); Settle();
            Assert.Equal(s.HumanTeam.DepthChart!.Starters, rot.Starters.Select(sl => sl.Row!.Player.Id).ToList());

            // No text cut off on the depth chart at this size.
            var problems = LayoutCheck.Scan(window);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            foreach (var f in new[] { path, path + ".bak" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
