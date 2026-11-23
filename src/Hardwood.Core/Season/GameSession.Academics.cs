using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// CS7 part 2c (run 175, #1121): your players' grades. A midterm report flags who is at risk; study hall (a recruiting
/// hour a week each) or the academic-support center (booster money) gets him through; a player under the mark at the
/// grades sits out (the fall's until mid-February, the spring's until December). Every message goes out on one exact
/// day, and nothing draws a random number.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>A college head coach looks after his players' grades.</summary>
    public bool HasAcademics => HasProgramBuilding;

    /// <summary>The term under way (null in the summer).</summary>
    public (bool Fall, int Year)? AcademicTerm => Academics.TermOf(Today);

    /// <summary>The books academics reads (not seeded here, so reading grades never changes the program's facilities).</summary>
    private ProgramBooks AcademicBooks(Team t) => t.Money.Program;

    private bool AcademicFreshman(Player p) => World.College?.ClassYearOf(p, Today) is { Year: 1 };

    /// <summary>A player's projected GPA this term and his standing (null in the summer or outside college).</summary>
    public (double Projected, Academics.Standing Standing)? AcademicOutlook(Player p)
    {
        if (!HasAcademics || AcademicTerm is not { } term || p.TeamId != HumanTeam.Id) return null;
        double proj = Academics.Projected(p, AcademicBooks(HumanTeam), term, AcademicFreshman(p));
        return (proj, Academics.StandingOf(proj));
    }

    /// <summary>The players the spring's grades don't count for: seniors in their final term graduate in May.</summary>
    private bool GradesCount(Player p, (bool Fall, int Year) term) =>
        term.Fall || World.College?.ClassYearOf(p, Today) is not { Final: true };

    /// <summary>Your players whose grades count this term, worst first, with their outlook.</summary>
    public List<(Player Player, double Projected, Academics.Standing Standing, bool StudyHall)> AcademicRoster()
    {
        var list = new List<(Player, double, Academics.Standing, bool)>();
        if (!HasAcademics || AcademicTerm is not { } term) return list;
        var b = AcademicBooks(HumanTeam);
        foreach (var p in HumanTeam.Roster.Where(p => GradesCount(p, term)))
        {
            double proj = Academics.Projected(p, b, term, AcademicFreshman(p));
            list.Add((p, proj, Academics.StandingOf(proj), b.StudyHall.Contains(p.Id)));
        }
        return list.OrderBy(x => x.Item2).ThenBy(x => x.Item1.Id).ToList();
    }

    /// <summary>Put a player in study hall, or take him out. It costs the coaches <see cref="Academics.StudyHallHours"/> of
    /// recruiting time a week while he's there (this week's comes off at once).</summary>
    public string ToggleStudyHall(Player p)
    {
        if (!HasAcademics || p.TeamId != HumanTeam.Id) return "Only your own players go to study hall.";
        if (AcademicTerm is null) return "It's the summer: there are no classes until the fall.";
        var b = MyProgramBooks;
        string line;
        if (b.StudyHall.Remove(p.Id)) line = $"{p.Name} is out of study hall: the coaches get {Academics.StudyHallHours} recruiting hour a week back from next Monday.";
        else
        {
            b.StudyHall.Add(p.Id);
            var c = World.College!;
            int left = c.HoursLeft(Today);
            c.TrailHours = Math.Max(0, left - Academics.StudyHallHours);
            line = $"{p.Name} is in study hall (+{Academics.StudyHallLift:0.0} on his grades): it takes {Academics.StudyHallHours} of the coaches' recruiting hours a week.";
        }
        SyncAcademics();
        SaveNow();
        return line;
    }

    /// <summary>Pay for the next academic-support center level from the booster fund.</summary>
    public string BuildAcademicCenter()
    {
        if (!HasAcademics) return "Only a college program builds with booster money.";
        string line = Academics.BuildCenter(MyProgramBooks);
        SaveNow();
        return line;
    }

    /// <summary>Copy the books' ineligible list onto the players (on load and after the grades), and hold the study-hall
    /// hours out of the coaches' week.</summary>
    internal void SyncAcademics()
    {
        if (World.College is not { } c) return;
        foreach (var t in c.Programs)
        {
            var b = t.Money.Program;
            foreach (var p in t.Roster) p.IneligibleUntil = b.Ineligible.TryGetValue(p.Id, out var until) ? until : null;
        }
        c.HoursHeld = HasAcademics ? AcademicBooks(HumanTeam).StudyHall.Count(id => HumanTeam.Roster.Any(p => p.Id == id)) * Academics.StudyHallHours : 0;
    }

    /// <summary>The midterm reports and the grades (from the college calendar).</summary>
    private void AcademicCalendar(CollegeWorld c)
    {
        if (!HasAcademics) return;
        int y = c.Season.Year;
        if (Today == Academics.FallMidterm(y) || Today == Academics.SpringMidterm(y)) MidtermReport();
        else if (Today == Academics.FallGrades(y) || Today == Academics.SpringGrades(y)) TermGrades();
    }

    private void MidtermReport()
    {
        if (AcademicTerm is not { } term) return;
        var roster = AcademicRoster();
        var flagged = roster.Where(x => x.Standing != Academics.Standing.Fine).ToList();
        string name = Academics.TermName(term);
        var due = term.Fall ? Academics.FallGrades(term.Year) : Academics.SpringGrades(term.Year);
        var back = term.Fall ? Academics.FallBack(term.Year) : Academics.SpringBack(term.Year);
        if (flagged.Count == 0)
        {
            Post($"Midterm grades: everyone on track for the {name} term",
                $"The academic advisers' midterm report: every player is on track to stay eligible (the lowest projection is {roster.FirstOrDefault().Projected:0.00}; the mark is {Academics.PassMark:0.0}). Grades come out on {due:MMM d}.", "Roster");
            return;
        }
        string Line((Player Player, double Projected, Academics.Standing Standing, bool StudyHall) x) =>
            $"{x.Player.Name} ({x.Player.PrimaryPosition}): projected {x.Projected:0.00}, {Academics.Word(x.Standing)}" + (x.StudyHall ? ", in study hall" : "");
        Post($"Midterm grades: {flagged.Count} player{(flagged.Count == 1 ? "" : "s")} at risk of being ineligible",
            $"The academic advisers' midterm report for the {name} term: " + string.Join("; ", flagged.Select(Line)) + ". "
            + $"A player under {Academics.PassMark:0.0} when the grades come out on {due:MMM d} sits out until {back:MMM d}."
            + $"\n\nStudy hall lifts a player's grades by {Academics.StudyHallLift:0.0} (enough for anyone at risk) and takes {Academics.StudyHallHours} of the coaches' recruiting hours a week. "
            + "Put players in it on the Board page, where the academic-support center can also be built with booster money.", "Board");
    }

    private void TermGrades()
    {
        if (AcademicTerm is not { } term) return;
        var b = AcademicBooks(HumanTeam);
        var back = term.Fall ? Academics.FallBack(term.Year) : Academics.SpringBack(term.Year);
        var failed = new List<(Player, double)>();
        var grades = new Dictionary<int, double>();
        foreach (var p in HumanTeam.Roster.Where(p => GradesCount(p, term)).OrderBy(p => p.Id))
        {
            double gpa = Academics.Grades(p, b, term, AcademicFreshman(p));
            grades[p.Id] = gpa;
            if (gpa < Academics.PassMark) { failed.Add((p, gpa)); b.Ineligible[p.Id] = back; }
        }
        // Players who have left the program, and sentences served, drop off the list.
        var onRoster = HumanTeam.Roster.Select(p => p.Id).ToHashSet();
        foreach (var id in b.Ineligible.Where(kv => !onRoster.Contains(kv.Key) || kv.Value <= Today).Select(kv => kv.Key).ToList()) b.Ineligible.Remove(id);
        int helped = b.StudyHall.Count(id => grades.TryGetValue(id, out var g) && g >= Academics.PassMark && g - Academics.StudyHallLift < Academics.PassMark);
        b.StudyHall.Clear();
        b.LastGrades = grades; b.LastTerm = Academics.TermName(term);
        SyncAcademics();
        string name = Academics.TermName(term);
        double avg = grades.Count > 0 ? grades.Values.Average() : 0;
        string team = $"The team's average is {avg:0.00}" + (helped > 0 ? $"; study hall got {helped} player{(helped == 1 ? "" : "s")} through who would have failed" : "") + ".";
        if (failed.Count == 0)
        {
            Post($"Grades: everyone eligible after the {name} term", team + " Study hall is cleared for the new term.", "Roster");
            return;
        }
        string who = string.Join(", ", failed.Select(x => $"{x.Item1.Name} ({x.Item1.PrimaryPosition}, {x.Item2:0.00})"));
        Post($"Grades: {failed[0].Item1.Name}{(failed.Count > 1 ? $" and {failed.Count - 1} more" : "")} academically ineligible",
            $"{who} {(failed.Count == 1 ? "is" : "are")} under {Academics.PassMark:0.0} for the {name} term and can't play until {back:MMM d}. "
            + team + (term.Fall ? $" {(failed.Count == 1 ? "He misses" : "They miss")} the start of conference play." : $" The new season starts without {(failed.Count == 1 ? "him" : "them")}.")
            + " An ineligible player's mood suffers; study hall next term keeps it from happening again.", "Roster");
    }
}
