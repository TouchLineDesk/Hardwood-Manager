using Hardwood.Core.Engine;
using Hardwood.Core.Jobs;
using Hardwood.Core.Model;
using Hardwood.Core.World;

namespace Hardwood.Core.Season;

/// <summary>One of your club's games in any competition (NABL, an overseas league or a continental one).</summary>
public sealed record MyGame(DateOnly Date, Team Opponent, bool Home, string Competition, string Stage, int? Us, int? Them, GameResult? Result)
{
    public bool Played => Us != null;
    public bool Won => Us > Them;
    /// <summary>College (#528): the opponent's Top 25 rank at tip-off for a played game, its rank now for one to come.</summary>
    public int? OpponentRank { get; init; }
    /// <summary>Your own rank the same way.</summary>
    public int? OurRank { get; init; }
    /// <summary>College (C5 part 1): the rivalry this game belongs to, if any.</summary>
    public College.Rivalry? Rivalry { get; init; }
    /// <summary>College (C5 part 2): an event on a neutral court (no home edge).</summary>
    public bool Neutral { get; init; }
    /// <summary>
    /// Run 136 (critic review #18): how a card names the opponent: "vs Gonzaga" at home, "at Gonzaga" away, and "vs Gonzaga
    /// (neutral)" at an invitational, a showcase or a tournament. Neutral games used to read "at" or "vs" by which side the
    /// bracket listed first.
    /// </summary>
    public string Versus(string opponent) => Neutral ? $"vs {opponent} (neutral)" : $"{(Home ? "vs" : "at")} {opponent}";
    /// <summary>College (C5, run 96): the conference whose style this game is played in (conference play and its tournament).</summary>
    public string? StyleConference { get; init; }
    /// <summary>Its Schedule &amp; Results filter (#529): the season's part, the tournament's name, or the playoffs (<see cref="ScheduleFilter"/>).</summary>
    public string Group { get; init; } = ScheduleFilter.Regular;
    /// <summary>H8 (run 65): a played game you didn't coach: from before you took this job (a move in the summer or
    /// mid-season, the hiring day included) or while you were out of work. The world keeps every game's score, but box
    /// scores are kept only for the games you coached, so this one has none.</summary>
    public bool NotCoached { get; init; }
}

