using System.Globalization;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.RealWorld;

/// <summary>One real NBA record as it stands in this career: the mark, who holds it and when it was set. Saved in the
/// records book (<see cref="RecordBook.RealMarks"/>); a book without it is seeded on the next game.</summary>
public sealed class RealMark
{
    public double Value { get; set; }
    public string Holder { get; set; } = "";
    /// <summary>The holder's player id when he is in the game world (LeBron's career points, or whoever breaks a record).</summary>
    public int? HolderId { get; set; }
    /// <summary>The season he set a single-season record in (his later games that season extend it).</summary>
    public int? HolderSeason { get; set; }
    public string When { get; set; } = "";
}

/// <summary>A news item from the record chase.</summary>
public sealed record ChaseNews(string Title, string Body, bool Broken);

/// <summary>
/// Record-breaking news against the real NBA's records (R2 part 2b, #225). In a real-world career the NBA's games are
/// measured against the real record book: single-game marks (Wilt's 100 points, Klay's 14 threes), single-season
/// marks (Wilt's 4,029 points, Curry's 402 threes) and career marks (LeBron's points, Wilt's rebounds, Stockton's
/// assists and steals, Olajuwon's blocks). A player who closes in on a career record or passes a legend on the
/// all-time scoring list makes news too. Broken records stay broken: the new holder's mark is the one to beat.
/// </summary>
public static class RealRecordChase
{
    private static readonly (string Key, string Data, string Unit)[] GameKeys =
    {
        ("game-pts", "Points", "points"), ("game-reb", "Rebounds", "rebounds"), ("game-ast", "Assists", "assists"),
        ("game-stl", "Steals", "steals"), ("game-blk", "Blocks", "blocks"), ("game-3pm", "Three-pointers made", "threes"),
    };
    private static readonly (string Key, string Data, string Unit)[] SeasonKeys =
    {
        ("season-pts", "Points", "points"), ("season-3pm", "Three-pointers made", "threes"),
    };
    /// <summary>Career records, and how close counts as "closing in".</summary>
    private static readonly (string Key, string Data, string Unit, int Near)[] CareerKeys =
    {
        ("career-pts", "Points", "points", 1000), ("career-reb", "Rebounds", "rebounds", 500), ("career-ast", "Assists", "assists", 500),
        ("career-stl", "Steals", "steals", 100), ("career-blk", "Blocks", "blocks", 100),
    };

    /// <summary>The book key for a real record ("game"/"season"/"career" and its label in the data), or null if it isn't chased.</summary>
    public static string? KeyFor(string section, string label) => section switch
    {
        "game" when label == "Team points" => "game-team-pts",
        "game" => GameKeys.FirstOrDefault(k => k.Data == label).Key,
        "season" => SeasonKeys.FirstOrDefault(k => k.Data == label).Key,
        "career" => CareerKeys.FirstOrDefault(k => k.Data == label).Key,
        _ => null,
    };

