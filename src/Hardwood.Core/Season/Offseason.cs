using Hardwood.Core.Generation;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

public sealed record DraftPick(int Round, int Pick, Team Team, Player Player, Team? OriginalTeam = null);

public enum OffseasonPhase { NotStarted, ReSigning, Draft, FreeAgency, Complete }

/// <summary>How a free agent is signed: cap room, the mid-level exception, or a minimum deal.</summary>
public enum SigningRoute { None, CapRoom, MidLevel, Minimum, Budget }

public sealed class OffseasonReport
{
    /// <summary>Your staff whose contracts ran out without an extension.</summary>
    public List<Staff> StaffLeft { get; } = new();
    public int Season { get; init; }
    public List<DevelopmentReport> Development { get; } = new();
    public List<Player> Retired { get; } = new();
    public List<(Player player, Team team)> ReSigned { get; } = new();
    public List<(Player player, Team from)> ToFreeAgency { get; } = new();
    public LotteryResult? Lottery { get; set; }
    public List<(DraftPickAsset pick, int slot)> ProtectionsTriggered { get; } = new();
    public List<DraftPick> Draft { get; } = new();
    public List<(Player player, Team team, long salary, int years)> FreeAgentSignings { get; } = new();
    public List<(Player player, Team team)> Waived { get; } = new();
    public List<Player> LeftUnsigned { get; } = new();
    public List<Player> HallOfFame { get; } = new();
    public List<(Player player, Team team)> RetiredNumbers { get; } = new();
    public List<(Staff staff, Team team)> StaffReleased { get; } = new();
    public List<(Staff staff, Team team)> StaffHired { get; } = new();
}

/// <summary>
/// Turns one finished season into the next, in phases so a human-controlled team can act at each decision point:
/// development, aging and retirements → contract expiry (re-sign or release) → lottery and draft → free agency → compliance.
/// AI teams decide for themselves; <see cref="RunAll"/> runs the whole thing headless.
/// </summary>
public sealed class Offseason
{
    private readonly Rng _rng;
    private readonly DevelopmentEngine _dev;
    private readonly League _league;
    private readonly SeasonOutcome _outcome;
    private readonly Dictionary<int, PlayerSeasonStats> _stats;

    public OffseasonReport Report { get; }
    public OffseasonPhase Phase { get; private set; } = OffseasonPhase.NotStarted;
    public DateOnly Date { get; }
    public int Season { get; }

    /// <summary>Players whose contracts expired, per team, awaiting a re-sign or release decision.</summary>
    public Dictionary<int, List<Player>> Expiring { get; } = new();
    /// <summary>Lottery result: the slot order by ORIGINAL team (slot 1 = worst / lottery winner).</summary>
    public List<Team> DraftOrder { get; private set; } = new();
    /// <summary>Who actually picks at each slot (60 entries: round 1 then round 2), after ownership and protections.</summary>
    public List<Team> PickOwners { get; private set; } = new();
    public List<Player> Prospects { get; private set; } = new();
    /// <summary>Index of the next pick to be made (0-based into PickOwners).</summary>
    public int NextPickIndex { get; private set; }
    public bool DraftComplete => NextPickIndex >= PickOwners.Count;
    public Team? OnTheClock => DraftComplete ? null : PickOwners[NextPickIndex];
    public int CurrentRound => NextPickIndex / Math.Max(1, DraftOrder.Count) + 1;
    /// <summary>Original team for a given pick index.</summary>
    public Team OriginalTeamAt(int pickIndex) => DraftOrder[pickIndex % DraftOrder.Count];

    /// <summary>The club you manage: its staff contracts are your call (staff part 2), so they aren't renewed for you.</summary>
    public int? HumanTeamId { get; init; }

    public Offseason(League league, SeasonOutcome outcome, Dictionary<int, PlayerSeasonStats> stats, Rng rng, DevelopmentConfig? devConfig = null)
    {
        _league = league; _outcome = outcome; _stats = stats; _rng = rng;
        _dev = new DevelopmentEngine(rng, devConfig);
        Season = league.Season;
        Date = new DateOnly(Season + 1, 7, 1);
        Report = new OffseasonReport { Season = Season };
    }

    /// <summary>Rebuild an in-progress offseason from saved state (development, retirements and lottery already applied).</summary>
    public static Offseason Restore(League league, Rng rng, OffseasonPhase phase, List<int> draftOrderTeamIds, List<int> pickOwnerTeamIds,
        List<Player> prospects, int nextPickIndex, Dictionary<int, List<Player>> expiring, List<DraftPick> picksMade)
    {
        var outcome = new SeasonOutcome { Standings = new Standings(league) };
        var o = new Offseason(league, outcome, new Dictionary<int, PlayerSeasonStats>(), rng)
        {
            Phase = phase,
            DraftOrder = draftOrderTeamIds.Select(league.TeamById).ToList(),
            PickOwners = pickOwnerTeamIds.Select(league.TeamById).ToList(),
            Prospects = prospects,
            NextPickIndex = nextPickIndex,
        };
        foreach (var kv in expiring) o.Expiring[kv.Key] = kv.Value;
        foreach (var t in league.Teams) o.Expiring.TryAdd(t.Id, new List<Player>());
        o.Report.Draft.AddRange(picksMade);
        return o;
    }

