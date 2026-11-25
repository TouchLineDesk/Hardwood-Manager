namespace Hardwood.Core.Model;

/// <summary>How a team runs its offense (decision #58). The sliders on <see cref="Tactic"/> fine-tune it.</summary>
public enum OffensiveSystem { Balanced, PickAndRoll, Motion, PostCentric, PaceAndSpace, Isolation }

/// <summary>How a team defends (decision #59). Run 194 (slice 4 of the college launch plan, #1170): the 1-3-1 and the
/// match-up zone are college schemes (<see cref="Engine.Tactics.Schemes"/>); the pros keep the first four. New values go
/// at the end, so saves keep their numbers.</summary>
public enum DefensiveScheme { Man, SwitchEverything, Zone23, Drop, Zone131, MatchUpZone }

/// <summary>FM-style player roles (decision #60). Each changes how often a player shoots, where from, and how he defends.</summary>
public enum Role
{
    FloorGeneral, ScoringGuard, Slasher, SpotUpShooter, ThreeAndD, PointForward,
    StretchBig, PostScorer, RimRunner, RimProtector, GlueGuy,
}

/// <summary>A saved set of tactics (decision #61): system, scheme and sliders, but not roles or matchups.</summary>
public sealed class TacticPreset
{
    public string Name { get; set; } = "";
    public OffensiveSystem System { get; set; }
    public DefensiveScheme Scheme { get; set; }
    public double Pace { get; set; } = 99;
    public int ThreePointEmphasis { get; set; } = 50;
    public int CrashGlass { get; set; } = 50;
    public int DefensiveAggression { get; set; } = 50;
    public int StarUsage { get; set; } = 50;
    public bool DoubleStar { get; set; }
    /// <summary>Run 194: the full-court press (college).</summary>
    public bool FullCourtPress { get; set; }

    public static TacticPreset From(Tactic t, string name) => new()
    {
        Name = name, System = t.System, Scheme = t.Scheme, Pace = t.Pace, ThreePointEmphasis = t.ThreePointEmphasis,
        CrashGlass = t.CrashGlass, DefensiveAggression = t.DefensiveAggression, StarUsage = t.StarUsage, DoubleStar = t.DoubleStar,
        FullCourtPress = t.FullCourtPress,
    };

    public void ApplyTo(Tactic t)
    {
        t.System = System; t.Scheme = Scheme; t.Pace = Pace; t.ThreePointEmphasis = ThreePointEmphasis;
        t.CrashGlass = CrashGlass; t.DefensiveAggression = DefensiveAggression; t.StarUsage = StarUsage; t.DoubleStar = DoubleStar;
        t.FullCourtPress = FullCourtPress;
    }
}

/// <summary>What each role does on the floor, and which attributes make a player suited to it.</summary>
public sealed record RoleInfo(
    Role Role, string Name, string Summary,
    (Attr attr, double weight)[] Suits,
    double Usage,          // shot-taking weight multiplier
    double Playmaking,     // assist weight multiplier
    double[] ZoneMix,      // Rim, ShortMid, LongMid, CornerThree, AboveBreakThree multipliers
    double RimDefense,     // added to the team's rim contest while he's on the floor
    double PerimeterDefense);

