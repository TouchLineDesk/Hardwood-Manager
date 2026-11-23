using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>
/// CS7 part 2c (run 175, #1121): academics as drama with a consequence, not a lottery. Each term every player on the
/// coached program has a grade-point average from his professionalism, the term's work and what the program gives him
/// (study hall, the academic-support center). Half of the term is known at the midterm report, and the other half can
/// only move him by <see cref="Spread"/>/2 at the grades, so a player who isn't flagged "at risk" can never fail, and a
/// flagged one put in study hall is safe. A player under <see cref="PassMark"/> at the grades is academically ineligible:
/// the fall's grades sit him out until mid-February (conference play), the spring's until December. No random number is
/// drawn (the term's work is a hash of the player and the term), and the record lives in <see cref="ProgramBooks"/>, so
/// no save schema changes. Computer programs have no academics, as with the rest of program building.
/// </summary>
public static class Academics
{
    /// <summary>The NCAA-style minimum to stay eligible.</summary>
    public const double PassMark = 2.0;
    /// <summary>An average player's GPA (professionalism 10.5) and the lift for each point of professionalism.</summary>
    public const double Base = 2.75, PerProfessionalism = 0.10;
    /// <summary>The term's work: ±Spread/2 known by the midterm, ±Spread/2 more at the grades.</summary>
    public const double Spread = 0.40;
    /// <summary>Study hall's lift (it costs <see cref="StudyHallHours"/> of the coaches' recruiting hours a week each).</summary>
    public const double StudyHallLift = 0.40;
    public const int StudyHallHours = 1;
    /// <summary>The academic-support center's lift per level (0 to <see cref="MaxCenter"/>), paid for by the boosters.</summary>
    public const double CenterLift = 0.12;
    public const int MaxCenter = 3;
    /// <summary>A freshman's first fall away from home.</summary>
    public const double FreshmanDip = 0.15;

    /// <summary>The next center level's price: $0.6M, $1.2M, $1.8M.</summary>
    public static long CenterCost(int level) => 600_000L * (level + 1);

    // The calendar of a season (its start year y).
    public static DateOnly FallMidterm(int y) => new(y, 11, 20);
    public static DateOnly FallGrades(int y) => new(y, 12, 20);
    public static DateOnly SpringMidterm(int y) => new(y + 1, 3, 2);
    public static DateOnly SpringGrades(int y) => new(y + 1, 5, 1);
    /// <summary>The first day a player who failed the fall (spring) term may play again.</summary>
    public static DateOnly FallBack(int y) => new(y + 1, 2, 14);
    public static DateOnly SpringBack(int y) => new(y + 1, 12, 1);

    /// <summary>The term a day falls in: the fall (to the fall grades) or the spring (to the spring grades); null in the summer.</summary>
    public static (bool Fall, int Year)? TermOf(DateOnly d)
    {
        int y = CollegeWorld.SchoolYear(d);
        if (d > SpringGrades(y)) return null;
        return (d <= FallGrades(y), y);
    }

    public static string TermName((bool Fall, int Year) t) => t.Fall ? $"fall {t.Year}" : $"spring {t.Year + 1}";
    private static string Key((bool Fall, int Year) t) => (t.Fall ? "F" : "S") + t.Year;

    /// <summary>The term's work, from -1 to 1 (part 0 is known at the midterm, part 1 only at the grades).</summary>
    public static double Work(int playerId, (bool Fall, int Year) term, int part) =>
        Presentation.FightSongs.Hash($"academics|{playerId}|{Key(term)}|{part}") / (double)uint.MaxValue * 2 - 1;

    /// <summary>What the midterm report shows: everything but the last half of the term's work.</summary>
    public static double Projected(Player p, ProgramBooks b, (bool Fall, int Year) term, bool freshman) =>
        Base + (p.Hidden.Professionalism - 10.5) * PerProfessionalism + Spread / 2 * Work(p.Id, term, 0)
        + (b.StudyHall.Contains(p.Id) ? StudyHallLift : 0) + CenterLift * b.AcademicCenter
        - (freshman && term.Fall ? FreshmanDip : 0);

    /// <summary>The term's grades.</summary>
    public static double Grades(Player p, ProgramBooks b, (bool Fall, int Year) term, bool freshman) =>
        Math.Round(Math.Clamp(Projected(p, b, term, freshman) + Spread / 2 * Work(p.Id, term, 1), 0, 4), 2);

    public enum Standing { Fine, AtRisk, Failing }

    /// <summary>Failing: under the mark even at the best grades; at risk: the last half of the term can take him under.</summary>
    public static Standing StandingOf(double projected) =>
        projected + Spread / 2 < PassMark ? Standing.Failing : projected - Spread / 2 < PassMark ? Standing.AtRisk : Standing.Fine;

    public static string Word(Standing s) => s switch { Standing.Failing => "failing", Standing.AtRisk => "at risk", _ => "on track" };

    /// <summary>Why the program can't build the next center level now (null: it can).</summary>
    public static string? CenterBlocked(ProgramBooks b)
    {
        if (b.AcademicCenter >= MaxCenter) return "The academic-support center is already the best it can be.";
        long cost = CenterCost(b.AcademicCenter);
        return b.Fund < cost ? $"The booster fund has {ProgramBuilding.Money(b.Fund)}; the next academic-center level costs {ProgramBuilding.Money(cost)}." : null;
    }

    /// <summary>Pay for the next center level: tutors and advisers start at once (it lifts the term already under way).</summary>
    public static string BuildCenter(ProgramBooks b)
    {
        if (CenterBlocked(b) is { } why) return why;
        long cost = CenterCost(b.AcademicCenter);
        b.Fund -= cost; b.AcademicCenter++;
        return $"Academic-support center level {b.AcademicCenter} is open ({ProgramBuilding.Money(cost)} from the booster fund): +{CenterLift * b.AcademicCenter:0.00} on every player's grades.";
    }
}
