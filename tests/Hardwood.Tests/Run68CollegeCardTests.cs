using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 68 (review #13 rank 7, Danny's #464; bug hunt H18's college items): the high-school classes and a college
/// coach's recruiting board open on star cards, with the full tables one click away; an empty board says why; the
/// College tabs fit on one line at 1480 px; college Standings drop the empty Div column; Roster's Team box shows your
/// program.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run68CollegeCardTests
{
    [Fact]
    public void Stars_edges_and_chips_read_the_rating_and_the_status()
    {
        Assert.Equal("★★★★", CollegeCards.On(4));
        Assert.Equal("★", CollegeCards.Off(4));
        Assert.Equal("", CollegeCards.On(0));
        Assert.Equal("★★★★★", CollegeCards.Off(0));
        Assert.Equal("★★★★★", CollegeCards.On(7));
        Assert.Same(CollegeCards.Gold, CollegeCards.StarEdge(5));
        Assert.NotSame(CollegeCards.StarEdge(5), CollegeCards.StarEdge(4));
        Assert.Contains("4-star", CollegeCards.StarTip(4, 31));
        Assert.Contains("No. 31", CollegeCards.StarTip(4, 31));
        Assert.Same(StandingRow.PlayoffBrush, CollegeCards.StatusBrush("Committed to you"));
        Assert.NotSame(StandingRow.PlayoffBrush, CollegeCards.StatusBrush("Committed: Duke"));
        Assert.NotSame(CollegeCards.StatusBrush("Offered"), CollegeCards.StatusBrush("Open"));
        Assert.Same(StandingRow.PlayoffBrush, CollegeCards.InterestBrush(75));
        Assert.Same(StandingRow.RelegationBrush, CollegeCards.InterestBrush(10));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_college_coach_recruits_from_star_cards()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run68-{Guid.NewGuid():N}.hwsave");
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
            start.PlayLeague = StartViewModel.CollegeChoice;
            start.SelectedTeam = start.Teams[1];
            start.NewGame();
            var s = vm.Session!;
            Assert.True(s.InCollege);
            Assert.True(s.TrailOpen);
            while (s.Today < new DateOnly(2026, 11, 20)) s.AdvanceDay();
            vm.AfterTick();
            void Settle() { for (int i = 0; i < 3; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            void Snap(string name) { if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, name)); } }

            // H18: Roster's Team box shows your program (it was blank: the box listed only the NBA), your conference first.
            vm.Navigate("Roster"); Settle();
            var roster = Assert.IsType<RosterViewModel>(vm.CurrentPage);
            Assert.Same(s.HumanTeam, roster.Team);
            Assert.Contains(s.HumanTeam, roster.Teams);
            Assert.True(roster.Teams.Take(20).Count(t => s.World.College!.Programs.Contains(t)) >= 8, "the conference's programs come first");
            var box = window.GetVisualDescendants().OfType<ComboBox>().First(c => ReferenceEquals(c.ItemsSource, roster.Teams));
            Assert.Same(s.HumanTeam, box.SelectedItem);
            // Code review: a conference rival in season isn't in the NBA's standings, so his page has no trade line (it threw).
            var rival = roster.Teams.First(t => t != s.HumanTeam && s.World.College!.Programs.Contains(t));
            roster.Team = rival; Settle();
            Assert.Same(rival, roster.Team);
            Assert.Contains("scouting knowledge", roster.Summary);
            roster.Team = s.HumanTeam;

            // H18: college conferences have no divisions, so Standings shows no Div column.
            vm.Navigate("Standings"); Settle();
            var grids = window.GetVisualDescendants().OfType<DataGrid>().Where(g => g.IsEffectivelyVisible && g.Columns.Any(c => c.Header as string == "Div")).ToList();
            Assert.NotEmpty(grids);
            Assert.All(grids, g => Assert.False(g.Columns.First(c => c.Header as string == "Div").IsVisible));

            // High school: star cards, the best 30, in rank order.
            vm.Navigate("College"); Settle();
            var col = Assert.IsType<CollegeViewModel>(vm.CurrentPage);
            col.Tab = 6; Settle();
            Assert.Equal("Cards", col.BoardView);
            Assert.Equal(CollegeCards.CardCount, col.HighSchoolCards.Count);
            Assert.Equal(col.HighSchoolers.Take(CollegeCards.CardCount), col.HighSchoolCards);
            Assert.True(col.HighSchoolCards.Select(r => r.Rank).SequenceEqual(col.HighSchoolCards.Select(r => r.Rank).Order()));
            Assert.Contains($"of {col.HighSchoolers.Count}", col.HighSchoolCardNote);
            Assert.All(col.HighSchoolCards, r => Assert.Equal(5, r.StarsOn.Length + r.StarsOff.Length));
            Assert.All(col.HighSchoolCards, r => Assert.Equal(r.HasCommit, r.Committed != "Uncommitted"));
            var cards = window.GetVisualDescendants().OfType<ItemsControl>().First(c => c.Name == "HighSchoolCards");
            Assert.True(cards.IsEffectivelyVisible);
            // A card press picks him: the border lights, Open player works and a line says what to do.
            var third = col.HighSchoolCards[2];
            var btn = cards.GetVisualDescendants().OfType<Button>().First(b => b.DataContext == third);
            btn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Same(third, col.SelectedHighSchool);
            Assert.True(third.Picked);
            Assert.Single(col.HighSchoolers, r => r.Picked);
            Assert.Contains(third.Name, col.HighSchoolHint);
            Assert.Contains("Open player", col.HighSchoolHint);
            // The school keeps its capitals ("committed to Ohio State").
            Assert.Contains(third.HasCommit ? $"(committed to {third.CommittedTeam!.City})" : "(uncommitted)", col.HighSchoolHint);
            Snap("run68-high-school-cards.png");
            // A filter narrows the cards too.
            col.StarFilter = "5-star"; Settle();
            Assert.All(col.HighSchoolCards, r => Assert.Equal(5, r.StarCount));
            Assert.Contains("5-star", col.HighSchoolCardNote);
            col.StarFilter = "All";
            // The full table is one click away.
            col.BoardView = "Full table"; Settle();
            Assert.False(cards.IsEffectivelyVisible);
            Assert.True(window.GetVisualDescendants().OfType<DataGrid>().Any(g => g.IsEffectivelyVisible && ReferenceEquals(g.ItemsSource, col.HighSchoolers)));
            col.BoardView = "Cards";

            // Your recruiting board: star cards with interest, the leaning program and a status chip.
            col.Tab = 5; col.TrailView = "All uncommitted"; Settle();
            Assert.NotEmpty(col.TrailCards);
            Assert.True(col.TrailCards.Count <= CollegeCards.CardCount);
            Assert.False(col.HasTrailEmpty);
            Assert.All(col.TrailCards, r => Assert.InRange(r.Interest, 0, 100));
            Assert.All(col.TrailCards, r => Assert.Equal(r.HasLeaning, r.LeaningText.StartsWith("Leans ")));
            var trailCards = window.GetVisualDescendants().OfType<ItemsControl>().First(c => c.Name == "TrailCards");
            Assert.True(trailCards.IsEffectivelyVisible);
            var pick = col.TrailCards[1];
            trailCards.GetVisualDescendants().OfType<Button>().First(b => b.DataContext == pick)
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Same(pick, col.SelectedTrail);
            Assert.True(col.HasTrail);
            Assert.Equal(pick.Name, col.TrailName);
            Assert.True(pick.Picked);
            // Acting on him rebuilds the board and keeps him picked (on a card, and lit).
            col.ScoutTrailCommand.Execute(null);
            Assert.NotEqual("", col.TrailReply);
            Assert.Equal(pick.Player, col.SelectedTrail!.Player);
            Assert.True(col.SelectedTrail.Picked);
            Assert.Contains(col.SelectedTrail, col.TrailCards);
            Snap("run68-recruiting-cards.png");
            // An empty view says why instead of showing a blank board.
            col.TrailView = "Committed to you"; Settle();
            if (col.TrailRows.Count == 0)
            {
                Assert.True(col.HasTrailEmpty);
                Assert.Contains("offered", col.TrailEmpty);
            }
            col.TrailView = "All uncommitted";
            col.BoardView = "Full table"; Settle();
            Assert.False(trailCards.IsEffectivelyVisible);
            Assert.True(window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "TrailGrid").IsEffectivelyVisible);
            col.BoardView = "Cards";

            // H18: the tabs fit on one line at 1480 px (eight since CP3, run 132: Your pros).
            Settle();
            var tabs = window.GetVisualDescendants().OfType<TabItem>().Where(t => t.IsVisible && t.IsEffectivelyVisible).ToList();
            Assert.Equal(8, tabs.Count);
            var tops = tabs.Select(t => t.TranslatePoint(new Avalonia.Point(0, 0), window)!.Value.Y).ToList();
            Assert.True(tops.Max() - tops.Min() < 2, $"the tabs wrap: tops {string.Join(", ", tops.Select(y => y.ToString("0")))}");
        }
        finally
        {
            window?.Close();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (File.Exists(AppSettings.FilePath)) File.Delete(AppSettings.FilePath);
            AppSettings.FilePath = oldPath; AppSettings.Use(oldSettings);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
