using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Core.World;
using Hardwood.Desktop;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 72 (review #13 rank 9): Schedule &amp; Results opens on scoreboard tiles (a W/L stripe, the opponent's
/// logo, the score big, the top performer) with a form strip of the last ten results; playoff series cards carry both
/// clubs' logos; the table is one click away. Bug hunt H20 rides along: once your club is out of the postseason,
/// Next game goes and Home and Schedule say why there's no game.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run72ScheduleTileTests
{
    private static MyGame G(int day, bool won, bool home = true) =>
        new(new DateOnly(2026, 11, day), new Team { City = "Test", Name = $"Club {day}", Abbreviation = "TST", Division = "D" }, home,
            "NBA", "Regular season", won ? 110 : 98, won ? 100 : 104, null);

    [Fact]
    public void The_form_line_counts_the_last_ten_and_the_streak()
    {
        Assert.Equal("", ScheduleViewModel.FormLine(new List<MyGame>()).text);
        var one = ScheduleViewModel.FormLine(new[] { G(1, false) });
        Assert.Equal("Last 1: 0-1 · lost the last one", one.text);
        Assert.Equal("L", Assert.Single(one.boxes).Letter);
        // Twelve games: only the last ten count, oldest first; the last three were wins.
        var games = Enumerable.Range(1, 12).Select(d => G(d, d is 1 or 2 or 5 or 10 or 11 or 12)).Reverse().ToList();
        var (text, boxes) = ScheduleViewModel.FormLine(games);
        Assert.Equal(10, boxes.Count);
        Assert.Equal("LLWLLLLWWW", string.Concat(boxes.Select(b => b.Letter)));
        Assert.Equal("Last 10: 4-6 · won 3 in a row", text);
        Assert.Contains("Nov 12", boxes[^1].Tip);
    }

    [Fact]
    public void A_tile_reads_the_result_or_what_is_to_come()
    {
        var won = new GameRow { Game = G(3, true), Kind = "W", TileOpponent = "vs Test Club 3", Outcome = "W", Score = "110-100", TopPerformer = "Smith 31 pts, 5 reb, 4 ast" };
        Assert.Equal("W 110-100", won.TileScore);
        Assert.Equal("Smith 31 pts, 5 reb, 4 ast", won.TileFoot);
        Assert.Equal("Tue, Nov 3 · home", won.TileWhen);
        var next = new GameRow { Game = G(4, true) with { Us = null, Them = null }, Kind = "next", HeadToHead = "2-1" };
        Assert.True(next.IsNext);
        Assert.Equal("Next game", next.TileScore);
        Assert.Equal("Head-to-head 2-1 going in", next.TileFoot);
        var later = new GameRow { Game = G(5, true) with { Us = null, Them = null }, HeadToHead = "–" };
        Assert.Equal("To come", later.TileScore);
        Assert.Equal("No meetings yet", later.TileFoot);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void A_G_League_season_on_tiles_through_to_being_knocked_out()
    {
        var (_, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        int teamId = world[UsaPyramid.Tier2].Clubs.OrderBy(t => QuickSim.Strength(t)).ElementAt(15).Id;
        var path = Path.Combine(Path.GetTempPath(), $"hw-run72-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        MainWindow? window = null;
        try
        {
            var s = GameSession.NewGame(31, teamId, path);
            var vm = new MainViewModel();
            window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { for (int i = 0; i < 6; i++) { Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); } }
            string? dir = Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } d ? d : null;
            void Snap(string name) { if (dir != null) { Settle(); using var f = window.CaptureRenderedFrame(); f?.Save(Path.Combine(dir, name)); } }
            var hl = s.HumanLeague!;

            // Six weeks in: tiles first, one per game, the stripe by result, your next game marked.
            while (s.Phase != SessionPhase.RegularSeason || s.MyRecord.Games < 14) s.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Schedule & Results"); Settle();
            var page = Assert.IsType<ScheduleViewModel>(vm.CurrentPage);
            Assert.Equal("Cards", page.GameView);
            Assert.True(page.ShowCards);
            Assert.All(page.Games.Where(g => g.Played), g => Assert.Equal(g.Game.Won ? "W" : "L", g.Kind));
            var next = Assert.Single(page.Games, g => g.IsNext);
            Assert.Same(next, page.Selected);
            Assert.Equal(s.NextMyGame!.Date, next.Game.Date);
            Assert.All(page.Games.Where(g => !g.Played && !g.IsNext), g => Assert.Equal("later", g.Kind));
            Assert.All(page.Games, g => Assert.DoesNotContain(" · ", g.TileOpponent));   // the event has its own line

            // The form strip: the last ten results, oldest first.
            Assert.True(page.HasForm);
            Assert.Equal(10, page.Form.Count);
            var last10 = page.Games.Where(g => g.Played).TakeLast(10).ToList();
            Assert.Equal(last10.Select(g => g.Game.Won ? "W" : "L"), page.Form.Select(b => b.Letter));
            Assert.StartsWith($"Last 10: {last10.Count(g => g.Game.Won)}-{last10.Count(g => !g.Game.Won)} · ", page.FormText);
            Assert.Equal("", page.SeasonOver);

            var view = window.GetVisualDescendants().OfType<ScheduleView>().First(v => v.DataContext == page && v.IsEffectivelyVisible);
            var tiles = view.GetVisualDescendants().OfType<ListBox>().Single(x => x.Name == "GameTiles");
            var table = view.GetVisualDescendants().OfType<DataGrid>().Single(x => x.Name == "GameTable");
            Assert.True(tiles.IsEffectivelyVisible);
            Assert.False(table.IsEffectivelyVisible);
            Assert.Same(next, tiles.SelectedItem);
            // It opens on your next game, not the season's first.
            var nextTile = tiles.ContainerFromItem(next);
            Assert.NotNull(nextTile);
            var at = nextTile!.TranslatePoint(new Avalonia.Point(0, 0), tiles);
            Assert.NotNull(at);
            Assert.InRange(at!.Value.Y, -1, tiles.Bounds.Height - 60);
            Assert.Contains(view.GetVisualDescendants().OfType<Border>(), b => b.Classes.Contains("tile") && b.IsEffectivelyVisible);
            Assert.True(view.GetVisualDescendants().OfType<StackPanel>().Single(x => x.Name == "FormStrip").IsEffectivelyVisible);
            Snap("run72-schedule-tiles.png");

            // A click on a played tile shows its head-to-head.
            var played = page.Games.First(g => g.Played);
            tiles.SelectedItem = played; Settle();
            Assert.Same(played, page.Selected);
            Assert.True(page.HasRival);
            Assert.Same(played.OpponentTeam, page.RivalTeam);
            Assert.True(page.CanOpenBox);

            // The full table, one pick away.
            page.GameView = "Full table"; Settle();
            Assert.False(tiles.IsEffectivelyVisible);
            Assert.True(table.IsEffectivelyVisible);
            Snap("run72-schedule-table.png");
            page.GameView = "Cards"; Settle();
            // Back on the tiles, the side panel returns beside them (it had stayed under them for ever).
            var side = view.GetVisualDescendants().OfType<StackPanel>().Single(x => x.Name == "Side");
            Assert.Equal(0, Grid.GetRow(side));

            // H20: play on until your club is out with the league still going.
            int guard = 0;
            string? over = null;
            while (!hl.Season.IsComplete && s.Phase != SessionPhase.Offseason && guard++ < 260)
            {
                s.AdvanceDay();
                if (hl.Season.Stage == CompetitionSeason.RegularSeason) continue;
                if (vm.CurrentPage == page && page.ShowPlayoffs && page.Series.Count > 0)
                    Assert.All(page.Series, x => { Assert.NotNull(x.High); Assert.NotNull(x.Low); });
                over = MainViewModel.SeasonOverText(s);
                if (over != null) break;
                if (guard % 10 == 0) { vm.AfterTick(); page.Refresh(); }
            }
            Assert.NotNull(over);
            vm.AfterTick();
            Assert.True(vm.SeasonOver);
            Assert.False(vm.ShowNextGameButton);
            Assert.True(vm.ShowSimButtons);   // Sim week and Continue still move on
            page.Refresh(); Settle();
            Assert.StartsWith(char.ToUpper(over![0]) + over[1..] + ": the rest of the playoffs go on without you", page.SeasonOver);
            Assert.DoesNotContain(page.Games, g => g.IsNext);
            Assert.True(page.ShowPlayoffs);
            Assert.All(page.Series, x => { Assert.NotNull(x.High); Assert.NotNull(x.Low); });
            var results = view.GetVisualDescendants().OfType<DockPanel>().Single(x => x.Name == "Results");
            Assert.True(results.Bounds.Height >= 300, $"results {results.Bounds.Height}");
            Assert.True(tiles.Bounds.Height >= 150, $"tiles {tiles.Bounds.Height}");
            Snap("run72-schedule-out.png");
            // The table in the playoffs at this width sits above the side panel, and keeps room too.
            page.GameView = "Full table"; Settle();
            Assert.True(results.Bounds.Height >= 300, $"table results {results.Bounds.Height}");
            Snap("run72-schedule-out-table.png");
            page.GameView = "Cards";
            vm.Navigate("Home"); Settle();
            var home = Assert.IsType<HomeViewModel>(vm.CurrentPage);
            Assert.Equal(char.ToUpper(over[0]) + over[1..], home.NextGame);
            Assert.Contains("go on without you", home.NextDetail);
            Snap("run72-home-out.png");
        }
        finally
        {
            window?.Close();
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            foreach (var f in new[] { path, path + ".bak" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
