using Hardwood.Core.Model;

namespace Hardwood.Core.Generation;

/// <summary>
/// Procedurally generates players. Attributes are derived from Current Ability (1–200)
/// through a position profile plus noise, then nudged so the weighted attribute mean
/// matches the CA the player was given.
/// </summary>
public sealed class PlayerGenerator
{
    private readonly Rng _rng;
    private int _nextId = 1;

    public PlayerGenerator(Rng rng, int firstId = 1)
    {
        _rng = rng;
        _nextId = firstId;
    }

    /// <summary>Mean visible attribute implied by a CA value. CA 100 → ~11, CA 180 → ~16.6.</summary>
    public static double MeanAttributeFor(int ca) => 4.0 + ca / 200.0 * 14.0;

    public Player Generate(Position pos, int currentAbility, int age, DateOnly today)
    {
        int ca = Math.Clamp(currentAbility, 20, 200);
        int pa = PotentialFor(ca, age);

        var birth = today.AddYears(-age).AddDays(-_rng.Next(0, 365));
        var (height, wingspan, weight) = Body(pos);

        var player = new Player
        {
            Id = _nextId++,
            FirstName = _rng.Pick(NamePool.FirstNames),
            LastName = _rng.Pick(NamePool.LastNames),
            BirthDate = birth,
            HeightCm = height,
            WingspanCm = wingspan,
            WeightKg = weight,
            PrimaryPosition = pos,
            SecondaryPosition = Secondary(pos),
            CurrentAbility = ca,
            PotentialAbility = pa,
        };

        FillAttributes(player, ca, age);
        FillHidden(player);
        FillTendencies(player);
        return player;
    }

    private int PotentialFor(int ca, int age)
    {
        // Young players have headroom; veterans have PA ≈ CA (or a touch above, their old peak).
        double headroom = age switch
        {
            <= 20 => _rng.NextGaussian(45, 15),
            <= 23 => _rng.NextGaussian(28, 12),
            <= 26 => _rng.NextGaussian(10, 7),
            _ => _rng.NextGaussian(2, 2),
        };
        return Math.Clamp(ca + (int)Math.Max(0, headroom), ca, 200);
    }

    private Position? Secondary(Position pos) => pos switch
    {
        Position.PG => _rng.Chance(0.5) ? Position.SG : null,
        Position.SG => _rng.Chance(0.6) ? (_rng.Chance(0.5) ? Position.PG : Position.SF) : null,
        Position.SF => _rng.Chance(0.6) ? (_rng.Chance(0.5) ? Position.SG : Position.PF) : null,
        Position.PF => _rng.Chance(0.6) ? (_rng.Chance(0.6) ? Position.C : Position.SF) : null,
        Position.C => _rng.Chance(0.4) ? Position.PF : null,
        _ => null,
    };

    private (int height, int wingspan, int weight) Body(Position pos)
    {
        var (meanH, sdH) = pos switch
        {
            Position.PG => (188.0, 4.5),
            Position.SG => (195.0, 4.0),
            Position.SF => (201.0, 4.0),
            Position.PF => (206.0, 4.0),
            _ => (211.0, 4.5),
        };
        int h = (int)Math.Round(_rng.NextGaussian(meanH, sdH));
        int ws = (int)Math.Round(h * 1.06 + _rng.NextGaussian(0, 4));
        int w = (int)Math.Round((h - 100) * 1.05 + _rng.NextGaussian(0, 7));
        return (h, ws, w);
    }

