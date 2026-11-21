using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.College;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>
/// Run 139, B1 (broadcast-presentation-design.md): the Selection Show on the HSN studio set. The First Four and all 32
/// first-round games revealed game by game, each with a site and a line from the desk; Next game, Autorun and Advance;
/// Advance gives the same bracket as before. It only reads the season.
/// </summary>
[Collection(SharedUiState.Name)]
public class Run139SelectionShowTests
{
    private static readonly string[] RealNetworks = { "ESPN", "CBS", "TBS", "TNT", "truTV", "Fox", "NBC", "ABC", "Big Ten Network", "SEC Network", "ACC Network", "Draft Day" };

    [AvaloniaFact]
    public void The_selection_show_reveals_every_game_and_advances_to_the_same_bracket()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-show-{Guid.NewGuid():N}.save");
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
            var c = session.World.College!;
            while (!c.Season.Selected) session.AdvanceDay();
            vm.AfterTick();
            var s = c.Season;
            int me = session.HumanTeam.Id;

            // The script: the open, the First Four, 32 first-round games, the close.
            string before = string.Join(",", s.Bracket) + "|" + string.Join(",", s.Games.Where(g => g.Event == CollegeWorld.Madness).Select(g => $"{g.HomeId}-{g.AwayId}-{g.Slot}"));
            var beats = SelectionShow.Build(c, me);
            Assert.Equal(SelectionShow.Build(c, me).Select(b => b.Line), beats.Select(b => b.Line));
            Assert.Equal(before, string.Join(",", s.Bracket) + "|" + string.Join(",", s.Games.Where(g => g.Event == CollegeWorld.Madness).Select(g => $"{g.HomeId}-{g.AwayId}-{g.Slot}")));
            Assert.Equal("Open", beats[0].Kind);
            Assert.Equal("Close", beats[^1].Kind);
            Assert.Equal(4, beats.Count(b => b.Kind == "First Four"));
            var games = beats.Where(b => b.Kind == "Game").ToList();
            Assert.Equal(32, games.Count);
            Assert.Equal(1 + 4 + 32 + 1, beats.Count);
            for (int r = 0; r < 4; r++)
            {
                var field = MadnessBracket.RegionField(s, r);
                var shown = games.Where(g => g.Region == r).ToList();
                Assert.Equal(8, shown.Count);
                for (int p = 0; p < 8; p++)
                {
                    Assert.Equal(p, shown[p].Pair);
                    Assert.Equal(field[p].Top.Seed, shown[p].Top!.Seed);
                    Assert.Equal(field[p].Bottom.Seed, shown[p].Bottom!.Seed);
                    Assert.Equal(field[p].Top.Teams[0], shown[p].Top!.TeamId);
                }
            }
            // Every team of the 68 is on the screen at least once; the First Four is in Dayton.
            var ids = beats.Where(b => b.IsGame).SelectMany(b => new[] { b.Top!.TeamId, b.Bottom!.TeamId }).Where(i => i > 0).ToHashSet();
            Assert.Equal(68, ids.Count);
            Assert.All(beats.Where(b => b.Kind == "First Four"), b => Assert.Equal("DAYTON, OHIO", b.Site));
            // Eight first-round sites, each hosting two pods (four games).
            var sites = games.GroupBy(g => g.Site).ToList();
            Assert.Equal(8, sites.Count);
            Assert.All(sites, g => Assert.Equal(4, g.Count()));
            // The desk alternates, uses our own cast, and never names a real network.
            Assert.All(beats, b => Assert.Contains(b.Speaker, new[] { SelectionShow.Host, SelectionShow.Analyst }));
            Assert.All(beats.Skip(1).Zip(beats.Skip(2)).Take(36), p => Assert.NotEqual(p.First.Speaker, p.Second.Speaker));
            foreach (var b in beats)
                foreach (var n in RealNetworks)
                    Assert.DoesNotContain(n, b.Line + b.Site + b.Round, StringComparison.Ordinal);
            // Your program: its game says so, or the close does.
            if (c.RegionOf(me) >= 0) Assert.Contains(beats, b => b.Mine && b.IsGame && (b.Line.Contains(" is in!") || b.Line.Contains("You're in")));
            else Assert.True(beats[^1].Mine);

