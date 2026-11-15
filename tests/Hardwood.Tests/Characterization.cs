using System.Security.Cryptography;
using System.Text;
using Hardwood.Core.Engine;
using Hardwood.Core.Season;

namespace Hardwood.Tests;

/// <summary>
/// Phase 1 of Danny's roadmap (#596, run 57): fingerprints of what the game does now, so a refactor (Phase 1) or a speed-up
/// (Phase 2) can prove it changed nothing. Each fingerprint is a list of plain lines (so a failure shows the first line
/// that moved) and a short hash of them (so a test can pin the exact value).
/// </summary>
public static class Characterization
{
    /// <summary>A short, stable hash of a list of lines (the first 16 hex digits of SHA-256).</summary>
    public static string Hash(IEnumerable<string> lines)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines)));
        return Convert.ToHexString(bytes)[..16];
    }

    /// <summary>Every number in a box score, line by line (seconds to the tenth, as the save keeps them).</summary>
    public static List<string> Box(GameResult r)
    {
        var lines = new List<string>
        {
            $"{r.Home.Team.Abbreviation} {r.Home.Points} - {r.Away.Points} {r.Away.Team.Abbreviation}; OT {r.Overtimes}; injuries {r.Injuries.Count}",
        };
        foreach (var box in new[] { r.Home, r.Away })
            foreach (var l in box.Lines)
                lines.Add($"{box.Team.Abbreviation} {l.Player.Id} {(l.Started ? "S" : "-")} {Math.Round(l.Seconds, 1)}s {l.Points}p {l.FGM}/{l.FGA} {l.ThreePM}/{l.ThreePA} {l.FTM}/{l.FTA} "
                    + $"{l.OReb}+{l.DReb}r {l.Assists}a {l.Steals}s {l.Blocks}b {l.Turnovers}to {l.Fouls}f {l.PlusMinus:+0;-0;0} "
                    + $"z{string.Join(",", l.ZoneAttempts)}/{string.Join(",", l.ZoneMakes)}");
        foreach (var (p, inj) in r.Injuries) lines.Add($"injury {p.Id} {inj.Name} {inj.ReturnDate:yyyy-MM-dd}");
        return lines;
    }

    /// <summary>The state of a career a player could see: the date and phase, every result and table in the NBA and the
    /// world, every club's money and roster, every player's season numbers, and the inbox. It leaves out the random
    /// state on purpose: comparing two sessions' futures is what checks that.</summary>
    public static List<string> Session(GameSession s)
    {
        var lines = new List<string> { $"{s.Today:yyyy-MM-dd} {s.Phase} season {s.League.Season} coach {s.HumanTeam.Abbreviation} employed {s.Employed}" };
        foreach (var g in s.Schedule.Where(g => s.Results.ContainsKey(g.Index)).OrderBy(g => g.Index))
        {
            var r = s.Results[g.Index];
            lines.Add($"nba {g.Index} {g.Date:MMdd} {g.Home.Abbreviation} {r.Home.Points}-{r.Away.Points} {g.Away.Abbreviation}");
        }
        foreach (var se in s.PlayoffSeries)
            lines.Add($"series {se.Higher.Abbreviation} {se.HigherWins}-{se.LowerWins} {se.Lower.Abbreviation} games {se.Games.Count}");
        foreach (var t in s.League.Teams.OrderBy(t => t.Id))
        {
            string rec = s.Standings.Has(t) ? $"{s.Standings[t].Wins}-{s.Standings[t].Losses} {s.Standings[t].PointsFor}:{s.Standings[t].PointsAgainst}" : "-";
            lines.Add($"team {t.Id} {t.Abbreviation} {rec} bank {t.Money.Balance} roster {string.Join(",", t.Roster.Select(p => p.Id).OrderBy(i => i))}");
        }
        foreach (var c in s.World.Competitions.OrderBy(c => c.Key))
        {
            var played = c.Season.Fixtures.Where(f => f.Played).ToList();
            lines.Add($"comp {c.Key} played {played.Count} pts {played.Sum(f => (long)f.HomePts!.Value * 1000 + f.AwayPts!.Value)} "
                + $"series {string.Join(",", c.Season.Series.Select(x => $"{x.HighId}:{x.HighWins}-{x.LowWins}:{x.LowId}"))} "
                + $"banks {c.Clubs.Sum(t => t.Money.Balance)} players {c.Clubs.Sum(t => t.Roster.Count)}");
        }
        foreach (var st in s.Stats.OrderBy(kv => kv.Key))
            lines.Add($"stats {st.Key} t{st.Value.TeamId} {st.Value.Games}g {st.Value.Points}p {st.Value.Assists}a {st.Value.OReb + st.Value.DReb}r");
        lines.Add($"inbox {s.Inbox.Count}: {string.Join(" | ", s.Inbox.Take(5).Select(m => $"{m.Date:MMdd} {m.Title}"))}");
        return lines;
    }

    /// <summary>The first line where two fingerprints differ (for the failure message), or null if they match.</summary>
    public static string? FirstDifference(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            string x = i < a.Count ? a[i] : "(none)", y = i < b.Count ? b[i] : "(none)";
            if (x != y) return $"line {i}:\n  {x}\n  {y}";
        }
        return null;
    }
}
