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
        // K29 (run 187): the opening screen, which every game starts with, was three rows on a mostly empty wall. It also
        // carries the form lines from the tape (last 10, streak, strength, injuries, the series), up to MinRows.
        string[] opener = { "Last 10", "Streak", "Top-8 strength", "Out injured", "Season series" };
        var form = opener.SelectMany(l => s.Tape.Where(t => t.Label == l)).Select(t => new DeskRow(t.Label, t.Away, t.Home, t.Edge));
        // On opening night there's no form yet, so the screen also says how each side plays.
        var style = new List<DeskRow>();
        // The review of PR #256: an AI side's Tactic is only set at tip-off (CoachAi.Prepare), so the screen shows the plan
        // Prepare will pick (the same read, no random numbers, nothing changed); a coach's own fixed plan as it stands.
        (OffensiveSystem sys, DefensiveScheme sch) Plan(Team t, Team opp) =>
            t.Tactic.Auto || t.Tactic.AssistantPlans ? CoachAi.PlanFor(t, opp, s.Date, s.Set == StudioSet.College) : (t.Tactic.System, t.Tactic.Scheme);
        var (pa, ph) = (Plan(s.Away, s.Home), Plan(s.Home, s.Away));
        style.Add(new("Offense", Systems.Name(pa.sys), Systems.Name(ph.sys)));
        // Run 194: a college side that presses says so.
        string D(Team t, DefensiveScheme sch) => Systems.Name(sch) + (s.Set == StudioSet.College && (t.Tactic.Auto ? CoachAi.Presses(CoachAi.Rotation(t, s.Date), true) : t.Tactic.FullCourtPress) ? " + press" : "");
        style.Add(new("Defense", D(s.Away, pa.sch), D(s.Home, ph.sch)));
        HalftimeDesk.Fill(match, form.Concat(style), HalftimeDesk.MinRows + 1);
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

    /// <summary>K29 (run 181): the most rows the big screen shows, and the fewest a data beat shows.</summary>
    public const int MaxRows = 9, MinRows = 6;

    /// <summary>K29: box-score rows a beat can fall back on to fill the screen, in order (a label already shown is skipped).</summary>
    internal static IEnumerable<DeskRow> Filler(GameResult r)
    {
        TeamBox a = r.Away, h = r.Home;
        static string Pct(int m, int n) => n == 0 ? "-" : $"{m}-{n} ({100.0 * m / n:0}%)";
        yield return new("Field goals", Pct(a.FGM, a.FGA), Pct(h.FGM, h.FGA), Math.Sign((h.FGA == 0 ? 0 : (double)h.FGM / h.FGA) - (a.FGA == 0 ? 0 : (double)a.FGM / a.FGA)));
        yield return new("Threes", $"{a.ThreePM}-{a.ThreePA}", $"{h.ThreePM}-{h.ThreePA}", Math.Sign(h.ThreePM - a.ThreePM));
        yield return new("Rebounds", (a.OReb + a.DReb).ToString(), (h.OReb + h.DReb).ToString(), Math.Sign(h.OReb + h.DReb - a.OReb - a.DReb));
        yield return new("Assists", a.Assists.ToString(), h.Assists.ToString(), Math.Sign(h.Assists - a.Assists));
        yield return new("Turnovers", a.Turnovers.ToString(), h.Turnovers.ToString(), -Math.Sign(h.Turnovers - a.Turnovers));
        yield return new("Fouls", a.Fouls.ToString(), h.Fouls.ToString());
    }

    /// <summary>K29: tops a beat's rows up to <see cref="MinRows"/> from <paramref name="extra"/> (labels not yet shown), at most <see cref="MaxRows"/>.</summary>
    internal static List<DeskRow> Fill(List<DeskRow> rows, IEnumerable<DeskRow> extra, int upTo = MinRows)
    {
        foreach (var x in extra)
        {
            if (rows.Count >= Math.Min(upTo, MaxRows)) break;
            if (x.Label != "" && rows.Any(r => r.Label == x.Label)) continue;
            rows.Add(x);
        }
        return rows;
    }

    /// <summary>K29: the score timeline's rows (biggest lead each way, lead changes, times tied); empty without a play log.</summary>
    internal static IEnumerable<DeskRow> FlowRows(GameResult r)
    {
        if (r.Flow.Count == 0) yield break;
        var (lh, la) = GameFlow.BiggestLeads(r);
        yield return new("Biggest lead", la.ToString(), lh.ToString(), Math.Sign(lh - la));
        yield return new("Lead changes", GameFlow.LeadChanges(r).ToString(), "");
        yield return new("Times tied", GameFlow.TimesTied(r).ToString(), "");
    }

    private static string AroundLine(IReadOnlyList<string> around)
    {
        int finals = around.Count(a => a.StartsWith("Final")), later = around.Count - finals;
        if (finals == 0) return later == 1 ? "One more game with a ranked team still to come tonight." : $"Still to come tonight: {later} more games with a ranked team.";
        return (finals == 1 ? "One final already in from around the country" : $"{finals} finals already in from around the country")
            + (later > 0 ? $", and {later} still to come." : ".");
    }

    public static List<DeskBeat> Build(GameResult r, double homeChance, IReadOnlyList<string>? around = null,
        Func<Team, (OffensiveSystem system, DefensiveScheme scheme)?>? read = null, DeskContext? ctx = null)
    {
        ctx ??= DeskContext.None;
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
        // K29: the screen also carries the matchup (records, ranks, form) and the half's shape.
        Fill(storyRows, ctx.Matchup.Concat(FlowRows(r)).Concat(Filler(r)), MaxRows - 1);
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
        // K29: the screen shows both benches side by side (away, home): where they stand, what they run, and the numbers
        // the adjustment is about.
        string Stand(TeamBox b) => leader == null ? "level" : b == leader ? $"up {Math.Abs(margin)}" : $"down {Math.Abs(margin)}";
        string Off(TeamBox b) => b.Team.Tactic is { } t ? Systems.Name(t.System) : "-";
        string Def(TeamBox b) => b.Team.Tactic is { } t ? Systems.Name(t.Scheme) : "-";
        var adj = new List<DeskRow>
        {
            new("", a.Team.FullName, h.Team.FullName),
            new("Score", Stand(a), Stand(h), Math.Sign(margin), true),
            new("Offense", Off(a), Off(h)),
            new("Defense", Def(a), Def(h)),
            rows.First(x => x.Label == d.Label) with { Key = false },
            new("Points at the rim", PostgameDesk.RimPoints(a).ToString(), PostgameDesk.RimPoints(h).ToString(), Math.Sign(PostgameDesk.RimPoints(h) - PostgameDesk.RimPoints(a))),
        };
        Fill(adj, Filler(r));
        var lines = new List<StudioLine>();
        foreach (var b in new[] { trailer ?? a, leader ?? h })
        {
            bool behind = trailer != null ? b == trailer : b == a;
            string plan = behind ? Fix(d.Key) : Keep(d.Key);
            var t = b.Team.Tactic;
            string switchTo = read?.Invoke(b.Team) is { } c && t != null && (c.system != t.System || c.scheme != t.Scheme)
                ? $" The counter is there: switch to {Systems.Name(c.system).ToLower()} and {Systems.Name(c.scheme).ToLower()}." : "";
            lines.Add(new StudioLine(Big, $"{b.Team.FullName}: {plan}.{switchTo}"));
        }
        foreach (var l in lines) beats.Add(new("ADJUSTMENTS", "What each bench changes", adj, l));

        // 4. Around college: the night's other scores (only when there are some, and only on the full show: K29).
        if (around is { Count: > 0 } && ctx.Full)
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
        // K29: what the second half starts from: foul trouble, timeouts, and the half's shape.
        static int Trouble(TeamBox b) => b.Lines.Count(l => l.Fouls >= 3);
        prow.Add(new("Foul trouble (3+)", Trouble(a).ToString(), Trouble(h).ToString(), -Math.Sign(Trouble(h) - Trouble(a))));
        prow.Add(new("Timeouts left", a.Timeouts.ToString(), h.Timeouts.ToString()));
        Fill(prow, FlowRows(r).Concat(Filler(r)));
        string ptxt = comeback ? $"I'm going against the scoreboard: {pick.Team.FullName} come back and win by {by}."
            : leader == null ? $"Coin flip, but I'll take {pick.Team.FullName} by {by}."
            : $"{pick.Team.FullName} hold on. {pick.Team.Abbreviation} by {by}.";
        beats.Add(new("SECOND HALF", "The pick", prow, new StudioLine(Numbers, ptxt), $"{pick.Team.Abbreviation} by {by}"));
        return beats;
    }
}

