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
            var liveVm = new LiveGameViewModel(vm, live!, others);
            vm.CurrentPage = liveVm;
            for (int i = 0; i < 120; i++) live!.Step();
            liveVm.Refresh();
            liveVm.PlayPause();
            Snap(window, Path.Combine(dir, $"{n++:00}-live-game.png"));
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
            }
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
            vm.Navigate("Schedule & Results");
            var sched = (ScheduleViewModel)vm.CurrentPage!;
            sched.OpenBox(sched.Games.First(g => g.Played));
            Snap(window, Path.Combine(dir, $"{n++:00}-boxscore.png"));

            int guard = 0;
            while (session.Phase != SessionPhase.Offseason && guard++ < 400) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Awards");
            Snap(window, Path.Combine(dir, $"{n++:00}-awards.png"));
            vm.Navigate("College");
            var college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 2;
            Snap(window, Path.Combine(dir, $"{n++:00}-college-bracket.png"));
            college.Tab = 3;
            Snap(window, Path.Combine(dir, $"{n++:00}-college-prospects.png"));
            college.Tab = 4;
            Snap(window, Path.Combine(dir, $"{n++:00}-college-recruiting.png"));
            college.Tab = 6;
            Snap(window, Path.Combine(dir, $"{n++:00}-college-high-school.png"));
            college.Tab = 7;
            Snap(window, Path.Combine(dir, $"{n++:00}-college-class-rankings.png"));
            vm.Navigate("Offseason");
            Snap(window, Path.Combine(dir, $"{n++:00}-offseason-resign.png"));
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
