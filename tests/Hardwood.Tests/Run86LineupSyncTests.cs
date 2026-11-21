using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Model;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>Run 86. The live game's tracker, two bug-hunt finds: "Your five" follows the floor after Step, Sim to end
/// and the final, keeps the user's own edits while paused, and Apply lineup names a fouled-out pick (H55); the break
/// card leaves the play by play / box score under it room for rows (H57). Presentation only: no engine result changes.</summary>
[Collection(SharedUiState.Name)]
public class Run86LineupSyncTests
{
    private readonly ITestOutputHelper _out;
    public Run86LineupSyncTests(ITestOutputHelper output) => _out = output;

    [Fact]
    public void The_pickers_sync_unless_the_user_edited_while_paused_or_a_five_is_waiting()
    {
        // complete, pending, running, edited
        Assert.True(LiveGameViewModel.ShouldSyncLineup(running: false, complete: false, pending: false, edited: false));   // Step
        Assert.True(LiveGameViewModel.ShouldSyncLineup(running: true, complete: false, pending: false, edited: true));    // playing
        Assert.False(LiveGameViewModel.ShouldSyncLineup(running: false, complete: false, pending: false, edited: true));  // paused, edited
        Assert.False(LiveGameViewModel.ShouldSyncLineup(running: true, complete: false, pending: true, edited: false));   // waiting to go in
        Assert.True(LiveGameViewModel.ShouldSyncLineup(running: false, complete: true, pending: true, edited: true));     // the final
    }

    private static HashSet<Player> Floor(LiveGameViewModel page, Hardwood.Core.Engine.GameSimulator.LiveGame live) =>
        (live.Result.Home.Team == page.Us ? live.HomeOnCourt : live.AwayOnCourt).ToHashSet();
    private static HashSet<Player> Pickers(LiveGameViewModel page) => page.Lineup.Select(s => s.Selected).OfType<Player>().ToHashSet();

