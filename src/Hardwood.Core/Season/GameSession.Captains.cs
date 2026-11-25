using Hardwood.Core.College;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>Slice 6 of the revised launch plan (#1164, run 196): captains and the senior farewell (see <see cref="Captaincy"/>).</summary>
public sealed partial class GameSession
{
    /// <summary>This season's captains of your program (empty before they're named, in the summer, or outside college).</summary>
    public List<Player> MyCaptains =>
        InCollege && World.College?.CaptainsOf(HumanTeam.Id) is { } rec
            ? rec.PlayerIds.Select(id => HumanTeam.Roster.FirstOrDefault(p => p.Id == id)).OfType<Player>().ToList()
            : new();

    public bool IsCaptain(Player p) => InCollege && HumanTeam.CaptainIds.Contains(p.Id);

    /// <summary>Why the captaincy can't be given to (or taken from) a player now, or null when it can.</summary>
    public string? CaptaincyBlocked(Player p)
    {
        if (!InCollege || World.College is not { } c) return "Only college programs name captains.";
        if (c.Season.ChampionId != null) return "The season is over: next season's captains are named a week before its first game.";
        if (c.CaptainsOf(HumanTeam.Id) == null)
            return c.CaptainsDay is { } d && d > Today ? $"Captains are named a week before the first game ({d:MMM d})."
                : HumanTeam.Roster.Count == 0 ? "Sign players first: the captains come from the roster." : "Captains are named a week before the first game.";
        if (!IsCaptain(p) && MyCaptains.Count >= Captaincy.Count) return $"You have {Captaincy.Count} captains: take the captaincy from one of them first.";
        return null;
    }

    /// <summary>Make a player captain, or take the captaincy away. Returns the reply shown on the Squad page.</summary>
    public string ToggleCaptain(Player p)
    {
        if (CaptaincyBlocked(p) is { } why) return why;
        var c = World.College!;
        var now = MyCaptains;
        string last = p.Name.Split(' ').Last();
        if (IsCaptain(p))
        {
            now.Remove(p);
            // Review (run 196): its own mood line, so it doesn't wipe (or get wiped by) another lift.
            c.CaptainRecord(HumanTeam).Stripped[p.Id] = Today.AddDays(Captaincy.LostCaptaincyDays);
            c.SetCaptains(HumanTeam, now);
            return $"{last} takes it badly: losing the captaincy will weigh on him for about a month. "
                + (now.Count == 0 ? "The team has no captain now." : $"{now[0].Name} is the only captain now.");
        }
        now.Add(p);
        c.SetCaptains(HumanTeam, now);
        int lead = p.Attributes[Attr.Leadership];
        return $"{last} is proud to be named captain. " + (lead >= 15 ? "The room follows him." : lead >= Captaincy.LeadershipPar
            ? "The room respects him." : $"He isn't a natural leader (leadership {lead} of 20), so the locker room may not follow him.");
    }

    /// <summary>Called from the college calendar: name the captains on their day and tell the coach.</summary>
    private void CaptainsCalendar(CollegeWorld c)
    {
        c.NameCaptainsThrough(Today);
        if (c.CaptainsOf(HumanTeam.Id) is not { Announced: false } rec) return;
        rec.Announced = true;
        var caps = MyCaptains;
        if (caps.Count == 0) return;
        string Line(Player p) =>
            $"{p.Name} ({p.PrimaryPosition}, {ClassYearOf(p)?.Long.ToLowerInvariant() ?? "age " + p.Age(Today)}; leadership {p.Attributes[Attr.Leadership]})";
        var chem = Captaincy.ChemistryLine(HumanTeam);
        string body = $"The players have chosen their captains for {CollegeAwards.SeasonText(c.Season.Year)}, with your assistant's blessing: "
            + string.Join(" and ", caps.Select(Line)) + ".\n\n"
            + "Captains lead the locker room: their leadership sets the team's chemistry"
            + (chem is { } ch && Math.Round(ch.effect) != 0 ? $" ({ch.effect:+0;-0} today)" : "")
            + ", and a captain is proud to be one. An unhappy captain drags the room down.\n\n"
            + "You can change them on the Squad page (pick a player, then Make captain), but a player who loses the captaincy takes it badly.";
        Post($"Captains: {string.Join(" and ", caps.Select(p => p.Name))}", body, "Squad");
    }

    /// <summary>Senior Night (run 196): the seniors honoured tomorrow get their own lift for the rest of the season.</summary>
    private void HonourSeniors(CollegeWorld c)
    {
        var rec = c.CaptainRecord(HumanTeam);
        foreach (var p in c.FinalYear(HumanTeam)) if (!rec.Honoured.Contains(p.Id)) rec.Honoured.Add(p.Id);
        c.RefreshCaptainFlags();
    }

    /// <summary>The senior farewell (run 196): the evening of the national final, a message for each of your seniors.</summary>
    private void SeniorFarewell(CollegeWorld c)
    {
        if (c.Season.ChampionId == null || c.Season.Year != CollegeWorld.SchoolYear(Today)) return;
        var books = HumanTeam.Money.Program;
        if (books.FarewellYear >= c.Season.Year) return;
        books.FarewellYear = c.Season.Year;
        var seniors = c.FinalYear(HumanTeam);
        if (seniors.Count == 0) return;
        Post(FarewellTitle(seniors), FarewellBody(c, seniors), "Roster");
    }

    internal static string FarewellTitle(List<Player> seniors) =>
        seniors.Count == 1 ? $"Farewell to {seniors[0].Name}" : $"Farewell to the seniors: {seniors.Count} careers end";

    internal string FarewellBody(CollegeWorld c, List<Player> seniors)
    {
        var (w, l) = c.Record(HumanTeam.Id);
        int reached = c.MadnessReached(HumanTeam.Id);
        string season = $"{HumanTeam.FullName} finished {w}-{l}" + (c.Season.ChampionId == HumanTeam.Id ? " as national champions"
            : reached >= 0 ? $" and reached {MadnessRoundName(reached)}" : "") + ".";
        var lines = new List<string>
        {
            $"The season is over, and with it the college careers of {(seniors.Count == 1 ? "one senior" : $"{seniors.Count} seniors")}. {season}",
        };
        foreach (var p in seniors)
        {
            var bits = new List<string>();
            if (c.Season.Stats.TryGetValue(p.Id, out var st) && st.G > 0) bits.Add($"{st.Ppg:F1} points and {st.Rpg:F1} rebounds a game this season");
            var honours = c.HonoursOf(p.Id);
            if (honours.Count > 0) bits.Add(string.Join("; ", honours.Take(4)));
            lines.Add($"{p.Name} ({p.PrimaryPosition})" + (bits.Count > 0 ? ": " + string.Join(". ", bits) + "." : "."));
        }
        lines.Add("They leave the program this summer. Your juniors are next season's seniors, and the captaincy is theirs to win.");
        return string.Join("\n\n", lines);
    }

    private static string MadnessRoundName(int reached) => reached switch
    {
        >= 7 => "the national title",
        6 => "the national final",
        5 => "the Final Four",
        4 => "the Elite Eight",
        3 => "the Sweet 16",
        2 => "the Round of 32",
        1 => "March Madness",
        _ => "the First Four",
    };
}
