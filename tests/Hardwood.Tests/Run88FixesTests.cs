using Hardwood.Core;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

/// <summary>Run 88. Bug hunt H56 (the NBA end of summer), H64 (History names overseas players) and H58 (Coach of the Year
/// names the coach).</summary>
[Collection(SharedUiState.Name)]
public class Run88FixesTests
{
    /// <summary>H56, `CriticBot nba-real-strong 3 31`: the Spurs opened 2027-28 at $172.93M on a $171.90M budget. Your
    /// sporting director made up the roster at the end of the summer, the last of it a minimum deal that may go over the
    /// budget; the owner now funds it (he isn't in debt), as run 87 did abroad and for your own minimum offers.</summary>
    [Fact]
    public void The_sporting_directors_end_of_summer_fill_is_funded_for_your_club()
    {
        var (league, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(31);
        var rng = new Rng(32);
        var outcome = new SeasonSimulator(rng).Run(league, ScheduleBuilder.Build(league, new DateOnly(2026, 10, 21), rng));
        var off = new Offseason(league, outcome, new Dictionary<int, PlayerSeasonStats>(), rng);
        var sas = league.Teams.First(t => t.Abbreviation == "SAS");
        var lal = league.Teams.First(t => t.Abbreviation == "LAL");
        off.HumanTeamId = sas.Id;
        off.Begin();
        off.AiReSignAll(sas.Id);
        off.AdvanceToDraft();
        off.AutoDraftUntil(null);
        off.AdvanceToFreeAgency();
        Assert.True(league.Finance.OpenMarket);

        // Both clubs short of the minimum, with budgets set at exactly what's signed (the floor's case) and money in the bank.
        foreach (var t in new[] { sas, lal })
        {
            while (t.Roster.Count > league.Finance.RosterMin - 2) off.Waive(t, t.Roster.OrderBy(p => p.Contract?.Salary ?? 0).First());
            t.Money.WageBudget = ContractMarket.Payroll(t);
            t.Money.Balance = 50_000_000;
        }
        long lalBudget = lal.Money.WageBudget;
        // Run 118 (H110): every real club opens at 14 after the expansion draft, so the computer clubs take 30 more of this
        // bare league's pool (a career shares the world's) before the minimum fill; top it up so the test still reaches it.
        int next = league.Teams.SelectMany(t => t.Roster).Concat(league.FreeAgents).Max(p => p.Id) + 1;
        var gen = new Hardwood.Core.Generation.PlayerGenerator(new Rng(118), next);
        for (int i = 0; i < 60; i++) league.FreeAgents.Add(gen.Generate((Position)(i % 5), 95, 27, new DateOnly(2027, 7, 1)));
        off.Complete();

        Assert.True(sas.Roster.Count >= league.Finance.RosterMin);
        Assert.True(ContractMarket.Payroll(sas) <= sas.Money.WageBudget, $"{ContractMarket.Payroll(sas)} on {sas.Money.WageBudget}");
        Assert.True(sas.Money.WageBudget - ContractMarket.Payroll(sas) < 100_000);
        // Computer clubs don't answer to a wage goal: their budgets aren't touched (results elsewhere unchanged).
        Assert.Equal(lalBudget, lal.Money.WageBudget);
    }

    /// <summary>H64, bottom seed 9: the 2026-27 card read "MVP Giannis Antetokounmpo" in 2027 and "MVP -" in 2029 after he
    /// moved abroad. The lookup now includes players at clubs abroad.</summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void History_still_names_an_award_winner_who_moved_abroad()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-run88-{Guid.NewGuid():N}.hwsave");
        try
        {
            var (probe, _) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(2026);
            var s = GameSession.NewGame(2026, probe.Teams.First(t => t.Abbreviation == "BOS").Id, path);
            var abroad = s.World.Players.First();
            Assert.DoesNotContain(s.League.AllActivePlayers.Concat(s.League.FreeAgents).Concat(s.League.Retired), p => p.Id == abroad.Id);
            s.League.History.Add(new SeasonSummary { Season = 2025, ChampionTeamId = s.HumanTeam.Id, MvpPlayerId = abroad.Id, ScoringLeaderPlayerId = abroad.Id,
                Records = s.League.Teams.Select(t => (t.Id, 41, 41)).ToList() });
            var vm = new Hardwood.Desktop.ViewModels.MainViewModel();
            vm.AttachSession(s);
            vm.Navigate("History");
            var h = (Hardwood.Desktop.ViewModels.HistoryViewModel)vm.CurrentPage!;
            Assert.Equal(abroad.Name, h.Rows.Single().Mvp);
            Assert.Equal(abroad.Name, h.SeasonCards.Single().Mvp);
            Assert.Contains(abroad.Name, h.SeasonCards.Single().Extra);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    /// <summary>H58: Coach of the Year keeps the coach's name with the season (older saves read as before).</summary>
    [Fact]
    public void Coach_of_the_year_keeps_the_coachs_name()
    {
        var a = new SeasonAwards { Season = 2026, CoachOfYearTeamId = 1, CoachOfYearName = "Sam Ortiz" };
        var back = System.Text.Json.JsonSerializer.Deserialize<SeasonAwards>(System.Text.Json.JsonSerializer.Serialize(a))!;
        Assert.Equal("Sam Ortiz", back.CoachOfYearName);
        var old = System.Text.Json.JsonSerializer.Deserialize<SeasonAwards>("{\"Season\":2025,\"CoachOfYearTeamId\":1,\"CoachOfYearNote\":\"x\"}")!;
        Assert.Equal("", old.CoachOfYearName);
    }
}
