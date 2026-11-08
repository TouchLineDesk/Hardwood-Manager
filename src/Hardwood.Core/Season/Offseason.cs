using Hardwood.Core.Generation;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

public sealed record DraftPick(int Round, int Pick, Team Team, Player Player);

public sealed class OffseasonReport
{
    public int Season { get; init; }
    public List<DevelopmentReport> Development { get; } = new();
    public List<Player> Retired { get; } = new();
    public List<(Player player, Team team)> ReSigned { get; } = new();
    public List<(Player player, Team from)> ToFreeAgency { get; } = new();
    public List<DraftPick> Draft { get; } = new();
    public List<(Player player, Team team, long salary, int years)> FreeAgentSignings { get; } = new();
    public List<(Player player, Team team)> Waived { get; } = new();
    public List<Player> LeftUnsigned { get; } = new();
}

/// <summary>
/// Turns one finished season into the next: progression and aging, retirements, contract expiry under a hard cap,
/// a two-round draft of generated rookies, free agency, roster trimming, and the season counter.
/// </summary>
public sealed class Offseason
{
    private readonly Rng _rng;
    private readonly DevelopmentEngine _dev;

    public Offseason(Rng rng, DevelopmentConfig? devConfig = null)
    {
        _rng = rng;
        _dev = new DevelopmentEngine(rng, devConfig);
    }

    public OffseasonReport Run(League league, SeasonOutcome outcome, Dictionary<int, PlayerSeasonStats> stats)
    {
        int season = league.Season;
        var offseasonDate = new DateOnly(season + 1, 7, 1);
        var report = new OffseasonReport { Season = season };
        var fin = league.Finance;

        RecordHistory(league, outcome, stats);

        // 1. Development and aging for everyone in the world (free agents too).
        foreach (var p in league.AllActivePlayers.Concat(league.FreeAgents).ToList())
        {
            double mpg = stats.TryGetValue(p.Id, out var s) ? s.Mpg : 0;
            report.Development.Add(_dev.Develop(p, offseasonDate, mpg));
        }

        // 2. Retirements.
        foreach (var team in league.Teams)
        {
            foreach (var p in team.Roster.ToList())
            {
                double mpg = stats.TryGetValue(p.Id, out var s) ? s.Mpg : 0;
                if (!_dev.ShouldRetire(p, offseasonDate, mpg)) continue;
                Retire(league, team, p, report);
            }
        }
        foreach (var p in league.FreeAgents.ToList())
        {
            if (_dev.ShouldRetire(p, offseasonDate, 0) || p.CurrentAbility < 60)
            {
                league.FreeAgents.Remove(p);
                league.Retired.Add(p);
                report.Retired.Add(p);
            }
        }

        // 3. Draft first (rookie deals are cheap and known), then contracts: tick down and handle expiries under the hard cap.
        foreach (var team in league.Teams)
            foreach (var p in team.Roster) if (p.Contract != null) p.Contract.Years--;
        RunDraft(league, outcome, offseasonDate, report);

        foreach (var team in league.Teams)
        {
            var expiring = team.Roster.Where(p => p.Contract == null || p.Contract.Years <= 0)
                .OrderByDescending(p => ContractMarket.Value(p, offseasonDate)).ToList();
            foreach (var p in expiring)
            {
                long ask = ContractMarket.MarketSalary(p.CurrentAbility, p.Age(offseasonDate), fin);
                long payrollWithout = team.Roster.Where(x => x != p && x.Contract != null && x.Contract.Years > 0).Sum(x => x.Contract!.Salary);
                bool fits = payrollWithout + ask <= fin.HardCap;
                // Keep the good ones if the money works; the player's loyalty nudges the odds.
                double wantToKeep = Math.Clamp((ContractMarket.Value(p, offseasonDate) - 72) / 50.0, 0.05, 0.95);
                double wantToStay = 0.6 + p.Hidden.Loyalty / 20.0 * 0.35;
                if (fits && _rng.Chance(wantToKeep * wantToStay))
                {
                    p.Contract = new Contract { Years = ContractMarket.ContractLength(p, offseasonDate, _rng), Salary = ask, StartSeason = season + 1 };
                    report.ReSigned.Add((p, team));
                }
                else
                {
                    team.Roster.Remove(p);
                    p.TeamId = null;
                    p.Contract = null;
                    league.FreeAgents.Add(p);
                    report.ToFreeAgency.Add((p, team));
                }
            }
        }

        // 4. Free agency: fill rosters to 13+ with the best fits that fit under the cap.
        RunFreeAgency(league, offseasonDate, report);

        // 5. Trim rosters over the maximum and get under the hard cap; waived players join the pool, then one more pass of free agency.
        foreach (var team in league.Teams)
        {
            while (team.Roster.Count > fin.RosterMax ||
                   (ContractMarket.Payroll(team) > fin.HardCap && team.Roster.Count > fin.RosterMin))
            {
                // Over the cap: shed the worst value per dollar; over the roster limit: shed the least valuable.
                var cut = ContractMarket.Payroll(team) > fin.HardCap
                    ? team.Roster.OrderBy(p => ContractMarket.Value(p, offseasonDate) - (p.Contract?.Salary ?? 0) / 1e6 * 1.5).First()
                    : team.Roster.OrderBy(p => ContractMarket.Value(p, offseasonDate)).First();
                team.Roster.Remove(cut);
                cut.TeamId = null; cut.Contract = null;
                league.FreeAgents.Add(cut);
                report.Waived.Add((cut, team));
            }
        }
        RunFreeAgency(league, offseasonDate, report);

        // Unsigned players: the weak and the old leave the pool, and the pool itself is capped (the rest play abroad).
        foreach (var p in league.FreeAgents.ToList())
        {
            if (p.CurrentAbility < 80 || p.Age(offseasonDate) >= 35)
            {
                league.FreeAgents.Remove(p);
                league.Retired.Add(p);
                report.LeftUnsigned.Add(p);
            }
        }
        foreach (var p in league.FreeAgents.OrderByDescending(p => ContractMarket.Value(p, offseasonDate)).Skip(90).ToList())
        {
            league.FreeAgents.Remove(p);
            league.Retired.Add(p);
            report.LeftUnsigned.Add(p);
        }

        league.Season = season + 1;
        return report;
    }

