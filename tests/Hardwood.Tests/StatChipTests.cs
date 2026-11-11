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
}
