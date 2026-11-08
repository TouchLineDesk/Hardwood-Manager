using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>Aggregated regular-season totals for one player in one season.</summary>
public sealed class PlayerSeasonStats
{
    public required int PlayerId { get; init; }
    public int TeamId { get; set; }
    public int Season { get; set; }
    public int Games, Starts, Points, FGM, FGA, ThreePM, ThreePA, FTM, FTA, OReb, DReb, Assists, Steals, Blocks, Turnovers, Fouls, PlusMinus;
    public double Minutes;

    public double Mpg => Games == 0 ? 0 : Minutes / Games;
    public double Ppg => Games == 0 ? 0 : (double)Points / Games;
    public double Rpg => Games == 0 ? 0 : (double)(OReb + DReb) / Games;
    public double Apg => Games == 0 ? 0 : (double)Assists / Games;
    public double FgPct => FGA == 0 ? 0 : (double)FGM / FGA;
    public double ThreePct => ThreePA == 0 ? 0 : (double)ThreePM / ThreePA;

    public void Add(PlayerLine l)
    {
        if (l.Seconds <= 0) return;
        Games++;
        if (l.Started) Starts++;
        Minutes += l.Minutes;
        Points += l.Points; FGM += l.FGM; FGA += l.FGA; ThreePM += l.ThreePM; ThreePA += l.ThreePA;
        FTM += l.FTM; FTA += l.FTA; OReb += l.OReb; DReb += l.DReb; Assists += l.Assists;
        Steals += l.Steals; Blocks += l.Blocks; Turnovers += l.Turnovers; Fouls += l.Fouls; PlusMinus += l.PlusMinus;
    }

    public static Dictionary<int, PlayerSeasonStats> Aggregate(IEnumerable<GameResult> games, int season)
    {
        var stats = new Dictionary<int, PlayerSeasonStats>();
        foreach (var g in games)
        foreach (var box in new[] { g.Home, g.Away })
        foreach (var l in box.Lines)
        {
            if (l.Seconds <= 0) continue;
            if (!stats.TryGetValue(l.Player.Id, out var s))
                stats[l.Player.Id] = s = new PlayerSeasonStats { PlayerId = l.Player.Id, TeamId = box.Team.Id, Season = season };
            s.Add(l);
        }
        return stats;
    }
}
