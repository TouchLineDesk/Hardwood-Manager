using Hardwood.Core.Engine;
using Hardwood.Core.Generation;
using Hardwood.Core.Model;
using Hardwood.Core.Persistence;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

public enum SessionPhase { Preseason, RegularSeason, Postseason, Offseason }

/// <summary>One world headline waiting for the weekly digest.</summary>
public sealed record DigestItem(DateOnly Date, string Title, string Line);

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
public sealed partial class GameSession
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
    /// <summary>You (decision #70).</summary>
    public Jobs.Coach Coach { get; set; } = new();
    /// <summary>The name pack this career's clubs wear ("Real world 2025-26 (applied Nov 11, 2026)"), or null for the fictional names.</summary>
    public string? NamePackApplied { get; set; }
    /// <summary>Your job: employed, board confidence, expectations, open jobs (decisions #71-74).</summary>
    public Jobs.JobState Job { get; set; } = new();
    public bool Employed => Job.Employed;
    /// <summary>The team you control right now, or null while you're out of work (computer runs every team).</summary>
    public int? ControlledTeamId => Job.Employed ? HumanTeam.Id : null;
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
        // The human team is run by hand. A new career (or a save from before tactics were saved) starts from the
        // assistant's setup: the system and scheme that suit the roster, everyone in his best role.
        if (HumanTeam.Tactic.Auto) CoachAi.AssistantSetup(HumanTeam);
        Jobs.Board.EnsureOwnersAndCoaches(League, seed);
        Jobs.Board.EnsureOwnersAndCoaches(World.Clubs, seed);
        // Club money (decision #88): new careers, and saves from before the money model, get budgets now.
        var moneyRng = new Rng(seed ^ 0x4D4F4E4559UL);
        Budgets.Initialize(League.Teams, 1, moneyRng);
        foreach (var c in World.Competitions.Where(c => c.Clubs.Count > 0)) Budgets.Initialize(c.Clubs, c.Profile.SalaryScale, moneyRng);
        HumanTeam.Coach = Coach;
        // College programs get athletic directors (the board's role) and coaches' names (C2, #168).
        if (World.College != null) Jobs.Board.EnsureOwnersAndCoaches(World.College.Programs, seed);
        if (Abroad) EnsureStaff(HumanTeam);
        Job.HiredOn = Today; Job.HiredSeason = league.Season;
        Job.Current ??= Jobs.Board.NextExpectations(ContextOf(HumanTeam), HumanTeam, league.Season, null, MyLeagueKey, MyLevel, 0);
        BuildSeason();
    }

    /// <summary>Start a new career from a generated world.</summary>
    public static GameSession NewGame(ulong seed, int humanTeamId, string? savePath, int season = 2026, IEnumerable<string>? fullSim = null, Jobs.Coach? coach = null,
        Names.NamePack? names = null)
    {
        var league = new WorldGenerator(new Rng(seed)).Generate("North American Basketball League", season, new DateOnly(season, 10, 1));
        var world = Hardwood.Core.World.GameWorld.Generate(league, seed, fullSim ?? DefaultFullSim, new DateOnly(season, 10, 1));
        // A name pack renames the clubs before anything is written about them (the welcome, the first news).
        names?.ApplyToClubs(league, world);
        var team = league.Teams.FirstOrDefault(t => t.Id == humanTeamId) ?? world.TeamById(humanTeamId)
                   ?? throw new ArgumentException($"No club with id {humanTeamId}.", nameof(humanTeamId));
        var session = new GameSession(league, team, seed, savePath != null ? new SaveFile(savePath) : null, world: world);
        if (coach != null) { session.Coach = coach; session.HumanTeam.Coach = coach; }
        session.HumanTeam.CoachName = session.Coach.Name;
        session.Today = session.CareerYearStart();
        if (names != null) session.NamePackApplied = $"{names.Name} (applied {session.Today:MMM d, yyyy})";
        session.Job.HiredOn = session.Today;
        // A college coach starts with his assistant using any recruiting hours he leaves unspent (C3, #176).
        if (session.InCollege) session.Delegated.Recruiting = true;
        session.PlayWorld(session.Today.AddDays(-1));
        session.PostExpectations();
        var first = session.NextMyGame;
        session.Post("Welcome to the job", $"You are the new head coach and general manager of the {session.HumanTeam.FullName} ({session.HumanLeagueName}). "
            + (first != null ? $"Your first game is on {first.Date:MMM d}: set your rotation and tactics, then press Continue." : "Set your rotation and tactics, then press Continue.")
            + (session.TrailOpen ? $"\n\nRecruiting: you have {College.CollegeWorld.WeeklyHours} coach hours a week for high-school prospects (College page, Recruiting). Scout, call, visit and offer; a prospect can only commit to you once you've offered. Your assistant spends any hours you leave unused (Staff page, delegation)." : ""), "Board");
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
    /// <summary>What a staff member would be paid at your club: NABL wages at home, your league's scale abroad.</summary>
    public long StaffPayFor(Staff st) => !Abroad ? st.Salary
        : Math.Max(1_000, (long)Math.Round(Generation.StaffGenerator.SalaryFor(st) * Budgets.StaffScale(MySalaryScale) / 1000) * 1000);

    public bool HireStaff(Staff st)
    {
        if (!League.FreeStaff.Remove(st)) return false;
        var existing = HumanTeam.Staff.FirstOrDefault(x => x.Role == st.Role);
        if (existing != null) { HumanTeam.Staff.Remove(existing); existing.TeamId = null; League.FreeStaff.Add(existing); }
        st.TeamId = HumanTeam.Id;
        st.ContractYears = Math.Max(1, st.ContractYears);
        st.Salary = StaffPayFor(st);
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

    /// <summary>World headlines waiting for the weekly digest (critic review, Nov 10: 150-190 world messages a season buried the board's warnings).</summary>
    public List<DigestItem> WorldDigest { get; } = new();
    public DateOnly LastDigest { get; set; }

    /// <summary>World news: anything about your club goes straight to the inbox; the rest waits for the weekly "Around the world".</summary>
    public void PostWorld(string title, string body)
    {
        if (Employed && (title.Contains(HumanTeam.FullName) || body.Contains(HumanTeam.FullName))) { Post(title, body, "World"); return; }
        WorldDigest.Add(new DigestItem(Today, title, FirstSentence(body)));
        if (WorldDigest.Count > 200) WorldDigest.RemoveRange(0, WorldDigest.Count - 200);
    }

    /// <summary>Once a week (or when it fills up), the world's headlines arrive as one message.</summary>
    private void MaybePostDigest(bool now = false)
    {
        if (WorldDigest.Count == 0) return;
        if (!now && Today.DayNumber - LastDigest.DayNumber < 14 && WorldDigest.Count < 40) return; // fortnightly (review #4)
        var lines = WorldDigest.Select(d => $"• {d.Title}" + (d.Line.Length > 0 ? $": {d.Line}" : ""));
        Post($"Around the world: {WorldDigest.Count} {(WorldDigest.Count == 1 ? "story" : "stories")}",
            string.Join("\n", lines) + "\n\nTables, results and leaders are on the Leagues page.", "World");
        WorldDigest.Clear();
        LastDigest = Today;
    }

    private static string FirstSentence(string body)
    {
        var line = body.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        int dot = line.IndexOf(". ", StringComparison.Ordinal);
        if (dot > 0) line = line[..(dot + 1)];
        return line.Length > 180 ? line[..177] + "..." : line;
    }

    public void MarkAllRead() { foreach (var m in Inbox) m.Read = true; }

    /// <summary>This week's regular-season results, posted together once a week.</summary>
    public List<string> WeekResults { get; } = new();

    private void MaybePostResults(bool now = false)
    {
        if (WeekResults.Count == 0) return;
        if (!now && Today.DayOfWeek != DayOfWeek.Monday) return;
        int w = WeekResults.Count(l => l.Contains("  W ")), l = WeekResults.Count - w;
        Post($"This week: {w}-{l} ({MyRecord.Wins}-{MyRecord.Losses} overall)", string.Join("\n", WeekResults) + "\n\nBox scores are on the Schedule & Results page.", "Game");
        WeekResults.Clear();
    }

    /// <summary>
    /// Openings while you're out of work, posted together once a week (critic review #3: 104 separate "Job opening"
    /// messages in one spell out of work). The vacancies themselves are on the Board page straight away.
    /// </summary>
    public List<string> JobOpenings { get; } = new();
    private DateOnly _lastJobsPost;

    private void MaybePostJobs()
    {
        if (Employed) { JobOpenings.Clear(); return; }
        if (JobOpenings.Count == 0 || (Today.DayNumber - _lastJobsPost.DayNumber < 7 && _lastJobsPost != default)) return;
        Post(JobOpenings.Count == 1 ? "A new job opening" : $"{JobOpenings.Count} new job openings",
            string.Join("\n", JobOpenings) + "\n\nApply on the Board page; smaller clubs answer first.", "Board");
        JobOpenings.Clear();
        _lastJobsPost = Today;
    }

    private static string Count(int n, string word) => $"{n} {word}{(n == 1 ? "" : "s")}";

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
        Abroad ? Employed && Phase != SessionPhase.Offseason && (InCollege ? World.College!.GameOn(HumanTeam.Id, Today) != null : World.GameOn(HumanTeam.Id, Today) != null) :
        Employed && (Phase == SessionPhase.RegularSeason || (Phase == SessionPhase.Preseason && Today >= OpeningNight)) && GamesOn(Today).Any(g => g.Home == HumanTeam || g.Away == HumanTeam)
        || (Employed && Phase == SessionPhase.Postseason && Postseason != null && !Postseason.Complete && Postseason.Active.Any(se => se.Higher == HumanTeam || se.Lower == HumanTeam));

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
        _liveSeries = null; _liveScheduled = null; _liveWorld = null;
        if (Phase == SessionPhase.Offseason) return (null, played);
        if (Phase == SessionPhase.Preseason && Today >= OpeningNight) Phase = SessionPhase.RegularSeason;
        // A club abroad plays its league and continental games from the world's fixtures.
        live = BeginAbroadGame(liveHumanGame, played);

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
            if (_liveCollege is { } cg) RecordCollege(cg, live.Result);
            else if (_liveWorld is { } w) RecordAbroad(w.comp, w.fixture, live.Result);
            else if (_liveScheduled != null) RecordRegularSeasonGame(_liveScheduled, live.Result);
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
            // Career milestones (roadmap 4): completed seasons plus this one, before and after this game.
            var career = League.Careers.GetValueOrDefault(l.Player.Id);
            int pts = (career?.Points ?? 0) + s.Points, reb = (career?.Rebounds ?? 0) + s.OReb + s.DReb, ast = (career?.Assists ?? 0) + s.Assists, gp = (career?.Games ?? 0) + s.Games;
            s.Add(l);
            var passed = League.Records.CheckMilestones(l.Player, box.Team, Today, League.Season, new Dictionary<string, (int, int)>
            {
                ["points"] = (pts, pts + l.Points), ["rebounds"] = (reb, reb + l.Rebounds), ["assists"] = (ast, ast + l.Assists), ["games"] = (gp, gp + 1),
            });
            // Yours arrive on their own; other clubs' big ones wait for the fortnightly digest (dial-in item 5: inbox volume).
            foreach (var m in passed)
                if (box.Team == HumanTeam)
                    Post($"Milestone: {m.PlayerName} passes {m.Threshold:N0} {m.Stat}", $"{m.Text} on {Today:MMM d}.", "League");
                else if (m.Threshold >= (m.Stat == "points" ? 20_000 : m.Stat == "games" ? 1_000 : 10_000))
                    PostWorld($"Milestone: {m.PlayerName} passes {m.Threshold:N0} {m.Stat}", $"{m.Text} on {Today:MMM d}.");
        }
        bool ours = r.Home.Team == HumanTeam || r.Away.Team == HumanTeam;
        foreach (var line in League.Records.Consider(r, Today, League.Season))
            if (ours) Post("New league record", line, "League"); else PostWorld("New league record", line);
        ReportGame(r);
    }

    /// <summary>Play the world's games up to and including <paramref name="date"/> and post its headlines.</summary>
    public void PlayWorld(DateOnly date)
    {
        if (World.College is { } cw) cw.ManagedProgramId = Employed && InCollege ? HumanTeam.Id : null;
        World.PlayThrough(date, _sim);
        PostCoachNews();
        // Day to day the digest keeps its fortnightly rhythm (review #4: posting whenever there was news made it near-daily).
        foreach (var (title, body) in World.News) PostWorld(title, body); MaybePostDigest(now: date == DateOnly.MaxValue);
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
        bool inSeason = Abroad ? Phase != SessionPhase.Offseason : Phase is SessionPhase.RegularSeason or SessionPhase.Postseason;
        if (inSeason && Today.DayOfWeek == DayOfWeek.Monday)
        {
            WeeklyPeople();
            bool regular = InCollege ? !World.College!.Season.Selected && MyRecord.Games > 0
                : Abroad ? HumanLeague?.Season.Stage == CompetitionSeason.RegularSeason : Phase == SessionPhase.RegularSeason;
            if (regular) WeeklyBoard();
        }
        if (!Employed) DailyJobMarket();
        if (Today.DayOfWeek == DayOfWeek.Monday) { DelegatedTraining(); DelegatedRecruiting(); WeeklyRecruiting(); WeeklyTrail(); }
        DailyTransfers();
        MaybePostDigest();
        MaybePostJobs();
        MaybePostResults(now: Phase is SessionPhase.Postseason or SessionPhase.Offseason);
        AttachPressToResults();
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
                if (InNabl)
                {
                    var rec = Standings[HumanTeam];
                    int seed = Standings.Conference(HumanTeam.Conference).FindIndex(r => r.Team == HumanTeam) + 1;
                    Post("Regular season complete",
                        $"You finished {rec.Wins}-{rec.Losses}, {Ordinal(seed)} in the {HumanTeam.Conference}. " +
                        (seed <= 6 ? "You are in the playoffs." : seed <= 10 ? "You are in the play-in." : "Your season is over; the lottery awaits."), "League");
                }
            }
        }
        else if (Phase == SessionPhase.Postseason && Postseason != null)
        {
            if (Postseason.Stage != _stageBefore && !Postseason.Complete && Postseason.Active.Any(x => x.Higher == HumanTeam || x.Lower == HumanTeam))
                Post($"{Postseason.Stage} begins", string.Join("\n", Postseason.Active.Select(s => $"{s.Higher.FullName} vs {s.Lower.FullName}")), "League");
            if (Postseason.Complete)
            {
                string title = $"{Postseason.Champion!.FullName} are champions";
                if (!Inbox.Any(m => m.Title == title && m.Date >= OpeningNight))
                    Post(title, $"{Postseason.Champion.FullName} defeated {Postseason.RunnerUp!.FullName} in the Finals." +
                        (Postseason.Champion == HumanTeam ? " Congratulations: a title for your franchise." : ""), "League");
                // A club abroad may still be playing (overseas playoffs run into June): the summer waits for it.
                if (!HumanSeasonOver && Today < new DateOnly(League.Season + 1, 7, 20))
                {
                    Today = Today.AddDays(1);
                    SaveNow();
                    return played;
                }
                Today = Today.AddDays(1);
                SeasonEndCoaching();
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
        string title = $"{(won ? "W" : "L")} {us.Points}-{them.Points} {(us == r.Home ? "vs" : "at")} {them.Team.Abbreviation}";
        string line = $"{top.Player.Name} led with {Count(top.Points, "point")}, {Count(top.Rebounds, "rebound")} and {Count(top.Assists, "assist")}.";
        // Regular-season results go into one weekly message (critic review #2: up to 216 result messages in three
        // seasons buried everything else); playoff games still arrive one by one.
        // Playoff games arrive as one message when the series ends (critic review #4: up to 48 playoff messages in three seasons).
        if (series == null) WeekResults.Add($"{r.Date ?? Today:ddd MMM d}  {title}  · {line}");
        else if (series.Complete)
        {
            bool weWon = series.Winner == HumanTeam;
            var opp = series.Higher == HumanTeam ? series.Lower : series.Higher;
            int ours = series.Higher == HumanTeam ? series.HigherWins : series.LowerWins, theirs = series.Higher == HumanTeam ? series.LowerWins : series.HigherWins;
            var lines = series.Games.Select((g, i) =>
            {
                var u = g.Home.Team == HumanTeam ? g.Home : g.Away; var t = u == g.Home ? g.Away : g.Home;
                var best = u.Lines.OrderByDescending(l => l.Points).First();
                return $"Game {i + 1}: {(u.Points > t.Points ? "W" : "L")} {u.Points}-{t.Points} {(u == g.Home ? "vs" : "at")} {t.Team.Abbreviation} · {best.Player.Name} {best.Points}";
            });
            Post($"{series.Round}: {(weWon ? "won" : "lost")} {ours}-{theirs} {(weWon ? "against" : "to")} {opp.Abbreviation}",
                string.Join("\n", lines) + "\n\nBox scores are on the Schedule & Results page.", "Game");
        }
        foreach (var (p, inj) in r.Injuries.Where(x => x.player.TeamId == HumanTeam.Id))
            Post($"{p.Name} injured: {inj.Name}", $"Out approximately {inj.DaysOut(Today)} days (expected return {inj.ReturnDate:MMM d}).{(inj.AttributeLoss > 0 ? " The medical staff expect some lasting loss of athleticism." : "")}", "Medical");
    }

    private void AiTrades()
    {
        if (League.Finance.OpenMarket) return; // no trades on the open market: the transfer market replaces them
        var engine = new TradeEngine(League, Today, Standings);
        var done = engine.AiTradeRound(_rng, ControlledTeamId ?? -1);
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
        if (Abroad || League.Finance.OpenMarket)
            return new TradeEvaluation(false, "There are no trades on the open market: players move by transfer.", 0, 0, 0, false, "There are no trades on the open market: players move by transfer.");
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
    // Your job (decisions #70-74)

    private void PostExpectations()
    {
        var e = Job.Current!;
        var o = HumanTeam.Owner;
        Post($"Expectations for {e.Season}-{(e.Season + 1) % 100:00}",
            $"{(InCollege ? "Athletic director " : "")}{o.Name} ({o.Style.ToLower()}) has set your goal: {(e.Survive ? "stay up: newly promoted, avoid the relegation places" : Jobs.Board.Describe(e.Goal, ContextOf(HumanTeam)).ToLower())} (around {e.TargetWins} wins)."
            + (e.Demands.Count > 0 ? "\n\nThe owner also asks you to:\n" + string.Join("\n", e.Demands.Select(d => "• " + Jobs.Board.Describe(d))) : ""), "Board");
    }

    /// <summary>A college program's boss is its athletic director (C2): the board's wording, translated.</summary>
    private string Boss(string text) => !InCollege ? text
        : text.Replace("The owner", "The athletic director").Replace("the owner", "the athletic director").Replace("Board confidence", "AD confidence");

    private void WeeklyBoard()
    {
        if (!Employed) return;
        var rec = MyRecord;
        var (title, body, sacked) = Jobs.Board.Weekly(Job, ContextOf(HumanTeam), HumanTeam, rec.Wins, rec.Losses, MyStats, Today, League.Season);
        if (title != null) Post(Boss(title), Boss(body!), "Board");
        if (sacked) Fire();
    }

    /// <summary>After the playoffs: the board reviews your season; computer-run clubs change coaches.</summary>
    private void SeasonEndCoaching()
    {
        if (Abroad) PlayWorld(DateOnly.MaxValue); // every overseas season ends with the NABL's
        if (Employed)
        {
            var rec = MyRecord;
            var achieved = InCollege ? Jobs.Board.Achieved(World.College!, HumanTeam.Id)
                : HumanLeague is { } c ? Jobs.Board.Achieved(HumanTeam.Id, c.Season, c.Profile) : Jobs.Board.Achieved(HumanTeam, Postseason);
            var (title, body, sacked) = Jobs.Board.SeasonReview(Job, Coach, ContextOf(HumanTeam), HumanTeam, achieved, rec.Wins, rec.Losses, MyStats, Today, League.Season);
            Post(Boss(title), Boss(body), "Board");
            if (sacked) Fire();
        }
        foreach (var t in Jobs.Board.AiSeasonChanges(League, Standings, _rng, ControlledTeamId)) CoachLeaves(t, "parted ways with");
        WorldCoachingChanges();
    }

    /// <summary>A computer-run club changes coach; while you're out of work the job is open to you.</summary>
    public void CoachLeaves(Team t, string how = "parted ways with")
    {
        string old = t.CoachName;
        if (!Employed && t != HumanTeam && !Job.Vacancies.Any(v => v.TeamId == t.Id && v.Status is "open" or "applied" or "offered"))
        {
            Job.Vacancies.Add(new Jobs.Vacancy { TeamId = t.Id, Opened = Today });
            t.CoachName = "Interim coach";
            JobOpenings.Add($"• {t.FullName} ({ContextOf(t).Name}) have {how} {old}.");
        }
        else
        {
            t.CoachName = Jobs.Board.NewCoachName(new Rng(_rng.NextULong()));
            if (League.Teams.Contains(t) || HumanLeague?.Clubs.Contains(t) == true)
                Post($"{t.Abbreviation} change coach", $"{t.FullName} have {how} {old} and hired {t.CoachName}.", "League");
        }
    }

    /// <summary>You're sacked: the club goes back to the computer and you look for work.</summary>
    public void Fire()
    {
        if (!Employed) return;
        var rec = MyRecord;
        Coach.History.Add(new Jobs.JobRecord(HumanTeam.Id, HumanTeam.FullName, Job.HiredSeason, League.Season, rec.Wins, rec.Losses, "Sacked"));
        Coach.Reputation = Math.Max(0, Coach.Reputation - 5);
        HumanTeam.Coach = null;
        HumanTeam.Tactic.Auto = true;
        HumanTeam.DepthChart = null;
        HumanTeam.CoachName = Jobs.Board.NewCoachName(new Rng(_rng.NextULong()));
        Job.Employed = false;
        Job.Current = null;
        Promises.Clear();
        Post("Out of work", $"You are no longer coach of the {HumanTeam.FullName}; {HumanTeam.CoachName} takes over. Clubs that change coach will post their openings on the Board page, where you can apply. Your reputation: {Coach.Reputation:0}/100.", "Board");
    }

    /// <summary>Apply for an open job. Returns null on success, else why not.</summary>
    public string? Apply(int teamId)
    {
        var v = Job.Vacancies.FirstOrDefault(x => x.TeamId == teamId && x.Status == "open");
        if (Employed) return "You already have a job.";
        if (v == null) return "That job isn't open.";
        v.Status = "applied";
        v.DecideOn = Today.AddDays(3 + _rng.Next(0, 5));
        Post($"Application sent: {FindTeam(teamId)!.FullName}", $"They'll decide by {v.DecideOn:MMM d}.", "Board");
        SaveNow();
        return null;
    }

    /// <summary>Accept a job offer and take over the club.</summary>
    public string? AcceptOffer(int teamId)
    {
        var v = Job.Vacancies.FirstOrDefault(x => x.TeamId == teamId && x.Status == "offered");
        if (v == null) return "There's no offer from that club.";
        var team = FindTeam(teamId)!;
        foreach (var other in Job.Vacancies.Where(x => x.Status is "open" or "applied" or "offered" && x != v)) other.Status = "filled";
        v.Status = "filled";
        TakeJob(team);
        SaveNow();
        return null;
    }

    public void DeclineOffer(int teamId)
    {
        var v = Job.Vacancies.FirstOrDefault(x => x.TeamId == teamId && x.Status == "offered");
        if (v != null && FindTeam(teamId) is { } t) { v.Status = "filled"; t.CoachName = Jobs.Board.NewCoachName(new Rng(_rng.NextULong())); }
        SaveNow();
    }

    private void TakeJob(Team team)
    {
        HumanTeam = team;
        team.Coach = Coach;
        team.CoachName = Coach.Name;
        Job.Employed = true;
        Job.Confidence = 70;
        Job.WarningLevel = 0;
        Job.HiredOn = Today;
        Job.HiredSeason = League.Season;
        Job.History.Clear();
        Job.Current = Jobs.Board.NextExpectations(ContextOf(team), team, League.Season, null, MyLeagueKey, 0, 0);
        if (Abroad) EnsureStaff(team);
        CoachAi.AssistantSetup(team);
        team.DepthChart = null;
        Promises.Clear();
        MoodHistory.Clear();
        Post($"Welcome to the {team.FullName}", $"{team.Owner.Name} has hired you as head coach ({HumanLeagueName}). Check your rotation and tactics before the next game.", "Board");
        PostExpectations();
    }

    /// <summary>While you're out of work: clubs answer applications, unanswered jobs get filled, and new jobs open now and then.</summary>
    private void DailyJobMarket()
    {
        foreach (var v in Job.Vacancies.ToList())
        {
            if (FindTeam(v.TeamId) is not { } t) { v.Status = "filled"; continue; }
            if (v.Status == "applied" && v.DecideOn <= Today)
            {
                if (_rng.Chance(Jobs.Board.HireChance(Coach, ContextOf(t), t)))
                {
                    v.Status = "offered"; v.DecideOn = Today.AddDays(7);
                    Post($"Job offer: {t.FullName}", $"{t.Owner.Name} wants you as head coach of the {t.FullName}. Accept or decline on the Board page within a week.", "Board");
                }
                else
                {
                    v.Status = "rejected";
                    t.CoachName = Jobs.Board.NewCoachName(new Rng(_rng.NextULong()));
                    Post($"{t.Abbreviation} went another way", $"{t.FullName} have hired {t.CoachName} instead.", "Board");
                }
            }
            else if (v.Status == "offered" && v.DecideOn <= Today) DeclineOffer(v.TeamId);
            else if (v.Status == "open" && Today.DayNumber - v.Opened.DayNumber >= 21)
            {
                v.Status = "filled";
                t.CoachName = Jobs.Board.NewCoachName(new Rng(_rng.NextULong()));
                // Openings you didn't go for are news, not mail (critic review: 146 of these in one spell out of work).
                PostWorld($"{t.Abbreviation} hire a coach", $"{t.FullName} have filled their opening with {t.CoachName}.");
            }
        }
        // Keep the market moving: struggling clubs sometimes change coach mid-season.
        if (Phase == SessionPhase.RegularSeason && Today.DayOfWeek == DayOfWeek.Monday && !Job.Vacancies.Any(v => v.Status is "open" or "applied" or "offered"))
        {
            var struggling = League.Teams.Where(t => Standings[t].Games >= 10 && Standings[t].WinPct < 0.42).OrderBy(t => Standings[t].WinPct).FirstOrDefault();
            if (struggling != null && _rng.Chance(0.4)) CoachLeaves(struggling, "fired");
            // Overseas clubs too: a struggling club abroad sometimes looks for a new coach.
            else if (_rng.Chance(0.3))
            {
                var comp = World.Competitions.Where(c => c.Profile.Kind == CompetitionKind.Domestic).ToList() is { Count: > 0 } doms ? doms[_rng.Next(doms.Count)] : null;
                if (comp != null)
                {
                    var table = TableOf(comp);
                    var weak = comp.Clubs.Where(t => table[t].Games >= 6 && table[t].WinPct < 0.35).OrderBy(t => table[t].WinPct).FirstOrDefault();
                    if (weak != null) CoachLeaves(weak, "fired");
                }
            }
        }
        if (Phase == SessionPhase.Preseason && Today.DayOfWeek == DayOfWeek.Monday && !Job.Vacancies.Any(v => v.Status is "open" or "applied" or "offered") && _rng.Chance(0.3))
            CoachLeaves(League.Teams[_rng.Next(League.Teams.Count)], "parted ways with");
    }

    // ------------------------------------------------------------------------------------------
    // Players as people (decision #64)

    /// <summary>Promises the coach has made his players (more minutes, or a trade).</summary>
    public List<People.Promise> Promises { get; } = new();
    /// <summary>Weekly snapshot of your team's chemistry and average morale (for the trend charts).</summary>
    public List<People.MoodSnapshot> MoodHistory { get; } = new();

    private void WeeklyPeople()
    {
        List<People.PeopleEvent> events;
        if (InNabl)
            events = People.PeopleEngine.Weekly(League, Today, t => Standings[t].Games > 0 ? Standings[t].WinPct : 0.5, Stats, ControlledTeamId, Promises);
        else
        {
            // The NABL lives on; your club abroad gets the same weekly update, with wages judged by its league's market.
            People.PeopleEngine.Weekly(League, Today, t => Standings[t].Games > 0 ? Standings[t].WinPct : 0.5, Stats, null);
            var rec = MyRecord;
            events = People.PeopleEngine.Weekly(new[] { HumanTeam }, League.Finance, HumanLeague?.Profile.SalaryScale ?? 1, FindTeam, Today,
                _ => rec.Games > 0 ? rec.WinPct : 0.5, MyStats, ControlledTeamId, Promises);
        }
        MoodHistory.Add(new People.MoodSnapshot(Today, Math.Round(HumanTeam.Chemistry, 1), Math.Round(HumanTeam.Roster.Select(p => p.Morale).DefaultIfEmpty(60).Average(), 1)));
        if (MoodHistory.Count > 120) MoodHistory.RemoveAt(0);
        foreach (var e in events)
        {
            string title = e.Kind switch
            {
                People.PeopleEventKind.TradeRequest => $"{e.Player.Name} requests a trade",
                People.PeopleEventKind.WantsTalk => $"{e.Player.Name} wants a word",
                People.PeopleEventKind.PromiseKept => $"Promise kept: {e.Player.Name}",
                People.PeopleEventKind.PromiseBroken => $"Promise broken: {e.Player.Name}",
                People.PeopleEventKind.Settled => $"{e.Player.Name} settles",
                _ => e.Player.Name,
            };
            Post(title, e.Text, "Squad");
        }
        WeeklyMedia();
    }

    /// <summary>Hold a one-on-one conversation with one of your players. Returns his reply (or why you can't talk now).</summary>
    public People.TalkResult Talk(Player p, People.TalkTopic topic)
    {
        if (!Employed || p.TeamId != HumanTeam.Id) return new("He isn't one of your players.", 0, false);
        if (People.Talks.Unavailable(p, Today) is { } why) return new(why, 0, false);
        MyStats.TryGetValue(p.Id, out var st);
        double wp = MyRecord.Games > 0 ? MyRecord.WinPct : 0.5;
        var r = People.Talks.Hold(p, HumanTeam, topic, Today, wp, st, League.Finance, Promises, _rng, Coach);
        Coach.SeasonTalks++;
        SaveNow();
        return r;
    }

    // ------------------------------------------------------------------------------------------
    // Offseason
    // ------------------------------------------------------------------------------------------
    private void BeginOffseason()
    {
        World.ManagedClubId = Employed && Abroad && !InCollege ? HumanTeam.Id : null;
        var collegeBefore = Employed && InCollege ? HumanTeam.Roster.ToList() : null;
        NewWindow();
        var outcome = new SeasonOutcome { Standings = Standings, Midseason = Midseason };
        outcome.RegularSeason.AddRange(Results.Values);
        Postseason!.FillOutcome(outcome);

        Phase = SessionPhase.Offseason;
        Offseason = new Offseason(League, outcome, Stats, _rng) { HumanTeamId = ControlledTeamId };
        Offseason.Begin();
        if (Employed && Offseason.Report.StaffLeft.Count > 0)
            Post($"{Count(Offseason.Report.StaffLeft.Count, "staff contract")} ran out",
                string.Join("\n", Offseason.Report.StaffLeft.Select(st => $"{st.Name} ({Staff.RoleName(st.Role).ToLower()}) has left: his contract ended without an extension."))
                + "\n\nHire replacements on the Staff & Training page; interview candidates first.", "Staff");
        CoachGrowth();
        // The world's summer starts too: overseas contracts end and some players enter the shared free-agent pool.
        var recruiting = World.College?.Season.Recruiting;
        World.BeginSummer(Offseason.Season + 1, _sim, League.FreeAgents);
        Hardwood.Core.World.WorldMarket.Attach(World, Offseason, _sim);
        var abroad = World.LeftClub.Where(x => x.player.CurrentAbility >= Hardwood.Core.World.GameWorld.NablCalibre).OrderByDescending(x => x.player.CurrentAbility).Take(8).ToList();
        if (abroad.Count > 0)
            Post("Overseas free agents", "These players' overseas contracts have ended and they're free to sign anywhere:\n"
                + string.Join("\n", abroad.Select(x => $"{x.player.Name} ({x.player.PrimaryPosition}, {x.player.Age(Offseason.Date)}, {x.player.CurrentAbility} CA), last with {x.from.FullName}")), "World");
        Offseason.AiReSignAll(exceptTeamId: ControlledTeamId);
        // Seniors who committed during the season sign before free agency opens (decision #157).
        SignCommittedGraduates(recruiting);

        AnnounceAwards(Offseason.Summary!);
        if (!Employed) return; // out of work: the rest of these messages are about your team
        if (collegeBefore != null)
        {
            // College (C2): no contracts. Seniors graduate into the pros and the signed class enrolls (#162).
            var now = HumanTeam.Roster.ToHashSet();
            var left = collegeBefore.Where(p => !now.Contains(p)).ToList();
            var joined = HumanTeam.Roster.Where(p => !collegeBefore.Contains(p)).OrderByDescending(p => p.CurrentAbility).ToList();
            Post("Graduation and enrollment",
                (left.Count > 0 ? $"Graduated into the pros: {string.Join(", ", left.Select(p => $"{p.Name} ({p.PrimaryPosition}, {p.CurrentAbility})"))}." : "Nobody graduated this year.")
                + "\n\n" + (joined.Count > 0 ? $"Your new class: {string.Join(", ", joined.Select(p => $"{p.Name} ({p.PrimaryPosition}, {p.CurrentAbility}/{p.PotentialAbility})"))}." : "No new players enrolled.")
                + $"\n\nRoster: {HumanTeam.Roster.Count} players. The pros' summer runs until the new season; your program has nothing to sign, so press Continue to move it along.", "Roster");
            return;
        }
        if (Abroad)
        {
            _summerRoster = HumanTeam.Roster.Select(p => p.Id).ToList();
            var waiting = MyExpiring;
            Post("Summer: contract decisions", (waiting.Count == 0 ? "No contracts expire this summer." :
                    $"{waiting.Count} contract{(waiting.Count == 1 ? " is" : "s are")} up: {string.Join(", ", waiting.Select(p => $"{p.Name} (asks ${DemandOf(p).Wage / 1e6:F2}M)"))}. Talk to them on the Offseason page, or let them go.")
                + $"\n\nFree agents can be signed all summer within your budget ({Budgets.Describe(HumanTeam)}). Anyone you haven't renewed leaves when the summer ends, and if the squad is short of {Hardwood.Core.World.GameWorld.ManagedMinimum}, your sporting director makes up the numbers.", "Roster");
            return;
        }

        var retired = Offseason.Report.Retired.Where(p => p.InjuryHistory != null).ToList();
        var ours = Offseason.Report.Retired.Where(p => HumanTeamHad(p)).ToList();
        if (ours.Count > 0) Post("Retirements", string.Join(", ", ours.Select(p => p.Name)) + (ours.Count == 1 ? " has" : " have") + " retired.", "Roster");
        if (Offseason.Report.Lottery is { } lottery)
        {
            int ourPick = lottery.Order.IndexOf(HumanTeam) + 1;
            Post("Draft lottery results",
                $"You pick {Ordinal(ourPick)} in the first round." + (lottery.Jumps.Count > 0 ? "\n" + string.Join("\n", lottery.Jumps.Select(j => $"{j.team.Abbreviation} moved from {Ordinal(j.preLotterySlot)} to {Ordinal(j.pick)}")) : ""), "League");
        }
        var staffGone = Offseason.Report.StaffReleased.Where(x => x.team == HumanTeam).Select(x => $"{x.staff.Name} ({Staff.RoleName(x.staff.Role).ToLower()})").ToList();
        if (staffGone.Count > 0) Post("Staff contracts expired", $"{string.Join(", ", staffGone)} left at the end of their deals. Hire replacements from the staff market.", "Staff");
        var expiring = Offseason.Expiring[HumanTeam.Id];
        Post("Contract decisions",
            (expiring.Count == 0 ? "No contracts expire this summer." : $"{expiring.Count} contracts have expired: {string.Join(", ", expiring.Select(p => $"{p.Name} (asks ${DemandOf(p).Wage / 1e6:F1}M)"))}. Talk to each one or release him, then continue to free agency.")
            + $"\n\nThe owner has set next season's budget: {Budgets.Describe(HumanTeam)}.", "Roster");
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
        // One message for the season's awards (critic review #4: seven separate award messages a summer).
        var parts = new List<string> { $"MVP: {Name(a.Mvp)}\n{Voting(a.MvpVoting)}" };
        if (a.RoyVoting.Count > 0) parts.Add($"Rookie of the Year: {Name(a.Roy)}\n{Voting(a.RoyVoting)}");
        parts.Add($"Defensive Player of the Year: {Name(a.Dpoy)}\n{Voting(a.DpoyVoting)}");
        if (a.SixthManVoting.Count > 0) parts.Add($"Sixth Man of the Year: {Name(a.SixthMan)}\n{Voting(a.SixthManVoting)}");
        if (a.CoachOfYearTeamId is { } coy) parts.Add($"Coach of the Year: {League.TeamById(coy).FullName}. {a.CoachOfYearNote}" + (coy == HumanTeam.Id ? " That's you." : ""));
        parts.Add(string.Join("\n", a.AllLeague.Select((t, i) => $"All-League {(i == 0 ? "first" : i == 1 ? "second" : "third")} team: {string.Join(", ", t.Select(id => Name(id)))}")));
        if (summary.FinalsMvpPlayerId is { } fm) parts.Add($"Finals MVP: {Name(fm)} ({League.TeamById(summary.ChampionTeamId).FullName}).");
        var ours = a.AllLeague.SelectMany(t => t).Where(id => all.TryGetValue(id, out var p) && p.TeamId == HumanTeam.Id).Select(id => Name(id)).ToList();
        if (ours.Count > 0) parts.Insert(0, $"Your All-League honourees: {string.Join(", ", ours)}.");
        Post($"Season awards: {Name(a.Mvp)} is MVP", string.Join("\n\n", parts), "Awards");
        var honours = Offseason!.Report.HallOfFame.Select(h => $"Hall of Fame: {h.Name}, inducted after a {League.Careers[h.Id].Seasons}-season career: {League.Careers[h.Id].Points:N0} points, {League.Careers[h.Id].Mvps} MVP{(League.Careers[h.Id].Mvps == 1 ? "" : "s")}, {League.Careers[h.Id].Titles} title{(League.Careers[h.Id].Titles == 1 ? "" : "s")}, {League.Careers[h.Id].AllStars} All-Star selections.")
            .Concat(Offseason.Report.RetiredNumbers.Select(x => $"{x.Item2.FullName} retire {x.Item1.Name}'s number.")).ToList();
        if (honours.Count > 0) Post(honours.Count == 1 ? honours[0].Split(',')[0].Split(" retire ")[0] : $"Hall of Fame and retired numbers ({honours.Count})", string.Join("\n", honours), "Awards");
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
                if (League.Finance.OpenMarket)
                {
                    // No draft: free agency opens at once (the graduates are in the pool).
                    NewWindow();
                    Hardwood.Core.World.WorldMarket.AiMoves(World, Offseason, ControlledTeamId);
                    Offseason.AiFreeAgency(ControlledTeamId);
                    AiTransferRound(15); // the summer transfer market (decision #87)
                    if (Employed && !InCollege && _rng.Chance(0.6)) MaybeBidForYourPlayer();
                    foreach (var (title, body) in World.News) PostWorld(title, body); MaybePostDigest(now: true);
                    World.News.Clear();
                    if (Employed)
                    {
                        var grads = League.FreeAgents.Where(p => p.RookieSeason == League.Season + 1).OrderByDescending(p => p.CurrentAbility).Take(6).ToList();
                        Post("Free agency is open", $"Make offers to free agents on the Offseason page. {Budgets.Describe(HumanTeam)}."
                            + (grads.Count > 0 ? $"\n\nThe best of this year's college graduates still available: {string.Join(", ", grads.Select(p => $"{p.Name} ({p.PrimaryPosition}, {p.CurrentAbility})"))}." : ""), "Roster");
                    }
                    break;
                }
                Offseason.AutoDraftUntil(ControlledTeamId);
                if (Offseason.DraftComplete) AdvanceOffseason();
                else Post("You're on the clock", $"Pick {Offseason.NextPickIndex + 1} (round {Offseason.CurrentRound}). {Offseason.Prospects.Count} prospects remain.", "Draft");
                break;
            case OffseasonPhase.Draft:
                Offseason.AdvanceToFreeAgency();
                Hardwood.Core.World.WorldMarket.AiMoves(World, Offseason, ControlledTeamId);
                Offseason.AiFreeAgency(ControlledTeamId);
                foreach (var (title, body) in World.News) PostWorld(title, body); MaybePostDigest(now: true);
                World.News.Clear();
                var ours = Offseason.Report.Draft.Where(d => d.Team == HumanTeam).ToList();
                if (InNabl && !League.Finance.OpenMarket) Post("Draft complete", string.Join("\n", ours.Select(d => $"Round {d.Round}, pick {d.Pick}: {d.Player.Name} ({d.Player.PrimaryPosition}, age {d.Player.Age(Offseason.Date)}, {d.Player.CurrentAbility}/{d.Player.PotentialAbility})")) + $"\n\nFree agency is open. You have {HumanTeam.Roster.Count} players; {League.Finance.Describe(ContractMarket.Payroll(HumanTeam))}. Rosters need {League.Finance.RosterMin} to {League.Finance.RosterMax}.", "Draft");
                break;
            case OffseasonPhase.FreeAgency:
                int levelBefore = MyLevel; string leagueBefore = HumanLeagueName;
                Offseason.Complete();
                StartNewSeason();
                // Promotion and relegation happen at the end of the summer (decision #79).
                if (Employed && HumanLeagueName != leagueBefore)
                {
                    bool up = MyLevel > 0 && MyLevel < levelBefore;
                    // A relegated club's first season down is a rebuild: no mid-season sacking (critic review #5).
                    if (!up) { Job.ProtectedSeason = League.Season; Job.ProtectedReason = "rebuild"; }
                    Post(up ? $"Promoted to {HumanLeagueName}!" : $"Relegated to {HumanLeagueName}",
                        up ? $"The {HumanTeam.FullName} go up from {leagueBefore} to {HumanLeagueName}. The owner has set new budgets and expectations for life at the higher level."
                           : $"The {HumanTeam.FullName} drop from {leagueBefore} to {HumanLeagueName}. Wages already agreed don't fall with you; the owner expects you to bounce back.", "Board");
                }
                break;
        }
        SaveNow();
    }

    /// <summary>Buy an overseas player out of his contract and sign him; also brings over a stashed pick. Null = done, else why not.</summary>
    public string? BuyOut(Player p)
    {
        if (!Employed) return "You aren't coaching a team right now.";
        if (Abroad) return $"Only {Hardwood.Core.Names.Brand.Top} clubs buy players out of overseas contracts.";
        if (Offseason == null) return "Players can only move in the summer.";
        var terms = Hardwood.Core.World.WorldMarket.Terms(World, Offseason, HumanTeam, p);
        if (!terms.Possible) return terms.Note;
        var club = Hardwood.Core.World.WorldMarket.ClubOf(World, p);
        if (!Hardwood.Core.World.WorldMarket.BuyOut(World, Offseason, HumanTeam, p)) return "The move fell through.";
        Post($"{p.Name} signs", $"{p.Name} leaves {club?.FullName} and joins you. {terms.Note} He signs for ${terms.NablSalary / 1e6:F1}M a year.", "Roster");
        SaveNow();
        return null;
    }

    /// <summary>Send one of this year's draft picks to play overseas, keeping his rights. Null = done, else why not.</summary>
    public string? Stash(Player p)
    {
        if (!Employed) return "You aren't coaching a team right now.";
        if (Abroad) return $"Only {Hardwood.Core.Names.Brand.Top} clubs have draft picks.";
        if (Offseason == null) return "Picks can only be stashed in the summer.";
        if (Hardwood.Core.World.WorldMarket.CannotStash(Offseason, HumanTeam, p) is { } why) return why;
        if (!Hardwood.Core.World.WorldMarket.Stash(World, Offseason, HumanTeam, p, new Rng(World.NextRngSeed())))
            return "No overseas club has room for him under its import limit.";
        var club = Hardwood.Core.World.WorldMarket.ClubOf(World, p)!;
        Post($"{p.Name} stashed", $"{p.Name} will play for {club.FullName}. You keep his rights and can bring him over in a later summer (buyout ${p.Contract!.Buyout / 1e6:F2}M).", "Roster");
        SaveNow();
        return null;
    }

    /// <summary>Human draft pick; then AI picks run until the human is on the clock again or the draft ends.</summary>
    public DraftPick MakePick(Player prospect)
    {
        var pick = Offseason!.MakePick(prospect);
        Offseason.AutoDraftUntil(ControlledTeamId);
        if (Offseason.DraftComplete) AdvanceOffseason();
        SaveNow();
        return pick;
    }

    private List<int>? _summerRoster;

    /// <summary>Abroad: what the sporting director did over the summer.</summary>
    private void SummerReport()
    {
        if (_summerRoster == null) return;
        var now = HumanTeam.Roster.Select(p => p.Id).ToHashSet();
        var left = _summerRoster.Where(id => !now.Contains(id)).Select(id => League.FreeAgents.Concat(League.Retired).Concat(World.Players).Concat(League.AllActivePlayers).FirstOrDefault(p => p.Id == id)?.Name).Where(n => n != null).ToList();
        var joined = HumanTeam.Roster.Where(p => !_summerRoster.Contains(p.Id)).Select(p => $"{p.Name} ({p.PrimaryPosition}, {p.CurrentAbility})").ToList();
        Post("Summer business", (left.Count > 0 ? $"Left: {string.Join(", ", left)}." : "Nobody left.") + "\n\n"
            + (joined.Count > 0 ? $"Joined: {string.Join(", ", joined)}." : "No new signings.") + $"\n\nSquad size: {HumanTeam.Roster.Count}.", "Roster");
        _summerRoster = null;
    }

    private void StartNewSeason()
    {
        NewWindow();
        AgreedFees.Clear(); IncomingBids.Clear(); // the summer window shuts
        Phase = SessionPhase.Preseason;
        World.EndSummer(League.Season, _sim, League.FreeAgents); // normally already run at the end of free agency
        foreach (var (title, body) in World.News) PostWorld(title, body); MaybePostDigest(now: true);
        World.News.Clear();
        if (Employed)
        {
            if (Abroad && !InCollege) SummerReport();
            Job.Current = Jobs.Board.NextExpectations(ContextOf(HumanTeam), HumanTeam, League.Season, Job.Current,
                MyLeagueKey, MyLevel, Job.Current?.League is { Length: > 0 } was ? LevelOf(was) : 0);
            Job.WarningLevel = 0;
            PostExpectations();
        }
        Today = CareerYearStart();
        PlayWorld(Today.AddDays(-1));
        HumanTeam.DepthChart = null;
        Midseason = null;
        BuildSeason();
        var last = League.History.Last();
        Post($"Welcome to {League.Season}-{(League.Season + 1) % 100:00}",
            $"Last season's {Hardwood.Core.Names.Brand.Top} champions: {League.TeamById(last.ChampionTeamId).FullName}. " + (NextMyGame is { } g ? $"Your first game is on {g.Date:MMM d}." : $"Opening night is {OpeningNight:MMM d}.") + " Review your rotation before the first game.", "Board");
        // Money news for your club (critic review, Nov 10): parachute payments and the debt embargo.
        var m = HumanTeam.Money;
        if (Employed && m.Embargo)
        {
            Job.Confidence = Math.Max(0, Job.Confidence - 8);
            Post("Transfer embargo", $"The club's debt (${-m.Balance / 1e6:F1}M) is more than half a season's revenue. The owner has frozen transfer fees until the books recover: free agents only, within the wage budget. The board is watching.", "Board");
        }
        if (Employed && m.Parachute > 0)
            Post("Parachute payment", $"After relegation the league pays a parachute of ${m.Parachute / 1e6:F1}M this season ({(m.ParachuteYears > 1 ? "it halves next season, then stops" : "the last one")}). Every contract's relegation clause has cut wages by half. Use the time to bring the payroll down to this level.", "Board");
    }

    // ------------------------------------------------------------------------------------------
    /// <summary>Saves after every day and after every offseason decision.</summary>
    public void SaveNow()
    {
        if (Save == null) return;
        // A college program you coach keeps its staff in the save like a club abroad (C2).
        var clubs = World.Clubs.Concat(World.College?.Programs.Where(t => t.Staff.Count > 0) ?? Enumerable.Empty<Team>());
        Save.Save(League, Stats.Values, Phase == SessionPhase.Offseason ? Offseason?.Prospects : null, World.Players, clubs, World.College?.Players);
        Save.SaveSession(this);
        Save.SaveWorld(World);
        Save.SavePeople(this);
        Save.SaveCareer(this);
    }

    public static GameSession Load(string path)
    {
        var save = new SaveFile(path);
        var league = save.Load();
        var world = save.LoadWorld(league, save.LoadedOverseas, save.LoadedCollege);
        var session = save.LoadSession(league, world);
        save.LoadPeople(session);
        save.LoadCareer(session);
        session.CatchUpWorld();
        session.FixStaffPay();
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
        Hardwood.Core.World.WorldMarket.Attach(World, Offseason, _sim);
    }

    public string DescribeSide(IEnumerable<Player> players, IEnumerable<DraftPickAsset> picks)
    {
        var parts = players.Select(p => p.Name).Concat(picks.Select(p => p.Describe(id => League.TeamById(id).Abbreviation))).ToList();
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    private static string Ordinal(int n) => n % 100 is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
}
