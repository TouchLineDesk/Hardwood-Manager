using Hardwood.Core.College;

namespace Hardwood.Core.Season;

/// <summary>Slice 8 of the revised launch plan (#1164, run 199): your program's Hall of Fame class, the night of the national final.</summary>
public sealed partial class GameSession
{
    private void LegacyCeremony(CollegeWorld c)
    {
        if (c.Season.ChampionId == null || c.Season.Year != CollegeWorld.SchoolYear(Today)) return;
        var books = HumanTeam.Money.Program;
        if (books.LegacyYear >= c.Season.Year) return;
        books.LegacyYear = c.Season.Year;
        var book = c.LegacyOf(HumanTeam.Id);
        var cls = book.Hall.Where(h => h.Class == c.Season.Year + 1).OrderByDescending(h => h.Score).ToList();
        if (cls.Count == 0) return;
        Post(LegacyTitle(cls), LegacyBody(cls), "Roster");
    }

    public string LegacyTitle(List<LegacyInductee> cls)
    {
        var top = cls[0];
        if (top.Jersey is { } n) return $"Into the rafters: No. {n}, {top.Name}";
        return cls.Count == 1 ? $"{top.Name} joins the {HumanTeam.FullName} Hall of Fame" : $"{cls.Count} seniors join the {HumanTeam.FullName} Hall of Fame";
    }

    public string LegacyBody(List<LegacyInductee> cls)
    {
        var lines = new List<string>
        {
            $"The program's Hall of Fame takes its class of {cls[0].Class} tonight."
                + (cls.Any(h => h.Jersey != null) ? " The best of them will never see their number worn here again." : ""),
        };
        foreach (var h in cls)
            lines.Add($"{h.Name} ({h.Pos}, {h.Years})" + (h.Jersey is { } n ? $": No. {n} is retired and goes into the rafters." : ":")
                + (h.Line.Length > 0 ? $" {h.Line}." : "") + (h.Why.Count > 0 ? $" {string.Join("; ", h.Why.Take(4))}." : ""));
        lines.Add("The Hall of Fame, the rafters and the program's records are on its Team page, under Legacy.");
        return string.Join("\n\n", lines);
    }
}
