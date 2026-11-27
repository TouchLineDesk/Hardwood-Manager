using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

/// <summary>
/// K46 (run 229, critic review #26; Danny, Nov 27: "no he shouldnt unless i switch it on"): the game never signs a player or
/// hires staff for your club at the end of the summer unless you handed that duty to your staff, and whatever they do is
/// reported in one inbox message.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>
    /// Why "Start new season" can't go ahead yet, or null when it can: a pro club whose roster (not counting players still
    /// waiting on your renewal) is under next season's minimum while the end-of-summer signings are yours. Sign players on the
    /// Free agents tab (a one-year minimum deal that fills the roster always fits the budget), or hand the signings to your
    /// sporting director on the Staff page. Run 230: also tiers 2-5 and abroad, and the minimum is next season's league's
    /// (an NBA club going down needs the G League's 11; a club going up to the NBA needs 13).
    /// </summary>
    public string? SeasonStartHold()
    {
        if (!Employed || InCollege || Delegated.Signings) return null;
        if (Phase != SessionPhase.Offseason || Offseason?.Phase != OffseasonPhase.FreeAgency) return null;
        var waiting = MyExpiring;
        int have = HumanTeam.Roster.Count(p => !waiting.Contains(p)), min = NextSeasonRosterMin;
        if (have >= min) return null;
        int need = min - have;
        string league = NextSeasonLeagueName;
        return $"{HumanTeam.TheFullNameCap} have {have} players signed for next season and need at least {min} to start it in {league}. "
             + $"Sign {need} more on the Free agents tab (a one-year minimum deal that fills the roster always fits the budget)"
             + (waiting.Count > 0 ? $" or renew some of the {waiting.Count} whose contracts are up" : "")
             + ", or let your sporting director make the end-of-summer signings (Staff page, Delegate).";
    }

    /// <summary>The fewest players your club needs next season: the league it will play in (run 230).</summary>
    public int NextSeasonRosterMin =>
        InNabl ? (GoingDown() ? GameWorld.RosterMinimum : League.Finance.RosterMin)
        : GoingUp() && HumanLeague?.Key == UsaPyramid.Tier2 ? League.Finance.RosterMin : GameWorld.RosterMinimum;

    private string NextSeasonLeagueName =>
        RelegationOutlook() is { } down ? down.League
        : !InNabl && GoingUp() && HumanLeague?.Key == UsaPyramid.Tier2 ? $"the {Hardwood.Core.Names.Brand.Top}"
        : HumanLeagueName;

    /// <summary>Post the hold once for each roster count, so pressing Continue again doesn't fill the inbox.</summary>
    public void PostSeasonStartHold(string hold)
    {
        var waiting = MyExpiring;
        string title = $"Sign {NextSeasonRosterMin - HumanTeam.Roster.Count(p => !waiting.Contains(p))} more before the season";
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
        var parts = new List<string>();
        int n = signed.Count;
        if (n > 0)
            parts.Add((Delegated.Signings ? "Your sporting director made the end-of-summer signings you handed him:\n"
                : "Your roster was under the league minimum at the end of the summer, so the league filled it with one-year minimum deals:\n")
                + string.Join("\n", lines.Take(n)));
        if (hired.Count > 0) parts.Add("Your general manager filled the empty staff posts:\n" + string.Join("\n", lines.Skip(n)));
        string body = string.Join("\n\n", parts) + "\n\nWho handles what is on the Staff page (Delegate).";
        string title = n > 0 ? $"Summer signings: {n} player{(n == 1 ? "" : "s")}" + (hired.Count > 0 ? $", {hired.Count} staff" : "")
                     : $"Staff hired: {hired.Count}";
        Post(title, body, "Roster");
        if (Delegated.Signings && n > 0) Delegated.LastSignings = $"{Today:MMM d}: {title}";
    }
}
