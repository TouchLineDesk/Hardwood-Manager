using Avalonia;
using Avalonia.Media;
using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Desktop.Controls;

/// <summary>One player's marker on the tracker: where he stands (court feet), his side and a short label.
/// <see cref="Key"/> is the player's id, so a marker can glide from one frame to the next (run 83).</summary>
public sealed record TrackerMarker(Point Spot, bool Home, string Label, bool Actor, int Key = 0);

/// <summary>
/// One frame of the 2D tracker (D67 B, run 80): ten labelled markers, the ball, and what the play was. Built from the
/// engine's own play (<see cref="PlayEvent"/>) and the two fives on the floor.
/// <see cref="Pass"/> (run 83) is where the assister stood when a made shot was assisted: the ball goes to him, then to
/// the shooter, then to the rim.
/// </summary>
public sealed record TrackerFrame(IReadOnlyList<TrackerMarker> Markers, Point Ball, Point? From, string Caption, bool HomeOffense, PlayKind? Kind, bool Made, Point? Pass = null);

/// <summary>
/// Places the ten players for a play. <b>Presentation only:</b> the engine resolves possessions, not movement, so the
/// spots are a standard half-court set for the side with the ball, each defender between his man and the basket, and
/// the player who made the play moved to where it happened (the shot's zone, the glass, the line). Nothing here is read
/// back by the game, and it uses no random numbers from it (the shot's spot is the same stable spot as its dot: both are seeded by the play's index).
/// Home attacks the right-hand basket, as on the shot court.
/// </summary>
public static class TrackerLayout
{
    /// <summary>The five offensive spots, as (feet out from the basket's baseline, feet across), for positions PG..C.</summary>
    private static readonly (double Out, double Across)[] Set =
    {
        (27, 25),   // PG: top of the arc
        (20, 7),    // SG: right wing
        (3, 3.5),   // SF: corner
        (19, 42),   // PF: left wing
        (5, 32),    // C: low post
    };

    public const double BasketOut = 5.25;

    /// <summary>A spot in court feet for the side attacking the right basket (home) or the left (away).</summary>
    public static Point Spot(bool homeOffense, double outFromBaseline, double across)
        => new(homeOffense ? 94 - outFromBaseline : outFromBaseline, across);

    public static Point Basket(bool homeOffense) => Spot(homeOffense, BasketOut, 25);

    /// <summary>A short label for a marker: the first three letters of the surname, upper case.</summary>
    public static string Label(Player p)
    {
        var n = (p.LastName ?? p.Name ?? "?").Replace(" ", "").Replace(".", "").Replace("'", "");
        return (n.Length <= 3 ? n : n[..3]).ToUpperInvariant();
    }