    /// <summary>Headless: every team is AI.</summary>
    public static OffseasonReport RunAll(League league, SeasonOutcome outcome, Dictionary<int, PlayerSeasonStats> stats, Rng rng,
                                         World.GameWorld? world = null, Engine.GameSimulator? worldSim = null)
    {
        var o = new Offseason(league, outcome, stats, rng);
        o.Begin();
        if (world != null) { world.BeginSummer(o.Season + 1, worldSim ?? new Engine.GameSimulator(), league.FreeAgents); World.WorldMarket.Attach(world, o, worldSim); }
        o.AiReSignAll();
        o.AdvanceToDraft();
        o.AutoDraftUntil(null);
        o.AdvanceToFreeAgency();
        if (world != null) World.WorldMarket.AiMoves(world, o, null);
        o.AiFreeAgency(null);
        o.Complete();
        return o.Report;
    }

    // ------------------------------------------------------------------------------------------
    // Phase 1: history, development, retirements, contract expiry, lottery
    // ------------------------------------------------------------------------------------------
    public void Begin()
    {
        if (Phase != OffseasonPhase.NotStarted) return;
        RecordHistory();
        var fin0 = _league.Finance;
        if (fin0.OpenMarket)
        {
            // The books close and owners set next season's budgets (decision #88). No draft rights on the open market.
            Budgets.Initialize(_league.Teams, 1, _rng);
            var champ = _outcome.Playoffs.Count > 0 ? _outcome.Playoffs.Last().Series.Winner : null;
            Budgets.Summer(_league.Teams, 1, LastWinPct, t => t == champ, Season, RealWorld.RealWorldData.IsReal(_league));
            foreach (var p in _league.AllActivePlayers.Concat(_league.FreeAgents)) p.DraftRightsTeamId = null;
            _league.Picks.Clear();
        }

        foreach (var team in _league.Teams)
        {
            double quality = StaffEffects.TrainingQuality(team);
            foreach (var p in team.Roster.ToList())
            {
                double mpg = _stats.TryGetValue(p.Id, out var s) ? s.Mpg : 0;
                team.Training.Focus.TryGetValue(p.Id, out var focus);
                Report.Development.Add(_dev.Develop(p, Date, mpg, quality + StaffEffects.TrainingQualityFor(team, p, Date) - StaffEffects.TrainingQuality(team), team.Training.Focus.ContainsKey(p.Id) ? focus : null));
            }
        }
        foreach (var p in _league.FreeAgents.ToList())
            Report.Development.Add(_dev.Develop(p, Date, 0, 8));

        StaffOffseason();

        foreach (var team in _league.Teams)
        foreach (var p in team.Roster.ToList())
        {
            double mpg = _stats.TryGetValue(p.Id, out var s) ? s.Mpg : 0;
            if (_dev.ShouldRetire(p, Date, mpg)) Retire(team, p);
        }
        foreach (var p in _league.FreeAgents.ToList())
        {
            if (_dev.ShouldRetire(p, Date, 0) || p.CurrentAbility < 60)
            {
                _league.FreeAgents.Remove(p);
                _league.Retired.Add(p);
                Report.Retired.Add(p);
            }
        }

        // Contracts tick down; expiring players wait for a decision.
        foreach (var team in _league.Teams)
        {
            foreach (var p in team.Roster) if (p.Contract != null) p.Contract.Years--;
            Expiring[team.Id] = team.Roster.Where(p => p.Contract == null || p.Contract.Years <= 0)
                .OrderByDescending(p => ContractMarket.Value(p, Date)).ToList();
        }

        if (fin0.OpenMarket)
        {
            // No draft (decision #80): this summer's college graduates join free agency (decision #90).
            DraftOrder = new List<Team>();
            PickOwners = new List<Team>();
            // College graduates join free agency from the world (decision #98); otherwise a generated class (decision #90).
            Prospects = _league.CollegeActive ? new List<Player>() : GenerateGraduates();
        }
        else
        {
            // Lottery and draft order.
            var reverse = _outcome.Standings.All().Select(r => r.Team).Reverse().ToList();
            var playoffTeams = _outcome.Playoffs.Where(x => x.Round.EndsWith("First Round"))
                .SelectMany(x => new[] { x.Series.Winner.Id, x.Series.Loser.Id }).ToHashSet();
            Report.Lottery = DraftLottery.Run(reverse, playoffTeams, _rng);
            DraftOrder = Report.Lottery.Order;
            ResolvePickOwners();
            Prospects = GenerateProspects();
        }

        Phase = OffseasonPhase.ReSigning;
    }

