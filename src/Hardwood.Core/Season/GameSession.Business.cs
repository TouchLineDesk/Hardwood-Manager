using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

/// <summary>The revenue levers (D81, run 169): your pro club's ticket, food and parking prices and fan appreciation days.</summary>
public sealed partial class GameSession
{
    /// <summary>A pro club sets its own prices (college programs don't sell tickets in this game: CS7 is their money).</summary>
    public bool HasBusiness => Employed && !InCollege;

    /// <summary>A change takes effect at once before the season starts (preseason, or the summer after the books close);
    /// once games are being played it's for next season.</summary>
    public bool BusinessChangesNow => HasBusiness && (Phase == SessionPhase.Preseason || HumanTeam.Money.Business.BooksClosed);

    /// <summary>This season's plan and the one you've set for next season.</summary>
    public BusinessPlan MyPlan => HumanTeam.Money.Business.Now;
    public BusinessPlan MyNextPlan => HumanTeam.Money.Business.Next;

    /// <summary>What a plan would do this season at your club (or what this season's plan is doing).</summary>
    public Business.MatchDay MyMatchDay(BusinessPlan? plan = null) =>
        Business.Plan(HumanTeam.Money.Revenue, HumanTeam.Money.FanBase, plan ?? MyPlan);

    /// <summary>Set your prices. Returns what happens, in a line for the page.</summary>
    public string SetBusiness(BusinessPlan plan)
    {
        if (!HasBusiness) return "Only a pro club sets its prices.";
        var b = HumanTeam.Money.Business;
        var p = Business.Clamp(plan);
        b.Next = p;
        string line;
        if (BusinessChangesNow)
        {
            // The owner set the wage budget from the season's income: a change before the season moves it by his share.
            long was = Business.Change(HumanTeam.Money);
            b.Now = p.Copy();
            long delta = Business.Change(HumanTeam.Money) - was;
            var m = HumanTeam.Money;
            if (delta != 0 && m.Ready)
                m.WageBudget = Math.Max(m.WageBudget + (long)Math.Round(delta * Budgets.WageShare(HumanTeam.Owner) / 100_000.0) * 100_000,
                    m.Balance >= 0 ? Math.Min(m.WageBudget, ContractMarket.Payroll(HumanTeam)) : 0);
            var md = MyMatchDay();
            line = $"Set for this season: match day {Budgets.Money(md.Total)}" + (md.Change == 0 ? " (standard)" : $" ({Sign(md.Change)} on standard)") + $", arena {md.Fill * 100:0}% full.";
        }
        else line = b.Now.Same(p) ? "Back to this season's prices: nothing changes next season." : "Season tickets are already sold: these prices start next season.";
        SaveNow();
        return line;
    }

    private static string Sign(long v) => (v >= 0 ? "+" : "-") + Budgets.Money(Math.Abs(v));
}
