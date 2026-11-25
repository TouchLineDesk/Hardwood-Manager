using System.Text.Json.Serialization;
using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.College;

/// <summary>Slice 11 of the revised launch plan (#1164, run 202): where a college player came from, kept from his recruitment
/// (the recruit record is dropped when he enrolls).</summary>
public sealed class PlayerOrigin
{
    /// <summary>His home state (two-letter code).</summary>
    public string State { get; init; } = "";
    /// <summary>5 to 2, as the services rated him at signing.</summary>
    public int Stars { get; init; }
    /// <summary>His national rank in his class (0: unranked, a late signing).</summary>
    public int Rank { get; init; }
    /// <summary>The summer he enrolled ("class of 2027").</summary>
    public int ClassOf { get; init; }
    /// <summary>He was already in college when the career (or the save's first load on v0.190.0) began: his origin was set
    /// from a fixed hash and his ability, not from a recruitment the game played.</summary>
    public bool Seeded { get; init; }
}

/// <summary>One story told about a player of yours, once, in the inbox and on his player page.</summary>
public sealed class PlayerStory
{
    public int PlayerId { get; init; }
    public int TeamId { get; init; }
    /// <summary>"phenom", "hometown", "walkon", "breakout", "overlooked", "scholarship".</summary>
    public string Key { get; init; } = "";
    /// <summary>The college season it belongs to (the year it began).</summary>
    public int Year { get; init; }
    public DateOnly Date { get; init; }
    public string Title { get; init; } = "";
    public string Text { get; init; } = "";
    /// <summary>Told in your inbox yet (a summer story is told on the first day of the next season).</summary>
    public bool Posted { get; set; }
}

/// <summary>Slice 11 (run 202): the college world's player stories (saved as an optional <c>Stories</c>; missing before
/// v0.190.0, when every rostered player's origin is seeded on the first load).</summary>
public sealed class StoryBook
{
    public Dictionary<int, PlayerOrigin> Origins { get; set; } = new();
    public List<PlayerStory> Told { get; set; } = new();

    public bool Has(int playerId, string key) => Told.Any(s => s.PlayerId == playerId && s.Key == key);
    public IEnumerable<PlayerStory> Of(int playerId) => Told.Where(s => s.PlayerId == playerId).OrderBy(s => s.Date);
}

/// <summary>
/// Slice 11 (run 202): player stories over four years, told about the coached program's players from what they actually do.
/// Nothing here draws a random number.
/// <list type="bullet">
/// <item><b>The freshman phenom:</b> a freshman at <see cref="PhenomPpg"/>+ points a game, or the team's leading scorer at
/// <see cref="LeaderPpg"/>+, after <see cref="MinGames"/> games.</item>
/// <item><b>The hometown hero:</b> a player from the program's home state who starts <see cref="MinGames"/>+ games or scores
/// <see cref="HometownPpg"/>+.</item>
/// <item><b>The walk-on:</b> a walk-on who plays <see cref="WalkOnMpg"/>+ minutes a game or starts <see cref="WalkOnStarts"/>+;
/// the summer after, he goes on scholarship if one is open.</item>
/// <item><b>The senior's breakout:</b> a senior at <see cref="BreakoutPpg"/>+ points a game, <see cref="BreakoutJump"/>+ more
/// than his college average before this season (over <see cref="BreakoutPriorGames"/>+ games).</item>
/// <item><b>The one they overlooked:</b> a three-star recruit or less who is named All-America, a Player of the Year, or to an
/// all-conference first team.</item>
/// </list>
/// </summary>
public static class PlayerStories
{
    public const int MinGames = 10, WalkOnStarts = 5, BreakoutPriorGames = 20;
    public const double PhenomPpg = 14, LeaderPpg = 10, HometownPpg = 12, WalkOnMpg = 15, BreakoutPpg = 12, BreakoutJump = 6;

    /// <summary>"a four-star recruit, No. 57 in the class of 2027" (or "a walk-on from Ohio" style phrases built by callers).</summary>
    public static string RecruitText(PlayerOrigin o) =>
        $"a {StarWord(o.Stars)}-star recruit" + (o.Rank > 0 && !o.Seeded ? $", No. {o.Rank} in the class of {o.ClassOf}" : "");

    public static string StarWord(int stars) => stars switch { 5 => "five", 4 => "four", 3 => "three", _ => "two" };