    /// <summary>Map each slot to the team that owns that original team's pick this year, applying protections.</summary>
    private void ResolvePickOwners()
    {
        int draftYear = Season + 1;
        PickOwners = new List<Team>();
        for (int round = 1; round <= 2; round++)
        for (int slot = 0; slot < DraftOrder.Count; slot++)
        {
            var original = DraftOrder[slot];
            var asset = _league.Picks.FirstOrDefault(p => p.Year == draftYear && p.Round == round && p.OriginalTeamId == original.Id);
            Team owner = original;
            if (asset != null)
            {
                if (asset.ProtectedTop > 0 && slot + 1 <= asset.ProtectedTop && asset.OwnerTeamId != original.Id)
                {
                    // Protection hits: original keeps this pick; the owner gets next year's same-round pick instead (rolls once).
                    var next = _league.Picks.FirstOrDefault(p => p.Year == draftYear + 1 && p.Round == round && p.OriginalTeamId == original.Id && p.OwnerTeamId == original.Id);
                    if (next != null) next.OwnerTeamId = asset.OwnerTeamId;
                    Report.ProtectionsTriggered.Add((asset, slot + 1));
                }
                else owner = _league.TeamById(asset.OwnerTeamId);
                _league.Picks.Remove(asset); // consumed
            }
            PickOwners.Add(owner);
        }
    }

    /// <summary>What an expiring player asks for from his current team (open market: his demand in contract talks).</summary>
    public long Ask(Player p) =>
        _league.Finance.OpenMarket && p.TeamId is { } tid && _league.Teams.FirstOrDefault(t => t.Id == tid) is { } team
            ? ContractTalks.Demand(p, team, Date, 1, _league.Finance, renewal: true).Wage
            : ContractMarket.MarketSalary(p.CurrentAbility, p.Age(Date), _league.Finance);

    /// <summary>Payroll committed for next season (players under contract beyond this one).</summary>
    public long CommittedPayroll(Team team) => team.Roster.Where(x => x.Contract != null && x.Contract.Years > 0).Sum(x => x.Contract!.Salary);

    /// <summary>Rookie-scale money this team's first-round pick will cost, so re-signing leaves room for it.</summary>
    public long ReservedForDraft(Team team)
    {
        if (Phase != OffseasonPhase.ReSigning || _league.Finance.OpenMarket) return 0;
        long total = 0;
        for (int slot = 0; slot < DraftOrder.Count && slot < PickOwners.Count; slot++)
            if (PickOwners[slot] == team) total += Math.Max(_league.Finance.MinSalary, (long)(_league.Finance.RookieScaleFirstPick * Math.Pow(0.93, slot)));
        return total;
    }

    /// <summary>
    /// Can the team re-sign one of its own players at this salary? With the soft cap (Bird rights) it may go over the
    /// salary cap, up to the apron; with a hard cap it must stay under the cap.
    /// </summary>
    public bool CanAfford(Team team, long salary) => _league.Finance.OpenMarket
        ? CommittedPayroll(team) + salary <= team.Money.WageBudget
        : CommittedPayroll(team) + ReservedForDraft(team) + salary <= _league.Finance.Apron;

    /// <summary>
    /// The most an AI front office will commit: contenders and teams keeping a star go up to the apron, everyone else
    /// stays under the luxury tax line. (With a hard cap, the cap.)
    /// </summary>
    public long AiPayrollLimit(Team team, bool star)
    {
        var fin = _league.Finance;
        // Open market: the wage budget (a club stretches a little to keep a star).
        if (fin.OpenMarket) return (long)(team.Money.WageBudget * (star ? 1.05 : 1.0));
        if (!fin.SoftCap) return fin.HardCap;
        return star || LastWinPct(team) >= 0.55 ? fin.Apron : fin.TaxLine;
    }

    /// <summary>Re-sign an expiring player at his ask (Bird rights: allowed over the cap, up to the apron).</summary>
    public bool ReSign(Team team, Player p, int? years = null, long? wage = null)
    {
        if (!Expiring.TryGetValue(team.Id, out var list) || !list.Contains(p)) return false;
        long ask = wage ?? Ask(p);
        if (!CanAfford(team, ask)) return false;
        if (WontReSign(p)) return false;
        p.Contract = new Contract { Years = years ?? ContractMarket.ContractLength(p, Date, _rng), Salary = ask, StartSeason = Season + 1 };
        list.Remove(p);
        Report.ReSigned.Add((p, team));
        return true;
    }

    /// <summary>A very unhappy player won't sign a new deal with the team that made him unhappy.</summary>
    public static bool WontReSign(Player p) => p.Morale < 25 || p.TradeRequested;

    public void Release(Team team, Player p)
    {
        if (!Expiring.TryGetValue(team.Id, out var list) || !list.Contains(p)) return;
        team.Roster.Remove(p);
        p.TeamId = null; p.Contract = null;
        _league.FreeAgents.Add(p);
        list.Remove(p);
        Report.ToFreeAgency.Add((p, team));
    }

