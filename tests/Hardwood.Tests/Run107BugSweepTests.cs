using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.Model;
using Hardwood.Core.Names;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 107, the second bug sweep (Danny, #883). H79: no league abroad repeats a nickname, and Yokohama is the B-Corsairs;
/// a name pack written for an old name still finds the club. H57: a club in greys keeps a coloured accent. H77: a college
/// coach's spring with no games has one "Skip to the summer" button that plays the days through to the offseason.
/// </summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings and the accent, app-wide state
public class Run107BugSweepTests
{
    private readonly ITestOutputHelper _out;
    public Run107BugSweepTests(ITestOutputHelper o) => _out = o;

    private static string Key(string nickname) => new string(nickname.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    [Fact]
    public void No_league_abroad_repeats_a_nickname()
    {
        var problems = new List<string>();
        foreach (var p in Competitions.All.Where(p => p.Clubs.Length > 0 && !UsaPyramid.IsPyramid(p.Key) && !p.Legacy))
            foreach (var g in p.Clubs.GroupBy(c => Key(c.Name)).Where(g => g.Count() > 1))
                problems.Add($"{p.Name}: {string.Join(", ", g.Select(c => $"{c.City} {c.Name}"))}");
        Assert.Empty(problems);
        // Abbreviations stay unique per league too (old saves find their clubs' seeds by them).
        foreach (var p in Competitions.All.Where(p => p.Clubs.Length > 0 && !UsaPyramid.IsPyramid(p.Key)))
            Assert.Equal(p.Clubs.Length, p.Clubs.Select(c => c.Abbr).Distinct().Count());
        var japan = Competitions.Get(WorldLeagues.Japan).Clubs;
        Assert.Contains(japan, c => c.City == "Yokohama" && c.Name == "B-Corsairs" && c.Abbr == "YOK");
        Assert.DoesNotContain(japan, c => c.Name == "Bay Stars");
    }

    [Fact]
    public void A_name_pack_for_an_old_club_name_still_finds_the_club()
    {
        var seeds = Competitions.All.SelectMany(p => p.Clubs).Select(c => $"{c.City} {c.Name}").ToHashSet();
        foreach (var (now, before) in NamePack.FormerNames)
        {
            Assert.Contains(now, seeds);
            Assert.DoesNotContain(before, seeds);
        }
    }

    [AvaloniaFact]
    public void A_club_in_greys_keeps_a_coloured_accent()
    {
        Team T(string abbr, string a, string b) => new() { Id = 1, City = "X", Name = abbr, Abbreviation = abbr, Division = "", PrimaryColor = a, SecondaryColor = b };
        Assert.Equal(ThemeService.DefaultAccent, ThemeService.AccentFor(T("SAS", "#C4CED4", "#000000")));
        Assert.Equal(ThemeService.DefaultAccent, ThemeService.AccentFor(T("BKN", "#000000", "#FFFFFF")));
        // A club with one real colour keeps its own (Celtics green, Kings purple beside grey).
        Assert.NotEqual(ThemeService.DefaultAccent, ThemeService.AccentFor(T("BOS", "#007A33", "#BA9653")));
        Assert.NotEqual(ThemeService.DefaultAccent, ThemeService.AccentFor(T("SAC", "#5A2D81", "#63727A")));

        // On screen: a Spurs career's Home, with the active nav item and Continue in the accent.
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "SAS").Id, null);
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            vm.Navigate("Home");
            Assert.Equal(ThemeService.DefaultAccent, ThemeService.Accent);
            Snap(window, "run107-spurs-accent");
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            ThemeService.Apply(null);
        }
    }

    [AvaloniaFact]
    public void A_college_spring_has_one_button_to_the_summer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run107-{Guid.NewGuid():N}.save");
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
            start.SelectedTeam = start.Teams.First(t => t.Team.City == "Clemson");
            start.NewGame();
            var s = vm.Session!;
            Assert.True(s.InCollege);
            vm.AfterTick();
            Assert.False(vm.InCollegeSpring);
            Assert.Equal("Continue (1 day)", vm.ContinueText);
            int guard = 0;
            while (!MainViewModel.CollegeSpring(s) && s.Phase != SessionPhase.Offseason && guard++ < 260) s.AdvanceDay();
            _out.WriteLine($"Spring: {s.Today:MMM d yyyy}, phase {s.Phase}");
            Assert.NotEqual(SessionPhase.Offseason, s.Phase);
            vm.AfterTick();
            vm.Navigate("Home");
            Assert.True(vm.InCollegeSpring);
            Assert.Equal(MainViewModel.SkipToSummerText, vm.ContinueText);
            Assert.False(vm.ShowSimButtons);
            Assert.False(vm.ShowNextGameButton);
            var home = (HomeViewModel)vm.CurrentPage!;
            Assert.Equal("Season over", home.NextGame);
            Assert.Contains("Skip to the summer takes you there", home.NextDetail);
            Settle(window);
            foreach (var p in LayoutCheck.Scan(window)) problems.Add($"1480px Home college spring | {p}");
            Snap(window, "run107-college-spring-skip");

            // One press: the days play through to the offseason.
            var task = vm.ContinueCommand.ExecuteAsync(null);
            for (int i = 0; i < 100_000 && !task.IsCompleted; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            Assert.True(task.IsCompleted);
            Assert.Equal(SessionPhase.Offseason, s.Phase);
            Assert.False(vm.InCollegeSpring);
            Assert.IsType<OffseasonViewModel>(vm.CurrentPage);
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            ThemeService.Apply(null);
            try { File.Delete(path); } catch { }
        }
        Assert.Empty(problems);
    }

    private static void Settle(MainWindow window)
    {
        for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
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