/// <summary>
/// The postgame show (B4b, run 145): the final and how it went, the player of the game, how it was won (with the full
/// box's rim points, bench points and possessions, K17), the turning point from the score timeline, what it means for
/// the table, and around college. Pure: it reads the finished box score; the session supplies the table rows.
/// No interviews (#983).
/// </summary>
public static class PostgameDesk
{
    public static readonly StudioAnalyst Host = StudioShow.HostDesk, Numbers = StudioShow.Numbers, Big = StudioShow.BigMan;

    /// <summary>The leading scorer, the same way as the halftime desk: points, then rebounds plus assists, then the line order.</summary>
    internal static PlayerLine? Top(TeamBox b) => b.Lines.Where(l => l.Seconds > 0).OrderByDescending(l => l.Points).ThenByDescending(l => l.Rebounds + l.Assists).FirstOrDefault();

    /// <summary>The player of the game: the best game score on the winning side (the best overall if the loser's was 6+ better).</summary>
    public static (TeamBox Box, PlayerLine Line)? PlayerOfTheGame(GameResult r)
    {
        static (TeamBox, PlayerLine)? Best(TeamBox b) => b.Lines.Where(l => l.Seconds > 0).OrderByDescending(GameHighlights.GameScore).ThenByDescending(l => l.Points)
            .Select(l => ((TeamBox, PlayerLine)?)(b, l)).FirstOrDefault();
        var w = Best(r.Winner); var l = Best(r.Loser);
        if (w == null) return l;
        if (l != null && GameHighlights.GameScore(l.Value.Item2) >= GameHighlights.GameScore(w.Value.Item2) + 6) return l;
        return w;
    }

