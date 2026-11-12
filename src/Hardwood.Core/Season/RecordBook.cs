using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>One entry in the records book: a number, who set it, against whom and when.</summary>
public sealed class RecordEntry
{
    public string Category { get; set; } = "";
    public double Value { get; set; }
    public int? PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public int TeamId { get; set; }
    public string Team { get; set; } = "";
    public string Opponent { get; set; } = "";
    public int Season { get; set; }
    public DateOnly? Date { get; set; }
    public string Detail { get; set; } = "";
}

/// <summary>A career milestone: the game a player passed a round number.</summary>
public sealed class Milestone
{
    public DateOnly Date { get; set; }
    public int Season { get; set; }
    public int PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public int TeamId { get; set; }
    public string Team { get; set; } = "";
    public string Stat { get; set; } = "";
    public int Threshold { get; set; }
    public string Text => $"{PlayerName} ({Team}) passed {Threshold:N0} career {Stat}";
}

/// <summary>Something from an earlier season that happened around this date ("on this day").</summary>
public sealed record HistoryMoment(DateOnly Date, int YearsAgo, string Text);

/// <summary>
/// The living history, part 1 (roadmap 4): the league's single-game records (kept as games are played, top
/// <see cref="Keep"/> per category) and career milestones. Saved with the league (meta key <c>record_book</c>); a
/// save without it starts an empty book. Season and career records are worked out from the stored stats instead
/// (<see cref="SeasonLeaders"/>, <see cref="CareerLeaders"/>).
/// </summary>
public sealed class RecordBook
{
    public const int Keep = 10;
    public Dictionary<string, List<RecordEntry>> Games { get; set; } = new();
    public List<Milestone> Milestones { get; set; } = new();
    /// <summary>The first season the book saw. Records only make news after it: in a brand-new world every big night "breaks" one.</summary>
    public int FirstSeason { get; set; }
    /// <summary>In a real-world career, the real NBA records as they stand (R2 part 2b): seeded from the real record book,
    /// replaced when a player beats one. Empty in a fictional career (and in a save from before part 2b, until the next game).</summary>
    public Dictionary<string, RealWorld.RealMark> RealMarks { get; set; } = new();

    /// <summary>The single-game player categories: key, heading, unit, and how to read a box-score line.</summary>
    public static readonly (string Key, string Label, string Unit, Func<PlayerLine, int> Of)[] PlayerGame =
    {
        ("pts", "Points in a game", "pts", l => l.Points),
        ("reb", "Rebounds in a game", "reb", l => l.Rebounds),
        ("ast", "Assists in a game", "ast", l => l.Assists),
        ("3pm", "Threes in a game", "3PM", l => l.ThreePM),
        ("stl", "Steals in a game", "stl", l => l.Steals),
        ("blk", "Blocks in a game", "blk", l => l.Blocks),
    };

    /// <summary>The single-game team categories.</summary>
    public static readonly (string Key, string Label, string Unit)[] TeamGame =
    {
        ("team-pts", "Team points in a game", "pts"),
        ("margin", "Biggest winning margin", "pts"),
    };

    /// <summary>Career milestones: stat name and the round numbers that count.</summary>
    public static readonly (string Stat, int[] At)[] MilestoneSteps =
    {
        ("points", new[] { 5_000, 10_000, 15_000, 20_000, 25_000, 30_000, 35_000, 40_000 }),
        ("rebounds", new[] { 3_000, 5_000, 7_500, 10_000, 12_500, 15_000 }),
        ("assists", new[] { 2_000, 4_000, 6_000, 8_000, 10_000, 12_000 }),
        ("games", new[] { 500, 750, 1_000, 1_250, 1_500 }),
    };

    public List<RecordEntry> Top(string category) => Games.TryGetValue(category, out var l) ? l : new List<RecordEntry>();

