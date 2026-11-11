using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>One highlight card: a headline, the number that matters, the detail, and how it reads (good/bad/info…).</summary>
public sealed record Highlight(string Kind, string Title, string Value, string Detail, string Tone, Player? Player = null, Team? Team = null);

/// <summary>
/// Match day part 2 (roadmap 2): the moments worth a card — player of the game, the shot that won it, comebacks,
/// runs, big nights — and the one-line facts the live ticker cycles through. Everything comes from the box score and
/// the score timeline, so the cards always agree with the numbers on the page.
/// </summary>
public static class GameHighlights
{
    /// <summary>Hollinger's game score: one number for a whole stat line.</summary>
    public static double GameScore(PlayerLine l) =>
        l.Points + 0.4 * l.FGM - 0.7 * l.FGA - 0.4 * (l.FTA - l.FTM) + 0.7 * l.OReb + 0.3 * l.DReb + l.Steals + 0.7 * l.Assists + 0.7 * l.Blocks - 0.4 * l.Fouls - l.Turnovers;

    public static string StatLine(PlayerLine l)
    {
        var parts = new List<string> { $"{l.Points} pts" };
        if (l.Rebounds >= 5) parts.Add($"{l.Rebounds} reb");
        if (l.Assists >= 5) parts.Add($"{l.Assists} ast");
        if (l.Steals >= 3) parts.Add($"{l.Steals} stl");
        if (l.Blocks >= 3) parts.Add($"{l.Blocks} blk");
        return string.Join(" · ", parts);
    }

    /// <summary>How many of points, rebounds, assists, steals and blocks reached ten.</summary>
    public static int Doubles(PlayerLine l) => new[] { l.Points, l.Rebounds, l.Assists, l.Steals, l.Blocks }.Count(x => x >= 10);

    private static IEnumerable<(PlayerLine Line, TeamBox Box)> Lines(GameResult r) =>
        r.Away.Lines.Select(l => (l, r.Away)).Concat(r.Home.Lines.Select(l => (l, r.Home))).Where(x => x.l.Seconds > 0);

    /// <summary>
    /// The game's highlight cards, most important first (at most <paramref name="max"/>). <paramref name="final"/> is false
    /// while a game is still being played: cards then speak of "so far" and nothing claims to have won it.
    /// </summary>
    public static List<Highlight> For(GameResult r, bool final = true, int max = 6)
    {
        var cards = new List<Highlight>();
        var lines = Lines(r).ToList();
        if (lines.Count == 0) return cards;

        // Player of the game.
        var best = lines.OrderByDescending(x => GameScore(x.Line)).ThenByDescending(x => x.Line.Points).First();
        cards.Add(new("star", final ? "Player of the game" : "Best so far", StatLine(best.Line), $"{best.Line.Player.Name} ({best.Box.Team.Abbreviation})", "team", best.Line.Player, best.Box.Team));

        if (r.Flow.Count > 0)
        {
            bool homeWon = r.Home.Points > r.Away.Points;
            var winner = homeWon ? r.Home : r.Away;
            // The shot that won it: the last time the eventual winner took the lead, if it came late.
            if (final && r.Home.Points != r.Away.Points)
            {
                int sign = homeWon ? 1 : -1, prev = 0;
                FlowPoint? decisive = null;
                foreach (var p in r.Flow)
                {
                    int now = Math.Sign(p.Home - p.Away);
                    if (now == sign && prev != sign) decisive = p;
                    prev = now;
                }
                if (decisive is { } d && GameFlow.SecondsLeft(r, d.Elapsed, d.Period) <= 120 && d.Period >= r.Periods)
                {
                    double left = GameFlow.SecondsLeft(r, d.Elapsed, d.Period);
                    string who = d.Scorer?.Name ?? winner.Team.Abbreviation;
                    cards.Add(new("clutch", "Game winner", $"{(int)(left / 60)}:{(int)(left % 60):00} left",
                        $"{who} put {winner.Team.Abbreviation} ahead for good, {d.Away}-{d.Home}", "good", d.Scorer, winner.Team));
                }
            }

            // Comeback: how far the winner (or the side ahead now) was behind.
            var leader = r.Home.Points >= r.Away.Points ? r.Home : r.Away;
            bool leaderHome = leader == r.Home;
            int deficit = r.Flow.Max(p => leaderHome ? p.Away - p.Home : p.Home - p.Away);
            if (deficit >= 10 && r.Home.Points != r.Away.Points)
                cards.Add(new("comeback", final ? "Comeback" : "Fightback", $"{deficit} down",
                    $"{leader.Team.Abbreviation} {(final ? "came back to win" : "came back to lead")}", "good", null, leader.Team));
            else if (final && deficit <= 0 && r.Home.Points != r.Away.Points)
                cards.Add(new("wire", "Wire to wire", "Never trailed", $"{leader.Team.Abbreviation} led from start to finish", "info", null, leader.Team));

            var run = GameFlow.Runs(r).OrderByDescending(x => x.For - x.Against).FirstOrDefault();
            if (run != null)
                cards.Add(new("run", "Biggest run", run.Score, GameFlow.Describe(r, run), "warn", null, run.Home ? r.Home.Team : r.Away.Team));
        }

        // Big individual nights, best first; one card each.
        var triple = lines.Where(x => Doubles(x.Line) >= 3).OrderByDescending(x => GameScore(x.Line)).FirstOrDefault();
        if (triple.Line != null && triple.Line != best.Line)
            cards.Add(new("triple", "Triple-double", StatLine(triple.Line), $"{triple.Line.Player.Name} ({triple.Box.Team.Abbreviation})", "good", triple.Line.Player, triple.Box.Team));
        var threes = lines.OrderByDescending(x => x.Line.ThreePM).ThenBy(x => x.Line.ThreePA).First();
        if (threes.Line.ThreePM >= 5)
            cards.Add(new("threes", "Hot from deep", $"{threes.Line.ThreePM}-{threes.Line.ThreePA} 3PT", $"{threes.Line.Player.Name} ({threes.Box.Team.Abbreviation})", "good", threes.Line.Player, threes.Box.Team));
        var scorer = lines.OrderByDescending(x => x.Line.Points).First();
        if (scorer.Line.Points >= 35 && scorer.Line != best.Line)
            cards.Add(new("scorer", "Big scoring night", $"{scorer.Line.Points} pts", $"{scorer.Line.Player.Name} ({scorer.Box.Team.Abbreviation})", "good", scorer.Line.Player, scorer.Box.Team));
        var dbl = lines.Where(x => Doubles(x.Line) == 2 && x.Line != best.Line).OrderByDescending(x => GameScore(x.Line)).FirstOrDefault();
        if (dbl.Line != null)
            cards.Add(new("double", "Double-double", StatLine(dbl.Line), $"{dbl.Line.Player.Name} ({dbl.Box.Team.Abbreviation})", "info", dbl.Line.Player, dbl.Box.Team));
        var stopper = lines.OrderByDescending(x => x.Line.Blocks + x.Line.Steals).First();
        if (stopper.Line.Blocks + stopper.Line.Steals >= 5)
            cards.Add(new("defence", "Defensive stopper", $"{stopper.Line.Steals} stl · {stopper.Line.Blocks} blk", $"{stopper.Line.Player.Name} ({stopper.Box.Team.Abbreviation})", "info", stopper.Line.Player, stopper.Box.Team));
        var fouled = lines.Where(x => x.Line.FouledOut).OrderByDescending(x => x.Line.Player.Overall).FirstOrDefault();
        if (fouled.Line != null)
            cards.Add(new("fouls", "Fouled out", $"{fouled.Line.Minutes:0} min", $"{fouled.Line.Player.Name} ({fouled.Box.Team.Abbreviation})", "bad", fouled.Line.Player, fouled.Box.Team));

        return cards.Take(max).ToList();
    }

