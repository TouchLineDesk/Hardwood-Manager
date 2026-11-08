namespace Hardwood.Core.Model;

public enum InjurySeverity { Minor, Moderate, Severe, SeasonEnding }

public sealed class Injury
{
    public required string Name { get; init; }
    public InjurySeverity Severity { get; init; }
    public DateOnly Occurred { get; init; }
    /// <summary>First day the player is available again.</summary>
    public DateOnly ReturnDate { get; init; }
    /// <summary>Permanent attribute damage already applied (for severe injuries), for display.</summary>
    public int AttributeLoss { get; init; }

    public int DaysOut(DateOnly today) => Math.Max(0, ReturnDate.DayNumber - today.DayNumber);
    public bool IsOut(DateOnly today) => today < ReturnDate;

    public override string ToString() => $"{Name} ({Severity})";
}

/// <summary>Catalogue of injury types with day ranges; drawn from with severity weights.</summary>
public static class InjuryCatalogue
{
    public sealed record Kind(string Name, InjurySeverity Severity, int MinDays, int MaxDays, double Weight, bool Physical);

    public static readonly Kind[] Kinds =
    {
        new("Ankle sprain", InjurySeverity.Minor, 2, 10, 22, false),
        new("Knee soreness", InjurySeverity.Minor, 1, 7, 14, false),
        new("Back spasms", InjurySeverity.Minor, 2, 8, 10, false),
        new("Illness", InjurySeverity.Minor, 1, 4, 10, false),
        new("Hamstring strain", InjurySeverity.Moderate, 10, 25, 12, false),
        new("Calf strain", InjurySeverity.Moderate, 8, 21, 8, false),
        new("Groin strain", InjurySeverity.Moderate, 7, 20, 7, false),
        new("Wrist sprain", InjurySeverity.Moderate, 7, 18, 5, false),
        new("Concussion", InjurySeverity.Moderate, 5, 14, 4, false),
        new("Broken finger", InjurySeverity.Moderate, 14, 30, 4, false),
        new("High ankle sprain", InjurySeverity.Severe, 28, 50, 4, false),
        new("Meniscus tear", InjurySeverity.Severe, 40, 75, 3, true),
        new("Broken hand", InjurySeverity.Severe, 35, 55, 2, false),
        new("Stress fracture (foot)", InjurySeverity.Severe, 45, 90, 2, true),
        new("ACL tear", InjurySeverity.SeasonEnding, 240, 330, 1.2, true),
        new("Achilles rupture", InjurySeverity.SeasonEnding, 270, 365, 0.6, true),
    };
}
