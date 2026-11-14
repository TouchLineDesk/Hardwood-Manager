using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>One team in the Top 25 (#528): its rank now, where it stood before the latest day of games, and its record.</summary>
public sealed record PollEntry(int Rank, Team Team, int Wins, int Losses, int? Before)
{
    /// <summary>Places climbed since the latest day of games (negative: fell). Null when it wasn't ranked before.</summary>
    public int? Move => Before is { } b ? b - Rank : null;
    /// <summary>"▲3", "▼2", "–", or "NEW" for a team that wasn't ranked before.</summary>
    public string MoveText => Move switch { null => "NEW", 0 => "–", > 0 and var m => $"▲{m}", var m => $"▼{-m}" };
}

/// <summary>
/// The college Top 25 (Danny, #528): ranked on the rating, re-ranked after every game, and the rank travels with the
/// team (its logo, schedules, scoreboards, standings and the live game).
/// </summary>
public sealed partial class CollegeWorld
{
    public const int PollSize = 25;

    private long _pollVersion;
    private long _pollCachedVersion = -1;
    private CollegeSeason? _pollCachedSeason;
    private List<int> _pollOrder = new();
    private Dictionary<int, int> _pollRanks = new();

    private void EnsurePoll()
    {
        if (_pollCachedVersion == _pollVersion && ReferenceEquals(_pollCachedSeason, Season) && _pollOrder.Count > 0) return;
        var elo = Season.Elo;
        _pollOrder = Programs.Select(t => t.Id).OrderByDescending(id => elo.GetValueOrDefault(id, 1500)).ThenBy(id => id).Take(PollSize).ToList();
        _pollRanks = _pollOrder.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);
        _pollCachedVersion = _pollVersion; _pollCachedSeason = Season;
    }

    /// <summary>The Top 25's program ids, best first.</summary>
    public IReadOnlyList<int> PollOrder() { EnsurePoll(); return _pollOrder; }

    /// <summary>Before the first game of a day: keep the poll as it stood, for the arrows.</summary>
    private void BeforeGameDay(DateOnly date)
    {
        if (Season.PollDay == date) return;
        Season.PollBefore = PollRanks();
        Season.PollDay = date;
        // Every game that day tips off against the poll as it stood, even when yours is played first.
        foreach (var g in Season.Games.Where(g => g.Date == date)) TipOff(g);
    }

    /// <summary>Both sides' ranks at tip-off, once (a game you coach is recorded before the rest of the day's).</summary>
    private void TipOff(CollegeGame g)
    {
        if (g.Played) return;
        g.HomeRank ??= Rank(g.HomeId);
        g.AwayRank ??= Rank(g.AwayId);
    }

    /// <summary>A copy of the poll as program id → rank.</summary>
    public Dictionary<int, int> PollRanks() { EnsurePoll(); return new Dictionary<int, int>(_pollRanks); }

    /// <summary>A program's Top 25 rank right now (1-25), or null when unranked or not a college program.</summary>
    public int? Rank(int teamId) { EnsurePoll(); return _pollRanks.TryGetValue(teamId, out var r) ? r : null; }

    /// <summary>Call after changing ratings outside a game (tests, a new season): the poll is re-ranked on its next read.</summary>
    public void InvalidatePoll() => _pollVersion++;

    /// <summary>The Top 25 with records and movement since the latest day of games.</summary>
    public List<PollEntry> Poll()
    {
        var before = Season.PollBefore;
        return PollOrder().Select((id, i) =>
        {
            var (w, l) = Record(id);
            return new PollEntry(i + 1, Program(id)!, w, l, before.Count == 0 ? i + 1 : before.TryGetValue(id, out var b) ? b : null);
        }).ToList();
    }

    /// <summary>Teams that were in the poll before the latest day of games and have dropped out.</summary>
    public List<Team> DroppedOut()
    {
        var now = PollRanks();
        return Season.PollBefore.Where(kv => !now.ContainsKey(kv.Key)).OrderBy(kv => kv.Value).Select(kv => Program(kv.Key)).OfType<Team>().ToList();
    }

    /// <summary>"#5 Duke" for a ranked program, else the name as given (#528).</summary>
    public string Ranked(Team team, string name) => Rank(team.Id) is { } r ? $"#{r} {name}" : name;

    /// <summary>A name with a rank as it stood at the time ("#5 Duke"), or the name alone.</summary>
    public static string WithRank(int? rank, string name) => rank is { } r ? $"#{r} {name}" : name;
}
