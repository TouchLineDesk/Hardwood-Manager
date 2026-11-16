using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 62: D63 A's assistant panel (task cards only, the page tip and strategy behind a Tips button) and the
/// last Low findings on `claude/bug-hunt.md`: #13 (panels bound to a null selection), #14 (the Squad panel's morale
/// wrapping mid-number), H2 (the assistant's buttons silent when nothing changes) and H3 (a leader who has left the
/// game was a link that did nothing). H4 is the bug finder's own fix (it opens the live game's Tactics tab).</summary>
[Collection(SharedUiState.Name)]   // run 65: it swaps AppSettings, app-wide state (raced PortraitServiceTests in CI)
public class Run62FixTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_assistant_panel_keeps_tips_behind_a_button_and_the_assistant_s_buttons_say_when_nothing_changed()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var path = Path.Combine(Path.GetTempPath(), $"hw-run62-ui-{Guid.NewGuid():N}.hwsave");
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
            bool Shows(string text) => window.GetVisualDescendantsOf<Avalonia.Controls.TextBlock>()
                .Any(t => t.IsEffectivelyVisible && (t.Text ?? "").Contains(text));
            Avalonia.Controls.Button? Button(string startsWith) => window.GetVisualDescendantsOf<Avalonia.Controls.Button>()
                .FirstOrDefault(b => b.IsEffectivelyVisible && (b.Content as string ?? "").StartsWith(startsWith));

            // D63 A: on Home the panel shows task cards; the tip and strategy wait behind "Tips (N)".
            vm.Navigate("Home"); Settle();
            var g = vm.Guide;
            Assert.True(g.Expanded);
            Assert.True(g.HasTips);
            Assert.False(g.ShowPageTip);
            Assert.False(g.ShowStrategy);
            Assert.False(Shows("ON THIS PAGE"));
            Assert.False(Shows("STRATEGY"));
            Assert.True(g.Tasks.Count > 0 || g.NoTasks);
            var tips = Button("Tips (");
            Assert.NotNull(tips);
            Assert.Matches(@"^Tips \(\d+\)$", (string)tips!.Content!);
            g.ToggleTipsCommand.Execute(null); Settle();
            Assert.True(g.ShowPageTip);
            Assert.True(Shows("ON THIS PAGE"));
            Assert.NotNull(Button("Hide tips"));
            // It stays open while you move between pages, and closes again on a second press.
            vm.Navigate("Inbox"); Settle();
            Assert.True(g.TipsOpen);
            g.ToggleTipsCommand.Execute(null); Settle();
            Assert.False(Shows("ON THIS PAGE"));

            // #13: no selection shows a hint, not a binding error.
            vm.Navigate("Tactics"); Settle();
            var tac = (TacticsViewModel)vm.CurrentPage!;
            Assert.False(string.IsNullOrEmpty(tac.SystemDescription));
            tac.SelectedSystem = null; tac.SelectedScheme = null; Settle();
            Assert.Equal("Pick an offensive system above.", tac.SystemDescription);
            Assert.Equal("Pick a defensive scheme above.", tac.SchemeDescription);
            Assert.False(tac.HasSystemReasons);
            tac.Refresh(); Settle();

            // H2: pressed twice, the second press says nothing changed.
            tac.AssistantSetup();
            tac.AssistantSetup();
            Assert.StartsWith("Already the assistant's set-up", tac.Status);
            if (s.NextMyGame != null)
            {
                tac.UseSuggestion();
                tac.UseSuggestion();
                Assert.StartsWith("Already the assistant's game plan", tac.Status);
            }
            vm.Navigate("Rotation"); Settle();
            var rot = (RotationViewModel)vm.CurrentPage!;
            rot.AutoRotation();
            rot.AutoRotation();
            Assert.StartsWith("Already the assistant's rotation", rot.Status);

            // #14: the morale score is its own unwrapped text.
            vm.Navigate("Squad"); Settle();
            var squad = (SquadViewModel)vm.CurrentPage!;
            Assert.Matches(@"^\d+/100$", squad.PlayerScore);
            Assert.DoesNotContain("/100", squad.PlayerHeadline);

            // H3: a name with no player behind it is greyed out with the reason.
            var link = new PlayerLink { Text = "(left the game)" };
            Assert.False(link.IsEnabled);
            Assert.Equal("He has left the game world, so there's no page to open.", Avalonia.Controls.ToolTip.GetTip(link) as string);
            link.Player = s.HumanTeam.Roster[0];
            Assert.True(link.IsEnabled);
            Assert.Null(Avalonia.Controls.ToolTip.GetTip(link));
            window.Close();
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
        }
    }
}
