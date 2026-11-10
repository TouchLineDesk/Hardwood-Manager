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
        if (Phase == SessionPhase.Offseason && Offseason?.Phase == OffseasonPhase.ReSigning && InNabl)
            return "Free agency opens after the re-signing period. Continue to free agency first.";
        return null;
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
        if (MyCommittedWages + wage > budget && wage > (long)(League.Finance.MinSalary * MySalaryScale))
            return new TalkReply(false, false, demand,
                $"The owner won't sanction it: that would take wages to ${(MyCommittedWages + wage) / 1e6:F1}M against a ${budget / 1e6:F1}M budget.");

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
