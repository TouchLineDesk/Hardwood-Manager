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
    // WithInterFont: the game's own font, so text measures in tests as it does in the app (CI has no Inter installed).
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia().WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
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
            vm.Navigate("Rotation");
            var rot = (RotationViewModel)vm.CurrentPage!;
            rot.Rows[0].Minutes = 38;
            Assert.Equal(38, session.HumanTeam.DepthChart!.MinutesTargets[rot.Rows[0].Player.Id]);

            // Tactics page: system, scheme, a slider, a role, the stopper, a preset and the late-game switch.
            vm.Navigate("Tactics");
            var tac = (TacticsViewModel)vm.CurrentPage!;
            window.UpdateLayout();
            tac.ThreeEmphasis = 80;
            Assert.Equal(80, session.HumanTeam.Tactic.ThreePointEmphasis);
            tac.SelectedSystem = tac.Systems.First(o => o.System == Hardwood.Core.Model.OffensiveSystem.Motion);
            tac.SelectedScheme = tac.Schemes.First(o => o.Scheme == Hardwood.Core.Model.DefensiveScheme.Zone23);
            Assert.Equal(Hardwood.Core.Model.OffensiveSystem.Motion, session.HumanTeam.Tactic.System);
            Assert.Equal(Hardwood.Core.Model.DefensiveScheme.Zone23, session.HumanTeam.Tactic.Scheme);
            var roleRow = tac.RoleRows[0];
            roleRow.Selected = roleRow.Choices.First(c => c.Role == Hardwood.Core.Model.Role.GlueGuy);
            Assert.Equal(Hardwood.Core.Model.Role.GlueGuy, session.HumanTeam.Tactic.RoleOf(roleRow.Player));
            tac.SelectedStopper = tac.Stoppers[1];
            Assert.Equal(tac.Stoppers[1].Player!.Id, session.HumanTeam.Tactic.StarStopperId);
            tac.NewPresetName = "Small ball";
            tac.SavePreset();
            tac.LateGame = "Small ball";
            Assert.Equal("Small ball", session.HumanTeam.Tactic.LateGamePreset);
            Assert.False(string.IsNullOrEmpty(tac.ScoutingReport));
            tac.UseSuggestion();
            window.UpdateLayout();
            // The overhaul (#475, D60 A): the diagram follows the tactic, the board has five lines, a card applies.
            Assert.Equal(session.HumanTeam.Tactic.System, tac.DiagramSystem);
            Assert.Equal(5, tac.PlayLabels.Count);
            Assert.Equal(5, tac.Board.Count);
            var lockCard = tac.Setups.First(s => s.Setup.Key == "lock");
            lockCard.ApplyCommand.Execute(null);
            Assert.True(lockCard.Setup.Matches(session.HumanTeam.Tactic));
            Assert.Equal(session.HumanTeam.Tactic.Scheme, tac.DiagramScheme);
            Assert.Contains(tac.Setups, s => s.Active && s.Setup.Key == "lock");
            Assert.Equal(1, Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Hardwood.Desktop.Controls.PlayDiagram>().Count());
            window.UpdateLayout();

            // Squad page: chemistry, moods, and a talk.
            vm.Navigate("Squad");
            var squad = (SquadViewModel)vm.CurrentPage!;
            window.UpdateLayout();
            Assert.False(string.IsNullOrEmpty(squad.ChemistryHeadline));
            Assert.Equal(session.HumanTeam.Roster.Count, squad.Players.Count);
            squad.Selected = squad.Players[0];
            Assert.NotEmpty(squad.Topics);
            squad.Topics[0].Command.Execute(null);
            Assert.False(string.IsNullOrEmpty(squad.Reply));
            window.UpdateLayout();

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
            // Match day (roadmap 2): what the flow panel says matches the game.
            for (int step = 0; step < 200 && !live!.Complete; step++)
            {
                live.Step();
                liveVm.Refresh();
                var run = Hardwood.Core.Engine.GameFlow.CurrentRun(live.Result);
                Assert.Equal(run == null ? null : $"{run.Team} on a {run.Score} run", liveVm.RunAlert);
                Assert.Equal(live.AwayScore.ToString(), liveVm.Splits[1].Total);
                Assert.Equal(live.HomeScore.ToString(), liveVm.Splits[2].Total);
                Assert.Equal(live.AwayScore, liveVm.Splits[1].Cells.Where(c => c != "–").Sum(int.Parse));
                Assert.Equal(live.HomeScore, liveVm.Splits[2].Cells.Where(c => c != "–").Sum(int.Parse));
                Assert.Equal(live.Result.Away.FGA, liveVm.AwayZoneAttempts.Sum());
                Assert.Equal(live.Result.Home.FGM, liveVm.HomeZoneMakes.Sum());
                // Match day part 2: the ticker quotes a current fact, the stats tab matches the box, no winner yet.
                Assert.Contains(liveVm.Ticker, Hardwood.Core.Engine.GameHighlights.Ticker(live.Result).DefaultIfEmpty(liveVm.Ticker));
                Assert.Equal($"{live.Result.Away.FGM}-{live.Result.Away.FGA}", liveVm.TeamStats[0].AwayText.Split(' ')[0]);
                Assert.Equal(live.Result.Home.Turnovers.ToString(), liveVm.TeamStats.First(t => t.Label == "Turnovers").HomeText);
                if (!live.Complete) Assert.DoesNotContain(liveVm.Highlights, h => h.Kind == "clutch");
            }
            Assert.Contains(liveVm.Log, l => l.Contains(" run") && l.Contains(">>"));
            // Match day part 3: the break card after a period.
            liveVm.ShowBreakCard(final: false);
            Assert.True(liveVm.ShowBreak);
            Assert.Matches(@"^(End of (Q\d|OT\d?)|Halftime)$", liveVm.BreakTitle);
            var topHome = live.Result.Home.Lines.OrderByDescending(l => l.Points).First();
            Assert.Contains(liveVm.BreakChips, c => c.Label == $"{live.Result.Home.Team.Abbreviation} leader" && c.Value == $"{topHome.Points} pts");
            liveVm.SimToEnd();
            Assert.True(liveVm.Complete);
            liveVm.Continue();
            var box = Assert.IsType<BoxScoreViewModel>(vm.CurrentPage);
            Assert.True(box.HasFlow);
            Assert.Equal("Player of the game", box.Highlights[0].Title);
            Assert.Equal(Hardwood.Core.Engine.GameFlow.Runs(box.Result).Count, box.RunLines.Count);
            Assert.Equal(box.Result.Home.PointsByPeriod.Select(x => x.ToString()), box.Splits[2].Cells);
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
            Assert.False(tx.TradesOpen); // the open market has no trades
            if (tx.FreeAgents.Count > 0)
            {
                tx.SelectedFreeAgent = tx.FreeAgents.First();
                Assert.Same(tx.FreeAgents.First().Player, tx.Talk.Player);
            }
            window.UpdateLayout();

            // Run to the summer: contract talks, releases, free agency (no draft on the open market).
            int guard = 0;
            while (session.Phase != SessionPhase.Offseason && guard++ < 400) session.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Offseason");
            var off = (OffseasonViewModel)vm.CurrentPage!;
            Assert.True(off.InOffseason);
            Assert.True(off.ShowContracts);
            window.UpdateLayout();
            foreach (var row in off.Expiring.ToList())
            {
                off.SelectedExpiring = row;
                off.Talk.OfferCommand.Execute(null); // at his ask: accepted, unless he won't stay or it's over budget
            }
            vm.AfterTick();
            foreach (var p in session.MyExpiring.ToList()) session.ReleaseExpiring(p);
            Assert.Empty(session.MyExpiring);

            session.AdvanceOffseason();
            vm.AfterTick();
            off.Refresh();
            // A real career drafts first (D40): on the clock, the Draft tab picks the selected graduate.
            if (session.Offseason!.Phase == OffseasonPhase.Draft)
            {
                Assert.True(off.ShowDraft);
                Assert.True(off.OnTheClock);
                window.UpdateLayout();
                var pick = off.SelectedProspect!.Player;
                off.DraftCommand.Execute(null);
                Assert.Contains(pick, session.HumanTeam.Roster);
                off.Refresh();
            }
            Assert.True(off.ShowFreeAgency);
            Assert.Contains(off.FreeAgents, f => f.Graduate);
            window.UpdateLayout();
            if (session.HumanTeam.Roster.Count < 15)
            {
                off.SelectedFreeAgent = off.FreeAgents.Last();
                Assert.False(string.IsNullOrEmpty(off.Talk.AskText));
                off.Talk.OfferCommand.Execute(null);
                Assert.False(string.IsNullOrEmpty(off.Talk.Reply));
            }
            Assert.NotEmpty(off.Overseas);
            off.SelectedOverseas = off.Overseas.First();
            window.UpdateLayout();
            off.BuyOut();
            Assert.False(string.IsNullOrEmpty(off.Status));
            window.UpdateLayout();

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
