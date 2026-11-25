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
/// The college Top 25 (Danny, #528): ranked on the rating and the record (H148, run 133), and the rank travels with the
/// team (its logo, schedules, scoreboards, standings and the live game). K35 (run 193): the poll is released on Mondays, as
/// the real one is, and the badges, the Top 25 page and the tip-off ranks read that release all week; the first release is
/// the preseason poll, on the season's first day of games. The final poll (after the title) is ranked live.
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
        // H154 (run 136): once the title is decided, the final poll counts March, as the real one does: the champion first,
        // then every round won in the tournament adds weight (the runner-up and the Final Four rise near the top).
        Func<int, double> score = PollScore;
        if (Season.ChampionId is { } champ && !_preMarchPoll)
        {
            var march = Programs.ToDictionary(t => t.Id, t => MadnessReached(t.Id));
            // Run 147: the runner-up second, as the final coaches' poll nearly always has him (seed 25 had him 5th once
            // K15b's flips moved the season's rosters).
            int? runnerUp = Season.RunnerUpId;
            score = id => id == champ ? double.MaxValue : id == runnerUp ? double.MaxValue / 2 : PollScore(id) + MarchPollWeight * Math.Max(0, march[id] - 1);
        }
        var all = Programs.Select(t => t.Id).OrderByDescending(score).ThenBy(id => id).ToList();
        if (Season.ChampionId is { } ch && !_preMarchPoll) all = CapMarchClimb(all, ch, Season.RunnerUpId);
        if (Season.ChampionId == null || _preMarchPoll) KeepUnbeatenAhead(all);
        _fullRanks = all.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);
        _pollOrder = all.Take(PollSize).ToList();
        _pollRanks = _pollOrder.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);
        _pollCachedVersion = _pollVersion; _pollCachedSeason = Season;
    }

    private Dictionary<int, int> _fullRanks = new();

    /// <summary>H165 (run 203): the places a March team may climb in the final poll for each round won past its first game.</summary>
    public const int MarchClimbPerRound = 2;
    /// <summary>H165: the most places a March run lifts a team (the champion and the runner-up excepted).</summary>
    public const int MarchClimbMax = 8;
    /// <summary>H165: a Final Four team never finishes below this rank, so every semi-finalist is in the Top 25.</summary>
    public const int FinalFourFloor = 20;

    /// <summary>
    /// H165 (run 203): a March run lifts a team a few places, not 10+ past teams with far better records. Hunt 26's final
    /// poll had Missouri (22-14, an 8-seed Final Four) at No. 14, above Michigan State at 26-9. Each team's place is held
    /// to at most <see cref="MarchClimbPerRound"/> places a round above where its score without March puts it (at most
    /// <see cref="MarchClimbMax"/>); the champion and runner-up stay first and second, and a Final Four team stays in the
    /// top <see cref="FinalFourFloor"/>. No random draw.
    /// </summary>
    private List<int> CapMarchClimb(List<int> withMarch, int champ, int? runnerUp)
    {
        var baseIdx = Programs.Select(t => t.Id).OrderByDescending(PollScore).ThenBy(id => id)
            .Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var key = new Dictionary<int, int>();
        for (int i = 0; i < withMarch.Count; i++)
        {
            int id = withMarch[i];
            if (id == champ || id == runnerUp) { key[id] = i; continue; }
            int reached = MadnessReached(id);
            int climb = Math.Min(MarchClimbMax, MarchClimbPerRound * Math.Max(0, reached - 1));
            int k = Math.Max(i, baseIdx[id] - climb);
            if (reached >= 5) k = Math.Min(k, FinalFourFloor - 1);
            key[id] = k;
        }
        var pos = withMarch.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        return withMarch.OrderBy(id => key[id]).ThenBy(id => pos[id]).ToList();
    }

    /// <summary>
    /// H148, hardened (run 156): the score alone let an 8-0 DePaul sit 20th behind 9-3 BYU once the non-conference season
    /// grew (H159). No voter does that, so an unbeaten team (8+ games) moves up to just ahead of the first team above it
    /// with three or more losses. Unbeaten teams keep their order among themselves.
    /// </summary>
    public const int UnbeatenReach = 40;

    private void KeepUnbeatenAhead(List<int> order)
    {
        var rec = order.ToDictionary(id => id, Record);
        for (int i = 0; i < order.Count; i++)
        {
            var (w, l) = rec[order[i]];
            // Only a team the voters already have in view (the top 40 by score): an 8-0 low-major 200th on the ratings
            // doesn't leap into the Top 25 on the record alone (run 156 review).
            if (l != 0 || w < 8 || i >= UnbeatenReach) continue;
            int first = order.FindIndex(0, i, id => rec[id].losses >= 3);
            if (first < 0) continue;
            int id = order[i];
            order.RemoveAt(i);
            order.Insert(first, id);
        }
    }

    /// <summary>
    /// H148 (run 133, critic review #18): the poll votes on the rating and the record together, as voters do. Before the
    /// first game it's the rating alone; from about the tenth game a perfect record is worth +130 and a .500 record nothing,
    /// so an unbeaten power team no longer sits behind a three-loss one. The rating still breaks ties between similar records.
    /// </summary>
    public double PollScore(int id)
    {
        double elo = Season.Elo.GetValueOrDefault(id, 1500);
        var (w, l) = Record(id);
        int g = w + l;
        if (g == 0) return elo;
        double score = elo + RecordWeight * ((double)w / g - 0.5) * Math.Min(1, g / 10.0);
        // Run 134: voters keep an unbeaten team above the three-loss ones and count each loss (seed 2026's Gonzaga, 10-0,
        // rating 1568, sat 19th behind Duke 11-3, rating 1718, once K11's college engine made games less predictable).
        if (l == 0 && g >= 8) score += UnbeatenBonus;
        return score - LossPenalty * Math.Min(l, 4);
    }

    /// <summary>H154 (run 136): the final poll's weight for each March Madness round won past the first game.</summary>
    public const double MarchPollWeight = 35;

    /// <summary>Whether the poll is the final one (the title decided, March counted).</summary>
    public bool FinalPoll => Season.ChampionId != null;

    /// <summary>Set while the season's awards are voted: the poll without March (review, run 136).</summary>
    private bool _preMarchPoll;

    /// <summary>Run 134: what an unbeaten record from the 8th game on adds to the poll score.</summary>
    public const double UnbeatenBonus = 60;
    /// <summary>Run 134: each of a team's first four losses costs this much on top of the win share (voters count losses).</summary>
    public const double LossPenalty = 20;

    /// <summary>Poll points for a record's win share above .500 (×0.5 for a perfect record).</summary>
    public const double RecordWeight = 260;

    /// <summary>K35 (run 193): whether the poll on show is the Monday release (otherwise the live ranking: the preseason, an
    /// older save until its first Monday, and the final poll).</summary>
    private bool Released => Season.PollRelease.Count > 0 && !FinalPoll;

    /// <summary>Where a program would stand if the poll went on past 25 (H148: "Your program: No. 31").</summary>
    public int PollPosition(int teamId)
    {
        if (Released) { int i = Season.PollRelease.IndexOf(teamId); return i >= 0 ? i + 1 : Programs.Count; }
        EnsurePoll(); return _fullRanks.TryGetValue(teamId, out var r) ? r : Programs.Count;
    }

    /// <summary>The Top 25's program ids, best first.</summary>
    public IReadOnlyList<int> PollOrder() { if (Released) return Season.PollRelease.Take(PollSize).ToList(); EnsurePoll(); return _pollOrder; }

    /// <summary>The poll as it would be ranked right now (the voters' running order between Mondays), best first.</summary>
    public IReadOnlyList<int> LivePollOrder() { EnsurePoll(); return _pollOrder; }

    /// <summary>The Monday of the week a date falls in.</summary>
    public static DateOnly PollMondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>
    /// K35 (run 193): release the poll if one is due: the preseason poll on the season's first day of games, then every
    /// Monday (taken before that day's games). The previous release is kept for the arrows. No random numbers.
    /// </summary>
    public void ReleasePoll(DateOnly today)
    {
        var s = Season;
        if (FinalPoll) return;
        bool first = s.PollRelease.Count == 0;
        var monday = PollMondayOf(today);
        if (!first && s.PollDay is { } last && last >= monday) return;
        s.PollBefore = first ? new() : PollRanks();
        EnsurePoll();
        s.PollRelease = Programs.Select(t => t.Id).OrderBy(id => _fullRanks.GetValueOrDefault(id, int.MaxValue)).ThenBy(id => id).ToList();
        s.PollDay = first ? today : monday;
    }

    /// <summary>Before the first game of a day: release the poll if a Monday has come (K35), then every game that day tips off against it.</summary>
    private void BeforeGameDay(DateOnly date)
    {
        if (_tippedOff == date && ReferenceEquals(_tippedSeason, Season)) return;
        ReleasePoll(date);
        _tippedOff = date; _tippedSeason = Season;
        // Every game that day tips off against the poll as it stood, even when yours is played first.
        foreach (var g in Season.Games.Where(g => g.Date == date)) TipOff(g);
    }

    private DateOnly? _tippedOff;
    private CollegeSeason? _tippedSeason;

    /// <summary>Both sides' ranks at tip-off, once (a game you coach is recorded before the rest of the day's).</summary>
    private void TipOff(CollegeGame g)
    {
        if (g.Played) return;
        g.HomeRank ??= Rank(g.HomeId);
        g.AwayRank ??= Rank(g.AwayId);
    }

    /// <summary>A copy of the poll as program id → rank.</summary>
    public Dictionary<int, int> PollRanks() => PollOrder().Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i + 1);

    /// <summary>A program's Top 25 rank right now (1-25; K35: the Monday release's), or null when unranked or not a college program.</summary>
    public int? Rank(int teamId)
    {
        if (Released) { int i = Season.PollRelease.IndexOf(teamId); return i >= 0 && i < PollSize ? i + 1 : null; }
        EnsurePoll(); return _pollRanks.TryGetValue(teamId, out var r) ? r : null;
    }

    /// <summary>Call after changing ratings outside a game (tests, a new season): the poll is re-ranked on its next read.</summary>
    public void InvalidatePoll() => _pollVersion++;

    /// <summary>The Top 25 with records and movement since the previous release (K35: last week's poll).</summary>
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
