using Hardwood.Core.Engine;
using Hardwood.Core.Model;

namespace Hardwood.Core.Presentation;

/// <summary>A spot on the floor in feet: x 0..94 baseline to baseline (home attacks the right basket), y 0..50 sideline to sideline.</summary>
public readonly record struct Spot(double X, double Y)
{
    public static Spot Lerp(Spot a, Spot b, double u) => new(a.X + (b.X - a.X) * u, a.Y + (b.Y - a.Y) * u);
    public double DistanceTo(Spot o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
    public Spot Clamp() => new(Math.Clamp(X, 0.8, 93.2), Math.Clamp(Y, 0.8, 49.2));
}

/// <summary>How the ball moves in one stretch of a possession.</summary>
public enum BallMode { Held, Pass, Shot, Loose }

/// <summary>One stretch of the ball's path. Holder and To are slots 0–9 (home 0–4, away 5–9), or −1.</summary>
public sealed record BallSegment(double T0, double T1, BallMode Mode, int Holder, int To, Spot From, Spot Dest, double Arc, bool FreeThrow = false);

/// <summary>A line for the commentary bar, from <see cref="T"/> seconds into the possession.</summary>
public sealed record CommentaryLine(double T, string Text, bool Big);

/// <summary>A flash on the floor: a made basket (gold ring at the rim) or a block (white ring).</summary>
public sealed record Burst(double T, Spot At, bool Made);

/// <summary>What the match view draws at one moment.</summary>
public sealed record ChoreoFrame(Spot[] Players, double[] Headings, bool[] Moving, Spot Ball, double BallZ, int Holder,
    double Clock, int HomeScore, int AwayScore, string Line, bool LineBig, Burst? Burst, double BurstAge, bool HomeOffense, Player[] Who);

/// <summary>Everything the choreographer needs for one possession. Presentation only: it is built from what the engine
/// already decided and read-only state, so nothing here can change a result.</summary>
/// <param name="Index">The possession's number in the game (it seeds the choreography's own random draws).</param>
/// <param name="Seed">The game's seed.</param>
/// <param name="Events">The engine's plays for this possession, in order.</param>
/// <param name="StartClock">The game clock when the possession began (seconds left in the period).</param>
/// <param name="Home">The home five on the floor (after the engine's subs).</param>
/// <param name="Away">The away five.</param>
/// <param name="Start">Where each player stood when the last possession ended (players not in it walk on from the
/// scorer's table), or null for the tip.</param>
public sealed record PossessionInput(int Index, ulong Seed, IReadOnlyList<PlayEvent> Events, double StartClock, double EndClock,
    IReadOnlyList<Player> Home, IReadOnlyList<Player> Away, IReadOnlyDictionary<Player, Spot>? Start,
    int StartHomeScore, int StartAwayScore, bool HomeBallIfNoEvents, string HomeName, string AwayName,
    DefensiveScheme HomeScheme, DefensiveScheme AwayScheme, PossessionStart How, int Periods, bool LateAndClose);

/// <summary>How the possession began: from the tip or a period's start, after a make (an inbound under the basket),
/// after a defensive rebound or a steal (a push), or after a dead ball.</summary>
public enum PossessionStart { PeriodStart, AfterMake, AfterStop, DeadBall }

/// <summary>
/// One possession played out over its real game-clock time (M1 of the 2D match engine, `claude/match-engine-2d-design.md`):
/// ten tracks of keyframes, the ball's path, the clock, the score and the commentary. Pure: no UI types, and its random
/// draws come from its own generator seeded by the game seed and the possession's index, never the engine's.
/// </summary>
public sealed class Choreography
{
    public required PossessionInput Input { get; init; }
    /// <summary>Home 0–4, away 5–9, as drawn (an engine actor missing from a five is swapped into it).</summary>
    public required Player[] Players { get; init; }
    public List<(double T, Spot P)>[] Tracks { get; } = Enumerable.Range(0, 10).Select(_ => new List<(double, Spot)>()).ToArray();
    public List<BallSegment> Ball { get; } = new();
    public List<CommentaryLine> Lines { get; } = new();
    public List<(double T, int Home, int Away)> Scores { get; } = new();
    public List<(double T, double Clock)> ClockPoints { get; } = new();
    public List<Burst> Bursts { get; } = new();
    /// <summary>The possession's length on screen at 1× (its game-clock time plus the dead-ball moments).</summary>
    public double Length { get; set; }
    /// <summary>A possession worth watching at Key moments speed: a make, a block, a steal, or a late, close game.</summary>
    public bool Key { get; set; }
    public bool HomeOffense { get; set; }
    /// <summary>How the next possession starts.</summary>
    public PossessionStart Next { get; set; } = PossessionStart.DeadBall;

    public Spot PositionOf(int slot, double t)
    {
        var k = Tracks[slot];
        if (k.Count == 0) return new Spot(47, 25);
        if (t <= k[0].T) return k[0].P;
        for (int i = 1; i < k.Count; i++)
        {
            if (t > k[i].T) continue;
            double span = k[i].T - k[i - 1].T;
            double u = span <= 1e-9 ? 1 : (t - k[i - 1].T) / span;
            return Spot.Lerp(k[i - 1].P, k[i].P, Ease(u));
        }
        return k[^1].P;
    }

    private static double Ease(double u) => u < 0.5 ? 2 * u * u : 1 - Math.Pow(-2 * u + 2, 2) / 2;

    /// <summary>Where everyone stands when the possession ends (the next one starts from here).</summary>
    public Dictionary<Player, Spot> FinalSpots()
    {
        var d = new Dictionary<Player, Spot>();
        for (int i = 0; i < 10; i++) d[Players[i]] = PositionOf(i, Length);
        return d;
    }

    public double ClockAt(double t)
    {
        var c = ClockPoints;
        if (c.Count == 0) return Input.StartClock;
        if (t <= c[0].T) return c[0].Clock;
        for (int i = 1; i < c.Count; i++)
        {
            if (t > c[i].T) continue;
            double span = c[i].T - c[i - 1].T;
            double u = span <= 1e-9 ? 1 : (t - c[i - 1].T) / span;
            return c[i - 1].Clock + (c[i].Clock - c[i - 1].Clock) * u;
        }
        return c[^1].Clock;
    }

    public ChoreoFrame Sample(double t)
    {
        t = Math.Clamp(t, 0, Length);
        var pos = new Spot[10]; var head = new double[10]; var moving = new bool[10];
        for (int i = 0; i < 10; i++)
        {
            pos[i] = PositionOf(i, t);
            var a = PositionOf(i, Math.Max(0, t - 0.08)); var b = PositionOf(i, Math.Min(Length, t + 0.08));
            double dx = b.X - a.X, dy = b.Y - a.Y;
            moving[i] = dx * dx + dy * dy > 0.0004;
            // Standing players face the ball's end of the floor.
            head[i] = moving[i] ? Math.Atan2(dy, dx) : (HomeOffense == (i < 5) ? (HomeOffense ? 0 : Math.PI) : (HomeOffense ? 0 : Math.PI));
        }
        var (ball, z, holder) = BallAt(t, pos);
        int hs = Input.StartHomeScore, aws = Input.StartAwayScore;
        foreach (var s in Scores) if (s.T <= t) { hs = s.Home; aws = s.Away; }
        string line = ""; bool big = false;
        foreach (var l in Lines) if (l.T <= t) { line = l.Text; big = l.Big; }
        Burst? burst = null; double age = 0;
        foreach (var bu in Bursts) if (bu.T <= t && t - bu.T < 0.7) { burst = bu; age = (t - bu.T) / 0.7; }
        return new ChoreoFrame(pos, head, moving, ball, z, holder, ClockAt(t), hs, aws, line, big, burst, age, HomeOffense, Players);
    }

    private (Spot, double, int) BallAt(double t, Spot[] pos)
    {
        BallSegment? seg = null;
        foreach (var s in Ball) { if (t >= s.T0 && t < s.T1) { seg = s; break; } }
        if (seg == null)
        {
            var last = Ball.Count > 0 ? Ball[^1] : null;
            if (last == null) return (new Spot(47, 25), 0, -1);
            if (t < last.T0) seg = Ball[0];
            else return last.Mode switch
            {
                BallMode.Held => (Hand(pos[last.Holder]), Bounce(t), last.Holder),
                BallMode.Pass when last.To >= 0 => (Hand(pos[last.To]), 0.3, last.To),
                _ => (last.Dest, 0, -1),
            };
        }
        double span = seg.T1 - seg.T0, u = span <= 1e-9 ? 1 : (t - seg.T0) / span;
        switch (seg.Mode)
        {
            case BallMode.Held: return (Hand(pos[seg.Holder]), Bounce(t), seg.Holder);
            case BallMode.Pass:
            {
                var from = seg.Holder >= 0 ? Hand(PositionOf(seg.Holder, seg.T0)) : seg.From;
                var to = seg.To >= 0 ? Hand(pos[seg.To]) : seg.Dest;
                return (Spot.Lerp(from, to, u), 0.5 + Math.Sin(Math.PI * u) * seg.Arc, -1);
            }
            case BallMode.Shot:
            {
                var from = seg.Holder >= 0 ? Hand(PositionOf(seg.Holder, seg.T0)) : seg.From;
                return (Spot.Lerp(from, seg.Dest, u), 1 + Math.Sin(Math.PI * u) * seg.Arc, -1);
            }
            default:
                return (Spot.Lerp(seg.From, seg.Dest, u), Math.Abs(Math.Sin(u * Math.PI * 2)) * seg.Arc, -1);
        }
    }

    private static Spot Hand(Spot p) => new(p.X + 0.9, p.Y + 0.4);
    private static double Bounce(double t) => (Math.Sin(t * 14) + 1) / 4;
}

/// <summary>
/// Turns one possession's engine plays into the moving picture (M1: walk it up, a basic set with a screen and a pass or
/// two, then the engine's play, the rebound or the free throws, and the next side's start). It never decides anything:
/// the shooter, zone, make, assister, blocker, stealer, fouler and rebounder all come from the events.
/// </summary>
public static class PossessionChoreographer
{
    public const double RimRight = 88.75, RimLeft = 5.25;
    /// <summary>Where substitutes walk on from: the scorer's table at midcourt.</summary>
    public static readonly Spot ScorersTable = new(47, 50.5);

    // Dead-ball and flight times on screen (seconds at 1×; the game clock is stopped for them).
    public const double ShotFlight = 1.0, ReboundTime = 0.8, FreeThrowTime = 1.9, LineUpTime = 1.4, FoulTime = 1.0, TurnoverTime = 1.0,
        PeriodEndTime = 1.6, Epilogue = 0.8;

    public static Choreography Build(PossessionInput input)
    {
        var rng = new Random(unchecked((int)(Mix(input.Seed) ^ (ulong)(input.Index * 0x9E3779B1L))));
        var ev = input.Events;
        var firstBall = ev.FirstOrDefault(e => e.Kind != PlayKind.PeriodEnd);
        bool homeOff = firstBall?.HomeOffense ?? input.HomeBallIfNoEvents;

        // The two fives, ordered PG..C so slot k plays spot k; an engine actor not in them is swapped in (presentation only).
        var home = Order(input.Home); var away = Order(input.Away);
        var actors = ev.SelectMany(e => new[] { e.Actor, e.Other }).OfType<Player>().ToHashSet();
        foreach (var e in ev)
        {
            Place(e.Actor, e.Home ? home : away, actors);
            if (e.Other != null) Place(e.Other, e.Kind is PlayKind.Shot && e.Made ? (e.Home ? home : away) : e.Kind is PlayKind.Shot or PlayKind.Steal or PlayKind.Foul ? (e.Home ? away : home) : (e.Home ? home : away), actors);
        }
        var players = home.Concat(away).ToArray();
        var c = new Choreography { Input = input, Players = players, HomeOffense = homeOff };
        int Slot(Player? p) => p == null ? -1 : Array.IndexOf(players, p);
        int offBase = homeOff ? 0 : 5, defBase = homeOff ? 5 : 0;
        double bx = homeOff ? RimRight : RimLeft;
        double o = bx > 47 ? -1 : 1;   // from the basket out towards midcourt
        Spot S(double outFt, double y) => new Spot(bx + o * outFt, y).Clamp();
        var rim = new Spot(bx, 25);

        // Start: where they stood, or the tip.
        var cur = new Spot[10];
        for (int i = 0; i < 10; i++)
        {
            if (input.Start != null && input.Start.TryGetValue(players[i], out var at)) cur[i] = at;
            else if (input.Start != null) cur[i] = ScorersTable;   // a sub walks on
            else cur[i] = TipSpot(i, homeOff);
            c.Tracks[i].Add((0, cur[i]));
        }
        // Keyframing: hold where you are until t0, then move to `to` by t1.
        void Go(int slot, double t0, double t1, Spot to)
        {
            var k = c.Tracks[slot];
            if (t0 > k[^1].T) k.Add((t0, k[^1].P));
            k.Add((Math.Max(t1, k[^1].T + 0.05), to.Clamp()));
            cur[slot] = to.Clamp();
        }

        // The offence's half-court spots by slot (PG, SG, SF, PF, C) and the defence's.
        Spot[] OffSpots() => new[] { S(23, 25 + J(rng, 3)), S(19, 7 + J(rng, 2)), S(19, 43 + J(rng, 2)), S(13, 18 + J(rng, 2)), S(4, 32 + J(rng, 2)) };
        var scheme = homeOff ? input.AwayScheme : input.HomeScheme;
        Spot DefSpot(int k, Spot man, Spot ball)
        {
            if (scheme == DefensiveScheme.Zone23)
            {
                var zone = new[] { S(15, 18), S(15, 32), S(6, 9), S(6, 41), S(4, 25) };
                double shift = (ball.Y - 25) * 0.3;
                return new Spot(zone[k].X, zone[k].Y + shift).Clamp();
            }
            // Man: between his man and the basket, sagging off the ball.
            double sag = man.DistanceTo(ball) < 3 ? 0.18 : 0.35;
            return Spot.Lerp(man, rim, sag).Clamp();
        }

        double D = Math.Max(0, input.StartClock - (firstBall?.Clock ?? input.EndClock));
        if (ev.Count == 0) D = Math.Max(0, input.StartClock - input.EndClock);
        c.ClockPoints.Add((0, input.StartClock));
        double t = 0;
        string offName = homeOff ? input.HomeName : input.AwayName;
        int handler = offBase;   // the point guard brings it up

        // 1. The start: an inbound after a make, a push after a stop, or a walk up.
        if (firstBall != null)
        {
            if (input.How == PossessionStart.AfterMake && D > 3)
            {
                int inb = offBase + 4;
                // The inbounder steps out under the basket the other side just scored on.
                var inbAt = new Spot(homeOff ? 1.0 : 93.0, 22);
                Go(inb, 0, 0.6, inbAt);
                Go(handler, 0, 0.7, new Spot(homeOff ? 8 : 86, 18));
                c.Ball.Add(new BallSegment(0, 0.6, BallMode.Loose, -1, -1, new Spot(homeOff ? 5.25 : 88.75, 25), inbAt, 0.4));
                c.Ball.Add(new BallSegment(0.6, 1.2, BallMode.Pass, inb, handler, inbAt, default, 0.2));
                t = 1.2;
            }
            c.Lines.Add(new(0, input.How switch
            {
                PossessionStart.AfterStop => $"{Last(players[handler])} pushes it up the floor for {offName}",
                PossessionStart.PeriodStart => $"{offName} have the ball to start the period",
                _ => $"{Last(players[handler])} brings it up for {offName}",
            }, false));
        }

        // How the build-up is split: bring it up, then the set (a screen and a pass or two), then the play.
        double walk = input.How == PossessionStart.AfterStop ? Math.Min(4, D * 0.3) : Math.Min(7, D * 0.4);
        walk = Math.Max(walk, t);
        var spots = OffSpots();
        if (firstBall != null && D > 0.5)
        {
            double bring = Math.Max(walk, t + 0.5);
            for (int k = 0; k < 5; k++)
            {
                var to = k == 0 ? S(26, 25) : spots[k];
                Go(offBase + k, t, bring, to);
            }
            // Ball with the handler from wherever it was.
            if (c.Ball.Count == 0) c.Ball.Add(new BallSegment(0, bring, BallMode.Held, handler, -1, default, default, 0));
            else c.Ball.Add(new BallSegment(t, bring, BallMode.Held, handler, -1, default, default, 0));
            for (int k = 0; k < 5; k++) Go(defBase + k, t, bring + 0.3, DefSpot(k, cur[offBase + k], cur[handler]));
            t = bring;
        }
        else if (c.Ball.Count == 0)
            c.Ball.Add(new BallSegment(0, 0.01, BallMode.Held, handler, -1, default, default, 0));

        // 2. The set and the play, event by event.
        double clockNow = input.StartClock;
        bool firstChance = true;
        bool inFreeThrows = false;
        for (int i = 0; i < ev.Count; i++)
        {
            var e = ev[i];
            double evClock = e.Clock;
            // Game time to play before this event (the build-up, or a second chance after an offensive board).
            double run = Math.Max(0, clockNow - evClock);
            if (e.Kind is PlayKind.Shot or PlayKind.Turnover or PlayKind.Steal or (PlayKind.Foul) && !inFreeThrows)
            {
                double setEnd = firstChance ? Math.Max(t, D + ExtraBefore(c)) : t + run;
                double setStart = t;
                int target = e.Kind switch
                {
                    PlayKind.Shot => Slot(e.Actor),
                    PlayKind.Turnover => Slot(e.Actor),
                    PlayKind.Steal => Slot(e.Other),
                    _ => Slot(e.Other) >= 0 ? Slot(e.Other) : handler,
                };
                if (target < 0 || target / 5 != offBase / 5) target = handler;
                int assister = e.Kind == PlayKind.Shot && e.Made && e.Other != null ? Slot(e.Other) : -1;
                // The set: a screen for the ball handler, and passes round to the shooter (the assister last).
                var chain = new List<int>();
                int from = c.Ball.Count > 0 && c.Ball[^1].Mode == BallMode.Held ? c.Ball[^1].Holder : handler;
                if (from < 0 || from / 5 != offBase / 5) from = handler;
                double span = setEnd - setStart;
                int extra = span > 9 ? 2 : span > 4 ? 1 : 0;
                for (int x = 0; x < extra; x++)
                {
                    int pick = offBase + rng.Next(5);
                    if (pick != from && pick != target && pick != assister && (chain.Count == 0 || chain[^1] != pick)) chain.Add(pick);
                }
                if (assister >= 0 && assister != from && assister != target) chain.Add(assister);
                if ((chain.Count == 0 ? from : chain[^1]) != target) chain.Add(target);
                // A high screen from the big early in the set.
                if (span > 5 && firstChance)
                {
                    int big = offBase + 4;
                    if (big != from)
                    {
                        var screen = new Spot(cur[from].X - o * 1.5, cur[from].Y + (cur[from].Y < 25 ? 2.5 : -2.5));
                        Go(big, setStart, setStart + 1.5, screen);
                        Go(from, setStart + 1.6, setStart + 3.2, new Spot(cur[from].X + o * 6, cur[from].Y + (cur[from].Y < 25 ? 5 : -5)));
                        Go(big, setStart + 2.0, setStart + 3.4, S(5, cur[big].Y < 25 ? 20 : 30));
                        c.Lines.Add(new(setStart + 0.2, $"{Last(players[big])} comes up to set the screen for {Last(players[from])}", false));
                        c.Lines.Add(new(setStart + 1.7, $"{Last(players[from])} comes off the screen", false));
                    }
                }
                // Shot spot for the shooter, so he gets there as the ball does.
                Spot shotSpot = e.Kind == PlayKind.Shot ? ShotSpot(e.Zone, bx, rng) : cur[target];
                double passTime = 0.55;
                double firstPass = setStart + Math.Max(0.5, span - chain.Count * 1.6 - 0.4);
                double tt = firstPass;
                int holder = from;
                for (int x = 0; x < chain.Count; x++)
                {
                    int to = chain[x];
                    bool last = x == chain.Count - 1;
                    if (last && e.Kind == PlayKind.Shot && assister >= 0) Go(to, tt - 1.0, tt + passTime, shotSpot);
                    else Go(to, tt - 0.8, tt + passTime, Spot.Lerp(cur[to], cur[holder], 0.15));
                    c.Ball.Add(new BallSegment(LastEnd(c), tt, BallMode.Held, holder, -1, default, default, 0));
                    if (last && e.Kind == PlayKind.Steal)
                    {
                        // Picked off: the stealer jumps the lane.
                        int st = Slot(e.Actor);
                        var mid = Spot.Lerp(cur[holder], cur[to], 0.55);
                        if (st >= 0) Go(st, tt - 0.5, tt + passTime * 0.55, mid);
                        c.Ball.Add(new BallSegment(tt, tt + passTime * 0.55, BallMode.Pass, holder, st, cur[holder], mid, 0.1));
                        tt += passTime * 0.55;
                        holder = st >= 0 ? st : holder;
                        break;
                    }
                    c.Ball.Add(new BallSegment(tt, tt + passTime, BallMode.Pass, holder, to, default, default, 0.25));
                    c.Lines.Add(new(tt, last && assister >= 0 && to == target
                        ? $"{Last(players[holder])} finds {Last(players[to])} {Where(e.Zone)}"
                        : $"{Last(players[holder])} swings it to {Last(players[to])}", false));
                    holder = to;
                    tt += passTime + 0.9;
                }
                double caught = chain.Count > 0 ? tt - 0.9 : setStart;
                double playAt = Math.Max(setEnd, caught + (e.Kind == PlayKind.Shot && assister < 0 ? 1.4 : 0.01));
                if (e.Kind == PlayKind.Shot && assister < 0)
                {
                    // His own shot: he takes it there off the dribble.
                    double go = Math.Max(caught, playAt - 2.2);
                    Go(target, go, playAt - 0.1, shotSpot);
                    if (e.Zone == ShotZone.Rim) c.Lines.Add(new(go, $"{Last(players[target])} drives to the basket", false));
                }
                // The defence follows its men and the ball through the set.
                for (int k = 0; k < 5; k++) Go(defBase + k, setStart + 0.5, Math.Max(setStart + 0.6, playAt - 0.2), DefSpot(k, cur[offBase + k], cur[target]));
                if (c.Ball.Count > 0 && c.Ball[^1].Mode != BallMode.Held || LastEnd(c) < playAt)
                    c.Ball.Add(new BallSegment(LastEnd(c), playAt, BallMode.Held, holder, -1, default, default, 0));
                // The clock runs down to the play.
                c.ClockPoints.Add((playAt, evClock));
                clockNow = evClock;
                t = playAt;
                firstChance = false;

                switch (e.Kind)
                {
                    case PlayKind.Shot:
                    {
                        bool three = e.Zone is ShotZone.CornerThree or ShotZone.AboveBreakThree;
                        c.Lines.Add(new(t, three ? $"{Last(e.Actor)} rises for three..." : e.Zone == ShotZone.Rim ? $"{Last(e.Actor)} goes up at the rim..." : $"{Last(e.Actor)} pulls up from {(e.Zone == ShotZone.LongMid ? "the elbow" : "short range")}...", false));
                        if (e.Blocked)
                        {
                            int bl = Slot(e.Other);
                            if (bl >= 0) Go(bl, t - 0.8, t + 0.2, Spot.Lerp(cur[target], rim, 0.15));
                            var deflect = Spot.Lerp(cur[target], rim, 0.3) with { Y = cur[target].Y + J(rng, 6) };
                            c.Ball.Add(new BallSegment(t, t + 0.3, BallMode.Shot, target, -1, default, Spot.Lerp(cur[target], rim, 0.15), 1.5));
                            c.Ball.Add(new BallSegment(t + 0.3, t + 0.9, BallMode.Loose, -1, -1, Spot.Lerp(cur[target], rim, 0.15), deflect.Clamp(), 0.8));
                            c.Bursts.Add(new Burst(t + 0.3, Spot.Lerp(cur[target], rim, 0.15), false));
                            c.Lines.Add(new(t + 0.3, $"REJECTED! {Last(e.Other)} blocks {Last(e.Actor)}", true));
                            c.Key = true;
                            t += 0.9;
                        }
                        else
                        {
                            double flight = three ? ShotFlight : e.Zone == ShotZone.Rim ? 0.45 : 0.8;
                            c.Ball.Add(new BallSegment(t, t + flight, BallMode.Shot, target, -1, default, rim, three ? 6 : e.Zone == ShotZone.Rim ? 1.5 : 4));
                            t += flight;
                            if (e.Made)
                            {
                                c.Scores.Add((t, e.HomeScore, e.AwayScore));
                                c.Bursts.Add(new Burst(t, rim, true));
                                string who = Last(e.Actor).ToUpperInvariant();
                                c.Lines.Add(new(t, $"{who}! {(three ? $"The {(e.Zone == ShotZone.CornerThree ? "corner " : "")}three is good" : e.Zone == ShotZone.Rim ? "He finishes at the rim" : "The jumper is good")}. {ScoreLine(input, e.HomeScore, e.AwayScore)}", true));
                                c.Key = true;
                                c.Ball.Add(new BallSegment(t, t + 0.5, BallMode.Loose, -1, -1, rim, new Spot(bx + o * 2, 25 + J(rng, 3)), 0.3));
                                c.Next = PossessionStart.AfterMake;
                            }
                            else
                            {
                                c.Lines.Add(new(t, three ? $"{Last(e.Actor)}'s three is off the mark" : $"{Last(e.Actor)} misses", false));
                            }
                        }
                        break;
                    }
                    case PlayKind.Turnover:
                    {
                        var outSpot = new Spot(cur[target].X + J(rng, 3), cur[target].Y < 25 ? 0.2 : 49.8);
                        c.Ball.Add(new BallSegment(t, t + 0.6, BallMode.Loose, -1, -1, cur[target], outSpot, 0.5));
                        c.Lines.Add(new(t, $"{Last(e.Actor)} turns it over. {(e.Home ? input.AwayName : input.HomeName)} ball", false));
                        t += TurnoverTime;
                        c.Next = PossessionStart.DeadBall;
                        break;
                    }
                    case PlayKind.Steal:
                    {
                        c.Lines.Add(new(t, $"Stolen! {Last(e.Actor)} picks off {Last(e.Other)}", true));
                        c.Key = true;
                        int st = Slot(e.Actor);
                        if (st >= 0) c.Ball.Add(new BallSegment(t, t + 0.8, BallMode.Held, st, -1, default, default, 0));
                        if (st >= 0) Go(st, t, t + 0.8, new Spot(cur[st].X - o * 6, cur[st].Y));
                        t += 0.8;
                        c.Next = PossessionStart.AfterStop;
                        break;
                    }
                    case PlayKind.Foul:
                    {
                        int fo = Slot(e.Actor);
                        if (fo >= 0) Go(fo, t - 0.4, t + 0.2, Spot.Lerp(cur[fo], cur[target], 0.7));
                        c.Lines.Add(new(t, e.Other != null ? $"Whistle: foul on {Last(e.Actor)}, {Last(e.Other)} was hit" : $"Whistle: foul on {Last(e.Actor)}", false));
                        c.Ball.Add(new BallSegment(t, t + FoulTime, BallMode.Held, target, -1, default, default, 0));
                        t += FoulTime;
                        c.Next = PossessionStart.DeadBall;
                        break;
                    }
                }
                continue;
            }

            switch (e.Kind)
            {
                case PlayKind.OffensiveRebound:
                case PlayKind.DefensiveRebound:
                {
                    if (run > 0.3) { c.ClockPoints.Add((t, clockNow)); }
                    int r = Slot(e.Actor);
                    var at = new Spot(bx + o * (3 + rng.NextDouble() * 4), 25 + J(rng, 7)).Clamp();
                    if (r >= 0) Go(r, t - 0.6, t + 0.3, at);
                    // Everyone else crashes or retreats a little.
                    for (int k = 0; k < 10; k++)
                        if (k != r) Go(k, t - 0.4, t + ReboundTime, Spot.Lerp(cur[k], rim, k / 5 == offBase / 5 ? 0.15 : 0.25));
                    c.Ball.Add(new BallSegment(t, t + 0.5, BallMode.Loose, -1, -1, rim, at, 1.2));
                    if (r >= 0) c.Ball.Add(new BallSegment(t + 0.5, t + ReboundTime, BallMode.Held, r, -1, default, default, 0));
                    bool off = e.Kind == PlayKind.OffensiveRebound;
                    c.Lines.Add(new(t + 0.4, off ? $"Offensive board, {Last(e.Actor)}!" : $"{Last(e.Actor)} secures the rebound for {(e.Home ? input.HomeName : input.AwayName)}", off));
                    t += ReboundTime;
                    if (off) { firstChance = false; handler = r >= 0 ? r : handler; c.Ball.Add(new BallSegment(t, t + 0.01, BallMode.Held, handler, -1, default, default, 0)); }
                    else
                    {
                        // The outlet: the rebounder looks for the point guard.
                        int pg = defBase;
                        if (r >= 0 && r != pg)
                        {
                            Go(pg, t - 0.5, t + 0.4, new Spot(bx + o * 18, cur[r].Y < 25 ? 12 : 38));
                            c.Ball.Add(new BallSegment(t, t + 0.5, BallMode.Pass, r, pg, default, default, 0.25));
                            c.Lines.Add(new(t + 0.1, $"Outlet to {Last(players[pg])}", false));
                            t += 0.5;
                        }
                        c.Next = PossessionStart.AfterStop;
                    }
                    break;
                }
                case PlayKind.Foul:
                {
                    // A foul inside a free-throw trip (a lane violation is not modelled): just the whistle.
                    c.Lines.Add(new(t, $"Foul on {Last(e.Actor)}", false));
                    t += 0.6;
                    break;
                }
                case PlayKind.FreeThrow:
                {
                    int sh = Slot(e.Actor);
                    if (!inFreeThrows)
                    {
                        inFreeThrows = true;
                        if (clockNow - evClock > 0.05) { c.ClockPoints.Add((t, evClock)); clockNow = evClock; }
                        // The line-up: the shooter at the line, two on each side of the lane, the rest behind the arc.
                        double ft = 15;
                        if (sh >= 0) Go(sh, t, t + LineUpTime, S(ft, 25));
                        var lane = new[] { S(7, 19), S(7, 31), S(10, 19), S(10, 31) };
                        int li = 0;
                        int shooterSide = sh / 5;
                        foreach (var k in Enumerable.Range(0, 10).OrderBy(k => k / 5 == shooterSide ? 1 : 0))
                        {
                            if (k == sh) continue;
                            if (li < 4 && (k / 5 != shooterSide || li >= 2)) Go(k, t, t + LineUpTime, lane[li++]);
                            else Go(k, t, t + LineUpTime, S(28 + J(rng, 2), 25 + J(rng, 14)));
                        }
                        c.Lines.Add(new(t, $"{Last(e.Actor)} goes to the line", false));
                        if (sh >= 0) c.Ball.Add(new BallSegment(t, t + LineUpTime, BallMode.Held, sh, -1, default, default, 0));
                        t += LineUpTime;
                    }
                    if (sh >= 0) c.Ball.Add(new BallSegment(t, t + 0.9, BallMode.Held, sh, -1, default, default, 0));
                    c.Ball.Add(new BallSegment(t + 0.9, t + FreeThrowTime, BallMode.Shot, sh, -1, S(15, 25), rim, 3.5, FreeThrow: true));
                    t += FreeThrowTime;
                    if (e.Made) { c.Scores.Add((t, e.HomeScore, e.AwayScore)); c.Bursts.Add(new Burst(t, rim, true)); }
                    bool more = i + 1 < ev.Count && ev[i + 1].Kind == PlayKind.FreeThrow && ev[i + 1].Actor == e.Actor;
                    c.Lines.Add(new(t, e.Made ? (more ? $"{Last(e.Actor)} makes the first" : $"{Last(e.Actor)} knocks it down. {ScoreLine(input, e.HomeScore, e.AwayScore)}") : $"{Last(e.Actor)} misses from the line", false));
                    if (!more)
                    {
                        inFreeThrows = false;
                        if (e.Made) { c.Next = PossessionStart.AfterMake; c.Ball.Add(new BallSegment(t, t + 0.5, BallMode.Loose, -1, -1, rim, new Spot(bx + o * 2, 25), 0.3)); }
                    }
                    break;
                }
                case PlayKind.PeriodEnd:
                {
                    c.ClockPoints.Add((t, 0));
                    clockNow = 0;
                    string name = PeriodName(e.Period, input.Periods);
                    c.Lines.Add(new(t, $"That's the end of {name}: {input.AwayName} {e.AwayScore}, {input.HomeName} {e.HomeScore}", true));
                    c.Ball.Add(new BallSegment(t, t + PeriodEndTime, BallMode.Loose, -1, -1, BallAt(c, t), new Spot(47, 25), 0.2));
                    t += PeriodEndTime;
                    c.Next = PossessionStart.PeriodStart;
                    break;
                }
            }
        }
        // If nothing happened on the floor (the clock ran out), let the clock run anyway.
        if (c.ClockPoints.Count == 1) { c.ClockPoints.Add((Math.Max(t, D), input.EndClock)); t = Math.Max(t, D); }
        t += Epilogue;
        // Everybody turns for the other end as the possession ends (the next one moves them).
        c.Length = t;
        if (input.LateAndClose) c.Key = true;
        return c;
    }

    private static double ExtraBefore(Choreography c) => c.Ball.Count > 0 && c.Ball[0].Mode == BallMode.Loose ? 1.2 : 0;

    private static double LastEnd(Choreography c) => c.Ball.Count == 0 ? 0 : c.Ball[^1].T1;

    private static Spot BallAt(Choreography c, double t)
    {
        var f = c.Sample(Math.Min(t, c.Ball.Count == 0 ? 0 : c.Ball[^1].T1));
        return f.Ball;
    }

    /// <summary>A spot inside the engine's shot zone, for the side attacking the basket at <paramref name="bx"/>.</summary>
    public static Spot ShotSpot(ShotZone zone, double bx, Random rng)
    {
        double o = bx > 47 ? -1 : 1;
        double r, a;
        switch (zone)
        {
            case ShotZone.Rim: r = 1.5 + rng.NextDouble() * 2.5; a = (rng.NextDouble() - 0.5) * 2.4; break;
            case ShotZone.ShortMid: r = 7 + rng.NextDouble() * 6; a = (rng.NextDouble() - 0.5) * 2.2; break;
            case ShotZone.LongMid: r = 15.5 + rng.NextDouble() * 5; a = (rng.NextDouble() - 0.5) * 2.0; break;
            case ShotZone.CornerThree:
            {
                double along = 1 + rng.NextDouble() * 7;
                double y = rng.NextDouble() < 0.5 ? 2.2 : 47.8;
                return new Spot(o < 0 ? 94 - along : along, y);
            }
            default: r = 24.5 + rng.NextDouble() * 2.5; a = (rng.NextDouble() - 0.5) * 1.8; break;
        }
        return new Spot(bx + o * Math.Cos(a) * r, 25 + Math.Sin(a) * r).Clamp();
    }

    private static Spot TipSpot(int slot, bool homeOff)
    {
        // Around the centre circle for the tip; home on the left half (they attack right), away on the right.
        int k = slot % 5; bool home = slot < 5;
        double side = home ? -1 : 1;
        if (k == 4) return new Spot(47 + side * 1.5, 25);
        double[] ys = { 14, 36, 8, 42 };
        return new Spot(47 + side * (8 + k * 2), ys[k]);
    }

    private static List<Player> Order(IReadOnlyList<Player> five)
    {
        var l = five.Take(5).OrderBy(p => (int)p.PrimaryPosition).ToList();
        return l;
    }

    private static void Place(Player? p, List<Player> five, HashSet<Player> actors)
    {
        if (p == null || five.Contains(p) || five.Count == 0) return;
        // Swap out someone who does nothing in this possession: his position first.
        int at = five.FindIndex(x => x.PrimaryPosition == p.PrimaryPosition && !actors.Contains(x));
        if (at < 0) at = five.FindIndex(x => !actors.Contains(x));
        five[at >= 0 ? at : five.Count - 1] = p;
    }

    private static double J(Random rng, double r) => (rng.NextDouble() * 2 - 1) * r;

    public static string Last(Player? p) => p?.LastName ?? "";

    private static string Where(ShotZone z) => z switch
    {
        ShotZone.CornerThree => "in the corner",
        ShotZone.AboveBreakThree => "on the wing",
        ShotZone.Rim => "cutting to the rim",
        ShotZone.ShortMid => "in the lane",
        _ => "at the elbow",
    };

    public static string ScoreLine(PossessionInput input, int home, int away) =>
        home == away ? $"All square at {home}-{away}"
        : home > away ? $"{input.HomeName} lead {home}-{away}" : $"{input.AwayName} lead {away}-{home}";

    public static string PeriodName(int period, int periods) =>
        period > periods ? (period - periods == 1 ? "overtime" : $"overtime {period - periods}")
        : periods == 2 ? (period == 1 ? "the first half" : "the second half")
        : period switch { 1 => "the first quarter", 2 => "the second quarter", 3 => "the third quarter", _ => "the fourth quarter" };

    private static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }
}

/// <summary>Shirt numbers for the match view's discs (presentation only; the game keeps no numbers). Stable for a
/// player within his team, unique on a roster.</summary>
public static class ShirtNumbers
{
    private static readonly int[] Pool = { 0, 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 20, 21, 22, 23, 24, 25, 30, 31, 32, 33, 34, 35, 40, 41, 42, 44, 45, 50, 55 };

    public static Dictionary<Player, int> For(Team team)
    {
        var d = new Dictionary<Player, int>();
        var used = new HashSet<int>();
        foreach (var p in team.Roster.OrderBy(p => p.Id))
        {
            int start = (int)((uint)(p.Id * 2654435761u) % (uint)Pool.Length);
            int n = Pool[start];
            for (int i = 0; i < Pool.Length && used.Contains(n); i++) n = Pool[(start + i) % Pool.Length];
            used.Add(n);
            d[p] = n;
        }
        return d;
    }
}