    /// <summary>The most a side led by after halftime (run 224, as <see cref="Hardwood.Core.Presentation.GameCalls.Final"/>).</summary>
    public static int LateLead(GameResult r, bool home)
    {
        int half = Math.Max(1, r.Periods / 2), led = 0;
        foreach (var p in r.Flow) if (p.Period > half) led = Math.Max(led, home ? p.Home - p.Away : p.Away - p.Home);
        return led;
    }

    public static int RimPoints(TeamBox b) => b.Lines.Sum(l => l.ZoneMakes[0]) * 2;
    public static int BenchPoints(TeamBox b) => b.Lines.Where(l => !l.Started).Sum(l => l.Points);

    public static List<DeskBeat> Build(GameResult r, IReadOnlyList<DeskRow>? table = null, string? tableLine = null, IReadOnlyList<string>? around = null,
        DeskContext? ctx = null)
    {
        ctx ??= DeskContext.None;
        TeamBox a = r.Away, h = r.Home;
        var win = r.Winner; var lose = r.Loser;
        int margin = h.Points - a.Points;
        string finalLabel = r.Overtimes == 0 ? "FINAL" : r.Overtimes == 1 ? "FINAL · OT" : $"FINAL · {r.Overtimes}OT";
        var score = new DeskRow(finalLabel, a.Points.ToString(), h.Points.ToString(), Math.Sign(margin), true);
        var beats = new List<DeskBeat>();

        // 1. The final: the score, each side's leading scorer, and the shape of the game in one line.
        var (ta, th) = (Top(a), Top(h));
        string Name(PlayerLine? l) => l == null ? "-" : l.Player.Name;
        string Line(PlayerLine? l) => l == null ? "-" : $"{l.Points} pts · {l.Rebounds} reb · {l.Assists} ast";
        var (leadH, leadA) = GameFlow.BiggestLeads(r);
        int loserLead = lose == h ? leadH : leadA, winnerLead = win == h ? leadH : leadA;
        int gap = Math.Abs(margin);
        bool winHomeSide = win == h;
        string story = $"{win.Team.FullName} beat {lose.Team.FullName} {win.Points}-{lose.Points}";
        // K43's leftover (run 224; review): the desk agrees with the final call (GameCalls.Final) when a big lead nearly
        // went: a 15-point lead after halftime, won by 6 or less, and not a comeback (10 down after halftime or 15 at any time).
        bool comebackWin = (r.Flow.Count > 0 && LateLead(r, !winHomeSide) >= 10) || loserLead >= 15;
        int lateLead = r.Flow.Count > 0 ? LateLead(r, winHomeSide) : 0;
        if (!comebackWin && lateLead >= 15 && gap <= 6)
            story += (r.Overtimes == 1 ? " in overtime" : r.Overtimes > 1 ? $" in {r.Overtimes} overtimes" : "")
                + $". They led by {lateLead} after halftime and had to hold off a late {lose.Team.Name} rally.";
        else if (r.Overtimes > 0) story += r.Overtimes == 1 ? " in overtime." : $" in {r.Overtimes} overtimes.";
        else if (r.Flow.Count > 0 && loserLead >= 10) story += $", and they were down {loserLead} at one point.";
        else if (r.Flow.Count > 0 && loserLead == 0) story += ": they led from the first basket to the last.";
        else if (gap >= 20) story += ". It was never close.";
        else if (gap <= 3) story += ": it came down to the last possession.";
        else story += ".";
        // H193 / K29: the host says what kind of night it was: the rivalry, or a win over a ranked side (on the road).
        bool winHome = win == h;
        int? loserRank = winHome ? ctx.AwayRank : ctx.HomeRank;
        if (ctx.Rivalry != null) story += $" That's {ctx.Rivalry}, and it goes to {win.Team.Name}.";
        else if (loserRank is { } lr) story += winHome || ctx.Neutral ? $" A win over No. {lr} {lose.Team.Name}." : $" A road win at No. {lr} {lose.Team.Name}.";
        // K29: the screen shows the game's shape (each period), the leading scorers and the biggest leads.
        var finalRows = new List<DeskRow> { score };
        if (r.Flow.Count > 0)
        {
            var (sh, sa) = GameFlow.Splits(r);
            for (int i = 0; i < sh.Length && finalRows.Count < 7; i++)   // review: two halves or four quarters plus two OTs
                finalRows.Add(new(GameFlow.PeriodName(r, i + 1), sa[i].ToString(), sh[i].ToString(), Math.Sign(sh[i] - sa[i])));
        }
        finalRows.Add(new("Leading scorer", Name(ta), Name(th)));
        finalRows.Add(new("", Line(ta), Line(th)));
        HalftimeDesk.Fill(finalRows, HalftimeDesk.FlowRows(r).Concat(HalftimeDesk.Filler(r)));
        beats.Add(new("POSTGAME", ctx.Occasion == "" ? "The final" : $"The final · {ctx.Occasion}", finalRows, new StudioLine(Host, story)));

        // 2. The player of the game.
        if (PlayerOfTheGame(r) is var (pb, pl))
        {
            var other = pb == h ? a : h;
            var ol = Top(other);
            string Fg(PlayerLine? l) => l == null ? "-" : $"{l.FGM}-{l.FGA}";
            string Three(PlayerLine? l) => l == null ? "-" : $"{l.ThreePM}-{l.ThreePA}";
            PlayerLine? Side(TeamBox b) => b == pb ? pl : ol;
            var rows = new List<DeskRow>
            {
                new("", Side(a)?.Player.Name ?? "-", Side(h)?.Player.Name ?? "-", pb == a ? -1 : 1, true),
                new("Points", $"{Side(a)?.Points}", $"{Side(h)?.Points}", Math.Sign((Side(h)?.Points ?? 0) - (Side(a)?.Points ?? 0))),
                new("Rebounds", $"{Side(a)?.Rebounds}", $"{Side(h)?.Rebounds}", Math.Sign((Side(h)?.Rebounds ?? 0) - (Side(a)?.Rebounds ?? 0))),
                new("Assists", $"{Side(a)?.Assists}", $"{Side(h)?.Assists}", Math.Sign((Side(h)?.Assists ?? 0) - (Side(a)?.Assists ?? 0))),
                new("Field goals", Fg(Side(a)), Fg(Side(h))),
                new("Threes", Three(Side(a)), Three(Side(h))),
            };
            string what = pl.Points >= 30 ? $"{pl.Points} points. That's a night you remember."
                : pl.Rebounds >= 12 && pl.Points >= 10 ? $"{pl.Points} and {pl.Rebounds}: he owned the paint."
                : pl.Assists >= 8 ? $"{pl.Assists} assists: he ran the whole thing."
                : $"{pl.Points} points, {pl.Rebounds} rebounds, {pl.Assists} assists: the most complete line on the floor.";
            string who = pb == win ? $"{pl.Player.Name} for {pb.Team.Abbreviation}. {what}" : $"{pl.Player.Name}, even in a loss for {pb.Team.Abbreviation}. {what}";
            beats.Add(new("PLAYER OF THE GAME", pl.Player.Name, rows, new StudioLine(Big, who)));
        }

        // 3. How it was won: the halftime desk's deciders over the whole game, plus the full box's rim points, bench and pace.
        var deciders = HalftimeDesk.Deciders(r);
        var d = HalftimeDesk.Decided(r);
        var how = new List<DeskRow> { score };
        how.AddRange(deciders.Select(x => new DeskRow(x.Label, x.Away, x.Home, Math.Sign(x.Worth), x.Key == d.Key)));
        int rimA = RimPoints(a), rimH = RimPoints(h), benA = BenchPoints(a), benH = BenchPoints(h);
        how.Add(new("Points at the rim", rimA.ToString(), rimH.ToString(), Math.Sign(rimH - rimA)));
        how.Add(new("Bench points", benA.ToString(), benH.ToString(), Math.Sign(benH - benA)));
        if (a.Possessions > 0 || h.Possessions > 0) how.Add(new("Possessions", a.Possessions.ToString(), h.Possessions.ToString()));
        var fav = d.Worth > 0 ? h : a;
        string why = Math.Abs(d.Worth) < 1.5
            ? "Nothing on the sheet separates them. A couple of possessions went one way."
            : $"{d.Label}: {fav.Team.Abbreviation} {(fav == h ? d.Home : d.Away)} against {(fav == h ? d.Away : d.Home)}. That's about {Math.Abs(d.Worth):0} points"
              + (fav == win ? ", and it's the game." : ", and they still lost it.");
        int rimWin = win == h ? rimH : rimA, rimLose = win == h ? rimA : rimH;
        if (rimWin >= rimLose + 10) why += $" Add {rimWin}-{rimLose} at the rim.";
        beats.Add(new("BY THE NUMBERS", "How it was won", how, new StudioLine(Numbers, why)));

        // 4. The turning point: the biggest run on the score timeline (games with a play log only; the full show only, K29).
        if (r.Flow.Count > 0 && ctx.Full)
        {
            var runs = GameFlow.Runs(r);
            var best = runs.OrderByDescending(x => x.For - x.Against).ThenBy(x => x.StartElapsed).FirstOrDefault();
            int changes = GameFlow.LeadChanges(r);
            var rows = new List<DeskRow>
            {
                score,
                new("Biggest lead", leadA.ToString(), leadH.ToString(), Math.Sign(leadH - leadA)),
                new("Lead changes", changes.ToString(), ""),
            };
            rows.Add(new("Times tied", GameFlow.TimesTied(r).ToString(), ""));
            if (best != null) rows.Add(new("Biggest run", best.Home ? "" : best.Score, best.Home ? best.Score : "", best.Home ? 1 : -1, true));
            HalftimeDesk.Fill(rows, HalftimeDesk.Filler(r));
            string turn = best == null
                ? (changes >= 8 ? $"No real run either way, and {changes} lead changes. A street fight." : "Nobody went on a run. It was a grind, basket for basket.")
                : $"The game turned on {GameFlow.Describe(r, best)}." + ((best.Home ? h : a) == win ? "" : " They still couldn't finish it.")
                  + (changes >= 8 ? $" {changes} lead changes before that." : "");
            beats.Add(new("TURNING POINT", "Where it swung", rows, new StudioLine(Big, turn)));
        }

        // 5. What it means: the table as it stands now (supplied by the session).
        // K29: plus the matchup rows the table doesn't already show (the conference record, recent form, the series), above "Next".
        if (table is { Count: > 0 })
        {
            var means = table.Where(x => x.Label != "Next").ToList();
            HalftimeDesk.Fill(means, ctx.Matchup, HalftimeDesk.MaxRows - 1);
            means.AddRange(table.Where(x => x.Label == "Next"));
            beats.Add(new("WHAT IT MEANS", "The standings now", means, tableLine == null ? null : new StudioLine(Host, tableLine)));
        }

        // 6. Around college: tonight's other ranked games (the full show only, K29).
        if (around is { Count: > 0 } && ctx.Full)
        {
            var shown = around.Take(6).ToList();   // review (run 145): the line counts the rows on the screen
            int finals = shown.Count(x => x.StartsWith("Final"));
            string line = finals == shown.Count ? (finals == 1 ? "One more final from around the country." : $"{finals} more finals from around the country.")
                : finals == 0 ? (shown.Count == 1 ? "One more game with a ranked team still to come tonight." : $"{shown.Count} more games with a ranked team still to come tonight.")
                : $"{finals} final{(finals == 1 ? "" : "s")} in from around the country, and {shown.Count - finals} still to come later tonight.";
            beats.Add(new("AROUND COLLEGE", "Elsewhere tonight", shown.Select(x => new DeskRow("", x, "")).ToList(), new StudioLine(Host, line)));
        }
        return beats;
    }
}