public static class Roles
{
    public static readonly RoleInfo[] All =
    {
        new(Role.FloorGeneral, "Floor General", "Runs the offense and sets others up; shoots when open.",
            new[] { (Attr.Passing, 3.0), (Attr.CourtVision, 3.0), (Attr.Decisions, 2.0), (Attr.BallHandling, 2.0), (Attr.PickAndRollHandler, 1.5), (Attr.BasketballIQ, 1.0) },
            0.85, 1.7, new[] { 1.0, 1.0, 1.0, 1.0, 1.0 }, 0, 0),
        new(Role.ScoringGuard, "Scoring Guard", "Creates his own shot from anywhere.",
            new[] { (Attr.ShotCreation, 3.0), (Attr.MidRange, 2.0), (Attr.ThreePoint, 2.0), (Attr.BallHandling, 2.0), (Attr.Composure, 1.0) },
            1.25, 0.9, new[] { 0.9, 1.2, 1.3, 0.85, 1.15 }, 0, 0),
        new(Role.Slasher, "Slasher", "Attacks the basket off the dribble.",
            new[] { (Attr.Layup, 3.0), (Attr.Dunk, 1.5), (Attr.Speed, 2.0), (Attr.Acceleration, 2.0), (Attr.BallHandling, 1.5), (Attr.Strength, 1.0) },
            1.1, 0.9, new[] { 1.6, 1.0, 0.7, 0.7, 0.7 }, 0, 0),
        new(Role.SpotUpShooter, "Spot-up Shooter", "Spaces the floor and waits for kick-outs.",
            new[] { (Attr.ThreePoint, 3.0), (Attr.CatchAndShoot, 3.0), (Attr.OffBallMovement, 2.0), (Attr.FreeThrow, 1.0) },
            0.8, 0.8, new[] { 0.6, 0.7, 0.8, 1.6, 1.4 }, 0, 0),
        new(Role.ThreeAndD, "3-and-D Wing", "Guards the other team's best wing and hits open corner threes.",
            new[] { (Attr.PerimeterDefense, 3.0), (Attr.ThreePoint, 2.0), (Attr.CatchAndShoot, 2.0), (Attr.Agility, 1.5), (Attr.Steal, 1.0) },
            0.7, 0.8, new[] { 0.7, 0.6, 0.6, 1.8, 1.2 }, 0, 0.6),
        new(Role.PointForward, "Point Forward", "A big wing who handles and passes.",
            new[] { (Attr.Passing, 2.5), (Attr.BallHandling, 2.0), (Attr.CourtVision, 2.0), (Attr.Strength, 1.5), (Attr.Layup, 1.0), (Attr.Decisions, 1.0) },
            1.0, 1.35, new[] { 1.15, 1.1, 1.0, 0.9, 0.95 }, 0, 0),
        new(Role.StretchBig, "Stretch Big", "A big who pulls his man out to the three-point line.",
            new[] { (Attr.ThreePoint, 3.0), (Attr.MidRange, 2.0), (Attr.CatchAndShoot, 2.0), (Attr.DefensiveRebounding, 1.0), (Attr.Strength, 1.0) },
            0.85, 0.9, new[] { 0.7, 0.9, 1.1, 1.3, 1.5 }, 0, 0),
        new(Role.PostScorer, "Post Scorer", "Wants the ball on the block.",
            new[] { (Attr.PostMoves, 3.0), (Attr.CloseShot, 2.5), (Attr.Strength, 2.0), (Attr.Layup, 1.0), (Attr.Composure, 1.0) },
            1.1, 0.9, new[] { 1.4, 1.5, 0.8, 0.4, 0.4 }, 0, 0),
        new(Role.RimRunner, "Rim Runner", "Rolls hard, runs the floor and finishes lobs.",
            new[] { (Attr.Dunk, 3.0), (Attr.Vertical, 2.0), (Attr.Speed, 1.5), (Attr.OffBallMovement, 1.5), (Attr.OffensiveRebounding, 1.5) },
            0.7, 0.7, new[] { 2.0, 0.8, 0.4, 0.2, 0.2 }, 0.3, 0),
        new(Role.RimProtector, "Rim Protector", "Anchors the defense; scores only what falls to him.",
            new[] { (Attr.Block, 3.0), (Attr.InteriorDefense, 3.0), (Attr.HelpDefense, 2.0), (Attr.DefensiveRebounding, 2.0), (Attr.Vertical, 1.0) },
            0.5, 0.7, new[] { 1.8, 0.9, 0.4, 0.2, 0.2 }, 1.0, 0),
        new(Role.GlueGuy, "Glue Guy", "Makes the right play, defends, does the little things.",
            new[] { (Attr.Teamwork, 2.5), (Attr.BasketballIQ, 2.0), (Attr.HelpDefense, 2.0), (Attr.Decisions, 1.5), (Attr.WorkRate, 1.5) },
            0.7, 1.1, new[] { 1.0, 1.0, 1.0, 1.0, 1.0 }, 0.3, 0.3),
    };

    public static RoleInfo Info(Role r) => All[(int)r];

