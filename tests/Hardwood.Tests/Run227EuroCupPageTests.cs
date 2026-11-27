using Avalonia.Headless;
using Hardwood.Core.Engine;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 227: Leagues → Club cups → the EuroCup shows a card per group with the quarter- and eighth-final places, and a
/// bracket that starts at the eighth-finals with the group winners waiting in the quarter-finals. Writes
/// <c>run227-eurocup.png</c> and <c>run227-eurocup-bracket.png</c> when HW_SCREENSHOT_DIR is set.</summary>
[Collection(SharedUiState.Name)]
public class Run227EuroCupPageTests
{
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void The_EuroCup_page_shows_both_groups_and_the_bracket()
    {
        var s = GameSession.NewGame(31, humanTeamId: 5, savePath: null);
        s.World.FinishSeasons(new GameSimulator());
        var vm = new MainViewModel();
        vm.AttachSession(s);
        vm.Navigate("Leagues");
        var w = Assert.IsType<WorldViewModel>(vm.CurrentPage);
        w.Region = "Club cups";
        w.Selected = w.Competitions.First(c => c.Key == WorldLeagues.EuroCup);

        Assert.Equal("Group A", Assert.Single(w.LeftCards).Title);
        Assert.Equal("Group B", Assert.Single(w.RightCards).Title);
        Assert.All(w.LeftCards.Concat(w.RightCards), c => Assert.Equal(10, c.Rows.Count));
        var a = w.LeftCards[0].Rows;
        Assert.Equal(StandingRow.PlayoffBrush, a[0].ZoneBrush);
        Assert.Equal(StandingRow.PlayInBrush, a[2].ZoneBrush);
        Assert.Contains("quarter-finals", a[1].ZoneTip);
        Assert.Contains("eighth-finals", a[5].ZoneTip);
        Assert.Equal("Outside the postseason places", a[6].ZoneTip);
        Assert.Contains(w.Keys, k => k.Label == "Quarter-finals");
        Assert.Contains(w.Keys, k => k.Label == "Eighth-finals");
        Assert.DoesNotContain(w.Keys, k => k.Label is "Play-in" or "Playoffs");
        Assert.Equal(new[] { 4, 4, 2, 1 }, w.MainBracket.Select(c => c.Slots.Count));

        var window = new MainWindow { DataContext = vm, Width = 1280, Height = 800 };
        window.Show();
        window.UpdateLayout();
        if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } dir)
        {
            Directory.CreateDirectory(dir);
            using (var f = window.CaptureRenderedFrame()) f?.Save(Path.Combine(dir, "run227-eurocup.png"));
        }
        window.Close();
    }
}
