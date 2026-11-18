using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;
using Xunit.Abstractions;

namespace Hardwood.Tests;

/// <summary>
/// Run 106, the first bug sweep (Danny, #883): the bug hunter's quick Mediums and Lows in one PR.
/// H71 live game's top bar and controls after the final; H31 L10 in quick-simmed leagues; H87 duplicate nicknames in
/// brackets and leader cards; H69 a relegated club's summer; H90 the promotion playoff in the top bar; H60 tabs at big
/// text; H82 suggested setups; H92 job history; H83 offers chip (Run79); H54 level records in the summer news.
/// </summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run106BugSweepTests
{
    private readonly ITestOutputHelper _out;
    public Run106BugSweepTests(ITestOutputHelper o) => _out = o;

    [Fact]
    public void The_top_bar_talks_about_the_game_on_screen_and_its_result()
    {
        Assert.Equal("Now: vs GSW · Q2", LiveGameViewModel.TopBarLine(true, "GSW", "Q2", false, 50, 48));
        Assert.Equal("Now: at GSW · OT1", LiveGameViewModel.TopBarLine(false, "GSW", "OT1", false, 100, 100));
        Assert.Equal("Final: W 112-104 vs GSW", LiveGameViewModel.TopBarLine(true, "GSW", "Q4", true, 112, 104));
        Assert.Equal("Final: L 99-101 at GSW", LiveGameViewModel.TopBarLine(false, "GSW", "Q4", true, 99, 101));
    }

    [Fact]
    public void Last_ten_comes_from_a_league_books_regular_season()
    {
        var a = new Team { Id = 1, City = "A", Name = "Ants", Abbreviation = "AAA", Division = "" };
        var b = new Team { Id = 2, City = "B", Name = "Bees", Abbreviation = "BBB", Division = "" };
        var d = new DateOnly(2027, 1, 1);
        var games = new List<BookGame>();
        // a wins 7 of 12 (the last 10: 7-3), then a playoff game that doesn't count, and an unplayed one.
        bool[] aWins = { false, false, true, true, true, false, true, false, true, false, true, true };
        for (int i = 0; i < aWins.Length; i++)
            games.Add(new BookGame(d.AddDays(i), i % 2 == 0 ? a : b, i % 2 == 0 ? b : a,
                (i % 2 == 0) == aWins[i] ? 100 : 90, (i % 2 == 0) == aWins[i] ? 90 : 100, CompetitionSeason.RegularSeason, null));
        games.Add(new BookGame(d.AddDays(20), a, b, 80, 100, "Playoffs", null));
        games.Add(new BookGame(d.AddDays(21), a, b, null, null, CompetitionSeason.RegularSeason, null));
        Assert.Equal("7-3", StandingsViewModel.Last10Of(a, games));
        Assert.Equal("3-7", StandingsViewModel.Last10Of(b, games));
        Assert.Equal("", StandingsViewModel.Last10Of(new Team { Id = 3, City = "C", Name = "Cats", Abbreviation = "CCC", Division = "" }, games));
    }

    [Fact]
    public void A_shared_nickname_is_never_shown_alone()
    {
        var hou = new Team { Id = 1, City = "Houston", Name = "Mustangs", Abbreviation = "HOU", Division = "" };
        var ind = new Team { Id = 2, City = "Indianapolis", Name = "Mustangs", Abbreviation = "IND", Division = "" };
        var bal = new Team { Id = 3, City = "Baltimore", Name = "Basketball Club", Abbreviation = "BAL", Division = "" };
        var sea1 = new Team { Id = 4, City = "Seattle", Name = "Highlanders Basketball", Abbreviation = "SE2", Division = "" };
        var sea2 = new Team { Id = 5, City = "Seattle", Name = "Highlanders Basketball", Abbreviation = "SEA", Division = "" };
        var peers = new[] { hou, ind, bal, sea1, sea2 };
        Assert.Equal("Houston Mustangs", BracketView.Label(hou, peers));   // short enough: the full name
        Assert.Equal("Indianapolis", BracketView.Label(ind, peers));       // was "Mustangs"
        Assert.Equal("Mustangs", BracketView.Label(ind));                  // without peers, the long name's nickname
        Assert.Equal("Baltimore", BracketView.Label(bal, peers));          // was "Basketball Club"
        Assert.Equal("SE2 Highlanders Basketball", BracketView.Label(sea1, peers));
        var lone = new Team { Id = 6, City = "San Antonio", Name = "Trail Blazers", Abbreviation = "SAT", Division = "" };
        Assert.Equal("Trail Blazers", BracketView.Label(lone, peers));
        // The leader card's club line follows the same rule.
        var card = new LeaderCard { Entries = new[] { new LeaderEntry { Rank = 1, Name = "X", Team = ind } }, Peers = peers };
        Assert.Equal("Indianapolis", card.TopClub);
    }

    [Fact]
    public void Suggested_setups_never_repeat_a_system_and_scheme()
    {
        foreach (ulong seed in new ulong[] { 31, 77, 2026 })
        {
            var lg = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed).League;
            foreach (var us in lg.Teams.Take(12))
            {
                var them = lg.Teams.First(t => t != us);
                foreach (var setups in new[] { TacticSetups.Suggest(us, them, null), TacticSetups.Suggest(us, null, null) })
                {
                    Assert.Equal(setups.Count, setups.Select(s => (s.System, s.Scheme)).Distinct().Count());
                    Assert.Single(setups, s => s.Recommended);
                    Assert.True(setups.Count >= 5, $"{us.Abbreviation}: {setups.Count} cards");
                }
            }
        }
    }

    [Fact]
    public void Job_history_names_the_season_of_the_record()
    {
        Assert.Equal("New Orleans Pelicans, 2026-27: sacked at 6-20",
            Hardwood.Core.Jobs.JobRecords.Line(new Hardwood.Core.Jobs.JobRecord(1, "New Orleans Pelicans", 2026, 2026, 6, 20, "Sacked")));
        Assert.Equal("Providence City, 2027-28 to 2028-29: left for Boston Celtics (40-42 in 2028-29)",
            Hardwood.Core.Jobs.JobRecords.Line(new Hardwood.Core.Jobs.JobRecord(1, "Providence City", 2027, 2028, 40, 42, "Left for Boston Celtics")));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_club_going_down_is_told_where_it_plays_and_what_its_contracts_come_to()
    {
        // nba-rebuild (seed 77): the weakest NBA club, to the summer.
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(77);
        var teamId = probe.Teams.OrderBy(t => QuickSim.Strength(t)).First().Id;
        var s = GameSession.NewGame(77, teamId, null);
        int guard = 0;
        while (s.Phase != SessionPhase.Offseason && guard++ < 700) s.AdvanceDay();
        Assert.Equal(SessionPhase.Offseason, s.Phase);
        var outlook = s.RelegationOutlook();
        _out.WriteLine($"{s.HumanTeam.FullName}: going down {s.GoingDown()}, outlook {outlook}");
        if (!s.GoingDown()) { Assert.Null(outlook); return; }
        Assert.NotNull(outlook);
        Assert.Equal(Competitions.Get(UsaPyramid.Tier2).Name, outlook!.Value.League);
        long committed = s.MyCommittedWages;
        Assert.InRange(outlook.Value.PayrollAfter, committed / 2 - 1000 * s.HumanTeam.Roster.Count, committed);
        // The page's line names the league and the cut payroll (the view model reads the shell's session).
        Assert.Contains("Going down", OffseasonLine(s));
    }

    private static string OffseasonLine(GameSession s)
    {
        var shell = new MainViewModel();
        shell.AttachSession(s);
        var page = new OffseasonViewModel(shell);
        page.Refresh();
        return page.RelegationText;
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void After_the_final_the_live_screen_shows_the_result_and_locks_the_lineup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run106-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false, UiScale = 1.3 });
        MainWindow? window = null;
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "SAS").Id, path);
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            var opp = s.League.Teams.First(t => t != s.HumanTeam);
            var game = new GameSimulator().StartLive(s.HumanTeam, opp, s.League.Rules, 77, s.Today);
            for (int i = 0; i < 115; i++) game.Step();
            var live = new LiveGameViewModel(vm, game, new());
            vm.CurrentPage = live; vm.InLiveGame = true;
            void Settle() { for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            Settle();
            Assert.StartsWith($"Now: vs {opp.Abbreviation} · Q", vm.NextGameText);
            Assert.Equal("LIVE", live.TickerTag);

            // H60: at 130% the live court's tabs sit on one row.
            var view = window.GetVisualDescendants().OfType<LiveGameView>().Single(v => v.IsEffectivelyVisible);
            var tabs = view.GetVisualDescendants().OfType<TabItem>().Where(t => t.IsEffectivelyVisible).ToList();
            Assert.True(tabs.Count >= 3);
            Assert.Single(tabs.Select(t => Math.Round(t.TranslatePoint(new Avalonia.Point(0, 0), view)!.Value.Y)).Distinct());

            live.SimToEnd();
            Settle();
            Assert.StartsWith("Final: ", vm.NextGameText);
            Assert.Contains(opp.Abbreviation, vm.NextGameText);
            Assert.Equal("FINAL", live.TickerTag);
            // The lineup editor is read-only, the win chance chip is gone, and Continue is the one primary button.
            var apply = view.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Apply lineup");
            Assert.False(apply.IsEffectivelyEnabled);
            // (The court/flow switch marks its picked view with the same class; the game's controls are the scoreboard's buttons.)
            var controls = view.GetVisualDescendants().OfType<StackPanel>().Single(p => p.Name == "ScoreButtons");
            var primaries = controls.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primary") && b.IsEffectivelyVisible).ToList();
            Assert.Single(primaries);
            Assert.Equal("Continue", primaries[0].Content as string);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible), t => t.Inlines?.Any(i => i is Avalonia.Controls.Documents.Run r && r.Text == "Win chance ") == true);
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_court_cards_keep_their_spots_and_an_empty_slot_binds_nothing()
    {
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
            vm.Navigate("Rotation");
            var rot = Assert.IsType<RotationViewModel>(vm.CurrentPage);
            rot.Starters[2].Row = null;   // an empty slot (H27)
            for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            var slots = window.GetVisualDescendants().OfType<ItemsControl>().First(c => c.Name == "CourtSlots" && c.IsEffectivelyVisible);
            var lefts = slots.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Where(p => p.DataContext is StarterSlot && p.GetVisualParent() is Canvas)
                .Select(p => (Canvas.GetLeft(p), Canvas.GetTop(p))).ToList();
            Assert.Equal(5, lefts.Count);
            Assert.All(lefts, x => Assert.False(double.IsNaN(x.Item1) || double.IsNaN(x.Item2)));
            Assert.Equal(5, lefts.Distinct().Count());
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
        }
    }
}