    /// <summary>How well a player fits a role: 0 to 5 stars in half steps (a weighted average of the role's attributes).</summary>
    public static double Stars(Player p, Role r)
    {
        var info = Info(r);
        double sum = 0, w = 0;
        foreach (var (a, wt) in info.Suits) { sum += p.Attributes[a] * wt; w += wt; }
        double avg = sum / w; // 1-20
        return Math.Clamp(Math.Round((avg - 6) / 2.4 * 2) / 2, 0, 5);
    }

    /// <summary>
    /// H204 (run 203): how far a role sits below the player's own best natural role, in stars (0 = his best fit). The star
    /// scale is absolute, so at a low-major every role reads under 2 stars (Rider: 15 of 15 "doesn't suit his role" on day
    /// one); a college player's mood compares the role with his own game instead.
    /// </summary>
    public static double FitGap(Player p, Role r) => Math.Max(0, Stars(p, Best(p)) - Stars(p, r));

    /// <summary>H204: the gap, in stars below his best natural role, at which a college player feels miscast.</summary>
    public const double CollegeMiscastGap = 1.5;

    /// <summary>
    /// H204: whether a player's role suits him (+1), doesn't (−1) or neither (0). The pros keep the absolute scale (3.5+
    /// suits, under 2 doesn't). In college a role suits him at 3.5+ stars or when it is his best natural role, and doesn't
    /// when it is under 2 stars and at least <see cref="CollegeMiscastGap"/> below his best.
    /// </summary>
    public static int Suitability(Player p, Role r, bool college)
    {
        double stars = Stars(p, r);
        if (!college) return stars >= 3.5 ? 1 : stars < 2 ? -1 : 0;
        double gap = FitGap(p, r);
        if (stars >= 3.5 || gap <= 0) return 1;
        return stars < 2 && gap >= CollegeMiscastGap ? -1 : 0;
    }

    /// <summary>The roles a player suits best, best first.</summary>
    public static IEnumerable<(Role role, double stars)> Ranked(Player p) =>
        All.Select(i => (i.Role, Stars(p, i.Role))).OrderByDescending(x => x.Item2);

    /// <summary>Usual roles by position, so a center isn't made a Floor General just because he passes well.</summary>
    public static bool Natural(Position pos, Role r) => pos switch
    {
        Position.PG => r is Role.FloorGeneral or Role.ScoringGuard or Role.Slasher or Role.SpotUpShooter or Role.GlueGuy,
        Position.SG => r is Role.ScoringGuard or Role.Slasher or Role.SpotUpShooter or Role.ThreeAndD or Role.GlueGuy or Role.FloorGeneral,
        Position.SF => r is Role.ThreeAndD or Role.Slasher or Role.SpotUpShooter or Role.PointForward or Role.ScoringGuard or Role.GlueGuy,
        Position.PF => r is Role.StretchBig or Role.PostScorer or Role.PointForward or Role.RimRunner or Role.GlueGuy or Role.ThreeAndD,
        _ => r is Role.RimProtector or Role.RimRunner or Role.PostScorer or Role.StretchBig or Role.GlueGuy,
    };

    /// <summary>The best natural role for a player's primary position. H105 (run 112): on a tie, the role that gives him
    /// more shots. Gilgeous-Alexander's attributes are all near 20, so every role tied at 5 stars and the first in the
    /// list (Floor General, 0.85 usage) won: 19 ppg while role players on thin teams led the league.</summary>
    public static Role Best(Player p)
    {
        var natural = Ranked(p).Where(x => Natural(p.PrimaryPosition, x.role)).ToList();
        if (natural.Count == 0) return Role.GlueGuy;
        double top = natural[0].stars;
        return natural.Where(x => x.stars >= top).OrderByDescending(x => Info(x.role).Usage).First().role;
    }
}

public static class Systems
{
    public static string Name(OffensiveSystem s) => s switch
    {
        OffensiveSystem.Balanced => "Balanced",
        OffensiveSystem.PickAndRoll => "Pick-and-roll heavy",
        OffensiveSystem.Motion => "Motion",
        OffensiveSystem.PostCentric => "Post-centric",
        OffensiveSystem.PaceAndSpace => "Pace and space",
        _ => "Isolation",
    };

