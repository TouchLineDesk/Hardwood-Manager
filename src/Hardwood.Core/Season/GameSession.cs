using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Persistence;

namespace Hardwood.Core.Season;

public enum SessionPhase { Preseason, RegularSeason, Postseason, Offseason }

public sealed class InboxMessage
{
    public int Id { get; init; }
    public DateOnly Date { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
    public string Category { get; init; } = "News";
    public bool Read { get; set; }
}

/// <summary>
/// A playable career: one human-controlled team in a living league, advanced one day at a time.
/// Owns the calendar, schedule, standings, stats, inbox, postseason and offseason, and saves after every day.
/// </summary>
public sealed class GameSession
{
    private readonly Rng _rng;
    private readonly GameSimulator _sim;
    private int _nextMessageId = 1;

    public League League { get; }
    /// <summary>Every competition outside the NABL (decision #38), played on the same calendar.</summary>
    public World.GameWorld World { get; }
    /// <summary>Leagues that get the full match engine when the player doesn't choose (the Euro Elite League).</summary>
    public static readonly string[] DefaultFullSim = { Hardwood.Core.World.Competitions.Elite };
    private readonly bool _worldGenerated;
    public Team HumanTeam { get; private set; }
    public DateOnly Today { get; private set; }
    public SessionPhase Phase { get; private set; }
    public SaveFile? Save { get; set; }

    public List<ScheduledGame> Schedule { get; private set; } = new();
    public Dictionary<int, GameResult> Results { get; } = new();          // by schedule index
    public Standings Standings { get; private set; }
    public Dictionary<int, PlayerSeasonStats> Stats { get; private set; } = new();
    public List<InboxMessage> Inbox { get; } = new();
    public Postseason? Postseason { get; private set; }
    public Offseason? Offseason { get; private set; }
    public List<TradeProposal> TradesThisSeason { get; } = new();
    /// <summary>All-Star selections and game result for the current season, once February arrives.</summary>
    public SeasonAwards? Midseason { get; set; }
    public DateOnly AllStarDate => new(League.Season + 1, 2, 14);
    /// <summary>A fresh seed for the RNG on reload, so a loaded game doesn't replay the same future.</summary>
    public ulong NextRngSeed => _rng.NextULong();
    public bool TradeDeadlinePassed => Phase != SessionPhase.RegularSeason && Phase != SessionPhase.Preseason || Today > TradeDeadline;

    public DateOnly OpeningNight => new(League.Season, 10, 21);
    public DateOnly TradeDeadline => new(League.Season + 1, 2, 6);
    public ulong Seed { get; }

    public GameSession(League league, Team humanTeam, ulong seed, SaveFile? save = null, EngineConfig? engine = null, ulong? rngState = null,
                       World.GameWorld? world = null)
    {
        League = league; HumanTeam = humanTeam; Seed = seed; Save = save;
        // A save from before the world existed (or a test) gets one built around its NABL; it catches up on load.
        _worldGenerated = world == null;
        World = world ?? Hardwood.Core.World.GameWorld.Generate(league, seed, DefaultFullSim, new DateOnly(league.Season, 10, 1));
        _rng = new Rng(rngState ?? (seed ^ 0x5E5510));
        _sim = new GameSimulator(engine);
        Standings = new Standings(league);
        Today = OpeningNight.AddDays(-7);
        Phase = SessionPhase.Preseason;
        BuildSeason();
    }

    /// <summary>Start a new career from a generated world.</summary>
    public static GameSession NewGame(ulong seed, int humanTeamId, string? savePath, int season = 2026, IEnumerable<string>? fullSim = null)
    {
        var league = new WorldGenerator(new Rng(seed)).Generate("North American Basketball League", season, new DateOnly(season, 10, 1));
        var world = Hardwood.Core.World.GameWorld.Generate(league, seed, fullSim ?? DefaultFullSim, new DateOnly(season, 10, 1));
        var session = new GameSession(league, league.TeamById(humanTeamId), seed, savePath != null ? new SaveFile(savePath) : null, world: world);
        session.PlayWorld(session.Today.AddDays(-1));
        session.Post("Welcome to the job", $"You are the new head coach and general manager of the {session.HumanTeam.FullName}. Preseason runs until {session.OpeningNight:MMM d}: set your rotation and tactics, then press Continue.", "Board");
        session.SaveNow();
        return session;
    }