    /// <summary>"From Ohio · a four-star recruit, No. 57 in the class of 2027", for his player page.</summary>
    public static string OriginLine(PlayerOrigin o, bool walkOn) =>
        $"From {CollegeWorld.StateName(o.State)} · " + (walkOn ? "a walk-on" : RecruitText(o));

    private static string Line(StatLine st) => $"{st.Ppg:F1} points, {st.Rpg:F1} rebounds and {st.Apg:F1} assists a game over {st.G} games";

    /// <summary>The in-season stories a player of the coached program has earned so far this season (key, title, body), from
    /// his line (<paramref name="st"/>) and what's known of him. <paramref name="priorG"/>/<paramref name="priorPts"/> are his
    /// college games and points at the program before this season.</summary>
    public static IEnumerable<(string Key, string Title, string Body)> InSeason(Player p, Team t, StatLine st, ClassYear cy, PlayerOrigin? o,
        bool walkOn, bool leadsTeam, int priorG, int priorPts)
    {
        if (st.G < MinGames) yield break;
        string school = t.FullName;
        if (cy.Year == 1 && (st.Ppg >= PhenomPpg || (leadsTeam && st.Ppg >= LeaderPpg)))
            yield return ("phenom", $"The freshman phenom: {p.Name}",
                $"{p.Name} is a freshman playing like a veteran for {school}: {Line(st)}"
                + (leadsTeam ? ", the team's best." : ".")
                + (o != null && !walkOn ? $" He came in as {RecruitText(o)}." : "")
                + " The staff say he's only getting started.");
        if (o != null && o.State.Length > 0 && o.State == CollegeWorld.HomeState(t) && (st.Gs >= MinGames || st.Ppg >= HometownPpg))
            yield return ("hometown", $"The hometown hero: {p.Name}",
                $"{p.Name} grew up in {CollegeWorld.StateName(o.State)} and stayed home to play for {school}. "
                + (st.Gs >= MinGames ? $"He has started {st.Gs} games" : $"He's scoring {st.Ppg:F1} a game")
                + $" ({Line(st)}), and the home crowd loves him for it.");
        if (walkOn && (st.Mpg >= WalkOnMpg || st.Gs >= WalkOnStarts))
            yield return ("walkon", $"From walk-on to the rotation: {p.Name}",
                $"Nobody gave {p.Name} a scholarship. He walked on at {school} and has earned his minutes: "
                + (st.Gs > 0 ? $"{st.Gs} starts, " : "") + $"{st.Mpg:F1} minutes a game ({Line(st)}). "
                + "If a scholarship opens up this summer, it's his.");
        if (cy.Final && priorG >= BreakoutPriorGames && st.Ppg >= BreakoutPpg && st.Ppg >= priorPts / (double)priorG + BreakoutJump)
            yield return ("breakout", $"The senior's breakout: {p.Name}",
                $"Three years of patience, then this: {p.Name} averaged {priorPts / (double)priorG:F1} points over his first {priorG} college games, "
                + $"and in his senior season he's at {Line(st)}. The best of him came last.");
    }

    /// <summary>The awards-night story: a three-star (or lower) recruit among the season's honours.</summary>
    public static (string Key, string Title, string Body)? Overlooked(Player p, Team t, PlayerOrigin? o, bool walkOn, List<string> honours)
    {
        if (o == null || honours.Count == 0 || (o.Stars > 3 && !walkOn)) return null;
        string who = walkOn ? "a walk-on" : RecruitText(o);
        return ("overlooked", $"The one they overlooked: {p.Name}",
            $"{p.Name} came to {t.FullName} as {who} from {CollegeWorld.StateName(o.State)}. This season: {string.Join("; ", honours)}. "
            + "Plenty of programs passed on him. They won't forget the name now.");
    }

