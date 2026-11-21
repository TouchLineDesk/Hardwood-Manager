using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 142, B3 (broadcast-presentation-design.md): draft night on the HSN stage. The lottery opens envelope by envelope
/// into the order the summer already drew; the draft airs pick by pick with the picks the summer made; a college coach's
/// graduates are flagged. It only reads the summer: nothing is re-drawn and the picks don't change.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run142DraftNightTests
{
    private static readonly string[] RealNames = { "ESPN", "ABC", "TNT", "NBA TV", "Adam Silver", "Mark Tatum", "Draft Day", "Wolverine" };

    [AvaloniaFact]
    public void Draft_night_shows_the_drawn_order_and_the_picks_made_with_your_graduates_flagged()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-draftnight-{Guid.NewGuid():N}.save");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { FaceFolder = oldSettings.FaceFolder, LogoFolder = oldSettings.LogoFolder, AutoPortraits = false });
        var problems = new List<string>();
        MainWindow? window = null;
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
            int guard = 0;
            while (session.Phase != SessionPhase.Offseason && guard++ < 400) session.AdvanceDay();
            Assert.True(session.Employed && session.InCollege);
            var o = session.Offseason!;
            Assert.True(o.CollegeDraftOn);
            Assert.Equal(OffseasonPhase.ReSigning, o.Phase);
            string order = string.Join(",", o.PickOwners.Select(t => t.Id));
            string pool = string.Join(",", o.Prospects.Select(p => p.Id));

            // The lottery: the extra pick (if any) first, then the last pick to the first; every pick once, as drawn.
            var c = session.World.College!;
            var grads = c.Alumni.Where(kv => kv.Value.ProgramId == session.HumanTeam.Id && kv.Value.Year == o.Season + 1).Select(kv => kv.Key).ToHashSet();
            var lottery = DraftNight.Lottery(o, null, grads);
            Assert.Equal(DraftNight.LotteryOpen, lottery[0].Kind);
            Assert.Equal(DraftNight.LotteryClose, lottery[^1].Kind);
            var envelopes = lottery.Where(b => b.Kind == DraftNight.Envelope).ToList();
            Assert.Equal(o.PickOwners.Count, envelopes.Count);
            Assert.Equal(Enumerable.Range(1, o.PickOwners.Count).ToList(), envelopes.Select(b => b.Pick).OrderBy(k => k).ToList());
            Assert.All(envelopes, b => Assert.Equal(o.PickOwners[b.Pick - 1], b.Team));
            Assert.Equal(1, envelopes[^1].Pick);
            if (o.PickOwners.Take(o.PickOwners.Count - 1).Contains(o.PickOwners[^1]))
                Assert.Equal(o.PickOwners.Count, envelopes[0].Pick);   // the extra pick opens first
            Assert.Contains("1 in ", lottery[0].Line);
            // Building the show twice gives the same show and changes nothing.
            Assert.Equal(lottery.Select(b => b.Line), DraftNight.Lottery(o, null, grads).Select(b => b.Line));
            Assert.Equal(order, string.Join(",", o.PickOwners.Select(t => t.Id)));
            Assert.Equal(pool, string.Join(",", o.Prospects.Select(p => p.Id)));
            // Before the draft there's nothing to air.
            Assert.Empty(DraftNight.Draft(o, null, _ => "x", p => new Scouting.Range(p.PotentialAbility, p.PotentialAbility), grads));

            // The page opens on draft night: the lottery.
            vm.AfterTick();
            vm.Navigate("Offseason");
            var page = (OffseasonViewModel)vm.CurrentPage!;
            Assert.True(page.ShowDraftNight);
            Assert.Equal(OffseasonViewModel.DraftNightTab, page.Tab);
            Assert.Equal(2, page.TabSpan);
            Assert.False(page.ShowCollegePanel);
            Assert.False(page.NightOnDraft);
            Assert.False(page.CanWatchDraft);
            Assert.True(page.NightShowsTitle);
            Assert.Equal($"1 of {lottery.Count}", page.NightProgress);
            Assert.All(page.RepCards, r => Assert.Null(r.Team));
            Snap(window, "run142-lottery-open");
            // Open every envelope but the last four: the stage's cards are still empty.
            int toFour = lottery.FindIndex(b => b.Kind == DraftNight.Envelope && b.Pick == DraftNight.OnStage);
            for (int k = 1; k < toFour; k++) page.NightNextCommand.Execute(null);
            Assert.True(page.NightShowsEnvelopes);
            Assert.All(page.RepCards, r => Assert.Null(r.Team));
            Assert.Equal(lottery[toFour - 1].Line, page.NightLine);
            Assert.Contains(page.Envelopes, e => e.Now && e.Open);
            Snap(window, "run142-lottery-envelopes");
            // Picks 4 to 1 fill the representatives' cards.
            for (int k = 0; k < DraftNight.OnStage; k++) page.NightNextCommand.Execute(null);
            Assert.Equal(o.PickOwners.Take(DraftNight.OnStage), page.RepCards.Select(r => r.Team));
            Assert.All(page.Envelopes, e => Assert.True(e.Open));
            Assert.All(page.NightBoard, r => Assert.StartsWith("#", r.Right));
            Snap(window, "run142-lottery-first-pick");
            foreach (var p in Scan(window, 1480, 1.0)) problems.Add($"1480px lottery | {p}");
            page.NightNextCommand.Execute(null);
            Assert.False(page.CanNightNext);
            Assert.Equal(lottery[^1].Line, page.NightLine);
            Assert.True(page.NightAtDesk);
            foreach (var (w, sc) in new[] { (1280, 1.0), (1280, 1.3) })
                foreach (var p in Scan(window, w, sc)) problems.Add($"{w}px {sc:P0} lottery close | {p}");
            window.ApplyScale(1.0); window.Width = 1480;

            // Continue: the pros draft (in one press for a college coach), and the broadcast airs.
            session.AdvanceOffseason();
            vm.AfterTick();
            Settle(window);
            o = session.Offseason!;
            Assert.Equal(OffseasonPhase.FreeAgency, o.Phase);
            page = (OffseasonViewModel)vm.CurrentPage!;
            page.Refresh();
            Assert.True(page.ShowDraft && page.ShowDraftNight && page.CanWatchDraft && page.NightOnDraft);
            Assert.Equal(OffseasonViewModel.DraftNightTab, page.Tab);
            var draft = DraftNight.Draft(o, null, _ => "x", p => new Scouting.Range(p.PotentialAbility, p.PotentialAbility), grads);
            var picks = draft.Where(b => b.IsPick).ToList();
            Assert.Equal(o.Report.Draft.Select(d => d.Player.Id), picks.Select(b => b.Player!.Id));
            Assert.Equal(o.Report.Draft.Select(d => d.Team.Id), picks.Select(b => b.Team!.Id));
            Assert.Equal(order, string.Join(",", o.PickOwners.Select(t => t.Id)));
            Assert.Equal(DraftNight.DraftClose, draft[^1].Kind);
            // Your graduates are flagged, and the close counts them.
            Assert.Equal(o.Report.Draft.Count(d => grads.Contains(d.Player.Id)), picks.Count(b => b.MyGraduate));
            if (o.Report.Draft.Any(d => grads.Contains(d.Player.Id)) || o.Prospects.Any(p => grads.Contains(p.Id)))
                Assert.Contains("senior", draft[^1].Line);
            Assert.Equal("DRAFT NIGHT", page.NightTitle);
            page.NightNextCommand.Execute(null);
            Assert.True(page.NightShowsCard && page.CardHasPlayer);
            Assert.Equal(o.Report.Draft[0].Player.Name, page.CardName);
            Assert.Contains("(your staff's read)", page.CardRange);
            Assert.StartsWith("With the 1st pick in the draft,", page.CardAnnounce);
            Assert.Equal(o.PickOwners.Take(DraftNight.OnStage), page.RepCards.Select(r => r.Team));
            Snap(window, "run142-draft-first-pick");
            // Go to your first graduate's pick, if one was taken.
            int mine = draft.FindIndex(b => b.MyGraduate);
            int at = 2;
            if (mine >= at) { while (at <= mine) { page.NightNextCommand.Execute(null); at++; } Assert.True(page.CardMine); }
            Snap(window, "run142-draft-mine");
            foreach (var p in Scan(window, 1480, 1.0)) problems.Add($"1480px draft | {p}");
            foreach (var (w, sc) in new[] { (1280, 1.0), (1280, 1.3) })
                foreach (var p in Scan(window, w, sc)) problems.Add($"{w}px {sc:P0} draft | {p}");
            window.ApplyScale(1.0); window.Width = 1480;
            page.NightAutorunCommand.Execute(null);
            Assert.True(page.NightAutorunning);
            page.NightAutorunCommand.Execute(null);
            Assert.False(page.NightAutorunning);
            page.NightSkipCommand.Execute(null);
            Assert.False(page.CanNightNext);
            // Review (run 142): in free agency the undrafted are back in the pool; the close still counts them and
            // speaks of every one of your seniors, drafted or not.
            Assert.Contains("go undrafted into free agency", page.NightLine);
            if (grads.Count > 0) Assert.Contains("senior", page.NightLine);
            Snap(window, "run142-draft-close");
            // The lottery can be watched again; the draft keeps its place.
            page.NightLotteryShowCommand.Execute(null);
            Assert.False(page.NightOnDraft);
            page.NightDraftShowCommand.Execute(null);
            Assert.Contains("go undrafted", page.NightLine);
            // Other tabs bring the side panel back.
            page.Tab = 3;
            Assert.Equal(1, page.TabSpan);
            Assert.True(page.ShowCollegePanel);

            // Our own cast and brand only.
            foreach (var b in lottery.Concat(draft))
                foreach (var n in RealNames)
                    Assert.DoesNotContain(n, b.Line + b.Announce + b.Speaker.Name, StringComparison.Ordinal);
        }
        finally
        {
            window?.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
            if (report != null) File.AppendAllLines(report, problems);
        }
        Assert.True(problems.Count == 0, $"{problems.Count} pieces of text are cut off:\n" + string.Join("\n", problems.Take(60)));
    }

    private static IEnumerable<string> Scan(MainWindow window, int width, double scale)
    {
        window.Width = width;
        window.ApplyScale(scale);
        Settle(window);
        if (Environment.GetEnvironmentVariable("HW_LAYOUT_SHOTS") is { Length: > 0 } shots)
        {
            Directory.CreateDirectory(shots);
            using var frame = window.CaptureRenderedFrame();
            frame?.Save(Path.Combine(shots, $"run142-{width}-{scale * 100:0}-{Guid.NewGuid():N}"[..24] + ".png"));
        }
        return LayoutCheck.Scan(window).Select(p => p.ToString()).ToList();
    }

    private static void Settle(MainWindow window)
    {
        for (int i = 0; i < 3; i++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }
    }

    private static void Snap(MainWindow window, string name)
    {
        Settle(window);
        if (Environment.GetEnvironmentVariable("HW_LAYOUT_SHOTS") is not { Length: > 0 } shots) return;
        Directory.CreateDirectory(shots);
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(shots, $"{name}.png"));
    }
}
