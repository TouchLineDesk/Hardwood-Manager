using Avalonia;

namespace Hardwood.Desktop.Controls;

/// <summary>
/// What the tracker shows at one moment of a play's motion (run 83): the ten markers, the ball, and how much of the
/// pass and the shot has been drawn so far. <see cref="Done"/> once the motion has reached the play's frame.
/// </summary>
public sealed record TrackerPose(IReadOnlyList<TrackerMarker> Markers, Point Ball, Point? PassFrom, Point? PassTo,
    Point? ShotFrom, Point? ShotTo, bool Done);

/// <summary>
/// Motion between two tracker frames (D67 B, the tracker's second slice, run 83). <b>Presentation only</b>, like the
/// spots themselves: the markers glide from where they were drawn to where the next play draws them, and the ball
/// travels the play's own path — to the passer on an assisted make, to the shooter, then to the rim. Nothing here
/// reads or changes the game; it is a pure function of the two frames and the time, so a paused or stepped game ends
/// on exactly the frame <see cref="TrackerLayout.Build"/> made.
/// </summary>
public static class TrackerMotion
{
    /// <summary>The share of the motion the markers take to reach their new spots (the ball keeps going after).</summary>
    public const double GlideShare = 0.55;

    /// <summary>The shot leg is a little slower than a carry or a pass, so the eye follows it to the rim.</summary>
    public const double ShotWeight = 1.4;

    /// <summary>How long the motion lasts for a speed's tick, in ms: most of the gap between ticks, never more than
    /// 650 ms, and none at all when ticks come too fast to follow (Fast).</summary>
    public static double DurationFor(double tickMs) => tickMs < 120 ? 0 : Math.Min(650, tickMs * 0.9);

    /// <summary>Smoothstep: starts and ends gently.</summary>
    public static double Ease(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

    /// <summary>The ball's path for a frame, starting where it was: the passer (if any), the shooter (if a shot), then
    /// where the play leaves it. Legs of no length are dropped.</summary>
    public static IReadOnlyList<Point> BallPath(Point start, TrackerFrame to)
    {
        var path = new List<Point> { start };
        void Add(Point p) { if (Dist(path[^1], p) > 0.01) path.Add(p); }
        if (to.Pass is { } pass) Add(pass);
        if (to.From is { } from) Add(from);
        Add(to.Ball);
        return path;
    }

    /// <summary>The frame itself, with the motion finished (no previous pose, or a game played too fast to follow).</summary>
    public static TrackerPose Still(TrackerFrame to) => At(null, to, 1);

    /// <summary>The pose at <paramref name="t"/> (0..1) of the motion from <paramref name="from"/> (what was on screen)
    /// to <paramref name="to"/>. A player who wasn't on the court before (a substitution) appears at his new spot.</summary>
    public static TrackerPose At(TrackerPose? from, TrackerFrame to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        if (from == null) t = 1;
        double glide = Ease(t / GlideShare);
        var markers = new List<TrackerMarker>(to.Markers.Count);
        foreach (var m in to.Markers)
        {
            var was = m.Key != 0 && from != null ? from.Markers.FirstOrDefault(o => o.Key == m.Key && o.Home == m.Home) : null;
            markers.Add(was == null || glide >= 1 ? m : m with { Spot = Lerp(was.Spot, m.Spot, glide) });
        }

        var path = BallPath(from?.Ball ?? to.Ball, to);
        // Leg weights: every carry or pass 1, the shot leg ShotWeight.
        int legs = path.Count - 1;
        int shotLeg = to.From is { } f0 ? IndexOf(path, f0) : -1;   // the leg that starts at the shooter
        double total = 0;
        for (int i = 0; i < legs; i++) total += i == shotLeg ? ShotWeight : 1;
        Point ball = path[^1];
        int leg = legs;          // the leg the ball is on (legs = arrived)
        double legT = 1;
        if (legs > 0 && t < 1)
        {
            double at = t * total, acc = 0;
            for (int i = 0; i < legs; i++)
            {
                double w = i == shotLeg ? ShotWeight : 1;
                if (at < acc + w) { leg = i; legT = Ease((at - acc) / w); ball = Lerp(path[i], path[i + 1], legT); break; }
                acc += w;
            }
        }

        // The pass line: from the passer to wherever the ball has got to, once the ball has left him.
        Point? passFrom = null, passTo = null;
        int passLeg = to.Pass is { } p0 ? IndexOf(path, p0) : -1;
        if (passLeg >= 0 && leg >= passLeg) { passFrom = path[passLeg]; passTo = leg == passLeg ? ball : path[passLeg + 1]; }
        // The shot's path: from the shooter to the ball, once the shot is up.
        Point? shotFrom = null, shotTo = null;
        if (shotLeg >= 0 && leg >= shotLeg) { shotFrom = path[shotLeg]; shotTo = leg == shotLeg ? ball : path[shotLeg + 1]; }

        return new TrackerPose(markers, ball, passFrom, passTo, shotFrom, shotTo, t >= 1);
    }

    private static int IndexOf(IReadOnlyList<Point> path, Point p)
    {
        // The last leg start at that point (a pass or shot can't start at the path's end).
        for (int i = path.Count - 2; i >= 0; i--) if (Dist(path[i], p) <= 0.01) return i;
        return -1;
    }

    private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