    public static string Describe(OffensiveSystem s) => s switch
    {
        OffensiveSystem.Balanced => "A bit of everything; no strengths to lean on, no weaknesses to exploit.",
        OffensiveSystem.PickAndRoll => "Ball screens for your best handler: drives, pull-ups and lobs to the roller. Needs a handler and a big who rolls or pops.",
        OffensiveSystem.Motion => "Cuts, screens and ball movement: more assists and open corner threes, slightly more turnovers. Needs passers and smart off-ball players.",
        OffensiveSystem.PostCentric => "Feed the post and play inside-out. Slower, more shots near the basket. Needs a big who scores on the block.",
        OffensiveSystem.PaceAndSpace => "Push the pace, shoot threes and attack the rim; few mid-range shots. Needs shooters and speed.",
        _ => "Clear out for your best scorer. Fewer turnovers and assists, more mid-range shots. Needs a player who creates his own shot.",
    };

    public static string Name(DefensiveScheme s) => s switch
    {
        DefensiveScheme.Man => "Man-to-man",
        DefensiveScheme.SwitchEverything => "Switch everything",
        DefensiveScheme.Zone23 => "2-3 zone",
        DefensiveScheme.Zone131 => "1-3-1 zone",
        DefensiveScheme.MatchUpZone => "Match-up zone",
        _ => "Drop coverage",
    };

    public static string Describe(DefensiveScheme s) => s switch
    {
        DefensiveScheme.Man => "Straight up. The baseline everything else is measured against.",
        DefensiveScheme.SwitchEverything => "Switch every screen: kills pick-and-roll, but bigs end up on guards. Needs versatile defenders.",
        DefensiveScheme.Zone23 => "Pack the paint: fewer shots at the rim, more threes allowed. Needs good help defense.",
        DefensiveScheme.Zone131 => "Long arms on the wings trap the ball: more turnovers, but the corners and the glass are open. Needs length and quick hands.",
        DefensiveScheme.MatchUpZone => "A zone that guards like man-to-man: a little less at the rim and a little more on threes than man. Needs smart, connected defenders.",
        _ => "The big sags to protect the rim: concedes pull-up mid-range jumpers. Needs a rim protector.",
    };

    private static double Avg(IEnumerable<Player> ps, params Attr[] attrs) =>
        ps.Select(p => attrs.Average(a => (double)p.Attributes[a])).DefaultIfEmpty(10).Average();

    private static double Top(IEnumerable<Player> ps, int n, params Attr[] attrs) =>
        ps.Select(p => attrs.Average(a => (double)p.Attributes[a])).OrderByDescending(x => x).Take(n).DefaultIfEmpty(10).Average();

    /// <summary>One ingredient of a system's fit: what it is, how good the group is at it, its weight, and the player who supplies it (for "best of" ingredients).</summary>
    public sealed record FitPart(string Label, double Value, double Weight, Player? Best, bool OneMan = false);

    private static FitPart AvgPart(string label, IEnumerable<Player> ps, double w, params Attr[] attrs) => new(label, Avg(ps, attrs), w, null);
    private static FitPart TopPart(string label, IEnumerable<Player> ps, double w, params Attr[] attrs)
    {
        var best = ps.Select(p => (p, v: attrs.Average(a => (double)p.Attributes[a]))).OrderByDescending(x => x.v).FirstOrDefault();
        return new(label, best.p == null ? 10 : best.v, w, best.p, true);
    }

    /// <summary>The ingredients of an offensive system's fit (weights add to 1; <see cref="Raw(OffensiveSystem, IReadOnlyCollection{Player})"/> is their weighted sum).</summary>
    public static List<FitPart> Parts(OffensiveSystem s, IReadOnlyCollection<Player> players) => s switch
    {
        OffensiveSystem.Balanced => new(),
        OffensiveSystem.PickAndRoll => new() { TopPart("ball handler", players, 0.55, Attr.PickAndRollHandler, Attr.BallHandling, Attr.ShotCreation),
                                               TopPart("roll man", players.Where(p => p.PrimaryPosition >= Position.PF), 0.45, Attr.Dunk, Attr.Vertical, Attr.OffBallMovement) },
        OffensiveSystem.Motion => new() { AvgPart("passing and movement", players, 1, Attr.Passing, Attr.OffBallMovement, Attr.Teamwork, Attr.BasketballIQ) },
        OffensiveSystem.PostCentric => new() { TopPart("post scorer", players, 0.65, Attr.PostMoves, Attr.CloseShot, Attr.Strength),
                                               AvgPart("shooting around him", players, 0.35, Attr.ThreePoint, Attr.CatchAndShoot) },
        OffensiveSystem.PaceAndSpace => new() { AvgPart("shooting", players, 0.6, Attr.ThreePoint, Attr.CatchAndShoot),
                                                AvgPart("speed and stamina", players, 0.4, Attr.Speed, Attr.Stamina) },
        _ => new() { TopPart("go-to scorer", players, 0.7, Attr.ShotCreation, Attr.MidRange, Attr.BallHandling),
                     AvgPart("catch-and-shoot help", players, 0.3, Attr.CatchAndShoot) },
    };