    /// <summary>
    /// Add a regular-season game. Returns a line for each new league record (a new number one in a category),
    /// e.g. "League record: Jason Parker scored 61 points (old record 58)".
    /// </summary>
    public List<string> Consider(GameResult r, DateOnly date, int season)
    {
        var news = new List<string>();
        if (FirstSeason == 0) FirstSeason = season;
        bool newsworthy = season > FirstSeason;
        foreach (var (box, opp) in new[] { (r.Home, r.Away), (r.Away, r.Home) })
        {
            foreach (var l in box.Lines.Where(l => l.Seconds > 0))
                foreach (var (key, label, unit, of) in PlayerGame)
                {
                    int v = of(l);
                    if (v <= 0) continue;
                    var e = new RecordEntry
                    {
                        Category = key, Value = v, PlayerId = l.Player.Id, PlayerName = l.Player.Name, TeamId = box.Team.Id, Team = box.Team.Abbreviation,
                        Opponent = opp.Team.Abbreviation, Season = season, Date = date, Detail = $"{v} {unit} vs {opp.Team.Abbreviation}",
                    };
                    if (Insert(e, out var old) && newsworthy && key is "pts" or "reb" or "ast") news.Add($"League record: {l.Player.Name} ({box.Team.Abbreviation}) had {v} {unit} against {opp.Team.Abbreviation}" + (old == null ? "" : $", passing {old.PlayerName}'s {old.Value:0} in {old.Season}-{(old.Season + 1) % 100:00}") + ".");
                }
            var pts = new RecordEntry { Category = "team-pts", Value = box.Points, TeamId = box.Team.Id, Team = box.Team.Abbreviation, Opponent = opp.Team.Abbreviation, Season = season, Date = date, Detail = $"{box.Points}-{opp.Points} vs {opp.Team.Abbreviation}" };
            if (Insert(pts, out var oldPts) && newsworthy) news.Add($"League record: {box.Team.FullName} scored {box.Points} against {opp.Team.Abbreviation}" + (oldPts == null ? "" : $", passing {oldPts.Team}'s {oldPts.Value:0}") + ".");
            int margin = box.Points - opp.Points;
            if (margin > 0)
            {
                var m = new RecordEntry { Category = "margin", Value = margin, TeamId = box.Team.Id, Team = box.Team.Abbreviation, Opponent = opp.Team.Abbreviation, Season = season, Date = date, Detail = $"{box.Points}-{opp.Points} vs {opp.Team.Abbreviation}" };
                if (Insert(m, out var oldM) && newsworthy) news.Add($"League record: {box.Team.FullName} beat {opp.Team.Abbreviation} by {margin}" + (oldM == null ? "" : $", passing {oldM.Team}'s {oldM.Value:0}-point win") + ".");
            }
        }
        return news;
    }

    /// <summary>Keep the entry if it makes the top <see cref="Keep"/>. True when it's a new number one (beating an earlier record, not just setting the first).</summary>
    private bool Insert(RecordEntry e, out RecordEntry? oldTop)
    {
        if (!Games.TryGetValue(e.Category, out var list)) Games[e.Category] = list = new List<RecordEntry>();
        oldTop = list.Count > 0 ? list[0] : null;
        if (list.Count >= Keep && e.Value <= list[^1].Value) return false;
        // Ties keep the earlier holder ahead.
        int at = list.FindIndex(x => x.Value < e.Value);
        if (at < 0) list.Add(e); else list.Insert(at, e);
        if (list.Count > Keep) list.RemoveAt(list.Count - 1);
        return at == 0 && oldTop != null;
    }

    /// <summary>Record the milestones a player passed in one game (totals before and after it). Returns the new ones.</summary>
    public List<Milestone> CheckMilestones(Player p, Team team, DateOnly date, int season, IReadOnlyDictionary<string, (int Before, int After)> totals)
    {
        var found = new List<Milestone>();
        foreach (var (stat, steps) in MilestoneSteps)
        {
            if (!totals.TryGetValue(stat, out var t)) continue;
            foreach (int at in steps)
                if (t.Before < at && t.After >= at && !Milestones.Any(m => m.PlayerId == p.Id && m.Stat == stat && m.Threshold == at))
                {
                    var m = new Milestone { Date = date, Season = season, PlayerId = p.Id, PlayerName = p.Name, TeamId = team.Id, Team = team.Abbreviation, Stat = stat, Threshold = at };
                    Milestones.Add(m);
                    found.Add(m);
                }
        }
        return found;
    }