    /// <summary>Last season's win percentage (0.5 if unknown).</summary>
    public double LastWinPct(Team team) => _outcome.Standings[team] is { Games: > 0 } r ? r.WinPct : 0.5;

    /// <summary>
    /// AI re-signing. Best players are decided first, so cap room goes to them; teams all but always keep stars, and
    /// players are keener to stay on a winning team. (Without this, stars drained from contenders to the teams with
    /// the most cap room every summer and the league flattened out; balance audit, Nov 9.)
    /// </summary>
    public void AiReSign(Team team)
    {
        double winPct = LastWinPct(team);
        foreach (var p in Expiring[team.Id].OrderByDescending(x => ContractMarket.Value(x, Date)).ToList())
        {
            double value = ContractMarket.Value(p, Date);
            double wantToKeep = Math.Clamp((value - 72) / 50.0, 0.05, value >= 140 ? 0.98 : 0.95);
            double wantToStay = Math.Min(0.98, 0.6 + p.Hidden.Loyalty / 20.0 * 0.35 + (winPct >= 0.55 ? 0.15 : winPct >= 0.45 ? 0.05 : 0))
                                * Math.Clamp(0.55 + p.Morale / 130.0, 0.4, 1.0); // happy players stay
            long ask = Ask(p);
            bool withinBudget = CommittedPayroll(team) + ReservedForDraft(team) + ask <= AiPayrollLimit(team, star: value >= 140);
            if (CanAfford(team, ask) && withinBudget && _rng.Chance(wantToKeep * wantToStay)) ReSign(team, p);
            else Release(team, p);
        }
    }

    public void AiReSignAll(int? exceptTeamId = null)
    {
        foreach (var team in _league.Teams) if (team.Id != exceptTeamId) AiReSign(team);
    }

    // ------------------------------------------------------------------------------------------
    // Phase 2: draft
    // ------------------------------------------------------------------------------------------
    public void AdvanceToDraft()
    {
        if (Phase != OffseasonPhase.ReSigning) return;
        // Anyone still undecided is released.
        foreach (var team in _league.Teams) foreach (var p in Expiring[team.Id].ToList()) Release(team, p);
        Phase = OffseasonPhase.Draft;
        // Open market: no draft; straight on to free agency, where the graduates are waiting.
        if (_league.Finance.OpenMarket) AdvanceToFreeAgency();
    }

    /// <summary>
    /// This summer's college graduates (decision #90): 23-year-olds, mostly American, from a few ready-made players to
    /// many long shots. Until the college game exists (stage B) they are generated here.
    /// </summary>
    private List<Player> GenerateGraduates()
    {
        var gen = new PlayerGenerator(_rng, _league.NextPlayerId);
        var grads = new List<Player>();
        var positions = Enum.GetValues<Position>();
        for (int i = 0; i < 70; i++)
        {
            double u = _rng.NextDouble();
            int ca = u < 0.06 ? _rng.Next(110, 128) : u < 0.32 ? _rng.Next(90, 110) : _rng.Next(60, 92);
            var p = gen.Generate(positions[_rng.Next(5)], ca, 23, Date);
            // Same restraint as the old draft classes: a few future stars a year, not a flood (issue #9).
            p.PotentialAbility = Math.Clamp(p.PotentialAbility - _rng.Next(0, 9), p.CurrentAbility, 200);
            p.RookieSeason = Season + 1;
            grads.Add(p);
        }
        _league.NextPlayerId = gen.NextId;
        return grads.OrderByDescending(p => ContractMarket.Value(p, Date)).ToList();
    }

    private List<Player> GenerateProspects()
    {
        var gen = new PlayerGenerator(_rng, _league.NextPlayerId);
        var prospects = new List<Player>();
        var positions = Enum.GetValues<Position>();
        for (int i = 0; i < 75; i++)
        {
            int age = _rng.Next(19, 23);
            double u = _rng.NextDouble();
            int ca = u < 0.05 ? _rng.Next(105, 125) : u < 0.30 ? _rng.Next(85, 105) : _rng.Next(55, 88);
            var p = gen.Generate(positions[_rng.Next(5)], ca, age, Date);
            // Prospects get the generator's potential less a little: the NABL also gains about ten players a summer from
            // overseas, so its own classes must hold fewer future stars or the league's talent creeps up (issue #9).
            // (A +0-25 potential bonus here gave ~6 future stars a class against ~3 the league can hold.)
            p.PotentialAbility = Math.Clamp(p.PotentialAbility - _rng.Next(0, 9), p.CurrentAbility, 200);
            p.RookieSeason = Season + 1;
            prospects.Add(p);
        }
        _league.NextPlayerId = gen.NextId;
        return prospects.OrderByDescending(p => ContractMarket.Value(p, Date)).ToList();
    }

    public double AiBoardScore(Team team, Player prospect) =>
        ContractMarket.Value(prospect, Date) + PositionNeed(team, prospect) * 4;

