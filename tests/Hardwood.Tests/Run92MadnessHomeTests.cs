using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Hardwood.Core.College;
using Hardwood.Core.Jobs;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 92. C4 part 2's last piece (college-sim-design #164): March Madness on a pro career's Home, from Selection Sunday
/// to a few days after the final. Also H72 (College → Conferences was empty in real careers) and H73 (Home's hero kept the
/// old club's crest after a job change).</summary>
[Collection(SharedUiState.Name)]
public class Run92MadnessHomeTests
{
    [AvaloniaFact]
    public void A_pro_career_follows_March_Madness_on_Home()
    {
        var report = Environment.GetEnvironmentVariable("HW_LAYOUT_REPORT");
        var path = Path.Combine(Path.GetTempPath(), $"hw-madhome-{Guid.NewGuid():N}.save");
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
            start.SelectedTeam = start.Teams[0];
            start.NewGame();
            var session = vm.Session!;
            Assert.False(session.InCollege);
            var c = session.World.College!;
            var s = c.Season;
            int y = s.Year + 1;

            // H72: the Conferences tab opens on a conference this (real) world has, with its table.
            vm.Navigate("College");
            var college = (CollegeViewModel)vm.CurrentPage!;
            college.Tab = 1;
            Settle(window);
            Assert.Contains(college.Conference, college.ConferenceNames);
            Assert.NotEmpty(college.ConferenceRows);
            college.Conference = null!; // what the ComboBox pushed back before the fix
            Assert.Contains(college.Conference, college.ConferenceNames);
            Assert.NotEmpty(college.ConferenceRows);

            // Before Selection Sunday: no card.
            vm.Navigate("Home");
            var home = (HomeViewModel)vm.CurrentPage!;
            Assert.False(home.HasMadness);

            // Selection Sunday: the field, the four No. 1 seeds and seniors to watch.
            while (!s.Selected) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Home");
            home = (HomeViewModel)vm.CurrentPage!;
            Assert.True(home.HasMadness);
            Assert.Equal($"March Madness {y}", home.MadnessTitle);
            Assert.StartsWith("The field of 68 is set · next: the First Four", home.MadnessStage);
            Assert.Equal("The No. 1 seeds", home.MadnessResultsTitle);
            Assert.Equal(4, home.MadnessResults.Count);
            Assert.All(home.MadnessResults, r => Assert.Equal("1 SEED", r.Tag));
            Assert.Contains("68 teams left · 4 of 4 No. 1 seeds still in", home.MadnessSummary);
            Assert.Equal(3, home.MadnessProspects.Count);
            Snap(window, "madness-home-selection");

            // After the round of 32: the latest round's upsets (or its winners), the Sweet 16 next.
            while (!(s.Games.Count(g => g.Event == CollegeWorld.Madness && g.Round == 2 && g.Played) == 16)) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Home");
            home = (HomeViewModel)vm.CurrentPage!;
            Assert.StartsWith("Next: the Sweet 16", home.MadnessStage);
            Assert.StartsWith("16 teams left", home.MadnessSummary);
            Assert.Contains("round of 32", home.MadnessResultsTitle);
            Assert.InRange(home.MadnessResults.Count, 1, 4);
            Assert.All(home.MadnessProspects, p => Assert.DoesNotContain(s.Games.Where(g => g.Event == CollegeWorld.Madness && g.Played),
                g => g.WinnerId != null && (g.HomeId == g.WinnerId ? g.AwayId : g.HomeId) == p.TeamId));
            Snap(window, "madness-home-sweet16");

            // The Final Four set: the four teams, the host city.
            while (s.Games.Count(g => g.Event == CollegeWorld.Madness && g.Round == 4 && g.Played) < 4) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Home");
            home = (HomeViewModel)vm.CurrentPage!;
            Assert.Equal($"The Final Four in {MadnessBracket.FinalFourHost(y)}", home.MadnessResultsTitle);
            Assert.Equal(4, home.MadnessResults.Count);
            Assert.Contains($" in {MadnessBracket.FinalFourHost(y).Split(',')[0]}", home.MadnessStage);

            // The champions: their road with the final last, then the card goes a few days later.
            while (s.ChampionId == null) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Home");
            home = (HomeViewModel)vm.CurrentPage!;
            int champ = s.ChampionId!.Value;
            Assert.Equal($"Champions: {c.Program(champ)!.FullName}", home.MadnessStage);
            Assert.Equal("The champions' road", home.MadnessResultsTitle);
            Assert.Equal("FINAL", home.MadnessResults[^1].Tag);
            Assert.Equal(6, home.MadnessResults.Count);
            Assert.All(home.MadnessProspects, p => Assert.Contains(p.TeamId, new int?[] { champ, s.RunnerUpId }));
            Snap(window, "madness-home-champions");
            foreach (int width in new[] { 1480, 1280 })
            {
                window.Width = width;
                Settle(window);
                foreach (var p in LayoutCheck.Scan(window)) problems.Add($"{width}px Home with March Madness | {p}");
            }
            window.Width = 1480;

            // "Open the bracket" goes to College → March Madness.
            home.OpenBracketCommand.Execute(null);
            Assert.Equal(2, ((CollegeViewModel)vm.CurrentPage!).Tab);

            var final = s.Games.Where(g => g.Event == CollegeWorld.Madness && g.Played).Max(g => g.Date);
            while (session.Today <= final.AddDays(CollegeWorld.MadnessWatchDaysAfter)) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Home");
            Assert.False(((HomeViewModel)vm.CurrentPage!).HasMadness);

            // H73: after a job change the hero's crest is the new club's.
            var other = session.League.Teams.First(t => t != session.HumanTeam);
            session.Job.Vacancies.Add(new Vacancy { TeamId = other.Id, Opened = session.Today, Status = "offered", WhileEmployed = true });
            Assert.Null(session.AcceptOffer(other.Id));
            vm.AfterTick();
            vm.Navigate("Home");
            Settle(window);
            var hero = window.GetVisualDescendants().OfType<StackPanel>().First(p => p.Name == "HeroLeft");
            Assert.Same(other, hero.GetVisualDescendants().OfType<TeamLogo>().First().Team);
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
