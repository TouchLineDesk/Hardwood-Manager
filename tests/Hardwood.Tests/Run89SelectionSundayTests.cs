using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.College;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 89. C4 part 2 (college-sim-design #164): Selection Sunday as a reveal screen, region by region, with your
/// seed and first opponent once your region is out, and the bubble at the end.</summary>
[Collection(SharedUiState.Name)]
public class Run89SelectionSundayTests
{
    [AvaloniaFact]
    public void Selection_Sunday_reveals_the_field_region_by_region()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-selection-{Guid.NewGuid():N}.save");
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
            Assert.True(session.InCollege);
            var c = session.World.College!;
            while (!c.Season.Selected) session.AdvanceDay();
            vm.AfterTick();
            var s = c.Season;
            Assert.DoesNotContain(s.Games, g => g.Event == CollegeWorld.Madness && g.Played);
            int me = session.HumanTeam.Id;
            // The coach's Selection Sunday message points at the reveal.
            Assert.Contains(session.Inbox, m => m.Title.StartsWith("Selection Sunday") && m.Body.Contains("March Madness → Selection Sunday") || m.Title == "Selection Sunday: we're left out");

            // The core view of a region: eight pairings in bracket order, seeds adding to 17, every team of the 68 once.
            var all = new List<int>();
            for (int r = 0; r < 4; r++)
            {
                var pairs = MadnessBracket.RegionField(s, r);
                Assert.Equal(8, pairs.Count);
                Assert.All(pairs, p => Assert.Equal(17, p.Top.Seed + p.Bottom.Seed));
                Assert.Equal(new[] { 1, 8, 5, 4, 6, 3, 7, 2 }, pairs.Select(p => p.Top.Seed));
                all.AddRange(pairs.SelectMany(p => p.Top.Teams.Concat(p.Bottom.Teams)));
            }
            Assert.Equal(68, all.Count);
            Assert.Equal(68, all.Distinct().Count());
            Assert.Equal(4, MadnessBracket.RegionField(s, 0).Concat(MadnessBracket.RegionField(s, 1)).Concat(MadnessBracket.RegionField(s, 2)).Concat(MadnessBracket.RegionField(s, 3))
                .SelectMany(p => new[] { p.Top, p.Bottom }).Count(l => l.FirstFour));
            Assert.All(all, id => Assert.True(s.Seeds.ContainsKey(id)));

            // The page opens on the reveal with nothing shown, and our seed hidden.
            vm.Navigate("College");
            var college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 2;
            Assert.Equal(CollegeViewModel.SelectionView, college.BracketView);
            Assert.True(college.ShowSelection && college.FieldPicked);
            Assert.Equal(0, college.RevealedRegions);
            Assert.All(college.RevealRegions, r => Assert.False(r.Shown));
            Assert.Equal("Revealed next", college.RevealRegions[0].Line);
            Assert.Equal("Reveal the East", college.RevealButton);
            Assert.Equal("?", college.MadnessChips.First(ch => ch.Label == "Our seed").Value);
            Snap(window, "selection-0");

            int myRegion = c.RegionOf(me);
            for (int k = 1; k <= 4; k++)
            {
                college.RevealNextCommand.Execute(null);
                Assert.Equal(k, college.RevealedRegions);
                Assert.Equal(k, college.RevealRegions.Count(r => r.Shown));
                Assert.All(college.RevealRegions.Where(r => r.Shown), r => Assert.Equal(8, r.Pairs.Count));
                Assert.StartsWith("No. 1 seed: ", college.RevealRegions[k - 1].Line);
                var seedChip = college.MadnessChips.First(ch => ch.Label == "Our seed");
                if (myRegion >= 0 && myRegion < k)
                {
                    Assert.Equal($"No. {s.Seeds[me]}", seedChip.Value);
                    Assert.StartsWith(MadnessBracket.Regions[myRegion] + " · ", seedChip.Sub);
                    Assert.True(college.RevealRegions[myRegion].Mine);
                    Assert.Contains(college.RevealRegions[myRegion].Pairs, p => p.Mine);
                }
                else if (k < 4 || myRegion >= 0) Assert.Equal("?", seedChip.Value);
                else Assert.Equal("Left out", seedChip.Value);
                if (k == 2) Snap(window, "selection-2");
            }
            Assert.False(college.CanReveal);
            Assert.Equal(3, college.SelectionBubble.Count);
            Assert.Contains(college.MadnessChips, ch => ch.Label == "Last team in");
            Snap(window, "selection-4");
            foreach (int width in new[] { 1480, 1280 })
            {
                window.Width = width;
                Settle(window);
                foreach (var p in LayoutCheck.Scan(window)) problems.Add($"{width}px Selection Sunday | {p}");
            }
            window.Width = 1480;

            // The reveal is kept for the season while the game is open; the other views still show the bracket.
            vm.Navigate("Home");
            vm.Navigate("College");
            college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 2;
            Assert.Equal(4, college.RevealedRegions);
            college.BracketView = "West";
            Assert.False(college.ShowSelection);
            Assert.Equal(4, college.BracketColumns.Count);
            Assert.True(college.FieldPicked);

            // Once a game is played, a page left on the reveal moves to your region's bracket, and the reveal shows everything.
            college.BracketView = CollegeViewModel.SelectionView;
            while (!s.Games.Any(g => g.Event == CollegeWorld.Madness && g.Played)) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Home");
            vm.Navigate("College");
            college = (CollegeViewModel)vm.CurrentPage!;
            Assert.Equal(myRegion >= 0 ? MadnessBracket.Regions[myRegion] : "East", college.BracketView);
            Assert.False(college.ShowSelection);
            college.BracketView = CollegeViewModel.SelectionView;
            Assert.Equal(4, college.RevealRegions.Count(r => r.Shown));
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

    /// <summary>H65 (playtest college-strong 3 31): a 1-seed out in the Round of 32 read "Result: made March Madness", and the
    /// goal read "reach the final four". The review now names the round, and goals keep their capitals.</summary>
    [Fact]
    public void The_college_season_review_names_the_round_reached()
    {
        var league = new Hardwood.Core.Generation.WorldGenerator(new Hardwood.Core.Rng(9)).Generate("North American Basketball League", 2026, new DateOnly(2026, 10, 1));
        var world = Hardwood.Core.World.GameWorld.Generate(league, 9, Hardwood.Core.Season.GameSession.DefaultFullSim, new DateOnly(2026, 10, 1));
        var cw = world.College!;
        var program = cw.Programs[0];
        var ctx = Hardwood.Core.Jobs.LeagueContext.College(cw, program);
        Hardwood.Core.Jobs.JobState Job() => new() { Confidence = 80, HiredSeason = 2025, Current = new Hardwood.Core.Jobs.Expectations { Season = 2026, Goal = Hardwood.Core.Jobs.SeasonGoal.Finals, TargetWins = 21 } };
        string Review(int round) => Hardwood.Core.Jobs.Board.SeasonReview(Job(), new Hardwood.Core.Jobs.Coach(), ctx, program, Hardwood.Core.Jobs.SeasonGoal.Playoffs, 24, 8,
            new Dictionary<int, Hardwood.Core.Season.PlayerSeasonStats>(), new DateOnly(2027, 4, 7), 2026, madnessRound: round).body;
        Assert.Contains("Goal: reach the Final Four. Result: won a game in March Madness, out in the Round of 32", Review(2));
        Assert.Contains("Result: made March Madness, out in the Round of 64", Review(1));
        Assert.Contains("Result: made March Madness (24-8", Review(-1));
        Assert.Equal("made the First Four, out before the Round of 64",
            Hardwood.Core.Jobs.Board.DescribeResult(Hardwood.Core.Jobs.SeasonGoal.Develop, ctx, madnessRound: 0));
        Assert.Equal("reach the Sweet 16", Hardwood.Core.Jobs.Board.LowerFirst(Hardwood.Core.Jobs.Board.Describe(Hardwood.Core.Jobs.SeasonGoal.SecondRound, ctx)));
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
