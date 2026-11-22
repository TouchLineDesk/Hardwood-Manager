using Hardwood.Core.Model;

namespace Hardwood.Core.Presentation;

/// <summary>The ten touchline shouts (M3 of the 2D match engine, `claude/match-engine-2d-design.md` C; Danny's answer 4,
/// #1027): one-tap moves of the tactic levers the engine already reads every possession. No new engine effect.</summary>
public enum Shout { PushPace, SlowDown, AttackPaint, HuntThrees, CrashGlass, GetBack, Pressure, PackPaint, FeedStar, DoubleStar }

/// <summary>What one shout does, and the words for it.</summary>
/// <param name="Label">The button.</param>
/// <param name="Tip">The tooltip: what it changes, by how much.</param>
/// <param name="Says">The commentary line, after the team's name ("Duke" + " push the pace").</param>
/// <param name="Opposite">The shout it cancels (Push the pace and Slow it down can't both be on), or null.</param>
public sealed record ShoutInfo(Shout Shout, string Label, string Tip, string Says, Shout? Opposite);

/// <summary>
/// A shout that is on: the lever it moved and by how much it actually moved (after the lever's limits), so cancelling
/// puts back exactly that much, even if the slider was moved by hand in between.
/// </summary>
public sealed record ActiveShout(Shout Shout, double Moved, bool? WasDoubling);

/// <summary>The shouts' rules (pure: they move a <see cref="Tactic"/>'s levers and put them back).</summary>
public static class Shouts
{
    /// <summary>How far each shout moves its lever.</summary>
    public const double PaceStep = 6;   // possessions per 48, on the 88–110 slider
    public const int ThreeStep = 20, GlassStep = 25, AggressionStep = 20, StarStep = 20;
    public const double PaceMin = 88, PaceMax = 110;

    public static readonly IReadOnlyList<ShoutInfo> All = new[]
    {
        new ShoutInfo(Shout.PushPace, "Push the pace", $"Pace +{PaceStep:0} (possessions per 48). More fast breaks, more possessions.", "push the pace", Shout.SlowDown),
        new ShoutInfo(Shout.SlowDown, "Slow it down", $"Pace −{PaceStep:0}. Walk it up and use the clock.", "slow it down and work the clock", Shout.PushPace),
        new ShoutInfo(Shout.AttackPaint, "Attack the paint", $"Three-point emphasis −{ThreeStep}. Drives and post touches; the shooters come in closer.", "attack the paint", Shout.HuntThrees),
        new ShoutInfo(Shout.HuntThrees, "Hunt threes", $"Three-point emphasis +{ThreeStep}. Four out, the shooters spread to the arc.", "spread the floor and hunt threes", Shout.AttackPaint),
        new ShoutInfo(Shout.CrashGlass, "Crash the glass", $"Offensive glass +{GlassStep}. More bodies to the rim on a miss, fewer back on defence.", "send everyone to the offensive glass", Shout.GetBack),
        new ShoutInfo(Shout.GetBack, "Get back", $"Offensive glass −{GlassStep}. Sprint back after a shot instead of crashing.", "get back on defence after every shot", Shout.CrashGlass),
        new ShoutInfo(Shout.Pressure, "Pressure the ball", $"Defensive aggression +{AggressionStep}. Tighter on the ball, gambling for steals (more fouls too).", "turn up the pressure on the ball", Shout.PackPaint),
        new ShoutInfo(Shout.PackPaint, "Pack the paint", $"Defensive aggression −{AggressionStep}. Sag off and protect the rim; fewer fouls, more open jumpers.", "sag off and pack the paint", Shout.Pressure),
        new ShoutInfo(Shout.FeedStar, "Feed the star", $"Star usage +{StarStep}. Run more of the offence through your best scorers.", "run everything through their star", null),
        new ShoutInfo(Shout.DoubleStar, "Double their star", "Double their best scorer: he shoots less and worse, his teammates get open looks.", "send a double at the other team's best scorer", null),
    };

