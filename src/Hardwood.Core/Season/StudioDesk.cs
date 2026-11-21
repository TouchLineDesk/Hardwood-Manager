using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>One row of the studio's big screen: a label in the middle, the away and home values either side.
/// Edge: −1 the away side is better, +1 the home side, 0 even or not compared. Key marks the row the desk is talking about.</summary>
public sealed record DeskRow(string Label, string Away, string Home, int Edge = 0, bool Key = false);

/// <summary>One beat of a show on the HSN set (B4a, run 143): what the big screen shows and who speaks over it.</summary>
public sealed record DeskBeat(string Kicker, string Title, IReadOnlyList<DeskRow> Rows, StudioLine? Line, string Note = "");

/// <summary>
/// The pre-game show on the studio set (B4a, run 143): every segment of <see cref="StudioShow"/> as big-screen beats with the
/// panel's lines as speech bubbles. Pure: it reads the show and draws nothing, so the show's content is exactly today's.
/// </summary>
public static class DeskReel
{
    public static List<DeskBeat> Pregame(StudioShow s)
    {
        var beats = new List<DeskBeat>();
        var match = new List<DeskRow>
        {
            new("", s.Away.FullName, s.Home.FullName),
            new("Record", s.AwayRecord, s.HomeRecord),
            new("Win chance", $"{100 - Math.Round(s.HomeChance * 100):0}%", $"{Math.Round(s.HomeChance * 100):0}%", Math.Sign(s.HomeChance - 0.5)),
        };
        // The segments: the open, keys to the game, off the court. One beat per line; the screen keeps the matchup.
        foreach (var seg in s.Segments)
        {
            if (seg.Lines.Count == 0) { beats.Add(new(seg.Kicker, seg.Title, match, null)); continue; }
            foreach (var l in seg.Lines) beats.Add(new(seg.Kicker, seg.Title, match, l));
        }
        // The tale of the tape: the screen shows it whole while the host reads the headline.
        if (s.Tape.Count > 0)
        {
            int homeEdges = s.Tape.Count(t => t.Edge > 0), awayEdges = s.Tape.Count(t => t.Edge < 0);
            string text = homeEdges == awayEdges
                ? (homeEdges == 0 ? "On paper these two are dead level." : $"On paper it's split: {homeEdges} categories each.")
                : $"On paper: {(homeEdges > awayEdges ? s.Home.FullName : s.Away.FullName)} have the edge in {Math.Max(homeEdges, awayEdges)} of {s.Tape.Count} categories.";
            beats.Add(new("TALE OF THE TAPE", $"{s.Away.Abbreviation} vs {s.Home.Abbreviation}", s.Tape.Select(t => new DeskRow(t.Label, t.Away, t.Home, t.Edge)).ToList(),
                new StudioLine(s.Host, text)));
        }
        // The standings watch: each table on the screen, then the panel's lines over the last one.
        if (s.Impact.Count > 0 || s.ImpactLines.Count > 0)
        {
            var tables = s.Impact.Select(t => (t.Title, Rows: t.Rows.Select(r => new DeskRow(r.Place.ToString(), r.Team.FullName, $"{r.Record}  {r.Back}".TrimEnd(), Key: r.Tonight)).ToList())).ToList();
            if (tables.Count == 0) tables.Add(("What it means", new List<DeskRow>()));
            for (int i = 0; i < tables.Count; i++)
            {
                bool last = i == tables.Count - 1;
                if (!last || s.ImpactLines.Count == 0) beats.Add(new("STANDINGS WATCH", tables[i].Title, tables[i].Rows, null));
                else foreach (var l in s.ImpactLines) beats.Add(new("STANDINGS WATCH", tables[i].Title, tables[i].Rows, l));
            }
        }
        // The award race.
        if (s.Race.Count > 0 || s.RaceLines.Count > 0)
        {
            var rows = s.Race.Select(r => new DeskRow(r.Rank.ToString(), r.Name, $"{r.Team} · {r.Line}", Key: r.Tonight)).ToList();
            if (s.RaceLines.Count == 0) beats.Add(new("THE RACE", s.RaceTitle, rows, null));
            else foreach (var l in s.RaceLines) beats.Add(new("THE RACE", s.RaceTitle, rows, l));
        }
        // The picks: each analyst on the record, the board filling in as they go.
        for (int i = 0; i < s.Picks.Count; i++)
        {
            var rows = s.Picks.Take(i + 1).Select(p => new DeskRow(p.Analyst.Name, $"{p.Pick.Abbreviation} by {p.Margin}",
                p.Wins + p.Losses == 0 ? "first pick" : p.Record, Key: p == s.Picks[i])).ToList();
            beats.Add(new("THE PICKS", "On the record", rows, new StudioLine(s.Picks[i].Analyst, s.Picks[i].Why)));
        }
        return beats;
    }
}

