namespace Hardwood.Core.Model;

public enum Position { PG, SG, SF, PF, C }

/// <summary>Hidden personality and consistency traits, 1–20.</summary>
public sealed class HiddenTraits
{
    public int Professionalism { get; set; } = 10;
    public int Ambition { get; set; } = 10;
    public int Loyalty { get; set; } = 10;
    public int Temperament { get; set; } = 10;
    public int Pressure { get; set; } = 10;
    public int Adaptability { get; set; } = 10;
    public int Consistency { get; set; } = 10;
    public int BigGames { get; set; } = 10;
    public int InjuryProneness { get; set; } = 10;
    public int Ego { get; set; } = 10;
}

/// <summary>Shot-diet and behaviour tendencies, 0–100.</summary>
public sealed class Tendencies
{
    public int ThreePointRate { get; set; } = 35;
    public int DriveRate { get; set; } = 30;
    public int PostUpRate { get; set; } = 10;
    public int IsolationRate { get; set; } = 15;
    public int PassFirst { get; set; } = 50;
    public int CrashGlass { get; set; } = 40;
    public int GambleSteals { get; set; } = 30;
}

public sealed class Player
{
    public int Id { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string Name => $"{FirstName} {LastName}";

    public DateOnly BirthDate { get; init; }
    public string Nationality { get; init; } = "USA";
    /// <summary>Appearance group; picks the portrait face folder.</summary>
    public Ethnicity Ethnicity { get; set; } = Ethnicity.African;
    /// <summary>Portrait face, relative to the face pack root (e.g. "African/African123.png"); chosen once, never re-picked. Null until assigned.</summary>
    public string? FaceFile { get; set; }

    /// <summary>Draft the player entered the league through; null if undrafted or generated already in the league.</summary>
    public DraftRecord? Draft { get; set; }
    /// <summary>NABL team holding this player's draft rights while he plays abroad ("stashed"); null otherwise.</summary>
    public int? DraftRightsTeamId { get; set; }

    /// <summary>Height in centimetres.</summary>
    public int HeightCm { get; init; }
    /// <summary>Wingspan in centimetres.</summary>
    public int WingspanCm { get; init; }
    public int WeightKg { get; init; }

    public Position PrimaryPosition { get; set; }
    public Position? SecondaryPosition { get; set; }

    /// <summary>Current Ability 1–200.</summary>
    public int CurrentAbility { get; set; }
    /// <summary>Potential Ability 1–200.</summary>
    public int PotentialAbility { get; set; }

    public AttributeSet Attributes { get; init; } = new();
    public HiddenTraits Hidden { get; init; } = new();
    public Tendencies Tendencies { get; init; } = new();

    public int? TeamId { get; set; }
    public Contract? Contract { get; set; }

    /// <summary>First season (start year) the player was eligible as a rookie; null for players generated already in the league.</summary>
    public int? RookieSeason { get; set; }

    public Injury? Injury { get; set; }
    public List<Injury> InjuryHistory { get; } = new();
    public bool IsAvailable(DateOnly on) => Injury == null || !Injury.IsOut(on);

    public int Age(DateOnly on)
    {
        var age = on.Year - BirthDate.Year;
        if (on < BirthDate.AddYears(age)) age--;
        return age;
    }

    /// <summary>A rough overall rating (1–20) used for ordering rotations in the prototype.</summary>
    public double Overall => Attributes.Average(AttrGroup.Shooting) * 0.28
                           + Attributes.Average(AttrGroup.Playmaking) * 0.17
                           + Attributes.Average(AttrGroup.Defense) * 0.25
                           + Attributes.Average(AttrGroup.Mental) * 0.15
                           + Attributes.Average(AttrGroup.Physical) * 0.15;

    public override string ToString() => $"{Name} ({PrimaryPosition}, {HeightCm}cm, CA {CurrentAbility}/{PotentialAbility})";
}

/// <summary>Draft year (the summer it happened), round and overall pick.</summary>
public sealed record DraftRecord(int Year, int Round, int Pick);

public sealed class Contract
{
    public int Years { get; set; }
    /// <summary>Annual salary in dollars.</summary>
    public long Salary { get; set; }
    public int StartSeason { get; set; }
    public bool TeamOption { get; set; }
    public bool PlayerOption { get; set; }
    /// <summary>Overseas contracts: the buyout an NABL team (plus the player) must pay to sign him before it ends. 0 = none.</summary>
    public long Buyout { get; set; }
}