    /// <summary>A player's honours in one season's awards, for <see cref="Overlooked"/>.</summary>
    public static List<string> HonoursIn(CollegeAwards a, int id, Func<string, string> confName)
    {
        var list = new List<string>();
        if (a.PlayerOfYearId == id) list.Add("National Player of the Year");
        int team = a.AllAmerica.FindIndex(x => x.Contains(id));
        if (team >= 0) list.Add($"All-America {CollegeAwardsText.TeamName(team)}");
        foreach (var (key, pid) in a.ConferencePlayer.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (pid == id) list.Add($"{confName(key)} Player of the Year");
        foreach (var (key, five) in a.AllConference.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (five.Contains(id) && a.ConferencePlayer.GetValueOrDefault(key) != id) list.Add($"All-{confName(key)} first team");
        return list;
    }
}

public sealed partial class CollegeWorld
{
    /// <summary>Slice 11 (run 202): player origins and the stories told about your players (saved; missing before v0.190.0).</summary>
    public StoryBook Stories { get; set; } = new();

    /// <summary>His origin, if the game knows it (every rostered college player from v0.190.0).</summary>
    public PlayerOrigin? OriginOf(int playerId) => Stories.Origins.GetValueOrDefault(playerId);

    /// <summary>Keeps an enrolling recruit's origin before his recruit record is dropped.</summary>
    private void KeepOrigin(Player p, Recruit r) =>
        Stories.Origins[p.Id] = new PlayerOrigin { State = r.State, Stars = r.Stars, Rank = r.Rank, ClassOf = r.ClassOf };

    /// <summary>
    /// Every rostered college player gets an origin, and players who left college lose theirs. A player with no recruitment
    /// (a new career's rosters, an older save's first load, a late signing) gets one from a fixed hash and his potential's
    /// rank in his age group: the top 25 five stars, the next 80 four, the next 300 three (decision #162's shape), and about
    /// one in three from the program's home state. Idempotent; no random draw. Returns how many were added.
    /// </summary>
    public int EnsureOrigins()
    {
        var rostered = Programs.SelectMany(t => t.Roster.Select(p => (p, t))).ToList();
        var ids = rostered.Select(x => x.p.Id).ToHashSet();
        foreach (var gone in Stories.Origins.Keys.Where(k => !ids.Contains(k)).ToList()) Stories.Origins.Remove(gone);
        var missing = rostered.Where(x => !Stories.Origins.ContainsKey(x.p.Id)).ToList();
        if (missing.Count == 0) return 0;
        var summer = new DateOnly(Season.Year, 7, 1);
        var rankIn = rostered.GroupBy(x => x.p.Age(summer))
            .SelectMany(g => g.OrderByDescending(x => x.p.PotentialAbility).ThenBy(x => x.p.Id).Select((x, i) => (x.p.Id, Rank: i + 1)))
            .ToDictionary(x => x.Id, x => x.Rank);
        foreach (var (p, t) in missing.OrderBy(x => x.p.Id))
        {
            int rank = rankIn.GetValueOrDefault(p.Id, 9999);
            int stars = rank <= 25 ? 5 : rank <= 105 ? 4 : rank <= 405 ? 3 : 2;
            ulong h = RivalCoaches.Hash($"{p.Id}|origin");
            string home = HomeState(t);
            string state = home.Length > 0 && h % 3 == 0 ? home : HashedState(h / 3);
            int classOf = summer.Year - (p.Age(summer) - EnrollAge);
            Stories.Origins[p.Id] = new PlayerOrigin { State = state, Stars = stars, Rank = rank <= 405 ? rank : 0, ClassOf = classOf, Seeded = true };
        }
        return missing.Count;
    }

    private static string HashedState(ulong h)
    {
        double u = h % 10_000 / 10_000.0 * StateWeightTotal;
        foreach (var s in StateTable) { if ((u -= s.Weight) <= 0) return s.Code; }
        return "TX";
    }

    /// <summary>The summer after a walk-on of the coached program earned his minutes: he goes on scholarship if one is open
    /// after the class enrolls (the late scholarships and walk-ons then fill around him). No random draw.</summary>
    private void WalkOnScholarship(DateOnly on)
    {
        if (ManagedProgramId is not { } me || Program(me) is not { } t) return;
        int room = Scholarships - t.Roster.Count(p => !WalkOnIds.Contains(p.Id));
        foreach (var p in t.Roster.Where(p => WalkOnIds.Contains(p.Id) && Stories.Has(p.Id, "walkon") && !Stories.Has(p.Id, "scholarship"))
                     .OrderByDescending(p => p.CurrentAbility).ThenBy(p => p.Id).ToList())
        {
            if (room <= 0) break;
            WalkOnIds.Remove(p.Id); room--;
            Stories.Told.Add(new PlayerStory
            {
                PlayerId = p.Id, TeamId = t.Id, Key = "scholarship", Year = on.Year, Date = on,
                Title = $"Walk-on no more: {p.Name} is on scholarship",
                Text = $"A scholarship opened at {t.FullName} this summer, and it went to {p.Name}, the walk-on who earned his minutes last season. "
                     + "He's one of the 13 now.",
            });
        }
    }
}