/// <summary>
/// The halftime desk (B4a, run 143): the first half's story, the stat that decided it, each bench's adjustment, the scores
/// around college and a second-half pick. Pure: it reads the box score at the half and the engine's win chance.
/// </summary>
public static class HalftimeDesk
{
    /// <summary>The cast at halftime: the host and two analysts (the studio render has two people at the desk).</summary>
    public static readonly StudioAnalyst Host = StudioShow.HostDesk, Numbers = StudioShow.Numbers, Big = StudioShow.BigMan;

    /// <summary>A first-half stat the desk can single out, with its rough worth in points (positive: it favoured home).</summary>
    public sealed record Decider(string Key, string Label, string Away, string Home, double Worth);

    public static List<Decider> Deciders(GameResult r)
    {
        TeamBox a = r.Away, h = r.Home;
        static double Pct(int m, int n) => n == 0 ? 0 : 100.0 * m / n;
        double fgA = Pct(a.FGM, a.FGA), fgH = Pct(h.FGM, h.FGA);
        double shots = (a.FGA + h.FGA) / 2.0;
        return new List<Decider>
        {
            new("fg", "Field goals", $"{a.FGM}-{a.FGA} ({fgA:0}%)", $"{h.FGM}-{h.FGA} ({fgH:0}%)", (fgH - fgA) / 100 * shots * 2 * 0.6),
            new("three", "Threes", $"{a.ThreePM}-{a.ThreePA}", $"{h.ThreePM}-{h.ThreePA}", (h.ThreePM - a.ThreePM) * 1.5),
            new("ft", "Free throws", $"{a.FTM}-{a.FTA}", $"{h.FTM}-{h.FTA}", (h.FTM - a.FTM) * 1.0),
            new("oreb", "Offensive rebounds", a.OReb.ToString(), h.OReb.ToString(), (h.OReb - a.OReb) * 1.1),
            new("tov", "Turnovers", a.Turnovers.ToString(), h.Turnovers.ToString(), (a.Turnovers - h.Turnovers) * 1.1),
        };
    }

    /// <summary>The stat that decided the half: the biggest one pointing the leader's way (the biggest of all when level).</summary>
    public static Decider Decided(GameResult r)
    {
        int margin = r.Home.Points - r.Away.Points;
        var all = Deciders(r);
        var forLeader = margin == 0 ? all : all.Where(d => Math.Sign(d.Worth) == Math.Sign(margin)).ToList();
        return (forLeader.Count > 0 ? forLeader : all).OrderByDescending(d => Math.Abs(d.Worth)).ThenBy(d => all.IndexOf(d)).First();
    }

    private static string Fix(string key) => key switch
    {
        "fg" => "get better looks: more at the rim, fewer contested pull-ups",
        "three" => "run shooters off the line and find their own threes",
        "ft" => "keep their hands off and stop sending them to the line",
        "oreb" => "put a body on someone on every shot and finish the possession",
        _ => "take care of the ball: no more live-ball turnovers",
    };
    private static string Keep(string key) => key switch
    {
        "fg" => "keep getting those shots: the offense is working",
        "three" => "keep the floor spaced and let it fly",
        "ft" => "keep attacking the rim and living at the line",
        "oreb" => "keep crashing the glass for second chances",
        _ => "keep the pressure on and turn mistakes into points",
    };

