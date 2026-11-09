using Hardwood.Core;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Season;
using Xunit;

namespace Hardwood.Tests;

public class SessionTests
{
    [Fact]
    public void Full_season_as_human_team_with_reload_and_offseason()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hw-session-{Guid.NewGuid():N}.save");
        try
        {
            var s = GameSession.NewGame(77, humanTeamId: 1, path);
            var team = s.HumanTeam;
            Assert.Equal(SessionPhase.Preseason, s.Phase);
            Assert.True(s.Inbox.Count >= 1);

            // Human rotation: top 10 by overall, starters by position.
            var chart = new DepthChart();
            foreach (var pos in Enum.GetValues<Position>())
                chart.Starters.Add(team.Roster.Where(p => p.PrimaryPosition == pos).OrderByDescending(p => p.Overall).First().Id);
            var starters = chart.Starters.Select(id => team.Roster.First(p => p.Id == id)).OrderByDescending(p => p.Overall).ToList();
            var bench = team.Roster.Where(p => !chart.Starters.Contains(p.Id)).OrderByDescending(p => p.Overall).Take(5).ToList();
            var rotation = starters.Concat(bench).ToList();
            double[] mins = { 36, 34, 32, 30, 28, 24, 20, 16, 12, 8 };
            for (int i = 0; i < 10; i++) chart.MinutesTargets[rotation[i].Id] = mins[i];
            team.DepthChart = chart;
            team.Tactic.ThreePointEmphasis = 70;

            // Play 60 days, then reload and check the world is the same.
            for (int i = 0; i < 60; i++) s.AdvanceDay();
            Assert.Equal(SessionPhase.RegularSeason, s.Phase);
            int playedBefore = s.Results.Count;
            var recBefore = (s.Standings[team].Wins, s.Standings[team].Losses);
            // The engine should land within a few minutes of each healthy rotation player's target.
            foreach (var kv in chart.MinutesTargets)
            {
                var player = team.Roster.First(p => p.Id == kv.Key);
                if (player.InjuryHistory.Count > 0) continue;
                Assert.InRange(s.Stats[kv.Key].Mpg, kv.Value - 5, kv.Value + 5);
            }

            var loaded = GameSession.Load(path);
            Assert.Equal(s.Today, loaded.Today);
            Assert.Equal(playedBefore, loaded.Results.Count);
            Assert.Equal(recBefore, (loaded.Standings[loaded.HumanTeam].Wins, loaded.Standings[loaded.HumanTeam].Losses));
            Assert.Equal(s.Inbox.Count, loaded.Inbox.Count);
            Assert.NotNull(loaded.HumanTeam.DepthChart);
            Assert.Equal(5, loaded.HumanTeam.DepthChart!.Starters.Count);
            Assert.True(loaded.HumanResults.All(x => x.result.Home.Lines.Count > 0)); // human box scores restored

            // A trade with an AI team: propose our worst player for their best, expect rejection; then a fair one.
            var other = loaded.League.Teams[5];
            var bad = new TradeProposal { From = loaded.HumanTeam, To = other };
            bad.FromPlayers.Add(loaded.HumanTeam.Roster.OrderBy(p => p.CurrentAbility).First());
            bad.ToPlayers.Add(other.Roster.OrderByDescending(p => p.CurrentAbility).First());
            Assert.False(loaded.ProposeTrade(bad).Accepted);

            // Play to the end of the regular season and through the postseason.
            int guard = 0;
            while (loaded.Phase != SessionPhase.Offseason && guard++ < 400) loaded.AdvanceDay();
            Assert.Equal(SessionPhase.Offseason, loaded.Phase);
            Assert.NotNull(loaded.Offseason);
            Assert.Single(loaded.League.History);
            Assert.Equal(1230, loaded.Results.Count);
            Assert.Equal(82, loaded.Standings[loaded.HumanTeam].Games);
            Assert.True(loaded.Postseason!.Complete);
            Assert.Equal(OffseasonPhase.ReSigning, loaded.Offseason!.Phase);

            // Re-sign everyone we can, release the rest.
            var off = loaded.Offseason;
            foreach (var p in off.Expiring[loaded.HumanTeam.Id].ToList())
                if (!off.ReSign(loaded.HumanTeam, p)) off.Release(loaded.HumanTeam, p);

            loaded.AdvanceOffseason(); // to draft
            int picksMade = 0;
            while (loaded.Offseason.Phase == OffseasonPhase.Draft && !loaded.Offseason.DraftComplete)
            {
                Assert.Equal(loaded.HumanTeam, loaded.Offseason.OnTheClock);
                loaded.MakePick(loaded.Offseason.Prospects.First());
                picksMade++;
            }
            Assert.Equal(2, picksMade);
            Assert.Equal(OffseasonPhase.FreeAgency, loaded.Offseason.Phase);

            // Sign free agents until the roster is full or nothing fits.
            foreach (var fa in loaded.League.FreeAgents.OrderByDescending(p => p.CurrentAbility).ToList())
                if (loaded.HumanTeam.Roster.Count >= 15) break; else loaded.Offseason.SignFreeAgent(loaded.HumanTeam, fa);

            Assert.Equal(2027, loaded.World.SummerStarted); // the world's summer began with the NABL's
            loaded.AdvanceOffseason(); // complete → new season
            Assert.Equal(SessionPhase.Preseason, loaded.Phase);
            Assert.Equal(2027, loaded.World.SummerEnded);
            Assert.All(loaded.World.Clubs, t => Assert.True(t.Roster.Count >= Hardwood.Core.World.GameWorld.RosterSize, t.FullName));
            Assert.Equal(2027, loaded.League.Season);
            Assert.InRange(loaded.HumanTeam.Roster.Count, 13, 15);
            Assert.True(ContractMarket.Payroll(loaded.HumanTeam) <= loaded.League.Finance.Apron + loaded.League.Finance.MinSalary * 2);
            Assert.Empty(loaded.Results);

            // Reload in the new preseason and keep playing a week.
            var again = GameSession.Load(path);
            Assert.Equal(2027, again.League.Season);
            Assert.Equal(SessionPhase.Preseason, again.Phase);
            for (int i = 0; i < 10; i++) again.AdvanceDay();
            Assert.True(again.Results.Count > 0);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Lottery_odds_favor_the_worst_teams_but_not_absolutely()
    {
        var league = new WorldGenerator(new Rng(3)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var reverse = league.Teams.ToList();
        var playoff = reverse.Skip(14).Select(t => t.Id).ToHashSet();
        var rng = new Rng(8);
        int worstGotFirst = 0, worstFellToFifth = 0;
        for (int i = 0; i < 2000; i++)
        {
            var result = DraftLottery.Run(reverse, playoff, rng);
            if (result.Order[0] == reverse[0]) worstGotFirst++;
            if (result.Order.IndexOf(reverse[0]) == 4) worstFellToFifth++;
            Assert.Equal(30, result.Order.Count);
            Assert.Equal(30, result.Order.Distinct().Count());
            // Playoff teams never move into the top 14.
            Assert.All(result.Order.Skip(14), t => Assert.Contains(t.Id, playoff));
        }
        Assert.InRange(worstGotFirst / 2000.0, 0.10, 0.18);
        Assert.True(worstFellToFifth > 0);
    }

    [Fact]
    public void Trade_engine_rejects_robbery_and_accepts_overpay()
    {
        var league = new WorldGenerator(new Rng(5)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var engine = new TradeEngine(league, new DateOnly(2026, 12, 1));
        var a = league.Teams[0]; var b = league.Teams[1];

        var robbery = new TradeProposal { From = a, To = b };
        robbery.FromPlayers.Add(a.Roster.OrderBy(p => p.CurrentAbility).First());
        robbery.ToPlayers.Add(b.Roster.OrderByDescending(p => p.CurrentAbility).First());
        Assert.False(engine.Evaluate(robbery).Accepted);

        // Our best player for their player of similar salary but clearly lower ability: cap-neutral, value-positive for them.
        var star = a.Roster.OrderByDescending(p => p.CurrentAbility).First();
        var filler = b.Roster.Where(p => p.CurrentAbility < star.CurrentAbility - 15)
            .OrderBy(p => Math.Abs(p.Contract!.Salary - star.Contract!.Salary)).First();
        var overpay = new TradeProposal { From = a, To = b };
        overpay.FromPlayers.Add(star);
        overpay.ToPlayers.Add(filler);
        // Make the test independent of exactly how far each side sits from the cap.
        foreach (var p in a.Roster.Concat(b.Roster)) if (p.Contract != null) p.Contract.Salary = (long)(p.Contract.Salary * 0.7);
        var eval = engine.Evaluate(overpay);
        Assert.True(eval.Legal, eval.LegalityNote);
        Assert.True(eval.Accepted);

        int aBefore = a.Roster.Count, bBefore = b.Roster.Count;
        engine.Execute(overpay);
        Assert.Equal(aBefore, a.Roster.Count);
        Assert.Equal(bBefore, b.Roster.Count);
        Assert.All(a.Roster, p => Assert.Equal(a.Id, p.TeamId));
    }

    [Fact]
    public void Injured_players_do_not_play()
    {
        var league = new WorldGenerator(new Rng(13)).Generate("L", 2026, new DateOnly(2026, 10, 1));
        var team = league.Teams[0];
        var star = team.Roster.OrderByDescending(p => p.Overall).First();
        var date = new DateOnly(2026, 11, 1);
        star.Injury = new Injury { Name = "Test", Severity = InjurySeverity.Moderate, Occurred = date, ReturnDate = date.AddDays(10) };
        var r = new Core.Engine.GameSimulator().Simulate(team, league.Teams[1], Rules.Nba, 1, date: date);
        Assert.Equal(0, r.Home.LineFor(star).Seconds);
        Assert.InRange(r.Home.Lines.Sum(l => l.Minutes), 240 + 25 * r.Overtimes - 0.5, 240 + 25 * r.Overtimes + 0.5);
    }
}
