using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 64: League Leaders and Leagues → Stats open on leader cards (review #13 rank 3, Danny's #464):
/// a card per stat with the leader big (face, club colour, the number) and 2nd to 5th below; the full table is
/// one click away, and a card's "Full list" opens that stat as the table.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run64LeaderCardTests
{
    [Theory]
    [InlineData(1180, 290, 4, 4)]
    [InlineData(900, 290, 4, 3)]
    [InlineData(500, 290, 4, 1)]
    [InlineData(100, 290, 4, 1)]
    [InlineData(5000, 290, 4, 4)]
    [InlineData(double.PositiveInfinity, 290, 4, 4)]
    public void Card_grid_fits_its_columns_to_the_width(double width, double min, int max, int expected) =>
        Assert.Equal(expected, CardGrid.ColumnsFor(width, min, max));

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Leaders_open_on_cards_and_the_table_is_one_click_away()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var path = Path.Combine(Path.GetTempPath(), $"hw-run64-{Guid.NewGuid():N}.hwsave");
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

            // Before a game: every card says when it fills in, and nothing binds through a null.
            vm.Navigate("League Leaders"); Settle();
            var empty = (LeadersViewModel)vm.CurrentPage!;
            Assert.True(empty.ShowCards);
            Assert.Equal(LeaderCards.Stats.Length, empty.Cards.Count);
            if (empty.Cards.All(c => c.IsEmpty)) Assert.EndsWith("the leaders fill in after the first games.", empty.Note);   // run 71: the league's name first

            // A month in.
            while (s.Phase != SessionPhase.RegularSeason || s.Stats.Values.Count(x => x.Games >= 10) < 100) s.AdvanceDay();
            vm.AfterTick();
            vm.Navigate("Home"); Settle();
            vm.Navigate("League Leaders"); Settle();
            var lv = (LeadersViewModel)vm.CurrentPage!;
            Assert.Equal("Cards", lv.View);
            Assert.Equal(new[] { "Points", "Rebounds", "Assists", "Steals", "Blocks", "FG%", "3P%", "Minutes" }, lv.Cards.Select(c => c.Category));
            foreach (var card in lv.Cards)
            {
                Assert.True(card.HasAny, card.Title);
                Assert.Equal(LeaderCards.PerCard, card.Entries.Count);
                Assert.Equal(Enumerable.Range(1, LeaderCards.PerCard), card.Entries.Select(e => e.Rank));
                Assert.Equal(LeaderCards.PerCard - 1, card.Rest.Count);
                Assert.NotNull(card.TopPlayer); Assert.NotNull(card.TopTeam);
                var values = card.Entries.Select(e => double.Parse(e.Value.TrimEnd('%'), System.Globalization.CultureInfo.CurrentCulture)).ToList();
                Assert.Equal(values.OrderByDescending(v => v), values);
            }
            // The points card's leader is the table's number one.
            var points = lv.Cards[0];
            lv.ShowFull("Points"); Settle();
            Assert.Equal(lv.Rows[0].Player, points.TopPlayer);
            Assert.Equal(lv.Rows[0].PPG.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture), points.TopValue);
            Assert.EndsWith("%", lv.Cards.Single(c => c.Category == "FG%").TopValue);

            // On screen: four cards a row at 1480 px, every card drawn, the table hidden.
            lv.View = "Cards"; Settle();
            var grid = window.GetVisualDescendantsOf<CardGrid>().Single(g => g.IsEffectivelyVisible);
            Assert.Equal(4, grid.Columns);
            var views = window.GetVisualDescendantsOf<LeaderCardView>().Where(v => v.IsEffectivelyVisible).ToList();
            Assert.Equal(8, views.Count);
            Assert.All(views, v => Assert.True(v.Bounds.Width >= 250, $"card {v.Bounds.Width:0} px wide"));
            Assert.DoesNotContain(window.GetVisualDescendantsOf<Avalonia.Controls.DataGrid>(), d => d.IsEffectivelyVisible);

            // A card's Full list opens that stat as the table.
            lv.Cards.Single(c => c.Category == "Assists").ShowAll!.Execute(null); Settle();
            Assert.Equal("Full table", lv.View);
            Assert.Equal("Assists", lv.Category);
            Assert.Contains(window.GetVisualDescendantsOf<Avalonia.Controls.DataGrid>(), d => d.IsEffectivelyVisible);
            Assert.Equal(lv.Rows.Max(r => r.APG), lv.Rows[0].APG);

            // Leagues → Stats: the same cards for any league.
            vm.Navigate("Leagues"); Settle();
            var wv = (WorldViewModel)vm.CurrentPage!;
            wv.Tab = 2; Settle();
            Assert.True(wv.ShowStatCards);
            Assert.Equal(LeaderCards.Stats.Length, wv.StatCards.Count);
            Assert.All(wv.StatCards, c => Assert.True(c.HasAny, c.Title));
            Assert.Contains(window.GetVisualDescendantsOf<LeaderCardView>(), v => v.IsEffectivelyVisible);
            wv.StatCards.Single(c => c.Category == "Rebounds").ShowAll!.Execute(null); Settle();
            Assert.Equal("Full table", wv.StatsView);
            Assert.Equal("Rebounds", wv.Category);
            Assert.Equal(wv.StatCards.Single(c => c.Category == "Rebounds").TopPlayer, wv.Leaders[0].Player);
        }
        finally
        {
            AppSettings.Use(oldSettings); AppSettings.FilePath = oldPath;
            foreach (var f in new[] { path, path + ".bak" }) if (File.Exists(f)) File.Delete(f);
        }
    }
}