    private static string AroundLine(IReadOnlyList<string> around)
    {
        int finals = around.Count(a => a.StartsWith("Final")), later = around.Count - finals;
        if (finals == 0) return later == 1 ? "One more game with a ranked team still to come tonight." : $"Still to come tonight: {later} more games with a ranked team.";
        return (finals == 1 ? "One final already in from around the country" : $"{finals} finals already in from around the country")
            + (later > 0 ? $", and {later} still to come." : ".");
    }

    public static List<DeskBeat> Build(GameResult r, double homeChance, IReadOnlyList<string>? around = null,
        Func<Team, (OffensiveSystem system, DefensiveScheme scheme)?>? read = null)
    {
        TeamBox a = r.Away, h = r.Home;
        int margin = h.Points - a.Points;
        var leader = margin > 0 ? h : margin < 0 ? a : null;
        var trailer = leader == null ? null : leader == h ? a : h;
        var beats = new List<DeskBeat>();
        var score = new DeskRow("HALFTIME", a.Points.ToString(), h.Points.ToString(), Math.Sign(margin), true);

        // 1. The first half's story: the score and each side's leading scorer.
        PlayerLine? Top(TeamBox b) => b.Lines.Where(l => l.Seconds > 0).OrderByDescending(l => l.Points).ThenByDescending(l => l.Rebounds + l.Assists).FirstOrDefault();
        var (ta, th) = (Top(a), Top(h));
        string Name(PlayerLine? l) => l == null ? "-" : l.Player.Name;
        string Line(PlayerLine? l) => l == null ? "-" : $"{l.Points} pts · {l.Rebounds} reb · {l.Assists} ast";
        var storyRows = new List<DeskRow> { score, new("Leading scorer", Name(ta), Name(th)), new("", Line(ta), Line(th)) };
        string story = leader == null
            ? $"All square at the break, {a.Points}-{h.Points}. Nobody has blinked."
            : $"{leader.Team.FullName} lead {trailer!.Team.FullName} {leader.Points}-{trailer.Points} at the half"
              + (Math.Abs(margin) >= 12 ? ", and it hasn't been close." : Math.Abs(margin) <= 3 ? ": a one-possession game." : ".");
        var star = new[] { (box: a, line: ta), (box: h, line: th) }.Where(x => x.line != null).OrderByDescending(x => x.line!.Points).FirstOrDefault();
        if (star.line != null && star.line.Points >= 8) story += $" {star.line.Player.Name} has {star.line.Points} for {star.box.Team.Abbreviation}.";
        beats.Add(new("HALFTIME", "The first half", storyRows, new StudioLine(Host, story)));

        // 2. The stat that decided it.
        var d = Decided(r);
        var rows = Deciders(r).Select(x => new DeskRow(x.Label, x.Away, x.Home,
            x.Key == "tov" ? -Math.Sign(h.Turnovers - a.Turnovers) : x.Key == "fg" ? Math.Sign(x.Worth) : Math.Sign(x.Worth), x.Key == d.Key)).ToList();
        rows.Insert(0, score);
        var favoured = d.Worth > 0 ? h : a;
        string why = Math.Abs(d.Worth) < 1.5
            ? "Nothing separates them on the numbers. It's coming down to a couple of possessions."
            : $"{d.Label}: {favoured.Team.Abbreviation} {(favoured == h ? d.Home : d.Away)} against {(favoured == h ? d.Away : d.Home)}. "
              + $"That's worth about {Math.Abs(d.Worth):0} points{(leader == null ? "" : leader == favoured ? ", and it's the difference" : " and they still trail")}.";
        beats.Add(new("BY THE NUMBERS", "The stat that decided it", rows, new StudioLine(Numbers, why)));

        // 3. Each bench's adjustment: the trailing side fixes the deciding stat, the leading side keeps at it.
        var adj = new List<DeskRow>();
        var lines = new List<StudioLine>();
        foreach (var b in new[] { trailer ?? a, leader ?? h })
        {
            bool behind = trailer != null ? b == trailer : b == a;
            string plan = behind ? Fix(d.Key) : Keep(d.Key);
            var t = b.Team.Tactic;
            string now = t == null ? "" : $"{Systems.Name(t.System)} · {Systems.Name(t.Scheme)}";
            string switchTo = read?.Invoke(b.Team) is { } c && t != null && (c.system != t.System || c.scheme != t.Scheme)
                ? $" The counter is there: switch to {Systems.Name(c.system).ToLower()} and {Systems.Name(c.scheme).ToLower()}." : "";
            adj.Add(new DeskRow(b.Team.Abbreviation, now, behind && trailer != null ? $"down {Math.Abs(margin)}" : leader == null ? "level" : $"up {Math.Abs(margin)}", Key: behind));
            lines.Add(new StudioLine(Big, $"{b.Team.FullName}: {plan}.{switchTo}"));
        }
        foreach (var l in lines) beats.Add(new("ADJUSTMENTS", "What each bench changes", adj, l));

        // 4. Around college: the night's other scores (only when there are some).
        if (around is { Count: > 0 })
        {
            var arows = around.Take(6).Select(s => new DeskRow("", s, "")).ToList();
            beats.Add(new("AROUND COLLEGE", "Elsewhere tonight", arows, new StudioLine(Host, AroundLine(around))));
        }

        // 5. The second-half pick: the numbers desk goes with the engine's win chance.
        var pick = homeChance >= 0.5 ? h : a;
        double pc = pick == h ? homeChance : 1 - homeChance;
        int by = Math.Max(1, (int)Math.Round(Math.Abs(margin) * 0.7 + (pc - 0.5) * 12));
        bool comeback = leader != null && pick != leader;
        var prow = new List<DeskRow> { score, new("Win chance", $"{100 - Math.Round(homeChance * 100):0}%", $"{Math.Round(homeChance * 100):0}%", Math.Sign(homeChance - 0.5), true) };
        string ptxt = comeback ? $"I'm going against the scoreboard: {pick.Team.FullName} come back and win by {by}."
            : leader == null ? $"Coin flip, but I'll take {pick.Team.FullName} by {by}."
            : $"{pick.Team.FullName} hold on. {pick.Team.Abbreviation} by {by}.";
        beats.Add(new("SECOND HALF", "The pick", prow, new StudioLine(Numbers, ptxt), $"{pick.Team.Abbreviation} by {by}"));
        return beats;
    }
}

