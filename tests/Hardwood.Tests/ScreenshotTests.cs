using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Writes PNGs of each screen when HW_SCREENSHOT_DIR is set; otherwise skipped. Used for visual review.</summary>
[Collection(SharedUiState.Name)]
public class ScreenshotTests
{
    [AvaloniaFact]
    public void Capture_screens()
    {
        var dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        var path = Path.Combine(Path.GetTempPath(), $"hw-shot-{Guid.NewGuid():N}.save");
        try
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            Snap(window, Path.Combine(dir, "00-start.png"));

            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path; start.SeedText = "2026";
            if (Environment.GetEnvironmentVariable("HW_ABROAD") is { Length: > 0 } league)
            {
                start.PlayLeague = league; // e.g. "Spanish League": a career abroad (stage 3)
                Snap(window, Path.Combine(dir, "00-start-abroad.png"));
                start.SelectedTeam = start.Teams[1];
            }
            else start.SelectedTeam = start.Teams.First(t => t.Team.Abbreviation == "BOS");
            start.NewGame();
            var session = vm.Session!;
            if (Environment.GetEnvironmentVariable("HW_SAMPLE_LOGOS") == "1") SampleLogos(session, Path.Combine(dir, "sample-logos"));
            for (int i = 0; i < 45; i++) session.AdvanceDay();
            vm.AfterTick();

            int n = 1;
            // Live game mid-way through.
            while (!session.HumanPlaysToday) session.AdvanceDay();
            vm.AfterTick();
            var (live, others) = session.BeginLiveDay();
            // Danny's pre-game studio show (#446) before tip-off.
            vm.CurrentPage = new StudioShowViewModel(vm, live!, others);
            Snap(window, Path.Combine(dir, $"{n++:00}-studio-show.png"));
            foreach (var sv in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.ScrollViewer>()
                         .Where(x => Avalonia.VisualTree.VisualExtensions.FindAncestorOfType<StudioShowView>(x) != null).Take(1))
            {
                sv.ScrollToEnd();
                Snap(window, Path.Combine(dir, $"{n++:00}-studio-show-picks.png"));
            }
            var liveVm = new LiveGameViewModel(vm, live!, others);
            vm.CurrentPage = liveVm;
            for (int i = 0; i < 120; i++) live!.Step();
            liveVm.Refresh();
            liveVm.PlayPause();
            Snap(window, Path.Combine(dir, $"{n++:00}-live-game.png"));
            liveVm.ShowCourt = false;
            Snap(window, Path.Combine(dir, $"{n++:00}-live-flow.png"));
            liveVm.ShowCourt = true;
            // The break card with the in-game read (#268): what they're really running and the counter.
            liveVm.ShowBreakCard(false);
            Snap(window, Path.Combine(dir, $"{n++:00}-live-break.png"));
            liveVm.ShowBreak = false;
            liveVm.SimToEnd();
            liveVm.Continue();
            if (session.TrailOpen)
            {
                // A college coach's recruiting trail (C3) in season, with a prospect scouted and offered.
                vm.Navigate("College");
                var trail = (CollegeViewModel)vm.CurrentPage!;
                trail.Tab = 5;
                if (trail.TrailRows.Count > 0) { trail.SelectedTrail = trail.TrailRows[0]; trail.ScoutTrailCommand.Execute(null); trail.OfferTrailCommand.Execute(null); }
                Snap(window, Path.Combine(dir, $"{n++:00}-college-trail.png"));
                trail.TrailView = "My board";
                Snap(window, Path.Combine(dir, $"{n++:00}-college-trail-board.png"));
            }
            if (session.InCollege)
            {
                // Bracketology (C4 part 1) in season: the projected field and the bubble.
                vm.Navigate("College");
                ((CollegeViewModel)vm.CurrentPage!).Tab = 2;
                Snap(window, Path.Combine(dir, $"{n++:00}-college-bracketology.png"));
            }
            foreach (var nav in vm.Nav.ToList())
            {
                vm.Navigate(nav.Name);
                Snap(window, Path.Combine(dir, $"{n++:00}-{nav.Name.Replace(" & ", "-").Replace(' ', '-').ToLower()}.png"));
            }
            if (Hardwood.Core.RealWorld.RealWorldData.IsReal(session.League))
            {
                // The real NBA's past (R1): records to beat, titles by franchise, every season.
                vm.Navigate("History");
                ((HistoryViewModel)vm.CurrentPage!).Tab = 2;
                Snap(window, Path.Combine(dir, $"{n++:00}-nba-history.png"));
                // The records book starts from the real careers (R2 part 2b).
                ((HistoryViewModel)vm.CurrentPage!).Tab = 1;
                Snap(window, Path.Combine(dir, $"{n++:00}-records-book.png"));
                window.UpdateLayout();
                foreach (var sv in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.ScrollViewer>()
                    .Where(x => x.IsEffectivelyVisible && x.Extent.Height > x.Viewport.Height + 1))
                    sv.Offset = new Vector(0, sv.Extent.Height);
                Snap(window, Path.Combine(dir, $"{n++:00}-records-book-careers.png"));
            }
            if (session.HasFacilities)
            {
                // Money buys things (item M, #281): facilities, a project under way and the front office's proposals.
                LayoutCheckTests.FacilityDemo(session);
                vm.Navigate("Board");
                Snap(window, Path.Combine(dir, $"{n++:00}-facilities.png"));
                window.UpdateLayout();
                foreach (var sv in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Avalonia.Controls.ScrollViewer>()
                    .Where(x => x.IsEffectivelyVisible && x.Extent.Height > x.Viewport.Height + 1))
                    sv.Offset = new Vector(0, 520);
                Snap(window, Path.Combine(dir, $"{n++:00}-facilities-proposals.png"));
            }
            // The inbox clean-up (#243): read messages deleted, "Delete these" armed on a filter, auto-clear on.
            vm.Navigate("Inbox");
            var inbox = (InboxViewModel)vm.CurrentPage!;
            if (inbox.Items.Count > 1) inbox.Selected = inbox.Items[1];
            inbox.DeleteReadCommand.Execute(null);
            inbox.Filter = inbox.Filters[^1];
            inbox.AutoClear = inbox.AutoClearOptions[2];
            inbox.DeleteShownCommand.Execute(null);
            Snap(window, Path.Combine(dir, $"{n++:00}-inbox-cleanup.png"));
            vm.Navigate("Roster");
            var roster = (RosterViewModel)vm.CurrentPage!;
            roster.Open(roster.Rows[0]);
            Snap(window, Path.Combine(dir, $"{n++:00}-player.png"));
            // Another team's page, and the Leagues page's other tabs.
            vm.ShowTeam(session.League.Teams.First(t => t != session.HumanTeam));
            Snap(window, Path.Combine(dir, $"{n++:00}-team-page.png"));
            vm.Navigate("Leagues");
            var leagues = (WorldViewModel)vm.CurrentPage!;
            leagues.Tab = 2;
            Snap(window, Path.Combine(dir, $"{n++:00}-leagues-stats.png"));
            leagues.Tab = 3;
            Snap(window, Path.Combine(dir, $"{n++:00}-leagues-rules.png"));
            leagues.Tab = 1;
            Snap(window, Path.Combine(dir, $"{n++:00}-leagues-schedule.png"));
            // Divisions everywhere (Danny, #318-#319): the top league and tier 2 by division, and Standings by division.
            leagues.Tab = 0;
            Snap(window, Path.Combine(dir, $"{n++:00}-leagues-divisions.png"));
            if (leagues.Competitions.FirstOrDefault(c => c.Key == Hardwood.Core.World.UsaPyramid.Tier2) is { } tier2)
            {
                leagues.Selected = tier2;
                Snap(window, Path.Combine(dir, $"{n++:00}-leagues-tier2-divisions.png"));
            }
            vm.Navigate("Standings");
            ((StandingsViewModel)vm.CurrentPage!).View = "By division";
            Snap(window, Path.Combine(dir, $"{n++:00}-standings-divisions.png"));
            if (!session.InCollege)
            {
                // Danny, #447 / D54: the My bids list.
                LayoutCheckTests.SeedBids(session);
                vm.Navigate("Transactions");
                ((TransactionsViewModel)vm.CurrentPage!).Tab = 1;
                Snap(window, Path.Combine(dir, $"{n++:00}-my-bids.png"));
                session.MyBids.Clear();
            }
            vm.Navigate("Schedule & Results");
            var sched = (ScheduleViewModel)vm.CurrentPage!;
            sched.OpenBox(sched.Games.First(g => g.Played));
            Snap(window, Path.Combine(dir, $"{n++:00}-boxscore.png"));

            int guard = 0;
            if (session.InCollege && session.World.College is { } cw)
            {
                // A walk through March Madness (C4 part 2): the bracket after each round, the Final Four, and the inbox.
                int Played() => cw.Season.Games.Count(g => g.Event == Hardwood.Core.College.CollegeWorld.Madness && g.Played);
                int[] after = { 4, 36, 52, 60, 64, 66, 67 };
                string[] label = { "first-four", "round-of-64", "round-of-32", "sweet-16", "elite-eight", "final-four", "final" };
                int k = 0;
                while (k < after.Length && session.Phase != SessionPhase.Offseason && guard++ < 400)
                {
                    session.AdvanceDay();
                    if (Played() < after[k]) continue;
                    while (k + 1 < after.Length && Played() >= after[k + 1]) k++;
                    vm.AfterTick();
                    vm.Navigate("College");
                    var cv = (CollegeViewModel)vm.CurrentPage!;
                    cv.Tab = 2;
                    int region = cw.RegionOf(session.HumanTeam.Id);
                    cv.BracketView = k >= 4 ? "Final Four" : region >= 0 ? Hardwood.Core.College.MadnessBracket.Regions[region] : "East";
                    Snap(window, Path.Combine(dir, $"{n++:00}-madness-{label[k]}.png"));
                    k++;
                }
                vm.Navigate("Inbox");
                var mail = (InboxViewModel)vm.CurrentPage!;
                var story = mail.Items.FirstOrDefault(i => Hardwood.Core.College.CollegeWorld.IsMadnessHeadline(i.Message.Title));
                if (story != null) mail.Selected = story;
                Snap(window, Path.Combine(dir, $"{n++:00}-madness-inbox.png"));
            }
            if (!session.InCollege && !session.Abroad)
            {
                // The division format's postseason (D36, run 30): the bracket and the third-place tournament mid-way, the
                // standings with their playoff and third-place colours, and the league's rules.
                while (session.Phase != SessionPhase.Offseason && guard++ < 400 && (session.Postseason == null || session.Postseason.Stage == "First Round"))
                    session.AdvanceDay();
                vm.AfterTick();
                vm.Navigate("Schedule & Results");
                Snap(window, Path.Combine(dir, $"{n++:00}-playoffs-series.png"));
                vm.Navigate("Standings");
                ((StandingsViewModel)vm.CurrentPage!).View = "By conference";
                Snap(window, Path.Combine(dir, $"{n++:00}-standings-playoff-places.png"));
                vm.Navigate("Leagues");
                var lp = (WorldViewModel)vm.CurrentPage!;
                if (lp.Competitions.FirstOrDefault(c => c.Key == Hardwood.Core.World.UsaPyramid.Tier2) is { } t2)
                {
                    lp.Selected = t2;
                    lp.Tab = 0;
                    Snap(window, Path.Combine(dir, $"{n++:00}-leagues-tier2-table.png"));
                    lp.Tab = 4;
                    Snap(window, Path.Combine(dir, $"{n++:00}-leagues-tier2-playoffs.png"));
                }
            }
            while (session.Phase != SessionPhase.Offseason && guard++ < 400) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Leagues");
            if (!session.InCollege)
            {
                var lp2 = (WorldViewModel)vm.CurrentPage!;
                lp2.Tab = 4;
                Snap(window, Path.Combine(dir, $"{n++:00}-leagues-history-after.png"));
                lp2.Tab = 3;
                Snap(window, Path.Combine(dir, $"{n++:00}-leagues-rules-after.png"));
            }
            vm.Navigate("Awards");
            Snap(window, Path.Combine(dir, $"{n++:00}-awards.png"));
            vm.Navigate("College");
            var college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 2;
            Snap(window, Path.Combine(dir, $"{n++:00}-college-bracket.png"));
            college.Tab = 3;
            Snap(window, Path.Combine(dir, $"{n++:00}-college-prospects.png"));
            if (session.InCollege || session.SeniorRecruiting) // real pro careers draft instead (D48 B): no senior Recruiting tab
            {
                college.Tab = 4;
                Snap(window, Path.Combine(dir, $"{n++:00}-college-recruiting.png"));
            }
            college.Tab = 6;
            Snap(window, Path.Combine(dir, $"{n++:00}-college-high-school.png"));
            college.Tab = 7;
            Snap(window, Path.Combine(dir, $"{n++:00}-college-class-rankings.png"));
            vm.Navigate("Offseason");
            Snap(window, Path.Combine(dir, $"{n++:00}-offseason-resign.png"));
            if (session.Offseason?.CollegeDraftOn == true)
            {
                ((OffseasonViewModel)vm.CurrentPage!).Tab = 4;
                Snap(window, Path.Combine(dir, $"{n++:00}-offseason-draft-lottery.png"));
            }
            session.AdvanceOffseason(); vm.AfterTick();
            Snap(window, Path.Combine(dir, $"{n++:00}-offseason-draft.png"));
            while (session.Offseason!.Phase == OffseasonPhase.Draft && !session.Offseason.DraftComplete) session.MakePick(session.Offseason.Prospects.First());
            vm.AfterTick();
            Snap(window, Path.Combine(dir, $"{n++:00}-offseason-fa.png"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>Screenshots only: a stand-in logo pack (a ringed roundel with the nickname's initial) for half the NABL.</summary>
    private static void SampleLogos(GameSession session, string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var t in session.League.Teams.Where((_, i) => i % 2 == 0))
        {
            using var bmp = new SkiaSharp.SKBitmap(256, 256);
            using var c = new SkiaSharp.SKCanvas(bmp);
            c.Clear(SkiaSharp.SKColors.Transparent);
            var primary = SkiaSharp.SKColor.Parse(t.PrimaryColor); var secondary = SkiaSharp.SKColor.Parse(t.SecondaryColor);
            using var p = new SkiaSharp.SKPaint { IsAntialias = true, Color = secondary };
            c.DrawCircle(128, 128, 124, p);
            p.Color = primary; c.DrawCircle(128, 128, 104, p);
            p.Color = SkiaSharp.SKColors.White; p.Style = SkiaSharp.SKPaintStyle.Stroke; p.StrokeWidth = 6; c.DrawCircle(128, 128, 88, p);
            p.Style = SkiaSharp.SKPaintStyle.Fill;
            p.TextSize = 130; p.FakeBoldText = true;
            var letter = t.Name[..1];
            c.DrawText(letter, 128 - p.MeasureText(letter) / 2, 174, p);
            using var data = bmp.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(folder, t.FullName + ".png"), data.ToArray());
        }
        Hardwood.Desktop.AppSettings.Use(new Hardwood.Desktop.AppSettings { LogoFolder = folder, FaceFolder = Hardwood.Desktop.AppSettings.Current.FaceFolder });
        Hardwood.Desktop.Graphics.LogoPackService.Reload();
    }

    private static void Snap(MainWindow window, string file)
    {
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        frame?.Save(file);
    }
}