    /// <summary>Position profile: multiplier on the mean per attribute. 1.0 = average for the CA.</summary>
    private static double Profile(Position pos, Attr a)
    {
        // Baseline by group, then position tweaks.
        double m = 1.0;
        switch (a)
        {
            case Attr.BallHandling: case Attr.Passing: case Attr.CourtVision: case Attr.PickAndRollHandler:
                m = pos switch { Position.PG => 1.30, Position.SG => 1.10, Position.SF => 0.95, Position.PF => 0.80, _ => 0.65 }; break;
            case Attr.ThreePoint: case Attr.CatchAndShoot:
                m = pos switch { Position.PG => 1.10, Position.SG => 1.20, Position.SF => 1.10, Position.PF => 0.95, _ => 0.70 }; break;
            case Attr.MidRange: case Attr.ShotCreation:
                m = pos switch { Position.PG => 1.10, Position.SG => 1.15, Position.SF => 1.05, Position.PF => 0.95, _ => 0.80 }; break;
            case Attr.FreeThrow:
                m = pos switch { Position.PG => 1.12, Position.SG => 1.12, Position.SF => 1.05, Position.PF => 0.95, _ => 0.82 }; break;
            case Attr.CloseShot: case Attr.Layup:
                m = pos switch { Position.PG => 1.00, Position.SG => 1.00, Position.SF => 1.00, Position.PF => 1.05, _ => 1.10 }; break;
            case Attr.Dunk: case Attr.PostMoves:
                m = pos switch { Position.PG => 0.70, Position.SG => 0.85, Position.SF => 1.00, Position.PF => 1.15, _ => 1.30 }; break;
            case Attr.InteriorDefense: case Attr.Block: case Attr.DefensiveRebounding: case Attr.OffensiveRebounding:
                m = pos switch { Position.PG => 0.65, Position.SG => 0.75, Position.SF => 0.95, Position.PF => 1.20, _ => 1.35 }; break;
            case Attr.PerimeterDefense: case Attr.Steal:
                m = pos switch { Position.PG => 1.15, Position.SG => 1.15, Position.SF => 1.10, Position.PF => 0.90, _ => 0.75 }; break;
            case Attr.Speed: case Attr.Acceleration: case Attr.Agility:
                m = pos switch { Position.PG => 1.20, Position.SG => 1.15, Position.SF => 1.05, Position.PF => 0.90, _ => 0.75 }; break;
            case Attr.Strength:
                m = pos switch { Position.PG => 0.75, Position.SG => 0.85, Position.SF => 1.00, Position.PF => 1.15, _ => 1.30 }; break;
            case Attr.Vertical:
                m = pos switch { Position.PG => 1.00, Position.SG => 1.05, Position.SF => 1.05, Position.PF => 1.00, _ => 0.95 }; break;
            default:
                m = 1.0; break; // mental, stamina, fitness, durability, help defense, off-ball
        }
        return m;
    }

    private void FillAttributes(Player p, int ca, int age)
    {
        double mean = MeanAttributeFor(ca);
        var attrs = p.Attributes;

        foreach (var a in AttrInfo.All)
        {
            double target = mean * Profile(p.PrimaryPosition, a);
            double noise = _rng.NextGaussian(0, 1.6);
            double v = target + noise;

            // Age shaping: young players lower on mental, older lower on physical.
            if (AttrInfo.GroupOf(a) == AttrGroup.Mental) v += (age - 26) * 0.12;
            if (AttrInfo.GroupOf(a) == AttrGroup.Physical) v -= Math.Max(0, age - 29) * 0.35;

            attrs[a] = (int)Math.Round(Math.Clamp(v, 1, 20));
        }

        // Re-centre so the plain mean matches the CA-implied mean (position profiles average ~1.0, noise and clamps drift it).
        double actual = AttrInfo.All.Average(x => (double)attrs[x]);
        double shift = mean - actual;
        if (Math.Abs(shift) > 0.3)
        {
            foreach (var a in AttrInfo.All)
                attrs[a] = (int)Math.Round(Math.Clamp(attrs[a] + shift, 1, 20));
        }
    }

    private void FillHidden(Player p)
    {
        var h = p.Hidden;
        int Gen() => (int)Math.Round(Math.Clamp(_rng.NextGaussian(10.5, 3.2), 1, 20));
        h.Professionalism = Gen(); h.Ambition = Gen(); h.Loyalty = Gen(); h.Temperament = Gen();
        h.Pressure = Gen(); h.Adaptability = Gen(); h.Consistency = Gen(); h.BigGames = Gen();
        h.InjuryProneness = Gen(); h.Ego = Gen();
    }

    private void FillTendencies(Player p)
    {
        var a = p.Attributes;
        var t = p.Tendencies;
        // Shot diet follows skill: good shooters shoot more threes, bigs post more, etc.
        t.ThreePointRate = Math.Clamp((int)Math.Round((a[Attr.ThreePoint] - 8) * 6 + _rng.NextGaussian(20, 8)), 0, 80);
        t.PostUpRate = Math.Clamp((int)Math.Round((a[Attr.PostMoves] - 10) * 4 + _rng.NextGaussian(10, 6)), 0, 60);
        t.DriveRate = Math.Clamp((int)Math.Round((a[Attr.Speed] + a[Attr.BallHandling] - 20) * 2 + _rng.NextGaussian(30, 8)), 5, 70);
        t.IsolationRate = Math.Clamp((int)Math.Round((a[Attr.ShotCreation] - 10) * 3 + _rng.NextGaussian(15, 6)), 0, 50);
        t.PassFirst = Math.Clamp((int)Math.Round((a[Attr.Passing] - a[Attr.ShotCreation]) * 4 + _rng.NextGaussian(50, 12)), 10, 90);
        t.CrashGlass = Math.Clamp((int)Math.Round((a[Attr.OffensiveRebounding] - 10) * 4 + _rng.NextGaussian(40, 10)), 5, 90);
        t.GambleSteals = Math.Clamp((int)Math.Round((a[Attr.Steal] - 10) * 3 + _rng.NextGaussian(30, 10)), 5, 80);
    }
}