    /// <summary>Make the current pick for the team on the clock.</summary>
    public DraftPick MakePick(Player prospect)
    {
        if (Phase != OffseasonPhase.Draft || DraftComplete) throw new InvalidOperationException("No pick on the clock");
        if (!Prospects.Remove(prospect)) throw new InvalidOperationException("Prospect not available");
        var team = OnTheClock!;
        int round = CurrentRound;
        int overall = NextPickIndex + 1;
        var fin = _league.Finance;
        var pick = new DraftPick(round, overall, team, prospect, OriginalTeamAt(NextPickIndex));
        Report.Draft.Add(pick);
        prospect.Draft = new DraftRecord(Date.Year, round, overall);
        if (round == 1)
        {
            long salary = (long)(fin.RookieScaleFirstPick * Math.Pow(0.93, overall - 1));
            prospect.Contract = new Contract { Years = 4, Salary = Math.Max(salary, fin.MinSalary), StartSeason = Season + 1, TeamOption = true };
            prospect.TeamId = team.Id;
            team.Roster.Add(prospect);
        }
        else
        {
            // Second-round picks are unguaranteed: they go to the pool and must earn a deal in free agency.
            _league.FreeAgents.Add(prospect);
        }
        NextPickIndex++;
        return pick;
    }

    public DraftPick AutoPick()
    {
        var team = OnTheClock ?? throw new InvalidOperationException("Draft complete");
        var best = Prospects.OrderByDescending(p => AiBoardScore(team, p) + _rng.NextGaussian(0, 6)).First();
        return MakePick(best);
    }

    /// <summary>Run AI picks until the given team is on the clock (or the draft ends). Null = run everything.</summary>
    public void AutoDraftUntil(int? humanTeamId)
    {
        while (!DraftComplete && OnTheClock!.Id != humanTeamId) AutoPick();
    }

    // ------------------------------------------------------------------------------------------
    // Phase 3: free agency
    // ------------------------------------------------------------------------------------------
    public void AdvanceToFreeAgency()
    {
        if (Phase != OffseasonPhase.Draft) return;
        AutoDraftUntil(null);
        // Undrafted prospects try their luck in free agency.
        _league.FreeAgents.AddRange(Prospects);
        Prospects = new List<Player>();
        Phase = OffseasonPhase.FreeAgency;
    }

    /// <summary>Cap room: how far the payroll is under the salary cap.</summary>
    public long Room(Team team) => _league.Finance.OpenMarket ? Budgets.Room(team) : _league.Finance.SalaryCap - ContractMarket.Payroll(team);

    /// <summary>Teams that have used their mid-level exception this offseason.</summary>
    public HashSet<int> MidLevelUsed { get; } = new();

    /// <summary>
    /// How a team could sign a free agent asking <paramref name="ask"/>: with cap room; with the mid-level exception
    /// (soft cap, once per offseason, up to the apron); or on a minimum deal (players asking up to twice the minimum
    /// accept one; a team below the roster minimum may always add one). Returns the route and the salary.
    /// </summary>
    public (SigningRoute Route, long Salary) SigningOption(Team team, long ask)
    {
        var fin = _league.Finance;
        long payroll = ContractMarket.Payroll(team);
        if (fin.OpenMarket)
        {
            if (ask <= Room(team)) return (SigningRoute.Budget, ask);
            // A club short of players may always add a minimum deal.
            if (ask <= fin.MinSalary * 2 && team.Roster.Count < fin.RosterMin) return (SigningRoute.Minimum, fin.MinSalary);
            return (SigningRoute.None, 0);
        }
        if (ask <= Room(team)) return (SigningRoute.CapRoom, ask);
        if (fin.SoftCap && !MidLevelUsed.Contains(team.Id) && ask <= fin.MidLevel && payroll + ask <= fin.Apron) return (SigningRoute.MidLevel, ask);
        bool mustFill = team.Roster.Count < fin.RosterMin;
        if (ask <= fin.MinSalary * 2 && (mustFill || (fin.SoftCap && payroll + fin.MinSalary <= fin.Apron))) return (SigningRoute.Minimum, fin.MinSalary);
        return (SigningRoute.None, 0);
    }

    /// <summary>Sign a free agent at market salary, using cap room, the mid-level exception or a minimum deal.</summary>
    public bool SignFreeAgent(Team team, Player p, int? years = null, long? wage = null)
    {
        if (!_league.FreeAgents.Contains(p) || team.Roster.Count >= _league.Finance.RosterMax) return false;
        if (p.DraftRightsTeamId is { } rights && rights != team.Id) return false; // another team holds his draft rights
        var fin = _league.Finance;
        long ask = wage ?? ContractMarket.MarketSalary(p.CurrentAbility, p.Age(Date), fin);
        var (route, salary) = SigningOption(team, ask);
        if (route == SigningRoute.None) return false;
        if (route == SigningRoute.MidLevel) MidLevelUsed.Add(team.Id);

        p.Contract = new Contract { Years = years ?? ContractMarket.ContractLength(p, Date, _rng), Salary = salary, StartSeason = Season + 1 };
        p.TeamId = team.Id;
        p.DraftRightsTeamId = null;
        team.Roster.Add(p);
        _league.FreeAgents.Remove(p);
        Report.FreeAgentSignings.Add((p, team, salary, p.Contract.Years));
        return true;
    }

