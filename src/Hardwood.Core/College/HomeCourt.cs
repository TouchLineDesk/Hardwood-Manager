using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>
/// C5 part 2 (college-sim-design #165): a stronger home court than in the pros, varying by arena. Measured college home
/// edges sit around 3 to 3.5 points nationally (KenPom), with the loudest buildings near 4.5; the pros' is 2.5 in this
/// game. A program's edge here follows its basketball tradition (the student section a long history builds) and its
/// arena's size: 2.8 points for a small, young program up to 4.6 for a blue blood in a big house. No random numbers.
/// </summary>
public sealed partial class CollegeWorld
{
    public const double HomeCourtMin = 2.8, HomeCourtMax = 4.6;

    /// <summary>The home program's edge in points on its own floor (neutral-court events have none).</summary>
    public double HomeCourtPoints(int programId)
    {
        double tradition = Tradition.TryGetValue(programId, out var tr) ? tr : Prestige.GetValueOrDefault(programId, 40);
        double t = Math.Clamp((tradition - 20) / 70, 0, 1);
        double c = Program(programId) is { } team ? Math.Clamp((team.ArenaCapacity - 4000) / 14000.0, 0, 1) : 0.3;
        return Math.Round(HomeCourtMin + 1.1 * t + 0.7 * c, 1);
    }

    /// <summary>A real program's home arena by name, or null.</summary>
    public static string? ArenaOf(Team program) => RealWorld.RealColleges.ArenaOf(program.City);

    /// <summary>"Cameron Indoor Stadium · home court +4.4" (or "Home court +3.1" with no arena on file).</summary>
    public string HomeCourtLine(int programId)
    {
        var team = Program(programId);
        var pts = HomeCourtPoints(programId);
        var arena = team != null ? ArenaOf(team) : null;
        string grade = pts >= 4.2 ? "one of the loudest in the country" : pts >= 3.6 ? "a tough place to play" : "an ordinary home court";
        return arena != null ? $"{arena}: {grade} (+{pts:0.0} points to the home side)" : $"Home court: {grade} (+{pts:0.0} points)";
    }

    /// <summary>
    /// Listed rivals in different conferences (conference rivals already meet in conference play), the named series first:
    /// each pair is scheduled once a season in the non-conference rounds, home and away alternating by year.
    /// </summary>
    public List<Rivalry> NonConferenceRivalries() =>
        Rivalries.Values.Where(r => !r.InState && ConferenceOf.GetValueOrDefault(r.A) != ConferenceOf.GetValueOrDefault(r.B))
            .OrderBy(r => r.Name == null ? 1 : 0).ThenBy(r => r.A).ThenBy(r => r.B).ToList();
}