    // ------------------------------------------------------------------------------------------
    private void BuildSeason()
    {
        Schedule = ScheduleBuilder.Build(League, OpeningNight, _rng);
        Results.Clear();
        Standings = new Standings(League);
        Stats = new Dictionary<int, PlayerSeasonStats>();
        Postseason = null;
        Offseason = null;
        TradesThisSeason.Clear();
    }

    public IEnumerable<ScheduledGame> GamesOn(DateOnly date) => Schedule.Where(g => g.Date == date);
    public ScheduledGame? NextHumanGame => Schedule.FirstOrDefault(g => g.Date >= Today && (g.Home == HumanTeam || g.Away == HumanTeam) && !Results.ContainsKey(g.Index));
    public IEnumerable<(ScheduledGame game, GameResult result)> HumanResults =>
        Schedule.Where(g => (g.Home == HumanTeam || g.Away == HumanTeam) && Results.ContainsKey(g.Index)).Select(g => (g, Results[g.Index]));
    public Series? HumanSeries => Postseason?.Series.LastOrDefault(s => s.Higher == HumanTeam || s.Lower == HumanTeam);

    // ------------------------------------------------------------------------------------------
    // Staff
    // ------------------------------------------------------------------------------------------
    public bool HireStaff(Staff st)
    {
        if (!League.FreeStaff.Remove(st)) return false;
        var existing = HumanTeam.Staff.FirstOrDefault(x => x.Role == st.Role);
        if (existing != null) { HumanTeam.Staff.Remove(existing); existing.TeamId = null; League.FreeStaff.Add(existing); }
        st.TeamId = HumanTeam.Id;
        st.ContractYears = Math.Max(1, st.ContractYears);
        HumanTeam.Staff.Add(st);
        Post($"Hired {st.Name}", $"{st.Name} joins as {Staff.RoleName(st.Role).ToLower()} on a {st.ContractYears}-year deal at ${st.Salary / 1e6:F2}M." + (existing != null ? $" {existing.Name} has been let go." : ""), "Staff");
        SaveNow();
        return true;
    }

    public void FireStaff(Staff st)
    {
        if (!HumanTeam.Staff.Remove(st)) return;
        st.TeamId = null;
        League.FreeStaff.Add(st);
        Post($"Released {st.Name}", $"{Staff.RoleName(st.Role)} {st.Name} has left the organisation.", "Staff");
        SaveNow();
    }

    public void Post(string title, string body, string category = "News")
    {
        Inbox.Insert(0, new InboxMessage { Id = _nextMessageId++, Date = Today, Title = title, Body = body, Category = category });
        if (Inbox.Count > 400) Inbox.RemoveRange(400, Inbox.Count - 400);
    }

    // ------------------------------------------------------------------------------------------
    /// <summary>Advance one day. Returns the games played today.</summary>
    public List<GameResult> AdvanceDay()
    {
        var live = BeginDay(liveHumanGame: false);
        return EndDay(live.played);
    }

    /// <summary>True when the human team plays today and the game could be followed live.</summary>
    public bool HumanPlaysToday =>
        (Phase == SessionPhase.RegularSeason || (Phase == SessionPhase.Preseason && Today >= OpeningNight)) && GamesOn(Today).Any(g => g.Home == HumanTeam || g.Away == HumanTeam)
        || (Phase == SessionPhase.Postseason && Postseason != null && !Postseason.Complete && Postseason.Active.Any(se => se.Higher == HumanTeam || se.Lower == HumanTeam));

    /// <summary>
    /// Start today with the human game held back as a <see cref="GameSimulator.LiveGame"/>. Everyone else's games are played.
    /// Call <see cref="FinishLiveDay"/> once the live game is complete. Returns a null live game when the human team is idle.
    /// </summary>
    public (GameSimulator.LiveGame? live, List<GameResult> played) BeginLiveDay() => BeginDay(liveHumanGame: true);

    private Series? _liveSeries;
    private ScheduledGame? _liveScheduled;