    /// <summary>The ingredients of a defensive scheme's fit.</summary>
    public static List<FitPart> Parts(DefensiveScheme s, IReadOnlyCollection<Player> players) => s switch
    {
        DefensiveScheme.Man => new(),
        DefensiveScheme.SwitchEverything => new() { AvgPart("perimeter defence", players, 0.6, Attr.PerimeterDefense, Attr.Agility),
                                                    AvgPart("bigs who can switch", players.Where(p => p.PrimaryPosition >= Position.PF), 0.4, Attr.PerimeterDefense, Attr.Agility, Attr.Speed) },
        DefensiveScheme.Zone23 => new() { AvgPart("help defence", players, 0.7, Attr.HelpDefense, Attr.BasketballIQ),
                                          TopPart("rim protector", players, 0.3, Attr.Block, Attr.InteriorDefense) },
        DefensiveScheme.Zone131 => new() { AvgPart("quick hands", players, 0.5, Attr.Steal, Attr.Decisions),
                                           AvgPart("length and speed on the wings", players.Where(p => p.PrimaryPosition is >= Position.SG and <= Position.PF), 0.5, Attr.PerimeterDefense, Attr.Speed, Attr.Agility) },
        DefensiveScheme.MatchUpZone => new() { AvgPart("reading the floor", players, 0.6, Attr.BasketballIQ, Attr.HelpDefense, Attr.Teamwork),
                                               AvgPart("perimeter defence", players, 0.4, Attr.PerimeterDefense) },
        _ => new() { TopPart("rim protector", players, 0.7, Attr.Block, Attr.InteriorDefense, Attr.HelpDefense),
                     AvgPart("perimeter defence", players, 0.3, Attr.PerimeterDefense) },
    };

    /// <summary>How well a group of players (the five on the floor, or a rotation) suits an offensive system (raw score; see <see cref="Fit(OffensiveSystem, IReadOnlyCollection{Player})"/>).</summary>
    public static double Raw(OffensiveSystem s, IReadOnlyCollection<Player> players) =>
        players.Count == 0 || s == OffensiveSystem.Balanced ? 11.5 : Parts(s, players).Sum(p => p.Value * p.Weight);

    /// <summary>How well players suit a defensive scheme (raw score).</summary>
    public static double Raw(DefensiveScheme s, IReadOnlyCollection<Player> players) =>
        players.Count == 0 || s == DefensiveScheme.Man ? 11.5 : Parts(s, players).Sum(p => p.Value * p.Weight);

    /// <summary>One reason behind a fit rating: what it says and how much it moves the fit (+ helps, − hurts; the effects add up to fit − 0.5 before clamping).</summary>
    public sealed record FitReason(string Text, double Effect);

    /// <summary>Why a system fits or doesn't (the assistant explains "why", roadmap 4): each ingredient against an average rotation, biggest first.</summary>
    public static List<FitReason> Reasons(OffensiveSystem s, IReadOnlyCollection<Player> players) =>
        s == OffensiveSystem.Balanced || players.Count == 0 ? new() : Explain(Parts(s, players), OffCenter[(int)s], OffSpread[(int)s]);

    public static List<FitReason> Reasons(DefensiveScheme s, IReadOnlyCollection<Player> players, bool college = false) =>
        s == DefensiveScheme.Man || players.Count == 0 ? new() : Explain(Parts(s, players), Center(s, college), Spread(s, college));

