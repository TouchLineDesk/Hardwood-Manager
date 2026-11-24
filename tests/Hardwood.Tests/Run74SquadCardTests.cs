using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 74 (review #13 rank 11): Squad opens on the dressing room as a picture: chemistry as a dial with its
/// factors as bars, and every player as a card in his mood's lane (unsettled, content, happy), the unsettled first.
/// A card picks him for the side panel; the table is one switch away, its worry on one line (H18).</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run74SquadCardTests
{
    [Fact]
    public void The_face_and_colour_follow_the_mood_words_bands()
    {
        // Before, a "Content" 62 had the green smile (the face turned at 60, the word at 68).
        Assert.Equal("Content", Hardwood.Core.People.PeopleEngine.Mood(62));
        Assert.Equal(1, MoodRow.Band(62));
        Assert.Equal(2, MoodRow.Band(68));
        Assert.Equal(1, MoodRow.Band(50));
        Assert.Equal(0, MoodRow.Band(49.6));   // run 188: shown as 49 (rounded down), so the lane, the number and the word all say Unhappy
        Assert.Equal(0, MoodRow.Band(49.4));
        Assert.Equal("#D29922", MoodRow.BandColor(62));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Squad_opens_on_mood_lanes_and_a_chemistry_dial()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run74-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            // One of each mood, and a trade request, so every lane and the status line show.
            var roster = s.HumanTeam.Roster;
            roster[0].Morale = 30; roster[0].TradeRequested = true;
            roster[1].Morale = 44;
            roster[2].Morale = 58;
            roster[3].Morale = 85;
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
            void Snap(string name) { if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, name)); } }

            vm.Navigate("Squad"); Settle();
            var sq = (SquadViewModel)vm.CurrentPage!;
            var view = window.GetVisualDescendants().OfType<SquadView>()
                .First(v => v.DataContext == sq && v.GetVisualDescendants().OfType<Button>().Any(b => b.DataContext is MoodRow));

            // Cards first; the table hidden.
            Assert.True(sq.ShowCards);
            Assert.True(view.FindControl<ScrollViewer>("MoodCards")!.IsEffectivelyVisible);
            Assert.False(view.FindControl<DataGrid>("MoodTable")!.IsEffectivelyVisible);

            // The lanes: unsettled first, then content, then happy; everyone in exactly one, in his band.
            Assert.Equal(3, sq.Lanes.Count);
            Assert.StartsWith("Unsettled (", sq.Lanes[0].Title);
            Assert.StartsWith("Content (", sq.Lanes[1].Title);
            Assert.StartsWith("Happy (", sq.Lanes[2].Title);
            Assert.Equal(roster.Count, sq.Lanes.Sum(l => l.Rows.Count));
            for (int b = 0; b < 3; b++) Assert.All(sq.Lanes[b].Rows, r => Assert.Equal(b, MoodRow.Band(r.Player.Morale)));
            Assert.Equal(roster[0], sq.Lanes[0].Rows[0].Player);   // the lowest first
            Assert.Contains(sq.Lanes[0].Rows, r => r.Player == roster[1]);
            Assert.Contains(sq.Lanes[1].Rows, r => r.Player == roster[2]);
            Assert.Contains(sq.Lanes[2].Rows, r => r.Player == roster[3]);
            Assert.Equal("Wants a trade", sq.Lanes[0].Rows[0].Status);
            Assert.Contains("Average morale", sq.MoraleSummary);

            // One card per player, all on screen-sized buttons.
            var cards = view.GetVisualDescendants().OfType<Button>().Where(b => b.DataContext is MoodRow).ToList();
            Assert.Equal(roster.Count, cards.Count);
            Assert.All(cards, c => Assert.True(c.Bounds.Width > 150 && c.Bounds.Height > 60, $"{c.Bounds}"));

            // The chemistry dial and its bars: the biggest factor fills the bar, the rest in proportion.
            var dial = view.FindControl<Gauge>("ChemistryDial")!;
            Assert.True(dial.IsEffectivelyVisible);
            Assert.Equal(s.HumanTeam.Chemistry, dial.Value, 3);
            Assert.NotNull(dial.ArcColor);
            if (sq.ChemistryFactors.Count > 0)
            {
                Assert.Equal(SquadViewModel.FactorBarMax, sq.ChemistryFactors.Max(f => f.BarWidth), 3);
                Assert.All(sq.ChemistryFactors, f => Assert.InRange(f.BarWidth, 3, SquadViewModel.FactorBarMax));
            }
            Snap("run74-squad-cards.png");

            // A click on the happy player's card shows him in the side panel; only his card is picked.
            var happyCard = cards.First(c => ((MoodRow)c.DataContext!).Player == roster[3]);
            happyCard.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
            Assert.Equal(roster[3], sq.Selected!.Player);
            Assert.Single(sq.Players, r => r.Picked);
            Assert.True(sq.Selected.Picked);
            Assert.StartsWith(roster[3].Name, sq.PlayerHeadline);
            Assert.NotEmpty(sq.Topics);

            // A talk refreshes the page and keeps him picked.
            sq.Topics[0].Command.Execute(null); Settle();
            Assert.Equal(roster[3], sq.Selected!.Player);
            Assert.True(sq.Selected.Picked);

            // The table, one switch away: the same players, and the pick follows.
            sq.View = "Full table"; Settle();
            var grid = view.FindControl<DataGrid>("MoodTable")!;
            Assert.True(grid.IsEffectivelyVisible);
            Assert.False(view.FindControl<ScrollViewer>("MoodCards")!.IsEffectivelyVisible);
            Assert.Equal(roster[3], ((MoodRow)grid.SelectedItem!).Player);
            Snap("run74-squad-table.png");
            sq.View = "Cards"; Settle();
            Assert.True(view.FindControl<ScrollViewer>("MoodCards")!.IsEffectivelyVisible);

            var problems = LayoutCheck.Scan(window);
            Assert.True(problems.Count == 0, string.Join("\n", problems));
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            foreach (var f in new[] { path, path + ".bak" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