    private (GameSimulator.LiveGame? live, List<GameResult> played) BeginDay(bool liveHumanGame)
    {
        var played = new List<GameResult>();
        GameSimulator.LiveGame? live = null;
        _liveSeries = null; _liveScheduled = null;
        if (Phase == SessionPhase.Offseason) return (null, played);
        if (Phase == SessionPhase.Preseason && Today >= OpeningNight) Phase = SessionPhase.RegularSeason;

        if (Phase == SessionPhase.RegularSeason)
        {
            foreach (var g in GamesOn(Today))
            {
                bool human = g.Home == HumanTeam || g.Away == HumanTeam;
                if (human && liveHumanGame)
                {
                    _liveScheduled = g;
                    live = _sim.StartLive(g.Home, g.Away, League.Rules, _rng.NextULong(), Today);
                    continue;
                }
                var r = _sim.Simulate(g.Home, g.Away, League.Rules, _rng.NextULong(), keepLog: human, date: Today);
                RecordRegularSeasonGame(g, r);
                played.Add(r);
            }
        }
        else if (Phase == SessionPhase.Postseason && Postseason != null)
        {
            var mine = liveHumanGame ? Postseason.Active.FirstOrDefault(se => se.Higher == HumanTeam || se.Lower == HumanTeam) : null;
            _stageBefore = Postseason.Stage;
            played = Postseason.PlayDayExcept(_sim, _rng, Today, mine != null ? HumanTeam : null);
            foreach (var r in played) if (r.Home.Team == HumanTeam || r.Away.Team == HumanTeam) ReportGame(r, HumanSeries);
            if (mine != null)
            {
                _liveSeries = mine;
                var (home, away) = Postseason.NextMatchup(mine);
                live = _sim.StartLive(home, away, League.Rules, _rng.NextULong(), Today);
            }
        }
        return (live, played);
    }

    private string _stageBefore = "";

    /// <summary>Record the finished live game and close the day.</summary>
    public List<GameResult> FinishLiveDay(GameSimulator.LiveGame? live, List<GameResult> played)
    {
        if (live != null)
        {
            if (!live.Complete) live.Finish();
            if (_liveScheduled != null) RecordRegularSeasonGame(_liveScheduled, live.Result);
            else if (_liveSeries != null)
            {
                Postseason!.RecordGame(_liveSeries, live.Result);
                ReportGame(live.Result, _liveSeries);
            }
            played.Add(live.Result);
        }
        if (Phase == SessionPhase.Postseason && Postseason != null) Postseason.AfterDay();
        return EndDay(played);
    }

    private void RecordRegularSeasonGame(ScheduledGame g, GameResult r)
    {
        Results[g.Index] = r;
        Standings.Record(r);
        foreach (var box in new[] { r.Home, r.Away })
        foreach (var l in box.Lines)
        {
            if (l.Seconds <= 0) continue;
            if (!Stats.TryGetValue(l.Player.Id, out var s))
                Stats[l.Player.Id] = s = new PlayerSeasonStats { PlayerId = l.Player.Id, TeamId = box.Team.Id, Season = League.Season };
            s.Add(l);
        }
        ReportGame(r);
    }

    /// <summary>Play the world's games up to and including <paramref name="date"/> and post its headlines.</summary>
    public void PlayWorld(DateOnly date)
    {
        World.PlayThrough(date, _sim);
        foreach (var (title, body) in World.News) Post(title, body, "World");
        World.News.Clear();
    }

    /// <summary>After loading a save made before the world existed: play the world's season up to yesterday.</summary>
    internal void CatchUpWorld()
    {
        if (_worldGenerated) PlayWorld(Today.AddDays(-1));
    }