    public void Waive(Team team, Player p)
    {
        if (!team.Roster.Remove(p)) return;
        p.TeamId = null; p.Contract = null;
        _league.FreeAgents.Add(p);
        Report.Waived.Add((p, team));
    }

    /// <summary>An AI club signs a free agent only if it fits the cap and (except minimum deals) its budget, as in AI free agency.</summary>
    public bool AiSignIfAffordable(Team team, Player p)
    {
        long ask = ContractMarket.MarketSalary(p.CurrentAbility, p.Age(Date), _league.Finance);
        var (route, salary) = SigningOption(team, ask);
        if (route == SigningRoute.None || route != SigningRoute.Minimum && ContractMarket.Payroll(team) + salary > AiPayrollLimit(team, false)) return false;
        return SignFreeAgent(team, p);
    }

    /// <summary>AI teams fill their rosters; the human team (if any) is skipped.</summary>
    public void AiFreeAgency(int? humanTeamId)
    {
        var fin = _league.Finance;
        // Would this AI team make this signing? It must be possible, and (except minimum deals) within its budget.
        bool AiCan(Team t, long ask, bool star)
        {
            var (route, salary) = SigningOption(t, ask);
            return route != SigningRoute.None && (route == SigningRoute.Minimum || ContractMarket.Payroll(t) + salary <= AiPayrollLimit(t, star));
        }
        // The best free agents choose where to go: among teams that can pay them, they lean toward winners.
        foreach (var p in _league.FreeAgents.Where(p => ContractMarket.Value(p, Date) >= 120).OrderByDescending(p => ContractMarket.Value(p, Date)).ToList())
        {
            long ask = ContractMarket.MarketSalary(p.CurrentAbility, p.Age(Date), fin);
            var choice = _league.Teams
                .Where(t => t.Id != humanTeamId && t.Roster.Count < fin.RosterMax && AiCan(t, ask, star: false) && SigningOption(t, ask).Route != SigningRoute.Minimum)
                .OrderByDescending(t => LastWinPct(t) + _rng.NextDouble() * 0.35 + PositionNeed(t, p) * 0.05)
                .FirstOrDefault();
            if (choice != null) SignFreeAgent(choice, p);
        }
        for (int round = 0; round < 6; round++)
        {
            var teams = _league.Teams.Where(t => t.Id != humanTeamId && t.Roster.Count < fin.RosterMax)
                .OrderBy(t => t.Roster.Count).ThenByDescending(Room).ToList();
            bool any = false;
            foreach (var team in teams)
            {
                var pick = _league.FreeAgents
                    .Select(p => (p, ask: ContractMarket.MarketSalary(p.CurrentAbility, p.Age(Date), fin)))
                    .Where(x => AiCan(team, x.ask, star: false))
                    .OrderByDescending(x => ContractMarket.Value(x.p, Date) + PositionNeed(team, x.p) * 5)
                    .FirstOrDefault();
                if (pick.p == null) continue;
                if (SignFreeAgent(team, pick.p)) any = true;
            }
            if (!any) break;
        }
    }

