using System.Text.Json;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.RealWorld;

/// <summary>
/// The real players' careers before the game starts (R2 part 2b, decisions #223-#226): games, points, rebounds,
/// assists, steals and blocks through 2025-26 (Data/nba_careers.json), plus their MVP and Finals MVP awards from the
/// real history. They seed <see cref="League.Careers"/>, so the records book's career leaders, the milestones and the
/// Hall of Fame start from the real past, and the game's own seasons add on top.
/// </summary>
public static class RealCareers
{
    /// <summary>One player's real totals through 2025-26; null where the source's top-100 list doesn't reach him.</summary>
    public sealed record Line(int? Games, int? Points, int? Rebounds, int? Assists, int? Steals, int? Blocks);

    private sealed record Pack(Dictionary<string, Line> Players, Dictionary<string, int> Cutoff, List<(string Name, int Points)> Ladder, string Checked, string Source);
    private static readonly Lazy<Pack> _pack = new(Load);

    public static IReadOnlyDictionary<string, Line> Players => _pack.Value.Players;
    /// <summary>Each stat's top-100 cut-off: a player missing from a list has fewer than this.</summary>
    public static IReadOnlyDictionary<string, int> Cutoff => _pack.Value.Cutoff;
    /// <summary>Retired players high on the all-time scoring list (NBA careers only), highest first.</summary>
    public static IReadOnlyList<(string Name, int Points)> ScoringLadder => _pack.Value.Ladder;
    public static string Checked => _pack.Value.Checked;
    public static string Source => _pack.Value.Source;

    /// <summary>A season's worth of games, for the estimate of how many seasons a career has run.</summary>
    public const int GamesPerSeason = 70;
    /// <summary>The most games a player missing from the games list can have (the list stops at 551).</summary>
    public const int UnlistedGamesMax = 550;

    /// <summary>
    /// The career totals for one real player, or null when the source has nothing on him (most players outside the
    /// top 100 in every stat). Where one list stops short of him, the total is estimated from his games and his
    /// per-game numbers (last season's line, else this season's), kept under that list's cut-off; a player missing from
    /// the games list gets his games estimated from his known totals and his per-game numbers (at most 550).
    /// </summary>
    public static CareerTotals? Totals(RealPlayerLine line, int playerId)
    {
        if (!Players.TryGetValue(line.Name, out var c)) return null;
        var row = line.Prev ?? line.Cur;
        double Per(string stat)
        {
            if (row == null) return 0;
            double V(int i) => i < row.Length ? row[i] ?? 0 : 0;
            // Columns: G, MP, FGA, FG%, 3PA, 3P%, FTA, FT%, ORB, DRB, AST, STL, BLK, TOV, PTS.
            return stat switch { "pts" => V(14), "reb" => V(8) + V(9), "ast" => V(10), "stl" => V(11), "blk" => V(12), _ => 0 };
        }
        var known = new (string Stat, int? Total)[] { ("pts", c.Points), ("reb", c.Rebounds), ("ast", c.Assists), ("stl", c.Steals), ("blk", c.Blocks) };
        int games = c.Games ?? (int)Math.Round(Math.Min(UnlistedGamesMax, known
            .Where(k => k.Total is > 0 && Per(k.Stat) > 0.2).Select(k => k.Total!.Value / Per(k.Stat)).DefaultIfEmpty(0).Average()));
        if (games <= 0) return null;
        int Of(string stat, int? total) => total ?? (int)Math.Min(Cutoff.GetValueOrDefault(stat, int.MaxValue) - 1, Math.Round(games * Per(stat)));
        var t = new CareerTotals
        {
            PlayerId = playerId,
            Games = games,
            Seasons = Math.Max(1, (int)Math.Round((double)games / GamesPerSeason)),
            Points = Of("pts", c.Points), Rebounds = Of("reb", c.Rebounds), Assists = Of("ast", c.Assists),
            Steals = Of("stl", c.Steals), Blocks = Of("blk", c.Blocks),
        };
        // Minutes from his minutes per game, so per-minute views aren't empty.
        if (row is { Length: > 1 } && row[1] is double mp) t.Minutes = Math.Round(games * mp);
        t.Mvps = RealWorldData.Seasons.Count(s => s.Mvp == line.Name);
        t.FinalsMvps = RealWorldData.Seasons.Count(s => s.FinalsMvp == line.Name);
        return t;
    }

    private static Pack Load()
    {
        var asm = typeof(RealCareers).Assembly;
        string name = asm.GetManifestResourceNames().Single(n => n.EndsWith("nba_careers.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(name)!;
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        int? N(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;
        var players = root.GetProperty("players").EnumerateObject().ToDictionary(p => p.Name,
            p => new Line(N(p.Value, "g"), N(p.Value, "pts"), N(p.Value, "reb"), N(p.Value, "ast"), N(p.Value, "stl"), N(p.Value, "blk")));
        var cutoff = root.GetProperty("cutoff").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
        var ladder = root.GetProperty("scoringLadder").EnumerateArray()
            .Select(e => (e.GetProperty("name").GetString()!, e.GetProperty("pts").GetInt32())).OrderByDescending(x => x.Item2).ToList();
        return new Pack(players, cutoff, ladder, root.GetProperty("checked").GetString() ?? "", root.GetProperty("source").GetString() ?? "");
    }
}