    /// <summary>One-line facts for the live stat ticker: leading scorers, shooting, the glass, turnovers, foul trouble, runs.</summary>
    public static List<string> Ticker(GameResult r)
    {
        var facts = new List<string>();
        foreach (var box in new[] { r.Away, r.Home })
        {
            var top = box.Lines.Where(l => l.Seconds > 0).OrderByDescending(l => l.Points).ThenByDescending(l => GameScore(l)).FirstOrDefault();
            if (top != null && top.Points > 0) facts.Add($"{box.Team.Abbreviation} leader: {top.Player.Name}, {StatLine(top)}");
        }
        string Pct(int m, int a) => a == 0 ? "–" : $"{100.0 * m / a:0}%";
        if (r.Away.FGA + r.Home.FGA > 0)
            facts.Add($"Shooting: {r.Away.Team.Abbreviation} {Pct(r.Away.FGM, r.Away.FGA)} ({r.Away.ThreePM}-{r.Away.ThreePA} from three), {r.Home.Team.Abbreviation} {Pct(r.Home.FGM, r.Home.FGA)} ({r.Home.ThreePM}-{r.Home.ThreePA})");
        if (r.Away.OReb + r.Away.DReb + r.Home.OReb + r.Home.DReb > 0)
            facts.Add($"Rebounds: {r.Away.Team.Abbreviation} {r.Away.OReb + r.Away.DReb} ({r.Away.OReb} offensive), {r.Home.Team.Abbreviation} {r.Home.OReb + r.Home.DReb} ({r.Home.OReb})");
        if (r.Away.Turnovers + r.Home.Turnovers > 0)
            facts.Add($"Turnovers: {r.Away.Team.Abbreviation} {r.Away.Turnovers}, {r.Home.Team.Abbreviation} {r.Home.Turnovers}");
        foreach (var (l, box) in Lines(r).Where(x => !x.Line.FouledOut && x.Line.Fouls >= 4).OrderByDescending(x => x.Line.Fouls).Take(2))
            facts.Add($"Foul trouble: {l.Player.Name} ({box.Team.Abbreviation}) has {l.Fouls} fouls");
        if (GameFlow.LeadChanges(r) is var lc and > 0) facts.Add($"Lead changes: {lc}");
        if (GameFlow.CurrentRun(r) is { } run) facts.Add($"{run.Team} on a {run.Score} run");
        return facts;
    }
}
