using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>Your record against one opponent (Danny's #529): wins, losses, points and the last meeting.</summary>
public sealed class H2HRecord
{
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int PointsFor { get; set; }
    public int PointsAgainst { get; set; }
    public DateOnly? LastDate { get; set; }
    public int LastUs { get; set; }
    public int LastThem { get; set; }
    public bool LastHome { get; set; }

    public int Games => Wins + Losses;
    public string Line => Games == 0 ? "–" : $"{Wins}-{Losses}";

    public void Add(DateOnly date, int us, int them, bool home)
    {
        if (us > them) Wins++; else Losses++;
        PointsFor += us; PointsAgainst += them;
        if (LastDate == null || date >= LastDate) { LastDate = date; LastUs = us; LastThem = them; LastHome = home; }
    }

    public void Add(H2HRecord o)
    {
        Wins += o.Wins; Losses += o.Losses; PointsFor += o.PointsFor; PointsAgainst += o.PointsAgainst;
        if (o.LastDate != null && (LastDate == null || o.LastDate >= LastDate)) { LastDate = o.LastDate; LastUs = o.LastUs; LastThem = o.LastThem; LastHome = o.LastHome; }
    }

    public H2HRecord Copy() { var c = new H2HRecord(); c.Add(this); return c; }
}

/// <summary>
/// The head-to-head book (Danny's #529): every game your clubs have played since the career began, by club and opponent.
/// Danny: "historical record (as of the current game - do not need history from before game start date) of head to head
/// matchup against team". Seasons that have ended sit in <see cref="Past"/>; the season being played is re-counted from
/// your schedule each time (<see cref="Current"/>) and folded into <see cref="Past"/> when the schedule changes (a new season, a new club).
/// An older save starts the book at its current season (<see cref="FromCareerStart"/> is false), since earlier meetings were never kept.
/// </summary>
public sealed class HeadToHeadBook
{
    /// <summary>The first day the book counts from.</summary>
    public DateOnly? Since { get; set; }
    /// <summary>True when the book began with the career (no games had been played); false for a save from before v0.79.0.</summary>
    public bool FromCareerStart { get; set; }
    /// <summary>Which schedule <see cref="Current"/> was counted from: your club and its season's first game.</summary>
    public string CurrentKey { get; set; } = "";
    /// <summary>Key "club:opponent".</summary>
    public Dictionary<string, H2HRecord> Past { get; set; } = new();
    public Dictionary<string, H2HRecord> Current { get; set; } = new();

    public static string Key(int us, int opponent) => $"{us}:{opponent}";

    /// <summary>The whole record of club <paramref name="us"/> against <paramref name="opponent"/> since <see cref="Since"/>.</summary>
    public H2HRecord Of(int us, int opponent)
    {
        var r = new H2HRecord();
        if (Past.TryGetValue(Key(us, opponent), out var p)) r.Add(p);
        if (Current.TryGetValue(Key(us, opponent), out var c)) r.Add(c);
        return r;
    }

    /// <summary>Counts the season being played from your games; folds the previous season in first when the schedule changed.</summary>
    public void Sync(int us, IReadOnlyList<MyGame> games, DateOnly today)
    {
        string key = games.Count == 0 ? $"{us}:none" : $"{us}:{games.Min(g => g.Date):yyyy-MM-dd}";
        var played = games.Where(g => g.Played).ToList();
        if (Since == null)
        {
            FromCareerStart = played.Count == 0;
            Since = played.Count == 0 ? today : played.Min(g => g.Date);
        }
        if (key != CurrentKey)
        {
            foreach (var (k, v) in Current)
            {
                if (!Past.TryGetValue(k, out var p)) Past[k] = p = new H2HRecord();
                p.Add(v);
            }
            Current = new();
            CurrentKey = key;
        }
        var now = new Dictionary<string, H2HRecord>();
        foreach (var g in played)
        {
            string k = Key(us, g.Opponent.Id);
            if (!now.TryGetValue(k, out var r)) now[k] = r = new H2HRecord();
            r.Add(g.Date, g.Us!.Value, g.Them!.Value, g.Home);
        }
        // A schedule only ever gains results; if one somehow shows fewer (a reload mid-day), keep what was counted.
        if (now.Values.Sum(r => r.Games) >= Current.Values.Sum(r => r.Games)) Current = now;
    }
}

/// <summary>The Schedule &amp; Results filters (Danny's #529): which part of the season a game belongs to.</summary>
public static class ScheduleFilter
{
    public const string All = "All games";
    public const string Regular = "Regular season";
    public const string NonConference = "Non-conference";
    public const string Conference = "Conference";
    public const string Playoffs = "Playoffs";

    /// <summary>The filters for a list of games: All, the season's parts, then each tournament in date order, the playoffs last.</summary>
    public static List<string> For(IEnumerable<MyGame> games)
    {
        var groups = games.OrderBy(g => g.Date).Select(g => g.Group).Distinct().ToList();
        var fixedFirst = new[] { Regular, NonConference, Conference };
        var list = new List<string> { All };
        list.AddRange(fixedFirst.Where(groups.Contains));
        list.AddRange(groups.Where(g => !fixedFirst.Contains(g) && g != Playoffs));
        if (groups.Contains(Playoffs)) list.Add(Playoffs);
        return list;
    }

    public static bool Matches(MyGame g, string filter) => filter == All || g.Group == filter;

    /// <summary>A pro game's filter: the regular season, the third-place tournament, the playoffs, or another competition by its name.</summary>
    public static string ProGroup(string competition, string stage, string myLeague) =>
        competition != myLeague ? competition
        : stage == Regular ? Regular
        : Postseason.IsThirdPlace(stage) ? Postseason.ThirdPlaceName
        : Playoffs;

    /// <summary>A college game's filter: non-conference, conference, or the event by its name (invitationals, showcases, the conference tournament, March Madness).</summary>
    public static string CollegeGroup(string? collegeEvent, string stage) =>
        collegeEvent ?? (stage == "Conference" ? Conference : NonConference);
}