    public static ShoutInfo Info(Shout s) => All[(int)s];

    /// <summary>Turn a shout on: move its lever (within its limits) and say how far it moved.</summary>
    public static ActiveShout Apply(Tactic t, Shout s)
    {
        switch (s)
        {
            case Shout.PushPace: case Shout.SlowDown:
            {
                double before = t.Pace;
                t.Pace = Math.Clamp(t.Pace + (s == Shout.PushPace ? PaceStep : -PaceStep), PaceMin, PaceMax);
                return new ActiveShout(s, t.Pace - before, null);
            }
            case Shout.AttackPaint: case Shout.HuntThrees:
            {
                int before = t.ThreePointEmphasis;
                t.ThreePointEmphasis = Math.Clamp(t.ThreePointEmphasis + (s == Shout.HuntThrees ? ThreeStep : -ThreeStep), 0, 100);
                return new ActiveShout(s, t.ThreePointEmphasis - before, null);
            }
            case Shout.CrashGlass: case Shout.GetBack:
            {
                int before = t.CrashGlass;
                t.CrashGlass = Math.Clamp(t.CrashGlass + (s == Shout.CrashGlass ? GlassStep : -GlassStep), 0, 100);
                return new ActiveShout(s, t.CrashGlass - before, null);
            }
            case Shout.Pressure: case Shout.PackPaint:
            {
                int before = t.DefensiveAggression;
                t.DefensiveAggression = Math.Clamp(t.DefensiveAggression + (s == Shout.Pressure ? AggressionStep : -AggressionStep), 0, 100);
                return new ActiveShout(s, t.DefensiveAggression - before, null);
            }
            case Shout.FeedStar:
            {
                int before = t.StarUsage;
                t.StarUsage = Math.Clamp(t.StarUsage + StarStep, 0, 100);
                return new ActiveShout(s, t.StarUsage - before, null);
            }
            default:
            {
                bool was = t.DoubleStar;
                t.DoubleStar = true;
                return new ActiveShout(s, 0, was);
            }
        }
    }

    /// <summary>Turn a shout off: put back what it moved (the lever stays within its limits).</summary>
    public static void Cancel(Tactic t, ActiveShout a)
    {
        switch (a.Shout)
        {
            case Shout.PushPace: case Shout.SlowDown: t.Pace = Math.Clamp(t.Pace - a.Moved, PaceMin, PaceMax); break;
            case Shout.AttackPaint: case Shout.HuntThrees: t.ThreePointEmphasis = Math.Clamp(t.ThreePointEmphasis - (int)a.Moved, 0, 100); break;
            case Shout.CrashGlass: case Shout.GetBack: t.CrashGlass = Math.Clamp(t.CrashGlass - (int)a.Moved, 0, 100); break;
            case Shout.Pressure: case Shout.PackPaint: t.DefensiveAggression = Math.Clamp(t.DefensiveAggression - (int)a.Moved, 0, 100); break;
            case Shout.FeedStar: t.StarUsage = Math.Clamp(t.StarUsage - (int)a.Moved, 0, 100); break;
            default: t.DoubleStar = a.WasDoubling ?? false; break;
        }
    }

    /// <summary>
    /// Toggle a shout in a set of active ones: on (cancelling its opposite first), or off if it was on. Returns true if
    /// it is now on.
    /// </summary>
    public static bool Toggle(Tactic t, List<ActiveShout> active, Shout s)
    {
        var on = active.FirstOrDefault(a => a.Shout == s);
        if (on != null) { Cancel(t, on); active.Remove(on); return false; }
        if (Info(s).Opposite is { } opp && active.FirstOrDefault(a => a.Shout == opp) is { } o) { Cancel(t, o); active.Remove(o); }
        active.Add(Apply(t, s));
        return true;
    }

