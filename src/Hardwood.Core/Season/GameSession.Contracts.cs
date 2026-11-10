using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

/// <summary>
/// The money model for your club (decisions #87-90), the same in the NABL and abroad: your budget, the players whose
/// deals are up, and contract talks (offer and counter) for renewals and free agents.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Your league's wage level (1 for the NABL; an overseas league pays a fraction).</summary>
    public double MySalaryScale => HumanLeague?.Profile.SalaryScale ?? 1;

    /// <summary>The day contracts are dated from: the summer's date in the offseason, otherwise today.</summary>
    private DateOnly ContractDate => Offseason?.Date ?? Today;
    /// <summary>The first season a contract signed now covers.</summary>
    private int ContractStart => Phase == SessionPhase.Offseason ? League.Season + 1 : League.Season;

    /// <summary>Players whose contracts are up and wait for your decision (summer only).</summary>
    public IReadOnlyList<Player> MyExpiring =>
        !Employed ? Array.Empty<Player>()
        : InNabl ? (Offseason is { Phase: OffseasonPhase.ReSigning } o && o.Expiring.TryGetValue(HumanTeam.Id, out var l) ? l : Array.Empty<Player>())
        : World.AwaitingDecision.TryGetValue(HumanTeam.Id, out var w) ? w : Array.Empty<Player>();

    /// <summary>The fewest players your league lets you carry.</summary>
    public int MyRosterMin => InNabl ? League.Finance.RosterMin : GameWorld.RosterSize - 2;

    /// <summary>Over the wage budget by more than a rounding margin (1%), as the owner sees it.</summary>
    public bool OverWageBudget => HumanTeam.Money.Ready && MyCommittedWages > HumanTeam.Money.WageBudget * 1.01;

    /// <summary>Wages already committed for the season a new contract would start in.</summary>
    public long MyCommittedWages => HumanTeam.Roster.Where(p => !MyExpiring.Contains(p)).Sum(p => p.Contract?.Salary ?? 0);

    private readonly Dictionary<int, (ContractDemand opening, int round)> _talks = new();
    private readonly HashSet<int> _talksOver = new();

    /// <summary>A player's demand of your club (fixed for the length of the talks).</summary>
    public ContractDemand DemandOf(Player p)
    {
        if (_talks.TryGetValue(p.Id, out var t)) return t.opening;
        var d = ContractTalks.Demand(p, HumanTeam, ContractDate, MySalaryScale, League.Finance, renewal: p.TeamId == HumanTeam.Id);
        _talks[p.Id] = (d, 0);
        return d;
    }

    /// <summary>A player's current ask of your club, without opening talks (for lists).</summary>
    public ContractDemand PeekDemand(Player p) =>
        _talks.TryGetValue(p.Id, out var t) ? t.opening
        : ContractTalks.Demand(p, HumanTeam, ContractDate, MySalaryScale, League.Finance, renewal: p.TeamId == HumanTeam.Id);

    /// <summary>Rounds of talks so far with this player (0 = not started).</summary>
    public int TalkRound(Player p) => _talks.TryGetValue(p.Id, out var t) ? t.round : 0;

    /// <summary>Why you can't talk to this player right now, or null if you can.</summary>
    public string? CannotTalk(Player p)
    {
        if (!Employed) return "You aren't coaching a club right now.";
        if (_talksOver.Contains(p.Id)) return $"{p.Name}'s agent won't talk to you again this window.";
        bool renewal = MyExpiring.Contains(p);
        if (renewal) return Offseason.WontReSign(p) ? $"{p.Name} is too unhappy here to sign a new deal." : null;
        if (p.TeamId == HumanTeam.Id) return "His contract isn't up yet.";
        if (AgreedFees.ContainsKey(p.Id))
            return HumanTeam.Roster.Count >= League.Finance.RosterMax ? "Your roster is full. Release someone first." : null;
        if (!League.FreeAgents.Contains(p)) return $"{p.Name} is under contract elsewhere. Agree a fee with his club first (Transactions page).";
        if (HumanTeam.Roster.Count >= League.Finance.RosterMax) return "Your roster is full. Release someone first.";
        if (AimsHigher(p) is { } higher) return higher;
        if (Phase == SessionPhase.Offseason && Offseason?.Phase == OffseasonPhase.ReSigning && InNabl)
            return "Free agency opens after the re-signing period. Continue to free agency first.";
        return null;
    }

    /// <summary>
    /// A free agent far better than your league won't sign for a club in it while he's young enough to do better
    /// (critic review #2: a local club stacked with tier-2 players climbed three levels in three seasons, first every time).
    /// Players aged 31+ will drop down; so will anyone within about 22 CA of the league's level.
    /// </summary>
    public string? AimsHigher(Player p)
    {
        if (InNabl || HumanLeague is not { } lg || p.Age(Today) >= 31) return null;
        if (!World.Levels.TryGetValue(lg.Key, out var level)) return null;
        double gap = p.CurrentAbility - level;
        return gap > 22 ? $"{p.Name} is aiming higher than the {lg.Profile.Name}. Players of his level want a stronger league." : null;
    }

    /// <summary>
    /// Make an offer. The player accepts (and signs at once), counters, or ends the talks.
    /// The owner won't let you go over the wage budget; that doesn't cost you a round.
    /// </summary>
    public TalkReply Offer(Player p, long wage, int years)
    {
        var demand = DemandOf(p);
        if (CannotTalk(p) is { } why) return new TalkReply(false, false, demand, why);
        years = Math.Clamp(years, 1, 5);
        long budget = HumanTeam.Money.WageBudget;
        // Over the budget only for a minimum deal that fills the roster to the league's minimum (critic review #3:
        // renewals and minimum deals let the strong club creep over its budget every summer).
        bool fillsRoster = wage <= (long)(League.Finance.MinSalary * MySalaryScale) && HumanTeam.Roster.Count(x => !MyExpiring.Contains(x)) < MyRosterMin;
        if (MyCommittedWages + wage > budget && !fillsRoster)
            return new TalkReply(false, false, demand,
                $"The owner won't sanction it: that would take wages to ${(MyCommittedWages + wage) / 1e6:F1}M against a ${budget / 1e6:F1}M budget."
                + (MyExpiring.Contains(p) ? " Free up wages first (sell or release someone), or let him go." : ""));

        var (opening, round) = _talks[p.Id];
        var reply = ContractTalks.Respond(p, opening, wage, years, ref round);
        _talks[p.Id] = (opening, round);
        if (reply.WalkedAway)
        {
            _talksOver.Add(p.Id);
            _talks.Remove(p.Id);
            Post($"Talks off: {p.Name}", reply.Text, "Roster");
        }
        else if (reply.Accepted)
        {
            _talks.Remove(p.Id);
            Sign(p, wage, years);
        }
        SaveNow();
        return reply;
    }

    private void Sign(Player p, long wage, int years)
    {
        bool renewal = MyExpiring.Contains(p);
        var team = HumanTeam;
        if (AgreedFees.TryGetValue(p.Id, out var fee) && FindTeam(p.TeamId ?? 0) is { } seller && seller != team)
        {
            CompleteTransfer(p, seller, team, fee, wage, years);
            return;
        }
        if (renewal)
        {
            if (InNabl) Offseason!.ReSign(team, p, years, wage);
            else
            {
                p.Contract = GameWorld.ContractAt(p, wage, years, ContractDate, ContractStart);
                World.AwaitingDecision[team.Id].Remove(p);
            }
            Post($"{p.Name} re-signs", $"{p.Name} agrees a new deal: ${wage / 1e6:F2}M a year for {years} year{(years == 1 ? "" : "s")}.", "Roster");
            return;
        }
        League.FreeAgents.Remove(p);
        p.Contract = InNabl ? new Contract { Years = years, Salary = wage, StartSeason = ContractStart }
                            : GameWorld.ContractAt(p, wage, years, ContractDate, ContractStart);
        p.TeamId = team.Id;
        p.DraftRightsTeamId = null;
        team.Roster.Add(p);
        team.DepthChart = null;
        People.PeopleEngine.Joined(p);
        if (InNabl && Offseason != null) Offseason.Report.FreeAgentSignings.Add((p, team, wage, years));
        Post($"{p.Name} signs", $"{p.Name} ({p.PrimaryPosition}, {p.Age(ContractDate)}) joins the {team.FullName}: ${wage / 1e6:F2}M a year for {years} year{(years == 1 ? "" : "s")}.", "Roster");
    }

    /// <summary>Let an expiring player go: he joins the free-agent pool.</summary>
    public void ReleaseExpiring(Player p)
    {
        if (!MyExpiring.Contains(p)) return;
        if (InNabl) Offseason!.Release(HumanTeam, p);
        else
        {
            World.AwaitingDecision[HumanTeam.Id].Remove(p);
            HumanTeam.Roster.Remove(p);
            p.TeamId = null; p.Contract = null;
            League.FreeAgents.Add(p);
        }
        _talks.Remove(p.Id);
        HumanTeam.DepthChart = null;
        Post($"{p.Name} released", $"{p.Name} leaves as a free agent.", "Roster");
        SaveNow();
    }

    /// <summary>A new window: agents talk to you again and asks are recalculated.</summary>
    private void NewWindow()
    {
        _talks.Clear();
        _talksOver.Clear();
    }
}
