using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>
/// Money buys things, part 1 (item M, #281): your club's facilities. Each summer the front office brings the owner's
/// fundable projects to the Board page; you approve or decline them, or let the general manager decide (D10, #249).
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Your club runs facilities (pro clubs only; college programs come in part 2).</summary>
    public bool HasFacilities => Employed && !InCollege;
    /// <summary>Your club's facilities (anything left from the retired academy, #291, is dropped as you look).</summary>
    public ClubFacilities MyFacilities { get { var f = HumanTeam.Money.Facilities; f.DropRetired(); return f; } }

    /// <summary>The league revenue facility prices are quoted in (your league's average club).</summary>
    public long MyLeagueRevenue => Budgets.LeagueRevenue(InNabl ? 1 : HumanLeague?.Profile.SalaryScale ?? 1);

    /// <summary>Only your club waits for your answer; everyone else's owner decides (and yours too when you delegate).</summary>
    private void MarkFacilityDecider()
    {
        foreach (var t in League.Teams) t.Money.Facilities.YouDecide = false;
        foreach (var t in World.Clubs) t.Money.Facilities.YouDecide = false;
        if (HasFacilities) HumanTeam.Money.Facilities.YouDecide = !Delegated.Facilities;
    }

    /// <summary>Let the general manager decide (or take it back). Takes effect from the next summer; today's proposals stay yours.</summary>
    public void DelegateFacilities(bool on)
    {
        Delegated.Facilities = on;
        if (HasFacilities) MyFacilities.YouDecide = !on;
    }

    /// <summary>What the owner will put into facilities now (his cash above the reserve he keeps).</summary>
    public long FacilityBudget => HasFacilities ? Franchise.Spendable(HumanTeam) : 0;

    /// <summary>Approve a proposal: the first payment comes out of the bank now. Null on success, or why not.</summary>
    public string? ApproveFacility(int index)
    {
        if (!HasFacilities) return "Only a pro club has facilities to build.";
        var f = MyFacilities;
        if (index < 0 || index >= f.Proposals.Count) return "That proposal has gone.";
        var p = f.Proposals[index];
        if (f.IsBuilding(p.Kind)) return $"{Franchise.Info(p.Kind).Name} is already being built.";
        if (p.Instalment > FacilityBudget)
            return $"The owner won't fund it now: it needs {Franchise.Money(p.Instalment)} and he'll spend {Franchise.Money(FacilityBudget)} above the {Franchise.Money(Franchise.Reserve(HumanTeam))} he keeps in the bank.";
        f.Proposals.RemoveAt(index);
        Franchise.Start(HumanTeam, p);
        f.LastNews.RemoveAt(f.LastNews.Count - 1); // the summer's news is the summer's; this one is yours
        // What's left must still be fundable.
        f.Proposals.RemoveAll(x => x.Instalment > FacilityBudget);
        SaveNow();
        return null;
    }

    /// <summary>Turn a proposal down: it comes back next summer if it's still the best use of the money.</summary>
    public void DeclineFacility(int index)
    {
        if (!HasFacilities) return;
        var f = MyFacilities;
        if (index >= 0 && index < f.Proposals.Count) f.Proposals.RemoveAt(index);
        SaveNow();
    }

    /// <summary>The summer's facilities letter: what opened or dated, and the proposals (or what the GM approved).</summary>
    private void FacilitiesSummerNews()
    {
        if (!HasFacilities) return;
        var f = MyFacilities;
        var lines = new List<string>();
        if (f.LastNews.Count > 0) lines.Add(string.Join("\n", f.LastNews.Select(n => n.StartsWith("Approved: ") ? "Your general manager approved " + n["Approved: ".Length..] : n)));
        lines.Add($"Running costs this season: {Franchise.Money(f.LastUpkeep)}. Bank: {Franchise.Money(HumanTeam.Money.Balance)}; the owner keeps {Franchise.Money(Franchise.Reserve(HumanTeam))} in reserve.");
        if (f.YouDecide)
        {
            if (f.Proposals.Count > 0)
                lines.Add("The front office proposes (the owner will fund any of these):\n" + string.Join("\n", f.Proposals.Select(p => "• " + Franchise.Describe(p) + ": " + Franchise.Info(p.Kind).Effect.ToLower())) +
                          "\n\nApprove or decline them on the Board page, or tick \"Let the GM decide\" there.");
            else lines.Add("No proposals this summer: the owner has nothing above his reserve to put into facilities.");
        }
        Post("Facilities: the summer meeting", string.Join("\n\n", lines), "Board");
    }
}
