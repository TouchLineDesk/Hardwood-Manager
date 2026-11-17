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
    public static (int home, int away) Play(Team home, Team away, DateOnly date, Rng rng, Dictionary<int, StatLine> stats, Rules? rules = null, double perTeam = 0, bool neutral = false, double homePoints = 2.5, double totalScale = 1.0)
    {
        // FIBA's 40 minutes score ~80 a side; NBA rules (48 minutes, the USA pyramid) ~100; each league can set its own level.
        bool long48 = (rules?.GameMinutes ?? 40) >= 48;
        double level = perTeam > 0 ? perTeam : long48 ? 101 : 80;
        double spread = level / 80.0;
        // College (C4 step 0, #188): matched to the full engine under college rules, so a computer program's record means
        // what yours does (`hardwood calib`: 0.52 points of margin a strength point, 8.9 either side).
        bool college = rules is { TalentScale: > 1 };
        double perPoint = college ? 0.52 : 0.55 * spread, sd = college ? 9.0 : 11 * spread;
        double margin = rng.NextGaussian((Strength(home, date) - Strength(away, date)) * perPoint + (neutral ? 0 : homePoints), sd);
        double total = rng.NextGaussian(level * 2 * totalScale, 12 * spread); // C5 (run 96): a college conference's style
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
        // H63 (run 91): steals, blocks, threes and shooting by skill come from a side stream seeded by the game itself,
        // so the main stream (and every seeded result) is untouched. Before this they were never recorded here, and the
        // League Leaders' steals, blocks, 3P% and FG% boards outside full-engine games held only your club's players.
        var side = new Rng(SideSeed(t.Id, date, points));
        bool long48 = gameMinutes >= 48;
        int teamStl = Math.Max(0, (int)Math.Round(side.NextGaussian(long48 ? 7.6 : 6.6, 2.2)));
        int teamBlk = Math.Max(0, (int)Math.Round(side.NextGaussian(long48 ? 4.9 : 3.3, 1.8)));
        int teamTpa = Math.Max(0, (int)Math.Round(side.NextGaussian(points * (long48 ? 0.36 : 0.34), 4)));
        var stl = Share(teamStl, rotation.Select((p, i) => mins[i] * (0.5 + p.Attributes[Attr.Steal] / 10.0) * (0.6 + p.Tendencies.GambleSteals / 75.0)).ToArray());
        var blk = Share(teamBlk, rotation.Select((p, i) => mins[i] * Math.Pow(p.Attributes[Attr.Block] / 10.0, 1.3) * (p.PrimaryPosition switch { Position.C => 2.6, Position.PF => 1.8, Position.SF => 0.9, _ => 0.5 })).ToArray());
        var tpa = Share(teamTpa, rotation.Select((p, i) => pts[i] * (0.03 + p.Tendencies.ThreePointRate / 100.0) * (0.4 + p.Attributes[Attr.ThreePoint] / 10.0)).ToArray());
        for (int i = 0; i < rotation.Count; i++)
        {
            var p = rotation[i];
            var st = stats.TryGetValue(p.Id, out var x) ? x : stats[p.Id] = new StatLine();
            st.TeamId = t.Id; st.G++; st.Min += mins[i] * scale; st.Pts += pts[i]; st.Reb += reb[i]; st.Ast += ast[i];
            int fga = (int)Math.Round(pts[i] / 1.25); // NBA 2024-25: about 1.27 points a shot (was 1.1, with a flat 46%)
            var (fgm, threesA, threesM) = Shooting(p, pts[i], fga, tpa[i], side);
            st.Fga += fga; st.Fgm += fgm; st.Tpa += threesA; st.Tpm += threesM; st.Stl += stl[i]; st.Blk += blk[i];
        }
    }

    /// <summary>A player's makes from his attempts: three-point % from his three-point rating (about 35% at 10 of 20), two-point %
    /// from his finishing (about 52%); never more makes than his points allow.</summary>
    public static (int Fgm, int Tpa, int Tpm) Shooting(Player p, int points, int fga, int tpa, Rng side)
    {
        tpa = Math.Min(tpa, fga);
        double p3 = Math.Clamp(0.27 + 0.008 * p.Attributes[Attr.ThreePoint], 0.2, 0.46);
        int tpm = 0;
        for (int k = 0; k < tpa; k++) if (side.NextDouble() < p3) tpm++;
        tpm = Math.Min(tpm, points / 3);
        double finish = (p.Attributes[Attr.CloseShot] + p.Attributes[Attr.Layup] + p.Attributes[Attr.MidRange]) / 3.0;
        double p2 = Math.Clamp(0.44 + 0.008 * finish + (p.PrimaryPosition is Position.C ? 0.04 : p.PrimaryPosition is Position.PF ? 0.02 : 0), 0.38, 0.66);
        int twoA = fga - tpa, twoM = 0;
        for (int k = 0; k < twoA; k++) if (side.NextDouble() < p2) twoM++;
        twoM = Math.Min(twoM, Math.Max(0, (points - 3 * tpm) / 2));
        return (tpm + twoM, tpa, tpm);
    }

    private static ulong SideSeed(int teamId, DateOnly date, int points) =>
        0xC0FFEE1234567UL ^ ((ulong)(uint)teamId << 32) ^ ((ulong)(uint)date.DayNumber << 8) ^ (ulong)(uint)points * 0x9E3779B97F4A7C15UL;

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
