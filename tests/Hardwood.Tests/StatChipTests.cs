using Avalonia.Headless.XUnit;
using Hardwood.Core.Season;
using Hardwood.Desktop.Controls;
using Hardwood.Desktop.ViewModels;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Roadmap 1, broadcast tokens: stat chips show the right numbers, and chart axes read in round numbers.</summary>
[Collection(SharedUiState.Name)]
public class StatChipTests
{
    [Theory]
    [InlineData(54, 64.4)]
    [InlineData(0, 100)]
    [InlineData(-145.8, 10.8)]
    [InlineData(54.1, 60.9)]
    [InlineData(3.2, 3.2)]
    [InlineData(0.12, 0.31)]
    [InlineData(-3000, 12000)]
    public void Axis_ticks_are_round_and_cover_the_data(double min, double max)
    {
        var t = LineChart.NiceTicks(min, max);
        Assert.InRange(t.Length, 3, 7);
        Assert.True(t[0] <= min && t[^1] >= max, string.Join(", ", t));
        double step = t[1] - t[0];
        for (int i = 1; i < t.Length; i++) Assert.Equal(step, t[i] - t[i - 1], 6);
        double mag = Math.Pow(10, Math.Floor(Math.Log10(step)));
        Assert.Contains(Math.Round(step / mag, 6), new[] { 1, 2, 2.5, 5, 10 });
        foreach (var v in t) Assert.Equal(0, Math.Round(v / step - Math.Round(v / step), 6));
    }

    [Fact]
    public void A_zero_to_100_meter_reads_in_quarters()
    {
        Assert.Equal(new[] { 0.0, 25, 50, 75, 100 }, LineChart.NiceTicks(0, 100));
    }

    [Theory]
    [InlineData(39_620_000L, "$39.62M")]
    [InlineData(1_500_000L, "$1.5M")]
    [InlineData(850_000L, "$850K")]
    public void Money_reads_short(long v, string text) => Assert.Equal(text, PlayerViewModel.Money(v));