    /// <summary>Cancel every active shout, newest first (at the final: shouts are for this game only).</summary>
    public static void ClearAll(Tactic t, List<ActiveShout> active)
    {
        for (int i = active.Count - 1; i >= 0; i--) Cancel(t, active[i]);
        active.Clear();
    }
}

/// <summary>A team's tactics at one moment, to say on the commentary bar what changed since (M3).</summary>
public sealed record TacticSnapshot(OffensiveSystem System, DefensiveScheme Scheme, double Pace, int Threes, int Glass, int Aggression, int Star, bool Double)
{
    public static TacticSnapshot Of(Tactic t) => new(t.System, t.Scheme, t.Pace, t.ThreePointEmphasis, t.CrashGlass, t.DefensiveAggression, t.StarUsage, t.DoubleStar);

    /// <summary>
    /// The changes from <paramref name="before"/> to now, as commentary phrases after the team's name ("switch to a 2-3
    /// zone", "push the pace"). Shouts turned on in between are named as shouts, and their levers aren't named twice.
    /// Small slider nudges (under 3 pace or 10 points) aren't called out.
    /// </summary>
    public List<string> ChangesFrom(TacticSnapshot before, IEnumerable<Shout> newShouts)
    {
        var said = new List<string>();
        var shouts = newShouts.ToHashSet();
        if (System != before.System) said.Add(OffenceWords(System));
        if (Scheme != before.Scheme) said.Add($"switch to {SchemeWords(Scheme)}");
        foreach (var s in shouts.OrderBy(s => (int)s)) said.Add(Shouts.Info(s).Says);
        bool Lever(params Shout[] ss) => ss.Any(shouts.Contains);
        if (!Lever(Shout.PushPace, Shout.SlowDown) && Math.Abs(Pace - before.Pace) >= 3) said.Add(Pace > before.Pace ? "pick up the pace" : "take some air out of the ball");
        if (!Lever(Shout.HuntThrees, Shout.AttackPaint) && Math.Abs(Threes - before.Threes) >= 10) said.Add(Threes > before.Threes ? "look for more threes" : "look to get into the paint");
        if (!Lever(Shout.CrashGlass, Shout.GetBack) && Math.Abs(Glass - before.Glass) >= 10) said.Add(Glass > before.Glass ? "send more to the offensive glass" : "get back after their shots");
        if (!Lever(Shout.Pressure, Shout.PackPaint) && Math.Abs(Aggression - before.Aggression) >= 10) said.Add(Aggression > before.Aggression ? "pressure the ball harder" : "sit back on defence");
        if (!Lever(Shout.FeedStar) && Math.Abs(Star - before.Star) >= 10) said.Add(Star > before.Star ? "go to their best scorers more" : "spread the ball around");
        if (!Lever(Shout.DoubleStar) && Double != before.Double) said.Add(Double ? "send a double at the other team's best scorer" : "stop doubling the other team's best scorer");
        return said;
    }

    public static string OffenceWords(OffensiveSystem s) => s switch
    {
        OffensiveSystem.Balanced => "go back to a balanced offence",
        OffensiveSystem.PickAndRoll => "go to the pick-and-roll",
        OffensiveSystem.Motion => "switch to motion",
        OffensiveSystem.PostCentric => "start going inside to the post",
        OffensiveSystem.PaceAndSpace => "switch to pace and space",
        _ => "clear out for isolations",
    };
    public static string SchemeWords(DefensiveScheme s) => s switch
    {
        DefensiveScheme.Man => "man-to-man",
        DefensiveScheme.SwitchEverything => "switching everything",
        DefensiveScheme.Zone23 => "a 2-3 zone",
        _ => "drop coverage",
    };

    /// <summary>One commentary line for a team's changes ("Duke switch to a 2-3 zone and push the pace"), or null.</summary>
    public static string? Line(string team, IReadOnlyList<string> changes) => changes.Count switch
    {
        0 => null,
        1 => $"{team} {changes[0]}",
        _ => $"{team} {string.Join(", ", changes.Take(changes.Count - 1))} and {changes[^1]}",
    };
}