/// <summary>
/// Stage 3, manage any club: the parts of the session that work the same whether your club is in the NABL or in a
/// league abroad: finding clubs, your league's table, your games, your record and your players' stats.
/// The NABL still runs on its own schedule; a club abroad plays its league and continental fixtures from the world.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>Any club in the game: the NABL first, then the world.</summary>
    public Team? FindTeam(int id) => League.Teams.FirstOrDefault(t => t.Id == id) ?? World.TeamById(id);

    /// <summary>Your club plays in the NABL.</summary>
    public bool InNabl => League.Teams.Contains(HumanTeam);
    /// <summary>Your club plays in a league abroad.</summary>
    public bool Abroad => !InNabl;
    /// <summary>The overseas league your club plays in (null in the NABL).</summary>
    public WorldCompetition? HumanLeague => InNabl ? null : World.LeagueOf(HumanTeam);
    public string HumanLeagueName => InCollege ? MyConference!.Name : HumanLeague?.Profile.Name ?? League.Name;

    /// <summary>You coach a college program (slice C2, #168). <see cref="Abroad"/> is true too: the NABL plays on without you.</summary>
    public bool InCollege => World.College?.Programs.Contains(HumanTeam) == true;
    /// <summary>Your program's conference (null outside college).</summary>
    public College.ConferenceInfo? MyConference =>
        InCollege && World.College!.ConferenceOf.TryGetValue(HumanTeam.Id, out var key) ? College.CollegeWorld.Conferences.First(c => c.Key == key) : null;
    /// <summary>The key the board's expectations are filed under: "nabl", an overseas league's key, or "college".</summary>
    public string MyLeagueKey => InCollege ? "college" : HumanLeague?.Key ?? "nabl";
    /// <summary>Who judges you: the owner, or a college program's athletic director.</summary>
    public string BossTitle => InCollege ? "athletic director" : "owner";

    /// <summary>Your club's level in the USA pyramid: 1 = NABL, 2-5 the tiers below; 0 for leagues abroad.</summary>
    public int MyLevel => InNabl ? 1 : HumanLeague is { } c ? UsaPyramid.Level(c.Key) : 0;
    /// <summary>The pyramid level of a league by key (1 = NABL, 0 = outside the USA).</summary>
    public static int LevelOf(string key) => key == "nabl" ? 1 : UsaPyramid.IsPyramid(key) ? UsaPyramid.Level(key) : 0;

    /// <summary>The league a club plays in, as the board sees it.</summary>
    public LeagueContext ContextOf(Team t) =>
        League.Teams.Contains(t) ? LeagueContext.Nabl(League) : World.LeagueOf(t) is { } c ? LeagueContext.Of(c)
        : World.College is { } cw && cw.Programs.Contains(t) ? LeagueContext.College(cw, t) : LeagueContext.Nabl(League);

    /// <summary>A college conference's table from its conference games (ties: the better rating).</summary>
    public Standings CollegeTable(string conferenceKey)
    {
        var cw = World.College!;
        var st = new Standings(cw.InConference(conferenceKey));
        foreach (var g in cw.Season.Games.Where(g => g.Played && g.Stage == "Conference"))
            if (cw.Program(g.HomeId) is { } h && cw.Program(g.AwayId) is { } a && st.Has(h) && st.Has(a))
                st.Record(h, a, g.HomePts!.Value, g.AwayPts!.Value);
        return st;
    }

    /// <summary>A college program's overall record this season: every game it has played.</summary>
    public TeamRecord CollegeRecord(Team program)
    {
        var cw = World.College!;
        var st = new Standings(new[] { program });
        foreach (var g in cw.GamesOf(program.Id).Where(g => g.Played))
        {
            bool home = g.HomeId == program.Id;
            var opp = cw.Program(home ? g.AwayId : g.HomeId);
            if (opp == null) continue;
            var r = st[program];
            int us = home ? g.HomePts!.Value : g.AwayPts!.Value, them = home ? g.AwayPts!.Value : g.HomePts!.Value;
            r.PointsFor += us; r.PointsAgainst += them;
            if (us > them) { r.Wins++; if (home) r.HomeWins++; } else { r.Losses++; if (home) r.HomeLosses++; }
        }
        return st[program];
    }

    /// <summary>Box scores of your college games this session (the college season keeps scores and stat lines).</summary>
    public Dictionary<string, GameResult> CollegeResults { get; } = new();
    private static string Key(College.CollegeGame g) => $"college:{g.Date:yyyyMMdd}:{g.HomeId}:{g.AwayId}";

    /// <summary>Box scores of your games abroad this session (the world keeps scores and stat lines; these add the detail).</summary>
    public Dictionary<string, GameResult> AbroadResults { get; } = new();
    private static string Key(WorldCompetition c, Fixture f) => AbroadKey(c.Key, f);
    /// <summary>The key of a game below the NBA or abroad. Run 56 added the date: fixture indices start again every
    /// season, so "usa5:12" in a third season used to find the first season's game 12.</summary>
    public static string AbroadKey(string competition, Fixture f) => $"{competition}:{f.Date:yyyyMMdd}:{f.Index}";

    /// <summary>Keep the newest <paramref name="max"/> box scores, judged by the date in their key (both kinds carry it
    /// as their second part). Run 56: removing <c>Keys.First()</c> dropped the game just added once the dictionary had
    /// been full, because a dictionary reuses the freed slot (and lists it first), so every later box was lost.</summary>
    public static void KeepNewest(Dictionary<string, GameResult> boxes, int max = 200)
    {
        if (boxes.Count <= max) return;
        var keep = boxes.OrderByDescending(kv => KeyDate(kv.Key)).ThenByDescending(kv => kv.Key, StringComparer.Ordinal).Take(max).ToList();
        boxes.Clear();
        foreach (var (k, v) in keep) boxes[k] = v;
    }

    internal static int KeyDate(string key)
    {
        var parts = key.Split(':');
        return parts.Length >= 3 && parts[1].Length == 8 && int.TryParse(parts[1], out var d) ? d : 0;
    }

    /// <summary>An overseas league's regular-season table built from its results.</summary>
    public Standings TableOf(WorldCompetition comp)
    {
        var st = new Standings(comp.Clubs);
        foreach (var f in comp.Season.Fixtures)
            if (f.Played && f.Stage == CompetitionSeason.RegularSeason && World.TeamById(f.HomeId) is { } h && World.TeamById(f.AwayId) is { } a && st.Has(h) && st.Has(a))
                st.Record(h, a, f.HomePts!.Value, f.AwayPts!.Value);
        return st;
    }

    /// <summary>Any club's regular-season record in its own league (NABL or abroad).</summary>
    public TeamRecord RecordOf(Team t) =>
        League.Teams.Contains(t) ? Standings[t] : World.LeagueOf(t) is { } c ? TableOf(c)[t]
        : World.College?.Programs.Contains(t) == true ? CollegeRecord(t) : new TeamRecord { Team = t };

    /// <summary>Your league's standings (the NABL's, or your overseas league's).</summary>
    public Standings MyStandings => InCollege ? CollegeTable(MyConference!.Key) : HumanLeague is { } c ? TableOf(c) : Standings;
    /// <summary>
    /// CS2 (run 126): the record the board's win target is judged on. In college that's the regular season only (no conference
    /// tournament, no March Madness: those are judged by how far the program went); elsewhere it's <see cref="MyRecord"/>.
    /// </summary>
    public TeamRecord MyBoardRecord => InCollege ? CollegeRegularRecord(HumanTeam) : MyRecord;

    /// <summary>CS2: a program's regular-season record (<see cref="College.CollegeWorld.IsRegularSeason"/>).</summary>
    public TeamRecord CollegeRegularRecord(Team program)
    {
        var (w, l) = World.College!.RegularSeasonRecord(program.Id);
        return new TeamRecord { Team = program, Wins = w, Losses = l };
    }

    /// <summary>Your record: in college, every game (the conference table is <see cref="MyTable"/>).</summary>
    public TeamRecord MyRecord => InCollege ? CollegeRecord(HumanTeam) : MyStandings.Has(HumanTeam) ? MyStandings[HumanTeam] : new TeamRecord { Team = HumanTeam };
    /// <summary>The table you're ranked in: your NABL conference, or your whole overseas league.</summary>
    public List<TeamRecord> MyTable => InNabl ? Standings.Conference(HumanTeam.Conference) : MyStandings.All();
    public string MyTableName => InNabl ? HumanTeam.Conference.ToString() : HumanLeagueName;
    /// <summary>
    /// K20 (run 155): your place as Home and Standings show it. Before a college conference's first game every record is 0-0,
    /// so those pages order the conference by overall record (H81, H134); the inbox's place now reads the same order
    /// (Duke was "4th" in the inbox and 2nd on Standings on Dec 24).
    /// </summary>
    public int MyRank => MyShownTable.FindIndex(r => r.Team == HumanTeam) + 1;

    /// <summary>K20: <see cref="MyTable"/>, ordered by overall record before college conference play (as Home and Standings show it).</summary>
    public List<TeamRecord> MyShownTable
    {
        get
        {
            var t = MyTable;
            if (!InCollege || t.Count == 0 || t.Any(r => r.Games > 0)) return t;
            return t.Select(r => CollegeRecord(r.Team)).OrderByDescending(r => r.WinPct).ThenByDescending(r => r.Wins).ThenByDescending(r => r.PointDiff).ToList();
        }
    }

    /// <summary>Your club's games this season in every competition, in date order.</summary>
    public List<MyGame> MyGames()
    {
        var list = new List<MyGame>();
        var us = HumanTeam;
        if (InNabl)
        {
            foreach (var g in Schedule.Where(g => g.Home == us || g.Away == us))
            {
                Results.TryGetValue(g.Index, out var r);
                var (mine, theirs) = r == null ? (null as int?, null as int?) : r.Home.Team == us ? (r.Home.Points, r.Away.Points) : (r.Away.Points, r.Home.Points);
                list.Add(new MyGame(g.Date, g.Home == us ? g.Away : g.Home, g.Home == us, League.Name, "Regular season", mine, theirs, r) { Group = ScheduleFilter.Regular });
            }
            foreach (var se in PlayoffSeries.Where(s => s.Higher == us || s.Lower == us))
            foreach (var r in se.Games)
            {
                bool home = r.Home.Team == us;
                list.Add(new MyGame(r.Date ?? Today, home ? r.Away.Team : r.Home.Team, home, League.Name, se.Round,
                    home ? r.Home.Points : r.Away.Points, home ? r.Away.Points : r.Home.Points, r) { Group = ScheduleFilter.ProGroup(League.Name, se.Round, League.Name) });
            }
            // A promotion playoff (run 102) is in tier 2's fixtures.
            foreach (var (comp, f) in World.FixturesOf(us.Id))
            {
                bool home = f.HomeId == us.Id;
                if (World.FixtureTeam(home ? f.AwayId : f.HomeId) is not { } opp) continue;
                AbroadResults.TryGetValue(Key(comp, f), out var r);
                list.Add(new MyGame(f.Date, opp, home, comp.Profile.Name, f.Stage,
                    f.Played ? (home ? f.HomePts : f.AwayPts) : null, f.Played ? (home ? f.AwayPts : f.HomePts) : null, r)
                    { Group = ScheduleFilter.ProGroup(comp.Profile.Name, f.Stage, HumanLeagueName) });
            }
            return list.OrderBy(g => g.Date).ToList();
        }
        if (InCollege)
        {
            var cw = World.College!;
            foreach (var g in cw.GamesOf(us.Id))
            {
                bool home = g.HomeId == us.Id;
                var opp = cw.Program(home ? g.AwayId : g.HomeId);
                if (opp == null) continue;
                CollegeResults.TryGetValue(Key(g), out var r);
                // Short labels for the schedule: conference games carry the conference's short name, non-conference games none,
                // events their name (and the round for March Madness).
                string comp = g.Event ?? HumanLeagueName;
                string stage = g.Event == College.CollegeWorld.Madness ? College.CollegeWorld.RoundName(g.Round)
                    : g.Stage == "Conference" ? MyConference!.Short : "Regular season";
                list.Add(new MyGame(g.Date, opp, home, comp, stage,
                    g.Played ? (home ? g.HomePts : g.AwayPts) : null, g.Played ? (home ? g.AwayPts : g.HomePts) : null, r)
                {
                    OpponentRank = g.Played ? (home ? g.AwayRank : g.HomeRank) : cw.Rank(opp.Id),
                    OurRank = g.Played ? (home ? g.HomeRank : g.AwayRank) : cw.Rank(us.Id),
                    Group = ScheduleFilter.CollegeGroup(g.Event, g.Stage),
                    Rivalry = cw.RivalryOf(us.Id, opp.Id),
                    Neutral = g.Event != null,
                    StyleConference = cw.StyleConference(g),
                    NotCoached = g.Played && r == null && (g.Date <= Job.HiredOn || !Job.Employed),
                });
            }
            return list;
        }
        foreach (var (comp, f) in World.FixturesOf(us.Id))
        {
            bool home = f.HomeId == us.Id;
            var opp = World.FixtureTeam(home ? f.AwayId : f.HomeId);
            if (opp == null) continue;
            AbroadResults.TryGetValue(Key(comp, f), out var r);
            list.Add(new MyGame(f.Date, opp, home, comp.Profile.Name, f.Stage,
                f.Played ? (home ? f.HomePts : f.AwayPts) : null, f.Played ? (home ? f.AwayPts : f.HomePts) : null, r)
                { Group = ScheduleFilter.ProGroup(comp.Profile.Name, f.Stage, HumanLeagueName), NotCoached = f.Played && r == null && (f.Date <= Job.HiredOn || !Job.Employed) });
        }
        return list;
    }

    /// <summary>Your next unplayed game (today's included).</summary>
    public MyGame? NextMyGame => MyGames().FirstOrDefault(g => !g.Played && g.Date >= Today);

    /// <summary>Your players' stats this season: NABL box scores, or every competition's lines abroad.</summary>
    public Dictionary<int, PlayerSeasonStats> MyStats
    {
        get
        {
            if (InNabl) return Stats;
            var stats = new Dictionary<int, PlayerSeasonStats>();
            var mine = HumanTeam.Roster.Select(p => p.Id).ToHashSet();
            var sources = InCollege ? new[] { World.College!.Season.Stats } : World.Playing.Select(c => c.Season.Stats);
            foreach (var source in sources)
            foreach (var (id, l) in source)
            {
                if (!mine.Contains(id) || l.TeamId != HumanTeam.Id) continue;
                if (!stats.TryGetValue(id, out var s)) stats[id] = s = new PlayerSeasonStats { PlayerId = id, TeamId = l.TeamId, Season = League.Season };
                s.Games += l.G; s.Starts += l.Gs; s.Minutes += l.Min; s.Points += l.Pts; s.DReb += l.Reb; s.Assists += l.Ast; s.Steals += l.Stl; s.Blocks += l.Blk;
                s.FGM += l.Fgm; s.FGA += l.Fga; s.ThreePM += l.Tpm; s.ThreePA += l.Tpa;
            }
            return stats;
        }
    }

    /// <summary>Your club's season is over: no game left to play in any competition (abroad), or the NABL playoffs are done.</summary>
    public bool HumanSeasonOver =>
        InNabl || !Employed
        || (InCollege ? World.College!.Season.ChampionId != null
            : World.FixturesOf(HumanTeam.Id).All(x => x.fixture.Played)
              && !World.Playing.Any(c => c.Season.Series.Any(s => !s.Complete && (s.HighId == HumanTeam.Id || s.LowId == HumanTeam.Id)))
              && !AwaitingPromotionPlayoff);

    /// <summary>
    /// H80 (run 105): has this club played its last game of the season? A coach hired after that is hired for next season:
    /// his goal, his first season and the board's review all start with the coming one. Conservative in the top league's
    /// playoffs: only a club that missed them (and has no promotion playoff to play) counts as finished.
    /// </summary>
    public bool ClubSeasonOver(Team t)
    {
        if (Phase == SessionPhase.Offseason) return true;
        if (Phase != SessionPhase.Postseason) return false;
        if (World.College?.Program(t.Id) != null) return World.College.Season.ChampionId != null;
        if (UsaPyramid.PromotionSeriesOf(World, t.Id) is { Complete: false }) return false;
        if (League.Teams.Contains(t)) return !Postseason.Series.Any(s => s.Higher == t || s.Lower == t);
        return World.FixturesOf(t.Id).All(x => x.fixture.Played)
            && !World.Playing.Any(c => c.Season.Series.Any(s => !s.Complete && (s.HighId == t.Id || s.LowId == t.Id)));
    }

    /// <summary>
    /// Run 102: your tier-2 club won its division and waits for the top league's regular season to end, when its promotion
    /// playoff starts. Its season isn't over.
    /// </summary>
    public bool AwaitingPromotionPlayoff =>
        Employed && HumanLeague is { Key: UsaPyramid.Tier2 } c && c.Season.Stage != CompetitionSeason.RegularSeason
        && UsaPyramid.PromotionSeriesOf(World, HumanTeam.Id) == null && Phase is SessionPhase.Preseason or SessionPhase.RegularSeason
        && UsaPyramid.PlaysPromotion(World, League) && UsaPyramid.WonDivision(c, HumanTeam);

    /// <summary>The first day of a career year: a week before the NABL opens, or before your overseas league starts if earlier.</summary>
    public DateOnly CareerYearStart()
    {
        var day = OpeningNight.AddDays(-7);
        if (World.LeagueOf(HumanTeam) is { } c)
        {
            var start = new DateOnly(League.Season, c.Profile.Start.Month, c.Profile.Start.Day).AddDays(-3);
            if (start < day) day = start;
        }
        return day;
    }

    private (WorldCompetition comp, Fixture fixture)? _liveWorld;

    /// <summary>Play or start today's game for a club abroad. Returns the live game when asked for one.</summary>
    private College.CollegeGame? _liveCollege;
    /// <summary>H106 (run 112): whether today's live game is still to be recorded and will count in <see cref="MyRecord"/>
    /// (a league or college game; not a continental cup or a playoff game). False once FinishLiveDay has recorded it.</summary>
    public bool LiveGameCountsInRecord => _liveCollege != null || _liveScheduled != null
        || (_liveWorld is { } w && w.comp.Profile.Kind == Hardwood.Core.World.CompetitionKind.Domestic
            && w.fixture.Stage == Hardwood.Core.World.CompetitionSeason.RegularSeason);

    private GameSimulator.LiveGame? BeginAbroadGame(bool live, List<GameResult> played)
    {
        _liveWorld = null; _liveCollege = null;
        if (Employed && InCollege)
        {
            // College (C2): your program's games use the full engine with college rules.
            var cw = World.College!;
            if (cw.GameOn(HumanTeam.Id, Today) is not { } cg) return null;
            var (h, a) = (cw.Program(cg.HomeId)!, cw.Program(cg.AwayId)!);
            if (live)
            {
                _liveCollege = cg;
                return _sim.StartLive(h, a, cw.GameRules(cg), _rng.NextULong(), Today);
            }
            var cr = _sim.Simulate(h, a, cw.GameRules(cg), _rng.NextULong(), keepLog: true, date: Today);
            RecordCollege(cg, cr);
            played.Add(cr);
            return null;
        }
        // A top-league club plays only a promotion playoff here (run 102).
        if (!Employed || World.GameOn(HumanTeam.Id, Today) is not { } g) return null;
        var home = World.FixtureTeam(g.fixture.HomeId)!; var away = World.FixtureTeam(g.fixture.AwayId)!;
        if (live)
        {
            _liveWorld = g;
            return _sim.StartLive(home, away, g.comp.Profile.Rules, _rng.NextULong(), Today);
        }
        var r = _sim.Simulate(home, away, g.comp.Profile.Rules, _rng.NextULong(), keepLog: true, date: Today);
        RecordAbroad(g.comp, g.fixture, r);
        played.Add(r);
        return null;
    }

    private void RecordCollege(College.CollegeGame g, GameResult r)
    {
        World.College!.Record(g, r);
        CollegeResults[Key(g)] = r;
        KeepNewest(CollegeResults);
        ReportGame(r);
        ReportRivalry(g);
    }

    /// <summary>C5 part 1 (run 94): after your program plays a rival, one inbox line with the result and the series. No random numbers.</summary>
    private void ReportRivalry(College.CollegeGame g)
    {
        var cw = World.College!;
        int us = HumanTeam.Id;
        if (!Employed || !InCollege || (g.HomeId != us && g.AwayId != us) || g.WinnerId is not { } w) return;
        int them = g.HomeId == us ? g.AwayId : g.HomeId;
        if (cw.RivalryOf(us, them) is not { } rv || cw.Program(them) is not { } opp) return;
        bool won = w == us;
        int ours = g.HomeId == us ? g.HomePts!.Value : g.AwayPts!.Value, theirs = g.HomeId == us ? g.AwayPts!.Value : g.HomePts!.Value;
        string title = rv.Name != null ? $"{rv.Name}: {(won ? "a win" : "a loss")} against {opp.City}, {ours}-{theirs}"
            : $"{(won ? "Beat" : "Lost to")} {(rv.InState ? "in-state rival" : "rival")} {opp.City}, {ours}-{theirs}";
        string body = $"{(won ? "You won" : "You lost")} {rv.Label} {(g.HomeId == us ? "at home" : g.Event != null ? "on a neutral court" : "on the road")}. "
            + (cw.SeriesLine(us, them) is { } line ? line + "." : "");
        Post(title, body, "League");
    }

    private void RecordAbroad(WorldCompetition comp, Fixture f, GameResult r)
    {
        World.Record(comp, f, r);
        AbroadResults[Key(comp, f)] = r;
        KeepNewest(AbroadResults);
        ReportGame(r);
    }

    /// <summary>Older saves paid staff abroad a sixth of NABL wages whatever the league; put them on the league's scale.</summary>
    private void FixStaffPay()
    {
        if (!Employed || !Abroad || HumanTeam.Staff.Count == 0) return;
        double limit = Budgets.StaffScale(MySalaryScale) * 1.5;
        if (HumanTeam.Staff.Any(st => st.Salary > Generation.StaffGenerator.SalaryFor(st) * limit + 1000)) Budgets.ScaleStaff(HumanTeam, MySalaryScale);
    }

    /// <summary>A club abroad without staff (the world doesn't give them any) gets a full team when you take over.</summary>
    private void EnsureStaff(Team t)
    {
        if (t.Staff.Count > 0) return;
        var gen = new Generation.StaffGenerator(new Rng(_rng.NextULong()));
        int quality = (int)Math.Round(Math.Clamp(_rng.NextGaussian(9, 2), 5, 14));
        foreach (var role in Enum.GetValues<StaffRole>())
        {
            var st = gen.Generate(League, role, Today, quality);
            st.TeamId = t.Id;
            t.Staff.Add(st);
        }
        Budgets.ScaleStaff(t, MySalaryScale);
    }

    /// <summary>Overseas clubs change coaches too; while you're out of work, their openings are yours to apply for.</summary>
    private void WorldCoachingChanges()
    {
        foreach (var comp in World.Competitions.Where(c => c.Profile.Kind == CompetitionKind.Domestic))
        {
            var table = TableOf(comp);
            foreach (var t in Board.AiSeasonChanges(LeagueContext.Of(comp), t => table[t].Wins, _rng, ControlledTeamId, League.Season))
                CoachLeaves(t, "parted ways with");
        }
    }
}
