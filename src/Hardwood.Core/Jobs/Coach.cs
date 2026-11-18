namespace Hardwood.Core.Jobs;

public enum CoachBackground { None, CollegeStar, ExPro, ExAllStar }
public enum CoachLicence { C, B, A, Pro }
public enum CoachAttr { Offence, Defence, Tactics, Motivation, ManManagement, Development, Discipline }

/// <summary>
/// You (decision #70): an FM-style coach profile. Seven coaching attributes (1-20) bought from a points budget that
/// grows with your playing background and coaching licence; reputation (0-100) opens doors when you look for work.
/// Effects are deliberately small: a great coach tilts games, he doesn't win them alone.
/// </summary>
public sealed class Coach
{
    public string FirstName { get; set; } = "Alex";
    public string LastName { get; set; } = "Morgan";
    public string Name => $"{FirstName} {LastName}";
    public string Nationality { get; set; } = "USA";
    public DateOnly BirthDate { get; set; } = new(1984, 5, 1);
    public CoachBackground Background { get; set; }
    public CoachLicence Licence { get; set; } = CoachLicence.B;
    public Dictionary<CoachAttr, int> Attributes { get; set; } = Enum.GetValues<CoachAttr>().ToDictionary(a => a, _ => 10);
    /// <summary>0-100: how the basketball world rates you. Grows with wins, playoff runs and titles; drops when you're sacked.</summary>
    public double Reputation { get; set; } = 30;
    /// <summary>Jobs held so far (team id, from, to, record, why it ended).</summary>
    public List<JobRecord> History { get; set; } = new();
    /// <summary>This season's work, for growth at the end of it (staff part 2): talks held, press and locker-room moments answered yourself.</summary>
    public int SeasonTalks { get; set; }
    public int SeasonAnswers { get; set; }

    public int this[CoachAttr a] => Attributes.TryGetValue(a, out var v) ? v : 10;
    public int Age(DateOnly on) => on.Year - BirthDate.Year - (on < BirthDate.AddYears(on.Year - BirthDate.Year) ? 1 : 0);

    public static int Budget(CoachBackground b, CoachLicence l) => 70 + b switch
    {
        CoachBackground.CollegeStar => 3, CoachBackground.ExPro => 6, CoachBackground.ExAllStar => 9, _ => 0,
    } + l switch { CoachLicence.B => 4, CoachLicence.A => 8, CoachLicence.Pro => 12, _ => 0 };

    public static double StartingReputation(CoachBackground b, CoachLicence l) => 15 + (int)b * 6 + (int)l * 5;

    public int PointsUsed => Attributes.Values.Sum();
    public int PointsLeft => Budget(Background, Licence) - PointsUsed;
    public const int MinAttr = 5, MaxAttr = 20;

    public static string Label(CoachAttr a) => a switch
    {
        CoachAttr.ManManagement => "Man management",
        _ => a.ToString(),
    };

    public static string Describe(CoachAttr a) => a switch
    {
        CoachAttr.Offence => "Your teams shoot a little better.",
        CoachAttr.Defence => "Your teams contest shots a little harder.",
        CoachAttr.Tactics => "Systems and schemes that suit your players pay off more.",
        CoachAttr.Motivation => "Players are a little happier and play for you.",
        CoachAttr.ManManagement => "One-on-one talks go better.",
        CoachAttr.Development => "Young players improve faster.",
        _ => "Fewer fouls and steadier team chemistry.",
    };

    // --- Effects (neutral at 10) -------------------------------------------------------------------
    public double ShotLogit => (this[CoachAttr.Offence] - 10) * 0.003;
    public double ContestBonus => (this[CoachAttr.Defence] - 10) * 0.05;
    public double FitMultiplier => 1 + (this[CoachAttr.Tactics] - 10) / 20.0;
    public double MoraleBonus => (this[CoachAttr.Motivation] - 10) * 0.4;
    public double TalkMultiplier(bool positive) => 1 + (this[CoachAttr.ManManagement] - 10) / 25.0 * (positive ? 1 : -1);
    public double TrainingBonus => (this[CoachAttr.Development] - 10) * 0.3;
    public double FoulMultiplier => 1 - (this[CoachAttr.Discipline] - 10) * 0.01;
    public double ChemistryBonus => (this[CoachAttr.Discipline] - 10) * 0.3;
}

public sealed record JobRecord(int TeamId, string TeamName, int FromSeason, int ToSeason, int Wins, int Losses, string Ended);

/// <summary>
/// Bug hunt H19 (run 93): a job's seasons as the game labels them everywhere else. A record's seasons are start years, so
/// "2026-2028" read as two seasons ending in 2028 when the last one was 2028-29; now "2026-27 to 2028-29".
/// A static helper rather than a property, so saves keep exactly the fields they had.
/// </summary>
public static class JobRecords
{
    public static string Season(int year) => $"{year}-{(year + 1) % 100:00}";
    public static string Seasons(JobRecord h) => h.FromSeason >= h.ToSeason ? Season(h.ToSeason) : $"{Season(h.FromSeason)} to {Season(h.ToSeason)}";
    /// <summary>"Anchorage Steelheads, 2026-27 to 2028-29: sacked (10-52 in 2028-29)". H92 (run 106): the season is named,
    /// since "in the last season" read as the season before when he was sacked mid-season ("sacked at 6-20 in 2026-27").</summary>
    public static string Line(JobRecord h) => h.FromSeason >= h.ToSeason
        ? $"{h.TeamName}, {Season(h.ToSeason)}: {Lower(h.Ended)} at {h.Wins}-{h.Losses}"
        : $"{h.TeamName}, {Seasons(h)}: {Lower(h.Ended)} ({h.Wins}-{h.Losses} in {Season(h.ToSeason)})";

    /// <summary>"Left for Boston Celtics" → "left for Boston Celtics" (the club's name keeps its capitals).</summary>
    private static string Lower(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
