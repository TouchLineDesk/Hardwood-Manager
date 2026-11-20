using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 60, D74 B (#635): the career saves every 7 days, at every phase change, after your decisions, on
/// Settings' Save now and when you leave (main menu or closing the window), instead of after every day.</summary>
[Collection(SharedUiState.Name)] // touches app settings or the portrait service (run 131: they raced PortraitServiceTests)
public class Run60SaveTests
{
    private static int Club(string abbr)
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        return probe.Teams.First(t => t.Abbreviation == abbr).Id;
    }

    private static string TempSave() => Path.Combine(Path.GetTempPath(), $"hw-run60-{Guid.NewGuid():N}.hwsave");

    private static void Clean(params string[] paths)
    {
        foreach (var p in paths)
            foreach (var f in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(p) + "*"))
                try { File.Delete(f); } catch (IOException) { }
    }

    [Fact]
    public void The_day_loop_saves_every_seven_days_and_at_each_phase_change()
    {
        var path = TempSave();
        try
        {
            var s = GameSession.NewGame(2026, Club("PHX"), path);
            Assert.Equal(s.Today, s.LastSaved); // a new career is saved at once
            var saves = new List<DateOnly> { s.LastSaved };
            for (int i = 0; i < 45; i++)
            {
                var phase = s.Phase; var before = s.LastSaved;
                s.AdvanceDay();
                Assert.InRange(s.Today.DayNumber - s.LastSaved.DayNumber, 0, GameSession.SaveEveryDays - 1);
                if (s.Phase != phase) Assert.Equal(s.Today, s.LastSaved); // the preseason → the regular season
                if (s.LastSaved != before) saves.Add(s.LastSaved);
            }
            // 45 days: about one save a week (6–8), not 45.
            Assert.InRange(saves.Count, 6, 9);
            for (int i = 1; i < saves.Count; i++) Assert.InRange(saves[i].DayNumber - saves[i - 1].DayNumber, 1, GameSession.SaveEveryDays);
        }
        finally { Clean(path); }
    }

    [Fact]
    public void A_crash_loses_only_the_days_since_the_last_save_and_the_career_plays_on()
    {
        var path = TempSave(); string? copy = null;
        try
        {
            var s = GameSession.NewGame(2026, Club("PHX"), path);
            for (int i = 0; i < 11; i++) s.AdvanceDay();
            Assert.NotEqual(s.Today, s.LastSaved); // mid-week: the last few days aren't on disk yet
            // A crash now: the file holds the career as of the last save.
            copy = Path.Combine(Path.GetTempPath(), $"hw-run60-copy-{Guid.NewGuid():N}.hwsave");
            File.Copy(path, copy);
            var loaded = GameSession.Load(copy);
            Assert.Equal(s.LastSaved, loaded.Today);
            Assert.Equal(loaded.Today, loaded.LastSaved);
            Assert.InRange(s.Today.DayNumber - loaded.Today.DayNumber, 1, GameSession.SaveEveryDays - 1);
            for (int i = 0; i < 10; i++) loaded.AdvanceDay();
            Assert.InRange(loaded.Today.DayNumber - loaded.LastSaved.DayNumber, 0, GameSession.SaveEveryDays - 1);

            // An explicit save (Settings' Save now, quitting) writes today.
            s.SaveNow();
            Assert.Equal(s.Today, s.LastSaved);
            var copy2 = copy + "-2.hwsave"; File.Copy(path, copy2);
            try { Assert.Equal(s.Today, GameSession.Load(copy2).Today); }
            finally { Clean(copy2); }
        }
        finally { Clean(path, copy ?? "x-none"); }
    }

    [Fact]
    public void A_career_without_a_save_file_never_claims_to_be_saved()
    {
        var s = GameSession.NewGame(2026, Club("PHX"), savePath: null);
        for (int i = 0; i < 9; i++) s.AdvanceDay();
        Assert.Equal(default, s.LastSaved);
        Assert.Equal("Not saved", MainViewModel.SaveLine(s).text);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_top_bar_shows_the_last_save_and_the_main_menu_and_Save_now_save()
    {
        var path = TempSave();
        try
        {
            var s = GameSession.NewGame(2026, Club("BOS"), path);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            bool Shows(string text) => window.GetVisualDescendantsOf<Avalonia.Controls.TextBlock>()
                .Any(t => t.IsEffectivelyVisible && (t.Text ?? "") == text);
            Settle();
            Assert.Equal("Saved today", vm.SavedText);
            Assert.True(Shows("Saved today"));

            for (int i = 0; i < 3; i++) s.AdvanceDay();
            var lastSave = s.LastSaved;
            vm.RefreshHeader(); Settle();
            if (lastSave != s.Today) Assert.True(Shows($"Saved {lastSave:MMM d}"), vm.SavedText);

            // Settings' Save now.
            vm.Navigate("Settings"); Settle();
            var settings = (SettingsViewModel)vm.CurrentPage!;
            Assert.True(settings.CanSave);
            Assert.Contains("every 7 days", settings.SaveNote);
            var save = window.GetVisualDescendantsOf<Avalonia.Controls.Button>().First(b => b.IsEffectivelyVisible && (b.Content as string) == "Save now");
            save.Command!.Execute(null); Settle();
            Assert.Equal(s.Today, s.LastSaved);
            Assert.Equal("Saved today", vm.SavedText);

            // The main menu saves before leaving the career.
            s.AdvanceDay(); s.AdvanceDay();
            var today = s.Today;
            Assert.NotEqual(today, s.LastSaved);
            vm.BackToStart(); Settle();
            Assert.Equal(today, s.LastSaved);
            var copy = path + "-copy.hwsave"; File.Copy(path, copy);
            try { Assert.Equal(today, GameSession.Load(copy).Today); }
            finally { Clean(copy); }
            window.Close();
        }
        finally { Clean(path); }
    }
}