    /// <summary>Records and milestones from earlier seasons within <paramref name="window"/> days of today's date, nearest first.</summary>
    public List<HistoryMoment> OnThisDay(DateOnly today, int window = 3)
    {
        var moments = new List<HistoryMoment>();
        int Gap(DateOnly d)
        {
            // Same calendar date in today's year (Feb 29 falls back to Feb 28).
            var then = new DateOnly(today.Year, d.Month, Math.Min(d.Day, DateTime.DaysInMonth(today.Year, d.Month)));
            return Math.Abs(then.DayNumber - today.DayNumber);
        }
        foreach (var (key, list) in Games)
        {
            var label = PlayerGame.FirstOrDefault(c => c.Key == key).Label ?? TeamGame.FirstOrDefault(c => c.Key == key).Label ?? key;
            for (int i = 0; i < list.Count; i++)
            {
                var e = list[i];
                if (e.Date is not { } d || d.DayNumber > today.DayNumber - 300 || Gap(d) > window) continue;
                string who = e.PlayerId != null ? $"{e.PlayerName} ({e.Team})" : e.Team;
                string rank = i == 0 ? "the league record" : $"number {i + 1} all time";
                moments.Add(new HistoryMoment(d, today.Year - d.Year, $"{who}: {e.Detail}, {rank} for {label.ToLowerInvariant()}"));
            }
        }
        foreach (var m in Milestones)
            if (m.Date.DayNumber <= today.DayNumber - 300 && Gap(m.Date) <= window)
                moments.Add(new HistoryMoment(m.Date, today.Year - m.Date.Year, m.Text));
        return moments.OrderBy(x => Gap(x.Date)).ThenByDescending(x => x.YearsAgo).ToList();
    }

    /// <summary>Single-season bests from stored seasons (each player-season counts once): per game with a games minimum, or totals.</summary>
    public static List<(PlayerSeasonStats Stats, double Value)> SeasonLeaders(IEnumerable<PlayerSeasonStats> seasons, Func<PlayerSeasonStats, double> of, int minGames, int take = 5) =>
        seasons.Where(s => s.Games >= minGames).Select(s => (s, of(s))).OrderByDescending(x => x.Item2).ThenBy(x => x.s.Season).Take(take).ToList();

    /// <summary>Career totals including the season being played (completed seasons plus this season so far).</summary>
    public static Dictionary<int, CareerTotals> CareerWithCurrent(IReadOnlyDictionary<int, CareerTotals> careers, IReadOnlyDictionary<int, PlayerSeasonStats> current)
    {
        var all = careers.ToDictionary(kv => kv.Key, kv => Copy(kv.Value));
        foreach (var (pid, s) in current)
        {
            if (!all.TryGetValue(pid, out var c)) all[pid] = c = new CareerTotals { PlayerId = pid };
            c.Games += s.Games; c.Points += s.Points; c.Rebounds += s.OReb + s.DReb; c.Assists += s.Assists; c.Steals += s.Steals; c.Blocks += s.Blocks; c.Minutes += s.Minutes;
        }
        return all;
    }

    /// <summary>The top careers by a total.</summary>
    public static List<CareerTotals> CareerLeaders(IReadOnlyDictionary<int, CareerTotals> careers, Func<CareerTotals, double> of, int take = 5) =>
        careers.Values.Where(c => of(c) > 0).OrderByDescending(of).ThenByDescending(c => c.Games).Take(take).ToList();

    private static CareerTotals Copy(CareerTotals c) => new()
    {
        PlayerId = c.PlayerId, Seasons = c.Seasons, Games = c.Games, Points = c.Points, Rebounds = c.Rebounds, Assists = c.Assists, Steals = c.Steals, Blocks = c.Blocks,
        Minutes = c.Minutes, Mvps = c.Mvps, AllLeague = c.AllLeague, AllStars = c.AllStars, Titles = c.Titles, Dpoys = c.Dpoys, Roys = c.Roys, SixthMen = c.SixthMen,
        FinalsMvps = c.FinalsMvps, GamesByTeam = new Dictionary<int, int>(c.GamesByTeam),
    };
}
