using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

public sealed class TeamRecord
{
    public required Team Team { get; init; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int PointsFor { get; set; }
    public int PointsAgainst { get; set; }
    public int HomeWins { get; set; }
    public int HomeLosses { get; set; }
    public Dictionary<int, (int w, int l)> HeadToHead { get; } = new();

    public int Games => Wins + Losses;
    public double WinPct => Games == 0 ? 0 : (double)Wins / Games;
    public double PointDiff => Games == 0 ? 0 : (double)(PointsFor - PointsAgainst) / Games;
}

public sealed class Standings
{
    private readonly Dictionary<int, TeamRecord> _records;

    public Standings(League league)
    {
        _records = league.Teams.ToDictionary(t => t.Id, t => new TeamRecord { Team = t });
    }

    public TeamRecord this[Team t] => _records[t.Id];

    public void Record(GameResult r)
    {
        var w = _records[r.Winner.Team.Id];
        var l = _records[r.Loser.Team.Id];
        w.Wins++; l.Losses++;
        w.PointsFor += r.Winner.Points; w.PointsAgainst += r.Loser.Points;
        l.PointsFor += r.Loser.Points; l.PointsAgainst += r.Winner.Points;
        if (r.Winner == r.Home) { w.HomeWins++; } else { l.HomeLosses++; }

        var wh = w.HeadToHead.GetValueOrDefault(l.Team.Id); w.HeadToHead[l.Team.Id] = (wh.w + 1, wh.l);
        var lh = l.HeadToHead.GetValueOrDefault(w.Team.Id); l.HeadToHead[w.Team.Id] = (lh.w, lh.l + 1);
    }

    /// <summary>Ordered by win %, then head-to-head, then point differential.</summary>
    public List<TeamRecord> Conference(Conference conf) =>
        _records.Values.Where(r => r.Team.Conference == conf)
            .OrderByDescending(r => r.WinPct)
            .ThenByDescending(r => r.PointDiff)
            .ToList();

    public List<TeamRecord> All() => _records.Values.OrderByDescending(r => r.WinPct).ThenByDescending(r => r.PointDiff).ToList();
}