            // The page opens on the show.
            vm.Navigate("College");
            var college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 2;
            Assert.Equal(CollegeViewModel.SelectionView, college.BracketView);
            Assert.True(college.ShowOn);
            Assert.False(college.ShowRegions);
            Assert.Equal($"1 of {beats.Count}", college.ShowProgress);
            Assert.Equal("THE FIELD OF 68", college.ScreenTitle);
            Assert.Equal(4, college.ScreenBoard.Count);
            Snap(window, "run139-selection-show-open");

            // Next game through the First Four and the East: the East counts as revealed after its eighth game.
            for (int k = 0; k < 4; k++) college.ShowNextCommand.Execute(null);
            Assert.True(college.ScreenHasGame);
            Assert.Equal("FIRST FOUR · DAYTON", college.ScreenBoardTitle);
            Assert.Equal(0, college.RevealedRegions);
            for (int k = 0; k < 8; k++) college.ShowNextCommand.Execute(null);
            Assert.Equal("EAST REGION", college.ScreenBoardTitle);
            Assert.All(college.ScreenBoard, row => Assert.True(row.Shown));
            Assert.Equal(1, college.RevealedRegions);
            Assert.Equal(beats[12].Line, college.BubbleText);
            Snap(window, "run139-selection-show-east");
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px East board | {p}");
            // Go to your own game, if any, for the shots and the layout check.
            int mine = beats.FindIndex(b => b.Mine && b.IsGame);
            int at = 13;
            if (mine >= at) { while (at <= mine) { college.ShowNextCommand.Execute(null); at++; } Assert.True(college.ScreenTop!.Mine || college.ScreenBottom!.Mine); }
            Snap(window, "run139-selection-show-mine");
            foreach (var (width, scale) in new[] { (1480, 1.0), (1280, 1.0), (1280, 1.3) })
            {
                window.Width = width;
                window.ApplyScale(scale);
                Settle(window);
                if (Environment.GetEnvironmentVariable("HW_LAYOUT_SHOTS") is { Length: > 0 } shots)
                {
                    using var frame = window.CaptureRenderedFrame();
                    frame?.Save(Path.Combine(shots, $"run139-selection-show-{width}-{scale * 100:0}.png"));
                }
                foreach (var p in LayoutCheck.Scan(window)) problems.Add($"{width}px {scale:P0} Selection Show | {p}");
            }
            window.ApplyScale(1.0); window.Width = 1480;

            // Autorun toggles; Advance ends the show on the same field the reveal always showed.
            college.ShowAutorunCommand.Execute(null);
            Assert.True(college.Autorunning);
            Assert.Equal("Stop", college.AutorunText);
            college.ShowAutorunCommand.Execute(null);
            Assert.False(college.Autorunning);
            college.ShowAdvanceCommand.Execute(null);
            Assert.False(college.ShowOn);
            Assert.True(college.ShowRegions);
            Assert.Equal(4, college.RevealedRegions);
            Assert.All(college.RevealRegions, r => Assert.True(r.Shown));
            for (int r = 0; r < 4; r++)
                Assert.Equal(MadnessBracket.RegionField(s, r).Select(p => p.Top.Seed + ":" + string.Join("/", p.Top.Teams.Select(i => c.Program(i)!.City))),
                    college.RevealRegions[r].Pairs.Select(p => p.Top.Seed + ":" + p.Top.Name));
            Assert.Equal(before, string.Join(",", s.Bracket) + "|" + string.Join(",", s.Games.Where(g => g.Event == CollegeWorld.Madness).Select(g => $"{g.HomeId}-{g.AwayId}-{g.Slot}")));
            Snap(window, "run139-selection-show-advanced");

            // The show stays finished for the season while the game is open.
            vm.Navigate("Home");
            vm.Navigate("College");
            college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 2;
            Assert.False(college.ShowOn);
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

    [Fact]
    public void Pod_sites_rotate_and_pair_regions()
    {
        for (int year = 2027; year < 2040; year++)
        {
            var all = Enumerable.Range(0, 4).SelectMany(r => Enumerable.Range(0, 4).Select(p => (r, site: SelectionShow.PodSite(year, r, p)))).ToList();
            var bySite = all.GroupBy(x => x.site).ToList();
            Assert.Equal(8, bySite.Count);
            Assert.All(bySite, g => { Assert.Equal(2, g.Count()); Assert.Equal(2, g.Select(x => x.r).Distinct().Count()); });
        }
        Assert.NotEqual(SelectionShow.PodSite(2027, 0, 0), SelectionShow.PodSite(2028, 0, 0));
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