    // ------------------------------------------------------------------------------------------
    // Phase 4: compliance and roll the calendar
    // ------------------------------------------------------------------------------------------
    public void Complete()
    {
        if (Phase == OffseasonPhase.Complete) return;
        if (Phase < OffseasonPhase.FreeAgency) { AdvanceToDraft(); AdvanceToFreeAgency(); }
        var fin = _league.Finance;

        // Every team, human included, must be legal: trim over the roster max, get under the apron (the cap itself
        // under a hard cap), reach the minimum.
        foreach (var team in _league.Teams)
        {
            if (fin.OpenMarket)
            {
                // Open market: only the roster limit is enforced; a club over its budget pays for it from its balance.
                while (team.Roster.Count > fin.RosterMax) Waive(team, team.Roster.OrderBy(p => ContractMarket.Value(p, Date)).First());
                continue;
            }
            while (team.Roster.Count > fin.RosterMax ||
                   (ContractMarket.Payroll(team) > fin.Apron && team.Roster.Count > fin.RosterMin))
            {
                var cut = ContractMarket.Payroll(team) > fin.Apron
                    ? team.Roster.OrderBy(p => ContractMarket.Value(p, Date) - (p.Contract?.Salary ?? 0) / 1e6 * 1.5).First()
                    : team.Roster.OrderBy(p => ContractMarket.Value(p, Date)).First();
                Waive(team, cut);
            }

            // Leave room for the minimum signings still needed to reach the roster minimum: those may take a team
            // over the apron (the cap, under a hard cap) by at most two minimum deals. If they would go further,
            // release the worst-value contract (an expensive player replaced by a minimum deal lowers the payroll).
            long allowance = fin.Apron + fin.MinSalary * 2;
            while (ContractMarket.Payroll(team) + Math.Max(0, fin.RosterMin - team.Roster.Count) * fin.MinSalary > allowance)
            {
                var cut = team.Roster.Where(p => (p.Contract?.Salary ?? 0) > fin.MinSalary)
                    .OrderBy(p => ContractMarket.Value(p, Date) - (p.Contract?.Salary ?? 0) / 1e6 * 1.5).FirstOrDefault();
                if (cut == null) break;
                Waive(team, cut);
            }
        }
        AiFreeAgency(null);
        AiHireStaff(null);

        // The rest of the world signs from the pool before the leftovers retire (decision #41).
        BeforeFreeAgentCleanup?.Invoke(this);

        foreach (var p in _league.FreeAgents.ToList())
        {
            if (p.CurrentAbility < 80 || p.Age(Date) >= 35) { _league.FreeAgents.Remove(p); _league.Retired.Add(p); Report.LeftUnsigned.Add(p); }
        }
        foreach (var p in _league.FreeAgents.OrderByDescending(p => ContractMarket.Value(p, Date)).Skip(90).ToList())
        {
            _league.FreeAgents.Remove(p); _league.Retired.Add(p); Report.LeftUnsigned.Add(p);
        }

        // Injuries heal over the summer unless they run past opening night.
        foreach (var p in _league.AllActivePlayers)
            if (p.Injury != null && p.Injury.ReturnDate <= new DateOnly(Season + 1, 10, 15)) p.Injury = null;

        // New season, fresh start: moods settle and trade requests lapse (unhappy players have had their chance to leave).
        People.PeopleEngine.Summer(_league);
        _league.Season = Season + 1;
        if (!fin.OpenMarket) _league.EnsurePicks();
        Phase = OffseasonPhase.Complete;
    }

    // ------------------------------------------------------------------------------------------
    public SeasonSummary? Summary { get; private set; }

    /// <summary>Runs at the end of free agency, before unsigned players retire: overseas clubs sign from the pool here.</summary>
    public Action<Offseason>? BeforeFreeAgentCleanup { get; set; }
    public League League => _league;

    private void RecordHistory()
    {
        var finals = _outcome.Playoffs.Last().Series;
        var scoring = _stats.Values.Where(s => s.Games >= 50).OrderByDescending(s => s.Ppg).FirstOrDefault();
        var awards = new AwardsEngine(_league, _outcome.Standings, _stats, _rng).EndOfSeason(_league.Season, _outcome.Midseason);

        // Finals MVP: best scorer on the champions across the Finals games.
        int? finalsMvp = null;
        if (finals.Games.Count > 0)
        {
            var totals = finals.Games.SelectMany(g => new[] { g.Home, g.Away }).Where(b => b.Team == finals.Winner).SelectMany(b => b.Lines)
                .GroupBy(l => l.Player.Id).Select(g => (id: g.Key, score: g.Sum(l => l.Points + l.Rebounds * 0.7 + l.Assists * 0.9)))
                .OrderByDescending(x => x.score).FirstOrDefault();
            if (totals.id != 0) finalsMvp = totals.id;
        }

        Summary = new SeasonSummary
        {
            Season = _league.Season,
            ChampionTeamId = finals.Winner.Id,
            RunnerUpTeamId = finals.Loser.Id,
            Records = _league.Teams.Select(t => (t.Id, _outcome.Standings[t].Wins, _outcome.Standings[t].Losses)).ToList(),
            ScoringLeaderPlayerId = scoring?.PlayerId,
            MvpPlayerId = awards.Mvp,
            FinalsMvpPlayerId = finalsMvp,
            Awards = awards,
        };
        _league.History.Add(Summary);
        AccumulateCareers(Summary);
    }

    private void AccumulateCareers(SeasonSummary summary)
    {
        foreach (var s in _stats.Values)
        {
            if (!_league.Careers.TryGetValue(s.PlayerId, out var c)) _league.Careers[s.PlayerId] = c = new CareerTotals { PlayerId = s.PlayerId };
            c.Seasons++; c.Games += s.Games; c.Points += s.Points; c.Rebounds += s.OReb + s.DReb; c.Assists += s.Assists;
            c.Steals += s.Steals; c.Blocks += s.Blocks; c.Minutes += s.Minutes;
            c.GamesByTeam[s.TeamId] = c.GamesByTeam.GetValueOrDefault(s.TeamId) + s.Games;
        }
        var a = summary.Awards!;
        void Bump(int? id, Action<CareerTotals> act) { if (id is { } i && _league.Careers.TryGetValue(i, out var c)) act(c); }
        Bump(a.Mvp, c => c.Mvps++); Bump(a.Dpoy, c => c.Dpoys++); Bump(a.Roy, c => c.Roys++); Bump(a.SixthMan, c => c.SixthMen++);
        Bump(summary.FinalsMvpPlayerId, c => c.FinalsMvps++);
        foreach (var id in a.AllLeague.SelectMany(t => t)) Bump(id, c => c.AllLeague++);
        foreach (var id in a.AllStarsEast.Concat(a.AllStarsWest)) Bump(id, c => c.AllStars++);
        var champ = _league.TeamById(summary.ChampionTeamId);
        foreach (var p in champ.Roster) Bump(p.Id, c => c.Titles++);
    }

