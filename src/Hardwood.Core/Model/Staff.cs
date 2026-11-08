namespace Hardwood.Core.Model;

public enum StaffRole { AssistantCoach, DevelopmentCoach, StrengthCoach, Physio, Scout }

/// <summary>A member of the coaching, medical or scouting staff. Attributes 1–20; only the ones for the role matter much.</summary>
public sealed class Staff
{
    public int Id { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public string Name => $"{FirstName} {LastName}";
    public DateOnly BirthDate { get; init; }
    public StaffRole Role { get; set; }

    /// <summary>Tactical and in-game coaching; also feeds development.</summary>
    public int Coaching { get; set; } = 10;
    /// <summary>Teaching young players: the main driver of growth.</summary>
    public int Development { get; set; } = 10;
    /// <summary>Conditioning: lowers injury risk, slows physical decline.</summary>
    public int Fitness { get; set; } = 10;
    /// <summary>Medical: shortens injuries.</summary>
    public int Medical { get; set; } = 10;
    /// <summary>Judging players: narrows the fog on other teams' players and prospects.</summary>
    public int Scouting { get; set; } = 10;
    public int Reputation { get; set; } = 10;

    public int? TeamId { get; set; }
    public long Salary { get; set; }
    public int ContractYears { get; set; }

    public int Age(DateOnly on)
    {
        var age = on.Year - BirthDate.Year;
        if (on < BirthDate.AddYears(age)) age--;
        return age;
    }

    /// <summary>The attribute that defines the role.</summary>
    public int KeyAttribute => Role switch
    {
        StaffRole.AssistantCoach => Coaching,
        StaffRole.DevelopmentCoach => Development,
        StaffRole.StrengthCoach => Fitness,
        StaffRole.Physio => Medical,
        _ => Scouting,
    };

    public static string RoleName(StaffRole r) => r switch
    {
        StaffRole.AssistantCoach => "Assistant coach",
        StaffRole.DevelopmentCoach => "Development coach",
        StaffRole.StrengthCoach => "Strength & conditioning",
        StaffRole.Physio => "Physio",
        _ => "Head scout",
    };

    public override string ToString() => $"{Name} ({RoleName(Role)}, {KeyAttribute})";
}

/// <summary>Weekly training setup for a team.</summary>
public sealed class TrainingPlan
{
    /// <summary>0 = light, 100 = brutal. Growth and injuries both scale with it.</summary>
    public int Intensity { get; set; } = 50;
    /// <summary>Attribute group each player works on; missing = balanced.</summary>
    public Dictionary<int, AttrGroup> Focus { get; set; } = new();
}

/// <summary>Convenience views over a team's staff, with sensible defaults when a role is empty.</summary>
public static class StaffEffects
{
    private static Staff? Find(Team t, StaffRole role) => t.Staff.FirstOrDefault(s => s.Role == role);

    /// <summary>Training quality 1–20 fed into development (10 with no staff).</summary>
    public static double TrainingQuality(Team t)
    {
        var dev = Find(t, StaffRole.DevelopmentCoach);
        var asst = Find(t, StaffRole.AssistantCoach);
        double q = ((dev?.Development ?? 7) * 0.7 + (asst?.Coaching ?? 7) * 0.3);
        return q + (t.Training.Intensity - 50) / 50.0 * 2.0; // intensity shifts it ±2
    }

    /// <summary>Injury probability multiplier.</summary>
    public static double InjuryRiskFactor(Team t)
    {
        var sc = Find(t, StaffRole.StrengthCoach);
        double fit = sc?.Fitness ?? 7;
        return (1.25 - fit / 20.0 * 0.5) * (0.85 + t.Training.Intensity / 330.0);
    }

    /// <summary>Injury duration multiplier.</summary>
    public static double RecoveryFactor(Team t)
    {
        var ph = Find(t, StaffRole.Physio);
        double med = ph?.Medical ?? 7;
        return 1.25 - med / 20.0 * 0.5;
    }

    /// <summary>How much of the truth the team sees about outsiders: 0.3 (no scout) to 0.95.</summary>
    public static double ScoutingKnowledge(Team t)
    {
        var sc = Find(t, StaffRole.Scout);
        double judge = sc?.Scouting ?? 4;
        return Math.Clamp(0.3 + judge / 20.0 * 0.65, 0.3, 0.95);
    }
}
