using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Hardwood.Tests.TestAppBuilder))]

namespace Hardwood.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

[Collection(SharedUiState.Name)]
public class UiSmokeTests
{
    [AvaloniaFact]
    public void Every_screen_renders_with_a_live_session()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-ui-{Guid.NewGuid():N}.save");
        try
        {
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm };
            window.Show();
            Assert.IsType<StartViewModel>(vm.CurrentPage);

            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path;
            start.SeedText = "99";
            start.SelectedTeam = start.Teams.First();
            start.NewGame();
            Assert.True(vm.HasSession);
            Assert.NotNull(vm.Session);

            var session = vm.Session!;
            foreach (var nav in vm.Nav.ToList())
            {
                vm.Navigate(nav.Name);
                window.UpdateLayout();
                Assert.NotNull(vm.CurrentPage);
            }

            // Rotation page: change a starter and some minutes; the chart must follow.
            vm.Navigate("Rotation & Tactics");
            var rot = (RotationViewModel)vm.CurrentPage!;
            rot.Rows[0].Minutes = 38;
            Assert.Equal(38, session.HumanTeam.DepthChart!.MinutesTargets[rot.Rows[0].Player.Id]);
            rot.ThreeEmphasis = 80;
            Assert.Equal(80, session.HumanTeam.Tactic.ThreePointEmphasis);

            // Live game through the view model: play a few possessions, change the five, finish, continue.
            while (!session.HumanPlaysToday) session.AdvanceDay();
            vm.AfterTick();
            var (live, others) = session.BeginLiveDay();
            var liveVm = new LiveGameViewModel(vm, live!, others);
            vm.CurrentPage = liveVm;
            window.UpdateLayout();
            for (int i = 0; i < 30; i++) live!.Step();
            liveVm.Refresh();
            liveVm.Lineup[0].Selected = liveVm.OurPlayers.Last().Player;
            liveVm.ApplyLineup();
            liveVm.SimToEnd();
            Assert.True(liveVm.Complete);
            liveVm.Continue();
            Assert.IsType<BoxScoreViewModel>(vm.CurrentPage);
            window.UpdateLayout();

            // Play into the season and open the box score of the first game.
            for (int i = 0; i < 12; i++) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Schedule & Results");
            var sched = (ScheduleViewModel)vm.CurrentPage!;
            var played = sched.Games.First(g => g.Played);
            sched.OpenBox(played);
            Assert.IsType<BoxScoreViewModel>(vm.CurrentPage);
            window.UpdateLayout();

            vm.Navigate("Roster");
            var roster = (RosterViewModel)vm.CurrentPage!;
            roster.Open(roster.Rows[0]);
            Assert.IsType<PlayerViewModel>(vm.CurrentPage);
            window.UpdateLayout();

            vm.Navigate("Transactions");
            var tx = (TransactionsViewModel)vm.CurrentPage!;
            tx.Ours[0].Included = true;
            tx.Theirs[^1].Included = true;
            Assert.False(string.IsNullOrEmpty(tx.Verdict));
            window.UpdateLayout();

            // Run to the offseason and walk the three phases through the view model.
            int guard = 0;
            while (session.Phase != SessionPhase.Offseason && guard++ < 400) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Offseason");
            var off = (OffseasonViewModel)vm.CurrentPage!;
            Assert.True(off.IsReSigning);
            window.UpdateLayout();
            foreach (var row in off.Expiring.ToList()) { off.SelectedExpiring = row; off.ReSign(); }
            vm.AfterTick();
            // Whatever didn't fit is still expiring; release it.
            foreach (var row in off.Expiring.ToList()) { off.SelectedExpiring = row; off.Release(); }

            session.AdvanceOffseason();
            vm.AfterTick();
            Assert.True(off.IsDraft);
            window.UpdateLayout();
            while (off.IsDraft && session.Phase == SessionPhase.Offseason && session.Offseason!.Phase == OffseasonPhase.Draft)
            {
                off.SelectedProspect = off.Prospects.First();
                off.MakePick();
            }
            vm.AfterTick();
            Assert.True(off.IsFreeAgency);
            window.UpdateLayout();
            if (off.FreeAgents.Count > 0 && session.HumanTeam.Roster.Count < 15) { off.SelectedFreeAgent = off.FreeAgents.First(); off.SignFreeAgent(); }

            session.AdvanceOffseason();
            vm.AfterTick();
            Assert.Equal(SessionPhase.Preseason, session.Phase);
            vm.Navigate("History");
            window.UpdateLayout();
            Assert.Single(((HistoryViewModel)vm.CurrentPage!).Rows);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
