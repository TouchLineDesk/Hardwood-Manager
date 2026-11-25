using System.Text.Json.Serialization;
using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>Slice 10 of the revised launch plan (#1164, run 201): one of your seasons as a college head coach.</summary>
public sealed class CoachSeason
{
    /// <summary>The year the season began (2026 for 2026-27).</summary>
    public int Year { get; init; }
    public int ProgramId { get; init; }
    public string Program { get; init; } = "";
    public int Wins { get; init; }
    public int Losses { get; init; }
    /// <summary>The NCAA tournament seed (0: no bid).</summary>
    public int Seed { get; init; }
    /// <summary><see cref="CollegeWorld.MadnessReached"/>: -1 not in the field, 0 First Four ... 7 national champion.</summary>
    public int Reached { get; init; } = -1;
}

/// <summary>A milestone of your career, kept so it is told once.</summary>
public sealed class CoachMilestone
{
    public string Key { get; init; } = "";
    public DateOnly Date { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>One branch of your coaching tree: an assistant of yours who became a college head coach.</summary>
public sealed class TreeBranch
{
    public string Name { get; init; } = "";
    /// <summary>The spring he left (the year of the national final).</summary>
    public int Year { get; init; }
    public int FromProgramId { get; init; }
    public string FromProgram { get; init; } = "";
    public int ProgramId { get; init; }
    public string Program { get; init; } = "";
}

/// <summary>
/// Slice 10 (run 201): your college coaching career, as the game keeps it: every season you finished (counted the day the
/// athletic director reviews it), the milestones you reached (told once, in the inbox and the news), and your coaching tree.
/// Saved as an optional <c>MyCareer</c> in the college world (missing before v0.189.0: an older save's book starts empty and
/// counts from its next season). Nothing here draws a random number.
/// </summary>
public sealed class CoachBook
{
    public List<CoachSeason> Seasons { get; set; } = new();
    public List<CoachMilestone> Milestones { get; set; } = new();
    public List<TreeBranch> Tree { get; set; } = new();

    [JsonIgnore] public int Wins => Seasons.Sum(s => s.Wins);
    [JsonIgnore] public int Losses => Seasons.Sum(s => s.Losses);
    [JsonIgnore] public int Bids => Seasons.Count(s => s.Seed > 0);
    [JsonIgnore] public int FinalFours => Seasons.Count(s => s.Reached >= 5);
    [JsonIgnore] public int Titles => Seasons.Count(s => s.Reached >= 7);
    public bool Has(string key) => Milestones.Any(m => m.Key == key);
}

/// <summary>Slice 10 (run 201): the rules of your coaching legacy and the coaches' Hall of Fame. No random draw.</summary>
public static class CoachingLegacy
{
    /// <summary>Career wins told as milestones.</summary>
    public static readonly int[] WinMarks = { 1, 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000 };

    /// <summary>A coach joins a program's Hall of Fame when he leaves it after <see cref="HallSeasons"/>+ seasons at
    /// <see cref="HallPct"/>+ with <see cref="HallBids"/>+ NCAA bids there, or after <see cref="TitleSeasons"/>+ seasons with a
    /// national title there.</summary>
    public const int HallSeasons = 10, HallBids = 5, TitleSeasons = 5;
    public const double HallPct = 0.600;

    /// <summary>Your assistant is ready for a head job after this many of your seasons at the program, with this much coaching (1-20).</summary>
    public const int TreeSeasons = 2, TreeCoaching = 11;

    public static bool HallWorthy(int seasons, int wins, int losses, int bids, int titles)
    {
        if (seasons <= 0 || wins + losses == 0) return false;
        double pct = wins / (double)(wins + losses);
        return (seasons >= HallSeasons && pct >= HallPct && bids >= HallBids) || (seasons >= TitleSeasons && titles >= 1);
    }

    /// <summary>"312-140 in 14 seasons, 9 NCAA bids, 1 national title".</summary>
    public static string CoachLine(int seasons, int wins, int losses, int bids, int titles) =>
        $"{wins}-{losses} in {seasons} season{(seasons == 1 ? "" : "s")}"
        + (bids > 0 ? $", {bids} NCAA bid{(bids == 1 ? "" : "s")}" : "")
        + (titles > 0 ? $", {titles} national title{(titles == 1 ? "" : "s")}" : "");

    /// <summary>Whether your assistant takes a head job this spring: a fixed hash of him and the year, so no random draw moves
    /// (about one spring in three once he's ready).</summary>
    public static bool AssistantReady(Staff asst, int year, int yourSeasonsHere) =>
        yourSeasonsHere >= TreeSeasons && asst.Coaching >= TreeCoaching && RivalCoaches.Hash($"{asst.Id}|{year}|tree") % 3 == 0;

    /// <summary>The milestones a career has reached, oldest first: (key, title, body). <paramref name="wins"/> counts this
    /// season's games so far; <paramref name="reached"/> is this season's March so far.</summary>
    public static IEnumerable<(string Key, string Title, string Body)> Reached(CoachBook book, string coach, string program, int wins, int seasons, int reached, int bidsBefore)
    {
        foreach (int m in WinMarks)
            if (wins >= m)
                yield return m == 1
                    ? ("win-1", "Your first win as a head coach", $"{coach} has his first win as a college head coach, with {program}. The first of many, the staff hopes.")
                    : ($"win-{m}", $"{m} career wins", $"{coach} reached {m} career wins as a college head coach tonight, with {program}.");
        if (reached >= 0 || bidsBefore > 0)
            yield return ("bid-1", "Your first NCAA tournament", $"{coach} takes a team to the NCAA tournament for the first time: {program} are in the field.");
        if (reached >= 2 || book.Seasons.Any(s => s.Reached >= 2))
            yield return ("march-win", "Your first NCAA tournament win", $"{coach} has his first NCAA tournament win, with {program}.");
        if (reached >= 5 || book.FinalFours > 0)
            yield return ("final-four", "Your first Final Four", $"{coach} is going to the Final Four for the first time, with {program}.");
        int titles = book.Titles + (reached >= 7 ? 1 : 0);
        for (int n = 1; n <= titles; n++)
            yield return ($"title-{n}", n == 1 ? "Your first national title" : $"National title No. {n}",
                n == 1 ? $"{coach} is a national champion: {program} won it all." : $"{coach} has {n} national titles now, the latest with {program}.");
        foreach (int s in new[] { 10, 20, 30 })
            if (seasons >= s)
                yield return ($"seasons-{s}", $"{s} seasons as a head coach", $"{coach} has finished {s} seasons as a college head coach.");
        if (book.Tree.Count > 0)
            yield return ("tree-1", "Your coaching tree grows", $"{book.Tree[0].Name}, your former assistant, is the head coach at {book.Tree[0].Program}: the first branch of your coaching tree.");
    }
}

public sealed partial class CollegeWorld
{
    /// <summary>Slice 10 (run 201): your college coaching career (saved; missing before v0.189.0).</summary>
    public CoachBook MyCareer { get; set; } = new();

    /// <summary>A coach who left a program joins its Hall of Fame (slice 10). Returns the inductee, or null when he hasn't
    /// earned it (or is already in).</summary>
    public LegacyInductee? InductCoach(int programId, string name, int first, int last, int seasons, int wins, int losses, int bids, int titles, int classYear)
    {
        if (!HallWorthy(seasons, wins, losses, bids, titles) || Program(programId) is null) return null;
        var book = LegacyBook(programId);
        if (book.Hall.Any(h => h.IsCoach && h.Name == name)) return null;
        var why = new List<string>();
        if (titles > 0) why.Add(titles == 1 ? "a national title" : $"{titles} national titles");
        why.Add($"{wins} wins, a {wins / (double)Math.Max(1, wins + losses):.000} record");
        if (bids > 0) why.Add($"{bids} NCAA tournament{(bids == 1 ? "" : "s")}");
        var who = new LegacyInductee
        {
            Name = name, Pos = "head coach", Class = classYear, First = first, Last = last, IsCoach = true,
            CoachRecord = CoachingLegacy.CoachLine(seasons, wins, losses, bids, titles),
            Score = seasons + titles * 5 + bids * 0.5, Why = why,
        };
        book.Hall.Add(who);
        return who;
    }

    private static bool HallWorthy(int seasons, int wins, int losses, int bids, int titles) =>
        CoachingLegacy.HallWorthy(seasons, wins, losses, bids, titles);

    /// <summary>The program a coach (by name) leads now, if any.</summary>
    public BenchCoach? BenchNamed(string name) => Benches.FirstOrDefault(b => b.Name == name && Program(b.ProgramId)?.CoachName == name);
}
