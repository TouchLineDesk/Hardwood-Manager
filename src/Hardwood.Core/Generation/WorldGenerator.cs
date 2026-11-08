using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.Generation;

/// <summary>
/// Builds the default 30-team closed-franchise league with 15-man rosters.
/// Team strength varies so there are contenders, mid-table teams and rebuilders.
/// </summary>
public sealed class WorldGenerator
{
    private readonly Rng _rng;

    public WorldGenerator(Rng rng) => _rng = rng;

    public League Generate(string leagueName, int season, DateOnly today)
    {
        var league = new League { Name = leagueName, Season = season, Rules = Rules.Nba };
        var players = new PlayerGenerator(_rng);
        try
        {

        for (int i = 0; i < NamePool.Franchises.Length; i++)
        {
            var (city, name, abbr) = NamePool.Franchises[i];
            bool east = i < 15;
            var divs = east ? NamePool.EastDivisions : NamePool.WestDivisions;
            var team = new Team
            {
                Id = i + 1,
                City = city,
                Name = name,
                Abbreviation = abbr,
                Conference = east ? Conference.East : Conference.West,
                Division = divs[(i % 15) / 5],
                ArenaCapacity = _rng.Next(17000, 21000),
            };

            // Team quality: a normal around league average. +-1 SD ≈ a 10-CA swing on the whole roster.
            double quality = _rng.NextGaussian(0, 8);
            BuildRoster(team, players, quality, today, league);
            league.Teams.Add(team);
        }
        }
        finally { league.NextPlayerId = players.NextId; }
        league.EnsurePicks();
        return league;
    }

    private void BuildRoster(Team team, PlayerGenerator gen, double quality, DateOnly today, League league)
    {
        // CA by roster slot (sorted best to worst), then shifted by team quality.
        int[] slotCa = { 158, 145, 135, 128, 122, 115, 110, 105, 100, 95, 90, 85, 80, 75, 70 };
        // Positions: ensure a balanced roster. 3 of each position.
        var positions = new List<Position>();
        for (int k = 0; k < 3; k++) positions.AddRange(Enum.GetValues<Position>());
        _rng.Shuffle(positions);

        for (int s = 0; s < 15; s++)
        {
            int ca = (int)Math.Round(slotCa[s] + quality + _rng.NextGaussian(0, 6));
            // Stars are rare: cap very high values unless the team quality earns it.
            ca = Math.Clamp(ca, 50, 190);

            int age = AgeForSlot(s);
            var p = gen.Generate(positions[s], ca, age, today);
            p.TeamId = team.Id;
            p.Contract = new Contract
            {
                Years = _rng.Next(1, 5),
                Salary = ContractMarket.MarketSalary(ca, age, league.Finance),
                StartSeason = today.Year,
            };
            team.Roster.Add(p);
        }

        // Scale salaries so every team opens between 82% and 98% of the hard cap, like a real league in-season.
        long cap = league.Finance.HardCap;
        long payroll = team.Roster.Sum(p => p.Contract!.Salary);
        double target = cap * (0.82 + _rng.NextDouble() * 0.16);
        double factor = target / payroll;
        foreach (var p in team.Roster)
            p.Contract!.Salary = Math.Max(league.Finance.MinSalary, (long)Math.Round(p.Contract.Salary * factor / 10_000) * 10_000);
    }

    private int AgeForSlot(int slot)
    {
        // Stars and starters are mostly in their prime; the back of the bench is young or old.
        double mean = slot switch
        {
            < 5 => 27.0,
            < 10 => 26.5,
            _ => 24.5,
        };
        return (int)Math.Round(Math.Clamp(_rng.NextGaussian(mean, 3.5), 19, 37));
    }

}
