using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 70 (review #13 rank 8): another club's Team page opens its roster on collector cards in the club's
/// colours (its crest and colour on every card), with what your scouts know and the player's line this season in that
/// club's league; the full table (and its column views) is one click away.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run70TeamCardTests
{
    [Fact]
    public void The_card_foot_reads_his_season_then_his_contract_then_his_age()
    {
        var p = new Player { FirstName = "Test", LastName = "Player", BirthDate = new DateOnly(2000, 1, 1) };
        var today = new DateOnly(2026, 11, 20);
        Assert.Equal("Age 26", TeamViewModel.CardLine(null, p, today));
        p.Contract = new Contract { Salary = 12_500_000, Years = 3 };
        Assert.Equal("$12.5M · 3 yr", TeamViewModel.CardLine(null, p, today));
        Assert.Equal("$12.5M · 3 yr", TeamViewModel.CardLine(new BookStat(1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0), p, today));
        p.Contract = new Contract { Salary = 2_000_000, Years = 0 };
        Assert.Equal("$2.0M · expiring", TeamViewModel.CardLine(null, p, today));
        var line = TeamViewModel.CardLine(new BookStat(1, 1, 10, 300, 185, 52, 41, 9, 4, 70, 150, 20, 55), p, today);
        Assert.Equal("18.5 pts · 5.2 reb · 4.1 ast", line);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Another_clubs_roster_opens_on_cards_in_its_colours()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run70-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
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
            var s = vm.Session!;
            while (s.Today < new DateOnly(2026, 11, 20)) s.AdvanceDay();
            vm.AfterTick();
            void Settle() { for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            void Snap(string name) { if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, name)); } }

            // An NBA rival: cards first, one per player, each in the club's colours and as your scouts see him.
            var other = s.League.Teams.First(t => t != s.HumanTeam && t.Roster.Count > 0);
            vm.ShowTeam(other); Settle();
            var page = Assert.IsType<TeamViewModel>(vm.CurrentPage);
            Assert.Equal("Cards", page.RosterView);
            Assert.True(page.ShowRosterCards);
            Assert.Equal(other.Roster.Count, page.Players.Count);
            Assert.All(page.Players, r => Assert.Same(other, r.Club));
            Assert.Contains(page.Players, r => r.Knowledge < 0.999);   // not your player: ranges, not the truth
            Assert.Contains(page.Players, r => r.CardFooter.Contains(" pts · "));   // November: his line in this league
            Assert.StartsWith($"{other.Roster.Count} players, most minutes first", page.RosterNote);
            Assert.Contains("scouts believe", page.RosterNote);
            var view = window.GetVisualDescendants().OfType<TeamView>().First(v => v.DataContext == page && v.IsEffectivelyVisible);
            var cardsHost = view.GetVisualDescendants().OfType<ScrollViewer>().Single(x => x.Name == "TeamRosterCards");
            Assert.True(cardsHost.IsEffectivelyVisible);
            var cards = cardsHost.GetVisualDescendants().OfType<PlayerCard>().ToList();
            Assert.Equal(other.Roster.Count, cards.Count);
            Assert.All(cards, c => Assert.Same(other, c.Team));
            Assert.Equal(page.Players.Select(r => r.Player), cards.Select(c => c.Player));
            var grid = view.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "TeamRosterGrid");
            Assert.False(grid.IsEffectivelyVisible);
            Assert.False(view.GetVisualDescendants().OfType<ColumnPicker>().Single(p => p.Key == "team-roster").IsEffectivelyVisible);
            Snap("run70-team-cards.png");

            // A double-click opens his page.
            var second = page.Players[1];
            cards[1].RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, null!));
            var playerPage = Assert.IsType<PlayerViewModel>(vm.CurrentPage);
            Assert.Same(second.Player, playerPage.Player);
            vm.GoBack(); Settle();
            page = Assert.IsType<TeamViewModel>(vm.CurrentPage);

            // The full table is one click away, with its column views.
            page.RosterView = "Full table"; Settle();
            view = window.GetVisualDescendants().OfType<TeamView>().First(v => v.DataContext == page && v.IsEffectivelyVisible);
            Assert.True(view.GetVisualDescendants().OfType<DataGrid>().Single(g => g.Name == "TeamRosterGrid").IsEffectivelyVisible);
            Assert.False(view.GetVisualDescendants().OfType<ScrollViewer>().Single(x => x.Name == "TeamRosterCards").IsEffectivelyVisible);
            Assert.True(view.GetVisualDescendants().OfType<ColumnPicker>().Single(p => p.Key == "team-roster").IsEffectivelyVisible);
            Snap("run70-team-table.png");

            // Your own club: everything known, and no scouts' line.
            vm.ShowTeam(s.HumanTeam); Settle();
            var mine = Assert.IsType<TeamViewModel>(vm.CurrentPage);
            Assert.All(mine.Players, r => Assert.Equal(1.0, r.Knowledge, 3));
            Assert.DoesNotContain("scouts believe", mine.RosterNote);

            // A college program: its players are prospects to your scouts, on cards in the program's colours.
            if (s.World.College?.Programs.FirstOrDefault(t => t.Roster.Count > 0) is { } program)
            {
                vm.ShowTeam(program); Settle();
                var cp = Assert.IsType<TeamViewModel>(vm.CurrentPage);
                Assert.True(cp.ShowRosterCards);
                Assert.Equal(program.Roster.Count, cp.Players.Count);
                Assert.All(cp.Players, r => Assert.Same(program, r.Club));
                Assert.All(cp.Players, r => Assert.Equal(Scouting.Knowledge(s.HumanTeam, r.Player, prospect: true), r.Knowledge, 6));
                Snap("run70-college-team-cards.png");
            }
            window.Close();
        }
        finally
        {
            window?.Close();
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
        }
    }
}
