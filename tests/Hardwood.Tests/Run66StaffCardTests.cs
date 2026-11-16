using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Hardwood.Desktop;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Hardwood.Desktop.Views;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 66 (review #13 rank 5, Danny's #464): Staff &amp; Training opens on staff cards (a role badge, the five
/// ratings as bars with the role's key one first, the deal as a chip), a vacant role as a card that finds you one, and
/// the staff market as cards with roles as chips and a "vs yours" chip. Both tables are one click away.</summary>
[Collection(SharedUiState.Name)]   // it swaps AppSettings, app-wide state
public class Run66StaffCardTests
{
    [Fact]
    public void Bars_put_the_key_rating_first_and_compare_with_your_man()
    {
        var st = new Staff { Id = 7, FirstName = "Ann", LastName = "Lee", Role = StaffRole.Physio, Medical = 16, Coaching = 5, Scouting = 9 };
        var bars = StaffCards.Bars(st);
        Assert.Equal(5, bars.Count);
        Assert.Equal(("Med", 16, true), (bars[0].Label, bars[0].Value, bars[0].IsKey));
        Assert.Single(bars, b => b.IsKey);
        Assert.Same(StandingRow.PlayoffBrush, bars[0].Brush);
        Assert.Contains("16 of 20", bars[0].Tip);
        Assert.Equal(new[] { "Med", "Coa", "Dev", "Fit", "Sco" }, bars.Select(b => b.Label));
        Assert.Same(StandingRow.RelegationBrush, StaffCards.BandBrush(7));
        Assert.Equal(0.5, RatingBar.Share(10, 20));
        Assert.Equal(1, RatingBar.Share(25, 20));
        Assert.Equal(0, RatingBar.Share(3, 0));

        var mine = new Staff { Id = 8, FirstName = "Bo", LastName = "Hart", Role = StaffRole.Physio, Medical = 12 };
        var up = StaffViewModel.CompareWith(mine, st);
        Assert.Equal("+4 on Hart", up.Text); Assert.Same(StandingRow.PlayoffBrush, up.Brush);
        Assert.Equal("-4 on Lee", StaffViewModel.CompareWith(st, mine).Text);
        Assert.StartsWith("Level with", StaffViewModel.CompareWith(st, st).Text);
        Assert.Equal("Fills a vacancy", StaffViewModel.CompareWith(null, st).Text);

        var ours = new StaffRow { Staff = new Staff { Id = 9, FirstName = "C", LastName = "D", Role = StaffRole.Scout, ContractYears = 1, Salary = 400_000 } };
        Assert.Equal("Final year · $400K", ours.ContractChip);
        Assert.Same(StandingRow.PlayInBrush, ours.ContractBrush);
        var market = new StaffRow { Staff = ours.Staff, Pay = 1_250_000 };
        Assert.Equal("Asks $1.25M a season", market.ContractChip);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Staff_opens_on_cards_and_the_tables_are_one_click_away()
    {
        var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
        var path = Path.Combine(Path.GetTempPath(), $"hw-run66-{Guid.NewGuid():N}.hwsave");
        var oldSettings = AppSettings.Current; var oldPath = AppSettings.FilePath;
        AppSettings.FilePath = Path.Combine(Path.GetTempPath(), $"hw-settings-{Guid.NewGuid():N}.json");
        AppSettings.Use(new AppSettings { AutoPortraits = false });
        try
        {
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            // Leave the physio's job open so the vacant card shows.
            var physio = s.HumanTeam.Staff.FirstOrDefault(x => x.Role == StaffRole.Physio);
            if (physio != null) s.FireStaff(physio);
            var vm = new MainViewModel();
            var window = new MainWindow { DataContext = vm, Width = 1480, Height = 900 };
            window.Show();
            vm.AttachSession(s);
            void Settle() { window.UpdateLayout(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
            vm.Navigate("Staff & Training"); Settle();
            var st = Assert.IsType<StaffViewModel>(vm.CurrentPage);
            T Named<T>(string name) where T : Control => window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

            // Cards first: one per staff member, and the vacant physio as its own card.
            Assert.Equal("Cards", st.View);
            Assert.True(Named<ItemsControl>("OurCards").IsEffectivelyVisible);
            Assert.False(Named<DataGrid>("StaffOursGrid").IsEffectivelyVisible);
            Assert.False(Named<DataGrid>("StaffMarketGrid").IsEffectivelyVisible);
            Assert.Equal(s.HumanTeam.Staff.Count, st.Ours.Count);
            Assert.Contains(st.Vacant, v => v.Role == StaffRole.Physio);
            Assert.Equal(5, st.Ours.Count + st.Vacant.Count);
            Assert.All(st.Ours, r => { Assert.Equal(5, r.Bars.Count); Assert.True(r.Bars[0].IsKey); Assert.Equal(r.Key, r.Bars[0].Value); Assert.False(r.HasCompare); });

            // Pressing a card picks him; Release lights up and his renewal answer shows.
            var card = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("staffCard") && b.DataContext == st.Ours[0]);
            card.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            Assert.Same(st.Ours[0], st.SelectedOurs);
            Assert.True(st.Ours[0].Picked);
            Assert.False(string.IsNullOrEmpty(st.Renewal));
            st.PickOurs(st.Ours[1]);
            Assert.False(st.Ours[0].Picked); Assert.True(st.Ours[1].Picked);

            // The market: the best 12 by key rating, the same order as the table; role chips with counts that add up.
            Assert.Equal(st.Market.Take(StaffViewModel.CardCount), st.MarketCards);
            Assert.Equal(st.RoleFilters, st.RoleChips.Select(c => c.Name));
            int all = int.Parse(st.RoleChips[0].Record.Replace(",", ""));
            Assert.Equal(all, st.RoleChips.Skip(1).Sum(c => int.Parse(c.Record.Replace(",", ""))));
            Assert.All(st.MarketCards, r => Assert.True(r.HasCompare));

            // "Find one" on the vacant card shows physios, each one filling the vacancy.
            var find = window.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is VacantRole { Role: StaffRole.Physio } && (b.Content as string)?.StartsWith("Find") == true);
            find.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            Assert.Equal("Physio", st.RoleFilter);
            Assert.True(st.RoleChips.Single(c => c.Name == "Physio").Selected);
            Assert.All(st.Market, r => Assert.Equal(StaffRole.Physio, r.Staff.Role));
            Assert.All(st.MarketCards, r => Assert.Equal("Fills a vacancy", r.Compare));

            // A role chip; a market card picks him for Interview.
            var chip = window.GetVisualDescendants().OfType<Button>().First(b => b.DataContext is FilterChip { Name: "Head scout" });
            chip.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
            Assert.Equal("Head scout", st.RoleFilter);
            st.SetRole("nonsense"); Assert.Equal("Head scout", st.RoleFilter);
            if (st.MarketCards.Count > 0)
            {
                var mc = window.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("staffCard") && b.DataContext == st.MarketCards[0]);
                mc.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Settle();
                Assert.Same(st.MarketCards[0], st.SelectedMarket);
                Assert.True(st.MarketCards[0].Picked);
                st.InterviewCandidate();
                Assert.False(string.IsNullOrEmpty(st.Interview));
                // A role that hides him drops the pick.
                st.SetRole("Physio"); Settle();
                Assert.Null(st.SelectedMarket);
                Assert.Equal("", st.Interview);
            }
            st.SetRole("All roles"); Settle();
            Assert.Contains(st.Market.Count <= StaffViewModel.CardCount ? "All " : $"The best {StaffViewModel.CardCount} of", st.MarketCardsNote);

            // The full tables.
            st.View = "Full table"; Settle();
            Assert.True(Named<DataGrid>("StaffMarketGrid").IsEffectivelyVisible);
            Assert.True(Named<DataGrid>("StaffOursGrid").IsEffectivelyVisible);
            Assert.False(Named<ItemsControl>("OurCards").IsEffectivelyVisible);
            Assert.False(Named<ItemsControl>("StaffMarketCards").IsEffectivelyVisible);

            if (Environment.GetEnvironmentVariable("HW_SCREENSHOT_DIR") is { Length: > 0 } dir)
            {
                using (var f = window.CaptureRenderedFrame()) f?.Save(Path.Combine(dir, "staff-table.png"));
                st.View = "Cards"; st.PickOurs(st.Ours[0]); Settle();
                using (var f = window.CaptureRenderedFrame()) f?.Save(Path.Combine(dir, "staff-cards.png"));
                var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First(x => x.IsEffectivelyVisible && x.Extent.Height > x.Viewport.Height);
                scroll.Offset = new Avalonia.Vector(0, Avalonia.VisualExtensions.TranslatePoint(Named<ItemsControl>("StaffMarketCards"), new Avalonia.Point(0, 0), (Avalonia.Visual)scroll.Content!)!.Value.Y - 120); Settle();
                using (var f = window.CaptureRenderedFrame()) f?.Save(Path.Combine(dir, "staff-market-cards.png"));
                vm.Guide.On = true; Settle();
                using (var f = window.CaptureRenderedFrame()) f?.Save(Path.Combine(dir, "staff-cards-assistant.png"));
            }
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
