namespace Hardwood.Core.Model;

/// <summary>
/// The 36 visible player attributes, each on a 1–20 scale (Football Manager convention).
/// Order matters: it is the storage order in <see cref="AttributeSet"/>.
/// </summary>
public enum Attr
{
    // Shooting
    CloseShot, Layup, Dunk, MidRange, ThreePoint, FreeThrow, ShotCreation, CatchAndShoot,
    // Playmaking
    BallHandling, Passing, CourtVision, PickAndRollHandler, PostMoves,
    // Defense
    PerimeterDefense, InteriorDefense, HelpDefense, Steal, Block, DefensiveRebounding, OffensiveRebounding,
    // Mental
    BasketballIQ, Decisions, Composure, WorkRate, Leadership, Teamwork, Discipline, OffBallMovement,
    // Physical
    Speed, Acceleration, Strength, Vertical, Stamina, Agility, NaturalFitness, Durability,
}

public enum AttrGroup { Shooting, Playmaking, Defense, Mental, Physical }

public static class AttrInfo
{
    public const int Count = 36;

    public static AttrGroup GroupOf(Attr a) => (int)a switch
    {
        <= (int)Attr.CatchAndShoot => AttrGroup.Shooting,
        <= (int)Attr.PostMoves => AttrGroup.Playmaking,
        <= (int)Attr.OffensiveRebounding => AttrGroup.Defense,
        <= (int)Attr.OffBallMovement => AttrGroup.Mental,
        _ => AttrGroup.Physical,
    };

    public static IEnumerable<Attr> All => Enum.GetValues<Attr>();
}

/// <summary>Fixed-size, value-type storage for the 36 attributes.</summary>
public sealed class AttributeSet
{
    private readonly byte[] _values = new byte[AttrInfo.Count];

    public int this[Attr a]
    {
        get => _values[(int)a];
        set => _values[(int)a] = (byte)Math.Clamp(value, 1, 20);
    }

    public AttributeSet Clone()
    {
        var c = new AttributeSet();
        Array.Copy(_values, c._values, AttrInfo.Count);
        return c;
    }

    public double Average(params Attr[] attrs) => attrs.Length == 0 ? 0 : attrs.Average(a => (double)this[a]);

    public double Average(AttrGroup g) =>
        AttrInfo.All.Where(a => AttrInfo.GroupOf(a) == g).Average(a => (double)this[a]);
}