public sealed partial class GameSession
{
    /// <summary>Halftime's "around college": today's other games with a ranked side, finals first, then the rest of the night.</summary>
    public List<string> HalftimeAround(Team home, Team away)
    {
        if (!InCollege || World.College is not { } cw) return new();
        var games = cw.Season.Games.Where(g => g.Date == Today && g.HomeId != home.Id && g.AwayId != home.Id && g.HomeId != away.Id && g.AwayId != away.Id).ToList();
        string Nm(int id, int? rank) => Hardwood.Core.College.CollegeWorld.WithRank(rank, cw.Program(id)?.FullName ?? "?");
        return games.Where(g => g.HomeRank != null || g.AwayRank != null || cw.Rank(g.HomeId) != null || cw.Rank(g.AwayId) != null)
            .OrderBy(g => g.Played ? 0 : 1).ThenBy(g => Math.Min(g.HomeRank ?? cw.Rank(g.HomeId) ?? 99, g.AwayRank ?? cw.Rank(g.AwayId) ?? 99))
            .Select(g => g.Played
                ? $"Final: {Nm(g.AwayId, g.AwayRank ?? cw.Rank(g.AwayId))} {g.AwayPts}, {Nm(g.HomeId, g.HomeRank ?? cw.Rank(g.HomeId))} {g.HomePts}"
                : $"Later: {Nm(g.AwayId, cw.Rank(g.AwayId))} at {Nm(g.HomeId, cw.Rank(g.HomeId))}")
            .Take(8).ToList();
    }
}