    private static List<FitReason> Explain(List<FitPart> parts, double center, double spread) =>
        parts.Select(p =>
        {
            double effect = p.Weight * (p.Value - center) / spread;
            string level = effect >= 0.08 ? "a real strength" : effect >= 0.025 ? "better than most" : effect <= -0.08 ? "a real weakness" : effect <= -0.025 ? "below average" : "about average";
            string who = p.Best != null ? $"best is {p.Best.Name}, {p.Value:0.#}" : p.OneMan ? "nobody in the rotation" : $"{p.Value:0.#} average";
            string text = $"{Cap(p.Label)}: {level} ({who})";
            return new FitReason(text, effect);
        }).OrderByDescending(r => Math.Abs(r.Effect)).ToList();

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>
    /// How well a group of players (the five on the floor, or a rotation) suits a system or scheme: 0 (bad) to 1
    /// (ideal), with 0.5 an average NABL rotation. Centers and spreads measured on generated leagues (Nov 9).
    /// </summary>
    public static double Fit(OffensiveSystem s, IReadOnlyCollection<Player> players) =>
        s == OffensiveSystem.Balanced ? 0.5 : Math.Clamp(0.5 + (Raw(s, players) - OffCenter[(int)s]) / OffSpread[(int)s], 0, 1);
    /// <param name="college">Run 194: measured against college rotations. Against the pros' centres every college rotation
    /// fit every scheme at 0 (college players are 4-5 points lower), so every college bench played man-to-man.</param>
    public static double Fit(DefensiveScheme s, IReadOnlyCollection<Player> players, bool college = false) =>
        s == DefensiveScheme.Man ? 0.5 : Math.Clamp(0.5 + (Raw(s, players) - Center(s, college)) / Spread(s, college), 0, 1);

    private static double Center(DefensiveScheme s, bool college) => (college ? DefCenterCollege : DefCenter)[(int)s];
    private static double Spread(DefensiveScheme s, bool college) => (college ? DefSpreadCollege : DefSpread)[(int)s];

    // Indexed by enum value (Balanced/Man unused). Center = mean raw score of a top-9 rotation over four generated
    // leagues; spread = 4 standard deviations, so a rotation two SDs above average fits perfectly.
    internal static double[] OffCenter = { 0, 16.26, 12.81, 15.79, 12.85, 15.39 };
    internal static double[] OffSpread = { 1, 3.7, 2.5, 3.9, 3.1, 4.3 };
    // Run 194 (`hardwood schemefits`, seeds 2026 and 31, top-9 rotations): the 1-3-1 and the match-up zone on the pros'
    // clubs (12.9 / 3.0, 12.73 / 3.05), and every scheme on the 365 college programs (mean / 4 SD).
    internal static double[] DefCenter = { 0, 12.01, 14.56, 15.91, 12.90, 12.73 };
    internal static double[] DefSpread = { 1, 3.0, 3.1, 4.0, 3.0, 3.05 };
    internal static double[] DefCenterCollege = { 0, 7.92, 9.26, 10.37, 8.33, 8.11 };
    internal static double[] DefSpreadCollege = { 1, 3.62, 4.16, 4.81, 3.58, 3.46 };

    /// <summary>Run 194: what the full-court press needs from a rotation (all nine, so depth counts: pressing tires legs).</summary>
    public static List<FitPart> PressParts(IReadOnlyCollection<Player> players) => new()
    {
        AvgPart("speed and stamina", players, 0.5, Attr.Speed, Attr.Stamina, Attr.Acceleration),
        AvgPart("hands and ball pressure", players, 0.5, Attr.Steal, Attr.PerimeterDefense),
    };

    public static double PressRaw(IReadOnlyCollection<Player> players) => players.Count == 0 ? 11.5 : PressParts(players).Sum(p => p.Value * p.Weight);

    /// <summary>Run 194: how well a rotation suits the full-court press, 0 to 1 (0.5 an average rotation).</summary>
    public static double PressFit(IReadOnlyCollection<Player> players) =>
        Math.Clamp(0.5 + (PressRaw(players) - PressCenter) / PressSpread, 0, 1);

    public static List<FitReason> PressReasons(IReadOnlyCollection<Player> players) =>
        players.Count == 0 ? new() : Explain(PressParts(players), PressCenter, PressSpread);

    internal static double PressCenter = 8.42, PressSpread = 3.6; // college programs (`hardwood schemefits`)
}