    private List<GameResult> EndDay(List<GameResult> played)
    {
        PlayWorld(Today);
        if (Phase == SessionPhase.RegularSeason)
        {
            if (Today == TradeDeadline.AddDays(-10) || Today == new DateOnly(League.Season, 12, 15))
                AiTrades();

            if (Today == TradeDeadline)
                Post("Trade deadline has passed", "No more trades until the season ends.", "League");

            if (Today == AllStarDate && Midseason == null)
            {
                Midseason = new AwardsEngine(League, Standings, Stats, _rng).AllStar(League.Season, _sim, Today);
                var all = League.AllActivePlayers.ToDictionary(p => p.Id);
                string Names(IEnumerable<int> ids) => string.Join(", ", ids.Select(id => all.TryGetValue(id, out var p) ? $"{p.Name} ({League.TeamById(p.TeamId!.Value).Abbreviation})" : "?"));
                var ours = Midseason.AllStarsEast.Concat(Midseason.AllStarsWest).Where(id => all.TryGetValue(id, out var p) && p.TeamId == HumanTeam.Id).Select(id => all[id].Name).ToList();
                Post("All-Star Game", $"{Midseason.AllStarGame}. Game MVP: {(Midseason.AllStarMvpId is { } m && all.TryGetValue(m, out var mp) ? mp.Name : "-")}."
                    + (ours.Count > 0 ? $"\n\nYour All-Stars: {string.Join(", ", ours)}." : "\n\nNo one from your team was selected.")
                    + $"\n\nEast: {Names(Midseason.AllStarsEast)}\n\nWest: {Names(Midseason.AllStarsWest)}", "League");
            }

            if (Schedule.All(g => Results.ContainsKey(g.Index)))
            {
                Phase = SessionPhase.Postseason;
                Postseason = new Postseason(League, Standings);
                var rec = Standings[HumanTeam];
                int seed = Standings.Conference(HumanTeam.Conference).FindIndex(r => r.Team == HumanTeam) + 1;
                Post("Regular season complete",
                    $"You finished {rec.Wins}-{rec.Losses}, {Ordinal(seed)} in the {HumanTeam.Conference}. " +
                    (seed <= 6 ? "You are in the playoffs." : seed <= 10 ? "You are in the play-in." : "Your season is over; the lottery awaits."), "League");
            }
        }
        else if (Phase == SessionPhase.Postseason && Postseason != null)
        {
            if (Postseason.Stage != _stageBefore && !Postseason.Complete)
                Post($"{Postseason.Stage} begins", string.Join("\n", Postseason.Active.Select(s => $"{s.Higher.FullName} vs {s.Lower.FullName}")), "League");
            if (Postseason.Complete)
            {
                Post($"{Postseason.Champion!.FullName} are champions",
                    $"{Postseason.Champion.FullName} defeated {Postseason.RunnerUp!.FullName} in the Finals." +
                    (Postseason.Champion == HumanTeam ? " Congratulations: a title for your franchise." : ""), "League");
                Today = Today.AddDays(1);
                BeginOffseason();
                SaveNow();
                return played;
            }
        }

        Today = Today.AddDays(1);
        SaveNow();
        return played;
    }

    /// <summary>Advance until the human team's next game has been played, or the phase changes.</summary>
    public List<GameResult> AdvanceToNextGame()
    {
        var all = new List<GameResult>();
        var start = Phase;
        for (int i = 0; i < 30 && Phase == start; i++)
        {
            var day = AdvanceDay();
            all.AddRange(day);
            if (day.Any(r => r.Home.Team == HumanTeam || r.Away.Team == HumanTeam)) break;
        }
        return all;
    }

    private void ReportGame(GameResult r, Series? series = null)
    {
        if (r.Home.Team != HumanTeam && r.Away.Team != HumanTeam)
        {
            foreach (var (p, inj) in r.Injuries) { /* league-wide injuries are not inbox-worthy */ }
            return;
        }
        var us = r.Home.Team == HumanTeam ? r.Home : r.Away;
        var them = us == r.Home ? r.Away : r.Home;
        bool won = us.Points > them.Points;
        var top = us.Lines.OrderByDescending(l => l.Points).First();
        string seriesNote = series != null ? $" Series: {series.Score}." : "";
        Post($"{(won ? "W" : "L")} {us.Points}-{them.Points} {(us == r.Home ? "vs" : "at")} {them.Team.Abbreviation}",
            $"{top.Player.Name} led with {top.Points} points, {top.Rebounds} rebounds and {top.Assists} assists.{seriesNote}", "Game");
        foreach (var (p, inj) in r.Injuries.Where(x => x.player.TeamId == HumanTeam.Id))
            Post($"{p.Name} injured: {inj.Name}", $"Out approximately {inj.DaysOut(Today)} days (expected return {inj.ReturnDate:MMM d}).{(inj.AttributeLoss > 0 ? " The medical staff expect some lasting loss of athleticism." : "")}", "Medical");
    }

