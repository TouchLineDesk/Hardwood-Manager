using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 63: custom views on the big tables (Danny, #465; Phase 3, #653). A View button above Roster, the
/// transfer market, free agents, League Leaders and a team page's roster: ready-made views, a tick box per column,
/// "My view" remembered per table in settings.json.</summary>
[Collection(SharedUiState.Name)]   // run 65: it swaps AppSettings, app-wide state (raced PortraitServiceTests in CI)
public class Run63ViewTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Tables_offer_views_and_remember_my_view()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var path = Path.Combine(Path.GetTempPath(), $"hw-run63-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        try
        {
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            ColumnPicker Picker() => window.GetVisualDescendantsOf<ColumnPicker>().Single(p => p.IsEffectivelyVisible);
            List<string> Shown(ColumnPicker p) => p.Target!.Columns.Where(c => c.IsVisible).Select(ColumnPicker.HeaderOf).ToList();

            // Roster: all columns by default, exactly as before.
            vm.Navigate("Roster"); ((RosterViewModel)vm.CurrentPage!).CardView = false; Settle();
            var roster = Picker();
            Assert.Equal("roster", roster.Key);
            Assert.Equal(ColumnPicker.AllName, roster.Current);
            var all = roster.Target!.Columns.Count;
            Assert.Equal(all, Shown(roster).Count);
            Assert.Equal(new[] { "All columns", "Overview", "Ratings", "Stats", "Contracts" }, roster.Views.Select(v => v.Name));
            // Every ready-made view names real columns (a typo would silently drop one).
            foreach (var (name, cols) in roster.Views) Assert.True(cols.Count >= 4, name);

            // A ready-made view.
            roster.Choose("Ratings"); Settle();
            Assert.Equal(new[] { "Player", "Pos", "Age", "Tier", "OVR", "CA", "PA", "Sho", "Ply", "Def", "Men", "Phy" }, Shown(roster));
            Assert.Equal($"View: Ratings (12 of {all} columns) ▾", (string)window.GetVisualDescendantsOf<Avalonia.Controls.Primitives.ToggleButton>()
                .First(b => (b.Content as string ?? "").StartsWith("View:")).Content!);

            // The panel: chips and tick boxes; unticking a column makes it My view.
            var toggle = window.GetVisualDescendantsOf<Avalonia.Controls.Primitives.ToggleButton>().First(b => (b.Content as string ?? "").StartsWith("View:"));
            toggle.IsChecked = true; Settle();
            var boxes = roster.GetVisualDescendantsOf<Avalonia.Controls.CheckBox>().Where(b => b.IsEffectivelyVisible).ToList();
            Assert.Equal(all - 1, boxes.Count);   // every column but Player
            Assert.DoesNotContain(boxes, b => (string)b.Content! == "Player");
            boxes.Single(b => (string)b.Content! == "Men").IsChecked = false; Settle();
            Assert.Equal(ColumnPicker.MineName, roster.Current);
            Assert.DoesNotContain("Men", Shown(roster));
            roster.SetShown("PPG", true); Settle();
            Assert.Contains("PPG", Shown(roster));
            Assert.Contains(roster.Views, v => v.Name == ColumnPicker.MineName);

            // Remembered: in settings.json, and when the page is built again.
            var reloaded = AppSettings.Load();
            Assert.Equal(ColumnPicker.MineName, reloaded.TableViews["roster"].View);
            Assert.Contains("PPG", reloaded.TableViews["roster"].Shown);
            Assert.DoesNotContain("Men", reloaded.TableViews["roster"].Shown);
            vm.Navigate("Home"); Settle();
            vm.Navigate("Roster"); ((RosterViewModel)vm.CurrentPage!).CardView = false; Settle();
            Assert.Equal(ColumnPicker.MineName, Picker().Current);
            Assert.DoesNotContain("Men", Shown(Picker()));
            // A ready-made view and back to My view keeps your columns.
            Picker().Choose("Stats"); Settle();
            Assert.Equal(new[] { "Player", "Pos", "MPG", "PPG", "RPG", "APG", "FG%" }, Shown(Picker()));
            Picker().Choose(ColumnPicker.MineName); Settle();
            Assert.Contains("PPG", Shown(Picker()));
            // Unticking every box leaves only Player (not every column back).
            foreach (var c in Picker().Choosable.ToList()) Picker().SetShown(ColumnPicker.HeaderOf(c), false);
            Settle();
            Assert.Equal(ColumnPicker.MineName, Picker().Current);
            Assert.Equal(new[] { "Player" }, Shown(Picker()));
            Picker().Choose(ColumnPicker.AllName); Settle();
            Assert.Equal(all, Shown(Picker()).Count);

            // League Leaders: the rank and Player always show.
            vm.Navigate("League Leaders"); ((LeadersViewModel)vm.CurrentPage!).View = "Full table"; Settle();   // cards first since run 64
            var leaders = Picker();
            leaders.Choose("Scoring"); Settle();
            var shown = Shown(leaders);
            Assert.Equal("#", shown[0]);
            Assert.Contains("Player", shown);
            Assert.DoesNotContain("RPG", shown);
            leaders.SetShown("PPG", false); Settle();
            Assert.Contains("Player", Shown(leaders));

            // The transfer market and free agents have their own views, kept apart from Roster's.
            vm.Navigate("Transactions"); ((TransactionsViewModel)vm.CurrentPage!).MarketView = "Full table"; Settle();   // cards first since run 65
            var market = Picker();
            Assert.Equal("market", market.Key);
            Assert.Equal(ColumnPicker.AllName, market.Current);
            market.Choose("The deal"); Settle();
            Assert.Contains("Likely fee", Shown(market));
            Assert.DoesNotContain("Club", Shown(market));
            Assert.Contains("", Shown(market));   // the unnamed status mark always shows
            var tx = (TransactionsViewModel)vm.CurrentPage!;
            tx.Tab = 3; Settle();
            var fa = window.GetVisualDescendantsOf<ColumnPicker>().Single(p => p.Key == "freeagents");
            fa.Choose("Quick look"); Settle();
            Assert.Equal(new[] { "Player", "Pos", "Age", "OVR", "Status" }, Shown(fa));

            // A team page's roster.
            vm.ShowTeam(s.League.Teams.First(t => t != s.HumanTeam)); Settle();
            var team = window.GetVisualDescendantsOf<ColumnPicker>().Single(p => p.Key == "team-roster");
            team.Choose("Stats"); Settle();
            Assert.Equal(new[] { "Player", "Pos", "G", "PPG", "RPG", "APG", "" }, Shown(team));
            window.Close();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
        }
    }

    [Fact]
    public void A_repeated_offer_says_so()
    {
        Assert.Equal("He wants more.", TalkViewModel.NumberedReply(1, "He wants more.", ""));
        Assert.Equal("Offer 2, same answer: He wants more.", TalkViewModel.NumberedReply(2, "He wants more.", "He wants more."));
        Assert.Equal("Offer 3: Deal!", TalkViewModel.NumberedReply(3, "Deal!", "He wants more."));
    }

    [Fact]
    public void Old_settings_files_load_without_table_views()
    {
        var file = Path.Combine(Path.GetTempPath(), $"hw-settings-old-{Guid.NewGuid():N}.json");
        var oldPath = AppSettings.FilePath;
        try
        {
            File.WriteAllText(file, "{ \"UiScale\": 1.1, \"GuideOn\": true }");
            AppSettings.FilePath = file;
            var s = AppSettings.Load();
            Assert.Equal(1.1, s.UiScale);
            Assert.Empty(s.TableViews);
        }
        finally { AppSettings.FilePath = oldPath; File.Delete(file); }
    }
}
