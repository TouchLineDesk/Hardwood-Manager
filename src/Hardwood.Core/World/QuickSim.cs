using Hardwood.Core.Model;

namespace Hardwood.Core.World;

/// <summary>
/// Results for background competitions (decision #40): a score from the two teams' strength plus home court and
/// noise, and believable stat lines shared out by minutes and ability. Thousands of times cheaper than the full engine.
/// </summary>
public static class QuickSim
{
    private static readonly double[] TopEight = { .18, .16, .14, .13, .12, .10, .09, .08 };
    private static readonly double[] Minutes = { 34, 32, 30, 28, 26, 20, 14, 10, 6 };

    /// <summary>Weighted ability of a team's best eight available players.</summary>
    public static double Strength(Team t, DateOnly? on = null)
    {
        var best = (on is { } d ? t.Available(d) : t.Roster).Select(p => (double)p.CurrentAbility).OrderByDescending(x => x).Take(8).ToList();
        double s = 0;
        for (int i = 0; i < TopEight.Length; i++) s += TopEight[i] * (i < best.Count ? best[i] : 60);
        return s;
    }

    /// <summary>Play one game. Returns the score and adds each player's line to <paramref name="stats"/>.</summary>
    public static (int home, int away) Play(Team home, Team away, DateOnly date, Rng rng, Dictionary<int, StatLine> stats, Rules? rules = null)
    {
        // FIBA's 40 minutes score ~80 a side; NBA rules (48 minutes, the USA pyramid) ~100.
        bool long48 = (rules?.GameMinutes ?? 40) >= 48;
        double margin = rng.NextGaussian((Strength(home, date) - Strength(away, date)) * 0.55 * (long48 ? 1.2 : 1) + 2.5, long48 ? 13 : 11);
        double total = rng.NextGaussian(long48 ? 202 : 160, long48 ? 14 : 12);
        int hp = (int)Math.Round((total + margin) / 2), ap = (int)Math.Round((total - margin) / 2);
        if (hp == ap) { if (rng.Chance(0.5)) hp += rng.Next(1, 6); else ap += rng.Next(1, 6); }
        int minutes = rules?.GameMinutes ?? 40;
        Lines(home, date, hp, rng, stats, minutes);
        Lines(away, date, ap, rng, stats, minutes);
        return (hp, ap);
    }

    private static void Lines(Team t, DateOnly date, int points, Rng rng, Dictionary<int, StatLine> stats, int gameMinutes = 40)
    {
        var rotation = t.Available(date).OrderByDescending(p => p.CurrentAbility).Take(Minutes.Length).ToList();
        if (rotation.Count == 0) return;
        var mins = rotation.Select((p, i) => Math.Max(0, Minutes[i] + rng.NextGaussian(0, 3))).ToArray();
        double scale = gameMinutes * 5 / mins.Sum();
        var weight = rotation.Select((p, i) => mins[i] * scale * Math.Pow(p.CurrentAbility / 100.0, 2) * Math.Exp(rng.NextGaussian(0, 0.25))).ToArray();
        int teamReb = (int)Math.Round(rng.NextGaussian(36, 4)), teamAst = (int)Math.Round(rng.NextGaussian(18, 3));
        var pts = Share(points, weight);
        var reb = Share(teamReb, rotation.Select((p, i) => mins[i] * (p.PrimaryPosition switch { Position.C => 1.8, Position.PF => 1.5, Position.SF => 1.0, _ => 0.7 })).ToArray());
        var ast = Share(teamAst, rotation.Select((p, i) => mins[i] * (p.PrimaryPosition switch { Position.PG => 2.2, Position.SG => 1.2, Position.SF => 0.9, _ => 0.6 })).ToArray());
        for (int i = 0; i < rotation.Count; i++)
        {
            var p = rotation[i];
            var st = stats.TryGetValue(p.Id, out var x) ? x : stats[p.Id] = new StatLine();
            st.TeamId = t.Id; st.G++; st.Min += mins[i] * scale; st.Pts += pts[i]; st.Reb += reb[i]; st.Ast += ast[i];
            int fga = (int)Math.Round(pts[i] / 1.1); st.Fga += fga; st.Fgm += (int)Math.Round(fga * 0.46);
        }
    }

    /// <summary>Split a whole number in proportion to weights (largest remainders), so lines add up to the team total.</summary>
    private static int[] Share(int total, double[] w)
    {
        double sum = w.Sum();
        if (sum <= 0 || total <= 0) return new int[w.Length];
        var raw = w.Select(x => x / sum * total).ToArray();
        var whole = raw.Select(x => (int)Math.Floor(x)).ToArray();
        int left = total - whole.Sum();
        foreach (var i in Enumerable.Range(0, w.Length).OrderByDescending(i => raw[i] - whole[i]).Take(left)) whole[i]++;
        return whole;
    }
}