    private static void RecordHistory(League league, SeasonOutcome outcome, Dictionary<int, PlayerSeasonStats> stats)
    {
        var finals = outcome.Playoffs.Last().Series;
        var scoring = stats.Values.Where(s => s.Games >= 50).OrderByDescending(s => s.Ppg).FirstOrDefault();
        // MVP stub: best per-game production on a winning team.
        var teamWins = league.Teams.ToDictionary(t => t.Id, t => outcome.Standings[t].Wins);
        var mvp = stats.Values.Where(s => s.Games >= 55 && teamWins[s.TeamId] >= 41)
            .OrderByDescending(s => s.Ppg + s.Rpg * 1.2 + s.Apg * 1.5 + s.PlusMinus / 20.0).FirstOrDefault();

        league.History.Add(new SeasonSummary
        {
            Season = league.Season,
            ChampionTeamId = finals.Winner.Id,
            RunnerUpTeamId = finals.Loser.Id,
            Records = league.Teams.Select(t => (t.Id, outcome.Standings[t].Wins, outcome.Standings[t].Losses)).ToList(),
            ScoringLeaderPlayerId = scoring?.PlayerId,
            MvpPlayerId = mvp?.PlayerId,
        });
    }

    private void Retire(League league, Team team, Player p, OffseasonReport report)
    {
        team.Roster.Remove(p);
        p.TeamId = null; p.Contract = null;
        league.Retired.Add(p);
        report.Retired.Add(p);
    }

