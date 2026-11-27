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
    public double MySalaryScale => InCollege ? CollegeStaffScale : HumanLeague?.Profile.SalaryScale ?? 1;
    /// <summary>College staff are paid on a modest scale (C2): the programs have no player wages at all.</summary>
    public const double CollegeStaffScale = 0.12;

    /// <summary>The day contracts are dated from: the summer's date in the offseason, otherwise today.</summary>
    private DateOnly ContractDate => Offseason?.Date ?? Today;
    /// <summary>The first season a contract signed now covers.</summary>
    private int ContractStart => Phase == SessionPhase.Offseason ? League.Season + 1 : League.Season;

    /// <summary>Players whose contracts are up and wait for your decision (summer only).</summary>
    public IReadOnlyList<Player> MyExpiring =>
        !Employed ? Array.Empty<Player>()
        : InNabl ? (Offseason is { Phase: OffseasonPhase.ReSigning } o && o.Expiring.TryGetValue(HumanTeam.Id, out var l) ? l : Array.Empty<Player>())
        : World.AwaitingDecision.TryGetValue(HumanTeam.Id, out var w) ? w : Array.Empty<Player>();

    /// <summary>The fewest players your league lets you carry (H29: the one number every screen, the assistant and the
    /// sporting director use: the NBA's 13, or 11 in tiers 2-5 and abroad).</summary>
    public int MyRosterMin => InNabl ? League.Finance.RosterMin : GameWorld.RosterMinimum;
    /// <summary>The most players your league lets you carry (the NBA's 15, or 15 outside it).</summary>
    public int MyRosterMax => InNabl ? League.Finance.RosterMax : GameWorld.RosterMaximum;

    /// <summary>
    /// H25/H30: true once this summer has counted a club's contracts down (the NBA's offseason from re-signing on; the world's
    /// summer for clubs abroad and in tiers 2-5). Before that, a deal with one year left is in its final season.
    /// </summary>
    public bool ContractsCounted(Team team) =>
        Phase == SessionPhase.Offseason
        && (League.Teams.Contains(team)
            ? Offseason is { } o && o.Phase != OffseasonPhase.NotStarted
            : World.SummerStarted == League.Season + 1);

    /// <summary>H30: deals that end this summer: in season, those in their final year (Years 1); once counted, those at 0.</summary>
    public bool EndsThisSummer(Player p, Team team) => p.Contract is { } c && c.Years <= (ContractsCounted(team) ? 0 : 1);

    /// <summary>
    /// Run 120 (critic #16: "the Offseason page is an empty dead end in season"): before the summer, the page previews it:
    /// the deals that end, best first, and what the rest of the squad is already owed for next season.
    /// </summary>
    public string SummerPreview()
    {
        if (!Employed || InCollege) return "";
        var team = HumanTeam;
        var ending = team.Roster.Where(p => EndsThisSummer(p, team)).OrderByDescending(p => p.CurrentAbility).ToList();
        long staying = team.Roster.Where(p => p.Contract != null && !EndsThisSummer(p, team)).Sum(p => p.Contract!.Salary);
        int kept = team.Roster.Count(p => p.Contract != null) - ending.Count;   // review: a player without a deal isn't under contract
        if (ending.Count == 0)
            return $"No deals end this summer: all {kept} players with deals are under contract for next season ({Budgets.Money(staying)}).";
        string names = string.Join(", ", ending.Take(4).Select(p => p.Name)) + (ending.Count > 4 ? $" and {ending.Count - 4} more" : "");
        return $"Deals ending this summer ({ending.Count}): {names}, {Budgets.Money(ending.Sum(p => p.Contract!.Salary))} between them. "
             + $"The other {kept} are under contract for next season ({Budgets.Money(staying)}).";
    }

    /// <summary>Over the wage budget by more than a rounding margin (1%), as the owner sees it.</summary>
    public bool OverWageBudget => HumanTeam.Money.Ready && MyCommittedWages > HumanTeam.Money.WageBudget * 1.01;

    /// <summary>Wages already committed for the season a new contract would start in.</summary>
    public long MyCommittedWages => HumanTeam.Roster.Where(p => !MyExpiring.Contains(p)).Sum(p => p.Contract?.Salary ?? 0);

    /// <summary>
    /// In the summer, the wages of college seniors committed to you who haven't signed yet (they sign on graduation day).
    /// The summer budget makes room for them (#256), so a renewal or free agent can't take that room (run 22: three
    /// signings filled the budget to $10k, then two committed seniors signed and the club was $2.4M over all season).
    /// </summary>
    public long MyReservedWages => Phase == SessionPhase.Offseason ? PendingCommitments(HumanTeam).wages : 0;

    /// <summary>
    /// The minimum deals still needed to reach the league's roster minimum after one more signing. Those deals may go over the
    /// budget (they fill the roster), so a bigger deal that takes their room leaves the club over for the season (review #10:
    /// `nba-real-mid` opened year 3 over after a $5.85M deal and three minimums). The owner keeps that room back.
    /// </summary>
    public long MyRosterFillReserve
    {
        get
        {
            int signed = HumanTeam.Roster.Count(x => !MyExpiring.Contains(x)) + (Phase == SessionPhase.Offseason ? PendingCommitments(HumanTeam).count : 0);
            int missing = Math.Max(0, MyRosterMin - signed - 1);
            return missing * (long)(League.Finance.MinSalary * MySalaryScale);
        }
    }

    /// <summary>How far past the wage budget the owner goes to keep this player of yours, whose deal is up; 0 in season, for
    /// anyone else, or when the club is in debt. In the NBA (D81, run 159: no salary cap) any of your own players, up to
    /// what the club's finances can carry; elsewhere (H122) only your own young star (CA 150+, under 26), by up to 35%.</summary>
    public long MyStretchFor(Player p) => !MyExpiring.Contains(p) ? 0
        : InNabl ? ContractRules.NbaKeepAllowance(HumanTeam, p)
        : ContractRules.KeepOwnAllowance(HumanTeam, p, ContractDate, ContractStart);

    /// <summary>D81: whether your renewals are bound by the club's means (the NBA: no salary cap) rather than the 35% young-star stretch.</summary>
    public bool KeepsByMeans => InNabl;

    /// <summary>The most the owner would go past the budget for a player of yours (for the summer page and the probe).</summary>
    public long KeepOwnAllowance() => InNabl ? Math.Max(0, ContractRules.NbaMeans(HumanTeam) - HumanTeam.Money.WageBudget) : ContractRules.Allowance(HumanTeam, ContractStart);

    /// <summary>H122: whether this renewal's wage fits the room the owner allows him (the budget, stretched for a young star).</summary>
    public bool CanKeep(Player p, long wage) => MyCommittedWages + MyReservedWages + MyRosterFillReserve + wage <= HumanTeam.Money.WageBudget + MyStretchFor(p);

    /// <summary>What's left for a new deal: the budget less committed wages, the summer's reserved seniors and the minimum deals
    /// the roster will still need.</summary>
    public long MyWageRoom => HumanTeam.Money.WageBudget - MyCommittedWages - MyReservedWages - MyRosterFillReserve;

    private readonly Dictionary<int, (ContractDemand opening, int round)> _talks = new();
    private readonly HashSet<int> _talksOver = new();

    /// <summary>
    /// H61 (run 110): the least this player takes to play in your league (<see cref="GameWorld.AskingFloor"/>, from the
    /// best league open to him); 0 in the top league and at college.
    /// </summary>
    /// Run 226: in the summer, another club's or the pool's player good enough for the top league waits for its offers
    /// (<see cref="GameWorld.SummerAskingFloor"/>); your own players renew on the usual floor, as the computer's clubs' do.
    public long MyAskingFloor(Player p) => InNabl || InCollege ? 0
        : p.TeamId == HumanTeam.Id ? World.AskingFloor(p, ContractDate) : World.SummerAskingFloor(p, ContractDate);

    /// <summary>
    /// Why his ask is above your league's usual wage for his level, for the talks screen; null when it isn't.
    /// </summary>
    public string? AskingFloorNote(Player p)
    {
        long floor = MyAskingFloor(p);
        long scaled = (long)(ContractMarket.MarketSalary(p.CurrentAbility, p.Age(ContractDate), League.Finance) * MySalaryScale);
        if (floor <= scaled) return null;
        if (floor > World.AskingFloor(p, ContractDate))
            return $"{p.Name} is good enough for the {Hardwood.Core.Names.Brand.Top} and is waiting for its offers: until its free agency ends he asks at least {Budgets.Money(floor)} a year.";
        return $"{p.Name} could earn far more in a bigger league, so he asks more than the {HumanLeague!.Profile.Name}'s usual wage for his level: at least {Budgets.Money(floor)} a year.";
    }

    /// <summary>K1 (run 111): in the top league, a star's deal includes a relegation release clause; the talks say so.</summary>
    public string? ReleaseClauseNote(Player p) =>
        InNabl && ContractMarket.AsksForReleaseClause(p)
            ? $"His deal will include a relegation release clause: if {HumanTeam.TheFullName} go down, he can leave on a free."
            : null;

    /// <summary>A player's demand of your club (fixed for the length of the talks).</summary>
    public ContractDemand DemandOf(Player p)
    {
        if (_talks.TryGetValue(p.Id, out var t)) return t.opening;
        var d = ContractTalks.Demand(p, HumanTeam, ContractDate, MySalaryScale, League.Finance, renewal: p.TeamId == HumanTeam.Id, floor: MyAskingFloor(p));
        _talks[p.Id] = (d, 0);
        return d;
    }

    /// <summary>A player's current ask of your club, without opening talks (for lists).</summary>
    public ContractDemand PeekDemand(Player p) =>
        _talks.TryGetValue(p.Id, out var t) ? t.opening
        : ContractTalks.Demand(p, HumanTeam, ContractDate, MySalaryScale, League.Finance, renewal: p.TeamId == HumanTeam.Id, floor: MyAskingFloor(p));

    /// <summary>
    /// Bug hunt H43/H44 (run 81): why waiving, trading or accepting bids is closed to you, or null. Out of work, the
    /// session still points at your old club (<see cref="Fire"/> leaves <c>HumanTeam</c> there), so without this a
    /// manager with no job could cut or trade its players; a college program has scholarship players, not pros.
    /// </summary>
    public string? RosterMovesClosed =>
        !Employed ? "You aren't coaching a club right now."
        : InCollege ? "A college program doesn't sign, waive or trade professionals: your roster is your scholarship players and walk-ons, and it changes through recruiting (College → Recruiting)."
        : null;

    /// <summary>
    /// Waive one of your players: he becomes a free agent at once (H44, run 81: moved here from the Transactions page so
    /// the checks live with the rule). Returns null when done, else why not. Reads no random numbers.
    /// </summary>
    public string? Waive(Player p)
    {
        if (RosterMovesClosed is { } closed) return closed;
        var team = HumanTeam;
        if (!team.Roster.Contains(p)) return $"{p.Name} isn't on your roster.";
        if (team.Roster.Count <= MyRosterMin) return $"You need at least {MyRosterMin} players.";
        team.Roster.Remove(p);
        p.TeamId = null; p.Contract = null;
        League.FreeAgents.Add(p);
        team.DepthChart = null;
        Post($"Waived {p.Name}", $"{p.Name} has been released and is a free agent.", "Transactions");
        SaveNow();
        return null;
    }

    /// <summary>
    /// Bug hunt H45 (run 81): your wages against the budget, the way every summer screen counts them. In the summer the
    /// deals that are up (and leave unless renewed) aren't counted as spent; they're named beside it, so the
    /// Transactions header, the Offseason page and the summer inbox lines agree. In season it is <see cref="Budgets.Describe"/>.
    /// </summary>
    public string MyBudgetLine
    {
        get
        {
            var expiring = MyExpiring;
            if (expiring.Count == 0) return Budgets.Describe(HumanTeam);
            long up = expiring.Sum(p => p.Contract?.Salary ?? 0);
            return Budgets.Describe(HumanTeam, MyCommittedWages, "wages committed")
                + $" (not counting {Budgets.Money(up)} of {expiring.Count} expiring deal{(expiring.Count == 1 ? "" : "s")})";
        }
    }

    /// <summary>Rounds of talks so far with this player (0 = not started).</summary>
    public int TalkRound(Player p) => _talks.TryGetValue(p.Id, out var t) ? t.round : 0;

    /// <summary>Why you can't talk to this player right now, or null if you can.</summary>
    public string? CannotTalk(Player p)
    {
        if (!Employed) return "You aren't coaching a club right now.";
        if (InCollege) return "A college program doesn't sign professionals: your roster comes from high-school recruiting.";
        if (_talksOver.Contains(p.Id)) return $"{p.Name}'s agent won't talk to you again this window.";
        bool renewal = MyExpiring.Contains(p);
        if (renewal) return Offseason.WontReSign(p) ? $"{p.Name} is too unhappy here to sign a new deal." : null;
        if (p.TeamId == HumanTeam.Id) return "His contract isn't up yet.";
        if (AgreedFees.ContainsKey(p.Id))
            return HumanTeam.Roster.Count >= MyRosterMax ? "Your roster is full. Release someone first." : null;
        if (!League.FreeAgents.Contains(p)) return $"{p.Name} is under contract elsewhere. Agree a fee with his club first (Transactions page).";
        if (HumanTeam.Roster.Count >= MyRosterMax) return "Your roster is full. Release someone first.";
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
        // H122 / D81: the owner goes past the budget to keep your own young star (CA 150+, under 26), or in the NBA any of
        // your own players, up to the club's means.
        long stretch = MyStretchFor(p);
        long budget = HumanTeam.Money.WageBudget + stretch;
        // Over the budget only for a minimum deal that fills the roster to the league's minimum (critic review #3:
        // renewals and minimum deals let the strong club creep over its budget every summer).
        bool fillsRoster = wage <= (long)(League.Finance.MinSalary * MySalaryScale) && HumanTeam.Roster.Count(x => !MyExpiring.Contains(x)) < MyRosterMin;
        long reserved = MyReservedWages;
        long fill = fillsRoster ? 0 : MyRosterFillReserve;
        if (MyCommittedWages + reserved + fill + wage > budget && !fillsRoster)
            return new TalkReply(false, false, demand,
                $"The owner won't sanction it: that would take wages to ${(MyCommittedWages + reserved + fill + wage) / 1e6:F1}M against a ${budget / 1e6:F1}M budget"
                + (stretch > 0 ? (InNabl ? $" (the most the club's finances can carry to keep {p.Name})" : $" (stretched by {Budgets.Money(stretch)} to keep {p.Name})") : "")
                + (reserved > 0 ? $", counting ${reserved / 1e6:F1}M for your committed college seniors" : "")
                + (fill > 0 ? $", counting {Budgets.Money(fill)} for the minimum deals the roster still needs" : "") + "."
                + (MyExpiring.Contains(p) ? " Free up wages first (sell or release someone), or let him go." : ""));

        var (opening, round) = _talks[p.Id];
        var reply = ContractTalks.Respond(p, opening, wage, years, ref round);
        _talks[p.Id] = (opening, round);
        if (reply.WalkedAway)
        {
            _talksOver.Add(p.Id);
            _talks.Remove(p.Id);
            if (AgreedFees.ContainsKey(p.Id))
                foreach (var b in MyBids.Where(b => b.PlayerId == p.Id && b.Status == MyBidStatus.FeeAgreed))
                { b.Status = MyBidStatus.TermsFailed; b.Note = "He walked away from your terms."; }
            Post($"Talks off: {p.Name}", reply.Text, "Roster");
        }
        else if (reply.Accepted)
        {
            _talks.Remove(p.Id);
            Sign(p, wage, years);
            // H56 (run 87): a minimum deal that makes up the roster may go over the budget (above); the owner funds it.
            if (fillsRoster) Budgets.OwnerFunds(HumanTeam, MyCommittedWages);
        }
        SaveNow();
        return reply;
    }

    private void Sign(Player p, long wage, int years, bool quiet = false)
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
            // H122 / D81: a player kept over the budget (a young star; in the NBA anyone of yours): the owner raises the budget
            // to cover the deals (no-op when they fit).
            if (InNabl || ContractRules.IsYoungStar(p, ContractDate)) ContractRules.OwnerCovers(team, MyCommittedWages + MyReservedWages, ContractStart);
            Post($"{p.Name} re-signs", $"{p.Name} agrees a new deal: ${wage / 1e6:F2}M a year for {years} year{(years == 1 ? "" : "s")}.", "Roster");
            return;
        }
        League.FreeAgents.RemoveAll(x => x.Id == p.Id);   // H28: every entry, never one of two
        p.Contract = InNabl ? new Contract { Years = years, Salary = wage, StartSeason = ContractStart, RelegationRelease = ContractMarket.AsksForReleaseClause(p) }
                            : GameWorld.ContractAt(p, wage, years, ContractDate, ContractStart);
        p.TeamId = team.Id;
        p.DraftRightsTeamId = null;
        team.Roster.RemoveAll(x => x.Id == p.Id && x != p);   // H28: never a stale copy beside him
        if (!team.Roster.Contains(p)) team.Roster.Add(p);
        team.DepthChart = null;
        People.PeopleEngine.Joined(p);
        if (InNabl && Offseason != null) Offseason.Report.FreeAgentSignings.Add((p, team, wage, years));
        if (!quiet) Post($"{p.Name} signs", $"{p.Name} ({p.PrimaryPosition}, {p.Age(ContractDate)}) joins {team.TheFullName}: ${wage / 1e6:F2}M a year for {years} year{(years == 1 ? "" : "s")}.", "Roster");
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
            if (!League.FreeAgents.Contains(p)) League.FreeAgents.Add(p);
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