    [Avalonia.Headless.XUnit.AvaloniaTheory]
    [InlineData("BOS", "run86-lineup-bos")]
    [InlineData("SAC", "run86-lineup-sac")]
    public void Your_five_follows_the_floor_and_the_break_card_leaves_box_rows(string club, string shot)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run86-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        var settingsPath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.FilePath = settingsPath;
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
            start.SelectedTeam = start.Teams.First(t => t.Team.Abbreviation == club);
            start.NewGame();
            var s = vm.Session!;
            int guard = 0;
            while (!s.HumanPlaysToday && guard++ < 60) s.AdvanceDay();
            Assert.True(s.HumanPlaysToday);
            vm.AfterTick();
            var w = window;
            void Settle() { for (int i = 0; i < 5; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); w.UpdateLayout(); } }

            var (live, others) = s.BeginLiveDay();
            var page = new LiveGameViewModel(vm, live!, others);
            vm.CurrentPage = page; Settle();
            Assert.Equal(Floor(page, live!), Pickers(page));

            // Step: the pickers follow every substitution.
            int subs = 0; var last = Floor(page, live!);
            for (int i = 0; i < 120 && !page.Complete; i++)
            {
                page.StepOne();
                var floor = Floor(page, live!);
                if (!floor.SetEquals(last)) subs++;
                last = floor;
                Assert.True(floor.SetEquals(Pickers(page)), $"after Step {i + 1} the pickers show {string.Join(", ", Pickers(page).Select(p => p.LastName))}, the floor {string.Join(", ", floor.Select(p => p.LastName))}");
            }
            _out.WriteLine($"{club}: {subs} line-up changes in 120 Steps, the pickers matched every time");
            Assert.True(subs > 0);

            // Paused, an edit stays through a Step (the user is choosing), and the applied five goes in and is shown.
            var bench = page.OurPlayers.Select(r => r.Player).First(p => !Floor(page, live!).Contains(p)
                && !(live!.Result.Home.Team == page.Us ? live.Result.Home : live.Result.Away).LineFor(p).FouledOut);
            page.Lineup[4].Selected = bench;
            page.StepOne();
            Assert.Same(bench, page.Lineup[4].Selected);
            var want = Pickers(page);
            page.ApplyLineup();
            Assert.True(live!.LineupPending(page.Us));
            for (int i = 0; i < 40 && live.LineupPending(page.Us); i++)
            {
                page.StepOne();
                if (live.LineupPending(page.Us)) Assert.True(want.SetEquals(Pickers(page)), "the applied five was overwritten before it went in");
            }
            Assert.False(live.LineupPending(page.Us));
            Assert.True(Floor(page, live).SetEquals(Pickers(page)));
            Assert.Contains(bench, Floor(page, live));

            // Sim to end: the final's floor, and no fouled-out player among the pickers.
            page.SimToEnd(); Settle();
            Assert.True(page.Complete);
            Assert.True(Floor(page, live).SetEquals(Pickers(page)));
            var ourBox = live.Result.Home.Team == page.Us ? live.Result.Home : live.Result.Away;
            Assert.DoesNotContain(Pickers(page), p => ourBox.LineFor(p).FouledOut);

            // H57: at the final the break card shows, and the box score under it still has room for rows.
            Assert.True(page.ShowBreak);
            // B4b (run 145): the postgame show comes on first; the final's card is one button away.
            if (page.DeskUp) { Assert.True(page.PostgameDesk); page.ToCardCommand.Execute(null); }
            Assert.True(page.CardUp);
            page.ToBoxCommand.Execute(null); Settle();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Settle();
            var view = window.GetVisualDescendants().OfType<LiveGameView>().First(v => v.Bounds.Width > 0);
            var box = view.GetVisualDescendants().OfType<ScrollViewer>().First(x => x.Name == "LiveBox");
            var card = view.GetVisualDescendants().OfType<ScrollViewer>().First(x => x.Name == "BreakCard");
            _out.WriteLine($"{club}: break card {card.Bounds.Height:0} (cap {card.MaxHeight:0}), box rows {box.Bounds.Height:0}");
            Assert.True(card.IsEffectivelyVisible && box.IsEffectivelyVisible);
            var court = view.GetVisualDescendants().OfType<Border>().First(x => x.Name == "CourtPanel");
            var logPanel = view.GetVisualDescendants().OfType<Border>().First(x => x.Name == "LogPanel");
            if (dir != null) { using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, shot + "-final-box.png")); }
            // The box score keeps the room it has without the card (it isn't squeezed under it any more).
            double withCardRows = box.Bounds.Height;
            Assert.True(withCardRows >= 80, $"the box score has only {withCardRows:0} px with the break card up");
            // The card takes the court's place in its panel: inside it, and the court is hidden while it shows.
            Assert.True(card.GetVisualAncestors().Contains(court));
            Assert.False(view.GetVisualDescendants().OfType<Hardwood.Desktop.Controls.LiveCourt>().First().IsEffectivelyVisible);
            var withCard = LayoutCheck.Scan(window);
            Assert.True(withCard.Count == 0, "Live game final with the break card:\n" + string.Join("\n", withCard));
            // Hide puts it away.
            page.HideBreakCommand.Execute(null); Settle();
            Assert.False(card.IsEffectivelyVisible);
            Assert.True(page.CanReopenBreak);
            Assert.True(view.GetVisualDescendants().OfType<Hardwood.Desktop.Controls.LiveCourt>().First().IsEffectivelyVisible);
            _out.WriteLine($"{club}: box rows {withCardRows:0} with the card, {box.Bounds.Height:0} with the big court");
            Assert.True(withCardRows >= box.Bounds.Height - 2, $"box rows {withCardRows:0} with the card, {box.Bounds.Height:0} without");
            // Game flow puts it away too, and "Break card" brings it back.
            page.ReopenBreakCommand.Execute(null); Settle();
            Assert.True(card.IsEffectivelyVisible);
            page.ToFlowCommand.Execute(null); Settle();
            Assert.False(card.IsEffectivelyVisible);
            page.ReopenBreakCommand.Execute(null); Settle();
            Assert.True(card.IsEffectivelyVisible && !page.FlowShown);
            page.ToCourtCommand.Execute(null); Settle();
            var problems = LayoutCheck.Scan(window);
            Assert.True(problems.Count == 0, $"Live game final, box score at 1480x900:\n" + string.Join("\n", problems));
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            try { File.Delete(path); } catch { }
            try { File.Delete(settingsPath); } catch { }
        }
    }
}