    private void RunDraft(League league, SeasonOutcome outcome, DateOnly date, OffseasonReport report)
    {
        var fin = league.Finance;
        int season = league.Season;

        // Draft order: reverse regular-season record (no lottery yet).
        var order = outcome.Standings.All().Select(r => r.Team).Reverse().ToList();

        // Prospect pool: 75 players aged 19–22 with a steep talent curve.
        var gen = new PlayerGenerator(_rng, league.NextPlayerId);
        var prospects = new List<Player>();
        var positions = Enum.GetValues<Position>();
        for (int i = 0; i < 75; i++)
        {
            int age = _rng.Next(19, 23);
            // A few blue-chippers, a solid middle, a long tail.
            double u = _rng.NextDouble();
            int ca = u < 0.05 ? _rng.Next(105, 125) : u < 0.30 ? _rng.Next(85, 105) : _rng.Next(55, 88);
            var p = gen.Generate(positions[_rng.Next(5)], ca, age, date);
            // Prospects get extra headroom: the draft is about potential.
            p.PotentialAbility = Math.Clamp(p.PotentialAbility + _rng.Next(0, 25), p.CurrentAbility, 200);
            prospects.Add(p);
        }
        league.NextPlayerId = gen.NextId;

        // AI picks: best available by front-office value with a little noise (imperfect scouting).
        for (int round = 1; round <= 2; round++)
        {
            for (int i = 0; i < order.Count; i++)
            {
                var team = order[i];
                var pick = prospects
                    .OrderByDescending(p => ContractMarket.Value(p, date) + _rng.NextGaussian(0, 6) + PositionNeed(team, p) * 4)
                    .First();
                prospects.Remove(pick);
                int overall = (round - 1) * order.Count + i + 1;
                report.Draft.Add(new DraftPick(round, overall, team, pick));
                if (round == 1)
                {
                    long salary = (long)(fin.RookieScaleFirstPick * Math.Pow(0.93, overall - 1));
                    pick.Contract = new Contract { Years = 4, Salary = Math.Max(salary, fin.MinSalary), StartSeason = season + 1, TeamOption = true };
                    pick.TeamId = team.Id;
                    team.Roster.Add(pick);
                }
                else
                {
                    // Second-round picks are not guaranteed: they compete for a spot in free agency like everyone else.
                    league.FreeAgents.Add(pick);
                }
            }
        }
        // Undrafted prospects enter free agency.
        league.FreeAgents.AddRange(prospects);
    }

    private static int PositionNeed(Team team, Player p)
    {
        int count = team.Roster.Count(x => x.PrimaryPosition == p.PrimaryPosition);
        return count <= 1 ? 2 : count == 2 ? 1 : 0;
    }

    private void RunFreeAgency(League league, DateOnly date, OffseasonReport report)
    {
        var fin = league.Finance;
        // Several rounds: teams with the most open spots and most cap room choose first each round.
        for (int round = 0; round < 6; round++)
        {
            var teams = league.Teams.Where(t => t.Roster.Count < fin.RosterMax)
                .OrderBy(t => t.Roster.Count).ThenByDescending(t => fin.HardCap - ContractMarket.Payroll(t)).ToList();
            bool any = false;
            foreach (var team in teams)
            {
                long room = fin.HardCap - ContractMarket.Payroll(team);
                bool mustFill = team.Roster.Count < fin.RosterMin;
                var pick = league.FreeAgents
                    .Select(p => (p, ask: ContractMarket.MarketSalary(p.CurrentAbility, p.Age(date), fin)))
                    .Where(x => x.ask <= room || (mustFill && x.ask <= fin.MinSalary * 2))
                    .OrderByDescending(x => ContractMarket.Value(x.p, date) + PositionNeed(team, x.p) * 5)
                    .FirstOrDefault();
                if (pick.p == null) continue;

                // Below the minimum roster a team may sign minimum deals regardless of cap room (roster-minimum exception).
                long salary = pick.ask <= room ? pick.ask : fin.MinSalary;
                int years = ContractMarket.ContractLength(pick.p, date, _rng);
                pick.p.Contract = new Contract { Years = years, Salary = salary, StartSeason = league.Season + 1 };
                pick.p.TeamId = team.Id;
                team.Roster.Add(pick.p);
                league.FreeAgents.Remove(pick.p);
                report.FreeAgentSignings.Add((pick.p, team, salary, years));
                any = true;
            }
            if (!any) break;
        }
    }
}
