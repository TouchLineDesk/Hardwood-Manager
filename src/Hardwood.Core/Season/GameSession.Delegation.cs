using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// K46 (run 229, critic review #26; Danny, Nov 27: "no he shouldnt unless i switch it on"): the game never signs a player or
/// hires staff for your club at the end of the summer unless you handed that duty to your staff, and whatever they do is
/// reported in one inbox message.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Why "Start new season" can't go ahead yet, or null when it can: an NBA club whose roster is under the league minimum
    /// while the end-of-summer signings are yours. Sign players on the Free agents tab (a one-year minimum deal that fills the
    /// roster always fits the budget), or hand the signings to your sporting director on the Staff page.
    /// </summary>
    public string? SeasonStartHold()
    {
        if (!Employed || InCollege || !InNabl || Delegated.Signings) return null;
        if (Phase != SessionPhase.Offseason || Offseason?.Phase != OffseasonPhase.FreeAgency) return null;
        int have = HumanTeam.Roster.Count, min = League.Finance.RosterMin;
        if (have >= min) return null;
        int need = min - have;
        return $"{HumanTeam.TheFullNameCap} have {have} players under contract and need at least {min} to start the season. "
             + $"Sign {need} more on the Free agents tab (a one-year minimum deal that fills the roster always fits the budget), "
             + "or let your sporting director make the end-of-summer signings (Staff page, Delegate).";
    }

    /// <summary>Post the hold once for each roster count, so pressing Continue again doesn't fill the inbox.</summary>
    public void PostSeasonStartHold(string hold)
    {
        string title = $"Sign {League.Finance.RosterMin - HumanTeam.Roster.Count} more before the season";
        if (Inbox.Any(m => m.Date == Today && m.Title == title)) return;
        Post(title, hold, "Roster");
    }

    /// <summary>One message for everything the end of the summer did for your club: signings (wage, years, why) and hires.</summary>
    private void PostSummerSignings()
    {
        if (!Employed || InCollege || Offseason is not { } o) return;
        var signed = o.HumanSummerSignings;
        var hired = o.HumanSummerHires;
        if (signed.Count == 0 && hired.Count == 0) return;
        var lines = new List<string>();
        long min = League.Finance.MinSalary;
        foreach (var (p, _, salary, years) in signed)
        {
            string why = salary <= min && years == 1 ? "a one-year minimum deal to reach the roster minimum"
                : $"CA {p.CurrentAbility}, age {p.Age(Today)}, within the wage budget";
            lines.Add($"{p.Name} ({p.PrimaryPosition}): {Budgets.Money(salary)} × {years} — {why}.");
        }
        foreach (var st in hired)
            lines.Add($"{st.Name} joins as {Staff.RoleName(st.Role, CollegeWords).ToLower()} ({Budgets.Money(st.Salary)} a year for {st.ContractYears}).");
        string who = Delegated.Signings ? "Your sporting director made the end-of-summer signings you handed him"
                   : "Your roster was under the league minimum when the season began, so the league filled it with one-year minimum deals";
        string body = (signed.Count > 0 ? who + ":\n" : "Your general manager filled the empty staff posts:\n") + string.Join("\n", lines)
                    + "\n\nWho handles what is on the Staff page (Delegate).";
        string title = signed.Count > 0 ? $"Summer signings: {signed.Count} player{(signed.Count == 1 ? "" : "s")}" + (hired.Count > 0 ? $", {hired.Count} staff" : "")
                     : $"Staff hired: {hired.Count}";
        Post(title, body, "Roster");
        Delegated.LastSignings = $"{Today:MMM d}: {title}";
    }
}