    private void AiTrades()
    {
        var engine = new TradeEngine(League, Today, Standings);
        var done = engine.AiTradeRound(_rng, HumanTeam.Id);
        TradesThisSeason.AddRange(done);
        foreach (var t in done)
            Post($"Trade: {t.From.Abbreviation} and {t.To.Abbreviation}",
                $"{t.From.FullName} send {DescribeSide(t.FromPlayers, t.FromPicks)} to {t.To.FullName} for {DescribeSide(t.ToPlayers, t.ToPicks)}.", "Transactions");
    }

    // ------------------------------------------------------------------------------------------
    // Human trades
    // ------------------------------------------------------------------------------------------
    public TradeEngine Trades => new(League, Today, Phase is SessionPhase.RegularSeason or SessionPhase.Postseason ? Standings : null);

    public TradeEvaluation ProposeTrade(TradeProposal proposal)
    {
        if (TradeDeadlinePassed && Phase != SessionPhase.Offseason && Phase != SessionPhase.Preseason)
            return new TradeEvaluation(false, "The trade deadline has passed.", 0, 0, 0, false, "The trade deadline has passed.");
        var eval = Trades.Evaluate(proposal);
        if (eval.Accepted)
        {
            Trades.Execute(proposal);
            TradesThisSeason.Add(proposal);
            Post($"Trade completed with {proposal.To.Abbreviation}",
                $"You send {DescribeSide(proposal.FromPlayers, proposal.FromPicks)} for {DescribeSide(proposal.ToPlayers, proposal.ToPicks)}.", "Transactions");
            SaveNow();
        }
        return eval;
    }

    // ------------------------------------------------------------------------------------------
    // Offseason
    // ------------------------------------------------------------------------------------------
    private void BeginOffseason()
    {
        var outcome = new SeasonOutcome { Standings = Standings, Midseason = Midseason };
        outcome.RegularSeason.AddRange(Results.Values);
        Postseason!.FillOutcome(outcome);

        Phase = SessionPhase.Offseason;
        Offseason = new Offseason(League, outcome, Stats, _rng);
        Offseason.Begin();
        Offseason.AiReSignAll(exceptTeamId: HumanTeam.Id);

        AnnounceAwards(Offseason.Summary!);

        var retired = Offseason.Report.Retired.Where(p => p.InjuryHistory != null).ToList();
        var ours = Offseason.Report.Retired.Where(p => HumanTeamHad(p)).ToList();
        if (ours.Count > 0) Post("Retirements", string.Join(", ", ours.Select(p => p.Name)) + (ours.Count == 1 ? " has" : " have") + " retired.", "Roster");
        var lottery = Offseason.Report.Lottery!;
        int ourPick = lottery.Order.IndexOf(HumanTeam) + 1;
        Post("Draft lottery results",
            $"You pick {Ordinal(ourPick)} in the first round." + (lottery.Jumps.Count > 0 ? "\n" + string.Join("\n", lottery.Jumps.Select(j => $"{j.team.Abbreviation} moved from {Ordinal(j.preLotterySlot)} to {Ordinal(j.pick)}")) : ""), "League");
        var staffGone = Offseason.Report.StaffReleased.Where(x => x.team == HumanTeam).Select(x => $"{x.staff.Name} ({Staff.RoleName(x.staff.Role).ToLower()})").ToList();
        if (staffGone.Count > 0) Post("Staff contracts expired", $"{string.Join(", ", staffGone)} left at the end of their deals. Hire replacements from the staff market.", "Staff");
        var expiring = Offseason.Expiring[HumanTeam.Id];
        Post("Contract decisions",
            expiring.Count == 0 ? "No contracts expire this summer." : $"{expiring.Count} contracts have expired: {string.Join(", ", expiring.Select(p => $"{p.Name} (asks ${Offseason.Ask(p) / 1e6:F1}M)"))}. Re-sign or release each one, then continue to the draft.", "Roster");
        var dev = Offseason.Report.Development.Where(d => d.Player.TeamId == HumanTeam.Id && Math.Abs(d.NewCa - d.OldCa) >= 6).OrderByDescending(d => d.NewCa - d.OldCa).ToList();
        if (dev.Count > 0) Post("Player development", string.Join("\n", dev.Select(d => $"{d.Player.Name}: {(d.NewCa > d.OldCa ? "+" : "")}{d.NewCa - d.OldCa} ability")), "Roster");
    }

