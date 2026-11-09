namespace Hardwood.Core.Model;

/// <summary>How a team runs its offense (decision #58). The sliders on <see cref="Tactic"/> fine-tune it.</summary>
public enum OffensiveSystem { Balanced, PickAndRoll, Motion, PostCentric, PaceAndSpace, Isolation }

/// <summary>How a team defends (decision #59).</summary>
public enum DefensiveScheme { Man, SwitchEverything, Zone23, Drop }

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

    public static TacticPreset From(Tactic t, string name) => new()
    {
        Name = name, System = t.System, Scheme = t.Scheme, Pace = t.Pace, ThreePointEmphasis = t.ThreePointEmphasis,
        CrashGlass = t.CrashGlass, DefensiveAggression = t.DefensiveAggression, StarUsage = t.StarUsage, DoubleStar = t.DoubleStar,
    };

    public void ApplyTo(Tactic t)
    {
        t.System = System; t.Scheme = Scheme; t.Pace = Pace; t.ThreePointEmphasis = ThreePointEmphasis;
        t.CrashGlass = CrashGlass; t.DefensiveAggression = DefensiveAggression; t.StarUsage = StarUsage; t.DoubleStar = DoubleStar;
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

    /// <summary>The best natural role for a player's primary position.</summary>
    public static Role Best(Player p) =>
        Ranked(p).Where(x => Natural(p.PrimaryPosition, x.role)).Select(x => x.role).DefaultIfEmpty(Role.GlueGuy).First();
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
        _ => "Drop coverage",
    };

    public static string Describe(DefensiveScheme s) => s switch
    {
        DefensiveScheme.Man => "Straight up. The baseline everything else is measured against.",
        DefensiveScheme.SwitchEverything => "Switch every screen: kills pick-and-roll, but bigs end up on guards. Needs versatile defenders.",
        DefensiveScheme.Zone23 => "Pack the paint: fewer shots at the rim, more threes allowed. Needs good help defense.",
        _ => "The big sags to protect the rim: concedes pull-up mid-range jumpers. Needs a rim protector.",
    };

    private static double Avg(IEnumerable<Player> ps, params Attr[] attrs) =>
        ps.Select(p => attrs.Average(a => (double)p.Attributes[a])).DefaultIfEmpty(10).Average();

    private static double Top(IEnumerable<Player> ps, int n, params Attr[] attrs) =>
        ps.Select(p => attrs.Average(a => (double)p.Attributes[a])).OrderByDescending(x => x).Take(n).DefaultIfEmpty(10).Average();

    /// <summary>How well a group of players (the five on the floor, or a rotation) suits an offensive system: 0 (bad) to 1 (ideal).</summary>
    public static double Raw(OffensiveSystem s, IReadOnlyCollection<Player> players)
    {
        if (players.Count == 0) return 11.5;
        return s switch
        {
            OffensiveSystem.Balanced => 11.5,
            OffensiveSystem.PickAndRoll => Top(players, 1, Attr.PickAndRollHandler, Attr.BallHandling, Attr.ShotCreation) * 0.55
                                           + Top(players.Where(p => p.PrimaryPosition >= Position.PF), 1, Attr.Dunk, Attr.Vertical, Attr.OffBallMovement) * 0.45,
            OffensiveSystem.Motion => Avg(players, Attr.Passing, Attr.OffBallMovement, Attr.Teamwork, Attr.BasketballIQ),
            OffensiveSystem.PostCentric => Top(players, 1, Attr.PostMoves, Attr.CloseShot, Attr.Strength) * 0.65 + Avg(players, Attr.ThreePoint, Attr.CatchAndShoot) * 0.35,
            OffensiveSystem.PaceAndSpace => Avg(players, Attr.ThreePoint, Attr.CatchAndShoot) * 0.6 + Avg(players, Attr.Speed, Attr.Stamina) * 0.4,
            _ => Top(players, 1, Attr.ShotCreation, Attr.MidRange, Attr.BallHandling) * 0.7 + Avg(players, Attr.CatchAndShoot) * 0.3,
        };
    }

    /// <summary>How well players suit a defensive scheme: 0 to 1.</summary>
    public static double Raw(DefensiveScheme s, IReadOnlyCollection<Player> players)
    {
        if (players.Count == 0) return 11.5;
        return s switch
        {
            DefensiveScheme.Man => 11.5,
            DefensiveScheme.SwitchEverything => Avg(players, Attr.PerimeterDefense, Attr.Agility) * 0.6
                                                + Avg(players.Where(p => p.PrimaryPosition >= Position.PF), Attr.PerimeterDefense, Attr.Agility, Attr.Speed) * 0.4,
            DefensiveScheme.Zone23 => Avg(players, Attr.HelpDefense, Attr.BasketballIQ) * 0.7 + Top(players, 1, Attr.Block, Attr.InteriorDefense) * 0.3,
            _ => Top(players, 1, Attr.Block, Attr.InteriorDefense, Attr.HelpDefense) * 0.7 + Avg(players, Attr.PerimeterDefense) * 0.3,
        };
    }

    /// <summary>
    /// How well a group of players (the five on the floor, or a rotation) suits a system or scheme: 0 (bad) to 1
    /// (ideal), with 0.5 an average NABL rotation. Centers and spreads measured on generated leagues (Nov 9).
    /// </summary>
    public static double Fit(OffensiveSystem s, IReadOnlyCollection<Player> players) =>
        s == OffensiveSystem.Balanced ? 0.5 : Math.Clamp(0.5 + (Raw(s, players) - OffCenter[(int)s]) / OffSpread[(int)s], 0, 1);
    public static double Fit(DefensiveScheme s, IReadOnlyCollection<Player> players) =>
        s == DefensiveScheme.Man ? 0.5 : Math.Clamp(0.5 + (Raw(s, players) - DefCenter[(int)s]) / DefSpread[(int)s], 0, 1);

    // Indexed by enum value (Balanced/Man unused). Center = mean raw score of a top-9 rotation over four generated
    // leagues; spread = 4 standard deviations, so a rotation two SDs above average fits perfectly.
    internal static double[] OffCenter = { 0, 16.26, 12.81, 15.79, 12.85, 15.39 };
    internal static double[] OffSpread = { 1, 3.7, 2.5, 3.9, 3.1, 4.3 };
    internal static double[] DefCenter = { 0, 12.01, 14.56, 15.91 };
    internal static double[] DefSpread = { 1, 3.0, 3.1, 4.0 };
}
