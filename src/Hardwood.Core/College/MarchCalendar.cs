namespace Hardwood.Core.College;

/// <summary>
/// K39 (run 204): March on the real weekdays. The Final Four is the first Saturday of April and the final the Monday after;
/// Selection Sunday is three weeks before the Final Four. The conference tournaments run Friday to Sunday into Selection
/// Sunday (their finals that afternoon); the First Four Tuesday and Wednesday; the Round of 64 Thursday and Friday; the Round
/// of 32 Saturday and Sunday; the Sweet 16 Thursday and Friday; the Elite Eight Saturday and Sunday. The NIT runs Tuesday to
/// Thursday of Final Four week. A season started before v0.192.0 (<see cref="CollegeSeason.Weekdays"/> false) keeps the old
/// fixed day numbers, so a save made mid-season plays on as it was scheduled.
/// </summary>
public static class MarchCalendar
{
    /// <summary>The national semi-finals: the first Saturday of April in the spring of the season that began in <paramref name="seasonYear"/>.</summary>
    public static DateOnly FinalFour(int seasonYear)
    {
        var d = new DateOnly(seasonYear + 1, 4, 1);
        while (d.DayOfWeek != DayOfWeek.Saturday) d = d.AddDays(1);
        return d;
    }

    /// <summary>Selection Sunday: three weeks before the Final Four (between Mar 12 and Mar 18).</summary>
    public static DateOnly SelectionSunday(int seasonYear) => FinalFour(seasonYear).AddDays(-20);

    /// <summary>The conference tournaments' three days (quarter-finals, semi-finals, final).</summary>
    public static List<DateOnly> ConferenceTournament(int seasonYear, bool weekdays)
    {
        if (!weekdays) return new() { new(seasonYear + 1, 3, 10), new(seasonYear + 1, 3, 11), new(seasonYear + 1, 3, 12) };
        var s = SelectionSunday(seasonYear);
        return new() { s.AddDays(-2), s.AddDays(-1), s };
    }

    /// <summary>March Madness by round: the First Four, the Round of 64, 32, the Sweet 16, the Elite Eight, the semi-finals and the
    /// final. Rounds played over two days list their first day (<see cref="SecondDay"/>).</summary>
    public static List<DateOnly> Madness(int seasonYear, bool weekdays)
    {
        int y = seasonYear + 1;
        if (!weekdays) return new() { new(y, 3, 17), new(y, 3, 19), new(y, 3, 21), new(y, 3, 26), new(y, 3, 28), new(y, 4, 4), new(y, 4, 6) };
        var s = SelectionSunday(seasonYear);
        var f = FinalFour(seasonYear);
        return new() { s.AddDays(2), s.AddDays(4), s.AddDays(6), s.AddDays(11), s.AddDays(13), f, f.AddDays(2) };
    }

    /// <summary>Whether a March Madness round is split over two days (the First Four to the Elite Eight; before v0.192.0 only the Round of 64).</summary>
    public static bool SplitRound(int round, bool weekdays) => weekdays ? round is >= 0 and <= 4 : round == 1;

    /// <summary>The day a game in a split round is played: its slot's half of the bracket (games <paramref name="index"/> of
    /// <paramref name="count"/>) on the first day, the other half the day after.</summary>
    public static DateOnly SecondDay(DateOnly first, int index, int count) => index < count / 2 ? first : first.AddDays(1);

    /// <summary>The NIT: the first round (two days), the second round, the quarter-finals, the semi-finals and the final.</summary>
    public static List<DateOnly> Nit(int seasonYear, bool weekdays)
    {
        int y = seasonYear + 1;
        if (!weekdays) return new() { new(y, 3, 18), new(y, 3, 22), new(y, 3, 25), new(y, 4, 1), new(y, 4, 3) };
        var s = SelectionSunday(seasonYear);
        return new() { s.AddDays(2), s.AddDays(6), s.AddDays(9), s.AddDays(16), s.AddDays(18) };
    }

    /// <summary>The All-American games: the day the rosters are named and the day each is played. The high-school Classic on
    /// the Tuesday of Final Four week; the seniors' game on the Friday, at the Final Four.</summary>
    public static (DateOnly Named, DateOnly Played) AllAmerican(bool highSchool, int seasonYear, bool weekdays)
    {
        int y = seasonYear + 1;
        if (!weekdays) return highSchool ? (new(y, 2, 1), new(y, 3, 31)) : (new(y, 3, 30), new(y, 4, 3));
        var f = FinalFour(seasonYear);
        return highSchool ? (new(y, 2, 1), f.AddDays(-4)) : (f.AddDays(-4), f.AddDays(-1));
    }
}