    /// <summary>Put the real records in the book, with the holder's id when he's one of the game's players.</summary>
    public static void Seed(RecordBook book, IReadOnlyDictionary<string, int> idsByName)
    {
        void Put(string key, IReadOnlyList<RealRecord> list, string label, bool careerHolder)
        {
            if (book.RealMarks.ContainsKey(key)) return;
            var r = list.FirstOrDefault(x => x.Label == label);
            if (r == null || !double.TryParse(r.Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return;
            book.RealMarks[key] = new RealMark
            {
                Value = v, Holder = r.Holder, When = r.When,
                HolderId = careerHolder && idsByName.TryGetValue(r.Holder, out var id) ? id : null,
            };
        }
        foreach (var (key, data, _) in GameKeys) Put(key, RealWorldData.GameRecords, data, false);
        Put("game-team-pts", RealWorldData.GameRecords, "Team points", false);
        foreach (var (key, data, _) in SeasonKeys) Put(key, RealWorldData.SeasonRecords, data, false);
        foreach (var (key, data, _, _) in CareerKeys) Put(key, RealWorldData.CareerRecords, data, true);
    }

    /// <summary>A team's points in a game against the real team record (Detroit's 186 in triple overtime, 1983).</summary>
    public static ChaseNews? CheckTeam(RecordBook book, Team team, string opponent, int points, int against, DateOnly date)
    {
        if (!book.RealMarks.TryGetValue("game-team-pts", out var m) || points <= m.Value) return null;
        var news = new ChaseNews($"NBA record: {team.FullName} score {points}",
            $"{team.FullName} scored {points} against {opponent} ({points}-{against}) on {date:MMM d}, the most points by a team in an NBA game. The old mark was {m.Value:N0} by the {m.Holder} ({m.When}).", true);
        book.RealMarks["game-team-pts"] = new RealMark { Value = points, Holder = team.FullName, When = $"vs {opponent} ({points}-{against}), {date:MMM d, yyyy}" };
        return news;
    }

    /// <summary>Seed the book from the league's players when it has no real marks yet (a save from before part 2b).</summary>
    public static void EnsureSeeded(League league)
    {
        if (league.Records.RealMarks.Count > 0) return;
        var ids = new Dictionary<string, int>();
        foreach (var p in league.Teams.SelectMany(t => t.Roster)) ids.TryAdd(p.Name, p.Id);
        Seed(league.Records, ids);
    }

    /// <summary>
    /// One player's game against the real records. <paramref name="game"/>: his numbers in this game (pts, reb, ast,
    /// stl, blk, 3pm). <paramref name="season"/>: his season totals before and after it (pts, 3pm).
    /// <paramref name="career"/>: his career totals before and after it (pts, reb, ast, stl, blk).
    /// </summary>
    public static List<ChaseNews> Check(RecordBook book, Player p, Team team, string opponent, DateOnly date, int seasonYear,
        IReadOnlyDictionary<string, int> game, IReadOnlyDictionary<string, (int Before, int After)> season,
        IReadOnlyDictionary<string, (int Before, int After)> career)
    {
        var news = new List<ChaseNews>();
        string who = $"{p.Name} ({team.Abbreviation})";
        string when = $"{date:MMM d, yyyy}";
        string seasonText = $"{seasonYear}-{(seasonYear + 1) % 100:00}";

        foreach (var (key, _, unit) in GameKeys)
        {
            if (!book.RealMarks.TryGetValue(key, out var m) || !game.TryGetValue(key[5..], out int v) || v <= 0) continue;
            if (v > m.Value)
            {
                news.Add(new ChaseNews($"NBA record: {p.Name} has {v} {unit}",
                    $"{who} had {v} {unit} against {opponent} on {date:MMM d}, an NBA record for a single game. The old mark was {m.Value:N0} by {m.Holder} ({m.When}).", true));
                book.RealMarks[key] = new RealMark { Value = v, Holder = p.Name, HolderId = p.Id, When = $"{team.Abbreviation} vs {opponent}, {when}" };
            }
            else if (v == m.Value && m.HolderId != p.Id)
                news.Add(new ChaseNews($"{p.Name} ties an NBA record",
                    $"{who} had {v} {unit} against {opponent} on {date:MMM d}, equal to the NBA record for a single game ({m.Holder}, {m.When}).", true));
        }

        foreach (var (key, _, unit) in SeasonKeys)
        {
            if (!book.RealMarks.TryGetValue(key, out var m) || !season.TryGetValue(key[7..], out var s)) continue;
            if (m.HolderId == p.Id && m.HolderSeason == seasonYear) { m.Value = Math.Max(m.Value, s.After); continue; }
            if (s.Before <= m.Value && s.After > m.Value)
            {
                news.Add(new ChaseNews($"NBA record: {p.Name}'s {s.After:N0} {unit} in a season",
                    $"{who} passed {(m.HolderId == p.Id ? "his own" : m.Holder + "'s")} {m.Value:N0} {unit} ({m.When}) against {opponent} on {date:MMM d}: the most in an NBA season, and the season isn't over.", true));
                book.RealMarks[key] = new RealMark { Value = s.After, Holder = p.Name, HolderId = p.Id, HolderSeason = seasonYear, When = seasonText };
            }
        }

        foreach (var (key, _, unit, near) in CareerKeys)
        {
            if (!book.RealMarks.TryGetValue(key, out var m) || !career.TryGetValue(key[7..], out var c)) continue;
            if (m.HolderId == p.Id) { m.Value = Math.Max(m.Value, c.After); continue; }
            if (c.Before <= m.Value && c.After > m.Value)
            {
                news.Add(new ChaseNews($"NBA record: {p.Name} is the all-time leader in {unit}",
                    $"{who} passed {m.Holder} ({m.Value:N0}) against {opponent} on {date:MMM d} and now has more career {unit} than anyone in NBA history: {c.After:N0}.", true));
                book.RealMarks[key] = new RealMark { Value = c.After, Holder = p.Name, HolderId = p.Id, When = $"since {when}" };
            }
            else if (c.Before < m.Value - near && c.After >= m.Value - near)
                news.Add(new ChaseNews($"Record watch: {p.Name} closes in on {m.Holder}",
                    $"{who} has {c.After:N0} career {unit}, {m.Value - c.After:N0} short of {m.Holder}'s NBA record ({m.Value:N0}).", false));
        }

        // The all-time scoring list: passing a legend is news of its own.
        if (career.TryGetValue("pts", out var pts))
            foreach (var (legend, total) in RealCareers.ScoringLadder)
                if (pts.Before <= total && pts.After > total && legend != p.Name)
                    news.Add(new ChaseNews($"{p.Name} passes {legend} on the all-time scoring list",
                        $"{who} passed {legend} ({total:N0} points) against {opponent} on {date:MMM d}. He now has {pts.After:N0} career points.", false));
        return news;
    }
}
