namespace Hardwood.Core.International;

public enum FibaEventKind { Window, WorldCup, Olympics, OlympicQualifying, Continental }

/// <summary>One date on the FIBA calendar.</summary>
public sealed record FibaEvent(FibaEventKind Kind, string Name, DateOnly Start, DateOnly End, string Where, string Note)
{
    /// <summary>"Aug 27 – Sep 12, 2027".</summary>
    public string Dates => Start.Year == End.Year
        ? $"{Start.ToString("MMM d", Inv)} – {End.ToString("MMM d, yyyy", Inv)}"
        : $"{Start.ToString("MMM d, yyyy", Inv)} – {End.ToString("MMM d, yyyy", Inv)}";

    private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
}

/// <summary>
/// The real FIBA cycle (#106), international plan I1 (run 207): the World Cup every four years from 2027, the Olympics from 2028,
/// the continental championships (EuroBasket, AmeriCup, AfroBasket, the Asia Cup) in the odd years between, and the November and
/// February qualifying windows. Dates follow FIBA's pattern; the 2027 World Cup (Qatar) and the 2028 (Los Angeles) and 2032
/// (Brisbane) Olympics are the announced ones. Nothing here plays yet: it's the calendar later slices fill.
/// </summary>
public static class FibaCalendar
{
    /// <summary>Every event that ends on or after <paramref name="from"/> and starts within <paramref name="years"/> years of it.</summary>
    public static IReadOnlyList<FibaEvent> From(DateOnly from, int years = 3)
    {
        var until = from.AddYears(years);
        var all = new List<FibaEvent>();
        for (int y = from.Year - 1; y <= until.Year + 1; y++) all.AddRange(InYear(y));
        return all.Where(e => e.End >= from && e.Start <= until).OrderBy(e => e.Start).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>The next World Cup or continental championship starting after <paramref name="d"/>: what a window qualifies for.</summary>
    public static string NextMainEvent(DateOnly d)
    {
        for (int y = d.Year; y < d.Year + 5; y++)
        {
            if (IsWorldCupYear(y) && new DateOnly(y, 8, 27) > d) return $"the {y} World Cup";
            if (IsContinentalYear(y) && new DateOnly(y, 8, 8) > d) return $"the {y} continental championships";
        }
        return "the next tournament";
    }

    public static bool IsWorldCupYear(int y) => y >= 2027 && (y - 2027) % 4 == 0;
    public static bool IsOlympicYear(int y) => y >= 2028 && (y - 2028) % 4 == 0;
    public static bool IsContinentalYear(int y) => y >= 2029 && (y - 2029) % 4 == 0;

    public static IEnumerable<FibaEvent> InYear(int y)
    {
        // The qualifying windows: the last week of February and the last week of November.
        var feb = Monday(new DateOnly(y, 2, 23));
        yield return new FibaEvent(FibaEventKind.Window, "FIBA window", feb, feb.AddDays(8), "Home and away",
            "Qualifiers for " + NextMainEvent(feb) + "; club seasons carry on (#107)");
        var nov = Monday(new DateOnly(y, 11, 24));
        yield return new FibaEvent(FibaEventKind.Window, "FIBA window", nov, nov.AddDays(8), "Home and away",
            "Qualifiers for " + NextMainEvent(nov) + "; club seasons carry on (#107)");

        if (IsWorldCupYear(y))
        {
            bool known = y == 2027;
            var start = known ? new DateOnly(2027, 8, 27) : Friday(new DateOnly(y, 8, 26));
            yield return new FibaEvent(FibaEventKind.WorldCup, $"FIBA Basketball World Cup {y}", start, known ? new DateOnly(2027, 9, 12) : start.AddDays(16),
                known ? "Qatar" : "Host to be named", "32 teams; the best of each zone also earn Olympic places");
        }
        if (IsOlympicYear(y))
        {
            var oqt = Monday(new DateOnly(y, 7, 1)).AddDays(1);
            yield return new FibaEvent(FibaEventKind.OlympicQualifying, $"Olympic qualifying tournaments {y}", oqt, oqt.AddDays(5), "Four host cities",
                "Four tournaments of six; each winner goes to the Olympics");
            var (start, end, where) = y switch
            {
                2028 => (new DateOnly(2028, 7, 15), new DateOnly(2028, 7, 30), "Los Angeles"),
                2032 => (new DateOnly(2032, 7, 24), new DateOnly(2032, 8, 8), "Brisbane"),
                _ => (Saturday(new DateOnly(y, 7, 24)), Saturday(new DateOnly(y, 7, 24)).AddDays(15), "Host to be named"),
            };
            yield return new FibaEvent(FibaEventKind.Olympics, $"Olympic basketball tournament {y}", start, end, where, "12 teams: the host, the World Cup's qualifiers and four qualifying winners");
        }
        if (IsContinentalYear(y))
        {
            var s = Friday(new DateOnly(y, 8, 28));
            yield return new FibaEvent(FibaEventKind.Continental, $"EuroBasket {y}", s, s.AddDays(16), "Host to be named", "24 teams");
            var a = Friday(new DateOnly(y, 8, 22));
            yield return new FibaEvent(FibaEventKind.Continental, $"FIBA AmeriCup {y}", a, a.AddDays(9), "Host to be named", "12 teams");
            var af = Friday(new DateOnly(y, 8, 15));
            yield return new FibaEvent(FibaEventKind.Continental, $"FIBA AfroBasket {y}", af, af.AddDays(10), "Host to be named", "16 teams");
            var asia = Friday(new DateOnly(y, 8, 8));
            yield return new FibaEvent(FibaEventKind.Continental, $"FIBA Asia Cup {y}", asia, asia.AddDays(10), "Host to be named", "16 teams");
        }
    }

    private static DateOnly Monday(DateOnly near) => Nearest(near, DayOfWeek.Monday);
    private static DateOnly Friday(DateOnly near) => Nearest(near, DayOfWeek.Friday);
    private static DateOnly Saturday(DateOnly near) => Nearest(near, DayOfWeek.Saturday);

    private static DateOnly Nearest(DateOnly near, DayOfWeek day)
    {
        int diff = ((int)day - (int)near.DayOfWeek + 7) % 7;
        return diff <= 3 ? near.AddDays(diff) : near.AddDays(diff - 7);
    }
}