    private void AnnounceAwards(SeasonSummary summary)
    {
        var a = summary.Awards;
        if (a == null) return;
        var all = League.AllActivePlayers.Concat(League.FreeAgents).Concat(League.Retired).ToDictionary(p => p.Id);
        string Name(int? id) => id is { } i && all.TryGetValue(i, out var p) ? p.Name : "-";
        string Voting(List<VoteLine> v) => string.Join("\n", v.Take(5).Select((l, i) => $"{i + 1}. {l.Name} ({l.Team}) {l.Share * 100:F0}% — {l.StatLine}"));
        Post($"MVP: {Name(a.Mvp)}", Voting(a.MvpVoting), "Awards");
        if (a.RoyVoting.Count > 0) Post($"Rookie of the Year: {Name(a.Roy)}", Voting(a.RoyVoting), "Awards");
        Post($"Defensive Player of the Year: {Name(a.Dpoy)}", Voting(a.DpoyVoting), "Awards");
        if (a.SixthManVoting.Count > 0) Post($"Sixth Man of the Year: {Name(a.SixthMan)}", Voting(a.SixthManVoting), "Awards");
        if (a.CoachOfYearTeamId is { } coy)
            Post($"Coach of the Year: {League.TeamById(coy).FullName}", a.CoachOfYearNote + (coy == HumanTeam.Id ? "\n\nThat's you." : ""), "Awards");
        Post("All-League teams", string.Join("\n\n", a.AllLeague.Select((t, i) => $"{(i == 0 ? "First" : i == 1 ? "Second" : "Third")} team: {string.Join(", ", t.Select(id => Name(id)))}")), "Awards");
        if (summary.FinalsMvpPlayerId is { } fm) Post($"Finals MVP: {Name(fm)}", $"{League.TeamById(summary.ChampionTeamId).FullName} win the title.", "Awards");
        var ours = a.AllLeague.SelectMany(t => t).Where(id => all.TryGetValue(id, out var p) && p.TeamId == HumanTeam.Id).Select(id => Name(id)).ToList();
        if (ours.Count > 0) Post("Your All-League honourees", string.Join(", ", ours), "Awards");
        foreach (var h in Offseason!.Report.HallOfFame)
            Post($"Hall of Fame: {h.Name}", $"Inducted after a {League.Careers[h.Id].Seasons}-season career: {League.Careers[h.Id].Points:N0} points, {League.Careers[h.Id].Mvps} MVP{(League.Careers[h.Id].Mvps == 1 ? "" : "s")}, {League.Careers[h.Id].Titles} title{(League.Careers[h.Id].Titles == 1 ? "" : "s")}, {League.Careers[h.Id].AllStars} All-Star selections.", "Awards");
        foreach (var (p, t) in Offseason.Report.RetiredNumbers)
            Post($"{t.FullName} retire {p.LastName}'s number", $"{p.Name}'s jersey goes to the rafters.", "Awards");
    }

    private bool HumanTeamHad(Player p) => Stats.TryGetValue(p.Id, out var s) && s.TeamId == HumanTeam.Id;

    /// <summary>Move the offseason forward one phase: re-signing → draft (AI picks until you're on the clock) → free agency → new season.</summary>
    public void AdvanceOffseason()
    {
        if (Offseason == null) return;
        switch (Offseason.Phase)
        {
            case OffseasonPhase.ReSigning:
                Offseason.AdvanceToDraft();
                Offseason.AutoDraftUntil(HumanTeam.Id);
                if (Offseason.DraftComplete) AdvanceOffseason();
                else Post("You're on the clock", $"Pick {Offseason.NextPickIndex + 1} (round {Offseason.CurrentRound}). {Offseason.Prospects.Count} prospects remain.", "Draft");
                break;
            case OffseasonPhase.Draft:
                Offseason.AdvanceToFreeAgency();
                Offseason.AiFreeAgency(HumanTeam.Id);
                var ours = Offseason.Report.Draft.Where(d => d.Team == HumanTeam).ToList();
                Post("Draft complete", string.Join("\n", ours.Select(d => $"Round {d.Round}, pick {d.Pick}: {d.Player.Name} ({d.Player.PrimaryPosition}, age {d.Player.Age(Offseason.Date)}, {d.Player.CurrentAbility}/{d.Player.PotentialAbility})")) + $"\n\nFree agency is open. You have {HumanTeam.Roster.Count} players; {League.Finance.Describe(ContractMarket.Payroll(HumanTeam))}. Rosters need {League.Finance.RosterMin} to {League.Finance.RosterMax}.", "Draft");
                break;
            case OffseasonPhase.FreeAgency:
                Offseason.Complete();
                StartNewSeason();
                break;
        }
        SaveNow();
    }

