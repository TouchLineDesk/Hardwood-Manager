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

    /// <summary>
    /// His way of working (staff depth, roadmap 12): one of two styles per role, fixed for a person. Derived from his id so
    /// every career and older save has it without a save change.
    /// </summary>
    public string Style => Styles(Role)[(int)((uint)Id * 2654435761u >> 16) % 2].Name;
    public string StyleText => Styles(Role).First(x => x.Name == Style).Text;

    public static (string Name, string Text)[] Styles(StaffRole r) => r switch
    {
        StaffRole.AssistantCoach => new[] { ("Mentor", "Stays late with the kids: +0.3 training quality for players 23 and under, -0.2 for the rest."), ("Film junkie", "Breaks down opponents: +3% scouting knowledge.") },
        StaffRole.DevelopmentCoach => new[] { ("Teacher", "Lives for young players: +1 training quality for players 23 and under, -0.3 for the rest."), ("Craftsman", "Polishes finished players: +0.5 training quality for players 24 and over, -0.3 for the young.") },
        StaffRole.StrengthCoach => new[] { ("Conditioner", "Keeps legs fresh: injury risk ×0.95."), ("Builder", "Builds durable bodies: injury risk ×0.97 and recovery time ×0.97.") },
        StaffRole.Physio => new[] { ("Healer", "Gets players back sooner: recovery time ×0.93."), ("Preventer", "Catches problems early: injury risk ×0.96.") },
        _ => new[] { ("Analyst", "Reads the numbers: +5% scouting knowledge."), ("Networker", "Knows every college coach: +2 recruiting points a month.") },
    };

    public bool Is(string style) => Style == style;

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
        return q + (t.Training.Intensity - 50) / 50.0 * 2.0 // intensity shifts it ±2
               + (t.Coach?.TrainingBonus ?? 0)                  // your Development rating (decision #70)
               + 0.4 * t.Money.Facilities.Practice.Edge;        // the practice facility against the league's (item M, #281)
    }

    /// <summary>
    /// Training quality for one player: the team's, plus the coaching styles' tilt. Each style trades young players against
    /// established ones (Teacher and Mentor toward the young, Craftsman toward the rest), so styles shape a squad without
    /// inflating the league's talent.
    /// </summary>
    public static double TrainingQualityFor(Team t, Player p, DateOnly on)
    {
        bool young = p.Age(on) <= 23;
        double tilt = 0;
        var dev = Find(t, StaffRole.DevelopmentCoach);
        if (dev?.Is("Teacher") == true) tilt += young ? 1.0 : -0.3;
        if (dev?.Is("Craftsman") == true) tilt += young ? -0.3 : 0.5;
        if (Find(t, StaffRole.AssistantCoach)?.Is("Mentor") == true) tilt += young ? 0.3 : -0.2;
        return TrainingQuality(t) + tilt;
    }

    /// <summary>Facilities' training edge alone (clubs abroad have no staff model: they train at 10 plus this).</summary>
    public static double FacilityTraining(Team t, Player p, DateOnly on) => 0.4 * t.Money.Facilities.Practice.Edge;

    /// <summary>Injury probability multiplier.</summary>
    public static double InjuryRiskFactor(Team t)
    {
        var sc = Find(t, StaffRole.StrengthCoach);
        double fit = sc?.Fitness ?? 7;
        return (1.25 - fit / 20.0 * 0.5) * (0.85 + t.Training.Intensity / 330.0)
               * (sc?.Is("Conditioner") == true ? 0.95 : sc?.Is("Builder") == true ? 0.97 : 1) * (Find(t, StaffRole.Physio)?.Is("Preventer") == true ? 0.96 : 1)
               * Math.Clamp(1 - 0.05 * t.Money.Facilities.Medical.Edge, 0.8, 1.2); // sports science (item M, #281)
    }

    /// <summary>Injury duration multiplier.</summary>
    public static double RecoveryFactor(Team t)
    {
        var ph = Find(t, StaffRole.Physio);
        double med = ph?.Medical ?? 7;
        return (1.25 - med / 20.0 * 0.5) * (ph?.Is("Healer") == true ? 0.93 : 1) * (Find(t, StaffRole.StrengthCoach)?.Is("Builder") == true ? 0.97 : 1)
               * Math.Clamp(1 - 0.06 * t.Money.Facilities.Medical.Edge, 0.76, 1.24); // the medical centre (item M, #281)
    }

    /// <summary>How much of the truth the team sees about outsiders: 0.3 (no scout) to 0.95.</summary>
    public static double ScoutingKnowledge(Team t)
    {
        var sc = Find(t, StaffRole.Scout);
        double judge = sc?.Scouting ?? 4;
        double style = (sc?.Is("Analyst") == true ? 0.05 : 0) + (Find(t, StaffRole.AssistantCoach)?.Is("Film junkie") == true ? 0.03 : 0);
        return Math.Clamp(0.3 + judge / 20.0 * 0.65 + style + 0.03 * t.Money.Facilities.Analytics.Edge, 0.3, 0.97); // analytics (item M, #281)
    }
}