    [AvaloniaFact]
    public void Chips_match_the_session_data()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-chips-{Guid.NewGuid():N}.save");
        try
        {
            var vm = new MainViewModel();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path;
            start.SeedText = "41";
            start.SelectedTeam = start.Teams.First();
            start.NewGame();
            var s = vm.Session!;
            for (int i = 0; i < 20; i++) s.AdvanceDay();
            var team = s.HumanTeam;

            // Player: salary, health and this season's points come straight from the player.
            var p = team.Roster.Where(x => x.Contract != null).OrderByDescending(x => x.Contract!.Salary).First();
            var pv = new PlayerViewModel(vm, p);
            Chip C(IEnumerable<Chip> chips, string label) => chips.Single(c => c.Label == label);
            Assert.Equal(PlayerViewModel.Money(p.Contract!.Salary), C(pv.Chips, "Salary").Value);
            Assert.Equal(p.Age(s.Today).ToString(), C(pv.Chips, "Age").Value);
            bool hurt = p.Injury is { } inj && inj.IsOut(s.Today);
            Assert.Equal(hurt ? "bad" : "good", C(pv.Chips, "Status").Tone);
            if (s.Stats.TryGetValue(p.Id, out var st) && st.Games > 0) Assert.Equal(st.Ppg.ToString("0.0"), C(pv.Chips, "Points").Value);
            Assert.Equal(Hardwood.Core.People.PeopleEngine.Mood(p.Morale), C(pv.Chips, "Mood").Value);

            // Team page: payroll is the sum of contracts, and the record matches the table.
            var tv = new TeamViewModel(vm, team);
            tv.Refresh();
            long payroll = ContractMarket.Payroll(team);
            Assert.Equal(PlayerViewModel.Money(payroll), C(tv.Chips, "Payroll").Value);
            Assert.Equal(payroll > team.Money.WageBudget ? "bad" : payroll > team.Money.WageBudget * 0.95 ? "warn" : "good", C(tv.Chips, "Payroll").Tone);
            Assert.Equal($"{s.MyRecord.Wins}-{s.MyRecord.Losses}", C(tv.Chips, "Record").Value);

            // Board: wages and wage room agree with the payroll and the budget.
            var bv = new BoardViewModel(vm);
            bv.Refresh();
            Assert.Equal(6, bv.FinanceChips.Count);
            Assert.Contains(C(bv.FinanceChips, "Wages").Value, new[] { $"${payroll / 1e6:F0}M", $"${payroll / 1e6:F1}M" });
            Assert.Equal(payroll > team.Money.WageBudget ? "bad" : "", C(bv.FinanceChips, "Wage room").Tone);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [AvaloniaFact]
    public void Roster_squad_and_box_score_chips_match_the_session_data()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-chips2-{Guid.NewGuid():N}.save");
        try
        {
            var vm = new MainViewModel();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path;
            start.SeedText = "41";
            start.SelectedTeam = start.Teams.First();
            start.NewGame();
            var s = vm.Session!;
            for (int i = 0; i < 25; i++) s.AdvanceDay();
            var team = s.HumanTeam;
            Chip C(IEnumerable<Chip> chips, string label) => chips.Single(c => c.Label == label);

            // Roster: counts come straight from the squad.
            vm.Navigate("Roster");
            var rv = (RosterViewModel)vm.CurrentPage!;
            Assert.Equal(team.Roster.Count.ToString(), C(rv.Chips, "Players").Value);
            int injured = team.Roster.Count(p => p.Injury is { } inj && inj.IsOut(s.Today));
            Assert.Equal(injured.ToString(), C(rv.Chips, "Injured").Value);
            Assert.Equal(injured > 0 ? "bad" : "good", C(rv.Chips, "Injured").Tone);
            Assert.Equal(team.Roster.Count(p => p.Contract is { Years: <= 0 }).ToString(), C(rv.Chips, "Expiring").Value);
            Assert.Equal(PlayerViewModel.Money(ContractMarket.Payroll(team)), C(rv.Chips, "Wages").Value);
            Assert.Equal(team.Roster.OrderByDescending(p => p.CurrentAbility).First().Name.Split(' ').Last(), C(rv.Chips, "Best player").Value);
            // "Unhappy" agrees with the Mood column: anyone shown as Unhappy or Very unhappy, or asking out.
            Assert.Equal(team.Roster.Count(p => p.TradeRequested || Hardwood.Core.People.PeopleEngine.Mood(p.Morale) is "Unhappy" or "Very unhappy").ToString(), C(rv.Chips, "Unhappy").Value);

            // Squad: chemistry and trade requests.
            vm.Navigate("Squad");
            var sv = (SquadViewModel)vm.CurrentPage!;
            Assert.Equal($"{team.Chemistry:0}", C(sv.Chips, "Chemistry").Value);
            Assert.Equal(team.Roster.Count(p => p.TradeRequested).ToString(), C(sv.Chips, "Trade requests").Value);
            Assert.Equal(team.Roster.Count(p => !p.TradeRequested && Hardwood.Core.People.PeopleEngine.Mood(p.Morale) is "Unhappy" or "Very unhappy").ToString(), C(sv.Chips, "Unhappy").Value);
            Assert.Equal(team.Roster.Count(p => Hardwood.Core.People.PeopleEngine.Mood(p.Morale) is "Happy" or "Delighted").ToString(), C(sv.Chips, "Happy").Value);

            // Box score: the top scorer chip is the game's highest scorer; team chips compare the two sides.
            var game = s.Results.Values.First();
            var box = new BoxScoreViewModel(vm, game);
            int top = game.Away.Lines.Concat(game.Home.Lines).Max(l => l.Points);
            Assert.Equal($"{top} pts", C(box.Leaders, "Top scorer").Value);
            Assert.Equal((game.Away.OReb + game.Away.DReb).ToString(), C(box.AwayChips, "Rebounds").Value);
            bool awayMoreBoards = game.Away.OReb + game.Away.DReb > game.Home.OReb + game.Home.DReb;
            Assert.Equal(awayMoreBoards ? "good" : "", C(box.AwayChips, "Rebounds").Tone);
            Assert.Equal(game.Away.Turnovers < game.Home.Turnovers ? "good" : "", C(box.AwayChips, "Turnovers").Tone);

            // Staff: vacant roles and staff payroll come from the staff list.
            vm.Navigate("Staff & Training");
            var stv = (StaffViewModel)vm.CurrentPage!;
            int vacant = Enum.GetValues<Hardwood.Core.Model.StaffRole>().Count(r => team.Staff.All(x => x.Role != r));
            Assert.Equal(vacant.ToString(), C(stv.Chips, "Vacant roles").Value);
            Assert.Equal(PlayerViewModel.Money(team.Staff.Sum(x => x.Salary)), C(stv.Chips, "Staff payroll").Value);

            // Home: each Latest item carries its inbox category as a badge.
            vm.Navigate("Home");
            var hv = (HomeViewModel)vm.CurrentPage!;
            Assert.All(hv.News, r => Assert.False(string.IsNullOrEmpty(r.Badge)));

            // Player page: your own player gets a Talk button that opens the Squad page with him selected.
            var mine = team.Roster.First();
            var pv = new PlayerViewModel(vm, mine);
            Assert.True(pv.CanTalk);
            pv.TalkToHim();
            var squad = Assert.IsType<SquadViewModel>(vm.CurrentPage);
            Assert.Equal(mine, squad.Selected?.Player);
            var other = s.League.Teams.First(t => t != team).Roster.First();
            Assert.False(new PlayerViewModel(vm, other).CanTalk);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [AvaloniaFact]
    public void Transfer_deal_chips_show_the_fee_range_stance_and_interest()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-chips3-{Guid.NewGuid():N}.save");
        try
        {
            var vm = new MainViewModel();
            var start = (StartViewModel)vm.CurrentPage!;
            start.SavePath = path;
            start.SeedText = "41";
            start.SelectedTeam = start.Teams.First();
            start.NewGame();
            var s = vm.Session!;
            vm.Navigate("Transactions");
            var tx = (TransactionsViewModel)vm.CurrentPage!;
            Assert.NotEmpty(tx.Market);
            Chip C(IEnumerable<Chip> chips, string label) => chips.Single(c => c.Label == label);
            foreach (var row in tx.Market.Take(25).ToList())
            {
                tx.SelectedMarket = row;
                var likely = s.LikelyFee(row.Player);
                Assert.Equal(likely.Short, C(tx.DealChips, "Likely fee").Value);
                Assert.Equal(likely.Short, row.Ask);
                Assert.Equal(s.StanceOn(row.Player)!.Label, C(tx.DealChips, "Club stance").Value);
                Assert.Equal(s.StanceOn(row.Player)!.Label, row.Stance);
                bool comes = s.WontCome(row.Player) == null;
                Assert.Equal(comes ? "Yes" : "No", C(tx.DealChips, "Will he come?").Value);
                Assert.Equal(comes, row.Interest == "Yes");
                Assert.Equal(row.Player.Contract is { Buyout: > 0 }, tx.DealChips.Any(c => c.Label == "Release clause"));
                Assert.Equal((decimal)Math.Round(likely.Mid / 1e6, 2), tx.BidFee);
            }
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
