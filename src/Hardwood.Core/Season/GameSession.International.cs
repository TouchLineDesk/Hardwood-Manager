using Hardwood.Core.International;

namespace Hardwood.Core.Season;

public sealed partial class GameSession
{
    /// <summary>
    /// International plan I2 (run 209): a World Cup that ended over the summer is played at the summer's jump to the new season
    /// (the date holds in May and jumps to October, run 197's lesson), once, from its own stream (<see cref="WorldCup.Play(GameSession, int, Engine.GameSimulator?)"/>).
    /// Nothing the shared streams, players or clubs see changes: the inbox gets the result.
    /// </summary>
    private void PlayInternationalOverSummer(DateOnly summer, DateOnly newYear)
    {
        for (int y = summer.Year; y <= newYear.Year; y++)
        {
            if (WorldCup.EventIn(y) is not { } ev) continue;
            // Review (run 209): a jump that lands mid-tournament (a league that opens before Sep 15) still plays it, whole.
            if (ev.Start >= newYear || ev.End < summer.AddMonths(-6)) continue;
            if (World.International.WorldCups.Any(w => w.Year == y)) continue;
            if (WorldCup.Play(this, y, _sim) is not { } rec) continue;
            World.International.WorldCups.Add(rec);
            RankWorldCup(rec);
            Post($"World Cup {y}: {rec.NameOf(rec.Gold)} win gold", WorldCupStory(rec), "World");
        }
    }

    /// <summary>I3 (run 210): every World Cup game moves the FIBA ranking.</summary>
    private void RankWorldCup(WorldCupRecord rec)
    {
        var h = World.International;
        var teams = NationalTeams.Build(WorldCup.Entries(this, Today), Today);
        var points = FibaRanking.Current(h, teams);
        var before = FibaRanking.Apply(teams, points).Where(t => t.Rank > 0).ToDictionary(t => t.Nation.Code, t => t.Rank);
        foreach (var g in rec.Games) FibaRanking.Update(points, g, FibaRanking.WorldCupK);
        h.Ranking = points;
        h.LastRanking = before;
    }

    /// <summary>
    /// I3 (run 210): a FIBA window that ended today (or within <see cref="Qualifying.CatchUpDays"/>, after a jump) is played
    /// once, by quick result on its own stream, from today's rosters (players fit on the window's first day). No player, club or
    /// shared stream is touched. The inbox gets one message.
    /// </summary>
    private void PlayFibaWindows()
    {
        foreach (var w in Qualifying.DueWindows(Today))
        {
            var h = World.International;
            string key = Qualifying.KeyOf(w);
            if (h.Windows.Any(x => x.Key == key)) continue;
            var teams = NationalTeams.Build(WorldCup.Entries(this, w.Start), w.Start);
            if (teams.Count(t => t.Rank > 0) < 2) continue;
            var rec = Qualifying.Play(h, w, teams, Seed);
            Post($"FIBA window: qualifiers for the {rec.Target}", WindowStory(rec), "World");
        }
    }

