using Hardwood.Core.Season;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 59, the play-test kit (D76 B, #632): the first-season empty states and silent buttons left on
/// `claude/bug-hunt.md`: #11 (an empty Inbox said nothing), #12 (Home's charts were blank boxes before opening night),
/// H1 (the contract panel's Release did nothing with no player in talks) and H6 (Mark all read with nothing unread).</summary>
[Collection(SharedUiState.Name)] // touches app settings or the portrait service (run 131: they raced PortraitServiceTests)
public class Run59FixTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_first_season_s_empty_pages_say_why_and_idle_buttons_are_greyed_out()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var path = Path.Combine(Path.GetTempPath(), $"hw-run59-ui-{Guid.NewGuid():N}.hwsave");
        try
        {
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            Avalonia.Controls.Button Button(string label) => window.GetVisualDescendantsOf<Avalonia.Controls.Button>()
                .First(b => b.IsEffectivelyVisible && (b.Content as string) == label);
            bool Shows(string text) => window.GetVisualDescendantsOf<Avalonia.Controls.TextBlock>()
                .Any(t => t.IsEffectivelyVisible && (t.Text ?? "").Contains(text));

            // #12: before opening night Home's two game charts say when they fill in.
            Assert.Equal(0, s.MyGames().Count(g => g.Played));
            vm.Navigate("Home"); Settle();
            var home = (HomeViewModel)vm.CurrentPage!;
            Assert.Equal("Fills in after your first game.", home.FormNote);
            Assert.Equal("Fills in after your first game.", home.ComparisonNote);
            Assert.True(Shows("Fills in after your first game."));

            // H6: with nothing unread, Mark all read is greyed out and says so.
            vm.Navigate("Inbox"); Settle();
            var inbox = (InboxViewModel)vm.CurrentPage!;
            s.MarkAllRead(); inbox.Refresh(); Settle();
            var mark = Button("Mark all read");
            Assert.False(mark.IsEffectivelyEnabled);
            Assert.Equal("Nothing unread", Avalonia.Controls.ToolTip.GetTip(mark) as string);

            // #11: an empty filter and an empty inbox each get a line instead of a blank list.
            inbox.Filter = "Unread"; Settle();
            Assert.Empty(inbox.Items);
            Assert.Contains("No messages in \"Unread\"", inbox.EmptyText);
            Assert.True(Shows("No messages in \"Unread\""));
            inbox.Filter = "All messages";
            s.DeleteWhere(_ => true);
            s.Inbox.RemoveAll(_ => true);
            inbox.Refresh(); Settle();
            Assert.StartsWith("No messages.", inbox.EmptyText);
            Assert.True(Shows("No messages. News, results"));

            // A new message clears the line and lights the button again.
            s.Inbox.Add(new InboxMessage { Id = 999_001, Date = s.Today, Title = "Test", Body = "Test", Category = "League" });
            inbox.Refresh(); Settle();
            Assert.Equal("", inbox.EmptyText);
            Assert.True(inbox.CanMarkAllRead || inbox.Items.All(i => i.IsRead));

            // H1: the contract panel's Release is never pressable without one of your players in renewal talks.
            var talk = new TalkViewModel(vm, () => { });
            talk.Show(null);
            Assert.False(talk.IsRenewal);
            Assert.False(talk.CanRelease);
            Assert.False(talk.ReleaseCommand.CanExecute(null) && talk.CanRelease);
            var freeAgent = s.League.FreeAgents.FirstOrDefault();
            if (freeAgent != null)
            {
                talk.Show(freeAgent);
                Assert.False(talk.IsRenewal);
                Assert.False(talk.CanRelease);
                talk.Show(null);
                Assert.False(talk.CanRelease);
            }
            window.Close();
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