    public double HallOfFameThreshold { get; set; } = 60;

    /// <summary>Staff contracts tick down; AI teams re-sign good staff or hire from the market; retirements and new faces.</summary>
    private void StaffOffseason()
    {
        var gen = new Generation.StaffGenerator(_rng);
        foreach (var team in _league.Teams)
        {
            foreach (var st in team.Staff.ToList())
            {
                st.ContractYears--;
                if (st.ContractYears > 0) continue;
                if (team.Id == HumanTeamId)
                {
                    // Not extended during the season: he moves on.
                    team.Staff.Remove(st); st.TeamId = null; _league.FreeStaff.Add(st); Report.StaffLeft.Add(st);
                    continue;
                }
                bool keep = st.KeyAttribute >= 10 && _rng.Chance(0.75);
                if (keep) { st.ContractYears = _rng.Next(1, 4); st.Salary = Generation.StaffGenerator.SalaryFor(st); }
                else { team.Staff.Remove(st); st.TeamId = null; _league.FreeStaff.Add(st); Report.StaffReleased.Add((st, team)); }
            }
        }
        // Old staff retire; the market gets fresh candidates.
        foreach (var st in _league.FreeStaff.ToList())
            if (st.Age(Date) >= 65 || _rng.Chance(0.15)) _league.FreeStaff.Remove(st);
        for (int i = 0; i < 12; i++)
            _league.FreeStaff.Add(gen.Generate(_league, Enum.GetValues<StaffRole>()[_rng.Next(5)], Date, (int)Math.Round(Math.Clamp(_rng.NextGaussian(10, 3), 3, 18))));
    }

    /// <summary>AI teams fill empty staff roles with the best available; the human team is left alone.</summary>
    public void AiHireStaff(int? humanTeamId)
    {
        foreach (var team in _league.Teams.Where(t => t.Id != humanTeamId))
        foreach (var role in Enum.GetValues<StaffRole>())
        {
            if (team.Staff.Any(s => s.Role == role)) continue;
            var best = _league.FreeStaff.Where(s => s.Role == role).OrderByDescending(s => s.KeyAttribute).FirstOrDefault();
            if (best == null) continue;
            Hire(team, best);
        }
    }

    public void Hire(Team team, Staff st)
    {
        if (!_league.FreeStaff.Remove(st)) return;
        var existing = team.Staff.FirstOrDefault(s => s.Role == st.Role);
        if (existing != null) Fire(team, existing);
        st.TeamId = team.Id;
        st.ContractYears = Math.Max(1, st.ContractYears);
        team.Staff.Add(st);
        Report.StaffHired.Add((st, team));
    }

    public void Fire(Team team, Staff st)
    {
        if (!team.Staff.Remove(st)) return;
        st.TeamId = null;
        _league.FreeStaff.Add(st);
        Report.StaffReleased.Add((st, team));
    }

    private void Retire(Team team, Player p)
    {
        team.Roster.Remove(p);
        p.TeamId = null; p.Contract = null;
        _league.Retired.Add(p);
        Report.Retired.Add(p);
        ConsiderHallOfFame(p);
    }

    private void ConsiderHallOfFame(Player p)
    {
        if (!_league.Careers.TryGetValue(p.Id, out var c)) return;
        if (c.HallScore < HallOfFameThreshold) return;
        _league.HallOfFame.Add(new HallOfFamer(p.Id, Season + 1, Math.Round(c.HallScore, 1)));
        Report.HallOfFame.Add(p);
        // A franchise retires the number of a Hall of Famer who gave it at least six seasons' worth of games.
        // (A real player's career before the game has no games by team; only games played here count. R2 part 2b.)
        if (c.GamesByTeam.Count == 0) return;
        var home = c.GamesByTeam.OrderByDescending(kv => kv.Value).First();
        // (His franchise may since have been relegated out of the NABL; a club down the pyramid doesn't retire numbers.
        // Looking it up in the NABL alone crashed the summer, 35 seasons into a 100-season run; critic audit, Nov 10.)
        if (home.Value >= 6 * 60 && _league.Teams.FirstOrDefault(t => t.Id == home.Key) is { } team)
        {
            if (!team.RetiredNumbers.Contains(p.Id)) { team.RetiredNumbers.Add(p.Id); Report.RetiredNumbers.Add((p, team)); }
        }
    }

    private static int PositionNeed(Team team, Player p)
    {
        int count = team.Roster.Count(x => x.PrimaryPosition == p.PrimaryPosition);
        return count <= 1 ? 2 : count == 2 ? 1 : 0;
    }
}
