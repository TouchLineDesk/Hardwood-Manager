using Hardwood.Core.Generation;
using Hardwood.Core.Model;

namespace Hardwood.Core.Season;

public sealed record DraftPick(int Round, int Pick, Team Team, Player Player, Team? OriginalTeam = null);

public enum OffseasonPhase { NotStarted, ReSigning, Draft, FreeAgency, Complete }

public sealed class OffseasonReport
{
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
    public static OffseasonReport RunAll(League league, SeasonOutcome outcome, Dictionary<int, PlayerSeasonStats> stats, Rng rng)
    {
        var o = new Offseason(league, outcome, stats, rng);
        o.Begin();
        o.AiReSignAll();
        o.AdvanceToDraft();
        o.AutoDraftUntil(null);
        o.AdvanceToFreeAgency();
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

        foreach (var p in _league.AllActivePlayers.Concat(_league.FreeAgents).ToList())
        {
            double mpg = _stats.TryGetValue(p.Id, out var s) ? s.Mpg : 0;
            Report.Development.Add(_dev.Develop(p, Date, mpg));
        }

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

        // Lottery and draft order.
        var reverse = _outcome.Standings.All().Select(r => r.Team).Reverse().ToList();
        var playoffTeams = _outcome.Playoffs.Where(x => x.Round.EndsWith("First Round"))
            .SelectMany(x => new[] { x.Series.Winner.Id, x.Series.Loser.Id }).ToHashSet();
        Report.Lottery = DraftLottery.Run(reverse, playoffTeams, _rng);
        DraftOrder = Report.Lottery.Order;
        ResolvePickOwners();
        Prospects = GenerateProspects();

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

    /// <summary>What an expiring player asks for from his current team.</summary>
    public long Ask(Player p) => ContractMarket.MarketSalary(p.CurrentAbility, p.Age(Date), _league.Finance);

    /// <summary>Payroll committed for next season (players under contract beyond this one).</summary>
    public long CommittedPayroll(Team team) => team.Roster.Where(x => x.Contract != null && x.Contract.Years > 0).Sum(x => x.Contract!.Salary);

    /// <summary>Rookie-scale money this team's first-round pick will cost, so re-signing leaves room for it.</summary>
    public long ReservedForDraft(Team team)
    {
        if (Phase != OffseasonPhase.ReSigning) return 0;
        long total = 0;
        for (int slot = 0; slot < DraftOrder.Count && slot < PickOwners.Count; slot++)
            if (PickOwners[slot] == team) total += Math.Max(_league.Finance.MinSalary, (long)(_league.Finance.RookieScaleFirstPick * Math.Pow(0.93, slot)));
        return total;
    }

    public bool CanAfford(Team team, long salary) => CommittedPayroll(team) + ReservedForDraft(team) + salary <= _league.Finance.HardCap;

    /// <summary>Re-sign an expiring player at his ask. Returns false if it doesn't fit under the cap.</summary>
    public bool ReSign(Team team, Player p, int? years = null)
    {
        if (!Expiring.TryGetValue(team.Id, out var list) || !list.Contains(p)) return false;
        long ask = Ask(p);
        if (!CanAfford(team, ask)) return false;
        p.Contract = new Contract { Years = years ?? ContractMarket.ContractLength(p, Date, _rng), Salary = ask, StartSeason = Season + 1 };
        list.Remove(p);
        Report.ReSigned.Add((p, team));
        return true;
    }

    public void Release(Team team, Player p)
    {
        if (!Expiring.TryGetValue(team.Id, out var list) || !list.Contains(p)) return;
        team.Roster.Remove(p);
        p.TeamId = null; p.Contract = null;
        _league.FreeAgents.Add(p);
        list.Remove(p);
        Report.ToFreeAgency.Add((p, team));
    }

    public void AiReSign(Team team)
    {
        foreach (var p in Expiring[team.Id].ToList())
        {
            double wantToKeep = Math.Clamp((ContractMarket.Value(p, Date) - 72) / 50.0, 0.05, 0.95);
            double wantToStay = 0.6 + p.Hidden.Loyalty / 20.0 * 0.35;
            if (CanAfford(team, Ask(p)) && _rng.Chance(wantToKeep * wantToStay)) ReSign(team, p);
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
            p.PotentialAbility = Math.Clamp(p.PotentialAbility + _rng.Next(0, 25), p.CurrentAbility, 200);
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

    public long Room(Team team) => _league.Finance.HardCap - ContractMarket.Payroll(team);

    /// <summary>Sign a free agent at market salary. Below the roster minimum a minimum deal is allowed regardless of room.</summary>
    public bool SignFreeAgent(Team team, Player p, int? years = null)
    {
        if (!_league.FreeAgents.Contains(p) || team.Roster.Count >= _league.Finance.RosterMax) return false;
        var fin = _league.Finance;
        long ask = ContractMarket.MarketSalary(p.CurrentAbility, p.Age(Date), fin);
        long room = Room(team);
        bool mustFill = team.Roster.Count < fin.RosterMin;
        long salary;
        if (ask <= room) salary = ask;
        else if (mustFill && ask <= fin.MinSalary * 2) salary = fin.MinSalary;
        else return false;

        p.Contract = new Contract { Years = years ?? ContractMarket.ContractLength(p, Date, _rng), Salary = salary, StartSeason = Season + 1 };
        p.TeamId = team.Id;
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

    /// <summary>AI teams fill their rosters; the human team (if any) is skipped.</summary>
    public void AiFreeAgency(int? humanTeamId)
    {
        var fin = _league.Finance;
        for (int round = 0; round < 6; round++)
        {
            var teams = _league.Teams.Where(t => t.Id != humanTeamId && t.Roster.Count < fin.RosterMax)
                .OrderBy(t => t.Roster.Count).ThenByDescending(Room).ToList();
            bool any = false;
            foreach (var team in teams)
            {
                long room = Room(team);
                bool mustFill = team.Roster.Count < fin.RosterMin;
                var pick = _league.FreeAgents
                    .Select(p => (p, ask: ContractMarket.MarketSalary(p.CurrentAbility, p.Age(Date), fin)))
                    .Where(x => x.ask <= room || (mustFill && x.ask <= fin.MinSalary * 2))
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

        // Every team, human included, must be legal: trim over the roster max, get under the cap, reach the minimum.
        foreach (var team in _league.Teams)
        {
            while (team.Roster.Count > fin.RosterMax ||
                   (ContractMarket.Payroll(team) > fin.HardCap && team.Roster.Count > fin.RosterMin))
            {
                var cut = ContractMarket.Payroll(team) > fin.HardCap
                    ? team.Roster.OrderBy(p => ContractMarket.Value(p, Date) - (p.Contract?.Salary ?? 0) / 1e6 * 1.5).First()
                    : team.Roster.OrderBy(p => ContractMarket.Value(p, Date)).First();
                Waive(team, cut);
            }
        }
        AiFreeAgency(null);

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

        _league.Season = Season + 1;
        _league.EnsurePicks();
        Phase = OffseasonPhase.Complete;
    }

    // ------------------------------------------------------------------------------------------
    private void RecordHistory()
    {
        var finals = _outcome.Playoffs.Last().Series;
        var scoring = _stats.Values.Where(s => s.Games >= 50).OrderByDescending(s => s.Ppg).FirstOrDefault();
        var teamWins = _league.Teams.ToDictionary(t => t.Id, t => _outcome.Standings[t].Wins);
        var mvp = _stats.Values.Where(s => s.Games >= 55 && teamWins[s.TeamId] >= 41)
            .OrderByDescending(s => s.Ppg + s.Rpg * 1.2 + s.Apg * 1.5 + s.PlusMinus / 20.0).FirstOrDefault();

        _league.History.Add(new SeasonSummary
        {
            Season = _league.Season,
            ChampionTeamId = finals.Winner.Id,
            RunnerUpTeamId = finals.Loser.Id,
            Records = _league.Teams.Select(t => (t.Id, _outcome.Standings[t].Wins, _outcome.Standings[t].Losses)).ToList(),
            ScoringLeaderPlayerId = scoring?.PlayerId,
            MvpPlayerId = mvp?.PlayerId,
        });
    }

    private void Retire(Team team, Player p)
    {
        team.Roster.Remove(p);
        p.TeamId = null; p.Contract = null;
        _league.Retired.Add(p);
        Report.Retired.Add(p);
    }

    private static int PositionNeed(Team team, Player p)
    {
        int count = team.Roster.Count(x => x.PrimaryPosition == p.PrimaryPosition);
        return count <= 1 ? 2 : count == 2 ? 1 : 0;
    }
}