    /// <summary>Human draft pick; then AI picks run until the human is on the clock again or the draft ends.</summary>
    public DraftPick MakePick(Player prospect)
    {
        var pick = Offseason!.MakePick(prospect);
        Offseason.AutoDraftUntil(HumanTeam.Id);
        if (Offseason.DraftComplete) AdvanceOffseason();
        SaveNow();
        return pick;
    }

    private void StartNewSeason()
    {
        Phase = SessionPhase.Preseason;
        World.Rollover(League.Season, _sim);
        foreach (var (title, body) in World.News) Post(title, body, "World");
        World.News.Clear();
        Today = OpeningNight.AddDays(-7);
        PlayWorld(Today.AddDays(-1));
        HumanTeam.DepthChart = null;
        Midseason = null;
        BuildSeason();
        var last = League.History.Last();
        Post($"Welcome to {League.Season}-{(League.Season + 1) % 100:00}",
            $"Last season's champions: {League.TeamById(last.ChampionTeamId).FullName}. Opening night is {OpeningNight:MMM d}. Review your rotation before the first game.", "Board");
    }

    // ------------------------------------------------------------------------------------------
    /// <summary>Saves after every day and after every offseason decision.</summary>
    public void SaveNow()
    {
        if (Save == null) return;
        Save.Save(League, Stats.Values, Phase == SessionPhase.Offseason ? Offseason?.Prospects : null, World.Players);
        Save.SaveSession(this);
        Save.SaveWorld(World);
    }

    public static GameSession Load(string path)
    {
        var save = new SaveFile(path);
        var league = save.Load();
        var world = save.LoadWorld(league, save.LoadedOverseas);
        var session = save.LoadSession(league, world);
        session.CatchUpWorld();
        if (save.UpgradedFrom is { } old)
        {
            session.Post("Save upgraded", $"This career was saved by an older version of the game (save format {old}) and has been upgraded to format {SaveFile.SchemaVersion}. "
                + $"An untouched copy of the old save was kept at:\n{save.BackupPath}", "Board");
            session.SaveNow();
        }
        return session;
    }

    /// <summary>Used by SaveFile to restore state; not for gameplay.</summary>
    internal void Restore(DateOnly today, SessionPhase phase, List<ScheduledGame> schedule, Dictionary<int, GameResult> results,
        Dictionary<int, PlayerSeasonStats> stats, List<InboxMessage> inbox, int nextMessageId)
    {
        Today = today; Phase = phase;
        Schedule = schedule;
        Results.Clear(); foreach (var kv in results) Results[kv.Key] = kv.Value;
        Standings = new Standings(League);
        foreach (var r in results.OrderBy(kv => kv.Key).Select(kv => kv.Value)) Standings.Record(r);
        Stats = stats;
        Inbox.Clear(); Inbox.AddRange(inbox);
        _nextMessageId = nextMessageId;
    }

    internal void RestorePostseason(string stage, IEnumerable<(string, int, int, int, int, int)> rows)
    {
        Postseason = new Postseason(League, Standings);
        Postseason.Restore(stage, rows);
    }

    internal void RestoreOffseason(OffseasonPhase phase, List<int> draftOrder, List<int> pickOwners, List<Player> prospects, int nextPick,
        Dictionary<int, List<Player>> expiring, List<DraftPick> picksMade)
    {
        Phase = SessionPhase.Offseason;
        Offseason = Offseason.Restore(League, _rng, phase, draftOrder, pickOwners, prospects, nextPick, expiring, picksMade);
    }

    public string DescribeSide(IEnumerable<Player> players, IEnumerable<DraftPickAsset> picks)
    {
        var parts = players.Select(p => p.Name).Concat(picks.Select(p => p.Describe(id => League.TeamById(id).Abbreviation))).ToList();
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    private static string Ordinal(int n) => n % 100 is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
}
