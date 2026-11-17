using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.College;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 90. C4 part 2 (college-sim-design #164): the Final Four weekend at a neutral site with its own page (the four
/// regional champions and their road, the two semis and the final with each side's leading scorer), then the champions'
/// run as highlight cards with the Most Outstanding Player.</summary>
[Collection(SharedUiState.Name)]
public class Run90FinalFourTests
{
    [AvaloniaFact]
    public void The_Final_Four_weekend_has_its_own_page_and_the_champions_run()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-finalfour-{Guid.NewGuid():N}.save");
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
            var s = c.Season;
            int y = s.Year + 1;

            // Halfway through the Sweet 16: the weekend page shows the regions still in play.
            while (!s.Games.Any(g => g.Event == CollegeWorld.Madness && g.Round == 3 && g.Played)) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("College");
            var college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 2;
            college.BracketView = CollegeViewModel.FinalFourView;
            Assert.True(college.ShowFinalFour);
            Assert.False(college.HasBracket);
            Assert.Equal($"The {y} Final Four · {MadnessBracket.FinalFourHost(y)}", college.WeekendTitle);
            Assert.Equal(4, college.WeekendTeams.Count);
            Assert.All(college.WeekendTeams, t => Assert.Equal("To be decided", t.Name));
            Assert.All(college.WeekendTeams, t => Assert.StartsWith("Still alive (", t.Route));
            Assert.Equal(3, college.WeekendGames.Count);
            Assert.Equal("East champion", college.WeekendGames[0].A.Name);
            Assert.False(college.HasMontage);

            // Every March Madness game on the full engine keeps each side's leading scorer.
            var played = s.Games.Where(g => g.Event == CollegeWorld.Madness && g.Played).ToList();
            Assert.All(played, g => Assert.True(g.HomeStar is { Pts: > 0 } && g.AwayStar is { Pts: > 0 }, $"{g.Round}/{g.Slot} has no star"));
            Assert.All(played, g => Assert.True(g.HomeStar!.Pts >= 1 && g.HomeStar.Pts <= g.HomePts && g.AwayStar!.Pts <= g.AwayPts));

            // The Final Four set: the page opens on the weekend, four champions with their roads, the semis' teams.
            while (s.Games.Count(g => g.Event == CollegeWorld.Madness && g.Round == 4 && g.Played) < 4) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Home");
            vm.Navigate("College");
            college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 2;
            Assert.Equal(CollegeViewModel.FinalFourView, college.BracketView);
            Assert.True(college.ShowFinalFour);
            Assert.All(college.WeekendTeams, t => Assert.StartsWith("No. ", t.Seed));
            Assert.All(college.WeekendTeams, t => Assert.Matches(@"^4 wins, by \d+\.\d a game\. Beat \d+ .+ in the Elite Eight\.$", t.Route));
            Assert.Equal(new[] { "EAST", "WEST", "SOUTH", "MIDWEST" }, college.WeekendTeams.Select(t => t.Region));
            Assert.All(college.WeekendGames.Take(2), g => Assert.True(g.A.Seed.Length > 0 && g.B.Seed.Length > 0 && g.A.Score == ""));
            Assert.Equal("Semi-final winner", college.WeekendGames[2].A.Name);
            Assert.Contains(session.World.News.Concat(session.Inbox.Select(m => (m.Title, m.Body))), n => n.Title.StartsWith("The Final Four:") && n.Body.Contains($"in {MadnessBracket.FinalFourHost(y)}"));
            Snap(window, "finalfour-set");

            // The champions: the montage, one card per win, the final last, and the Most Outstanding Player.
            while (s.ChampionId == null) session.AdvanceDay();
            int champ = s.ChampionId!.Value;
            vm.AfterTick();
            vm.Navigate("Home");
            vm.Navigate("College");
            college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 2;
            college.BracketView = CollegeViewModel.FinalFourView;
            int wins = s.Games.Count(g => g.Event == CollegeWorld.Madness && g.WinnerId == champ);
            Assert.True(wins is 6 or 7);
            Assert.True(college.HasMontage);
            Assert.Equal(wins, college.Montage.Count);
            Assert.Equal("CHAMPIONSHIP", college.Montage[^1].Round);
            Assert.Equal("NATIONAL CHAMPIONS", college.Montage[^1].Tag);
            Assert.All(college.Montage, m => Assert.StartsWith("W ", m.Result));
            Assert.All(college.Montage, m => Assert.True(m.HasStar));
            Assert.Contains("Most Outstanding Player: ", college.MontageSub);
            Assert.StartsWith($"Champions: {c.Program(champ)!.FullName}", college.WeekendSub);
            Assert.All(college.WeekendGames, g => Assert.Contains("Final", g.When));
            Assert.All(college.WeekendGames, g => Assert.True(g.HasStars && g.A.Won != g.B.Won));
            Assert.Equal(1, college.WeekendTeams.Count(t => !t.Out));
            Snap(window, "finalfour-champions");
            foreach (int width in new[] { 1480, 1280 })
            {
                window.Width = width;
                Settle(window);
                foreach (var p in LayoutCheck.Scan(window)) problems.Add($"{width}px Final Four | {p}");
            }
            window.Width = 1480;

            // The stars are saved with the season.
            session.SaveNow();
            var loaded = GameSession.Load(path);
            var final = loaded.World.College!.Season.Games.Single(g => g.Event == CollegeWorld.Madness && g.Round == 6);
            var was = s.Games.Single(g => g.Event == CollegeWorld.Madness && g.Round == 6);
            Assert.Equal(was.HomeStar, final.HomeStar);
            Assert.Equal(was.AwayStar, final.AwayStar);
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
    public void The_Final_Four_hosts_are_the_real_ones_then_a_rotation()
    {
        Assert.Equal("Detroit, Michigan", MadnessBracket.FinalFourHost(2027));
        Assert.Equal("San Antonio, Texas", MadnessBracket.FinalFourHost(2028));
        Assert.Equal("Indianapolis, Indiana", MadnessBracket.FinalFourHost(2029));
        for (int year = 2032; year < 2140; year++) Assert.Contains(", ", MadnessBracket.FinalFourHost(year));
        var star = new GameStar(1, "Ada Okafor", 24, 9, 5);
        Assert.Equal("Ada Okafor 24 pts · 9 reb · 5 ast", star.Line);
        Assert.Equal("Ada Okafor 12 pts", new GameStar(1, "Ada Okafor", 12, 4, 2).Line);
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