    /// <summary>
    /// Build the frame for a play. <paramref name="seq"/> fixes the shot's spot inside its zone (the play's index in
    /// the game). With no play yet (tip-off), the home side is set up to attack.
    /// </summary>
    public static TrackerFrame Build(PlayEvent? play, int seq, IReadOnlyList<Player> homeFive, IReadOnlyList<Player> awayFive, string caption)
    {
        bool homeO = play?.HomeOffense ?? true;
        var offFive = Order(homeO ? homeFive : awayFive);
        var defFive = Order(homeO ? awayFive : homeFive);
        var basket = Basket(homeO);

        var off = new Point[offFive.Count];
        for (int i = 0; i < off.Length; i++) off[i] = Spot(homeO, Set[Math.Min(i, 4)].Out, Set[Math.Min(i, 4)].Across);

        Player? actor = play?.Actor, other = play?.Other;
        Point? from = null;
        Point ball = off.Length > 0 ? off[0] : basket;
        int Idx(IReadOnlyList<Player> five, Player? p) { if (p == null) return -1; for (int i = 0; i < five.Count; i++) if (five[i] == p) return i; return -1; }

        // The man with the ball (or the one the play is about) moves to where it happened.
        int ia = Idx(offFive, play?.HomeOffense == play?.Home ? actor : other);
        Point? focus = null;
        if (play != null)
        {
            switch (play.Kind)
            {
                case PlayKind.Shot:
                {
                    var spot = new LiveShot(homeO, play.Zone, play.Made, seq).Spot;
                    focus = spot; from = spot;
                    ball = play.Made ? basket : Mid(spot, basket, play.Blocked ? 0.2 : 0.85);
                    // The passer stands where he'd have found him: on the wing he started from.
                    break;
                }
                case PlayKind.FreeThrow:
                    focus = Spot(homeO, 19, 25);
                    ball = play.Made ? basket : Spot(homeO, 7, 25);
                    break;
                case PlayKind.OffensiveRebound:
                    focus = Spot(homeO, 7, 21); ball = focus.Value; break;
                case PlayKind.Turnover:
                case PlayKind.Steal:
                    focus = Spot(homeO, 30, 22); ball = focus.Value; break;
                case PlayKind.Foul:
                    focus = Spot(homeO, 14, 27); ball = focus.Value; break;
                case PlayKind.DefensiveRebound:
                    ball = Spot(homeO, 7, 28); break;
                case PlayKind.PeriodEnd:
                    ball = new Point(47, 25); break;
            }
        }
        var offKey = play?.HomeOffense == play?.Home ? actor : other;
        if (ia < 0 && offKey != null && focus is { } fx && offFive.Count > 0)
        {
            // He was subbed off at the dead ball that ended the possession: he made the play, so he is drawn in place of
            // whoever stands nearest the spot.
            int near = 0;
            for (int i = 1; i < off.Length; i++) if (Dist(off[i], fx) < Dist(off[near], fx)) near = i;
            offFive[near] = offKey; ia = near;
        }
        if (ia >= 0 && focus is { } f)
        {
            // Swap him with whoever stands nearest that spot so the set keeps its shape.
            int near = 0;
            for (int i = 1; i < off.Length; i++) if (Dist(off[i], f) < Dist(off[near], f)) near = i;
            (off[near], off[ia]) = (off[ia], off[near]);
            off[ia] = f;
        }
        if (play?.Kind == PlayKind.FreeThrow)
        {
            // Everyone else lines the lane or waits at the arc.
            var lane = new[] { Spot(homeO, 11, 15.2), Spot(homeO, 11, 34.8), Spot(homeO, 28, 9), Spot(homeO, 28, 41) };
            int k = 0;
            for (int i = 0; i < off.Length; i++) if (i != ia && k < lane.Length) off[i] = lane[k++];
        }

        var def = new Point[defFive.Count];
        for (int i = 0; i < def.Length; i++)
        {
            var man = i < off.Length ? off[i] : basket;
            double t = play?.Kind == PlayKind.FreeThrow ? 0.0 : 0.3;
            def[i] = play?.Kind == PlayKind.FreeThrow
                ? (i switch { 0 => Spot(homeO, 6.5, 15.2), 1 => Spot(homeO, 6.5, 34.8), 2 => Spot(homeO, 15.5, 15.2), 3 => Spot(homeO, 15.5, 34.8), _ => Spot(homeO, 27, 30) })
                : Mid(man, basket, t);
        }
        // A defender who made the play (steal, block, rebound, foul) is shown where it happened.
        var defKey = play?.HomeOffense == play?.Home ? other : actor;
        int id = Idx(defFive, defKey);
        if (id >= 0 && play != null)
        {
            def[id] = play.Kind switch
            {
                PlayKind.Steal => Mid(off[Math.Max(0, Idx(offFive, other))], basket, 0.12),
                PlayKind.DefensiveRebound => Spot(homeO, 7, 28),
                PlayKind.Shot when play.Blocked => Mid(from ?? basket, basket, 0.18),
                PlayKind.Foul => Mid(focus ?? basket, basket, 0.1),
                _ => def[id],
            };
            if (play.Kind is PlayKind.Steal) ball = def[id];
        }

        // An assisted make: the passer is drawn where he stands in the set, and the ball goes through him (run 83).
        Point? pass = null;
        if (play is { Kind: PlayKind.Shot, Made: true, Other: { } assister })
        {
            int ip = Idx(offFive, assister);
            if (ip >= 0 && ip != ia) pass = Clamp(off[ip]);
        }

        var markers = new List<TrackerMarker>(10);
        for (int i = 0; i < offFive.Count; i++)
            markers.Add(new TrackerMarker(Clamp(off[i]), homeO, Label(offFive[i]), offFive[i] == actor || offFive[i] == other, offFive[i].Id));
        for (int i = 0; i < defFive.Count; i++)
            markers.Add(new TrackerMarker(Clamp(def[i]), !homeO, Label(defFive[i]), defFive[i] == actor || defFive[i] == other, defFive[i].Id));
        return new TrackerFrame(markers, Clamp(ball), from, caption, homeO, play?.Kind, play?.Made ?? false, pass);
    }

    /// <summary>The five in set order: guards up top, bigs inside (by listed position, then the order on the floor).</summary>
    private static List<Player> Order(IReadOnlyList<Player> five) => five.Select((p, i) => (p, i)).OrderBy(x => x.p.PrimaryPosition).ThenBy(x => x.i).Select(x => x.p).ToList();

    private static Point Mid(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    private static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    private static Point Clamp(Point p) => new(Math.Clamp(p.X, 1.2, 92.8), Math.Clamp(p.Y, 1.2, 48.8));
}
