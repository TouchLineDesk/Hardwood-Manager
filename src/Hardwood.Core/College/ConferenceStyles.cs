using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>
/// A conference's playing style (C5, the last piece; college-sim-design #165: "each conference has a style (pace, defence,
/// physicality) that shows in its games"). Pace multiplies possessions (0.95–1.05); defence (−1 to +1) takes up to 0.04
/// off every shot's make logit on both sides; physicality (−1 to +1) moves the foul rates by up to 10%. Both sides play
/// the same style, so the favourite's edge barely moves: the style changes how a game looks (score, possessions, free
/// throws), not who wins. Real conferences follow their reputations (KenPom tempo, the Big Ten's and Big 12's physical,
/// defensive play, the SWAC's and MEAC's fast games); the fictional ones get styles of their own. No random numbers.
/// </summary>
public sealed record ConferenceStyle(double Pace, double Defence, double Physical)
{
    public static readonly ConferenceStyle Neutral = new(1.0, 0, 0);

    public const double DefenceLogit = 0.04, FoulSpread = 0.10;

    /// <summary>Added to every shot's make logit (both sides).</summary>
    public double ShotLogitShift => -DefenceLogit * Defence;
    public double FoulScale => 1 + FoulSpread * Physical;

    /// <summary>The quick sim's total-points factor, matched to what the full engine does with the same style (run 96,
    /// 1,500 games each: points follow pace one for one, 0.41 per unit of shot logit, 0.08 per unit of foul scale).</summary>
    public double PointsScale => Pace * (1 + 0.41 * ShotLogitShift + 0.08 * (FoulScale - 1));

    /// <summary>"deliberate, defence first, physical" (or "balanced").</summary>
    public string Words
    {
        get
        {
            var w = new List<string>();
            if (Pace >= 1.03) w.Add("up-tempo"); else if (Pace <= 0.97) w.Add("deliberate");
            if (Defence >= 0.5) w.Add("defence first"); else if (Defence <= -0.4) w.Add("open, offence first");
            if (Physical >= 0.6) w.Add("physical"); else if (Physical <= -0.4) w.Add("finesse");
            return w.Count == 0 ? "balanced" : string.Join(", ", w);
        }
    }

    /// <summary>What a coach sees in conference games: "fewer possessions, lower scores, more whistles".</summary>
    public string Effect
    {
        get
        {
            var e = new List<string>();
            if (Pace >= 1.03) e.Add("more possessions"); else if (Pace <= 0.97) e.Add("fewer possessions");
            if (Defence >= 0.5) e.Add("lower scores"); else if (Defence <= -0.4) e.Add("higher scores");
            if (Physical >= 0.6) e.Add("more whistles"); else if (Physical <= -0.4) e.Add("fewer fouls");
            return string.Join(", ", e);
        }
    }

    /// <summary>The rules a game in this style plays under.</summary>
    public Rules Apply(Rules r) => this == Neutral ? r
        : r with { PaceScale = r.PaceScale * Pace, ShotLogitShift = r.ShotLogitShift + ShotLogitShift, FoulScale = r.FoulScale * FoulScale }; // K11: on top of the college profile

    // Keys are CollegeWorld's conference keys (the real ones carry RealColleges.KeyPrefix).
    private static readonly Dictionary<string, ConferenceStyle> Table = new()
    {
        // The fictional conferences (generated careers).
        ["aca"] = new(1.00, 0.0, 0.0), ["bmw"] = new(0.96, 0.5, 1.0), ["sac"] = new(1.03, 0.3, 0.4),
        ["pcu"] = new(1.02, -0.4, -0.4), ["htl"] = new(0.98, 1.0, 0.6), ["coa"] = new(1.04, -0.4, 0.0),
        ["fro"] = new(1.00, -0.2, 0.3), ["grc"] = new(0.97, 0.4, 0.6), ["mpc"] = new(0.97, 0.6, 0.2),
        ["lvc"] = new(1.01, -0.2, -0.4),
        // The real 2026-27 conferences (a real career's).
        ["r-acc"] = new(1.00, 0.0, 0.0), ["r-b1g"] = new(0.96, 0.5, 1.0), ["r-b12"] = new(0.99, 1.0, 0.8),
        ["r-sec"] = new(1.03, 0.3, 0.4), ["r-bige"] = new(0.98, 0.4, 0.8), ["r-pac"] = new(1.00, -0.3, -0.3),
        ["r-wcc"] = new(1.01, -0.3, -0.5), ["r-mwc"] = new(0.97, 0.6, 0.3), ["r-a10"] = new(0.99, 0.3, 0.3),
        ["r-aac"] = new(1.00, 0.4, 0.6), ["r-mvc"] = new(0.96, 0.6, 0.2), ["r-ivy"] = new(0.99, -0.4, -0.6),
        ["r-pat"] = new(0.96, 0.0, -0.4), ["r-swac"] = new(1.05, -0.5, 0.6), ["r-meac"] = new(1.04, -0.5, 0.5),
        ["r-summit"] = new(1.03, -0.6, -0.4), ["r-slc"] = new(1.03, -0.2, 0.3), ["r-sbc"] = new(1.01, 0.0, 0.2),
        ["r-cusa"] = new(1.01, 0.1, 0.2), ["r-mac"] = new(1.01, -0.1, 0.1), ["r-hl"] = new(1.01, -0.2, 0.0),
        ["r-caa"] = new(0.99, 0.0, 0.0), ["r-maac"] = new(1.01, -0.1, 0.3), ["r-nec"] = new(0.99, -0.2, 0.2),
        ["r-ovc"] = new(1.02, -0.3, 0.1), ["r-bsky"] = new(1.01, -0.4, -0.2), ["r-bsou"] = new(1.01, -0.2, 0.1),
        ["r-asun"] = new(1.03, -0.2, 0.0), ["r-ae"] = new(0.97, 0.1, 0.0), ["r-socon"] = new(1.01, -0.4, -0.3),
        ["r-bw"] = new(0.99, 0.2, 0.0), ["r-uac"] = new(1.02, -0.3, 0.1),
    };

    public static ConferenceStyle Of(string conferenceKey) => Table.GetValueOrDefault(conferenceKey, Neutral);
    public static IReadOnlyDictionary<string, ConferenceStyle> All => Table;
}

public sealed partial class CollegeWorld
{
    /// <summary>The conference whose style a game is played in: conference play and the conference tournament, when both
    /// programs are in it. Non-conference games, invitationals and March Madness play the plain college game.</summary>
    public string? StyleConference(CollegeGame g) =>
        g.Stage is "Conference" or "Conference tournament" && ConferenceOf.TryGetValue(g.HomeId, out var a)
        && ConferenceOf.TryGetValue(g.AwayId, out var b) && a == b ? a : null;

    public ConferenceStyle StyleOf(CollegeGame g) => StyleConference(g) is { } key ? ConferenceStyle.Of(key) : ConferenceStyle.Neutral;

    /// <summary>"Big Ten style: deliberate, defence first, physical (fewer possessions, lower scores, more whistles)".</summary>
    public static string StyleLine(string conferenceKey)
    {
        var st = ConferenceStyle.Of(conferenceKey);
        var conf = Conferences.FirstOrDefault(c => c.Key == conferenceKey);
        string name = conf?.Short ?? "Conference";
        return st.Effect.Length > 0 ? $"{name} style: {st.Words} ({st.Effect})" : $"{name} style: {st.Words}";
    }
}
