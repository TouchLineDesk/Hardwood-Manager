using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 77 (review #13 rank 14): Leagues → Rules &amp; format shows an icon per rule. H21 (bug hunt): a pyramid
/// league's division cards are headed by its own conferences, not "NBA". H23 (bug hunt): once the season is over the
/// Board shows the finished record against the target instead of a pace below the wins already banked.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run77RulesIconTests
{
    private static (GameSession s, MainViewModel vm, MainWindow window, Action settle) Open(string path, Func<GameWorld, Hardwood.Core.Model.League, int> pick, ulong seed = 2026)
    {
        var (probe, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed);
        var s = GameSession.NewGame(seed, pick(world, probe), path);
        var vm = new MainViewModel();
        var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
        window.Show();
        vm.AttachSession(s);
        void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
        return (s, vm, window, Settle);
    }

    private static void Snap(MainWindow window, Action settle, string name)
    {
        if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is not { Length: > 0 } dir) return;
        settle();
        using var f = window.CaptureRenderedFrame();
        f?.Save(Path.Combine(dir, name));
    }

    private static T WithSettings<T>(Func<T> body)
    {
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        try { return body(); }
        finally { AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings); }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Every_rule_label_the_game_writes_has_its_own_icon()
    {
        foreach (var label in new[] { "Level", "Game", "Season", "Playoffs", "March Madness", "Rosters", "Money", "Wages", "Format", "Imports", "Pro age", "Recruiting", "Simulation" })
        {
            var icon = RuleRow.IconFor(label);
            Assert.NotEqual("ClipboardList", icon);
            Assert.NotNull(Icon.GeometryFor(icon));
        }
        Assert.NotNull(Icon.GeometryFor(RuleRow.IconFor("Something new")));   // the fallback draws too
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Rules_and_format_draws_an_icon_beside_every_rule_and_reads_in_full()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run77-{Guid.NewGuid():N}.hwsave");
        MainWindow? window = null;
        WithSettings(() =>
        {
            try
            {
                var (s, vm, w, settle) = Open(path, (_, lg) => lg.Teams.First(t => t.Abbreviation == "BOS").Id);
                window = w;
                vm.Navigate("Leagues"); settle();
                var page = (WorldViewModel)vm.CurrentPage!;
                Assert.NotEmpty(page.Rules);
                var view = window.GetVisualDescendants().OfType<WorldView>().First(v => v.DataContext == page && v.Bounds.Width > 0);
                page.Tab = 3;   // Rules & format
                settle();
                var icons = view.GetVisualDescendants().OfType<Icon>().Where(i => i.IsEffectivelyVisible && i.DataContext is RuleRow).ToList();
                Assert.Equal(page.Rules.Count, icons.Count);
                Assert.All(icons, i => Assert.NotNull(Icon.GeometryFor(i.Kind)));
                Assert.Contains(icons, i => i.Kind == "Trophy");
                var problems = LayoutCheck.Scan(window);
                Assert.True(problems.Count == 0, "Rules & format:\n" + string.Join("\n", problems));
                Snap(window, settle, "run77-rules.png");
                return 0;
            }
            finally { window?.Close(); if (File.Exists(path)) File.Delete(path); }
        });
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_pyramid_leagues_division_cards_are_headed_by_its_own_conferences_not_the_NBA()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run77b-{Guid.NewGuid():N}.hwsave");
        MainWindow? window = null;
        WithSettings(() =>
        {
            try
            {
                var (s, vm, w, settle) = Open(path, (world, _) => world[UsaPyramid.Tier5].Clubs.OrderBy(t => QuickSim.Strength(t)).First().Id, 9);
                window = w;
                vm.Navigate("Standings"); settle();
                var st = (StandingsViewModel)vm.CurrentPage!;
                Assert.Equal("By division", st.View);
                Assert.True(st.ShowCards);
                Assert.NotEmpty(st.RightCards);
                var league = s.HumanLeague!.Profile.Name;
                Assert.DoesNotContain(Hardwood.Core.Names.Brand.Top, st.RightTitle);
                Assert.StartsWith(league, st.LeftTitle);
                Assert.StartsWith(league, st.RightTitle);
                Assert.NotEqual(st.LeftTitle, st.RightTitle);
                // Each title names the conference of the cards under it.
                string conf(DivisionCard c) => c.Title.Split(" · ")[0];
                Assert.All(st.LeftCards, c => Assert.Contains(StandingsViewModel.ConfName(conf(c)), st.LeftTitle));
                Assert.All(st.RightCards, c => Assert.Contains(StandingsViewModel.ConfName(conf(c)), st.RightTitle));
                Snap(window, settle, "run77-pyramid-divisions.png");
                var problems = LayoutCheck.Scan(window);
                Assert.True(problems.Count == 0, "Standings by division:\n" + string.Join("\n", problems));

                // The table view still puts the NBA on the right (that list is the NBA's).
                st.View = "By conference"; settle();
                Assert.Equal(Hardwood.Core.Names.Brand.Top, st.RightTitle);
                Assert.Equal(league, st.LeftTitle);
                return 0;
            }
            finally { window?.Close(); if (File.Exists(path)) File.Delete(path); }
        });
    }

    [Fact]
    public void The_boards_season_line_paces_in_season_and_states_the_finished_record_after_it()
    {
        Assert.Equal("The season hasn't started.", BoardViewModel.PaceLine(0, 0, 30, 22, false));
        Assert.Equal("10-5: on pace for 20 wins of 30", BoardViewModel.PaceLine(10, 5, 30, 22, false));
        // H23: Clemson, 23-8 with the ACC tournament and March Madness: no more "on pace for 22 wins".
        Assert.Equal("Finished 23-8 (target: around 22 wins): met", BoardViewModel.PaceLine(23, 8, 30, 22, true));
        Assert.Equal("Finished 23-8 (target: around 22 wins): met", BoardViewModel.PaceLine(23, 8, 30, 22, false));
        Assert.Equal("Finished 40-42 (target: around 45 wins): missed by 5", BoardViewModel.PaceLine(40, 42, 82, 45, true));
        Assert.Equal("Finished 40-42", BoardViewModel.PaceLine(40, 42, 82, null, true));
        // A pace is never below the wins already banked.
        for (int w = 0; w <= 35; w++)
            for (int l = 0; l + w <= 35 && l + w > 0; l++)
            {
                var line = BoardViewModel.PaceLine(w, l, 30, 22, false);
                if (line.Contains("on pace for ")) Assert.True(int.Parse(line.Split("on pace for ")[1].Split(' ')[0]) >= w, line);
            }
    }
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void In_the_summer_Homes_payroll_bars_start_from_next_season()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run77c-{Guid.NewGuid():N}.hwsave");
        MainWindow? window = null;
        WithSettings(() =>
        {
            try
            {
                var (s, vm, w, settle) = Open(path, (_, lg) => lg.Teams.First(t => t.Abbreviation == "SAS").Id);
                window = w;
                vm.Navigate("Home"); settle();
                var home = (HomeViewModel)vm.CurrentPage!;
                int season = s.League.Season;
                Assert.StartsWith($"{season}-", home.Payroll![0].Label);   // in season: this season first
                Assert.False(HomeViewModel.SummerCounted(s, s.HumanTeam));

                int guard = 0;
                while (s.Phase != SessionPhase.Offseason && guard++ < 400) s.AdvanceDay();
                Assert.Equal(SessionPhase.Offseason, s.Phase);
                Assert.Equal(season, s.League.Season);
                Assert.True(HomeViewModel.SummerCounted(s, s.HumanTeam));
                home.Refresh(); settle();
                // H25: the contracts have counted down, so the first bar is next season's and holds the deals that run into it.
                Assert.StartsWith($"{season + 1}-", home.Payroll![0].Label);
                long next = s.HumanTeam.Roster.Where(p => p.Contract is { Years: > 0 }).Sum(p => p.Contract!.Salary);
                Assert.Equal(next / 1e6, home.Payroll![0].Value, 3);
                Snap(window, settle, "run77-home-summer.png");
                return 0;
            }
            finally { window?.Close(); if (File.Exists(path)) File.Delete(path); }
        });
    }
}