    /// <summary>The window's inbox story: the top of the ranking, the biggest results, and your own players' nations.</summary>
    internal string WindowStory(WindowRecord rec)
    {
        var h = World.International;
        var teams = NationalTeams.Build(WorldCup.Entries(this, Today), Today);
        var ranked = FibaRanking.Apply(teams, FibaRanking.Current(h, teams)).Where(t => t.Rank > 0).ToList();
        string Name(string code) => ranked.FirstOrDefault(t => t.Nation.Code == code)?.Name ?? code;
        var lines = new List<string>
        {
            $"{rec.Games.Count} qualifiers were played ({rec.Dates}), window {rec.Index + 1} of the {rec.Target} cycle. Club seasons carried on.",
            "FIBA world ranking: " + string.Join(", ", ranked.Take(5).Select(t => $"{t.Rank}. {t.Name}" + Move(h, t))) + ".",
        };
        var upsets = rec.Games.Select(g => (g, gap: 0))
            .Where(x => (h.LastRanking.GetValueOrDefault(x.g.Winner, 999)) > (h.LastRanking.GetValueOrDefault(x.g.Loser, 999)) + 8)
            .OrderByDescending(x => h.LastRanking.GetValueOrDefault(x.g.Winner, 999) - h.LastRanking.GetValueOrDefault(x.g.Loser, 999)).Take(2).ToList();
        if (upsets.Count > 0)
            lines.Add("Upsets: " + string.Join("; ", upsets.Select(x => $"{Name(x.g.Winner)} beat {Name(x.g.Loser)} {Math.Max(x.g.HomePts, x.g.AwayPts)}-{Math.Min(x.g.HomePts, x.g.AwayPts)}")) + ".");
        if (Employed)
        {
            var mine = HumanTeam.Roster.Select(p => p.Id).ToHashSet();
            var nations = ranked.Where(t => t.Squad.Any(x => mine.Contains(x.Player.Id))).ToList();
            foreach (var t in nations)
            {
                var gs = rec.Games.Where(g => g.Home == t.Nation.Code || g.Away == t.Nation.Code).ToList();
                if (gs.Count == 0) continue;
                lines.Add($"{t.Name} (with " + string.Join(", ", t.Squad.Where(x => mine.Contains(x.Player.Id)).Select(x => x.Player.Name)) + "): "
                    + string.Join(", ", gs.Select(g => (g.Winner == t.Nation.Code ? "beat " : "lost to ") + Name(g.Home == t.Nation.Code ? g.Away : g.Home)
                        + $" {Math.Max(g.HomePts, g.AwayPts)}-{Math.Min(g.HomePts, g.AwayPts)}")) + $"; now No. {t.Rank}.");
            }
        }
        lines.Add("The groups and the ranking are on the International page. Players aren't called up from clubs yet (a later build).");
        return string.Join("\n\n", lines);
    }

    /// <summary>" (up 2)", " (down 1)" or "" against the rank before the latest window or tournament.</summary>
    public static string Move(InternationalHistory h, NationalTeam t)
    {
        if (!h.LastRanking.TryGetValue(t.Nation.Code, out var was) || was == t.Rank) return "";
        return was > t.Rank ? $" (up {was - t.Rank})" : $" (down {t.Rank - was})";
    }

    /// <summary>The inbox story: medals, the final, the MVP, the top scorer and your own players who were there.</summary>
    internal string WorldCupStory(WorldCupRecord rec)
    {
        var f = rec.Final!;
        var lines = new List<string>
        {
            $"{rec.NameOf(rec.Gold)} beat {rec.NameOf(rec.Silver)} {Math.Max(f.HomePts, f.AwayPts)}-{Math.Min(f.HomePts, f.AwayPts)} in the final in {rec.Host} ({rec.Dates}). {rec.NameOf(rec.Bronze)} took bronze over {rec.NameOf(rec.Fourth)}.",
        };
        if (rec.Mvp is { } mvp) lines.Add($"MVP: {mvp.Name} ({mvp.Code}), {mvp.Ppg:0.0} points, {mvp.Rpg:0.0} rebounds and {mvp.Apg:0.0} assists a game.");
        if (rec.TopScorers.FirstOrDefault() is { } top) lines.Add($"Top scorer: {top.Name} ({top.Code}), {top.Ppg:0.0} a game.");
        if (Employed)
        {
            var mine = HumanTeam.Roster.Select(p => p.Id).ToHashSet();
            var ours = rec.Teams.SelectMany(t => t.Squad.Where(mine.Contains).Select(id => (t, p: HumanTeam.Roster.First(p => p.Id == id)))).ToList();
            if (ours.Count > 0) lines.Add("Ours at the World Cup: " + string.Join(", ", ours.Select(x => $"{x.p.Name} ({x.t.Name}, {Placing(rec, x.t.Code)})")) + ".");
        }
        lines.Add("The groups, the knockouts and the all-star five are on the International page.");
        return string.Join("\n\n", lines);
    }

    /// <summary>"gold", "silver", "bronze", "fourth", "out in the quarter-finals", …</summary>
    public static string Placing(WorldCupRecord rec, string code)
    {
        if (code == rec.Gold) return "gold";
        if (code == rec.Silver) return "silver";
        if (code == rec.Bronze) return "bronze";
        if (code == rec.Fourth) return "fourth";
        if (rec.Games.Any(g => g.Round == WorldCup.QuarterRound && (g.Home == code || g.Away == code))) return "out in the quarter-finals";
        if (rec.Games.Any(g => g.Round == WorldCup.SecondRound && (g.Home == code || g.Away == code))) return "out in the second round";
        return "out in the group";
    }
}
