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
            if (ev.End > newYear || ev.End < summer.AddMonths(-6)) continue;
            if (World.International.WorldCups.Any(w => w.Year == y)) continue;
            if (WorldCup.Play(this, y, _sim) is not { } rec) continue;
            World.International.WorldCups.Add(rec);
            Post($"World Cup {y}: {rec.NameOf(rec.Gold)} win gold", WorldCupStory(rec), "World");
        }
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
