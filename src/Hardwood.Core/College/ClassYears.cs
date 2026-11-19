using Hardwood.Core.Model;

namespace Hardwood.Core.College;

/// <summary>A college player's class year (CS3, run 127): the short label for cards and tables and the long one for pages.</summary>
public sealed record ClassYear(int Year, string Short, string Long, bool Final);

/// <summary>
/// CS3 (run 127, the college plan): the years a fan can see. Players enroll at 18 and turn pro at 23 (#162), so a program
/// carries five age groups. With no redshirts (Danny, #938) the oldest group is shown as fifth-year seniors, in their final
/// season; whether college should be four years instead is D80 (`claude/for-danny.md`).
/// </summary>
public sealed partial class CollegeWorld
{
    public static readonly string[] ClassShort = { "Fr", "So", "Jr", "Sr", "5th" };
    public static readonly string[] ClassLong = { "Freshman", "Sophomore", "Junior", "Senior", "Fifth-year senior" };

    /// <summary>The summer the season of <paramref name="seasonYear"/> ends on: graduation and enrollment (July 1).</summary>
    public static DateOnly SummerAfter(int seasonYear) => new(seasonYear + 1, 7, 1);

    /// <summary>A player's class year in the season that began in <paramref name="seasonYear"/>, from his age the next summer
    /// (19 = freshman ... 23 = his final season). Null for anyone outside college age.</summary>
    public static ClassYear? ClassYearOf(Player p, int seasonYear)
    {
        int ageNext = p.Age(SummerAfter(seasonYear));
        int k = ageNext - (EnrollAge + 1);
        if (k < 0) return null;
        k = Math.Min(k, ClassShort.Length - 1);
        return new ClassYear(k + 1, ClassShort[k], ClassLong[k], ageNext >= GraduateAge);
    }

    /// <summary>The school year a date falls in (it starts on July 1, when classes graduate and enroll).</summary>
    public static int SchoolYear(DateOnly today) => today.Month >= 7 ? today.Year : today.Year - 1;

    /// <summary>A rostered college player's class year on a date (null for a high-schooler or anyone not in college).</summary>
    public ClassYear? ClassYearOf(Player p, DateOnly today) =>
        p.TeamId is { } id && Program(id) != null
            // Between the summer's enrollment and July 1 an incoming freshman is already on the roster: count him in next year.
            ? ClassYearOf(p, SchoolYear(today)) ?? ClassYearOf(p, SchoolYear(today) + 1)
            : null;

    /// <summary>Players in their final season (they turn pro next summer).</summary>
    public List<Player> FinalYear(Team t) =>
        t.Roster.Where(p => p.Age(SummerAfter(Season.Year)) >= GraduateAge).OrderByDescending(p => p.CurrentAbility).ToList();

    /// <summary>Senior Night (CS3): the program's last regular-season home game this season, if it has one left.</summary>
    public CollegeGame? LastHomeGame(int teamId) =>
        Season.Games.Where(g => g.HomeId == teamId && g.Event == null && IsRegularSeason(g)).OrderBy(g => g.Date).LastOrDefault();

    /// <summary>The preseason conference poll (CS3): the conference's programs by preseason rating, best first.</summary>
    public List<Team> ConferencePoll(string key) =>
        InConference(key).OrderByDescending(t => Season.Elo.GetValueOrDefault(t.Id, 1500)).ThenBy(t => t.Id).ToList();

    /// <summary>The first game of the season (the preseason ends the day before).</summary>
    public DateOnly? FirstGameDay => Season.Games.Count == 0 ? null : Season.Games.Min(g => g.Date);
}
