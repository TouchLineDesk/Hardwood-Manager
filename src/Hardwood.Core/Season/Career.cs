using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Persistence;

namespace Hardwood.Core.Season;

public sealed record SeasonPlayed(int Season, SeasonOutcome Outcome, Dictionary<int, PlayerSeasonStats> Stats, OffseasonReport Offseason);

/// <summary>Chains seasons together: schedule → play → stats → offseason → next season, saving after each.</summary>
public sealed class Career
{
    private readonly Rng _rng;
    private readonly EngineConfig _engine;
    private readonly SaveFile? _save;

    public Career(Rng rng, SaveFile? save = null, EngineConfig? engine = null)
    {
        _rng = rng;
        _save = save;
        _engine = engine ?? EngineConfig.Default;
    }

    public SeasonPlayed PlaySeason(League league, Action<int, int>? progress = null)
    {
        int season = league.Season;
        var opening = new DateOnly(season, 10, 21);
        var schedule = ScheduleBuilder.Build(league, opening, _rng);
        var sim = new SeasonSimulator(_rng, _engine);
        var outcome = sim.Run(league, schedule, progress);
        var stats = PlayerSeasonStats.Aggregate(outcome.RegularSeason, season);

        var offseason = Offseason.RunAll(league, outcome, stats, _rng);
        _save?.Save(league, stats.Values);
        return new SeasonPlayed(season, outcome, stats, offseason);
    }
}
