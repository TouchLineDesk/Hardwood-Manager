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
    /// <summary>The season's playoff series with their games: the live bracket, or, after a reload in the summer, the series
    /// as saved (review #14 bug #4: the summer forgot the playoff games, 98 → 82 on the schedule).</summary>
    public IReadOnlyList<Series> PlayoffSeries => Postseason?.Series ?? (IReadOnlyList<Series>)_pastPlayoffs;
    private List<Series> _pastPlayoffs = new();
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
        Names.Brand.Real = Hardwood.Core.RealWorld.RealWorldData.IsReal(league); // a loaded career wears its own world
        // A save from before the world existed (or a test) gets one built around its NABL; it catches up on load.
        _worldGenerated = world == null;
        World = world ?? Hardwood.Core.World.GameWorld.Generate(league, seed, DefaultFullSim, new DateOnly(league.Season, 10, 1), expandTop: false);
        _rng = new Rng(rngState ?? (seed ^ 0x5E5510));
        _sim = new GameSimulator(engine);
        Standings = new Standings(league);
        Today = OpeningNight.AddDays(-7);
        Phase = SessionPhase.Preseason;
        // The human team is run by hand. A new career (or a save from before tactics were saved) starts from the
        // assistant's setup: the system and scheme that suit the roster, everyone in his best role.
        // A loaded career (it brings its random state) keeps its tactics as saved: Auto there means you delegated
        // tactics on the Staff page, and the load mustn't switch that off (review #14, the bug finder).
        if (HumanTeam.Tactic.Auto && rngState == null) CoachAi.AssistantSetup(HumanTeam);
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

    /// <summary>Start a new career. Every career is a real-world one since v0.74.0 (D61 B, #478): the NBA's real teams,
    /// players and history and the real Division I on top of the generated lower tiers, clubs abroad and college players.</summary>
    public static GameSession NewGame(ulong seed, int humanTeamId, string? savePath, int season = 2026, IEnumerable<string>? fullSim = null, Jobs.Coach? coach = null,
        Names.NamePack? names = null)
    {
        var (league, world) = Hardwood.Core.RealWorld.RealWorldData.NewWorld(seed, season, fullSim ?? DefaultFullSim);
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
        session.Post("Welcome to the job", $"You are the new head coach and general manager of {session.HumanTeam.TheFullName} ({session.HumanLeagueName}). "
            + (first != null ? $"Your first game is on {first.Date:MMM d}: set your rotation and tactics, then press Continue." : "Set your rotation and tactics, then press Continue.")
            + (Hardwood.Core.RealWorld.RealWorldData.Seasons.LastOrDefault() is { } last
                ? $"\n\nThe reigning NBA champions are the {last.Champion} ({last.Label}; Finals MVP {last.FinalsMvp}). The league's whole history, records and every franchise's titles are on the History page."
                + $"\n\nThe rosters are the real ones: every player who had played in 2026-27 by Nov 11, 2026, rated from his numbers this season and last (checked {Hardwood.Core.RealWorld.RealPlayers.Checked}). Contracts, birthdays, heights and head coaches are real too (checked {Hardwood.Core.RealWorld.RealPlayers.RostersChecked}): this season's salary for each season left on his deal. Players on real rosters who hadn't played yet this season are rated from last season (everyone starts healthy). Careers before this season are real for the league's leading veterans (checked {Hardwood.Core.RealWorld.RealCareers.Checked}), so the records book and the NBA's records to beat start from the real past."
                + (session.World.College?.IsReal == true ? $" College basketball has every Division I program in its 2026-27 conference, with its real name, colours and tournament history (checked {Hardwood.Core.RealWorld.RealColleges.Checked}); the college players and coaches are made up." : "") : "")
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
        _pastPlayoffs = new();
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
        if (!Employed) return false;   // run 81 (code review, like H43): out of work there are no staff of yours to change
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
        if (!Employed) return;   // run 81 (code review, like H43)
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
        // March Madness is a college coach's own event (C4 part 2, #306): its round-by-round stories come straight to the
        // inbox. Pro careers read them in the world digest.
        if (Employed && InCollege && Hardwood.Core.College.CollegeWorld.IsMadnessHeadline(title)) { Post(title, body, "World"); return; }
        WorldDigest.Add(new DigestItem(Today, title, FirstSentence(body)));
        if (WorldDigest.Count > 200) WorldDigest.RemoveRange(0, WorldDigest.Count - 200);
    }

    /// <summary>Once a week (or when it fills up), the world's headlines arrive as one message.</summary>
    private void MaybePostDigest(bool now = false)
    {
        if (WorldDigest.Count == 0) return;
        // Review #12, Top fix 1 (run 42): during the regular season the headlines ride in the weekly results message
        // instead of a message of their own ("Around the world" was 53 of ~280 messages per three seasons).
        // Run 48: also between a Monday's results and the next game (WeekResults is empty then), so a burst of news early
        // in the week waits for next Monday's message instead of arriving on its own.
        bool resultsComing = WeekResults.Count > 0 || (Employed && Phase == SessionPhase.RegularSeason);
        if (!now && resultsComing && WorldDigest.Count < 80) return;
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

    /// <summary>The regular-season results since the last results message, posted together every Monday (or every other, by setting).</summary>
    public List<string> WeekResults { get; } = new();

    /// <summary>Short injuries (no lasting loss, back within <see cref="KnockDays"/> days) from regular-season games, carried by the next results message.</summary>
    public List<string> WeekKnocks { get; } = new();
    public const int KnockDays = 14;

    /// <summary>A results message's title starts with one of these ("This week: 3-1 …", "Last two weeks: 5-2 …").</summary>
    public static bool IsResultsTitle(string title) => title.StartsWith("This week") || title.StartsWith("Last two weeks");

    private void MaybePostResults(bool now = false)
    {
        if (WeekResults.Count == 0) return;
        if (!now && Today.DayOfWeek != DayOfWeek.Monday) return;
        // Every Monday again since v0.56.0 (Danny, #243): the inbox can be cleaned, so volume is his choice. The inbox
        // settings keep the every-other-Monday rhythm (critic review #8) as an option; a press conference or
        // locker-room moment still rides on a results message, so its week posts one anyway.
        bool evenWeek = !InboxSettings.ResultsEveryOtherWeek || Today.DayNumber / 7 % 2 == 0;
        bool decisionWaiting = Media.Decisions.Any(d => (d.Open || d.Created == Today) && d.MessageId == 0 && d.TeamId == HumanTeam.Id);
        if (!now && !evenWeek && !decisionWaiting) return;
        bool twoWeeks = WeekResults.Count > 0 && _resultsSince is { } since && Today.DayNumber - since.DayNumber > 7;
        int w = WeekResults.Count(l => l.Contains("  W ")), l = WeekResults.Count - w;
        string body = string.Join("\n", WeekResults) + "\n\nBox scores are on the Schedule & Results page.";
        // Review #12, Top fix 1 (run 42): short knocks and the world's headlines ride along instead of arriving one by one.
        if (WeekKnocks.Count > 0) body += "\n\nKnocks (back within two weeks):\n" + string.Join("\n", WeekKnocks.Select(k => $"• {k}"));
        if (WorldDigest.Count > 0)
        {
            body += $"\n\nAround the world ({WorldDigest.Count} {(WorldDigest.Count == 1 ? "story" : "stories")}):\n" + string.Join("\n", WorldDigest.Select(d => $"• {d.Title}" + (d.Line.Length > 0 ? $": {d.Line}" : "")));
            WorldDigest.Clear();
            LastDigest = Today;
        }
        Post($"{(twoWeeks ? "Last two weeks" : "This week")}: {w}-{l} ({MyRecord.Wins}-{MyRecord.Losses} overall)", body, "Game");
        WeekResults.Clear();
        WeekKnocks.Clear();
        _resultsSince = null;
    }

    /// <summary>The day of the oldest result waiting in <see cref="WeekResults"/> (not saved: after a load the next message just says "This week").</summary>
    private DateOnly? _resultsSince;

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
        || (Employed && Phase == SessionPhase.Postseason && Postseason != null && !Postseason.Complete && Postseason.Active.Any(se => se.Higher == HumanTeam || se.Lower == HumanTeam))
        // A promotion playoff (run 102): the top league's 4th-placed club plays it in tier 2's fixtures.
        || (Employed && Phase == SessionPhase.Postseason && World.GameOn(HumanTeam.Id, Today) != null);

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
        bool ours = r.Home.Team == HumanTeam || r.Away.Team == HumanTeam;
        // The real NBA's record book (R2 part 2b, #225): only in a real-world career's NBA.
        bool real = RealWorld.RealWorldData.IsReal(League);
        if (real) RealWorld.RealRecordChase.EnsureSeeded(League);
        void Chase(RealWorld.ChaseNews n)
        {
            // A broken NBA record reaches everyone; a record watch or a legend passed is yours, or the digest's.
            if (n.Broken || ours) Post(n.Title, n.Body, "League"); else PostWorld(n.Title, n.Body);
        }
        foreach (var box in new[] { r.Home, r.Away })
        foreach (var l in box.Lines)
        {
            if (l.Seconds <= 0) continue;
            if (!Stats.TryGetValue(l.Player.Id, out var s))
                Stats[l.Player.Id] = s = new PlayerSeasonStats { PlayerId = l.Player.Id, TeamId = box.Team.Id, Season = League.Season };
            // Career milestones (roadmap 4): completed seasons plus this one, before and after this game.
            var career = League.Careers.GetValueOrDefault(l.Player.Id);
            int pts = (career?.Points ?? 0) + s.Points, reb = (career?.Rebounds ?? 0) + s.OReb + s.DReb, ast = (career?.Assists ?? 0) + s.Assists, gp = (career?.Games ?? 0) + s.Games;
            int stl = (career?.Steals ?? 0) + s.Steals, blk = (career?.Blocks ?? 0) + s.Blocks, seasonPts = s.Points, season3 = s.ThreePM;
            s.Add(l);
            if (real)
            {
                var opp = box == r.Home ? r.Away.Team : r.Home.Team;
                foreach (var n in RealWorld.RealRecordChase.Check(League.Records, l.Player, box.Team, opp.Abbreviation, Today, League.Season,
                    new Dictionary<string, int> { ["pts"] = l.Points, ["reb"] = l.Rebounds, ["ast"] = l.Assists, ["stl"] = l.Steals, ["blk"] = l.Blocks, ["3pm"] = l.ThreePM },
                    new Dictionary<string, (int, int)> { ["pts"] = (seasonPts, s.Points), ["3pm"] = (season3, s.ThreePM) },
                    new Dictionary<string, (int, int)>
                    {
                        ["pts"] = (pts, pts + l.Points), ["reb"] = (reb, reb + l.Rebounds), ["ast"] = (ast, ast + l.Assists),
                        ["stl"] = (stl, stl + l.Steals), ["blk"] = (blk, blk + l.Blocks),
                    }))
                    Chase(n);
            }
            var passed = League.Records.CheckMilestones(l.Player, box.Team, Today, League.Season, new Dictionary<string, (int, int)>
            {
                ["points"] = (pts, pts + l.Points), ["rebounds"] = (reb, reb + l.Rebounds), ["assists"] = (ast, ast + l.Assists), ["games"] = (gp, gp + 1),
            });
            // Yours arrive on their own; other clubs' big ones wait for the fortnightly digest (dial-in item 5: inbox volume).
            // Since real careers carry in (R2 part 2b), veterans pass round numbers often: your players' smaller steps go
            // to the digest with everyone else's, and the big ones still arrive on their own (inbox volume, #226).
            foreach (var m in passed)
                if (box.Team == HumanTeam && m.Threshold >= (m.Stat == "points" ? 10_000 : m.Stat == "games" ? 750 : m.Stat == "rebounds" ? 5_000 : 4_000))
                    Post($"Milestone: {m.PlayerName} passes {m.Threshold:N0} {m.Stat}", $"{m.Text} on {Today:MMM d}.", "League");
                else if (box.Team == HumanTeam)
                    PostWorld($"Milestone: {m.PlayerName} passes {m.Threshold:N0} {m.Stat}", $"{m.Text} on {Today:MMM d}.");
                else if (m.Threshold >= (m.Stat == "points" ? 20_000 : m.Stat == "games" ? 1_000 : 10_000))
                    PostWorld($"Milestone: {m.PlayerName} passes {m.Threshold:N0} {m.Stat}", $"{m.Text} on {Today:MMM d}.");
        }
        if (real)
            foreach (var (box, opp) in new[] { (r.Home, r.Away), (r.Away, r.Home) })
                if (RealWorld.RealRecordChase.CheckTeam(League.Records, box.Team, opp.Team.Abbreviation, box.Points, opp.Points, Today) is { } n) Chase(n);
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
        GradeStudioPicks(played);
        PlayWorld(Today);
        bool inSeason = Abroad ? Phase != SessionPhase.Offseason : Phase is SessionPhase.RegularSeason or SessionPhase.Postseason;
        if (inSeason && Today.DayOfWeek == DayOfWeek.Monday)
        {
            WeeklyPeople();
            bool regular = InCollege ? !World.College!.Season.Selected && MyRecord.Games > 0
                : Abroad ? HumanLeague?.Season.Stage == CompetitionSeason.RegularSeason : Phase == SessionPhase.RegularSeason;
            if (regular) WeeklyBoard();
        }
        if (!Employed) DailyJobMarket(); else CareerMarketStep();
        if (Today.DayOfWeek == DayOfWeek.Monday) { DelegatedTraining(); DelegatedRecruiting(); WeeklyRecruiting(); WeeklyTrail(); }
        DailyTransfers();
        MaybePostDigest();
        MaybePostJobs();
        MaybePostResults(now: Phase is SessionPhase.Postseason or SessionPhase.Offseason);
        AttachPressToResults();
        MaybeAutoClear();
        if (Phase == SessionPhase.RegularSeason)
        {
            if (Today == TradeDeadline.AddDays(-10) || Today == new DateOnly(League.Season, 12, 15))
                AiTrades();

            // A college coach doesn't get the pro league's calendar news (critic review #7/#8: about 20 messages per three seasons).
            if (Today == TradeDeadline && !InCollege && !League.Finance.OpenMarket)
                Post("Trade deadline has passed", "No more trades until the season ends.", "League");
            // On the open market there are no trades (#80): the January window has shut instead (critic review #8).
            if (Today == new DateOnly(League.Season + 1, 2, 1) && !InCollege && League.Finance.OpenMarket)
                Post("The January window has shut", "No more transfers until the summer window opens.", "League");

            if (Today == AllStarDate && Midseason == null)
            {
                Midseason = new AwardsEngine(League, Standings, Stats, _rng).AllStar(League.Season, _sim, Today);
                var all = League.AllActivePlayers.ToDictionary(p => p.Id);
                string Names(IEnumerable<int> ids) => string.Join(", ", ids.Select(id => all.TryGetValue(id, out var p) ? $"{p.Name} ({League.TeamById(p.TeamId!.Value).Abbreviation})" : "?"));
                var ours = Midseason.AllStarsEast.Concat(Midseason.AllStarsWest).Where(id => all.TryGetValue(id, out var p) && p.TeamId == HumanTeam.Id).Select(id => all[id].Name).ToList();
                if (!InCollege) Post("All-Star Game", $"{Midseason.AllStarGame}. Game MVP: {(Midseason.AllStarMvpId is { } m && all.TryGetValue(m, out var mp) ? mp.Name : "-")}."
                    + (ours.Count > 0 ? $"\n\nYour All-Stars: {string.Join(", ", ours)}." : "\n\nNo one from your team was selected.")
                    + $"\n\nEast: {Names(Midseason.AllStarsEast)}\n\nWest: {Names(Midseason.AllStarsWest)}", "League");
            }

            if (Schedule.All(g => Results.ContainsKey(g.Index)))
            {
                Phase = SessionPhase.Postseason;
                Postseason = new Postseason(League, Standings);
                var promotion = World.StartPromotionPlayoffs(Standings, Today);
                if (InNabl && Postseason.DivisionFormat)
                {
                    var rec = Standings[HumanTeam];
                    int place = Standings.Conference(HumanTeam.Conference).Where(r => r.Team.Division == HumanTeam.Division).ToList().FindIndex(r => r.Team == HumanTeam) + 1;
                    int seed = Postseason.DivisionSeeds(Standings, HumanTeam.Conference).IndexOf(HumanTeam) + 1;
                    var ourPromo = promotion.FirstOrDefault(x => x.Top == HumanTeam);
                    Post("Regular season complete",
                        $"You finished {rec.Wins}-{rec.Losses}, {Ordinal(place)} in the {HumanTeam.Conference} · {HumanTeam.Division}. " +
                        (seed > 0 ? $"You are in the playoffs as the {HumanTeam.Conference}'s {Ordinal(seed)} seed."
                         : place == 3 ? $"You play in the {Postseason.ThirdPlaceName} with the other seven third-placed teams."
                         : ourPromo.Challenger != null ? $"You play the promotion playoff against {ourPromo.Challenger.FullName}, the {HumanTeam.Division} winners in {World[UsaPyramid.Tier2].Profile.Name}: best of three, Games 1 and 3 at their place. Win it and you stay up; lose it and you go down at the end of the summer."
                         : "Your season is over."), "League");
                }
                else if (InNabl)
                {
                    var rec = Standings[HumanTeam];
                    int seed = Standings.Conference(HumanTeam.Conference).FindIndex(r => r.Team == HumanTeam) + 1;
                    Post("Regular season complete",
                        $"You finished {rec.Wins}-{rec.Losses}, {Ordinal(seed)} in the {HumanTeam.Conference}. " +
                        (seed <= 6 ? "You are in the playoffs." : seed <= 10 ? "You are in the play-in." : (League.Finance.OpenMarket ? "Your season is over." : "Your season is over; the lottery awaits.")), "League");
                }
                PostPromotionPlayoffs(promotion);
            }
        }
        else if (Phase == SessionPhase.Postseason && Postseason != null)
        {
            if (Postseason.Stage != _stageBefore && !Postseason.Complete && Postseason.Active.FirstOrDefault(x => x.Higher == HumanTeam || x.Lower == HumanTeam) is { } mineNow)
            {
                bool third = Postseason.IsThirdPlace(mineNow.Round);
                var round = Postseason.Active.Where(x => Postseason.IsThirdPlace(x.Round) == third).ToList();
                Post(third ? $"{mineNow.Round} begin" : $"{Postseason.Stage} begins", string.Join("\n", round.Select(s => $"{s.Higher.FullName} vs {s.Lower.FullName}")), "League");
            }
            if (Postseason.DivisionFormat && _stageBefore == "Conference Finals" && Postseason.Stage == "Finals" && Postseason.ThirdPlaceWinner is { } tw)
            {
                bool ours = tw == HumanTeam || Postseason.ThirdPlaceRunnerUp == HumanTeam;
                Post($"{tw.FullName} win the {Postseason.ThirdPlaceName}",
                    $"{tw.FullName} beat {Postseason.ThirdPlaceRunnerUp!.FullName} in the {Postseason.ThirdFinal} and take the trophy and {Franchise.Money(Postseason.ThirdPlacePrizeFor(true))} in prize money " +
                    $"({Franchise.Money(Postseason.ThirdPlacePrizeFor(false))} to the runner-up)." + (tw == HumanTeam ? " Congratulations: silverware for your club." : ours ? " The runner-up's prize is in your accounts." : ""), "League");
            }
            if (Postseason.Complete)
            {
                string title = $"{Postseason.Champion!.FullName} are champions";
                if (_championPostedSeason != League.Season && !Inbox.Any(m => m.Title == title && m.Date >= OpeningNight))
                {
                    _championPostedSeason = League.Season;
                    Post(title, $"{Postseason.Champion.FullName} defeated {Postseason.RunnerUp!.FullName} in the Finals." +
                        (Postseason.Champion == HumanTeam ? " Congratulations: a title for your franchise." : ""), "League");
                }
                // A club abroad may still be playing (overseas playoffs run into June): the summer waits for it.
                if (!HumanSeasonOver && Today < new DateOnly(League.Season + 1, 7, 20))
                {
                    Today = Today.AddDays(1);
                    SaveIfDue();
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
        SaveIfDue();
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
        if (series == null) _resultsSince ??= r.Date ?? Today;
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
        {
            int days = inj.DaysOut(Today);
            if (series == null && inj.AttributeLoss <= 0 && days <= KnockDays)
            {
                WeekKnocks.Add($"{p.Name}: {inj.Name}, out about {days} days (back {inj.ReturnDate:MMM d})");
                continue;
            }
            Post($"{p.Name} injured: {inj.Name}", $"Out approximately {inj.DaysOut(Today)} days (expected return {inj.ReturnDate:MMM d}).{(inj.AttributeLoss > 0 ? " The medical staff expect some lasting loss of athleticism." : "")}", "Medical");
        }
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
        // H43 (run 81): out of work (or at a college program) there is no club of yours to trade from.
        if (RosterMovesClosed is { } closed) return new TradeEvaluation(false, closed, 0, 0, 0, false, closed);
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
            $"{(InCollege ? "Athletic director " : "")}{o.Name} ({o.Style.ToLower()}) has set your goal: {(e.Survive ? "stay up: newly promoted, avoid the relegation places" : Jobs.Board.LowerFirst(Jobs.Board.Describe(e.Goal, ContextOf(HumanTeam))))} (around {e.TargetWins} wins)."
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
        bool overachieved = false;
        // H80 (run 105): hired after your club's season ended, you aren't judged on it; the board reviews your own season.
        bool hiredForNext = Employed && Job.Current is { } cur && cur.Season > League.Season;
        if (Employed)
        {
            var rec = MyRecord;
            var achieved = InCollege ? Jobs.Board.Achieved(World.College!, HumanTeam.Id)
                : HumanLeague is { } c ? Jobs.Board.Achieved(HumanTeam.Id, c.Season, c.Profile) : Jobs.Board.Achieved(HumanTeam, Postseason);
            // A division winner in tiers 2-3 goes up (#319): the board counts that as a top-three finish at least.
            bool wonDivision = HumanLeague is { } hc && UsaPyramid.WonDivision(hc, HumanTeam);
            if (wonDivision && achieved < Jobs.SeasonGoal.SecondRound) achieved = Jobs.SeasonGoal.SecondRound;
            bool sacked = false;
            if (!hiredForNext)
            {
                (string title, string body, sacked) = Jobs.Board.SeasonReview(Job, Coach, ContextOf(HumanTeam), HumanTeam, achieved, rec.Wins, rec.Losses, MyStats, Today, League.Season,
                    relegated: GoingDown(), wonDivision: wonDivision, madnessRound: InCollege ? World.College!.MadnessReached(HumanTeam.Id) : -1);
                Post(Boss(title), Boss(body), "Board");
            }
            if (RelegationTiebreak() is { } tie)
                Post("Relegated on a tiebreak", tie + " The last club in each division goes down at the end of the summer; clubs level on record are split by point difference.", "Board");
            if (InNabl && UsaPyramid.PromotionSeriesOf(World, HumanTeam.Id) is { Complete: true } ps && ps.LoserId == HumanTeam.Id && FindTeam(ps.WinnerId!.Value) is { } beat)
                Post("Relegated", $"{beat.FullName} beat you {ps.Score} in the promotion playoff. You go down to {World[UsaPyramid.Tier2].Profile.Name} at the end of the summer.", "Board");
            if (sacked) Fire();
            else if (!hiredForNext) overachieved = Job.Current is { } e && Overachieved(e, achieved, rec.Wins, rec.Losses, ContextOf(HumanTeam));
        }
        foreach (var t in Jobs.Board.AiSeasonChanges(League, Standings, _rng, ControlledTeamId)) CoachLeaves(t, "parted ways with");
        WorldCoachingChanges();
        // The career path (#651): college programs change coaches too, then bigger clubs may come for you.
        CollegeCoachingChanges();
        CareerApproaches(overachieved);
    }

    /// <summary>The promotion playoffs have started (run 102): your own pairing goes to the inbox, the rest to the digest.</summary>
    private void PostPromotionPlayoffs(List<(Team Top, Team Challenger)> pairs)
    {
        if (pairs.Count == 0) return;
        string tier2 = World[UsaPyramid.Tier2].Profile.Name;
        string list = string.Join("\n", pairs.Select(x => $"{x.Challenger.FullName} ({tier2}) vs {x.Top.FullName}"));
        string body = $"The last club in each {Hardwood.Core.Names.Brand.Top} division plays the winner of the same division in {tier2}, best of three, "
            + "the challenger at home in Games 1 and 3. The winner plays in the " + Hardwood.Core.Names.Brand.Top + $" next season.\n\n{list}";
        var ours = pairs.FirstOrDefault(x => x.Challenger == HumanTeam);
        if (ours.Top != null)
            Post("Promotion playoff", $"You won the {HumanTeam.Division} and play {ours.Top.FullName} for a place in the {Hardwood.Core.Names.Brand.Top}: best of three, Games 1 and 3 at home. Win it and you go up at the end of the summer.\n\n{body}", "League");
        else if (pairs.Any(x => x.Top == HumanTeam)) Post("Promotion playoffs begin", body, "League");
        else PostWorld("Promotion playoffs begin", body);
    }

    /// <summary>
    /// Your club finished last in its division and goes down at the summer (review #14 bug #16): the top league's last in each
    /// division and the last in each division of tiers 2-4 (UsaPyramid.Promote's rule; nobody goes down from the bottom tier).
    /// </summary>
    public bool GoingDown()
    {
        if (InCollege) return false;
        if (InNabl)
        {
            if (!Postseason.UsesDivisions(League) || World.Competitions.All(c => c.Key != UsaPyramid.Tier2)) return false;
            // The promotion playoff decides it (run 102): down only if you lost it.
            if (UsaPyramid.PromotionSeriesOf(World, HumanTeam.Id) is { } ps) return ps.Complete && ps.LoserId == HumanTeam.Id;
            var div = League.Teams.Where(t => t.Conference == HumanTeam.Conference && t.Division == HumanTeam.Division).ToList();
            if (div.Count < 2) return false;
            // H33 (run 82): the same order as the Standings page and the summer's relegation (win %, then point difference).
            var order = Standings.All().Select((r, i) => (r.Team, i)).ToDictionary(x => x.Team, x => x.i);
            return div.OrderByDescending(t => order[t]).First() == HumanTeam;
        }
        if (HumanLeague is not { } c || !UsaPyramid.IsNational(c.Key) || c.Key == UsaPyramid.Tier5 || !c.Season.Fixtures.Any(f => f.Played)) return false;
        var table = c.Season.Table().Select(r => r.TeamId).ToList();
        var mine = UsaPyramid.Divisions(c.Key, c.Clubs).FirstOrDefault(d => d.Teams.Contains(HumanTeam)).Teams;
        if (mine == null || mine.Count < 2) return false;
        return table.LastOrDefault(id => mine.Any(x => x.Id == id)) == HumanTeam.Id;
    }

    /// <summary>
    /// K1 (run 111): the clubs going down from the top league lose the players who use their relegation release clauses, at the
    /// start of free agency. Your club's players' choices are an inbox item with names and reasons.
    /// </summary>
    private void UseReleaseClauses()
    {
        if (InCollege) return;
        var (news, mine) = Hardwood.Core.World.UsaPyramid.UseReleaseClauses(World, League, Offseason!.Date, ControlledTeamId);
        foreach (var (title, body) in news) World.News.Add((title, body));
        if (mine.Count == 0) return;
        var left = mine.Where(x => x.Left).ToList(); var stayed = mine.Where(x => !x.Left).ToList();
        string Line((Player Player, bool Left, string Why) x) => $"{x.Player.Name} ({x.Player.PrimaryPosition}, {x.Player.CurrentAbility}): {x.Why}.";
        Post(left.Count > 0 ? $"Relegation release: {string.Join(", ", left.Select(x => x.Player.Name))} {(left.Count == 1 ? "leaves" : "leave")}" : "Relegation release: everyone stays",
            "Going down, the players with a relegation release clause in their contracts chose whether to use it."
            + (left.Count > 0 ? $"\n\nLeaving on a free (their wages are off the books):\n{string.Join("\n", left.Select(Line))}" : "")
            + (stayed.Count > 0 ? $"\n\nStaying (the clause is used up for this relegation):\n{string.Join("\n", stayed.Select(Line))}" : ""), "Roster");
    }

    /// <summary>
    /// H69 (bug hunt; run 106): where your club plays next season when it is going down, and what its contracts come to once the
    /// relegation clauses cut them at the end of the summer (to the new league's scale, never below half; Budgets.Rescale).
    /// A solvent owner funds at least those contracts, so the cut payroll is the floor of next season's budget. Null when you
    /// aren't going down.
    /// </summary>
    public (string League, long PayrollAfter, bool OwnerFunds)? RelegationOutlook()
    {
        if (!GoingDown()) return null;
        string from = InNabl ? "" : HumanLeague?.Key ?? "";
        string? below = InNabl ? UsaPyramid.Tier2 : from switch
        {
            UsaPyramid.Tier2 => UsaPyramid.Tier3, UsaPyramid.Tier3 => UsaPyramid.Tier4, UsaPyramid.Tier4 => UsaPyramid.Tier5, _ => null,
        };
        if (below == null) return null;
        double oldScale = InNabl ? 1 : Competitions.Get(from).SalaryScale, newScale = Competitions.Get(below).SalaryScale;
        double keep = oldScale > newScale ? Math.Max(0.5, newScale / oldScale) : 1;
        long after = HumanTeam.Roster.Where(p => !MyExpiring.Contains(p))
            .Sum(p => p.Contract is { } c ? (long)Math.Round(c.Salary * keep / 1000) * 1000 : 0);
        return (Competitions.Get(below).Name, after, HumanTeam.Money.Balance >= 0);
    }

    /// <summary>
    /// H33 (bug hunt, Nov 16; run 82): when your club goes down level on record with others in its division, the tiebreak
    /// that decided it, in words ("Level at 35-27 with … : point difference decided it (… −40, … +12)."). Null when it
    /// wasn't a tie, or you aren't going down.
    /// </summary>
    public string? RelegationTiebreak()
    {
        if (!GoingDown()) return null;
        if (InNabl && UsaPyramid.PromotionSeriesOf(World, HumanTeam.Id) != null) return null;   // the playoff decided it, not the tie
        List<(string Name, int W, int L, int Diff)> rows;
        if (InNabl)
            rows = Standings.All().Where(r => r.Team.Conference == HumanTeam.Conference && r.Team.Division == HumanTeam.Division)
                .Select(r => (r.Team.FullName, r.Wins, r.Losses, r.PointsFor - r.PointsAgainst)).ToList();
        else if (HumanLeague is { } c && UsaPyramid.Divisions(c.Key, c.Clubs).FirstOrDefault(d => d.Teams.Contains(HumanTeam)).Teams is { } mine)
        {
            var names = mine.ToDictionary(t => t.Id, t => t.FullName);
            rows = c.Season.Table().Where(r => names.ContainsKey(r.TeamId)).Select(r => (names[r.TeamId], r.Wins, r.Losses, r.Diff)).ToList();
        }
        else return null;
        if (rows.Count < 2) return null;
        var last = rows[^1];
        var level = rows.Where(r => r.W == last.W && r.L == last.L).ToList();
        if (level.Count < 2) return null;
        static string Signed(int d) => d > 0 ? $"+{d}" : d < 0 ? $"\u2212{-d}" : "0";
        string others = string.Join(" and ", level.Where(r => r.Name != last.Name).Select(r => r.Name));
        bool sameDiff = level.Count(r => r.Diff == last.Diff) > 1;
        return $"Level at {last.W}-{last.L} with {others}: "
             + (sameDiff ? "point difference was level too, so the league's draw order decided it" : "point difference decided it")
             + $" ({string.Join(", ", level.Select(r => $"{r.Name} {Signed(r.Diff)}"))}).";
    }

    /// <summary>A computer-run club changes coach; while you're out of work the job is open to you.</summary>
    public void CoachLeaves(Team t, string how = "parted ways with")
    {
        string old = t.CoachName;
        if (Employed && IsStepUp(t))
        {
            // The career path (#651): a step up opens to you while you're in work. The successor is drawn now, as the
            // computer's hire always was, so the session's random numbers run exactly as before.
            string successor = Jobs.Board.NewCoachName(new Rng(_rng.NextULong()));
            if (OpenWhileEmployed(t, successor)) return;
            t.CoachName = successor;
            if (League.Teams.Contains(t) || HumanLeague?.Clubs.Contains(t) == true)
                PostWorld($"{t.Abbreviation} change coach", $"{t.FullName} have {how} {old} and hired {t.CoachName}.");
            return;
        }
        if (!Employed && t != HumanTeam && !Job.Vacancies.Any(v => v.TeamId == t.Id && v.Status is "open" or "applied" or "offered"))
        {
            Job.Vacancies.Add(new Jobs.Vacancy { TeamId = t.Id, Opened = Today });
            t.CoachName = "Interim coach";
            JobOpenings.Add($"• {t.FullName} ({ContextOf(t).Name}) have {how} {old}.");
        }
        else
        {
            t.CoachName = Jobs.Board.NewCoachName(new Rng(_rng.NextULong()));
            // Other clubs' coaching changes go to the world digest (run 42, review #12 Top fix 1; a college coach's already did).
            if (League.Teams.Contains(t) || HumanLeague?.Clubs.Contains(t) == true)
                PostWorld($"{t.Abbreviation} change coach", $"{t.FullName} have {how} {old} and hired {t.CoachName}.");
        }
    }

    /// <summary>You're sacked: the club goes back to the computer and you look for work.</summary>
    public void Fire()
    {
        if (!Employed) return;
        Coach.History.Add(JobRecordFor(HumanTeam, "Sacked"));
        Coach.Reputation = Math.Max(0, Coach.Reputation - 5);
        foreach (var v in LiveVacancies) v.WhileEmployed = false; // jobs you'd opened in work stay open to you
        ReleaseClub(Jobs.Board.NewCoachName(new Rng(_rng.NextULong())));
        Post("Out of work", $"You are no longer coach of {HumanTeam.TheFullName}; {HumanTeam.CoachName} takes over. Clubs that change coach will post their openings on the Board page, where you can apply. Your reputation: {Coach.Reputation:0}/100.", "Board");
    }

    /// <summary>Apply for an open job. Returns null on success, else why not.</summary>
    public string? Apply(int teamId)
    {
        var v = Job.Vacancies.FirstOrDefault(x => x.TeamId == teamId && x.Status == "open");
        if (v == null) return "That job isn't open.";
        if (Employed && !v.WhileEmployed) return "You already have a job.";
        if (FindTeam(teamId) is { } jt && Employed && !EligibleFor(jt)) return "College coaches can be hired by NBA and G League clubs, not this league.";
        v.Status = "applied";
        // In work, the career path's own random numbers (#651); out of work, as before.
        v.DecideOn = Today.AddDays(3 + (Employed ? CareerRng(7).Next(0, 5) : _rng.Next(0, 5)));
        bool summer = Phase == SessionPhase.Offseason;
        Post($"Application sent: {FindTeam(teamId)!.FullName}", (summer ? "They'll answer at the next step of the summer." : $"They'll decide by {v.DecideOn:MMM d}.")
            + (Employed ? $" You stay with {HumanTeam.TheFullName} unless you accept an offer." : ""), "Board");
        SaveNow();
        return null;
    }

    /// <summary>Accept a job offer and take over the club.</summary>
    public string? AcceptOffer(int teamId)
    {
        var v = Job.Vacancies.FirstOrDefault(x => x.TeamId == teamId && x.Status == "offered");
        if (v == null) return "There's no offer from that club.";
        var team = FindTeam(teamId)!;
        bool moving = Employed;
        foreach (var other in Job.Vacancies.Where(x => x.Status is "open" or "applied" or "offered" && x != v))
        {
            other.Status = "filled";
            if (other.NextCoach is { Length: > 0 } n && FindTeam(other.TeamId) is { } ot && ot != team) ot.CoachName = n;
        }
        v.Status = "filled";
        if (moving) MoveTo(team);
        TakeJob(team);
        SaveNow();
        return null;
    }

    public void DeclineOffer(int teamId)
    {
        var v = Job.Vacancies.FirstOrDefault(x => x.TeamId == teamId && x.Status == "offered");
        if (v != null && FindTeam(teamId) is { } t)
        {
            v.Status = "filled";
            // An offer made while you're in work names its successor from the start (#651); out of work, as before.
            t.CoachName = v.NextCoach is { Length: > 0 } n ? n : Jobs.Board.NewCoachName(new Rng(_rng.NextULong()));
        }
        SaveNow();
    }

    /// <summary>
    /// Your spell at a club for the job history. H80 (run 105): a summer hire who leaves before his first season coached no
    /// games there; the club's record of the season before isn't his.
    /// </summary>
    private Jobs.JobRecord JobRecordFor(Team club, string ended)
    {
        if (Job.HiredSeason > League.Season) return new Jobs.JobRecord(club.Id, club.FullName, Job.HiredSeason, Job.HiredSeason, 0, 0, ended);
        var rec = MyRecord;
        return new Jobs.JobRecord(club.Id, club.FullName, Job.HiredSeason, League.Season, rec.Wins, rec.Losses, ended);
    }

    private void TakeJob(Team team)
    {
        HumanTeam = team;
        team.Coach = Coach;
        team.CoachName = Coach.Name;
        Job.Employed = true;
        Job.Confidence = 70;
        Job.WarningLevel = 0;
        // H80 (run 105): a coach hired after the club's last game is hired for the coming season, not the one just finished
        // ("Expectations for 2027-28" on May 4 2028). His first season, for the board's patience, is that one too.
        int forSeason = ClubSeasonOver(team) ? League.Season + 1 : League.Season;
        Job.HiredOn = Today;
        Job.HiredSeason = forSeason;
        Job.History.Clear();
        Job.Current = Jobs.Board.NextExpectations(ContextOf(team), team, forSeason, null, MyLeagueKey, 0, 0);
        if (Abroad) EnsureStaff(team);
        if (InCollege) Delegated.Recruiting = true; // your assistant recruits until you take it over, as in a new college career
        SyncManagedIds();
        CoachAi.AssistantSetup(team);
        team.DepthChart = null;
        Promises.Clear();
        MoodHistory.Clear();
        // H56 (run 87): the contracts you inherit were signed before you came, so an owner who isn't in debt funds them.
        long funded = Budgets.OwnerFunds(team, MyCommittedWages);
        Post($"Welcome to {team.TheFullName}", $"{team.Owner.Name} has hired you as head coach ({HumanLeagueName}). Check your rotation and tactics before the next game."
            + (funded > 0 ? $" The contracts you inherit come to ${MyCommittedWages / 1e6:F1}M, so the owner has raised the wage budget by ${funded / 1e6:F1}M to cover them." : ""), "Board");
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
                    Post($"Job offer: {t.FullName}", $"{t.Owner.Name} wants you as head coach of {t.TheFullName}. Accept or decline on the Board page within a week.", "Board");
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
                People.PeopleEventKind.TradeRequest when InCollege => $"{e.Player.Name} is thinking of transferring",
                People.PeopleEventKind.TradeRequest => $"{e.Player.Name} requests a trade",
                People.PeopleEventKind.WantsTalk => $"{e.Player.Name} wants a word",
                People.PeopleEventKind.PromiseKept => $"Promise kept: {e.Player.Name}",
                People.PeopleEventKind.PromiseBroken => $"Promise broken: {e.Player.Name}",
                People.PeopleEventKind.Settled => $"{e.Player.Name} settles",
                _ => e.Player.Name,
            };
            Post(title, InCollege ? CollegeWording(e) : e.Text, "Squad");
        }
        WeeklyMedia();
    }

    /// <summary>
    /// H76 (bug hunt Nov 17, run 97): a college player who wants out is thinking of transferring. Programs don't trade or
    /// sell, so the message offers only what a college coach can do: talk to him.
    /// </summary>
    public static string CollegeWording(People.PeopleEvent e) => e.Kind switch
    {
        People.PeopleEventKind.TradeRequest =>
            $"{e.Player.Name} is unhappy and thinking of transferring. Talk to him on the Squad page (convince him to stay, or ask what's wrong). "
            + $"If nothing changes he'll decide within about {People.PeopleEngine.RequestWeeks} weeks whether to knuckle down.",
        People.PeopleEventKind.Settled when e.Text.Contains("trade request") =>
            $"{e.Player.Name} has put his transfer thoughts aside and will see out the season. He hasn't forgotten; expect the question again in the summer.",
        _ => e.Text,
    };

    /// <summary>Hold a one-on-one conversation with one of your players. Returns his reply (or why you can't talk now).</summary>
    public People.TalkResult Talk(Player p, People.TalkTopic topic)
    {
        if (!Employed || p.TeamId != HumanTeam.Id) return new("He isn't one of your players.", 0, false);
        if (People.Talks.Unavailable(p, Today) is { } why) return new(why, 0, false);
        MyStats.TryGetValue(p.Id, out var st);
        double wp = MyRecord.Games > 0 ? MyRecord.WinPct : 0.5;
        var r = People.Talks.Hold(p, HumanTeam, topic, Today, wp, st, League.Finance, Promises, _rng, Coach);
        if (InCollege) r = r with { Reply = r.Reply.Replace("withdraws his trade request", "puts his transfer thoughts aside") };   // H76
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
        MarkFacilityDecider();
        Offseason = new Offseason(League, outcome, Stats, _rng) { HumanTeamId = ControlledTeamId, PendingCommitments = PendingCommitments };
        Offseason.Begin();
        _summerNotes = new();
        if (Employed && Offseason.Report.StaffLeft.Count > 0)
            SummerNote($"{Count(Offseason.Report.StaffLeft.Count, "staff contract")} ran out",
                string.Join("\n", Offseason.Report.StaffLeft.Select(st => $"{st.Name} ({Staff.RoleName(st.Role).ToLower()}) has left: his contract ended without an extension."))
                + "\n\nHire replacements on the Staff & Training page; interview candidates first.", "Staff");
        CoachGrowth();
        // The world's summer starts too: overseas contracts end and some players enter the shared free-agent pool.
        var recruiting = World.College?.Season.Recruiting;
        World.BeginSummer(Offseason.Season + 1, _sim, League.FreeAgents);
        Hardwood.Core.World.WorldMarket.Attach(World, Offseason, _sim);
        // H108 (run 112): "overseas" means clubs abroad; players leaving G League or pyramid clubs aren't listed here.
        var abroad = World.LeftClub.Where(x => x.player.CurrentAbility >= Hardwood.Core.World.GameWorld.NablCalibre
                && World.LeagueOf(x.from) is { } lc && !Hardwood.Core.World.UsaPyramid.IsPyramid(lc.Key)).OrderByDescending(x => x.player.CurrentAbility).Take(8).ToList();
        if (abroad.Count > 0 && !InCollege)
            SummerNote("Overseas free agents", "These players' overseas contracts have ended and they're free to sign anywhere:\n"
                + string.Join("\n", abroad.Select(x => $"{x.player.Name} ({x.player.PrimaryPosition}, {x.player.Age(Offseason.Date)}, {x.player.CurrentAbility} CA), last with {x.from.FullName}")), "World");
        Offseason.AiReSignAll(exceptTeamId: ControlledTeamId);
        // Seniors who committed during the season sign before free agency opens (decision #157).
        SignCommittedGraduates(recruiting);
        // Danny's draft (D40, real careers): the lottery runs now, and the picks are made before free agency opens.
        if (CollegeDraft.SetUp(Offseason, League, World, _rng)) AnnounceDraftLottery();

        AnnounceAwards(Offseason.Summary!);
        FacilitiesSummerNews();
        // Run 42 (review #12, Top fix 1): in the NBA the summer's notes ride in "Contract decisions"; elsewhere they post as before.
        bool fold = Employed && collegeBefore == null && !Abroad;
        if (!fold) FlushSummerNotes();
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
                + $"\n\nFree agents can be signed all summer within your budget ({MyBudgetLine}). Anyone you haven't renewed leaves when the summer ends, and if the squad is short of {MyRosterMin}, your sporting director makes up the numbers.", "Roster");
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
        var dev = Offseason.Report.Development.Where(d => d.Player.TeamId == HumanTeam.Id && Math.Abs(d.NewCa - d.OldCa) >= 6).OrderByDescending(d => d.NewCa - d.OldCa).ToList();
        if (dev.Count > 0) SummerNote("Player development", string.Join("\n", dev.Select(d => $"{d.Player.Name}: {(d.NewCa > d.OldCa ? "+" : "")}{d.NewCa - d.OldCa} ability")), "Roster");
        var notes = _summerNotes ?? new();
        _summerNotes = null;
        Post("Contract decisions",
            (expiring.Count == 0 ? "No contracts expire this summer." : $"{expiring.Count} contracts have expired: {string.Join(", ", expiring.Select(p => $"{p.Name} (asks ${DemandOf(p).Wage / 1e6:F1}M)"))}. Talk to each one or release him, then continue to free agency.")
            + $"\n\nThe owner has set next season's budget: {MyBudgetLine}."
            + string.Concat(notes.Select(n => $"\n\n{n.title}:\n{n.body}")), "Roster");
    }

    /// <summary>The summer's smaller notes (staff whose deals ran out, your growth as a coach, overseas free agents, player
    /// development), gathered while the offseason opens so an NBA summer brings one message, not five (run 42).</summary>
    private List<(string title, string body, string category)>? _summerNotes;

    private void SummerNote(string title, string body, string category)
    {
        if (_summerNotes != null) _summerNotes.Add((title, body, category));
        else Post(title, body, category);
    }

    private void FlushSummerNotes()
    {
        var notes = _summerNotes;
        _summerNotes = null;
        if (notes != null) foreach (var (t, b, c) in notes) Post(t, b, c);
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
        if (a.CoachOfYearTeamId is { } coy) parts.Add($"Coach of the Year: {(a.CoachOfYearName.Length > 0 ? a.CoachOfYearName + ", " : "")}{League.TeamById(coy).FullName}. {a.CoachOfYearNote}" + (coy == HumanTeam.Id ? " That's you." : ""));
        parts.Add(string.Join("\n", a.AllLeague.Select((t, i) => $"All-League {(i == 0 ? "first" : i == 1 ? "second" : "third")} team: {string.Join(", ", t.Select(id => Name(id)))}")));
        if (summary.FinalsMvpPlayerId is { } fm) parts.Add($"Finals MVP: {Name(fm)} ({League.TeamById(summary.ChampionTeamId).FullName}).");
        var ours = a.AllLeague.SelectMany(t => t).Where(id => all.TryGetValue(id, out var p) && p.TeamId == HumanTeam.Id).Select(id => Name(id)).ToList();
        if (ours.Count > 0) parts.Insert(0, $"Your All-League honourees: {string.Join(", ", ours)}.");
        Post($"Season awards: {Name(a.Mvp)} is MVP", string.Join("\n\n", parts), "Awards");
        var honours = Offseason!.Report.HallOfFame.Select(h => HallOfFameLine(h, League.Careers[h.Id]))
            .Concat(Offseason.Report.RetiredNumbers.Select(x => $"{x.Item2.FullName} retire {x.Item1.Name}'s number.")).ToList();
        if (honours.Count > 0) Post(honours.Count == 1 ? honours[0].Split(',')[0].Split(" retire ")[0] : $"Hall of Fame and retired numbers ({honours.Count})", string.Join("\n", honours), "Awards");
    }

    /// <summary>
    /// The inbox line for a Hall of Fame induction. H36 (bug hunt, Nov 16; run 79): a real veteran's career is seeded with
    /// his real points and MVP / Finals MVP awards only (<see cref="RealWorld.RealCareers"/>), so his titles and All-Star
    /// selections count only the seasons played here; the line says so instead of "0 titles, 0 All-Star selections".
    /// </summary>
    public static string HallOfFameLine(Player h, CareerTotals c)
    {
        static string N(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
        var line = $"Hall of Fame: {h.Name}, inducted after a {c.Seasons}-season career: {c.Points:N0} points, {N(c.Mvps, "MVP", "MVPs")}";
        if (!RealWorld.RealCareers.Players.ContainsKey(h.Name))
            return line + $", {N(c.Titles, "title", "titles")}, {N(c.AllStars, "All-Star selection", "All-Star selections")}.";
        if (c.FinalsMvps > 0) line += $", {N(c.FinalsMvps, "Finals MVP", "Finals MVPs")}";
        line += ".";
        if (c.Titles > 0 || c.AllStars > 0)
            line += $" In the seasons played here: {N(c.Titles, "title", "titles")}, {N(c.AllStars, "All-Star selection", "All-Star selections")}.";
        return line;
    }

    private bool HumanTeamHad(Player p) => Stats.TryGetValue(p.Id, out var s) && s.TeamId == HumanTeam.Id;

    /// <summary>Move the offseason forward one phase: re-signing → draft (AI picks until you're on the clock) → free agency → new season.</summary>
    public void AdvanceOffseason()
    {
        if (Offseason == null) return;
        Offseason.HumanTeamId ??= ControlledTeamId; // a restored summer (a save made mid-draft) didn't carry it
        // Clubs answer your summer applications at each step (#651), in work or out of it (H75, run 97).
        if (Employed) CareerMarketStep(); else SummerJobMarket();
        switch (Offseason.Phase)
        {
            case OffseasonPhase.ReSigning:
                Offseason.AdvanceToDraft();
                if (Offseason.CollegeDraftOn)
                {
                    // D40: the computer clubs pick until you're on the clock (or the draft is over).
                    Offseason.AutoDraftUntil(ControlledTeamId);
                    // Review #12, Top fix 1 (run 42): no separate "on the clock" message: the lottery message says how to pick,
                    // and the Offseason page's Draft tab and the assistant show the pick waiting.
                    if (Offseason.DraftComplete) AdvanceOffseason();
                    break;
                }
                if (League.Finance.OpenMarket)
                {
                    OpenFreeAgencyOnTheOpenMarket();
                    break;
                }
                Offseason.AutoDraftUntil(ControlledTeamId);
                if (Offseason.DraftComplete) AdvanceOffseason();
                else Post("You're on the clock", $"Pick {Offseason.NextPickIndex + 1} (round {Offseason.CurrentRound}). {Offseason.Prospects.Count} prospects remain.", "Draft");
                break;
            case OffseasonPhase.Draft when Offseason.CollegeDraftOn:
                Offseason.AdvanceToFreeAgency(); // your scouts make any pick you haven't
                AnnounceDraft();
                OpenFreeAgencyOnTheOpenMarket();
                break;
            case OffseasonPhase.Draft:
                Offseason.AdvanceToFreeAgency();
                UseReleaseClauses();   // K1 (run 111): before anyone signs, so released stars are in the top league's pool
                Hardwood.Core.World.WorldMarket.AiMoves(World, Offseason, ControlledTeamId);
                Offseason.AiFreeAgency(ControlledTeamId);
                foreach (var (title, body) in World.News) PostWorld(title, body); MaybePostDigest(); // run 42: the summer's headlines wait (one digest a summer, at the new season)
                World.News.Clear();
                var ours = Offseason.Report.Draft.Where(d => d.Team == HumanTeam).ToList();
                if (InNabl && !League.Finance.OpenMarket) Post("Draft complete", string.Join("\n", ours.Select(d => $"Round {d.Round}, pick {d.Pick}: {d.Player.Name} ({d.Player.PrimaryPosition}, age {d.Player.Age(Offseason.Date)}, {d.Player.CurrentAbility}/{d.Player.PotentialAbility})")) + $"\n\nFree agency is open. You have {HumanTeam.Roster.Count} players; {League.Finance.Describe(ContractMarket.Payroll(HumanTeam))}. Rosters need {League.Finance.RosterMin} to {League.Finance.RosterMax}.", "Draft");
                break;
            case OffseasonPhase.FreeAgency:
                int levelBefore = MyLevel; string leagueBefore = HumanLeagueName;
                Offseason.Complete();
                CloseSummerOpenings();
                StartNewSeason();
                if (!Employed) CarrySummerSearch();
                // Promotion and relegation happen at the end of the summer (decision #79).
                if (Employed && HumanLeagueName != leagueBefore)
                {
                    bool up = MyLevel > 0 && MyLevel < levelBefore;
                    // A relegated club's first season down is a rebuild: no mid-season sacking (critic review #5).
                    if (!up) { Job.ProtectedSeason = League.Season; Job.ProtectedReason = "rebuild"; }
                    Post(up ? $"Promoted to {HumanLeagueName}!" : $"Relegated to {HumanLeagueName}",
                        up ? $"{HumanTeam.TheFullNameCap} go up from {leagueBefore} to {HumanLeagueName}. The owner has set new budgets and expectations for life at the higher level."
                           : $"{HumanTeam.TheFullNameCap} drop from {leagueBefore} to {HumanLeagueName}. Wages already agreed don't fall with you; the owner expects you to bounce back.", "Board");
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

    /// <summary>Free agency opens on the open market: the world and the computer clubs move, then your offers.</summary>
    private void OpenFreeAgencyOnTheOpenMarket()
    {
        NewWindow();
        Hardwood.Core.World.WorldMarket.AiMoves(World, Offseason!, ControlledTeamId);
        Offseason.AiFreeAgency(ControlledTeamId);
        AiTransferRound(15); // the summer transfer market (decision #87)
        if (Employed && !InCollege && _rng.Chance(0.6)) MaybeBidForYourPlayer();
        foreach (var (title, body) in World.News) PostWorld(title, body); MaybePostDigest(); // run 42: the summer's headlines wait (one digest a summer, at the new season)
        World.News.Clear();
        if (Employed && !InCollege) // college programs don't sign pros (C2)
        {
            var grads = League.FreeAgents.Where(p => p.RookieSeason == League.Season + 1).OrderByDescending(p => p.CurrentAbility).Take(6).ToList();
            Post("Free agency is open", $"Make offers to free agents on the Offseason page. {MyBudgetLine}."
                + (grads.Count > 0 ? $"\n\nThe best of this year's college graduates still available: {string.Join(", ", grads.Select(p => $"{p.Name} ({p.PrimaryPosition}, {p.CurrentAbility})"))}." : ""), "Roster");
        }
    }

    /// <summary>H59 (run 109): the same for a club below the NBA or abroad, which never drafts unless it is promoted into it.</summary>
    public const string NoPickReasonOutsideNba = "You have no pick this year: only NBA clubs draft (the top three in each division, the clubs promoted from the second tier and the third-place tournament's winner).";

    /// <summary>Why a club has no draft pick (the lottery's rule; H59 shows it on the Draft tab too).</summary>
    public const string NoPickReason = "You have no pick this year: picks go to the top three in each NBA division, the clubs promoted from the second tier, and the third-place tournament's winner.";

    /// <summary>D40: the lottery's order, posted when the summer opens.</summary>
    private void AnnounceDraftLottery()
    {
        var o = Offseason!;
        string Line(int i) => $"{i + 1}. {o.PickOwners[i].FullName}" + (i == o.PickOwners.Count - 1 && o.Outcome.ThirdPlaceWinner == o.PickOwners[i] ? " (the third-place tournament's extra pick)" : "");
        var ours = Enumerable.Range(0, o.PickOwners.Count).Where(i => o.PickOwners[i] == HumanTeam).Select(i => i + 1).ToList();
        string mine = !Employed || InCollege ? "" : ours.Count == 0 ? (InNabl ? NoPickReason : NoPickReasonOutsideNba) + "\n\n"
            : $"You pick {string.Join(" and ", ours.Select(Ordinal))}. When the draft reaches you, choose on the Offseason page (Draft), or press Continue and your scouts pick for you.\n\n";
        Post("The draft lottery", mine + $"The best {o.Prospects.Count} college graduates go in a one-round draft before free agency; everyone else from the class is a free agent. Every eligible club had the same odds.\n\n" + string.Join("\n", Enumerable.Range(0, o.PickOwners.Count).Select(Line)), "Draft");
    }

    /// <summary>D40: who went where.</summary>
    private void AnnounceDraft()
    {
        var picks = Offseason!.Report.Draft;
        if (picks.Count == 0) return;
        string Row(DraftPick d) => $"{d.Pick}. {d.Team.Abbreviation}: {d.Player.Name} ({d.Player.PrimaryPosition}, {d.Player.CurrentAbility})";
        var first = picks[0];
        PostWorld($"Draft: {first.Player.Name} goes first to {first.Team.TheFullName}", string.Join("\n", picks.Select(Row)) + "\n\nEveryone else from this year's class is a free agent.");
        var ours = picks.Where(d => d.Team == HumanTeam).ToList();
        if (Employed && !InCollege && ours.Count > 0)
            Post("Your draft", string.Join("\n", ours.Select(d => $"Pick {d.Pick}: {d.Player.Name} ({d.Player.PrimaryPosition}, age {d.Player.Age(Offseason.Date)}), ${d.Player.Contract!.Salary / 1e6:F2}M a year for {d.Player.Contract.Years} years (your option on the last)."))
                + (League.Finance.OpenMarket ? "\n\nA pick is compulsory, so the owner has added each rookie's wage to your wage budget." : ""), "Draft");
    }

    /// <summary>Human draft pick; then AI picks run until the human is on the clock again or the draft ends.</summary>
    public DraftPick MakePick(Player prospect)
    {
        Offseason!.HumanTeamId ??= ControlledTeamId; // a restored summer (a save made mid-draft) didn't carry it
        var pick = Offseason.MakePick(prospect);
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
        SyncHeadToHead(); // last season's meetings are counted before its schedule goes (#529)
        NewWindow();
        AgreedFees.Clear(); IncomingBids.Clear(); BidsWindowShut(); // the summer window shuts
        Phase = SessionPhase.Preseason;
        World.EndSummer(League.Season, _sim, League.FreeAgents); // normally already run at the end of free agency
        foreach (var (title, body) in World.News) PostWorld(title, body); MaybePostDigest(now: true);
        World.News.Clear();
        if (Employed)
        {
            if (Abroad && !InCollege) SummerReport();
            // H80 (run 105): a coach hired this summer already has this season's goal (posted when he took the job); it stands
            // unless the club has since moved league.
            bool setThisSummer = Job.Current is { } cur && cur.Season == League.Season && cur.TeamId == HumanTeam.Id && cur.League == MyLeagueKey;
            if (!setThisSummer)
            {
                Job.Current = Jobs.Board.NextExpectations(ContextOf(HumanTeam), HumanTeam, League.Season, Job.Current,
                    MyLeagueKey, MyLevel, Job.Current?.League is { Length: > 0 } was ? LevelOf(was) : 0);
                PostExpectations();
            }
            Job.WarningLevel = 0;
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
            Post("Parachute payment", ParachuteNote(m) + (FirstParachuteYear(m) ? " The relegation wage cut has halved every contract." : "")
                + " Use the time to bring the payroll down to what this league pays.", "Board");
    }

    /// <summary>The first season of a parachute (an older save's halving one, with no base, by its seasons left).</summary>
    private static bool FirstParachuteYear(ClubFinance m) =>
        m.ParachuteCarried == 0 && (m.ParachuteBase > 0 ? m.ParachuteYear <= 1 : m.ParachuteYears > 1);

    /// <summary>K1 (run 111): this season's parachute in words, with the schedule ahead.</summary>
    public static string ParachuteNote(ClubFinance m)
    {
        if (m.Parachute <= 0) return "";
        string ahead;
        if (m.ParachuteBase <= 0) ahead = m.ParachuteYears > 1 ? "it halves next season, then stops" : "the last one";
        else
        {
            var next = Enumerable.Range(m.ParachuteYear, Math.Max(0, m.ParachuteYears - 1))
                .Select(y => Budgets.Money((long)Math.Round(m.ParachuteBase * Budgets.ParachuteShares[Math.Min(y, Budgets.ParachuteShares.Length - 1)] / 100_000) * 100_000)).ToList();
            ahead = next.Count == 0 ? "the last one" : $"then {string.Join(", then ", next)}, then it stops";
        }
        return $"After relegation the league pays a parachute of {Budgets.Money(m.Parachute)} this season ({ahead}), a share of the media money of the league the club left."
            + (m.ParachuteCarried > 0 ? " It is still the parachute from the first relegation: a second drop doesn't add one." : "")
            + " It stops if the club goes straight back up.";
    }

    // ------------------------------------------------------------------------------------------
    /// <summary>How often the day loop saves on its own (D74 B, #635): every 7 game days, plus every phase change.</summary>
    public const int SaveEveryDays = 7;

    /// <summary>The game date of the last save (default when this session has never saved).</summary>
    public DateOnly LastSaved { get; private set; }
    /// <summary>The wall-clock time of the last save, for Settings' note.</summary>
    public DateTime LastSavedAt { get; private set; }
    private SessionPhase _lastSavedPhase;
    /// <summary>Raised after every save (the top bar's "Saved Nov 3" line listens).</summary>
    public event Action? Saved;

    /// <summary>The end of a day (D74 B, #635): save when a week has passed since the last save or the phase has
    /// changed (preseason, regular season, playoffs, the summer); otherwise only keep the head-to-head book current.
    /// Your own decisions (trades, signings, staff, talks, tactics) still save at once through <see cref="SaveNow"/>.</summary>
    private void SaveIfDue()
    {
        bool due = Save != null && (LastSaved == default || Phase != _lastSavedPhase || Today.DayNumber - LastSaved.DayNumber >= SaveEveryDays);
        if (due) SaveNow();
        else SyncHeadToHead();
    }

    /// <summary>Saves the whole career now: after every offseason step and decision of yours, at phase changes, every
    /// 7 days, on Settings' Save now, and when you go to the main menu or close the window (D74 B).</summary>
    public void SaveNow()
    {
        SyncHeadToHead(); // the day's results into the head-to-head book (#529)
        if (Save == null) return;
        HealDuplicatePlayers();
        LastSaved = Today; LastSavedAt = DateTime.Now; _lastSavedPhase = Phase;
        // A college program you coach keeps its staff in the save like a club abroad (C2).
        var clubs = World.Clubs.Concat(World.College?.Programs.Where(t => t.Staff.Count > 0) ?? Enumerable.Empty<Team>());
        Save.Save(League, Stats.Values, Phase == SessionPhase.Offseason ? Offseason?.Prospects : null, World.Players, clubs, World.College?.Players);
        Save.SaveSession(this);
        Save.SaveWorld(World);
        Save.SavePeople(this);
        Save.SaveCareer(this);
        Saved?.Invoke();
    }

    /// <summary>
    /// Bug hunt H28 (run 75): a player in the free-agent pool and on a roster at once made every later save fail
    /// ("UNIQUE constraint failed: player.id"), losing everything played after it. Before each save, anyone on a roster
    /// (here, abroad or in college) leaves the pool, and the pool lists each player once. No random numbers.
    /// Returns how many entries were dropped.
    /// </summary>
    public int HealDuplicatePlayers()
    {
        var placed = new HashSet<int>(League.Teams.SelectMany(t => t.Roster).Select(p => p.Id));
        foreach (var p in World.Players) placed.Add(p.Id);
        if (World.College?.Programs is { } programs) foreach (var p in programs.SelectMany(t => t.Roster)) placed.Add(p.Id);
        var seen = new HashSet<int>();
        int before = League.FreeAgents.Count;
        League.FreeAgents.RemoveAll(p => placed.Contains(p.Id) || !seen.Add(p.Id));
        return before - League.FreeAgents.Count;
    }

    public static GameSession Load(string path)
    {
        var save = new SaveFile(path);
        // The made-up world was removed in v0.74.0 (D61 B, #478): an NABL career says so instead of loading (and isn't upgraded).
        if (save.PeekLeagueName() is { } top && top != Hardwood.Core.RealWorld.RealWorldData.LeagueName)
            throw new InvalidOperationException(Hardwood.Core.RealWorld.RealWorldData.MadeUpSaveMessage);
        var league = save.Load();
        // The world's league names (D44) and the lower tiers' division labels follow the career's world as it loads.
        Names.Brand.Real = Hardwood.Core.RealWorld.RealWorldData.IsReal(league);
        var world = save.LoadWorld(league, save.LoadedOverseas, save.LoadedCollege);
        var session = save.LoadSession(league, world);
        save.LoadPeople(session);
        save.LoadCareer(session);
        session.CatchUpWorld();
        session.FixStaffPay();
        session.LastSavedAt = File.Exists(path) ? File.GetLastWriteTime(path) : DateTime.Now;
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
        LastSaved = today; _lastSavedPhase = phase; // a loaded career was last saved on the day it loads to (D74 B)
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

    /// <summary>The finished playoffs as saved, for a load in the summer (the bracket itself isn't rebuilt then).</summary>
    internal void RestorePastPlayoffs(List<Series> series) => _pastPlayoffs = series;

    internal void RestoreOffseason(OffseasonPhase phase, List<int> draftOrder, List<int> pickOwners, List<Player> prospects, int nextPick,
        Dictionary<int, List<Player>> expiring, List<DraftPick> picksMade)
    {
        Phase = SessionPhase.Offseason;
        // The college draft (D40) gives tier-2 clubs picks too: look teams up in the world as well.
        Offseason = Offseason.Restore(League, _rng, phase, draftOrder, pickOwners, prospects, nextPick, expiring, picksMade, FindTeam);
        Hardwood.Core.World.WorldMarket.Attach(World, Offseason, _sim);
    }

    public string DescribeSide(IEnumerable<Player> players, IEnumerable<DraftPickAsset> picks)
    {
        var parts = players.Select(p => p.Name).Concat(picks.Select(p => p.Describe(id => League.TeamById(id).Abbreviation))).ToList();
        return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
    }

    private static string Ordinal(int n) => n % 100 is 11 or 12 or 13 ? $"{n}th" : (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
}