public sealed partial class GameSession
{
    /// <summary>
    /// The postgame show's "what it means" (B4b): both sides' records once tonight's result counts (it is recorded on Continue),
    /// their Top 25 ranks, and your next game. Rows are away, home.
    /// </summary>
    public (List<DeskRow> Rows, string? Line) PostgameTable(GameResult r)
    {
        var (a, h) = (r.Away.Team, r.Home.Team);
        bool counts = LiveGameCountsInRecord;
        string Rec(Team t)
        {
            var rec = RecordOf(t);
            int w = rec.Wins, l = rec.Losses;
            if (counts) { if (r.Winner.Team == t) w++; else l++; }
            return w + l == 0 ? "" : $"{w}-{l}";
        }
        var rows = new List<DeskRow> { new("", a.FullName, h.FullName) };
        string ra = Rec(a), rh = Rec(h);
        if (ra != "" || rh != "") rows.Add(new("Record", ra, rh, Key: true));
        if (InCollege && World.College is { } cw)
        {
            int? rkA = cw.Rank(a.Id), rkH = cw.Rank(h.Id);
            if (rkA != null || rkH != null) rows.Add(new("Top 25", rkA is { } x ? $"No. {x}" : "-", rkH is { } y ? $"No. {y}" : "-"));
        }
        var us = HumanTeam;
        string? line = null;
        if (us == a || us == h)
        {
            var next = MyGames().FirstOrDefault(g => !g.Played && g.Date > Today);
            string rec = us == a ? ra : rh;
            // Review (run 145): a game that doesn't count in the record (a playoff or cup game) doesn't "move" it.
            string result = counts ? (r.Winner.Team == us ? $"{us.FullName} win and move to {rec}." : $"{us.FullName} fall to {rec}.")
                : r.Winner.Team == us ? $"A win for {us.FullName}." : $"A loss for {us.FullName}.";
            // A neutral-site game is "vs" even when you're the listed home side.
            string Where(MyGame g) => g.Neutral ? "vs" : g.Home ? "home to" : "at";
            line = result
                + (next == null ? (counts ? " That's the last game on the schedule for now." : "")
                   : $" Next up: {(next.Neutral ? $"{next.Opponent.FullName} on a neutral floor" : next.Home ? $"{next.Opponent.FullName} at home" : $"at {next.Opponent.FullName}")}, {next.Date:ddd MMM d}.");
            if (next != null)
            {
                string cell = $"{Where(next)} {next.Opponent.Abbreviation} · {next.Date:MMM d}";
                rows.Add(new("Next", us == a ? cell : "", us == h ? cell : ""));
            }
            if (counts && rec == "") line = null;
        }
        return (rows, line);
    }


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
